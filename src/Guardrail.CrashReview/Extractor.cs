using OpenCvSharp;

namespace Guardrail.CrashAi;

/// <summary>Everything measured on one sampled frame.</summary>
public sealed class Sample
{
    public double T { get; init; }
    /// <summary>Filled in by <see cref="MotionPass"/> after fixtures are removed.</summary>
    public CameraStep Camera { get; set; } = new();
    public required List<Detection> Detections { get; set; }
    /// <summary>Yellow centre line position at the bottom of the road view (0..1), when one is visible.</summary>
    public double? YellowLine { get; init; }
    /// <summary>Downscaled JPEG of the frame, for key frames in the report.</summary>
    public required byte[] Jpeg { get; init; }
    /// <summary>People and phones seen by the cab camera (split-screen recordings only; every few samples).</summary>
    public List<Detection>? Cabin { get; init; }
}

public sealed class Extraction
{
    public required List<Sample> Samples { get; init; }
    public double Fps { get; init; }
    public double Duration { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    /// <summary>The part of the frame used (split-screen and black bars removed).</summary>
    public Rect View { get; init; }
    public bool SplitScreen { get; init; }
    /// <summary>The cab-camera panel of a split-screen recording, when there is one.</summary>
    public Rect? CabinView { get; init; }
}

/// <summary>The expensive pass over the video: decode, detect, measure camera motion.</summary>
public static class Extractor
{
    public const double SampleFps = 10.0;
    public const double MaxSeconds = 300;

    public static Extraction Run(string videoPath, Detector detector, IProgress<double>? progress, CancellationToken cancel)
    {
        using var cap = new VideoCapture(videoPath);
        if (!cap.IsOpened()) throw new InvalidDataException("This video couldn't be opened. Try converting it to MP4 (H.264).");
        double fps = cap.Get(VideoCaptureProperties.Fps);
        if (double.IsNaN(fps) || fps < 1 || fps > 240) fps = 30;
        int total = (int)Math.Max(0, cap.Get(VideoCaptureProperties.FrameCount));
        int step = Math.Max(1, (int)Math.Round(fps / SampleFps));

        // Probe a few frames to find the road view.
        var probes = new List<Mat>();
        for (int k = 0; k < 10 && total > 0; k++)
        {
            cap.Set(VideoCaptureProperties.PosFrames, (int)(total * (k + 0.5) / 10));
            var m = new Mat();
            if (cap.Read(m) && !m.Empty()) probes.Add(m); else m.Dispose();
        }
        cap.Set(VideoCaptureProperties.PosFrames, 0);
        Rect view;
        Rect? cabin = null;
        int srcW, srcH;
        try
        {
            if (probes.Count == 0)
            {
                using var first = new Mat();
                if (!cap.Read(first) || first.Empty()) throw new InvalidDataException("No frames could be read from this video.");
                srcW = first.Cols; srcH = first.Rows;
                view = new Rect(0, 0, srcW, srcH);
                cap.Set(VideoCaptureProperties.PosFrames, 0);
            }
            else
            {
                srcW = probes[0].Cols; srcH = probes[0].Rows;
                (view, cabin) = Scene.FindView(probes, detector);
            }
        }
        finally { foreach (var p in probes) p.Dispose(); }
        bool split = view.Width < srcW * 0.8 || view.Height < srcH * 0.8;

        int cabEvery = Math.Max(1, (int)Math.Round(SampleFps / 2)); // the cab camera: 2 looks per second is plenty
        var samples = new List<Sample>();
        using var frame = new Mat();
        int idx = 0;
        {
            while (cap.Grab())
            {
                cancel.ThrowIfCancellationRequested();
                if (idx % step == 0)
                {
                    if (!cap.Retrieve(frame) || frame.Empty()) break;
                    double t = idx / fps;
                    if (t > MaxSeconds) break;
                    var v = CameraMotion.Clip(view, frame.Cols, frame.Rows);
                    using var cropped = new Mat(frame, v);
                    using var work = new Mat();
                    double scale = cropped.Cols > 960 ? 960.0 / cropped.Cols : 1.0;
                    if (scale != 1.0) Cv2.Resize(cropped, work, new OpenCvSharp.Size((int)(cropped.Cols * scale), (int)(cropped.Rows * scale)), 0, 0, InterpolationFlags.Linear);
                    else cropped.CopyTo(work);

                    var dets = detector.Detect(work).Where(d => d.Kind != Kind.Phone).ToList();
                    List<Detection>? cab = null;
                    if (cabin is { } cr && samples.Count % cabEvery == 0)
                    {
                        using var cabMat = new Mat(frame, CameraMotion.Clip(cr, frame.Cols, frame.Rows));
                        cab = detector.Detect(cabMat, 0.25f).Where(d => d.Kind is Kind.Person or Kind.Phone).ToList();
                    }
                    foreach (var d in dets)
                        d.Extra = d.Kind switch
                        {
                            Kind.Vehicle => Scene.VehicleColor(work, d.Box),
                            Kind.TrafficLight => Scene.LightColor(work, d.Box),
                            _ => null,
                        };
                    var movers = dets.Where(d => Classes.IsRoadUser(d.Kind)).Select(d => d.Box).ToList();

                    using var small = new Mat();
                    Cv2.Resize(work, small, new OpenCvSharp.Size(480, Math.Max(1, (int)(480.0 * work.Rows / work.Cols))), 0, 0, InterpolationFlags.Linear);
                    Cv2.ImEncode(".jpg", small, out byte[] jpeg, new ImageEncodingParam(ImwriteFlags.JpegQuality, 82));

                    samples.Add(new Sample { T = t, Detections = dets, YellowLine = Scene.YellowLine(work, movers), Jpeg = jpeg, Cabin = cab });
                    if (total > 0) progress?.Report(Math.Min(1.0, (double)idx / Math.Min(total, MaxSeconds * fps)));
                }
                idx++;
            }
        }

        return new Extraction
        {
            Samples = samples, Fps = fps, Duration = idx / fps, Width = srcW, Height = srcH, View = view, SplitScreen = split, CabinView = cabin,
        };
    }
}
