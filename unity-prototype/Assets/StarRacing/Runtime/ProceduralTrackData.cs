using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StarRacingPrototype.Procedural
{
// Source-space doubles deliberately retain the browser's left-facing lateral axis.
// Conversion to Unity's vehicle-right axis belongs exclusively to the runtime adapter.
[Serializable]
public struct DVec
{
    public double x, y, z;
    public DVec(double x, double y, double z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }
    public static DVec operator +(DVec a, DVec b) => new DVec(a.x + b.x, a.y + b.y, a.z + b.z);
    public static DVec operator -(DVec a, DVec b) => new DVec(a.x - b.x, a.y - b.y, a.z - b.z);
    public static DVec operator *(DVec a, double b) => new DVec(a.x * b, a.y *b, a.z *b);
    public double Squared => x * x + y * y + z * z;
    public static double Dot(DVec a, DVec b) => a.x * b.x + a.y * b.y + a.z * b.z;
    public static DVec Cross(DVec a, DVec b) => new DVec(a.y * b.z - a.z * b.y, a.z *b.x - a.x * b.z,
                                                         a.x *b.y - a.y * b.x);
    public DVec Unit(DVec fallback = default)
    {
        double n = Math.Sqrt(Squared);
        return n < 1e-8 ? (fallback.Squared == 0 ? new DVec(0, 0, 1) : fallback) : this * (1 / n);
    }
    public DVec Rotate(DVec axis, double angle)
    {
        axis = axis.Unit();
        double c = Math.Cos(angle), s = Math.Sin(angle);
        return this * c + Cross(axis, this) * s + axis * (Dot(axis, this) * (1 - c));
    }
    public Vector3 Float => new Vector3((float)x, (float)y, (float)z);
}
public sealed class SeedRandom
{
    uint state;
    public SeedRandom(uint seed)
    {
        state = seed == 0 ? 0x9e3779b9 : seed;
    }
    public double Next()
    {
        unchecked
        {
            uint v = (state += 0x6d2b79f5);
            v = (v ^ (v >> 15)) * (v | 1);
            v ^= v + (v ^ (v >> 7)) * (v | 61);
            return (v ^ (v >> 14)) / 4294967296.0;
        }
    }
    public double Range(double min, double max) => min + (max - min) * Next();
    public int Int(int min, int max) => (int)Math.Floor(Range(min, max + 1));
    public T Pick<T>(params T[] values) =>
        values[Math.Min(values.Length - 1, (int)Math.Floor(Next() * values.Length))];
}
[Serializable]
public sealed class Sample
{
    public int index, segmentIndex, checkpointIndex;
    public double distance, progress, halfWidth;
    public DVec position, tangent, normal, right;
    public bool railLeft, railRight;
    public string kind, patternKind;
}
[Serializable]
public sealed class Branch
{
    public string id;
    public int startIndex, endIndex, safeLane, fastLane;
    public double[] laneOffsets;
    public double laneHalfWidth;
}
[Serializable]
public sealed class Jump
{
    public string id, kind;
    public int rampStartIndex, launchIndex, gapStartIndex, gapEndIndex, landingEndIndex;
    // Native revision10 flight envelope; landingEndIndex remains the AI tactical boundary.
    public int ballisticLandingEndIndex;
    public double lateralCenter, lateralHalfWidth;
}
[Serializable]
public sealed class Pattern
{
    public string id, kind;
    public int startIndex, endIndex, turns;
}
[Serializable]
public sealed class Checkpoint
{
    public int index, sampleIndex;
    public double distance;
    public DVec position, tangent, normal, right;
}
[Serializable]
public sealed class Definition
{
    public uint requestedSeed, seed;
    public int version = 8;
    public string guardrailMode, theme, hash;
    public bool jumpModeEnabled, usedFallback;
    public Sample[] samples;
    public Branch[] branches;
    public Jump[] jumps;
    public Pattern[] patterns;
    public Checkpoint[] checkpoints;
    public double totalLength, openRatio, estimatedDuration;
}
public struct Span
{
    public double offset, halfWidth;
    public bool railLeft, railRight;
    public double Left => offset - halfWidth;
    public double Right => offset + halfWidth;
    public Span(double center, double half, bool left = false, bool right = false)
    {
        offset = center;
        halfWidth = half;
        railLeft = left;
        railRight = right;
    }
}
public struct SpanPair
{
    public Span first, second;
    public SpanPair(Span a, Span b)
    {
        first = a;
        second = b;
    }
}
public static class Layout
{
    public static double Smooth(double min, double max, double value)
    {
        double t = Math.Max(0, Math.Min(1, (value - min) / Math.Max(1e-8, max - min)));
        return t * t * (3 - 2 * t);
    }
    public static double Blend(Branch b, double i)
    {
        int transition = Math.Min(22, Math.Max(16, (int)Math.Floor((b.endIndex - b.startIndex) * .36)));
        return i < b.startIndex + transition ? Smooth(b.startIndex, b.startIndex + transition, i)
               : i > b.endIndex - transition ? 1 - Smooth(b.endIndex - transition, b.endIndex, i)
                                             : 1;
    }
    public static Span[] Lanes(Definition d, Sample s)
    {
        var b = Array.Find(d.branches, x => s.index >= x.startIndex && s.index <= x.endIndex);
        return b == null ? new[] { new Span(0, s.halfWidth, s.railLeft, s.railRight) }
                         : Lanes(b, s, d.guardrailMode);
    }
    public static Span[] Lanes(Branch b, Sample s, string mode)
    {
        double blend = Blend(b, s.index);
        var lanes = new Span[b.laneOffsets.Length];
        for (int i = 0; i < lanes.Length; i++)
        {
            double offset = b.laneOffsets[i], half = b.laneHalfWidth + (i == b.safeLane   ? .7
                                                                        : i == b.fastLane ? -.55
                                                                                          : 0);
            lanes[i] = new Span(offset * blend, half + (1 - blend) * (s.halfWidth - half),
                                i == 0 || (i == b.safeLane || (i == b.fastLane ? offset > 0 : s.railLeft)),
                                i == lanes.Length - 1 ||
                                    (i == b.safeLane || (i == b.fastLane ? offset < 0 : s.railRight)));
        }
        var spans = Merge(lanes);
        double left = spans[0].Left, right = spans[spans.Length - 1].Right;
        for (int i = 0; i < lanes.Length; i++)
        {
            var l = lanes[i];
            bool outerLeft = Math.Abs(l.Left - left) < .05, outerRight = Math.Abs(l.Right - right) < .05;
            if (mode == "none")
            {
                l.railLeft = false;
                l.railRight = false;
            }
            else if (mode == "normal")
            {
                l.railLeft = outerLeft && s.railLeft;
                l.railRight = outerRight && s.railRight;
            }
            else
            {
                l.railLeft = spans.Any(p => Math.Abs(p.Left - l.Left) < .05) &&
                             (outerLeft || (l.railLeft && (i == 0 || l.Left - lanes[i - 1].Right >= 3.5)));
                l.railRight = spans.Any(p => Math.Abs(p.Right - l.Right) < .05) &&
                              (outerRight || (l.railRight &&
                                              (i == lanes.Length - 1 || lanes[i + 1].Left - l.Right >= 3.5)));
            }
            lanes[i] = l;
        }
        return lanes;
    }
    public static Span[] Merge(IEnumerable<Span> lanes)
    {
        var sorted = lanes.OrderBy(s => s.Left);
        var result = new List<Span>();
        foreach (var span in sorted)
        {
            if (result.Count == 0 || span.Left >= result[result.Count - 1].Right + 3.5)
                result.Add(new Span(span.offset, span.halfWidth));
            else
            {
                var last = result[result.Count - 1];
                double r = Math.Max(last.Right, span.Right);
                result[result.Count - 1] = new Span((last.Left + r) * .5, (r - last.Left) * .5);
            }
        }
        return result.ToArray();
    }
    public static double[] Rails(Span[] lanes)
    {
        var r = new List<double>();
        foreach (var s in lanes)
        {
            if (s.railLeft && !r.Any(x => Math.Abs(x - s.Left) < .05))
                r.Add(s.Left);
            if (s.railRight && !r.Any(x => Math.Abs(x - s.Right) < .05))
                r.Add(s.Right);
        }
        return r.ToArray();
    }
    public static Span[] Paved(Definition d, Sample s)
    {
        var spans = Merge(Lanes(d, s));
        var jump = Array.Find(d.jumps, j => s.index >= j.gapStartIndex && s.index < j.gapEndIndex);
        if (jump == null || d.guardrailMode == "full")
            return spans;
        if (jump.kind == "mandatory")
            return Array.Empty<Span>();
        var kept = new List<Span>();
        double left = jump.lateralCenter - jump.lateralHalfWidth,
               right = jump.lateralCenter + jump.lateralHalfWidth;
        foreach (var span in spans)
        {
            if (left - span.Left > .1)
            {
                double r = Math.Min(left, span.Right);
                kept.Add(new Span((span.Left + r) * .5, (r - span.Left) * .5));
            }
            if (span.Right - right > .1)
            {
                double l = Math.Max(right, span.Left);
                kept.Add(new Span((l + span.Right) * .5, (span.Right - l) * .5));
            }
        }
        return kept.ToArray();
    }
    public static SpanPair[] Pairs(Definition d, Sample a, Sample b)
    {
        var first = Paved(d, a);
        var second = Paved(d, b);
        if (first.Length == 0 || second.Length == 0)
            return Array.Empty<SpanPair>();
        var result = new List<SpanPair>();
        if (first.Length == second.Length)
        {
            for (int i = 0; i < first.Length; i++)
                result.Add(new SpanPair(first[i], second[i]));
        }
        else if (first.Length < second.Length)
        {
            foreach (var s in second)
                result.Add(new SpanPair(Fit(s, first), s));
        }
        else
            foreach (var s in first)
                result.Add(new SpanPair(s, Fit(s, second)));
        return result.ToArray();
    }
    static Span Fit(Span target, Span[] sources)
    {
        var source = sources.OrderBy(s => Math.Abs(s.offset - target.offset)).First();
        double left = Math.Max(source.Left, target.Left), right = Math.Min(source.Right, target.Right);
        return right - left > .1 ? new Span((left + right) * .5, (right - left) * .5)
                                 : new Span(target.offset < source.offset ? source.Left : source.Right, .05);
    }
}
}
