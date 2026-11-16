using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace Guardrail.Server;

/// <summary>
/// Where uploaded files (documents, photos) live: a folder (the data folder, or a network share every server can
/// reach) or S3-compatible object storage (AWS S3, MinIO, Cloudflare R2, Backblaze B2, Wasabi…). Keys look like
/// "&lt;org id&gt;/&lt;file name&gt;".
/// </summary>
public abstract class BlobStore
{
    public abstract Task Put(string key, string sourceFile, CancellationToken ct);
    /// <summary>Null when there's no such file.</summary>
    public abstract Task<Stream?> Get(string key, CancellationToken ct);
    public abstract Task Delete(string key);
    public abstract Task DeletePrefix(string prefix);
    /// <summary>Can it be reached right now? (for /readyz)</summary>
    public abstract Task<bool> Check();
    public abstract string Describe { get; }
    /// <summary>Free space where files are kept, or -1 when unknown (object storage).</summary>
    public virtual long FreeBytes => -1;

    public static BlobStore Create(ServerConfig config) => config.Storage == "s3" ? new S3BlobStore(config) : new FolderBlobStore(config.FilesFolder!);
}

public sealed class FolderBlobStore(string root) : BlobStore
{
    /// <summary>"3/library/abc/Policy.pdf" → root/3/&lt;sha256 of the rest&gt;: safe on every file system.</summary>
    private string PathFor(string key)
    {
        int slash = key.IndexOf('/');
        string org = key[..slash], name = key[(slash + 1)..];
        return Path.Combine(root, org, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))).ToLowerInvariant());
    }

    public override Task Put(string key, string sourceFile, CancellationToken ct)
    {
        string path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N")[..6] + ".upload";
        File.Copy(sourceFile, temp, overwrite: true);
        File.Move(temp, path, overwrite: true);
        return Task.CompletedTask;
    }

    public override Task<Stream?> Get(string key, CancellationToken ct)
    {
        string path = PathFor(key);
        return Task.FromResult<Stream?>(File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, useAsync: true) : null);
    }

    public override Task Delete(string key)
    {
        try { File.Delete(PathFor(key)); } catch (IOException) { }
        return Task.CompletedTask;
    }

    public override Task DeletePrefix(string prefix)
    {
        // Prefixes are always "<org>/": that's one folder.
        string folder = Path.Combine(root, prefix.TrimEnd('/'));
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); } catch (IOException) { }
        return Task.CompletedTask;
    }

    public override Task<bool> Check()
    {
        try { Directory.CreateDirectory(root); return Task.FromResult(true); }
        catch { return Task.FromResult(false); }
    }

    public override string Describe => root;

    public override long FreeBytes
    {
        get
        {
            try { Directory.CreateDirectory(root); return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root)) ?? "/").AvailableFreeSpace; }
            catch { return -1; }
        }
    }
}

public sealed class S3BlobStore : BlobStore
{
    private readonly AmazonS3Client _s3;
    private readonly string _bucket;
    private readonly string _describe;

    public S3BlobStore(ServerConfig config)
    {
        var s3Config = new AmazonS3Config { ForcePathStyle = config.S3PathStyle };
        if (!string.IsNullOrWhiteSpace(config.S3Endpoint))
        {
            s3Config.ServiceURL = config.S3Endpoint;
            s3Config.AuthenticationRegion = config.S3Region ?? "us-east-1";
        }
        else s3Config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(config.S3Region ?? "us-east-1");
        _s3 = string.IsNullOrEmpty(config.S3AccessKey)
            ? new AmazonS3Client(s3Config)
            : new AmazonS3Client(new BasicAWSCredentials(config.S3AccessKey, config.S3SecretKey), s3Config);
        _bucket = config.S3Bucket!;
        _describe = config.StorageLabel;
    }

    public override async Task Put(string key, string sourceFile, CancellationToken ct) =>
        await _s3.PutObjectAsync(new PutObjectRequest { BucketName = _bucket, Key = key, FilePath = sourceFile, ContentType = "application/octet-stream" }, ct);

    public override async Task<Stream?> Get(string key, CancellationToken ct)
    {
        try
        {
            var res = await _s3.GetObjectAsync(_bucket, key, ct);
            return res.ResponseStream;
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound) { return null; }
    }

    public override async Task Delete(string key)
    {
        try { await _s3.DeleteObjectAsync(_bucket, key); }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound) { }
    }

    public override async Task DeletePrefix(string prefix)
    {
        string? token = null;
        do
        {
            var page = await _s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _bucket, Prefix = prefix, ContinuationToken = token });
            if (page.S3Objects is { Count: > 0 })
                await _s3.DeleteObjectsAsync(new DeleteObjectsRequest { BucketName = _bucket, Objects = page.S3Objects.Select(o => new KeyVersion { Key = o.Key }).ToList() });
            token = page.IsTruncated == true ? page.NextContinuationToken : null;
        } while (token != null);
    }

    public override async Task<bool> Check()
    {
        try
        {
            await _s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _bucket, MaxKeys = 1 });
            return true;
        }
        catch { return false; }
    }

    /// <summary>Creates the bucket on first start if it's missing (handy with MinIO).</summary>
    public async Task EnsureBucket(ILogger log)
    {
        try
        {
            await _s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _bucket, MaxKeys = 1 });
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            log.LogInformation("Creating bucket {Bucket}", _bucket);
            await _s3.PutBucketAsync(new PutBucketRequest { BucketName = _bucket });
        }
    }

    public override string Describe => _describe;
}
