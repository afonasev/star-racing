using System;
using System.Collections.Generic;
using System.Linq;
namespace StarRacingPrototype.Procedural
{
public static class Validator
{
    public static string[] Validate(Definition d, bool quick = false)
    {
        var reasons = new List<string>();
        var s = d.samples;
        Action<bool, string> check = (bad, why) =>
        {
            if (bad)
                reasons.Add(why);
        };
        check(s.Length < 900, "track-too-short");
        if (d.version < 11) {
        check(d.patterns.Length < 2 || d.patterns.Length > 4, "pattern-count");
        check(d.patterns.Select(p => p.kind).Distinct().Count() != d.patterns.Length, "pattern-repeat");
        } else ValidateComposition(d, check);
        var wides = d.patterns.Where(p => p.kind == "wide-open").ToArray();
        check(wides.Length > 1, "wide-pattern-count");
        foreach (var p in wides)
        {
            var middle = s[(int)Math.Floor((p.startIndex + p.endIndex) * .5 + .5)];
            check(middle.halfWidth <= 7.5, "wide-pattern-width");
            check(d.guardrailMode != "full" && (middle.railLeft || middle.railRight), "wide-pattern-rails");
        }
        check(d.branches.Length < 2 || d.branches.Length > 4, "branch-count");
        check((d.guardrailMode == "normal" && (d.openRatio < .2 || d.openRatio > .4)) ||
                  (d.guardrailMode == "full" && d.openRatio != 0) ||
                  (d.guardrailMode == "none" && d.openRatio != 1),
              "open-ratio");
        // Keep the150..210s composition contract. The explicitly added native
        // landing straights have their own travel budget; no other segment shrinks.
        if(d.version>=10 && d.jumpModeEnabled) {
            var runways=Generator.Runs(s,"jump-straight");
            check(runways.Count!=4 || runways.Any(run=>run.length*5!=Generator.NativeJumpStraightLength),"native-runway-length");
        }
        double compositionDuration=d.estimatedDuration-Generator.AddedRunwayLength(d)/32.5;
        check(compositionDuration < 150 || compositionDuration > 210, "duration");
        check(d.checkpoints.Length < 10, "checkpoints");
        check(d.jumpModeEnabled && d.jumps.Length != 4, "jump-count");
        check(!d.jumpModeEnabled && d.jumps.Length != 0, "disabled-jumps");
        foreach (var j in d.jumps)
        {
            var launch = s[j.launchIndex];
            var landing = s[j.landingEndIndex];
            var ramp = s[j.rampStartIndex];
            if(d.version>=10) {
                int end=j.ballisticLandingEndIndex;
                var run=Generator.Runs(s,"jump-straight").Find(x=>j.launchIndex>=x.start && j.launchIndex<x.start+x.length);
                check(end<j.landingEndIndex || end>=s.Length || end-j.launchIndex<Generator.NativeBallisticLandingSamples ||
                    end>run.start+run.length-10 || end<0 || (end>=0 && end<s.Length && s[end].segmentIndex!=launch.segmentIndex),"native-ballistic-landing-"+j.id);
            }
            double time = (j.gapEndIndex - j.launchIndex) * 5 / 42.0;
            check(j.rampStartIndex >= j.launchIndex || j.launchIndex >= j.gapStartIndex ||
                      j.gapStartIndex >= j.gapEndIndex || j.gapEndIndex >= j.landingEndIndex ||
                      d.branches.Any(b => j.rampStartIndex - 7 <= b.endIndex &&
                                          j.landingEndIndex + 14 >= b.startIndex) ||
                      ramp.kind != "jump-straight" || landing.kind != "jump-straight" ||
                      ramp.segmentIndex == s[0].segmentIndex ||
                      landing.segmentIndex == s[s.Length - 1].segmentIndex ||
                      !Generator.Stable(s, j.rampStartIndex - 7, j.landingEndIndex) ||
                      9.5 * time - MagneticVehicle.GravityMagnitude * time * time / 2 < 0,
                  "invalid-" + j.id);
        }
        var straights = Generator.Runs(s, "straight");
        check(straights.Any(r => r.length * 5 > 160), "straight-length");
        check(straights.Any(r => r.start > 0 && r.start + r.length < s.Length &&
                                 !d.jumps.Any(j => j.rampStartIndex >= r.start &&
                                                   j.landingEndIndex < r.start + r.length)),
              "internal-straight");
        if (d.version < 11) {
        var loops = Generator.Runs(s, "loop");
        check(loops.Count < 3, "loop-count");
        check(!loops.Any(a => loops.Any(b => b.start > a.start &&
                                             Math.Abs(DVec.Dot(s[a.start].right, s[b.start].right)) < .86)),
              "loop-planes");
        var hairpins = Generator.Runs(s, "hairpin");
        check(hairpins.Count < 2, "hairpin-count");
        check(hairpins.Where((r, i) => i > 0 && r.start - hairpins[i - 1].start > 510).Any(),
              "hairpin-interval");
        var curves = Generator.Runs(s, "sweeper").Concat(Generator.Runs(s, "turn")).ToArray();
        check(curves.Length < 2, "long-curve-count");
        var drift = curves.Where(r => Generator.IsDrift(s, r)).ToArray();
        check(drift.Length < 2, "drift-curve-count");
        var tight =
            hairpins.Where(r => r.length * 5 >= 70 && r.length * 5 <= 120 && Generator.Turn(s, r) >= 150)
                .ToArray();
        check(tight.Length < 2, "tight-hairpin-count");
        var starts = drift.Concat(tight).Select(r => s[r.start].distance).OrderBy(x => x).ToArray();
        check(starts.Where((x, i) => i > 0 && x - starts[i - 1] < 180).Any(), "demanding-turn-gap");
        var rhythm =
            starts.Concat(d.jumps.Select(j => s[j.rampStartIndex].distance)).OrderBy(x => x).ToArray();
        check(!d.jumpModeEnabled &&
                  (rhythm.Length == 0 || rhythm[0] > 900 || d.totalLength - rhythm[rhythm.Length - 1] > 900 ||
                   rhythm.Where((x, i) => i > 0 && x - rhythm[i - 1] > 900).Any()),
              "demanding-turn-window");
        }
        if (d.version >= 9)
            foreach (var pattern in d.patterns)
            {
                if (pattern.kind.StartsWith("spiral-"))
                {
                    var first = s[pattern.startIndex];
                    DVec axis = pattern.kind == "spiral-vertical" ? first.normal : first.right;
                    double turn = 0;
                    for (int i = pattern.startIndex + 1; i <= pattern.endIndex; i++)
                    {
                        DVec a = (s[i - 1].tangent - axis * DVec.Dot(s[i - 1].tangent, axis)).Unit();
                        DVec b = (s[i].tangent - axis * DVec.Dot(s[i].tangent, axis)).Unit();
                        turn += Math.Atan2(DVec.Dot(axis, DVec.Cross(a, b)), DVec.Dot(a, b));
                    }
                    check(Math.Abs(Math.Abs(turn) - 4 * Math.PI) > .02, "native-spiral-turns");
                    check(Math.Abs(DVec.Dot(s[pattern.endIndex].position - first.position, axis)) < 70,
                          "native-spiral-separation");
                }
                if (pattern.kind == "chicane")
                {
                    int reversals = 0, previous = 0;
                    double totalTurn = 0;
                    for (int i = pattern.startIndex + 1; i <= pattern.endIndex; i++)
                    {
                        double angle =
                            Math.Atan2(DVec.Dot(s[i].normal, DVec.Cross(s[i - 1].tangent, s[i].tangent)),
                                       DVec.Dot(s[i - 1].tangent, s[i].tangent));
                        totalTurn += Math.Abs(angle);
                        if (Math.Abs(angle) < .0005)
                            continue;
                        int sign = Math.Sign(angle);
                        if (previous != 0 && sign != previous)
                            reversals++;
                        previous = sign;
                    }
                    check(reversals < 4 || totalTurn < Math.PI / 3, "native-chicane-reversals");
                }
            }
        if (quick && reasons.Count > 0)
            return reasons.Distinct().ToArray();
        check(!Support(d), "road-support");
        foreach (var b in d.branches)
        {
            if (b.endIndex <= b.startIndex || b.laneOffsets.Length < 2)
            {
                reasons.Add("invalid-" + b.id);
                continue;
            }
            var start = s[b.startIndex];
            var end = s[b.endIndex];
            check((start.kind != "turn" && start.kind != "sweeper" && start.kind != "hill") ||
                      end.kind != start.kind ||
                      DVec.Dot(s[b.startIndex - 2].tangent, s[b.startIndex + 2].tangent) < .998 ||
                      DVec.Dot(s[b.endIndex - 2].tangent, s[b.endIndex + 2].tangent) < .998 ||
                      DVec.Dot(s[b.startIndex - 2].normal, s[b.startIndex + 2].normal) < .998 ||
                      DVec.Dot(s[b.endIndex - 2].normal, s[b.endIndex + 2].normal) < .998,
                  "unsafe-window-" + b.id);
            for (int i = b.startIndex; i <= b.endIndex; i++)
            {
                var lanes = Layout.Lanes(b, s[i], d.guardrailMode);
                var spans = Layout.Merge(lanes);
                check(Layout.Rails(lanes).Any(
                          x => spans.Any(span => x > span.Left + .05 && x < span.Right - .05)),
                      "rail-inside-road-" + b.id);
            }
        }
        check(Intersects(d), "clearance");
        return reasons.Distinct().ToArray();
    }
    static void ValidateComposition(Definition d, Action<bool, string> check)
    {
        var s = d.samples;
        check(!Generator.DrivingFamilies.Contains(d.family), "composition-family");
        check(d.composition.Count(x => x.role == "main") != 1 ||
              d.composition.Count(x => x.role == "supporting") < 1 ||
              d.composition.Count(x => x.role == "supporting") > 2, "composition-roles");
        int end = -1;
        foreach (var section in d.composition) {
            bool bounds = section.startIndex == end + 1 && section.endIndex >= section.startIndex && section.endIndex < s.Length;
            check(!bounds, "composition-bounds");
            if (!bounds) continue;
            check(!new[] { "main", "supporting", "recovery" }.Contains(section.role), "composition-role-kind");
            var phrase = s.Skip(section.startIndex).Take(section.endIndex - section.startIndex + 1).ToArray();
            check(phrase.Any(x => x.compositionIndex != section.index || x.compositionRole != section.role), "composition-metadata");
            if (section.role == "main") {
                check(d.family == "speed" && !phrase.Any(x => x.patternKind == "long-arc"), "speed-main");
                check(d.family == "technical" && !phrase.Any(x => x.patternKind == "connected-three"), "technical-main");
                check(d.family == "vertical" && !phrase.Any(x => x.kind == "loop"), "vertical-main");
            }
            if (section.role != "recovery") {
                check(section.startIndex == 0 || s[section.startIndex - 1].compositionRole != "recovery", "composition-entry-recovery");
                check(section.endIndex == s.Length - 1 || s[section.endIndex + 1].compositionRole != "recovery", "composition-exit-recovery");
                check(phrase.Max(x => x.halfWidth) - phrase.First().halfWidth < 1.5 ||
                      Math.Abs(phrase.First().halfWidth - phrase.Last().halfWidth) > .001, "composition-width-phrase");
            }
            end = section.endIndex;
        }
        check(end != s.Length - 1, "composition-coverage");
        for (int i = 0; i < s.Length; i++) {
            var a = s[i];
            check(double.IsNaN(a.position.Squared) || double.IsInfinity(a.position.Squared) ||
                  double.IsNaN(a.halfWidth) || a.halfWidth < 7.5 || a.halfWidth > 12, "composition-finite-width");
            if (i == 0) continue;
            var b = s[i - 1];
            check(Math.Abs(a.halfWidth - b.halfWidth) > .2, "composition-width-slope");
            check(DVec.Dot(a.tangent, b.tangent) < Math.Cos(.12) ||
                  DVec.Dot(a.normal, b.normal) < Math.Cos(.14), "composition-curvature-step");
            check(a.distance <= b.distance || a.progress <= b.progress, "composition-progress");
        }
        for (int i = 1; i < d.checkpoints.Length; i++)
            check(d.checkpoints[i].sampleIndex <= d.checkpoints[i - 1].sampleIndex ||
                  d.checkpoints[i].distance <= d.checkpoints[i - 1].distance, "composition-checkpoints");
    }
    static bool Support(Definition d)
    {
        for (int i = 0; i < d.samples.Length - 1; i++)
        {
            var a = d.samples[i];
            var b = d.samples[i + 1];
            var pairs = Layout.Pairs(d, a, b);
            bool gap = d.guardrailMode != "full" &&
                       d.jumps.Any(j => (a.index >= j.gapStartIndex && a.index < j.gapEndIndex) ||
                                        (b.index >= j.gapStartIndex && b.index < j.gapEndIndex));
            if (pairs.Length == 0 && !gap)
                return false;
            foreach (var p in pairs)
                if (p.first.halfWidth < 1.5 || p.second.halfWidth < 1.5 ||
                    p.first.halfWidth + p.second.halfWidth - Math.Abs(p.second.offset - p.first.offset) < 2.9)
                    return false;
        }
        return true;
    }
    struct Envelope
    {
        public int index;
        public DVec center;
        public double radius;
    }
    public static bool Intersects(Definition d)
    {
        var entries = new List<Envelope>();
        for (int i = 0; i < d.samples.Length; i += 4)
        {
            var s = d.samples[i];
            var spans = Layout.Paved(d, s);
            double left = spans.Length == 0 ? 0 : spans.Min(p => p.Left),
                   right = spans.Length == 0 ? 0 : spans.Max(p => p.Right);
            entries.Add(new Envelope { index = i, center = s.position + s.right * ((left + right) * .5),
                                       radius = (right - left) * .5 });
        }
        entries.Sort((a, b) => a.center.x.CompareTo(b.center.x));
        for (int i = 0; i < entries.Count; i++)
        {
            var a = entries[i];
            for (int j = i + 1; j < entries.Count; j++)
            {
                var b = entries[j];
                if (b.center.x - a.center.x >= 40)
                    break;
                if (Math.Abs(a.index - b.index) < 24)
                    continue;
                double required = a.radius + b.radius + 2.5;
                if ((a.center - b.center).Squared < required * required)
                    return true;
            }
        }
        return false;
    }
}
}
