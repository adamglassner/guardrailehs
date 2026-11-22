namespace Guardrail.CrashAi;

/// <summary>What to show in the key frames: which moments, which road users to outline.</summary>
public sealed record KeyPlan(List<(double T, string Caption)> Moments, List<(Track Track, string Label)> Highlight);

/// <summary>
/// Turns tracked road users and camera motion into a verdict: find the collision, work out what kind it was,
/// and apply rules of the road (following distance, lane changes, the centre line, signals, pedestrians).
/// </summary>
public static partial class Reasoner
{
    private sealed record Candidate(string Kind, double T, double Score, Track? Other = null, Track? A = null, Track? B = null);

    private sealed class Notes
    {
        public readonly List<Finding> All = [];
        public readonly List<string> Key = [];
        public void Add(double t, string text, bool key = false)
        {
            All.Add(new Finding(Math.Round(Math.Max(0, t), 2), text));
            if (key) Key.Add(text);
        }
    }

    /// <summary>Edited videos can cut between cameras: each piece is analyzed and the clearest collision wins.</summary>
    /// <param name="cameraHint">"auto", "moving" (dashcam in a moving vehicle), "fixed" (CCTV or a parked vehicle) or "handheld".</param>
    public static (CrashReport Report, KeyPlan Plan) Analyze(Extraction ex, double staticFraction, string cameraHint = "auto")
    {
        var all = ex.Samples;
        var bounds = new List<int> { 0 };
        for (int i = 1; i < all.Count; i++) if (all[i].Camera.Cut) bounds.Add(i);
        bounds.Add(all.Count);
        var pieces = new List<List<Sample>>();
        for (int k = 0; k + 1 < bounds.Count; k++)
        {
            var piece = all.GetRange(bounds[k], bounds[k + 1] - bounds[k]);
            if (piece.Count > 0 && piece[^1].T - piece[0].T >= 2.0) pieces.Add(piece);
        }
        if (pieces.Count <= 1) return AnalyzePiece(ex, pieces.Count == 1 ? pieces[0] : all, staticFraction, cameraHint).Result;
        var results = pieces.Select(p => AnalyzePiece(ex, p, staticFraction, cameraHint)).ToList();
        var hits = results.Where(r => r.Result.Report.Verdict != Verdicts.NoCollision).ToList();
        return (hits.Count == 0 ? results[0] : hits.MaxBy(r => r.Score)).Result;
    }

    private static ((CrashReport Report, KeyPlan Plan) Result, double Score) AnalyzePiece(Extraction ex, List<Sample> S, double staticFraction, string cameraHint)
    {
        var report = new CrashReport { DurationSeconds = Math.Round(ex.Duration, 1), SplitScreen = ex.SplitScreen, CameraSetting = cameraHint };
        var plan = new KeyPlan([], []);
        if (S.Count < 8)
        {
            report.Headline = "The video is too short to analyze";
            report.Summary = "Crash Review AI needs at least a second of video.";
            return ((report, plan), 0);
        }

        // ----- Tracks -----
        var tracker = new Tracker();
        foreach (var s in S) tracker.Step(s.Detections.Where(d => Classes.IsRoadUser(d.Kind)).ToList(), s.T);
        var tracks = tracker.All();
        report.VehiclesTracked = tracks.Count(t => t.History.Count >= 5);

        // ----- Camera motion series -----
        int n = S.Count;
        var ts = S.Select(s => s.T).ToArray();
        double dt = n > 1 ? CameraMotion.Median(ts.Zip(ts.Skip(1), (a, b) => b - a)) : 0.1;
        var mag = S.Select(s => s.Camera.Mag).ToArray();
        var tx = S.Select(s => s.Camera.Tx).ToArray();
        var ty = S.Select(s => s.Camera.Ty).ToArray();
        var rot = S.Select(s => s.Camera.Rot).ToArray();
        var inl = S.Select(s => s.Camera.Inliers).ToArray();
        var npts = S.Select(s => s.Camera.Points).ToArray();

        bool isFixed = CameraMotion.Percentile(mag, 90) < 0.0005 || staticFraction > 0.8;
        var expand = S.Select(s => s.Camera.Expand).ToArray();
        var moving = Enumerable.Range(0, n).Where(i => mag[i] > 0.001).ToList();
        bool handheld = !isFixed && moving.Count >= 5 &&
            (CameraMotion.Median(moving.Select(i => Math.Abs(tx[i]))) > 0.03 || moving.Count(i => expand[i] > 0) < 0.45 * moving.Count);
        if (cameraHint is "fixed" or "moving" or "handheld") { isFixed = cameraHint == "fixed"; handheld = cameraHint == "handheld"; }
        var speed = Smooth(mag, 5);
        double cruise = CameraMotion.Percentile(speed, 75);
        var jolt = new double[n];
        for (int i = 0; i < n; i++)
        {
            double D2(double[] a) => Math.Abs(a[Math.Min(n - 1, i + 1)] - 2 * a[i] + a[Math.Max(0, i - 1)]);
            jolt[i] = D2(tx) + 1.3 * D2(ty) + 0.6 * D2(rot);
        }
        var jz = RobustZ(jolt);
        double joltBase = Math.Max(CameraMotion.Percentile(jolt, 95), 0.005);
        report.CameraKind = isFixed ? "Fixed camera" : handheld ? "Handheld / panning camera" : "Dashcam (moving)";
        int stride = Math.Max(1, n / 120);
        report.SpeedTrace = speed.Where((_, i) => i % stride == 0).Select(v => Math.Round(v * 1000, 3)).ToList();
        report.TraceStepSeconds = Math.Round(dt * stride, 3);

        double Median(Func<int, bool> pick, double[] series)
        {
            var v = Enumerable.Range(0, n).Where(pick).Select(i => series[i]).ToList();
            return v.Count == 0 ? double.NaN : CameraMotion.Median(v);
        }
        double SpeedBetween(double t0, double t1) { var m = Median(i => ts[i] >= t0 && ts[i] <= t1, speed); return double.IsNaN(m) ? 0 : m; }

        // ----- Candidate collisions -----
        var cands = new List<Candidate>();
        if (!isFixed && !handheld)
        {
            for (int i = 0; i < n; i++)
            {
                double t = ts[i];
                if (t < ts[0] + 1.0) continue; // the first second is often start-up shake
                (double Score, Track Tr)? best = null;
                foreach (var tr in tracks)
                {
                    var ser = tr.Between(t - 1.2, t + 0.3).ToList();
                    if (ser.Count < 3) continue;
                    var b = ser[^1].B;
                    double a = b.Area, cx = Math.Abs(CorridorX(b));
                    if (cx > 1.25 || (a < 0.03 && tr.Kind == Kind.Vehicle) || a < 0.008) continue;
                    double span = ser[^1].T - ser[0].T;
                    double growth = Math.Log(Math.Max(a, 1e-4) / Math.Max(ser[0].B.Area, 1e-4)) / Math.Max(span, dt);
                    double close = Math.Min(1, a / 0.18) * (b.Y2 > 0.8 ? 1 : 0.6);
                    if (tr.Kind is Kind.Person or Kind.TwoWheeler) close = Math.Min(1, b.H / 0.45) * (b.Y2 > 0.75 ? 1 : 0.6);
                    double score = close * Math.Min(1, Math.Max(0, growth) / 1.2) * (1.2 - Math.Min(1, cx) * 0.5);
                    if (best is null || score > best.Value.Score) best = (score, tr);
                }
                double jscore = Math.Min(1, Math.Max(0, (jz[i] - 6) / 12));
                double pre = Median(k => ts[k] >= t - 2.0 && ts[k] <= t - 0.3, speed);
                double post = Median(k => ts[k] >= t + 0.8 && ts[k] <= t + 3.0, speed);
                bool stops = !double.IsNaN(pre) && !double.IsNaN(post) && post < 0.4 * Math.Max(pre, 1e-6) && pre > 0.15 * Math.Max(cruise, 1e-6);
                // The picture breaking up counts only when tracking was solid just before (some videos track poorly throughout).
                double baseInl = Median(k => ts[k] >= t - 2.0 && ts[k] <= t - 0.3, inl);
                bool breakup = inl[i] < 0.3 && npts[i] >= 10 && !double.IsNaN(baseInl) && baseInl >= 0.55;
                if (breakup)
                {
                    // A two-frame glitch (the picture recovers and the car carries on at the same speed) isn't an impact.
                    bool recovers = Enumerable.Range(i + 1, Math.Max(0, Math.Min(n, i + 3) - i - 1)).Any(j => inl[j] >= 0.75);
                    double mb = Median(k => ts[k] >= t - 0.6 && ts[k] <= t - 0.2, mag), ma = Median(k => ts[k] >= t + 0.2 && ts[k] <= t + 0.6, mag);
                    if (recovers && !double.IsNaN(mb) && !double.IsNaN(ma) && mb > 0.0005 && ma / Math.Max(mb, 1e-9) is >= 0.67 and <= 1.5)
                        breakup = false;
                }
                bool wasStopped = !double.IsNaN(pre) && pre < Math.Max(0.0003, 0.1 * cruise);
                bool impact = jolt[i] / joltBase >= 2.0 || breakup;
                bool confirm = impact && (stops || breakup || (wasStopped && jz[i] > 8));
                if (best is { } bb && bb.Score > 0.15 && confirm)
                {
                    double s = 0.55 * bb.Score + 0.45 * Math.Max(jscore, breakup ? 0.5 : 0);
                    if (bb.Tr.Kind is Kind.Person or Kind.TwoWheeler) s += 0.1;
                    cands.Add(new Candidate("ego_front", t, s, bb.Tr));
                }
                else if (jscore > 0.6 && confirm && t >= ts[0] + 2.0) // a jolt in the first seconds is usually the recording starting
                    cands.Add(new Candidate("ego_unseen", t, 0.5 * jscore));
            }
        }

        // Two road users colliding in view.
        var camShift = new Dictionary<double, (double X, double Y)>();
        for (int i = 0; i < n; i++) camShift[ts[i]] = (tx[i], ty[i]);
        for (int ai = 0; ai < tracks.Count; ai++)
            for (int bi = ai + 1; bi < tracks.Count; bi++)
            {
                var A = tracks[ai]; var B = tracks[bi];
                if (A.Kind == Kind.Person && B.Kind == Kind.Person) continue;
                var common = A.History.Keys.Intersect(B.History.Keys).OrderBy(t => t).ToList();
                if (common.Count < 4) continue;
                for (int k = 3; k < common.Count; k++)
                {
                    double t = common[k];
                    Box a = A.History[t], b = B.History[t];
                    if (Math.Min(a.H, b.H) < 0.05) continue; // too far away to judge
                    if (Box.InterOverMin(a, b) < 0.2) continue;
                    double ha = a.H, hb = b.H;
                    if (Math.Abs(a.Y2 - b.Y2) > 0.25 * Math.Max(ha, hb)) continue; // different depths: just one in front of the other
                    var prev = common.Where(c => c >= t - 1.2 && c < t - 0.2).ToList();
                    if (prev.Count == 0) continue;
                    double Dist(double tt)
                    {
                        Box x = A.History[tt], y = B.History[tt];
                        return Math.Sqrt(Math.Pow((x.X1 + x.X2 - y.X1 - y.X2) / 2, 2) + Math.Pow(x.Y2 - y.Y2, 2)) / Math.Max(Math.Max(ha, hb), 1e-3);
                    }
                    double approach = Dist(prev[0]) - Dist(t);
                    if (approach < 0.25) continue;
                    double jerk = 0;
                    foreach (var tr in new[] { A, B })
                    {
                        var vb = Velocity(tr, t - 0.5, t, camShift); var va = Velocity(tr, t, t + 0.6, camShift);
                        if (vb is null || va is null) { jerk = Math.Max(jerk, 0.4); continue; }
                        double sp = Math.Sqrt(vb.Value.X * vb.Value.X + vb.Value.Y * vb.Value.Y) + 1e-3;
                        jerk = Math.Max(jerk, Math.Min(1, Math.Sqrt(Math.Pow(va.Value.X - vb.Value.X, 2) + Math.Pow(va.Value.Y - vb.Value.Y, 2)) / sp));
                    }
                    double score = (0.35 * Math.Min(1, approach) + 0.45 * jerk + 0.2 * Math.Min(1, Box.InterOverMin(a, b))) * (isFixed ? 1.1 : handheld ? 0.75 : 0.8);
                    cands.Add(new Candidate("pair", t, score, A: A, B: B));
                    break;
                }
            }

        if (cands.Count == 0 || cands.Max(c => c.Score) < 0.35)
        {
            report.Verdict = Verdicts.NoCollision;
            report.Headline = "No collision found";
            report.Summary = isFixed
                ? "The AI followed every road user in view and didn't see two of them collide."
                : "The AI didn't see the camera vehicle hit anything or get hit, and didn't see other road users collide.";
            return ((report, plan), 0);
        }
        // Our own vehicle's first contact matters most for fault; otherwise take the clearest collision.
        var egoC = cands.Where(c => c.Kind != "pair").ToList();
        double top = egoC.Count > 0 ? egoC.Max(c => c.Score) : 0, overall = cands.Max(c => c.Score);
        var strong = top >= 0.8 * overall ? egoC.Where(c => c.Score >= 0.8 * top && c.Score >= 0.35).ToList() : new List<Candidate>();
        // A seen impact beats an unseen jolt of similar strength.
        if (strong.Any(c => c.Kind == "ego_front")) strong = strong.Where(c => c.Kind == "ego_front" || c.Score > 1.25 * strong.Where(f => f.Kind == "ego_front").Max(f => f.Score)).ToList();
        var hit = strong.Count > 0 ? strong.MinBy(c => c.T)! : cands.MaxBy(c => c.Score)!;
        double tImp = hit.T;
        report.ImpactSeconds = Math.Round(tImp, 2);
        var notes = new Notes();
        double faultFirst = 0, faultSecond = 0; // camera/A vs other/B
        double? hazardT = null; bool rearEnd = false, crossedLine = false;

        if (hit.Kind is "ego_front" or "ego_unseen")
        {
            var tr = hit.Other;
            report.Parties = ["camera vehicle", tr?.Name ?? "the other party"];
            double vBefore = SpeedBetween(tImp - 2.5, tImp - 1.0), vLast = SpeedBetween(tImp - 0.8, tImp - 0.1);
            bool stopped = vBefore < Math.Max(0.0003, 0.1 * cruise);
            bool braking = !stopped && vLast < 0.6 * vBefore;
            if (stopped) { notes.Add(tImp - 1.5, "The camera vehicle was stopped or barely moving before the impact.", key: true); faultSecond += 0.8; }
            else if (braking) notes.Add(tImp - 0.8, "The camera vehicle slowed sharply just before the impact (braking).");

            if (tr is null)
            {
                report.CollisionType = "Impact from outside the view";
                notes.Add(tImp, "A sudden jolt shook the camera with nothing approaching in front, so the impact came from the side or rear.", key: true);
                if (!stopped && !braking) notes.Add(tImp, "Without a view of the other party, fault can't be judged reliably.");
                if (braking) faultSecond += 0.3;
                plan.Highlight.Clear();
            }
            else
            {
                plan.Highlight.Add((tr, tr.Name));
                var ser = tr.Between(tImp - 4.0, tImp).ToList();
                var offs = ser.Select(p => (p.T, O: CorridorX(p.B))).ToList();
                var inPath = offs.Where(o => Math.Abs(o.O) <= 0.75).Select(o => o.T).ToList();
                double timeInPath = inPath.Count > 0 ? tImp - inPath[0] : 0;
                double seenFor = tImp - tr.FirstT;
                double lat = LateralRel(tr, tImp - 1.5, tImp - 0.1, camShift);
                string? enteredFrom = null;
                var outBefore = offs.Where(o => Math.Abs(o.O) > 1.0 && o.T < tImp - 0.2).ToList();
                if (outBefore.Count > 0 && inPath.Count > 0 && inPath[0] > outBefore[0].T)
                {
                    var lastOut = outBefore.Where(o => o.T < inPath[0] + 0.01).ToList();
                    if (lastOut.Count > 0) enteredFrom = lastOut[^1].O < 0 ? "left" : "right";
                }
                bool crossing = Math.Abs(lat) > 0.35 && tr.Kind is Kind.Vehicle or Kind.TwoWheeler;

                // Yellow centre line (right-hand traffic): it should stay on the camera vehicle's left.
                (double? Med, int Count) Yellow(double t0, double t1)
                {
                    var v = S.Where(s => s.T >= t0 && s.T <= t1 && s.YellowLine is not null).Select(s => s.YellowLine!.Value).ToList();
                    return v.Count == 0 ? (null, 0) : (CameraMotion.Median(v), v.Count);
                }
                var (ylEarly, _) = Yellow(tImp - 6, tImp - 2.0);
                var (ylLate, nLate) = Yellow(tImp - 1.8, tImp - 0.1);
                bool crossedCenter = ylLate is > 0.5 && nLate >= 1 && (ylEarly is null || ylEarly < 0.4);
                double drift = Enumerable.Range(0, n).Where(i => ts[i] > tImp - 3 && ts[i] <= tImp - 0.2).Sum(i => tx[i]);

                if (crossedCenter && tr.Kind is Kind.Vehicle or Kind.TwoWheeler)
                {
                    report.CollisionType = "Head-on (crossed the centre line)";
                    if (ylEarly is not null) notes.Add(tImp - 3.0, "The yellow centre line was on the camera vehicle's left, where it belongs.");
                    notes.Add(tImp - 1.0, "The camera vehicle drifted across the yellow centre line into the oncoming lane" + (drift > 0.02 ? " (steering left)" : "") + ".", key: true);
                    notes.Add(tImp, $"It collided head-on with the oncoming {tr.Name}.");
                    faultFirst += 0.95;
                }
                else if (tr.Kind == Kind.Person)
                {
                    report.CollisionType = "Pedestrian struck";
                    if (enteredFrom != null && inPath.Count > 0 && tImp - inPath[0] < 1.2)
                    {
                        notes.Add(inPath[0], $"The pedestrian stepped into the camera vehicle's path from the {enteredFrom} {tImp - inPath[0]:0.0} s before impact.", key: true);
                        faultFirst += 0.3; faultSecond += 0.4;
                    }
                    else
                    {
                        notes.Add(tImp - Math.Min(timeInPath, 4), $"The pedestrian was in the camera vehicle's path for at least {Math.Min(timeInPath, 4):0.0} s before the camera vehicle struck them.", key: true);
                        faultFirst += 0.7;
                    }
                }
                else if (crossing && (enteredFrom != null || timeInPath < 1.5))
                {
                    report.CollisionType = "Crossing paths (intersection / angle)";
                    string side = enteredFrom ?? (lat > 0 ? "left" : "right");
                    notes.Add(tImp - 1.0, $"The {tr.Name} crossed the camera vehicle's path from the {side}.", key: true);
                    string? light = LightState(S, tImp - 4, tImp - 0.5);
                    if (light == "red") { notes.Add(tImp - 2.0, "The traffic light ahead of the camera vehicle was red.", key: true); faultFirst += 0.8; }
                    else if (light == "green") { notes.Add(tImp - 2.0, "The traffic light ahead of the camera vehicle was green.", key: true); faultSecond += 0.7; }
                    else if (StopSignSeen(S, tImp - 5, tImp - 0.5) && !stopped && SpeedBetween(tImp - 4, tImp - 0.5) >= 0.3 * cruise)
                    { notes.Add(tImp - 2.0, "A stop sign was visible ahead and the camera vehicle didn't come to a stop.", key: true); faultFirst += 0.6; }
                    else notes.Add(tImp - 1.0, "No traffic light or stop sign could be read, so right of way is unclear.");
                }
                else if (enteredFrom != null && timeInPath < 2.5)
                {
                    report.CollisionType = "Lane change / cut-in";
                    notes.Add(inPath[0], $"The {tr.Name} moved into the camera vehicle's lane from the {enteredFrom} only {timeInPath:0.0} s before impact.", key: true);
                    faultSecond += 0.75;
                    if (timeInPath > 1.6 && !braking) { notes.Add(tImp - 0.5, "The camera vehicle didn't visibly slow down after it moved in."); faultFirst += 0.2; }
                }
                else
                {
                    report.CollisionType = "Rear-end";
                    double shown = Math.Min(Math.Min(timeInPath, seenFor), 4);
                    notes.Add(tImp - Math.Min(timeInPath, 4), $"The {tr.Name} was ahead of the camera vehicle, in its lane, for {shown:0.0}+ s before the impact.");
                    double? gLate = GrowthRate(tr, tImp - 1.0, tImp), gEarly = GrowthRate(tr, tImp - 3.0, tImp - 1.2);
                    if (!stopped)
                    {
                        notes.Add(tImp - 0.5, "The camera vehicle closed in on it and struck it from behind.", key: true);
                        faultFirst += 0.75;
                        if (gEarly is not null && gLate is not null && gLate > 3 * Math.Max(gEarly.Value, 0.05))
                        { notes.Add(tImp - 1.0, "The vehicle ahead slowed suddenly; the gap closed quickly."); faultFirst -= 0.15; }
                    }
                    else
                    {
                        notes.Add(tImp, "The vehicle ahead backed or rolled into the camera vehicle while it was stopped.", key: true);
                        faultSecond += 0.4;
                    }
                }
                hazardT = inPath.Count > 0 ? inPath[0] : null;
                rearEnd = report.CollisionType == "Rear-end";
                crossedLine = crossedCenter;
            }
        }
        else
        {
            var A = hit.A!; var B = hit.B!;
            report.Parties = [$"{A.Name} (A)", $"{B.Name} (B)"];
            plan.Highlight.Add((A, "A"));
            plan.Highlight.Add((B, "B"));
            report.CollisionType = "Other road users";
            notes.Add(tImp, $"The {A.Name} (A) and the {B.Name} (B) collided.");
            var va = Velocity(A, tImp - 1.5, tImp - 0.1, camShift); var vb = Velocity(B, tImp - 1.5, tImp - 0.1, camShift);
            double sa = va is { } x ? Math.Sqrt(x.X * x.X + x.Y * x.Y) / Math.Max(A.BoxAt(tImp).H, 1e-3) : 0;
            double sb = vb is { } y ? Math.Sqrt(y.X * y.X + y.Y * y.Y) / Math.Max(B.BoxAt(tImp).H, 1e-3) : 0;
            if (A.Kind == Kind.Person || B.Kind == Kind.Person)
            {
                var veh = A.Kind == Kind.Person ? B : A;
                report.CollisionType = "Other road users: pedestrian struck";
                notes.Add(tImp, $"A pedestrian was struck by the {veh.Name}.", key: true);
                if (veh == A) faultFirst += 0.5; else faultSecond += 0.5;
            }
            else
            {
                // Who did the closing? Project each one's motion onto the line joining them.
                var pa = Center(A, tImp - 0.6); var pb = Center(B, tImp - 0.6);
                double dx = pb.X - pa.X, dy = pb.Y - pa.Y, dn = Math.Max(Math.Sqrt(dx * dx + dy * dy), 1e-6);
                double ca = va is { } ua ? Math.Max(0, (ua.X * dx + ua.Y * dy) / dn / Math.Max(A.BoxAt(tImp).H, 1e-3)) : 0;
                double cb = vb is { } ub ? Math.Max(0, -(ub.X * dx + ub.Y * dy) / dn / Math.Max(B.BoxAt(tImp).H, 1e-3)) : 0;
                if (va is { } u1 && vb is { } u2)
                {
                    double ang = Angle(u1, u2);
                    report.CollisionType = ang < 35 ? "Other road users: rear-end" : ang > 145 ? "Other road users: head-on" : "Other road users: crossing paths";
                }
                if (Math.Min(sa, sb) < 0.15 && Math.Max(sa, sb) > 0.35)
                {
                    bool aMoves = sa > sb;
                    notes.Add(tImp - 1.0, $"The {(aMoves ? B : A).Name} was stopped or nearly stopped; the {(aMoves ? A : B).Name} ran into it.", key: true);
                    if (aMoves) faultFirst += 0.9; else faultSecond += 0.9;
                }
                else if (ca + cb > 0.1)
                {
                    double share = ca / (ca + cb), dom = Math.Max(share, 1 - share);
                    bool aStrikes = share >= 0.5;
                    Track striker = aStrikes ? A : B, struck = aStrikes ? B : A;
                    if (dom >= 0.7) notes.Add(tImp - 1.0, $"The {striker.Name} moved into the {struck.Name}: it did most of the closing before contact.", key: true);
                    else notes.Add(tImp - 1.0, $"Both the {A.Name} and the {B.Name} moved toward each other before contact.");
                    if (Math.Max(sa, sb) > 1.8 * Math.Max(Math.Min(sa, sb), 0.05))
                    {
                        var fast = sa > sb ? A : B;
                        notes.Add(tImp - 1.5, $"The {fast.Name} was moving much faster than the {(fast == A ? B : A).Name}.", key: true);
                    }
                    double add = 0.9 * (dom - 0.5) * 2 + (dom >= 0.7 ? 0.15 : 0);
                    if (aStrikes) faultFirst += add; else faultSecond += add;
                }
                else notes.Add(tImp - 1.0, "Their movements before contact couldn't be separated, so fault between them is unclear.");
            }
        }

        // ----- Verdict -----
        bool ego = hit.Kind != "pair";
        double detection = Math.Min(1, hit.Score);
        double net = faultFirst - faultSecond, margin = Math.Abs(net);
        if (margin < 0.25)
        {
            report.Verdict = Verdicts.Unclear;
            report.Confidence = (int)Math.Round(100 * detection * 0.5);
        }
        else
        {
            report.Verdict = net > 0 ? (ego ? Verdicts.Camera : Verdicts.A) : (ego ? Verdicts.Other : Verdicts.B);
            report.Confidence = (int)Math.Round(100 * detection * Math.Min(0.95, 0.45 + 0.5 * Math.Min(1, margin)));
        }
        int first = (int)Math.Round(100 / (1 + Math.Exp(-3 * net)));
        first = Math.Clamp(first, 5, 95);
        report.FaultShares = [first, 100 - first];
        report.Headline = report.Verdict switch
        {
            Verdicts.Camera => "The camera vehicle was most likely at fault",
            Verdicts.Other => hit.Other is { } o ? $"The {o.Name} was most likely at fault" : "The other party was most likely at fault",
            Verdicts.A => $"The {report.Parties[0]} was most likely at fault",
            Verdicts.B => $"The {report.Parties[1]} was most likely at fault",
            _ => "Fault is shared or unclear",
        };
        report.Summary = notes.Key.Count > 0 ? string.Join(" ", notes.Key.Take(2)) : notes.All.FirstOrDefault()?.Text ?? "";
        if (report.Verdict == Verdicts.Unclear && notes.Key.Count == 0)
            report.Summary = "The AI found the collision but the video doesn't show enough to say who caused it.";
        report.Findings = notes.All.OrderBy(f => f.Seconds).ToList();

        // Every road user, who was involved, where, what the camera driver did, and each party's share.
        Explain(new ExplainInput(report, S, ts, speed, cruise, tx, camShift, tracks, hit, cands, faultFirst, faultSecond,
            isFixed ? "fixed" : handheld ? "handheld" : "moving", hazardT, rearEnd, crossedLine), plan);

        // Key frames: before, just before, impact, after.
        foreach (var (k, cap) in new[] { (tImp - 2.0, "2 seconds before"), (tImp - 0.8, "Just before"), (tImp, "Impact"), (tImp + 0.8, "Just after") })
        {
            double t = ts.MinBy(x => Math.Abs(x - k));
            if (plan.Moments.All(m => Math.Abs(m.T - t) > 1e-6)) plan.Moments.Add((t, cap));
        }
        return ((report, plan), detection);
    }

    // ----- Geometry helpers -----

    /// <summary>Horizontal offset of a box's bottom-centre from the camera vehicle's path (0 = dead ahead, ±1 = edge of the lane).</summary>
    internal static double CorridorX(Box b)
    {
        double half = 0.12 + 0.30 * Math.Max(0, (b.Y2 - 0.45) / 0.55);
        return (b.Cx - 0.5) / half;
    }

    private static (double X, double Y)? Velocity(Track tr, double t0, double t1, Dictionary<double, (double X, double Y)> cam)
    {
        var ser = tr.Between(t0, t1).ToList();
        if (ser.Count < 2) return null;
        var (ta, a) = ser[0]; var (tb, b) = ser[^1];
        double cx = 0, cy = 0;
        foreach (var kv in cam) if (kv.Key > ta && kv.Key <= tb) { cx += kv.Value.X; cy += kv.Value.Y; }
        double span = Math.Max(1e-6, tb - ta);
        return ((b.Cx - a.Cx - cx) / span, (b.Y2 - a.Y2 - cy) / span);
    }

    private static double LateralRel(Track tr, double t0, double t1, Dictionary<double, (double X, double Y)> cam)
    {
        var v = Velocity(tr, t0, t1, cam);
        if (v is null) return 0;
        var widths = tr.Between(t0, t1).Select(p => p.B.W).ToList();
        double w = widths.Count > 0 ? CameraMotion.Median(widths) : 0.1;
        return v.Value.X / Math.Max(w, 0.02);
    }

    private static double? GrowthRate(Track tr, double t0, double t1)
    {
        var ser = tr.Between(t0, t1).ToList();
        if (ser.Count < 2) return null;
        return Math.Log(Math.Max(ser[^1].B.H, 1e-3) / Math.Max(ser[0].B.H, 1e-3)) / Math.Max(ser[^1].T - ser[0].T, 1e-3);
    }

    private static (double X, double Y) Center(Track tr, double t) { var b = tr.BoxAt(t); return (b.Cx, b.Y2); }

    private static double Angle((double X, double Y) u, (double X, double Y) v)
    {
        double nu = Math.Sqrt(u.X * u.X + u.Y * u.Y), nv = Math.Sqrt(v.X * v.X + v.Y * v.Y);
        if (nu < 1e-6 || nv < 1e-6) return 90;
        return Math.Acos(Math.Clamp((u.X * v.X + u.Y * v.Y) / nu / nv, -1, 1)) * 180 / Math.PI;
    }

    private static string? LightState(List<Sample> S, double t0, double t1)
    {
        var states = S.Where(s => s.T >= t0 && s.T <= t1)
            .SelectMany(s => s.Detections.Where(d => d.Kind == Kind.TrafficLight && d.Extra != null && Math.Abs(d.Box.Cx - 0.5) < 0.35 && d.Box.Y2 < 0.6))
            .Select(d => d.Extra!).ToList();
        if (states.Count < 2) return null;
        return states.GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key;
    }

    private static bool StopSignSeen(List<Sample> S, double t0, double t1) =>
        S.Count(s => s.T >= t0 && s.T <= t1 && s.Detections.Any(d => d.Kind == Kind.StopSign)) >= 2;

    internal static double[] Smooth(double[] a, int k)
    {
        if (a.Length < k) return (double[])a.Clone();
        int h = k / 2;
        var r = new double[a.Length];
        for (int i = 0; i < a.Length; i++)
        {
            double s = 0;
            for (int j = -h; j <= h; j++) s += a[Math.Clamp(i + j, 0, a.Length - 1)];
            r[i] = s / k;
        }
        return r;
    }

    internal static double[] RobustZ(double[] a)
    {
        double med = CameraMotion.Median(a);
        double mad = CameraMotion.Median(a.Select(x => Math.Abs(x - med))) * 1.4826 + 1e-9;
        return a.Select(x => (x - med) / mad).ToArray();
    }
}
