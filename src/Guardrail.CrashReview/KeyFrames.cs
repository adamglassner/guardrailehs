using OpenCvSharp;

namespace Guardrail.CrashAi;

/// <summary>Saves the key moments as JPEGs with the involved road users outlined.</summary>
public static class KeyFrames
{
    private static readonly Scalar[] Colors = [new(60, 60, 235), new(30, 150, 250)]; // red, amber (BGR)

    public static List<KeyFrame> Write(Extraction ex, KeyPlan plan, CrashReport report, string folder)
    {
        var result = new List<KeyFrame>();
        foreach (var f in Directory.Exists(folder) ? Directory.GetFiles(folder, "key*.jpg") : [])
            try { File.Delete(f); } catch (IOException) { }

        int n = 0;
        foreach (var (t, caption) in plan.Moments)
        {
            var sample = ex.Samples.MinBy(s => Math.Abs(s.T - t));
            if (sample is null) continue;
            using var img = Cv2.ImDecode(sample.Jpeg, ImreadModes.Color);
            if (img.Empty()) continue;
            int w = img.Cols, h = img.Rows;
            for (int i = 0; i < plan.Highlight.Count; i++)
            {
                var (track, label) = plan.Highlight[i];
                var near = track.History.Where(kv => Math.Abs(kv.Key - sample.T) < 0.11).ToList();
                if (near.Count == 0) continue;
                var b = near.MinBy(kv => Math.Abs(kv.Key - sample.T)).Value;
                var r = new Rect((int)(b.X1 * w), (int)(b.Y1 * h), Math.Max(1, (int)(b.W * w)), Math.Max(1, (int)(b.H * h)));
                var color = Colors[i % Colors.Length];
                Cv2.Rectangle(img, r, color, 2, LineTypes.AntiAlias);
                var size = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, 0.45, 1, out int baseLine);
                int ty = Math.Max(size.Height + 4, r.Y - 4);
                Cv2.Rectangle(img, new Rect(r.X, ty - size.Height - 4, size.Width + 8, size.Height + baseLine + 4), color, -1);
                Cv2.PutText(img, label, new Point(r.X + 4, ty - 2), HersheyFonts.HersheySimplex, 0.45, Scalar.White, 1, LineTypes.AntiAlias);
            }
            string file = $"key{++n}.jpg";
            Cv2.ImWrite(Path.Combine(folder, file), img, new ImageEncodingParam(ImwriteFlags.JpegQuality, 88));
            result.Add(new KeyFrame(Math.Round(sample.T, 2), file, caption));
        }
        return result;
    }
}
