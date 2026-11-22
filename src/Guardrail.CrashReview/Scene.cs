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
    /// Picks the road-facing view: trims black borders and, for split-screen or picture-in-picture recordings
    /// (cab camera beside, above or behind the road camera), the panel with the most traffic in it.
    /// Also returns the cab camera panel when there is one, so the driver can be checked for phone use.
    /// </summary>
    public static (Rect Road, Rect? Cabin) FindView(IReadOnlyList<Mat> probes, Detector detector)
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

            // Mean absolute horizontal / vertical gradient at every pixel, averaged over the probes.
            var gxMap = new float[W * H]; var gyMap = new float[W * H];
            foreach (var g in grays)
            {
                var px = Bytes(g);
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int i = y * W + x;
                        if (x > 0) gxMap[i] += Math.Abs(px[i] - px[i - 1]);
                        if (y > 0) gyMap[i] += Math.Abs(px[i] - px[i - W]);
                    }
            }
            float inv = 1f / grays.Count;
            for (int i = 0; i < gxMap.Length; i++) { gxMap[i] *= inv; gyMap[i] *= inv; }

            var leaves = Split(whole, gxMap, gyMap, W, 0);
            if (leaves.Count == 1 && Inset(whole, gxMap, gyMap, W) is { } pip) leaves = pip;
            leaves = leaves.Select(c => TrimDark(c, mxb, W)).Where(c => c.Width >= 64 && c.Height >= 64).ToList();
            if (leaves.Count <= 1) return (leaves.Count == 1 ? leaves[0] : whole, null);

            var scored = new List<(Rect R, double Road, double Cab)>();
            foreach (var c in leaves)
            {
                double road = 0, cab = 0;
                foreach (var p in probes)
                {
                    using var crop = new Mat(p, c);
                    foreach (var d in detector.Detect(crop))
                    {
                        if (d.Kind is Kind.Vehicle or Kind.TwoWheeler or Kind.TrafficLight or Kind.StopSign) road += 1;
                        else if (d.Kind == Kind.Person && d.Box.H > 0.45) cab += 1; // the cab camera sees the driver up close
                    }
                }
                scored.Add((c, road - 0.5 * cab, cab));
            }
            var roadView = scored.MaxBy(x => x.Road).R;
            var cabs = scored.Where(x => x.R != roadView && x.Cab >= 3).ToList();
            Rect? cabin = cabs.Count > 0 ? cabs.MaxBy(x => x.Cab * 1e7 + x.R.Width * x.R.Height).R : null;
            return (roadView, cabin);
        }
        finally { foreach (var g in grays) g.Dispose(); }
    }

    /// <summary>Recursively splits a panel at straight seams that run along (nearly) its whole height or width.</summary>
    private static List<Rect> Split(Rect r, float[] gxMap, float[] gyMap, int stride, int depth)
    {
        if (depth > 2 || r.Width < 160 || r.Height < 120) return [r];
        var gx = new double[r.Width]; var gy = new double[r.Height];
        for (int y = r.Y; y < r.Bottom; y++)
            for (int x = r.X; x < r.Right; x++)
            {
                gx[x - r.X] += gxMap[y * stride + x] / r.Height;
                gy[y - r.Y] += gyMap[y * stride + x] / r.Width;
            }
        if (Seam(gx, i => Column(gxMap, stride, r.X + i, r.Y, r.Bottom)) is int sx)
            return [.. Split(new Rect(r.X, r.Y, sx, r.Height), gxMap, gyMap, stride, depth + 1),
                    .. Split(new Rect(r.X + sx + 1, r.Y, r.Width - sx - 1, r.Height), gxMap, gyMap, stride, depth + 1)];
        if (Seam(gy, i => Row(gyMap, stride, r.Y + i, r.X, r.Right)) is int sy)
            return [.. Split(new Rect(r.X, r.Y, r.Width, sy), gxMap, gyMap, stride, depth + 1),
                    .. Split(new Rect(r.X, r.Y + sy + 1, r.Width, r.Height - sy - 1), gxMap, gyMap, stride, depth + 1)];
        return [r];
    }

    private static float[] Column(float[] map, int stride, int x, int y0, int y1)
    {
        var v = new float[Math.Max(0, y1 - y0)];
        for (int y = y0; y < y1; y++) v[y - y0] = map[y * stride + x];
        return v;
    }

    private static float[] Row(float[] map, int stride, int y, int x0, int x1)
    {
        var v = new float[Math.Max(0, x1 - x0)];
        for (int x = x0; x < x1; x++) v[x - x0] = map[y * stride + x];
        return v;
    }

    /// <summary>A panel seam is a straight edge along nearly the whole panel — unlike a horizon, a bonnet or a road edge.</summary>
    private static int? Seam(double[] g, Func<int, float[]> line)
    {
        int lo = (int)(g.Length * 0.3), hi = (int)(g.Length * 0.7);
        if (hi <= lo) return null;
        double baseLevel = CameraMotion.Median(g) + 1e-3;
        int best = lo;
        for (int i = lo; i < hi; i++) if (g[i] > g[best]) best = i;
        if (g[best] <= 4 * baseLevel) return null;
        var l = line(best);
        return l.Length > 0 && l.Count(v => v > 3 * baseLevel) >= 0.85 * l.Length ? best : null;
    }

    /// <summary>Picture-in-picture: a rectangle in one corner whose two inner edges are straight lines.</summary>
    private static List<Rect>? Inset(Rect r, float[] gxMap, float[] gyMap, int stride)
    {
        var gx = new double[r.Width]; var gy = new double[r.Height];
        for (int y = r.Y; y < r.Bottom; y++)
            for (int x = r.X; x < r.Right; x++)
            {
                gx[x - r.X] += gxMap[y * stride + x] / r.Height;
                gy[y - r.Y] += gyMap[y * stride + x] / r.Width;
            }
        double bx = CameraMotion.Median(gx) + 1e-3, by = CameraMotion.Median(gy) + 1e-3;
        var xs = Enumerable.Range(0, r.Width).OrderByDescending(i => gx[i]).Take(8).Where(i => i > 0.15 * r.Width && i < 0.85 * r.Width).ToList();
        var ys = Enumerable.Range(0, r.Height).OrderByDescending(i => gy[i]).Take(8).Where(i => i > 0.15 * r.Height && i < 0.85 * r.Height).ToList();
        (double F, Rect Ins, Rect Other)? best = null;
        foreach (int xi in xs)
            foreach (int yi in ys)
                foreach (bool top in new[] { true, false })
                    foreach (bool right in new[] { true, false })
                    {
                        var col = top ? Column(gxMap, stride, r.X + xi, r.Y, r.Y + yi) : Column(gxMap, stride, r.X + xi, r.Y + yi, r.Bottom);
                        var row = right ? Row(gyMap, stride, r.Y + yi, r.X + xi, r.Right) : Row(gyMap, stride, r.Y + yi, r.X, r.X + xi);
                        if (col.Length < 20 || row.Length < 20) continue;
                        double f = Math.Min(col.Count(v => v > 2.0 * bx) / (double)col.Length, row.Count(v => v > 2.0 * by) / (double)row.Length);
                        if (f < 0.88 || (best != null && f <= best.Value.F)) continue;
                        var ins = new Rect(right ? r.X + xi + 1 : r.X, top ? r.Y : r.Y + yi + 1, right ? r.Width - xi - 1 : xi, top ? yi : r.Height - yi - 1);
                        var rest = new Rect(right ? r.X : r.X + xi + 1, r.Y, right ? xi : r.Width - xi - 1, r.Height);
                        best = (f, ins, rest);
                    }
        return best is { } b ? [b.Ins, b.Other] : null;
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

    /// <summary>
    /// Rough paint colour of a vehicle ("white", "red", …) from the middle of its box. The frame is white-balanced
    /// first (gray world), so evening sun or a tinted windshield doesn't turn a white truck orange.
    /// </summary>
    public static string? VehicleColor(Mat img, Box b)
    {
        int h = img.Rows, w = img.Cols;
        double x1 = b.X1 * w, x2 = b.X2 * w, y1 = b.Y1 * h, y2 = b.Y2 * h;
        var r = CameraMotion.Clip(new Rect((int)(x1 + (x2 - x1) * 0.25), (int)(y1 + (y2 - y1) * 0.3),
            (int)((x2 - x1) * 0.5), (int)((y2 - y1) * 0.45)), w, h);
        if (r.Width < 2 || r.Height < 2) return null;
        var mean = Cv2.Mean(img);
        double gray = (mean.Val0 + mean.Val1 + mean.Val2) / 3;
        using var roi = new Mat(img, r);
        var px = Bytes(roi);
        int n = px.Length / 3;
        var bs = new double[n]; var gs = new double[n]; var rs = new double[n];
        for (int i = 0; i < n; i++) { bs[i] = px[i * 3]; gs[i] = px[i * 3 + 1]; rs[i] = px[i * 3 + 2]; }
        byte Bal(double v, double m) => (byte)Math.Clamp(v * gray / Math.Max(m, 1), 0, 255);
        using var one = new Mat(1, 1, MatType.CV_8UC3, new Scalar(
            Bal(CameraMotion.Median(bs), mean.Val0), Bal(CameraMotion.Median(gs), mean.Val1), Bal(CameraMotion.Median(rs), mean.Val2)));
        using var hsv = new Mat();
        Cv2.CvtColor(one, hsv, ColorConversionCodes.BGR2HSV);
        var v3 = hsv.Get<Vec3b>(0, 0);
        double hh = v3.Item0, s = v3.Item1, v = v3.Item2;
        if (s < 50 || (v > 180 && s < 75)) return v > 175 ? "white" : v > 120 ? "silver" : v > 70 ? "gray" : "black";
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
}
