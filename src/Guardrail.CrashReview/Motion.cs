using System.Numerics;
using OpenCvSharp;

namespace Guardrail.CrashAi;

/// <summary>How the camera itself moved between two sampled frames.</summary>
public sealed record CameraStep
{
    /// <summary>Sideways / vertical shift of the background (fraction of the frame).</summary>
    public double Tx { get; init; }
    public double Ty { get; init; }
    public double Rot { get; init; }
    /// <summary>Radial expansion of the background (forward motion; ~0 when stopped).</summary>
    public double Expand { get; init; }
    /// <summary>Median background flow (fraction of width).</summary>
    public double Mag { get; init; }
    public double Inliers { get; init; } = 1;
    public int Points { get; init; }
    /// <summary>The video cuts to a different camera / scene at this frame.</summary>
    public bool Cut { get; init; }
}

/// <summary>Background optical flow (Lucas–Kanade) with a robust similarity fit, ignoring moving road users.</summary>
public sealed class CameraMotion : IDisposable
{
    private Mat? _prev;
    private readonly Mat? _static;
    private static readonly Random Rng = new(7);

    /// <param name="usable">Optional mask: 255 where the background can show motion (not bonnet / overlay).</param>
    public CameraMotion(Mat? usable = null) => _static = usable;

    public CameraStep Step(Mat gray, IEnumerable<Box> movers)
    {
        var res = new CameraStep();
        int w = gray.Cols, h = gray.Rows;
        if (_prev != null)
        {
            using var mask = _static is { } st && st.Rows == h && st.Cols == w ? st.Clone() : new Mat(h, w, MatType.CV_8UC1, Scalar.All(255));
            foreach (var b in movers)
            {
                var r = Clip(new Rect((int)(b.X1 * w) - 4, (int)(b.Y1 * h) - 4, (int)(b.W * w) + 8, (int)(b.H * h) + 8), w, h);
                if (r.Width > 0 && r.Height > 0) using (var sub = new Mat(mask, r)) sub.SetTo(Scalar.All(0));
            }
            var p0 = GridFeatures(_prev, mask);
            if (p0.Length >= 12)
            {
                var p1 = Array.Empty<Point2f>();
                Cv2.CalcOpticalFlowPyrLK(_prev, gray, p0, ref p1, out byte[] status, out _, new OpenCvSharp.Size(21, 21), 3);
                var a = new List<Vector2>(); var b2 = new List<Vector2>();
                for (int i = 0; i < p0.Length && i < p1.Length; i++)
                    if (status[i] == 1) { a.Add(new(p0[i].X, p0[i].Y)); b2.Add(new(p1[i].X, p1[i].Y)); }
                if (a.Count >= 10)
                {
                    var flow = a.Select((p, i) => b2[i] - p).ToArray();
                    double mag = Percentile(flow.Select(f => (double)f.Length()), 70) / w;
                    var (sim, inl) = FitSimilarity(a, b2, 2.0);
                    double expand = 0;
                    var c = new Vector2(w * 0.5f, h * 0.45f);
                    var radial = new List<double>();
                    for (int i = 0; i < a.Count; i++)
                    {
                        var d = a[i] - c; double dn = d.Length();
                        if (dn > 0.08 * w) radial.Add(Vector2.Dot(flow[i], d) / dn / dn);
                    }
                    if (radial.Count >= 8) expand = Median(radial);
                    res = new CameraStep
                    {
                        Tx = sim.T.Real / w, Ty = sim.T.Imaginary / h,
                        Rot = sim.A.Phase, Expand = expand, Mag = mag, Inliers = inl, Points = a.Count,
                    };
                }
                else res = res with { Points = a.Count };
            }
            _prev.Dispose();
        }
        _prev = gray.Clone();
        return res;
    }

    /// <summary>
    /// The strongest corner (minimum eigenvalue) in each 8×8 cell, above 1% of the strongest in the image, at most 300.
    /// Used instead of goodFeaturesToTrack, which picks far fewer points in some OpenCV builds.
    /// </summary>
    internal static Point2f[] GridFeatures(Mat gray, Mat mask, int cell = 8, int maxPoints = 300)
    {
        int w = gray.Cols, h = gray.Rows;
        using var eig = new Mat();
        Cv2.CornerMinEigenVal(gray, eig, 3, 3);
        var e = new float[w * h];
        using (var cont = eig.IsContinuous() ? null : eig.Clone())
            System.Runtime.InteropServices.Marshal.Copy((cont ?? eig).Data, e, 0, e.Length);
        var m = Scene.Bytes(mask);
        float max = 0;
        for (int i = 0; i < e.Length; i++)
        {
            if (m[i] == 0) e[i] = 0;
            if (e[i] > max) max = e[i];
        }
        double thr = max > 0 ? 0.01 * max : 1;
        var found = new List<(float V, int X, int Y)>();
        for (int by = 0; by + cell <= h; by += cell)
            for (int bx = 0; bx + cell <= w; bx += cell)
            {
                float best = -1; int bxx = 0, byy = 0;
                for (int dy = 0; dy < cell; dy++)
                    for (int dx = 0; dx < cell; dx++)
                    {
                        float v = e[(by + dy) * w + bx + dx];
                        if (v > best) { best = v; bxx = bx + dx; byy = by + dy; }
                    }
                if (best > thr) found.Add((best, bxx, byy));
            }
        return found.OrderByDescending(f => f.V).Take(maxPoints).Select(f => new Point2f(f.X, f.Y)).ToArray();
    }

    /// <summary>RANSAC fit of q = a·p + t with a complex a (rotation + scale) — a 2-D similarity transform.</summary>
    private static ((Complex A, Complex T) Sim, double Inliers) FitSimilarity(List<Vector2> p, List<Vector2> q, double thr)
    {
        int n = p.Count;
        Complex P(int i) => new(p[i].X, p[i].Y);
        Complex Q(int i) => new(q[i].X, q[i].Y);
        var best = (A: Complex.One, T: Complex.Zero); int bestCount = -1;
        for (int it = 0; it < 120; it++)
        {
            int i = Rng.Next(n), j = Rng.Next(n);
            if (i == j) continue;
            var dp = P(j) - P(i);
            if (dp.Magnitude < 1e-3) continue;
            var a = (Q(j) - Q(i)) / dp;
            var t = Q(i) - a * P(i);
            int count = 0;
            for (int k = 0; k < n; k++) if ((a * P(k) + t - Q(k)).Magnitude < thr) count++;
            if (count > bestCount) { bestCount = count; best = (a, t); }
        }
        // Least-squares refinement on the inliers.
        var ins = Enumerable.Range(0, n).Where(k => (best.A * P(k) + best.T - Q(k)).Magnitude < thr).ToList();
        if (ins.Count >= 3)
        {
            Complex pm = Complex.Zero, qm = Complex.Zero;
            foreach (int k in ins) { pm += P(k); qm += Q(k); }
            pm /= ins.Count; qm /= ins.Count;
            Complex num = Complex.Zero; double den = 0;
            foreach (int k in ins) { var dp = P(k) - pm; num += (Q(k) - qm) * Complex.Conjugate(dp); den += dp.Magnitude * dp.Magnitude; }
            if (den > 1e-6) { var a = num / den; best = (a, qm - a * pm); }
        }
        return (best, n == 0 ? 0 : (double)Math.Max(0, bestCount) / n);
    }

    internal static double Median(IEnumerable<double> values)
    {
        var v = values.OrderBy(x => x).ToArray();
        if (v.Length == 0) return 0;
        return v.Length % 2 == 1 ? v[v.Length / 2] : (v[v.Length / 2 - 1] + v[v.Length / 2]) / 2;
    }

    /// <summary>numpy-style percentile with linear interpolation.</summary>
    internal static double Percentile(IEnumerable<double> values, double p)
    {
        var v = values.OrderBy(x => x).ToArray();
        if (v.Length == 0) return 0;
        double pos = (v.Length - 1) * p / 100.0;
        int lo = (int)Math.Floor(pos), hi = Math.Min(v.Length - 1, lo + 1);
        return v[lo] + (v[hi] - v[lo]) * (pos - lo);
    }

    internal static Rect Clip(Rect r, int w, int h)
    {
        int x1 = Math.Clamp(r.X, 0, w), y1 = Math.Clamp(r.Y, 0, h);
        int x2 = Math.Clamp(r.X + r.Width, 0, w), y2 = Math.Clamp(r.Y + r.Height, 0, h);
        return new Rect(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
    }

    public void Dispose() { _prev?.Dispose(); _static?.Dispose(); }
}
