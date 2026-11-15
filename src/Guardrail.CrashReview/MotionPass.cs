using OpenCvSharp;

namespace Guardrail.CrashAi;

/// <summary>Clean-up after detection, then camera motion for every sample.</summary>
public static class MotionPass
{
    /// <summary>
    /// Removes detections that are really part of the camera vehicle (bonnet, dashboard, a reflection):
    /// big boxes in the same place for a large share of the video, and one-off boxes covering most of the view.
    /// </summary>
    public static int RemoveFixtures(List<Sample> samples)
    {
        int n = samples.Count;
        var big = new List<(int I, Detection D)>();
        for (int i = 0; i < n; i++)
            foreach (var d in samples[i].Detections)
                if (d.Kind is Kind.Vehicle or Kind.TwoWheeler && d.Box.Area > 0.12) big.Add((i, d));

        var drop = new HashSet<Detection>(ReferenceEqualityComparer.Instance);
        foreach (var (_, d) in big)
        {
            if (drop.Contains(d)) continue;
            var same = big.Where(b => Box.Iou(d.Box, b.D.Box) > 0.8).ToList();
            if (same.Select(b => b.I).Distinct().Count() > Math.Max(8, 0.2 * n))
                foreach (var s in same) drop.Add(s.D);
        }
        foreach (var s in samples) s.Detections = s.Detections.Where(d => !drop.Contains(d)).ToList();

        // A box covering most of the view only counts if something grew into it (a real vehicle filling the view).
        int phantoms = 0;
        for (int i = 0; i < n; i++)
        {
            var prev = i > 0 ? samples[i - 1].Detections : new List<Detection>();
            samples[i].Detections = samples[i].Detections.Where(d =>
            {
                if (d.Box.Area <= 0.55) return true;
                bool grew = prev.Any(p => p.Kind == d.Kind && Box.InterOverMin(p.Box, d.Box) > 0.6 && p.Box.Area > 0.2);
                if (!grew) phantoms++;
                return grew;
            }).ToList();
        }
        return drop.Count + phantoms;
    }

    /// <summary>32-bin brightness histogram, for spotting cuts between cameras in edited videos.</summary>
    private static double[] Histogram(Mat gray)
    {
        var h = new double[32];
        foreach (var b in Scene.Bytes(gray)) h[b >> 3]++;
        return h;
    }

    private static double Correlation(double[] a, double[] b)
    {
        double ma = a.Average(), mb = b.Average(), num = 0, da = 0, db = 0;
        for (int i = 0; i < a.Length; i++) { num += (a[i] - ma) * (b[i] - mb); da += (a[i] - ma) * (a[i] - ma); db += (b[i] - mb) * (b[i] - mb); }
        return da <= 0 || db <= 0 ? 1 : num / Math.Sqrt(da * db);
    }

    /// <summary>Camera motion from the stored small frames, ignoring road users and pixels that never change.</summary>
    public static double Run(List<Sample> samples, CancellationToken cancel)
    {
        if (samples.Count == 0) return 0;
        Mat Gray(Sample s) => Cv2.ImDecode(s.Jpeg, ImreadModes.Grayscale);

        // Static mask from up to 24 frames spread through the video.
        int probes = Math.Min(24, samples.Count);
        var picks = Enumerable.Range(0, probes).Select(k => samples[(int)(samples.Count * (k + 0.5) / probes)]).ToList();
        Mat? usable = null;
        double staticFrac = 0;
        if (picks.Count >= 4)
        {
            int w = 0, h = 0;
            double[]? sum = null, sq = null;
            foreach (var p in picks)
            {
                using var g = Gray(p);
                using var blur = new Mat();
                Cv2.GaussianBlur(g, blur, new OpenCvSharp.Size(5, 5), 0);
                var px = Scene.Bytes(blur);
                if (sum is null) { w = blur.Cols; h = blur.Rows; sum = new double[px.Length]; sq = new double[px.Length]; }
                if (px.Length != sum.Length) continue;
                for (int i = 0; i < px.Length; i++) { sum[i] += px[i]; sq![i] += px[i] * (double)px[i]; }
            }
            if (sum != null)
            {
                var stat = new byte[sum.Length];
                int count = 0;
                for (int i = 0; i < sum.Length; i++)
                {
                    double m = sum[i] / picks.Count, v = sq![i] / picks.Count - m * m;
                    if (Math.Sqrt(Math.Max(0, v)) < 7) { stat[i] = 1; count++; }
                }
                staticFrac = (double)count / sum.Length;
                if (staticFrac < 0.6)
                {
                    using var st = new Mat(h, w, MatType.CV_8UC1);
                    System.Runtime.InteropServices.Marshal.Copy(stat, 0, st.Data, stat.Length);
                    using var k = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(7, 7));
                    using var dil = new Mat();
                    Cv2.Dilate(st, dil, k);
                    usable = new Mat();
                    Cv2.Compare(dil, Scalar.All(0), usable, CmpType.EQ); // 255 where not static
                }
            }
        }

        using var motion = new CameraMotion(usable);
        double[]? prevHist = null;
        foreach (var s in samples)
        {
            cancel.ThrowIfCancellationRequested();
            using var g = Gray(s);
            var step = motion.Step(g, s.Detections.Where(d => Classes.IsRoadUser(d.Kind)).Select(d => d.Box));
            var hist = Histogram(g);
            s.Camera = step with { Cut = prevHist != null && Correlation(prevHist, hist) < 0.5 };
            prevHist = hist;
        }
        return staticFrac;
    }
}
