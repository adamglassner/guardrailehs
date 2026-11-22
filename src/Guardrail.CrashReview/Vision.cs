using System.Runtime.InteropServices;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace Guardrail.CrashAi;

/// <summary>What a detected object is, as far as fault analysis cares.</summary>
public enum Kind { Vehicle, TwoWheeler, Person, TrafficLight, StopSign, Phone }

/// <summary>A box in normalized image coordinates (0..1 across and down).</summary>
public readonly record struct Box(double X1, double Y1, double X2, double Y2)
{
    public double W => X2 - X1;
    public double H => Y2 - Y1;
    public double Cx => (X1 + X2) / 2;
    public double Area => Math.Max(0, W) * Math.Max(0, H);

    public Box Shift(double dx1, double dy1, double dx2, double dy2, double dt) =>
        new(X1 + dx1 * dt, Y1 + dy1 * dt, X2 + dx2 * dt, Y2 + dy2 * dt);

    private static double Inter(Box a, Box b) =>
        Math.Max(0, Math.Min(a.X2, b.X2) - Math.Max(a.X1, b.X1)) * Math.Max(0, Math.Min(a.Y2, b.Y2) - Math.Max(a.Y1, b.Y1));

    public static double Iou(Box a, Box b)
    {
        double i = Inter(a, b), u = a.Area + b.Area - i;
        return u > 0 ? i / u : 0;
    }

    public static double InterOverMin(Box a, Box b)
    {
        double m = Math.Min(a.Area, b.Area);
        return m > 0 ? Inter(a, b) / m : 0;
    }
}

public sealed record Detection(Box Box, float Score, int ClassId)
{
    public Kind Kind => Classes.KindOf(ClassId)!.Value;
    /// <summary>Paint colour for vehicles ("white"), light state for traffic lights ("red").</summary>
    public string? Extra { get; set; }
}

/// <summary>The COCO classes the detector reports that matter for a crash.</summary>
public static class Classes
{
    public const int Person = 0, Bicycle = 1, Car = 2, Motorcycle = 3, Bus = 5, Truck = 7, TrafficLight = 9, StopSign = 11, CellPhone = 67;

    public static Kind? KindOf(int id) => id switch
    {
        Car or Bus or Truck => Kind.Vehicle,
        Bicycle or Motorcycle => Kind.TwoWheeler,
        Person => Kind.Person,
        TrafficLight => Kind.TrafficLight,
        StopSign => Kind.StopSign,
        CellPhone => Kind.Phone,
        _ => null,
    };

    public static bool IsRoadUser(Kind k) => k is Kind.Vehicle or Kind.TwoWheeler or Kind.Person;
}

/// <summary>YOLOX-S object detector (Apache-2.0) running on OpenCV's DNN module, CPU only.</summary>
public sealed class Detector : IDisposable
{
    private const int Size = 640;
    private readonly Net _net;
    private readonly float[] _gx, _gy, _stride;

    public Detector(string onnxPath)
    {
        _net = CvDnn.ReadNetFromOnnx(onnxPath) ?? throw new InvalidOperationException("The AI model couldn't be loaded.");
        var gx = new List<float>(); var gy = new List<float>(); var st = new List<float>();
        foreach (int s in new[] { 8, 16, 32 })
        {
            int n = Size / s;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++) { gx.Add(x); gy.Add(y); st.Add(s); }
        }
        _gx = gx.ToArray(); _gy = gy.ToArray(); _stride = st.ToArray();
    }

    public List<Detection> Detect(Mat img, float conf = 0.3f, float nms = 0.45f)
    {
        int h = img.Rows, w = img.Cols;
        double r = Math.Min((double)Size / h, (double)Size / w);
        int nw = Math.Max(1, (int)Math.Round(w * r)), nh = Math.Max(1, (int)Math.Round(h * r));
        using var pad = new Mat(Size, Size, MatType.CV_8UC3, new Scalar(114, 114, 114));
        using (var resized = new Mat())
        using (var roi = new Mat(pad, new Rect(0, 0, nw, nh)))
        {
            Cv2.Resize(img, resized, new OpenCvSharp.Size(nw, nh), 0, 0, InterpolationFlags.Linear);
            resized.CopyTo(roi);
        }
        using var blob = CvDnn.BlobFromImage(pad, 1.0, new OpenCvSharp.Size(Size, Size), new Scalar(0, 0, 0), false, false);
        _net.SetInput(blob);
        using var output = _net.Forward();
        int count = output.Size(1), width = output.Size(2);
        var data = new float[count * width];
        using (var cont = output.IsContinuous() ? null : output.Clone())
            Marshal.Copy((cont ?? output).Data, data, 0, data.Length);

        var raw = new List<Detection>();
        for (int i = 0; i < count && i < _gx.Length; i++)
        {
            int o = i * width;
            float obj = data[o + 4];
            if (obj < conf) continue;
            int best = 0; float bestScore = 0;
            for (int c = 5; c < width; c++)
                if (data[o + c] > bestScore) { bestScore = data[o + c]; best = c - 5; }
            float score = obj * bestScore;
            if (score < conf || Classes.KindOf(best) is null) continue;
            double cx = (data[o] + _gx[i]) * _stride[i], cy = (data[o + 1] + _gy[i]) * _stride[i];
            double bw = Math.Exp(data[o + 2]) * _stride[i], bh = Math.Exp(data[o + 3]) * _stride[i];
            double x1 = Math.Max(0, (cx - bw / 2) / r), y1 = Math.Max(0, (cy - bh / 2) / r);
            double x2 = Math.Min(w, (cx + bw / 2) / r), y2 = Math.Min(h, (cy + bh / 2) / r);
            if (x2 - x1 < 4 || y2 - y1 < 4) continue;
            raw.Add(new Detection(new Box(x1 / w, y1 / h, x2 / w, y2 / h), score, best));
        }

        // Per-class non-maximum suppression, then merge car/truck/bus double detections.
        var kept = new List<Detection>();
        foreach (var d in raw.OrderByDescending(d => d.Score))
        {
            bool dup = kept.Any(k => (k.ClassId == d.ClassId && Box.Iou(k.Box, d.Box) > nms)
                                  || (k.Kind == d.Kind && Box.Iou(k.Box, d.Box) > 0.7));
            if (!dup) kept.Add(d);
        }
        return kept;
    }

    public void Dispose() => _net.Dispose();
}

/// <summary>One road user followed through the video.</summary>
public sealed class Track
{
    public int Id { get; init; }
    public Kind Kind { get; init; }
    public int ClassId { get; set; }
    public SortedDictionary<double, Box> History { get; } = new();
    public List<string> Colors { get; } = [];
    internal int Misses;
    internal double LastT;
    private (double, double, double, double) _vel;

    public Box BoxAt(double t) => History.MinBy(kv => Math.Abs(kv.Key - t)).Value;
    public double FirstT => History.Keys.First();
    public double LastSeen => History.Keys.Last();

    public IEnumerable<(double T, Box B)> Between(double t0, double t1) =>
        History.Where(kv => kv.Key >= t0 - 1e-6 && kv.Key <= t1 + 1e-6).Select(kv => (kv.Key, kv.Value));

    internal Box Predict(double t)
    {
        var b = History[LastT]; double dt = t - LastT;
        return b.Shift(_vel.Item1, _vel.Item2, _vel.Item3, _vel.Item4, dt);
    }

    internal void Update(Detection d, double t)
    {
        var b0 = History[LastT]; double dt = Math.Max(1e-6, t - LastT);
        var v = ((d.Box.X1 - b0.X1) / dt, (d.Box.Y1 - b0.Y1) / dt, (d.Box.X2 - b0.X2) / dt, (d.Box.Y2 - b0.Y2) / dt);
        _vel = (0.5 * _vel.Item1 + 0.5 * v.Item1, 0.5 * _vel.Item2 + 0.5 * v.Item2, 0.5 * _vel.Item3 + 0.5 * v.Item3, 0.5 * _vel.Item4 + 0.5 * v.Item4);
        History[t] = d.Box; LastT = t; Misses = 0;
        if (d.Kind == Kind.Vehicle && d.Score > 0.5) ClassId = d.ClassId;
        if (d.Extra is { } c && Kind == Kind.Vehicle && Colors.Count < 12) Colors.Add(c);
    }

    /// <summary>Plain name for the report: "white car", "truck", "pedestrian".</summary>
    public string Name
    {
        get
        {
            string b = Kind switch
            {
                Kind.Vehicle => ClassId switch { Classes.Bus => "bus", Classes.Truck => "truck", _ => "car" },
                Kind.TwoWheeler => ClassId == Classes.Motorcycle ? "motorcycle" : "bicycle",
                _ => "pedestrian",
            };
            string? col = Colors.Count == 0 ? null : Colors.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;
            return col != null && Kind == Kind.Vehicle ? $"{col} {b}" : b;
        }
    }
}

/// <summary>Greedy IoU tracker with constant-velocity prediction.</summary>
public sealed class Tracker
{
    private readonly List<Track> _active = [], _done = [];
    private int _next = 1;
    private const int MaxMisses = 6;

    public void Step(IReadOnlyList<Detection> dets, double t)
    {
        var pairs = new List<(double Iou, int T, int D)>();
        for (int ti = 0; ti < _active.Count; ti++)
        {
            var p = _active[ti].Predict(t);
            for (int di = 0; di < dets.Count; di++)
            {
                if (dets[di].Kind != _active[ti].Kind) continue;
                double o = Box.Iou(p, dets[di].Box);
                if (o > 0.15) pairs.Add((o, ti, di));
            }
        }
        var usedT = new HashSet<int>(); var usedD = new HashSet<int>();
        foreach (var (_, ti, di) in pairs.OrderByDescending(p => p.Iou))
        {
            if (usedT.Contains(ti) || usedD.Contains(di)) continue;
            _active[ti].Update(dets[di], t); usedT.Add(ti); usedD.Add(di);
        }
        for (int ti = 0; ti < _active.Count; ti++) if (!usedT.Contains(ti)) _active[ti].Misses++;
        for (int di = 0; di < dets.Count; di++)
        {
            if (usedD.Contains(di) || dets[di].Score <= 0.4) continue;
            var tr = new Track { Id = _next++, Kind = dets[di].Kind, ClassId = dets[di].ClassId, LastT = t };
            tr.History[t] = dets[di].Box;
            if (dets[di].Extra is { } c && tr.Kind == Kind.Vehicle) tr.Colors.Add(c);
            _active.Add(tr);
        }
        for (int i = _active.Count - 1; i >= 0; i--)
            if (_active[i].Misses > MaxMisses) { _done.Add(_active[i]); _active.RemoveAt(i); }
    }

    public List<Track> All() => _done.Concat(_active).Where(t => t.History.Count >= 3).ToList();
}
