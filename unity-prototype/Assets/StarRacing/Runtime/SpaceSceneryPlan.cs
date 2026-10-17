using System;
using System.Collections.Generic;
using UnityEngine;

namespace StarRacingPrototype {
    // Pure presentation data. All variants, rotations and complete envelopes are part of the identity.
    public sealed class SpaceSceneryPlan {
        public const int Version = 2;
        public const int MaximumAsteroids = 210, MaximumStations = 18;
        public enum Kind { Research, Modular, Shipyard, Ring, Asteroid }
        public struct Item {
            public Kind kind;
            public Vector3 position;
            public Quaternion rotation;
            public float size;
            public int variant;
            // Models are authored inside a unit sphere. This cube encloses every rotated vertex.
            public Bounds Envelope => new Bounds(position, Vector3.one * (size * 2));
        }
        public readonly List<Item> items = new List<Item>();
        public readonly Bounds bounds;
        public readonly string hash;

        public SpaceSceneryPlan(uint seed, TrackFrame[] frames, Vector3 station, Func<Vector3, Vector3, float, bool> clear) {
            var random = new Procedural.SeedRandom(seed ^ 0xa57e201bu);
            // Keep the approved near-station anchor; the new complete module model fits inside 150m.
            Add(Kind.Modular, station, Quaternion.Euler(12, 25, -8), 145, 0);
            var start = frames[0];
            Vector3 forward = start.tangent.normalized, right = start.right.normalized;
            // Two architectural landmarks, visible at racing scale. Search outward, never shrink or force.
            Landmark(Kind.Ring, 480, 1120, 1);
            Landmark(Kind.Shipyard, 290, 920, -1);
            var anchors = new List<TrackFrame>();
            foreach (var frame in frames)
                if (anchors.Count == 0 || Vector3.Distance(frame.position, anchors[anchors.Count - 1].position) > 120)
                    anchors.Add(frame);
            // Shuffle spreads the budget over the entire course rather than filling only the beginning.
            for (int i = anchors.Count - 1; i > 0; i--) { int j = random.Int(0, i); var temp = anchors[i]; anchors[i] = anchors[j]; anchors[j] = temp; }
            int stations = items.Count;
            for (int pass = 0; pass < 4 && stations < MaximumStations; pass++) foreach (var frame in anchors) {
                if (stations >= MaximumStations) break;
                float size = (float)random.Range(35, 115), side = random.Next() < .5 ? -1 : 1;
                var p = frame.position + frame.right * side * (size + (float)random.Range(100, 380)) + frame.normal * (float)random.Range(-65, 150);
                var kind = stations % 5 == 0 ? Kind.Ring : stations % 3 == 0 ? Kind.Modular : Kind.Research;
                if (Add(kind, p, Quaternion.Euler((float)random.Range(-25, 25), (float)random.Range(-180, 180), (float)random.Range(-35, 35)), size, stations % 4)) stations++;
            }
            int rocks = 0;
            // Large foreground rocks first, followed by middle and small distance layers.
            for (int pass = 0; pass < 9 && rocks < MaximumAsteroids; pass++) foreach (var frame in anchors) {
                if (rocks >= MaximumAsteroids) break;
                float size = pass == 0 ? (float)random.Range(95, 230) : pass < 3 ? (float)random.Range(22, 75) : (float)random.Range(5, 30);
                float side = random.Next() < .5 ? -1 : 1;
                float lateral = size + (float)random.Range(pass == 0 ? 130 : 68, pass < 3 ? 520 : 900);
                var position = frame.position + frame.right * (side * lateral) + frame.normal * (float)random.Range(-170, 300) + frame.tangent * (float)random.Range(-80, 80);
                if (Add(Kind.Asteroid, position, Quaternion.Euler((float)random.Range(-180, 180), (float)random.Range(-180, 180), (float)random.Range(-180, 180)), size, random.Int(0, 11))) rocks++;
            }
            var total = new Bounds(frames[0].position, Vector3.zero);
            foreach (var item in items) total.Encapsulate(item.Envelope);
            // Leave a metre for Bounds min/max floating-point reconstruction at distant coordinates.
            total.Expand(2f); bounds = total;
            unchecked {
                uint value = 2166136261;
                void Hash(int n) { value = (value ^ (uint)n) * 16777619; }
                Hash(Version); Hash((int)seed);
                foreach (var item in items) {
                    Hash((int)item.kind); Hash(item.variant); Hash(Mathf.RoundToInt(item.size * 100));
                    for (int axis = 0; axis < 3; axis++) Hash(Mathf.RoundToInt(item.position[axis] * 100));
                    for (int axis = 0; axis < 4; axis++) Hash(Mathf.RoundToInt(item.rotation[axis] * 100000));
                }
                hash = value.ToString("x8");
            }
            bool Add(Kind kind, Vector3 p, Quaternion q, float size, int variant) {
                if (!clear(p, Vector3.one * size, EnvironmentPlan.CorridorMargin)) return false;
                foreach (var other in items) if (Vector3.Distance(p, other.position) < size + other.size + 12) return false;
                items.Add(new Item { kind = kind, position = p, rotation = q, size = size, variant = variant }); return true;
            }
            void Landmark(Kind kind, float size, float ahead, float side) {
                for (int attempt = 0; attempt < 32; attempt++) {
                    var p = start.position + forward * (ahead + attempt / 8 * 180) + right * side * (size + 170 + attempt * 65) + start.normal * (kind == Kind.Ring ? 155 : 75);
                    // Wheel lies in local XY; face the racing corridor, with a slight expressive tilt.
                    var facing = Quaternion.LookRotation((-right * side - forward * .35f).normalized, start.normal) * Quaternion.Euler(12, 0, -15);
                    if (Add(kind, p, facing, size, 0)) return;
                }
                // Dense folded courses can occupy every nearby candidate. Search beyond the full
                // course bounds while retaining the exact giant size and the same clearance predicate.
                var course = new Bounds(frames[0].position, Vector3.zero); float width = 0;
                foreach (var frame in frames) { course.Encapsulate(frame.position); width = Mathf.Max(width, frame.halfWidth); }
                var direction = (right * side + forward * .5f).normalized;
                var orientation = Quaternion.LookRotation(-direction, start.normal) * Quaternion.Euler(12, 0, -15);
                float distance = course.extents.magnitude + size * 1.74f + width + EnvironmentPlan.CorridorMargin + 256;
                for (int attempt = 0; attempt < 8; attempt++)
                    if (Add(kind, course.center + direction * (distance + attempt * 1000), orientation, size, 0)) return;
            }
        }

        // Cubic Hermite is contained by its Bezier control hull, not just its sample chords.
        // This predicate is scenery-only: existing city/core layout and hashes stay unchanged.
        public static Func<Vector3, Vector3, float, bool> LegacyClearance(TrackRoute route) {
            var frames = route.Samples;
            var corridors = new List<Bounds>();
            for (int i = 0; i + 1 < frames.Length; i++) {
                var a = frames[i]; var b = frames[i + 1]; float span = b.distance - a.distance;
                Vector3 Derivative(int at) { int lo = Mathf.Max(0, at - 1), hi = Mathf.Min(frames.Length - 1, at + 1); return (frames[hi].position - frames[lo].position) / (frames[hi].distance - frames[lo].distance); }
                var box = new Bounds(a.position, Vector3.zero); box.Encapsulate(b.position);
                box.Encapsulate(a.position + Derivative(i) * (span / 3)); box.Encapsulate(b.position - Derivative(i + 1) * (span / 3));
                float width = Mathf.Max(a.halfWidth, b.halfWidth);
                foreach (float at in new[] { a.distance, (a.distance + b.distance) * .5f, b.distance })
                    foreach (var paved in route.PavedAt(at)) width = Mathf.Max(width, (float)Math.Max(Math.Abs(paved.Left), Math.Abs(paved.Right)));
                // Additional ramp/air clearance, above the full road and camera margin.
                box.Expand(Vector3.one * (2 * (width + 16))); corridors.Add(box);
            }
            return (center, extents, margin) => {
                var objectBox = new Bounds(center, (extents + Vector3.one * margin) * 2);
                foreach (var corridor in corridors) if (objectBox.Intersects(corridor)) return false;
                return true;
            };
        }
    }
}
