using System.IO;
using Guardrail.CrashAi.Documents;
using Guardrail.Data;

namespace Guardrail.Services;

/// <summary>
/// Attaches police reports, news stories and other documents to a crash review. Files are read on this PC
/// (PDF, Word, text, web pages saved to disk, and pictures or scans through Windows' built-in text recognition);
/// only web links are fetched from the internet.
/// </summary>
public static class CrashDocuments
{
    public const int MaxTextLength = 400_000;

    /// <summary>File-picker filter for attachable documents.</summary>
    public const string Filter =
        "Documents and pictures|*.pdf;*.docx;*.txt;*.rtf;*.htm;*.html;*.md;*.eml;*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif;*.webp|" +
        "PDF|*.pdf|Word|*.docx|Text|*.txt;*.rtf;*.md|Web page|*.htm;*.html|Pictures and scans|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif;*.webp|All files|*.*";

    public static bool CanAttach(string path) => DocumentReader.CanRead(path) || DocumentReader.IsImage(path);

    /// <summary>Reads a file and attaches it. Returns an error message, or null when it worked.</summary>
    public static async Task<string?> AddFileAsync(VideoReview review, string path)
    {
        try
        {
            if (!File.Exists(path)) return $"{Path.GetFileName(path)} wasn't found.";
            if (!CanAttach(path)) return $"{Path.GetFileName(path)} isn't a document GuardHawk can read (PDF, Word, text, web page or a picture).";
            string text; bool ocr = false;
            if (DocumentReader.IsImage(path)) { text = await Ocr.ReadImageAsync(path); ocr = true; }
            else
            {
                try { text = await Task.Run(() => DocumentReader.ReadFile(path)); }
                catch (InvalidOperationException) when (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    // A scanned PDF has no text layer: render the pages and read them with text recognition.
                    text = await Ocr.ReadPdfAsync(path); ocr = true;
                }
            }
            if (string.IsNullOrWhiteSpace(text)) return $"No text could be read from {Path.GetFileName(path)}.";
            Attach(review, new ReviewDocument { Title = Path.GetFileName(path), Source = "file", Location = path, Text = Cap(text), Ocr = ocr });
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException
                                       or System.Xml.XmlException or NotSupportedException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            return $"Couldn't read {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    /// <summary>Downloads a web page (news story, police press release) and attaches its text.</summary>
    public static async Task<string?> AddLinkAsync(VideoReview review, string url)
    {
        try
        {
            var (title, text) = await DocumentReader.FetchAsync(url.Trim());
            Attach(review, new ReviewDocument { Title = title, Source = "link", Location = url.Trim(), Text = Cap(text) });
            return null;
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException or IOException)
        {
            return $"Couldn't read that page: {ex.Message}";
        }
    }

    /// <summary>Attaches text typed or pasted in (a narrative from a report, a witness statement).</summary>
    public static void AddText(VideoReview review, string title, string text) =>
        Attach(review, new ReviewDocument { Title = title.Trim().Length > 0 ? title.Trim() : "Pasted text", Source = "text", Text = Cap(text) });

    public static void Remove(VideoReview review, ReviewDocument doc)
    {
        review.Documents.Remove(doc);
        DataStore.CrashReviews.Update(review);
    }

    public static void SetMyParty(VideoReview review, string key)
    {
        review.MyDocParty = key;
        DataStore.CrashReviews.Update(review);
    }

    /// <summary>Changes what kind of camera filmed the video and analyzes it again.</summary>
    public static void SetCameraHint(VideoReview review, string hint)
    {
        review.CameraHint = hint;
        DataStore.CrashReviews.Update(review);
        CrashReviewService.Retry(review);
    }

    private static void Attach(VideoReview review, ReviewDocument doc)
    {
        review.Documents.Add(doc);
        DataStore.CrashReviews.Update(review);
    }

    private static string Cap(string text) => text.Length > MaxTextLength ? text[..MaxTextLength] : text;
}

/// <summary>Text recognition with the engine built into Windows 10 and 11 (works offline).</summary>
public static class Ocr
{
    private static global::Windows.Media.Ocr.OcrEngine Engine() =>
        global::Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()
        ?? global::Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new global::Windows.Globalization.Language("en-US"))
        ?? throw new InvalidOperationException("Windows text recognition isn't available on this PC. Add an OCR language in Windows Settings → Time & language → Language.");

    public static async Task<string> ReadImageAsync(string path)
    {
        var file = await global::Windows.Storage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        using var stream = await file.OpenAsync(global::Windows.Storage.FileAccessMode.Read);
        return await ReadStreamAsync(stream);
    }

    /// <summary>Renders each page of a scanned PDF and reads it (up to 30 pages).</summary>
    public static async Task<string> ReadPdfAsync(string path)
    {
        var file = await global::Windows.Storage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        var pdf = await global::Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
        var sb = new System.Text.StringBuilder();
        for (uint i = 0; i < Math.Min(pdf.PageCount, 30u); i++)
        {
            using var page = pdf.GetPage(i);
            using var stream = new global::Windows.Storage.Streams.InMemoryRandomAccessStream();
            // About 200 dpi: enough for small print on forms.
            var options = new global::Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = (uint)Math.Min(4000, page.Size.Width * 200 / 72) };
            await page.RenderToStreamAsync(stream, options);
            stream.Seek(0);
            sb.AppendLine(await ReadStreamAsync(stream));
        }
        return sb.ToString();
    }

    private static async Task<string> ReadStreamAsync(global::Windows.Storage.Streams.IRandomAccessStream stream)
    {
        var decoder = await global::Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        uint max = global::Windows.Media.Ocr.OcrEngine.MaxImageDimension;
        var transform = new global::Windows.Graphics.Imaging.BitmapTransform();
        if (decoder.PixelWidth > max || decoder.PixelHeight > max)
        {
            double s = Math.Min((double)max / decoder.PixelWidth, (double)max / decoder.PixelHeight);
            transform.ScaledWidth = (uint)(decoder.PixelWidth * s);
            transform.ScaledHeight = (uint)(decoder.PixelHeight * s);
        }
        using var bitmap = await decoder.GetSoftwareBitmapAsync(global::Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, global::Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
            transform, global::Windows.Graphics.Imaging.ExifOrientationMode.RespectExifOrientation, global::Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
        var result = await Engine().RecognizeAsync(bitmap);
        return string.Join("\n", result.Lines.Select(l => l.Text));
    }
}
