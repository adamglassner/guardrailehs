using System.Runtime.InteropServices;
using OpenCvSharp;

namespace Guardrail.CrashAi;

/// <summary>Scene reading helpers: which part of the frame shows the road, lane paint, paint colours, signal state.</summary>
public static class Scene
{
    /// <summary>Copies a Mat's pixels into a byte array (row-major, interleaved channels).</summary>
    internal static byte[] Bytes(Mat m)
    {
        using var c = m.IsContinuous() ? null : m.Clone();
        var src = c ?? m;
        var data = new byte[src.Rows * src.Cols * src.Channels()];
        if (data.Length > 0) Marshal.Copy(src.Data, data, 0, data.Length);
        return data;
    }

    internal static int[] Ints(Mat m)
    {
        using var c = m.IsContinuous() ? null : m.Clone();
        var src = c ?? m;
        var data = new int[src.Rows * src.Cols * src.Channels()];
        if (data.Length > 0) Marshal.Copy(src.Data, data, 0, data.Length);
        return data;
    }

    /// <summary>
    /// Picks the road-facing view: trims black borders and, for split-screen recordings
    /// (cab camera beside or above the road camera), the panel with the most traffic in it.
    /// </summary>
    public static Rect FindView(IReadOnlyList<Mat> probes, Detector detector)
    {
        int W = probes[0].Cols, H = probes[0].Rows;
        var grays = probes.Select(p => { var g = new Mat(); Cv2.CvtColor(p, g, ColorConversionCodes.BGR2GRAY); return g; }).ToList();
        try
        {
            // Brightest value seen at each pixel across the probes: black bars stay dark.
            using var mx = grays[0].Clone();
            foreach (var g in grays.Skip(1)) Cv2.Max(mx, g, mx);
            var mxb = Bytes(mx);
            var whole = TrimDark(new Rect(0, 0, W, H), mxb, W);

            // Mean absolute horizontal / vertical gradient per column / row inside the trimmed view.
            var gx = new double[whole.Width]; var gy = new double[whole.Height];
            foreach (var g in grays)
            {
                var px = Bytes(g);
                for (int y = whole.Y; y < whole.Bottom; y++)
                    for (int x = whole.X + 1; x < whole.Right; x++)
                    {
                        int d = Math.Abs(px[y * W + x] - px[y * W + x - 1]);
                        gx[x - whole.X] += d;
                        if (y > whole.Y) gy[y - whole.Y] += Math.Abs(px[y * W + x] - px[(y - 1) * W + x]);
                    }
            }
            var cands = new List<Rect>();
            if (Seam(gx) is int sx)
            {
                cands.Add(new Rect(whole.X, whole.Y, sx, whole.Height));
                cands.Add(new Rect(whole.X + sx + 1, whole.Y, whole.Width - sx - 1, whole.Height));
            }
            if (Seam(gy) is int sy)
            {
                cands.Add(new Rect(whole.X, whole.Y, whole.Width, sy));
                cands.Add(new Rect(whole.X, whole.Y + sy + 1, whole.Width, whole.Height - sy - 1));
            }
            if (cands.Count == 0) return whole;

            Rect best = whole; double bestScore = double.MinValue;
            foreach (var raw in cands)
            {
                var c = TrimDark(raw, mxb, W);
                if (c.Width < 64 || c.Height < 64) continue;
                double score = 0;
                foreach (var p in probes)
                {
                    using var crop = new Mat(p, c);
                    foreach (var d in detector.Detect(crop))
                    {
                        if (d.Kind is Kind.Vehicle or Kind.TwoWheeler or Kind.TrafficLight or Kind.StopSign) score += 1;
                        else if (d.Kind == Kind.Person && d.Box.H > 0.5) score -= 0.5; // the cab camera sees the driver up close
                    }
                }
                if (score > bestScore) { bestScore = score; best = c; }
            }
            return best;
        }
        finally { foreach (var g in grays) g.Dispose(); }
    }

    private static int? Seam(double[] g)
    {
        int lo = (int)(g.Length * 0.4), hi = (int)(g.Length * 0.6);
        if (hi <= lo) return null;
        double baseLevel = CameraMotion.Median(g) + 1e-3;
        int best = lo;
        for (int i = lo; i < hi; i++) if (g[i] > g[best]) best = i;
        return g[best] > 4 * baseLevel ? best : null;
    }

    private static Rect TrimDark(Rect r, byte[] mx, int stride)
    {
        int top = -1, bottom = -1, left = -1, right = -1;
        for (int y = r.Y; y < r.Bottom; y++)
            for (int x = r.X; x < r.Right; x++)
                if (mx[y * stride + x] > 40)
                {
                    if (top < 0) top = y;
                    bottom = y;
                    if (left < 0 || x < left) left = x;
                    if (x > right) right = x;
                }
        if (top < 0 || bottom - top < 10 || right - left < 10) return r;
        return new Rect(left, top, right - left + 1, bottom - top + 1);
    }

    /// <summary>Where a yellow centre line meets the bottom of the road view (0 = left edge, 1 = right edge), or null.</summary>
    public static double? YellowLine(Mat img, IEnumerable<Box> movers)
    {
        int h = img.Rows, w = img.Cols, top = (int)(h * 0.58), bot = (int)(h * 0.90), bandH = bot - top;
        if (bandH < 8) return null;
        using var band = new Mat(img, new Rect(0, top, w, bandH));
        using var hsv = new Mat();
        Cv2.CvtColor(band, hsv, ColorConversionCodes.BGR2HSV);
        using var mask = new Mat();
        Cv2.InRange(hsv, new Scalar(12, 111, 121), new Scalar(34, 255, 255), mask);
        foreach (var b in movers) // ignore yellow paint on vehicles
        {
            var r = CameraMotion.Clip(new Rect((int)(b.X1 * w), (int)(b.Y1 * h) - top, (int)(b.W * w), (int)(b.H * h)), w, bandH);
            if (r.Width > 0 && r.Height > 0) using (var sub = new Mat(mask, r)) sub.SetTo(Scalar.All(0));
        }
        using (var k = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(2, 2)))
            Cv2.MorphologyEx(mask, mask, MorphTypes.Open, k);
        using var labels = new Mat(); using var stats = new Mat(); using var centroids = new Mat();
        int n = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids, PixelConnectivity.Connectivity8, MatType.CV_32S);
        if (n <= 1) return null;
        var st = Ints(stats); var lab = Ints(labels);
        var pts = new List<(int X, int Y)>[n];
        for (int y = 0; y < bandH; y++)
            for (int x = 0; x < w; x++)
            {
                int l = lab[y * w + x];
                if (l > 0) (pts[l] ??= []).Add((x, y));
            }
        double? best = null; double bestScore = 0;
        for (int i = 1; i < n; i++)
        {
            int bx = st[i * 5], by = st[i * 5 + 1], bh = st[i * 5 + 3], area = st[i * 5 + 4];
            if (area < Math.Max(8, w * h * 0.00003) || bh < bandH * 0.07 || pts[i] is null) continue;
            // Principal direction of the blob; keep thin, line-like, not-horizontal blobs.
            double mxv = pts[i].Average(p => p.X), myv = pts[i].Average(p => p.Y);
            double sxx = 0, syy = 0, sxy = 0;
            foreach (var p in pts[i]) { double dx = p.X - mxv, dy = p.Y - myv; sxx += dx * dx; syy += dy * dy; sxy += dx * dy; }
            double theta = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
            double vx = Math.Cos(theta), vy = Math.Sin(theta);
            if (Math.Abs(vy) < 0.25) continue;
            var dist = pts[i].Select(p => Math.Abs((p.X - mxv) * vy - (p.Y - myv) * vx)).OrderBy(d => d).ToArray();
            if (dist[(int)(dist.Length * 0.9)] > Math.Max(3.0, 0.012 * w)) continue;
            double xb = mxv + (bandH - 1 - myv) * vx / vy;
            double score = area * (1 + (double)(by + bh) / bandH);
            if (score > bestScore) { bestScore = score; best = xb / w; }
        }
        return best is null ? null : Math.Clamp(best.Value, -0.5, 1.5);
    }

    /// <summary>Rough paint colour of a vehicle ("white", "red", …) from the middle of its box.</summary>
    public static string? VehicleColor(Mat img, Box b)
    {
        int h = img.Rows, w = img.Cols;
        double x1 = b.X1 * w, x2 = b.X2 * w, y1 = b.Y1 * h, y2 = b.Y2 * h;
        var r = CameraMotion.Clip(new Rect((int)(x1 + (x2 - x1) * 0.25), (int)(y1 + (y2 - y1) * 0.3),
            (int)((x2 - x1) * 0.5), (int)((y2 - y1) * 0.45)), w, h);
        if (r.Width < 2 || r.Height < 2) return null;
        var (hh, s, v) = MedianHsv(img, r);
        if (s < 45) return v > 175 ? "white" : v > 120 ? "silver" : v > 70 ? "gray" : "black";
        if (v < 50) return "black";
        if (hh < 8 || hh > 170) return "red";
        if (hh < 22) return "orange";
        if (hh < 34) return "yellow";
        if (hh < 85) return "green";
        if (hh < 130) return "blue";
        return "purple";
    }

    /// <summary>Which lamp of a traffic light is lit: "red", "yellow", "green" or null.</summary>
    public static string? LightColor(Mat img, Box b)
    {
        int h = img.Rows, w = img.Cols;
        var r = CameraMotion.Clip(new Rect((int)(b.X1 * w), (int)(b.Y1 * h), (int)(b.W * w), (int)(b.H * h)), w, h);
        if (r.Width < 2 || r.Height < 6) return null;
        using var roi = new Mat(img, r);
        using var hsv = new Mat();
        Cv2.CvtColor(roi, hsv, ColorConversionCodes.BGR2HSV);
        var px = Bytes(hsv);
        int red = 0, yel = 0, grn = 0, bright = 0;
        for (int i = 0; i + 2 < px.Length; i += 3)
        {
            if (px[i + 2] <= 150 || px[i + 1] <= 90) continue;
            bright++;
            int hue = px[i];
            if (hue < 10 || hue > 165) red++;
            else if (hue is >= 12 and <= 35) yel++;
            else if (hue is >= 40 and <= 95) grn++;
        }
        int m = Math.Max(red, Math.Max(yel, grn));
        if (bright < 4 || m < 4) return null;
        return m == red ? "red" : m == yel ? "yellow" : "green";
    }

    private static (double H, double S, double V) MedianHsv(Mat img, Rect r)
    {
        using var roi = new Mat(img, r);
        using var hsv = new Mat();
        Cv2.CvtColor(roi, hsv, ColorConversionCodes.BGR2HSV);
        var px = Bytes(hsv);
        int n = px.Length / 3;
        var hs = new double[n]; var ss = new double[n]; var vs = new double[n];
        for (int i = 0; i < n; i++) { hs[i] = px[i * 3]; ss[i] = px[i * 3 + 1]; vs[i] = px[i * 3 + 2]; }
        return (CameraMotion.Median(hs), CameraMotion.Median(ss), CameraMotion.Median(vs));
    }
}
