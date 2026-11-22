namespace Guardrail.CrashAi;

/// <summary>
/// The detailed half of the analysis: every road user and what it did, who was actually involved, where the collision
/// happened, how the camera vehicle was driven (speed, braking, reaction, following distance, lane keeping, phone use
/// when a cab camera is recorded) and each party's share of the fault.
/// </summary>
public static partial class Reasoner
{
    private sealed record ExplainInput(
        CrashReport Report, List<Sample> S, double[] Ts, double[] Speed, double Cruise, double[] Tx,
        Dictionary<double, (double X, double Y)> Cam, List<Track> Tracks, Candidate Hit, List<Candidate> Cands,
        double FaultFirst, double FaultSecond, string Camera, double? HazardT, bool RearEnd, bool CrossedLine);

    private static string Clock(double t)
    {
        t = Math.Max(0, t);
        int m = (int)(t / 60);
        return $"{m}:{t - 60 * m:00.0}";
    }

    private static void Explain(ExplainInput x, KeyPlan plan)
    {
        var report = x.Report; var hit = x.Hit; var ts = x.Ts;
        bool moving = x.Camera == "moving";
        double tImp = hit.T, cruise = Math.Max(x.Cruise, 1e-6);
        double EgoSpeed(double t0, double t1)
        {
            var v = Enumerable.Range(0, ts.Length).Where(i => ts[i] >= t0 && ts[i] <= t1).Select(i => x.Speed[i]).ToList();
            return v.Count == 0 ? 0 : CameraMotion.Median(v);
        }

        // ----- Who was involved -----
        var involved = new Dictionary<int, string>();
        bool cameraInvolved = hit.Kind != "pair";
        if (hit.Kind == "ego_front" && hit.Other != null) involved[hit.Other.Id] = "struck by the camera vehicle";
        if (hit.Kind == "pair") { involved[hit.A!.Id] = "collided"; involved[hit.B!.Id] = "collided"; }
        foreach (var k in x.Cands)
        {
            if (k == hit || k.Score < 0.45 || k.T < tImp - 0.5 || k.T > tImp + 3.0) continue;
            if (k.Kind == "pair" && (involved.Count == 0 || involved.ContainsKey(k.A!.Id) || involved.ContainsKey(k.B!.Id)))
            {
                involved.TryAdd(k.A!.Id, "hit in a follow-on collision");
                involved.TryAdd(k.B!.Id, "hit in a follow-on collision");
            }
            else if (k.Kind == "ego_front" && k.Other != null && moving)
            {
                cameraInvolved = true;
                involved.TryAdd(k.Other.Id, "struck by the camera vehicle");
            }
        }

        // ----- Every road user -----
        var users = new List<(RoadUser U, Track Tr)>();
        foreach (var tr in x.Tracks)
        {
            var hist = tr.History.ToList();
            bool inv = involved.ContainsKey(tr.Id);
            if (!inv && (hist.Count < 6 || hist[^1].Key - hist[0].Key < 1.0)) continue;
            double maxH = hist.Max(h => h.Value.H);
            if (!inv && maxH < (tr.Kind == Kind.Person ? 0.1 : 0.06)) continue;
            var cxs = hist.Select(h => CorridorX(h.Value)).ToList();
            double medCx = CameraMotion.Median(cxs);
            var u = new RoadUser
            {
                Label = tr.Name, Kind = tr.Kind switch { Kind.Person => "person", Kind.TwoWheeler => "two-wheeler", _ => "vehicle" },
                FirstSeconds = Math.Round(hist[0].Key, 1), LastSeconds = Math.Round(hist[^1].Key, 1),
                Involved = inv, Role = involved.GetValueOrDefault(tr.Id, "not involved"),
                Where = x.Camera == "fixed" ? "in view" : Math.Abs(medCx) <= 0.75 ? "ahead, in the camera vehicle's lane" : medCx < 0 ? "to the left of the camera vehicle" : "to the right of the camera vehicle",
            };
            if (moving && tr.Kind != Kind.Person)
            {
                double end = hist[0].Key < tImp - 0.6 ? Math.Min(hist[^1].Key, tImp - 0.3) : hist[^1].Key;
                if (GrowthRate(tr, hist[0].Key, end) is double g)
                    u.Notes.Add(g > 0.9 && medCx < -0.75 ? "oncoming"
                        : g > 0.25 ? "the camera vehicle was gaining on it (slower, stopped or parked)"
                        : g < -0.25 ? "pulling away (faster than the camera vehicle)"
                        : "keeping pace with the camera vehicle");
            }
            if (moving && cxs.Count >= 6)
                for (int i = 3; i < cxs.Count; i++)
                {
                    bool wasOut = true;
                    for (int j = Math.Max(0, i - 6); j < i - 2; j++) if (Math.Abs(cxs[j]) <= 0.95) { wasOut = false; break; }
                    if (Math.Abs(cxs[i]) <= 0.6 && wasOut)
                    {
                        u.Notes.Add($"moved into the camera vehicle's lane from the {(cxs[i - 3] < 0 ? "left" : "right")} at {Clock(hist[i].Key)}");
                        break;
                    }
                }
            users.Add((u, tr));
        }
        if (x.Camera == "fixed")
        {
            double SpeedIdx(Track tr)
            {
                var v = Velocity(tr, tr.FirstT, tr.LastSeen, x.Cam);
                double h = CameraMotion.Median(tr.History.Values.Select(b => b.H));
                return v is { } vv ? Math.Sqrt(vv.X * vv.X + vv.Y * vv.Y) / Math.Max(h, 1e-3) : 0;
            }
            var speeds = users.Where(p => p.Tr.Kind != Kind.Person).Select(p => (p, s: SpeedIdx(p.Tr))).ToList();
            var movingSpeeds = speeds.Where(s => s.s > 0.15).Select(s => s.s).ToList();
            double med = movingSpeeds.Count >= 3 ? CameraMotion.Median(movingSpeeds) : double.NaN;
            foreach (var (p, s) in speeds)
            {
                if (!double.IsNaN(med) && s > 2.0 * med && s > 0.6) p.U.Notes.Add("moving much faster than other traffic");
                if (s < 0.08) p.U.Notes.Add("stopped or parked");
            }
        }
        users = users.OrderBy(p => !p.U.Involved).ThenBy(p => p.U.FirstSeconds).ToList();
        int nv = 0, np = 0;
        foreach (var (u, _) in users) u.Tag = u.Kind == "person" ? $"P{++np}" : $"V{++nv}";
        report.RoadUsers = users.Select(p => p.U).ToList();
        string LabelOf(Track? tr)
        {
            if (tr == null) return "the other vehicle (not visible)";
            var u = users.FirstOrDefault(p => p.Tr == tr).U;
            return u != null ? $"{u.Tag} {u.Label}" : tr.Name;
        }
        for (int i = 0; i < plan.Highlight.Count; i++)
        {
            var (tr, label) = plan.Highlight[i];
            var u = users.FirstOrDefault(p => p.Tr == tr).U;
            if (u != null) plan.Highlight[i] = (tr, u.Tag);
        }

        // ----- Where -----
        report.Location = Where(hit, tImp, x.Camera);
        report.CameraInvolved = cameraInvolved && moving;

        // ----- The camera driver -----
        var driver = new List<Finding>();
        var issues = new List<(double W, string Text)>();
        void Drv(double t, string text) => driver.Add(new Finding(Math.Round(Math.Max(0, t), 2), text));
        (Track Tr, double LeftT)? cut = null;
        if (moving)
        {
            double vPre = EgoSpeed(tImp - 4.0, tImp - 1.5);
            bool stopped = vPre < Math.Max(0.0003, 0.1 * cruise);
            double? hazardT = x.HazardT;
            cut = x.RearEnd && hit.Other != null ? CutOut(hit.Other, x.Tracks, tImp) : null;
            if (cut != null) hazardT = cut.Value.LeftT;
            double? brakeT = null;
            for (int i = 0; i < ts.Length; i++)
                if (ts[i] >= tImp - 3.0 && ts[i] <= tImp - 0.05 && vPre > 0.0003 && x.Speed[i] < 0.7 * vPre) { brakeT = ts[i]; break; }

            if (stopped) Drv(tImp - 2.0, "The camera vehicle was stopped or barely moving before the collision.");
            else
            {
                double trend = EgoSpeed(tImp - 1.6, tImp - 0.9) / Math.Max(vPre, 1e-6);
                if (trend > 1.25) Drv(tImp - 1.5, "The camera vehicle was speeding up in the seconds before the collision.");
                else if (vPre / cruise > 1.35) Drv(tImp - 3.0, "The camera vehicle was travelling faster than it did for most of the video.");
                else Drv(tImp - 3.0, "The camera vehicle was travelling at a steady speed.");
            }
            if (cameraInvolved && !stopped)
            {
                if (brakeT is double bt)
                {
                    if (hazardT is double ht && bt >= ht)
                    {
                        double rt = bt - ht;
                        Drv(bt, $"It began slowing {tImp - bt:0.0} s before impact, about {rt:0.0} s after the hazard appeared" + (rt <= 1.5 ? " (a normal reaction time)." : " (a slow reaction)."));
                        if (rt > 1.5) issues.Add((0.25, $"slow reaction ({rt:0.0} s) to the hazard"));
                    }
                    else Drv(bt, $"It began slowing {tImp - bt:0.0} s before impact.");
                }
                else if (hazardT is double ht && tImp - ht >= 1.5)
                {
                    Drv(tImp - 0.5, $"No braking was seen before the impact, although the hazard was in the camera vehicle's path for {tImp - ht:0.0} s. That points to inattention or distraction.");
                    issues.Add((0.45, $"no braking with the hazard in view for {tImp - ht:0.0} s (possible distraction)"));
                }
                else Drv(tImp - 0.5, "No braking was seen before the impact; there was little time to react.");
            }
            if (cut is { } c)
                Drv(c.LeftT, $"The {c.Tr.Name} ahead moved out of the camera vehicle's lane at {Clock(c.LeftT)}, revealing the slower {hit.Other!.Name}; the camera vehicle had {tImp - c.LeftT:0.0} s to react.");
            // Following distance: how close (in seconds) the vehicle ahead came before the last moment.
            if (hit.Kind == "ego_front" && hit.Other is { Kind: Kind.Vehicle } lead && x.RearEnd)
            {
                double minTtc = double.MaxValue;
                for (double t0 = tImp - 4.0; t0 < tImp - 1.0; t0 += 0.25)
                    if (GrowthRate(lead, t0, t0 + 0.6) is double g && g > 0.02) minTtc = Math.Min(minTtc, 1.0 / g);
                if (minTtc < 2.0)
                {
                    Drv(tImp - 2.5, $"The camera vehicle was closing on the vehicle ahead quickly (as little as {minTtc:0.0} s from contact) before the final moment — following too closely for the speed.");
                    issues.Add((0.3, "following too closely"));
                }
            }
            // Speed compared with traffic: same-direction vehicles that pull away are faster than us.
            int pulling = users.Count(p => p.U.Kind == "vehicle" && p.U.Notes.Any(n => n.StartsWith("pulling away")));
            bool pacing = users.Any(p => p.U.Kind == "vehicle" && p.U.Notes.Contains("keeping pace with the camera vehicle"));
            if (!stopped && pulling >= 2) Drv(tImp - 3.0, "Other traffic was moving faster than the camera vehicle.");
            else if (!stopped && pacing) Drv(tImp - 3.0, "The camera vehicle was keeping pace with the traffic around it.");
            if (x.CrossedLine) issues.Add((0.9, "crossed the centre line"));
            // Weaving: the camera swinging left and right repeatedly before the collision.
            var pre = Enumerable.Range(0, ts.Length).Where(i => ts[i] > tImp - 6 && ts[i] < tImp - 0.5).Select(i => x.Tx[i]).ToArray();
            if (pre.Length > 10)
            {
                var sm = Smooth(pre, 5);
                var big = sm.Where(v => Math.Abs(v) > 0.006).ToList();
                int flips = 0;
                for (int i = 1; i < big.Count; i++) if (Math.Sign(big[i]) != Math.Sign(big[i - 1])) flips++;
                if (flips >= 3) { Drv(tImp - 4.0, "The camera vehicle weaved from side to side before the collision."); issues.Add((0.25, "weaving within or across lanes")); }
            }
            // The cab camera, when the recording has one.
            var cabins = x.S.Where(s => s.Cabin != null).ToList();
            if (cabins.Count >= 4)
            {
                var window = cabins.Where(s => s.T >= tImp - 10 && s.T <= tImp - 0.3).ToList();
                var withPhone = window.Where(s => s.Cabin!.Any(d => d.Kind == Kind.Phone && d.Score >= 0.4f)).ToList();
                if (window.Count >= 4 && withPhone.Count >= Math.Max(3, 0.2 * window.Count))
                {
                    report.CabinNotes.Add(new Finding(Math.Round(withPhone[0].T, 2),
                        $"The cab camera appears to show a phone in the driver's hand or at their ear in {withPhone.Count} of {window.Count} checks during the 10 seconds before the collision " +
                        $"({Clock(withPhone[0].T)} to {Clock(withPhone[^1].T)}). Watch the cab view to confirm."));
                    Drv(withPhone[^1].T, "The cab camera suggests the driver was handling a phone before the collision — possible distracted driving.");
                    issues.Add((0.4, "possible phone use before the collision (cab camera)"));
                }
                else if (window.Count >= 4)
                    report.CabinNotes.Add(new Finding(Math.Round(Math.Max(0, tImp - 10), 2), "The cab camera didn't show a phone in the driver's hand in the 10 seconds before the collision."));
            }
            else Drv(tImp, "The camera doesn't show the inside of the cab, so phone use or other distraction can only be judged from how the vehicle moved.");
            if (!cameraInvolved)
            {
                double after = EgoSpeed(tImp + 0.5, tImp + 4.0);
                Drv(tImp + 1.0, vPre > 0.0003 && after < 0.6 * vPre
                    ? "The camera vehicle was not involved and slowed down after the collision."
                    : "The camera vehicle was not involved in the collision.");
            }
        }
        else if (x.Camera == "fixed") Drv(tImp, "This is a fixed camera (CCTV or a parked vehicle), so the camera itself isn't a party to the collision.");
        else Drv(tImp, "This video was filmed by hand, so the camera isn't treated as a party to the collision.");
        report.DriverNotes = driver.OrderBy(d => d.Seconds).ToList();
        report.DriverIssues = issues.Select(i => i.Text).ToList();

        // ----- Fault shares -----
        var keyReasons = report.Findings.Select(f => f.Text).ToList();
        var shares = new List<PartyShare>();
        if (hit.Kind != "pair")
        {
            double e = Math.Max(0, x.FaultFirst), o = Math.Max(0, x.FaultSecond);
            double egoPct = e + o < 0.15 ? 50 : 100 * e / (e + o);
            double contrib = issues.Count > 0 ? issues.Max(i => i.W) : 0;
            if (egoPct < 100 && contrib > 0) egoPct = Math.Max(egoPct, Math.Min(50, 100 * contrib * 0.6));
            int ego = Round5(egoPct);
            var mine = new PartyShare { Party = "Camera vehicle", IsCamera = true, Reasons = issues.Select(i => i.Text).ToList() };
            if (cut is { } c && ego >= 60)
            {
                double react = tImp - c.LeftT;
                int cutPct = react < 1.5 ? 30 : react < 2.5 ? 20 : 10;
                mine.Percent = ego - cutPct;
                shares.Add(mine);
                shares.Add(new PartyShare { Party = LabelOf(c.Tr), Percent = cutPct, Reasons = [$"moved out of the camera vehicle's lane {react:0.0} s before the impact, leaving the camera vehicle little time to react to the slower vehicle it had been hiding"] });
                shares.Add(new PartyShare { Party = LabelOf(hit.Other), Percent = 100 - ego });
            }
            else
            {
                mine.Percent = ego;
                shares.Add(mine);
                shares.Add(new PartyShare { Party = LabelOf(hit.Other), Percent = 100 - ego });
            }
            if (ego >= 50) mine.Reasons.InsertRange(0, keyReasons.Take(2));
            else shares[^1].Reasons.AddRange(keyReasons.Take(2));
        }
        else
        {
            double a = Math.Max(0, x.FaultFirst), b = Math.Max(0, x.FaultSecond);
            int aPct = Round5(a + b < 0.15 ? 50 : 100 * a / (a + b));
            shares.Add(new PartyShare { Party = LabelOf(hit.A), Percent = aPct });
            shares.Add(new PartyShare { Party = LabelOf(hit.B), Percent = 100 - aPct });
            var lead = aPct >= 50 ? shares[0] : shares[1];
            lead.Reasons.AddRange(keyReasons.Where(r => !r.Contains(" collided.")).Take(3));
            foreach (var (u, tr) in users.Where(p => p.U.Involved && (p.Tr == hit.A || p.Tr == hit.B)))
            {
                var s = shares.First(sh => sh.Party == LabelOf(tr));
                s.Reasons.AddRange(u.Notes.Where(n => n.StartsWith("moving much faster") || n.StartsWith("moved into")));
            }
            // Other road users caught up in the collision carry no blame from what the video shows.
            foreach (var (u, tr) in users.Where(p => p.U.Involved && p.Tr != hit.A && p.Tr != hit.B))
                shares.Add(new PartyShare { Party = LabelOf(tr), Percent = 0, Reasons = [u.Role] });
            if (moving) shares.Add(new PartyShare { Party = "Camera vehicle", Percent = 0, IsCamera = true, Reasons = ["not involved in the collision"] });
        }
        report.Shares = shares;
        report.Parties = shares.Select(s => s.Party).ToList();
        report.FaultShares = shares.Select(s => s.Percent).ToList();
        var top = shares.OrderByDescending(s => s.Percent).First();
        if (report.Verdict != Verdicts.Unclear && top.Percent >= 55)
            report.Headline = top.IsCamera ? $"The camera vehicle was most likely at fault ({top.Percent}%)" : $"{top.Party} was most likely at fault ({top.Percent}%)";
        report.Narrative = Narrative(report, users.Select(p => p.U).ToList(), shares, x.Camera, issues.Select(i => i.Text).ToList());
    }

    private static int Round5(double v) => (int)(5 * Math.Round(Math.Clamp(v, 0, 100) / 5.0));

    /// <summary>A vehicle ahead moved out of the way and revealed a slower one ("cut-out"): that vehicle and when it left.</summary>
    private static (Track Tr, double LeftT)? CutOut(Track lead, List<Track> tracks, double tImp)
    {
        var lb = lead.Between(tImp - 3.5, tImp - 0.8).Select(p => p.B.Y2).ToList();
        double leadY = lb.Count > 0 ? CameraMotion.Median(lb) : 0;
        foreach (var tr in tracks)
        {
            if (tr == lead || tr.Kind != Kind.Vehicle) continue;
            var ser = tr.Between(tImp - 3.5, tImp - 0.2).ToList();
            if (ser.Count < 4) continue;
            var blocking = ser.Where(p => Math.Abs(CorridorX(p.B)) <= 0.75 && p.B.H >= 0.12 && p.T <= tImp - 0.8 && p.B.Y2 >= leadY - 0.02).Select(p => p.T).ToList();
            if (blocking.Count == 0) continue;
            var gone = ser.Where(p => p.T > blocking[^1] && Math.Abs(CorridorX(p.B)) > 1.0).Select(p => p.T).ToList();
            double? left = gone.Count > 0 ? gone[0] : tr.LastSeen < tImp - 0.3 ? tr.LastSeen + 0.1 : null;
            if (left is double l && l < tImp - 0.3) return (tr, l);
        }
        return null;
    }

    private static string Where(Candidate hit, double t, string camera)
    {
        if (hit.Kind == "ego_unseen") return "at the camera vehicle, from outside the camera's view (the side or rear)";
        var trs = hit.Kind == "ego_front" ? new[] { hit.Other! } : new[] { hit.A!, hit.B! };
        var boxes = trs.Where(tr => tr != null && tr.History.Count > 0).Select(tr => tr.BoxAt(t)).ToList();
        if (boxes.Count == 0) return "";
        double cx = boxes.Average(b => b.Cx), y = boxes.Average(b => b.Y2);
        if (camera == "fixed")
        {
            string side = cx < 0.36 ? "left side" : cx > 0.64 ? "right side" : "centre";
            string depth = y > 0.75 ? "near the camera" : y > 0.5 ? "mid-distance" : "far from the camera";
            return $"in the {side} of the camera view, {depth}";
        }
        if (hit.Kind == "ego_front")
        {
            double c = CorridorX(boxes[0]);
            return (Math.Abs(c) < 0.6 ? "directly ahead in the camera vehicle's lane" : $"ahead, at the {(c < 0 ? "left" : "right")} edge of the camera vehicle's lane") + ", at the front of the camera vehicle";
        }
        double off = boxes.Average(b => CorridorX(b));
        string lane = Math.Abs(off) <= 0.75 ? "in the camera vehicle's lane" : Math.Abs(off) <= 2.2 ? $"in the lane to the {(off < 0 ? "left" : "right")} of the camera vehicle" : $"off to the {(off < 0 ? "left" : "right")} (shoulder or far lane)";
        string dist = y > 0.75 ? "close to the camera" : y > 0.5 ? "a short distance ahead" : "well ahead";
        return $"{lane}, {dist}";
    }

    private static string Narrative(CrashReport r, List<RoadUser> users, List<PartyShare> shares, string camera, List<string> issues)
    {
        var parts = new List<string>();
        string what = r.CollisionType switch
        {
            "Rear-end" => "a rear-end collision",
            "Head-on (crossed the centre line)" => "a head-on collision",
            "Crossing paths (intersection / angle)" => "a side-impact (crossing) collision",
            "Lane change / cut-in" => "a collision after a vehicle cut in",
            "Pedestrian struck" => "a collision with a pedestrian",
            "Impact from outside the view" => "an impact from outside the camera's view",
            var s when s.StartsWith("Other road users") => "a collision between other road users",
            _ => "a collision",
        };
        parts.Add($"At {Clock(r.ImpactSeconds ?? 0)} the video shows {what}" + (r.Location.Length > 0 ? $", {r.Location}." : "."));
        var inv = users.Where(u => u.Involved).Select(u => $"{u.Tag} ({u.Label})").ToList();
        if (r.CameraInvolved) inv.Insert(0, "the camera vehicle");
        if (inv.Count > 0) parts.Add("Involved: " + string.Join(", ", inv) + ".");
        int others = users.Count(u => !u.Involved);
        if (others > 0) parts.Add($"{others} other road user{(others == 1 ? " was" : "s were")} in view but not involved.");
        var sorted = shares.OrderByDescending(s => s.Percent).ToList();
        static string Who(PartyShare s) => s.IsCamera ? "the camera vehicle" : s.Party;
        if (sorted.Count > 0 && sorted[0].Percent >= 65) parts.Add($"Most of the fault lies with {Who(sorted[0])} ({sorted[0].Percent}%).");
        else if (sorted.Count > 0) parts.Add("Fault looks shared: " + string.Join(", ", sorted.Where(s => s.Percent > 0).Select(s => $"{Who(s)} {s.Percent}%")) + ".");
        if (camera == "moving")
            parts.Add(r.CameraInvolved
                ? issues.Count > 0 ? "The camera vehicle's driving contributed: " + string.Join("; ", issues) + "." : "Nothing in the camera vehicle's own driving stood out as a cause."
                : "The camera vehicle was not part of the collision.");
        return string.Join(" ", parts);
    }
}
