using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Guardrail.CrashAi.Documents;

/// <summary>Turns attached files and web pages into plain text for <see cref="DocumentFacts"/>. Offline except for web links.</summary>
public static class DocumentReader
{
    /// <summary>File types that can be read as text here. (Pictures of documents are read by the desktop app's OCR.)</summary>
    public static readonly string[] TextExtensions = [".pdf", ".docx", ".txt", ".text", ".md", ".rtf", ".htm", ".html", ".mht", ".eml", ".csv", ".json", ".xml"];
    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".gif", ".webp"];

    public static bool CanRead(string path) => TextExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
    public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Reads a document file as plain text. Throws with a readable message when it can't.</summary>
    public static string ReadFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => ReadPdf(path),
            ".docx" => ReadDocx(path),
            ".rtf" => RtfToText(File.ReadAllText(path)),
            ".htm" or ".html" or ".mht" => HtmlToText(File.ReadAllText(path)).Text,
            _ => File.ReadAllText(path),
        };
    }

    private static string ReadPdf(string path)
    {
        var sb = new StringBuilder();
        using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
        foreach (var page in doc.GetPages())
        {
            // Rebuild lines from words so form-style reports keep "Label: value" together.
            var words = page.GetWords().OrderByDescending(w => Math.Round(w.BoundingBox.Bottom / 3)).ThenBy(w => w.BoundingBox.Left).ToList();
            double? lastY = null;
            foreach (var w in words)
            {
                double y = Math.Round(w.BoundingBox.Bottom / 3);
                if (lastY != null && y != lastY) sb.Append('\n');
                else if (lastY != null) sb.Append(' ');
                sb.Append(w.Text);
                lastY = y;
            }
            sb.Append("\n\n");
        }
        string text = sb.ToString();
        if (text.Trim().Length < 20)
            throw new InvalidOperationException("This PDF has no readable text (it may be a scan). Save the pages as pictures and attach those instead, so they can be read with text recognition.");
        return text;
    }

    private static string ReadDocx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidOperationException("This doesn't look like a Word document.");
        using var stream = entry.Open();
        var xml = XDocument.Load(stream);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var sb = new StringBuilder();
        foreach (var p in xml.Descendants(w + "p"))
        {
            foreach (var node in p.Descendants())
            {
                if (node.Name == w + "t") sb.Append(node.Value);
                else if (node.Name == w + "tab") sb.Append('\t');
                else if (node.Name == w + "br") sb.Append('\n');
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Very small RTF reader: drops control words and groups, keeps the text.</summary>
    public static string RtfToText(string rtf)
    {
        if (!rtf.TrimStart().StartsWith("{\\rtf")) return rtf;
        var sb = new StringBuilder();
        int depth = 0; var skip = new Stack<bool>(); bool skipping = false;
        for (int i = 0; i < rtf.Length; i++)
        {
            char c = rtf[i];
            if (c == '{') { skip.Push(skipping); depth++; continue; }
            if (c == '}') { skipping = skip.Count > 0 && skip.Pop(); depth--; continue; }
            if (c == '\\')
            {
                if (i + 1 < rtf.Length && (rtf[i + 1] is '\\' or '{' or '}')) { if (!skipping) sb.Append(rtf[i + 1]); i++; continue; }
                if (i + 1 < rtf.Length && rtf[i + 1] == '\'' && i + 3 < rtf.Length)
                {
                    if (!skipping && int.TryParse(rtf.AsSpan(i + 2, 2), System.Globalization.NumberStyles.HexNumber, null, out int code)) sb.Append((char)code);
                    i += 3; continue;
                }
                int j = i + 1;
                while (j < rtf.Length && char.IsLetter(rtf[j])) j++;
                string word = rtf[(i + 1)..j];
                while (j < rtf.Length && (char.IsDigit(rtf[j]) || rtf[j] == '-')) j++;
                if (j < rtf.Length && rtf[j] == ' ') j++;
                if (word is "par" or "line" or "row") { if (!skipping) sb.Append('\n'); }
                else if (word == "tab" || word == "cell") { if (!skipping) sb.Append('\t'); }
                else if (word is "fonttbl" or "colortbl" or "stylesheet" or "info" or "pict" or "header" or "footer" or "object") skipping = true;
                else if (word.Length == 0 && j < rtf.Length && rtf[j] == '*') skipping = true;
                i = j - 1;
                continue;
            }
            if (c is '\r' or '\n') continue;
            if (!skipping) sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>HTML to readable text: prefers the article body; keeps paragraphs and headings.</summary>
    public static (string Title, string Text) HtmlToText(string html)
    {
        string title = "";
        var og = Regex.Match(html, @"<meta[^>]+property=[""']og:title[""'][^>]+content=[""']([^""']+)", RegexOptions.IgnoreCase);
        var tm = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (og.Success) title = WebUtility.HtmlDecode(og.Groups[1].Value).Trim();
        else if (tm.Success) title = WebUtility.HtmlDecode(Regex.Replace(tm.Groups[1].Value, @"\s+", " ")).Trim();

        string body = Regex.Replace(html, @"<(script|style|noscript|svg|nav|footer|header|form|aside|iframe)\b.*?</\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        body = Regex.Replace(body, @"<!--.*?-->", " ", RegexOptions.Singleline);
        var article = Regex.Match(body, @"<article\b.*?</article>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (article.Success && article.Length > 500) body = article.Value;
        // Prefer paragraphs when the page has enough of them (skips menus and link lists).
        var paras = Regex.Matches(body, @"<(p|h1|h2|h3|li|td)\b[^>]*>(.*?)</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(m => Clean(m.Groups[2].Value)).Where(p => p.Length > 0).ToList();
        string text = paras.Sum(p => p.Length) > 300 ? string.Join("\n", paras) : Clean(Regex.Replace(body, @"<(br|/p|/div|/li|/h\d|/tr)\b[^>]*>", "\n", RegexOptions.IgnoreCase));
        return (title, text);

        static string Clean(string s)
        {
            s = Regex.Replace(s, @"<[^>]+>", " ");
            s = WebUtility.HtmlDecode(s);
            s = Regex.Replace(s, @"[ \t ]+", " ");
            s = Regex.Replace(s, @"\s*\n\s*", "\n");
            return s.Trim();
        }
    }

    /// <summary>Downloads a web page (a news story, a police press release) and returns its title and text.</summary>
    public static async Task<(string Title, string Text)> FetchAsync(string url, CancellationToken cancel = default)
    {
        using var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Guardrail-EHS");
        using var resp = await http.GetAsync(url, cancel);
        resp.EnsureSuccessStatusCode();
        string type = resp.Content.Headers.ContentType?.MediaType ?? "";
        if (type.Contains("pdf"))
        {
            string tmp = Path.Combine(Path.GetTempPath(), $"guardrail-doc-{Guid.NewGuid():N}.pdf");
            try
            {
                await File.WriteAllBytesAsync(tmp, await resp.Content.ReadAsByteArrayAsync(cancel), cancel);
                return (Path.GetFileName(new Uri(url).LocalPath), ReadPdf(tmp));
            }
            finally { try { File.Delete(tmp); } catch { } }
        }
        string html = await resp.Content.ReadAsStringAsync(cancel);
        if (!type.Contains("html") && !html.TrimStart().StartsWith('<')) return (url, html);
        var (title, text) = HtmlToText(html);
        if (text.Length < 80) throw new InvalidOperationException("The page didn't contain readable article text. Save it as a PDF or copy the text into a .txt file and attach that instead.");
        return (title.Length > 0 ? title : url, text);
    }
}
