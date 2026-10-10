using System;
using System.Collections.Generic;
using System.Linq;

namespace StarRacingPrototype.Procedural
{
/// <summary>Runtime port of src/track/generator.ts version 8. No Unity or vehicle state.</summary>
public static class Generator
{
    public const double NativeJumpStraightLength=480;
    public const int NativeBallisticLandingSamples=68; //340m, then>=50m before a turn.
    public static double AddedRunwayLength(Definition d) => d.version>=10 && d.jumpModeEnabled
        ?Math.Min(880,Runs(d.samples,"jump-straight").Sum(run=>Math.Min(220,Math.Max(0,run.length*5-260)))):0;

    sealed class Plan
    {
        public string kind, pattern;
        public double length, intensity;
        public Plan(string k, double l, double n)
        {
            kind = k;
            length = l;
            intensity = n;
        }
    }
    public struct Run
    {
        public int start, length;
        public Run(int s, int l)
        {
            start = s;
            length = l;
        }
    }
    static readonly string[] PatternKinds = { "spiral-horizontal", "spiral-vertical", "chicane",  "cascade",
                                              "false-apex",        "wave-crest",      "wide-open" };
    static Plan Drift(SeedRandom r) => new Plan("sweeper", 220, r.Pick(-1, 1) * .75);
    static Plan Hairpin(SeedRandom r) => new Plan("hairpin", 120, r.Pick(-1, 1) * (150.0 / 180));
    static Plan DeterministicDrift(int i) => new Plan("sweeper", 220, (i % 2 == 0 ? 1 : -1) * .75);
    static bool Replaceable(Plan s) =>
        s.kind == "hill" || s.kind == "esses" || s.kind == "turn" || s.kind == "sweeper";
    static bool Demanding(Plan s) => ((s.kind == "sweeper" || s.kind == "turn") && s.length >= 160 &&
                                      s.length <= 280 && Math.Abs(s.intensity) * 180 >= 110 &&
                                      Math.Abs(s.intensity) * 180 <= 160) ||
                                     (s.kind == "hairpin" && s.length >= 70 && s.length <= 120 &&
                                      Math.Abs(s.intensity) * 180 >= 150);
    static int PreviousReplacement(List<Plan> p, int before)
    {
        for (int i = before - 1; i >= 0; i--)
            if (Replaceable(p[i]))
            {
                p[i] = DeterministicDrift(i);
                return i;
            }
        return -1;
    }
    static List<Plan> MakePlan(SeedRandom r, bool jumps)
    {
        var p = new List<Plan> { new Plan("straight", 150, 0), Drift(r),
                                 jumps ? new Plan("jump-straight", 260, 0)
                                       : new Plan("esses", 260, r.Pick(-1, 1)),
                                 Drift(r), new Plan("loop", 420, r.Pick(-1, 1)) };
        if (jumps)
            p.Add(new Plan("jump-straight", 260, 0));
        p.Add(Hairpin(r));
        p.Add(new Plan("sweeper", 280, r.Pick(-.25, .25)));
        p.Add(new Plan("corkscrew", 410, r.Pick(-1.1, 1.1)));
        p.Add(Drift(r));
        p.Add(new Plan("hill", 260, .68));
        p.Add(new Plan("loop", 420, r.Pick(-1, 1)));
        p.Add(Hairpin(r));
        p.Add(new Plan("sweeper", 280, r.Pick(-.25, .25)));
        p.Add(new Plan("loop", 420, r.Pick(-1, 1)));
        if (jumps)
        {
            p.Add(new Plan("jump-straight", 260, 0));
            p.Add(new Plan("corkscrew", 320, r.Pick(-.7, .7)));
            p.Add(new Plan("jump-straight", 260, 0));
            p.Add(Drift(r));
            p.Add(new Plan("corkscrew", 320, r.Pick(-.7, .7)));
            p.Add(Drift(r));
        }
        var available = PatternKinds.Where(k => k != "wide-open" || r.Next() < .5).ToList();
        var selected = new List<string>();
        int wanted = Math.Min(available.Count, r.Int(2, 4));
        while (selected.Count < wanted && available.Count > 0)
        {
            int i = r.Int(0, available.Count - 1);
            selected.Add(available[i]);
            available.RemoveAt(i);
        }
        int sinceHairpin = 0;
        while (p.Sum(s => s.length) < 5600 + (jumps ? 1040 : 0) - 330)
        {
            string k =
                sinceHairpin >= 5 ? "hairpin" : r.Pick("hill", "sweeper", "esses", "turn", "corkscrew");
            double length = k == "esses"     ? r.Range(220, 280)
                            : k == "sweeper" ? r.Range(220, 310)
                                             : r.Range(250, 430);
            double n = k == "turn" || k == "sweeper" || k == "corkscrew" || k == "esses"
                           ? r.Pick(-1, 1) * r.Range(.32, .58)
                           : .65;
            p.Add(new Plan(k, length, n));
            sinceHairpin = k == "hairpin" ? 0 : sinceHairpin + 1;
        }
        int curves = p.Count(s => s.kind == "sweeper" || s.kind == "turn");
        for (int i = curves; i < 2; i++)
        {
            int at = p.FindIndex(s => s.kind == "hill" || s.kind == "esses");
            if (at < 0)
                break;
            p[at] = new Plan("sweeper", 290, at % 2 == 0 ? .52 : -.52);
        }
        int sign = 0;
        foreach (var s in p)
        {
            if (!(new[] { "turn", "sweeper", "chicane", "false-apex", "wide-open" }).Contains(s.kind) ||
                Math.Abs(s.intensity) < 1e-6)
                continue;
            if (sign != 0 && Math.Sign(s.intensity) == sign)
                s.intensity *= -1;
            sign = Math.Sign(s.intensity);
        }
        foreach (string profile in new[] { "drift", "hairpin" })
            while (p.Count(s => Demanding(s) &&
                                (profile == "hairpin" ? s.kind == "hairpin" : s.kind != "hairpin")) < 2)
            {
                int i = p.FindIndex(1, s => Replaceable(s));
                if (i < 0)
                    break;
                p[i] = profile == "drift" ? DeterministicDrift(i)
                                          : new Plan("hairpin", 120, (i % 2 == 0 ? 1 : -1) * (150.0 / 180));
            }
        double distance = 0, previous = double.NegativeInfinity;
        for (int i = 0; i < p.Count; i++)
        {
            var s = p[i];
            if (Demanding(s))
            {
                if (distance - previous > 900)
                {
                    int at = PreviousReplacement(p, i);
                    previous = at >= 0 ? p.Take(at).Sum(x => x.length) : distance;
                }
                else
                    previous = distance;
            }
            else if (s.kind == "jump-straight")
                previous = distance;
            else if (distance + s.length - previous > 900 && Replaceable(s))
            {
                p[i] = DeterministicDrift(i);
                previous = distance;
            }
            distance += p[i].length;
        }
        if (distance - previous > 900)
            PreviousReplacement(p, p.Count);
        var preferred = new[] { "loop", "corkscrew", "esses", "hill", "sweeper", "hill", "sweeper" };
        foreach (var pattern in selected)
        {
            int i = p.FindIndex(1, s => s.pattern == null &&
                                        s.kind == preferred[Array.IndexOf(PatternKinds, pattern)]);
            if (i < 0)
                i = p.FindIndex(1, s => s.pattern == null && Replaceable(s));
            if (i >= 0)
                p[i].pattern = pattern;
        }
        p.Add(new Plan("straight", 150, 0));
        return p;
    }
    static DVec Rates(Plan s, double t, double e, bool native = false)
    {
        double soft = Math.Pow(Math.Sin(t * Math.PI), 2) * 2, pi = Math.PI;
        if (native && s.pattern == "chicane")
            return new DVec(Math.Sin(t * pi * 6) * e * .04, 0, 0);
        if (native && s.pattern == "false-apex")
            return new DVec(s.intensity * pi * 5 * e / s.length, 0, 0);
        switch (s.kind)
        {
        case "turn":
            return new DVec(s.intensity * pi * .72 * 5 * e / s.length, 0, 0);
        case "sweeper":
            return new DVec(
                s.intensity * pi * 5 * e * (s.pattern == "false-apex" ? .5 + t * 1.1 : 1) / s.length, 0, 0);
        case "hairpin":
            return new DVec(s.intensity * pi * 5 * e / s.length, 0, 0);
        case "esses":
            return new DVec(Math.Sin(t * pi * 2) * s.intensity * .04, 0, 0);
        case "hill":
            return s.pattern == "cascade" || s.pattern == "wave-crest"
                       ? new DVec(Math.Sin(t * pi * 2) * .012, Math.Sin(t * pi * 4) * s.intensity * .028, 0)
                       : new DVec(0, Math.Sin(t * pi * 2) * s.intensity * .04, 0);
        case "loop":
            return new DVec(0, s.intensity * pi * 2 * 5 * e / s.length, 0);
        case "corkscrew":
            return new DVec(Math.Sin(t * pi * 2) * .012 * soft, Math.Cos(t * pi * 2) * .008 * soft,
                            s.intensity * pi * 2 * 5 * e / s.length);
        default:
            return default;
        }
    }
    static Sample[] BuildSamples(uint seed, bool jumps, bool native = false)
    {
        var r = new SeedRandom(seed);
        var plan = MakePlan(r, jumps);
        // Preserve plan selection/RNG; the approved runway is additional course.
        if(native && jumps)foreach(var section in plan)if(section.kind=="jump-straight")section.length=NativeJumpStraightLength;
        var list = new List<Sample>();
        DVec p = new DVec(0, 120, 0), tangent = new DVec(0, 0, 1), normal = new DVec(0, 1, 0),
             right = new DVec(-1, 0, 0);
        double distance = 0;
        for (int segment = 0; segment < plan.Count; segment++)
        {
            var s = plan[segment];
            var runwayRandom=new SeedRandom(seed ^ unchecked((uint)(segment+1)*0x9e3779b9u));
            int steps = Math.Max(2, (int)Math.Floor(s.length / 5 + .5));
            var weights = new double[steps];
            for (int i = 0; i < steps; i++)
            {
                double t = (double)i / (steps - 1);
                weights[i] = Layout.Smooth(0, .16, t) * Layout.Smooth(0, .16, 1 - t);
                if (native && s.pattern == "false-apex")
                    weights[i] *= Math.Pow(Math.Sin(t * Math.PI * 2), 2);
            }
            double norm = steps / Math.Max(1e-6, weights.Sum());
            bool spiral = native && (s.pattern == "spiral-horizontal" || s.pattern == "spiral-vertical");
            DVec initialT = tangent, initialN = normal,
                 axis = s.pattern == "spiral-vertical" ? normal : right;
            double theta = 0, q = 0;
            if (spiral)
            {
                double lo = 0, hi = 2;
                for (int iteration = 0; iteration < 48; iteration++)
                {
                    double mid = (lo + hi) * .5, advance = 0;
                    foreach (double w in weights)
                        advance += 5 * mid * w / Math.Sqrt(1 + mid * mid * w * w);
                    if (advance < 80)
                        lo = mid;
                    else
                        hi = mid;
                }
                q = (lo + hi) * .5;
            }
            for (int i = 0; i < steps; i++)
            {
                double t = (double)i / (steps - 1);
                DVec rates = spiral ? default : Rates(s, t, weights[i] * norm, native);
                if (rates.x != 0)
                {
                    tangent = tangent.Rotate(normal, rates.x).Unit();
                    right = DVec.Cross(tangent, normal).Unit(right);
                }
                if (rates.y != 0)
                {
                    tangent = tangent.Rotate(right, rates.y).Unit();
                    normal = DVec.Cross(right, tangent).Unit(normal);
                }
                if (rates.z != 0)
                {
                    normal = normal.Rotate(tangent, rates.z).Unit();
                    right = DVec.Cross(tangent, normal).Unit(right);
                }
                DVec st = tangent, sn = normal, sr = right;
                if (s.kind == "loop" && !spiral)
                {
                    st = (tangent + right * (s.intensity * .14 * Math.Pow(Math.Sin(t * Math.PI), 2))).Unit();
                    sr = DVec.Cross(st, normal).Unit(right);
                    sn = DVec.Cross(sr, st).Unit(normal);
                }
                if (spiral)
                {
                    theta += Math.Sign(s.intensity) * Math.PI * 4 * weights[i] * norm / steps;
                    st = (initialT.Rotate(axis, theta) + axis * (q * weights[i])).Unit();
                    sn = initialN.Rotate(axis, theta);
                    sr = DVec.Cross(st, sn).Unit(right);
                    sn = DVec.Cross(sr, st).Unit(normal);
                    tangent = st;
                    normal = sn;
                    right = sr;
                }
                bool hazard = s.kind == "loop" || s.kind == "hairpin" || Math.Abs(rates.x) > .006 ||
                              Math.Abs(rates.y) > .02;
                // Added runway samples do not advance the legacy rail RNG.
                var sampleRandom=native && s.kind=="jump-straight" && i>=52?runwayRandom:r;
                bool open = !hazard && sampleRandom.Next() < .38;
                list.Add(new Sample { index = list.Count, distance = distance, position = p, tangent = st,
                                      normal = sn, right = sr,
                                      halfWidth = s.pattern == "wide-open"
                                                      ? 7.5 * (1 + .7 * Math.Pow(Math.Sin(t * Math.PI), 2))
                                                  : s.kind == "straight" ? 8.5
                                                                         : 7.5,
                                      railLeft = !open || sampleRandom.Next() < .12, railRight = !open || sampleRandom.Next() < .12,
                                      kind = s.kind, patternKind = s.pattern, segmentIndex = segment });
                p += st * 5;
                distance += 5;
            }
        }
        double total = Math.Max(5, distance - 5);
        foreach (var s in list)
            s.progress = s.distance / total;
        return list.ToArray();
    }
    public static List<Run> Runs(Sample[] s, string kind)
    {
        var result = new List<Run>();
        int start = -1;
        for (int i = 0; i <= s.Length; i++)
        {
            bool match = i < s.Length && s[i].kind == kind;
            if (match && (start < 0 || s[i].segmentIndex == s[i - 1].segmentIndex))
            {
                if (start < 0)
                    start = i;
            }
            else
            {
                if (start >= 0)
                    result.Add(new Run(start, i - start));
                start = match ? i : -1;
            }
        }
        return result;
    }
    public static double Turn(Sample[] s, Run r) =>
        Math.Acos(Math.Max(-1,
                           Math.Min(1, DVec.Dot(s[r.start].tangent, s[r.start + r.length - 1].tangent)))) *
        180 / Math.PI;
    public static bool IsDrift(Sample[] s, Run r) => r.length * 5 >= 160 && r.length * 5 <= 280 &&
                                                     Turn(s, r) >= 110 && Turn(s, r) <= 160;
    static Branch[] Branches(Sample[] s, SeedRandom r)
    {
        var eligible = Runs(s, "sweeper")
                           .Concat(Runs(s, "hill"))
                           .Where(x => x.length >= 54 && x.length <= 80 && x.start > 70 &&
                                       x.start + x.length < s.Length - 50 && !IsDrift(s, x))
                           .Where(x => s.Skip(x.start).Take(x.length).All(a => a.patternKind == null))
                           .Select(x => new { run = x, order = r.Next() })
                           .OrderBy(x => x.order)
                           .ToArray();
        int count = Math.Min(r.Int(2, 4), eligible.Length);
        var result = new List<Branch>();
        foreach (var entry in eligible.Take(count).OrderBy(x => x.run.start))
        {
            var run = entry.run;
            int margin = r.Int(5, 7), lanes = r.Next() < .28 ? 3 : 2;
            double[] offsets = lanes == 3 ? new double[] { -12, 0, 12 } : new double[] { -7, 7 };
            int safe = lanes == 3 ? 1 : r.Int(0, lanes - 1), fast = safe == 0 ? 1 : 0;
            for (int i = 0; i < lanes; i++)
                if (i != safe && Math.Abs(offsets[i]) > Math.Abs(offsets[fast]))
                    fast = i;
            result.Add(new Branch { id = "branch-" + (result.Count + 1), startIndex = run.start + margin,
                                    endIndex = run.start + run.length - 1 - margin, laneOffsets = offsets,
                                    laneHalfWidth = lanes == 3 ? 3.6 : 4.2, safeLane = safe,
                                    fastLane = fast });
        }
        return result.ToArray();
    }
    static Pattern[] Patterns(Sample[] s)
    {
        var result = new List<Pattern>();
        for (int start = 0; start < s.Length; start++)
        {
            var sample = s[start];
            if (sample.patternKind == null)
                continue;
            int end = start;
            while (end + 1 < s.Length && s[end + 1].segmentIndex == sample.segmentIndex)
                end++;
            result.Add(new Pattern { id = "pattern-" + (result.Count + 1), kind = sample.patternKind,
                                     startIndex = start, endIndex = end,
                                     turns = sample.patternKind.StartsWith("spiral-") ? 2 : 0 });
            start = end;
        }
        return result.ToArray();
    }
    public static bool Stable(Sample[] s, int start, int end)
    {
        for (int i = start; i < end; i++)
            if (i < 0 || i + 1 >= s.Length || DVec.Dot(s[i].tangent, s[i + 1].tangent) < .999 ||
                DVec.Dot(s[i].normal, s[i + 1].normal) < .999)
                return false;
        return true;
    }
    static Jump[] Jumps(Sample[] s, Branch[] branches, bool enabled, bool native = false)
    {
        var result = new List<Jump>();
        if (!enabled)
            return result.ToArray();
        foreach (var run in Runs(s, "jump-straight")
                     .Where(x =>
                                x.length >= 37 && !branches.Any(b => x.start <= b.endIndex + 28 &&
                                                                     x.start + x.length >= b.startIndex - 28))
                     .OrderBy(x => x.start))
        {
            if (result.Count >= 4)
                break;
            int start = run.start + 14, launch = start + 4, gap = launch + 1, end = gap + 4,
                landing = end + 14;
            if (landing >= run.start + run.length || result.Any(j => start - j.landingEndIndex < 90) ||
                !Stable(s, run.start, landing))
                continue;
            bool mandatory = result.Count % 2 == 1;
            result.Add(
                new Jump { id = "jump-" + (result.Count + 1), kind = mandatory ? "mandatory" : "partial",
                           rampStartIndex = native ? start - 2 : start, launchIndex = launch,
                           gapStartIndex = gap, gapEndIndex = end, landingEndIndex = landing, ballisticLandingEndIndex=native?launch+NativeBallisticLandingSamples:0,
                           lateralCenter = mandatory ? 0 : -s[launch].halfWidth * (2.0 / 3),
                           lateralHalfWidth = mandatory ? s[launch].halfWidth : s[launch].halfWidth / 3 });
        }
        return result.ToArray();
    }
    static double Rails(Sample[] s, Branch[] branches, string mode, Pattern[] patterns)
    {
        if (mode != "normal")
        {
            foreach (var a in s)
                a.railLeft = a.railRight = mode == "full";
            return mode == "full" ? 0 : 1;
        }
        var protect = new HashSet<int>();
        foreach (var b in branches)
        {
            for (int i = b.startIndex - 4; i <= b.startIndex + 8; i++)
                protect.Add(i);
            for (int i = b.endIndex - 8; i <= b.endIndex + 8; i++)
                protect.Add(i);
        }
        var hazardous = new HashSet<int>();
        for (int i = 0; i < s.Length; i++)
        {
            var a = s[i];
            bool sharp = (i > 0 && DVec.Dot(s[i - 1].tangent, a.tangent) < .9992) ||
                         (i + 1 < s.Length && DVec.Dot(a.tangent, s[i + 1].tangent) < .9992);
            if (a.kind == "loop" || a.kind == "corkscrew" || a.kind == "hairpin" || sharp ||
                Math.Abs(a.normal.y) < .32)
                hazardous.Add(a.segmentIndex);
        }
        for (int i = 0; i < s.Length; i++)
            if (hazardous.Contains(s[i].segmentIndex))
                for (int j = Math.Max(0, i - 5); j <= Math.Min(s.Length - 1, i + 5); j++)
                    protect.Add(j);
        Func<Sample, bool> hazard = a => a.segmentIndex == s[0].segmentIndex ||
                                         a.segmentIndex == s[s.Length - 1].segmentIndex || a.kind == "loop" ||
                                         a.kind == "corkscrew" || hazardous.Contains(a.segmentIndex) ||
                                         protect.Contains(a.index) || Math.Abs(a.normal.y) < .32;
        int open = 0;
        foreach (var a in s)
        {
            if (hazard(a))
                a.railLeft = a.railRight = true;
            else if (!a.railLeft && !a.railRight)
                open++;
        }
        int target = (int)Math.Floor(s.Length * .25 + .5);
        foreach (bool patternOnly in new[] { true, false })
            foreach (var a in s)
            {
                if (open >= target)
                    break;
                if (!hazard(a) && (!patternOnly || a.index % 5 < 3) && (a.railLeft || a.railRight))
                {
                    a.railLeft = a.railRight = false;
                    open++;
                }
            }
        foreach (var p in patterns)
            if (p.kind == "wide-open")
                for (int i = p.startIndex + 3; i <= p.endIndex - 3; i++)
                    s[i].railLeft = s[i].railRight = false;
        return (double)s.Count(a => !a.railLeft && !a.railRight) / s.Length;
    }
    static Checkpoint[] Checkpoints(Sample[] s)
    {
        var result = new List<Checkpoint>();
        for (int i = 0; i < s.Length; i += 60)
            AddCheckpoint(result, s[i]);
        if (result[result.Count - 1].sampleIndex != s.Length - 1)
            AddCheckpoint(result, s[s.Length - 1]);
        for (int i = 0; i < result.Count; i++)
            for (int j = result[i].sampleIndex;
                 j < (i + 1 < result.Count ? result[i + 1].sampleIndex : s.Length); j++)
                s[j].checkpointIndex = i;
        return result.ToArray();
    }
    static void AddCheckpoint(List<Checkpoint> c, Sample s) => c.Add(new Checkpoint {
        index = c.Count, sampleIndex = s.index, distance = s.distance, position = s.position,
        tangent = s.tangent, normal = s.normal, right = s.right
    });
    public static Definition Candidate(uint seed, string mode, string theme, bool jumps, bool native = false)
    {
        var samples = BuildSamples(seed, jumps, native);
        var patterns = Patterns(samples);
        var branches = Branches(samples, new SeedRandom(seed ^ 0xa5a5a5a5));
        return new Definition { version = native ? 10 : 8,
                                seed = seed,
                                guardrailMode = mode,
                                theme = theme,
                                jumpModeEnabled = jumps,
                                samples = samples,
                                patterns = patterns,
                                branches = branches,
                                jumps = Jumps(samples, branches, jumps, native),
                                openRatio = Rails(samples, branches, mode, patterns),
                                checkpoints = Checkpoints(samples),
                                totalLength = samples[samples.Length - 1].distance,
                                estimatedDuration = samples[samples.Length - 1].distance / 32.5 };
    }
    public static Definition Generate(uint requested, string mode = "normal", string theme = "cloud-city",
                                      bool jumps = false, bool native = false)
    {
        if (!new[] { "normal", "full", "none" }.Contains(mode) ||
            !new[] { "cloud-city", "space-station" }.Contains(theme))
            throw new ArgumentException("Unsupported track settings");
        uint themed = theme == "space-station" ? requested ^ 0x5a710a1eu : requested;
        return GenerateCandidates(requested, mode, theme, jumps, native, themed, 0);
    }
    // The fallback path is also independently callable by the acceptance suite.
    public static Definition GenerateFallback(uint requested, string mode, string theme, bool jumps,
                                              bool native = false)
    {
        return GenerateCandidates(requested, mode, theme, jumps, native, 0, 1);
    }
    static Definition GenerateCandidates(uint requested, string mode, string theme, bool jumps, bool native,
                                         uint themed, int firstPhase)
    {
        string[] reasons = Array.Empty<string>();
        for (int phase = firstPhase; phase < 2; phase++)
        {
            uint basis = phase == 0
                             ? themed
                             : (jumps || theme != "space-station" ? 0x51a7faceu : 0x51a7faceu ^ 0x5a710a1eu);
            for (int attempt = 0; attempt < (phase == 0 ? 32 : 384); attempt++)
            {
                uint seed = unchecked(basis + (uint)attempt * 0x9e3779b9);
                var normal = Candidate(seed, "normal", theme, jumps, native);
                reasons = Validator.Validate(normal, true);
                if (reasons.Length > 0)
                    continue;
                var d = mode == "normal" ? normal : Candidate(seed, mode, theme, jumps, native);
                d.requestedSeed = requested;
                d.usedFallback = phase == 1;
                d.hash = Hash(d);
                return d;
            }
        }
        throw new InvalidOperationException("No valid fallback: " + string.Join(",", reasons));
    }
    static string Hash(Definition d)
    {
        var values = new List<double>();
        foreach (var s in d.samples)
            values.AddRange(new[] { s.position.x, s.position.y, s.position.z, s.halfWidth });
        foreach (var j in d.jumps)
            values.AddRange(new[] { (double)j.rampStartIndex, j.launchIndex, j.gapStartIndex, j.gapEndIndex,
                                    j.lateralCenter, j.lateralHalfWidth });
        if(d.version>=10)foreach(var jump in d.jumps)values.Add(jump.ballisticLandingEndIndex);
        foreach (var p in d.patterns)
            values.AddRange(
                new[] { (double)p.startIndex, p.endIndex, p.turns, Array.IndexOf(PatternKinds, p.kind) });
        if (d.theme == "space-station")
            values.Add(1);
        if (d.jumpModeEnabled)
            values.Add(1);
        if (d.version != 8)
            values.Add(d.version);
        uint h = 2166136261;
        unchecked
        {
            foreach (var v in values)
            {
                h ^= (uint)(long)Math.Floor(v * 1000 + .5);
                h *= 16777619;
            }
        }
        return h.ToString("x8");
    }
}
}
