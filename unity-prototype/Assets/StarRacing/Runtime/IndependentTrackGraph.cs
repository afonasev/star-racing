using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace StarRacingPrototype.Procedural {
    // The domain metric is the cumulative length of these canonical polyline samples.
    // A future spline/physics adapter must explicitly retain or remap that metric.
    public readonly struct GraphFrame {
        public readonly double distance, halfWidth;
        public readonly DVec position, tangent, normal, right;
        public readonly bool railLeft, railRight;
        public GraphFrame(double distance, DVec position, DVec tangent, DVec normal,
            DVec right, double halfWidth, bool railLeft = false, bool railRight = false) {
            this.distance = distance; this.position = position; this.tangent = tangent;
            this.normal = normal; this.right = right; this.halfWidth = halfWidth;
            this.railLeft = railLeft; this.railRight = railRight;
        }
    }

    public sealed class GraphRoad {
        public readonly int id, fromPort, toPort, branch;
        public readonly string kind;
        public readonly double progressStart, progressEnd;
        public readonly ReadOnlyCollection<GraphFrame> samples;
        // Road samples are copied at construction and immutable. Keep the same
        // subtraction/square arithmetic once; dynamic projection still divides.
        readonly ProjectionSegment[] projectionSegments;
        readonly struct ProjectionSegment {
            public readonly DVec origin, delta;
            public readonly double from, to, span, squared;
            public ProjectionSegment(GraphFrame a, GraphFrame b) {
                origin = a.position; delta = b.position - a.position;
                from = a.distance; to = b.distance;
                span = b.distance - a.distance; squared = delta.Squared;
            }
        }
        public double Length => samples[samples.Count - 1].distance;
        public GraphRoad(int id, string kind, int from, int to, int branch,
            IEnumerable<GraphFrame> samples, double start, double end) {
            this.id = id; this.kind = kind; fromPort = from; toPort = to; this.branch = branch;
            var frames = samples?.ToArray();
            if (frames == null || frames.Length < 2) throw new ArgumentException("A road needs two frames");
            this.samples = Array.AsReadOnly(frames); progressStart = start; progressEnd = end;
            projectionSegments = new ProjectionSegment[frames.Length - 1];
            for (int i = 0; i < projectionSegments.Length; i++)
                projectionSegments[i] = new ProjectionSegment(frames[i], frames[i + 1]);
        }
        public double Progress(double localPhysicalDistance) => progressStart +
            (progressEnd - progressStart) * Math.Max(0, Math.Min(1, localPhysicalDistance / Length));
        public double PhysicalDistance(double progress) => Length *
            Math.Max(0, Math.Min(1, (progress - progressStart) / (progressEnd - progressStart)));
        public GraphFrame Evaluate(double distance) {
            distance = Math.Max(0, Math.Min(Length, distance));
            int lo = 0, hi = samples.Count - 2;
            while (lo < hi) { int mid = (lo + hi + 1) / 2; if (samples[mid].distance <= distance) lo = mid; else hi = mid - 1; }
            var a = samples[lo]; var b = samples[lo + 1];
            double t = (distance - a.distance) / (b.distance - a.distance);
            var tangent = (a.tangent * (1 - t) + b.tangent * t).Unit(a.tangent);
            var normal = (a.normal * (1 - t) + b.normal * t).Unit(a.normal);
            normal = (normal - tangent * DVec.Dot(tangent, normal)).Unit(a.normal);
            return new GraphFrame(distance, a.position * (1 - t) + b.position * t,
                tangent, normal, DVec.Cross(tangent, normal).Unit(a.right),
                a.halfWidth * (1 - t) + b.halfWidth * t, a.railLeft, a.railRight);
        }
        // Normal of the actual swept ribbon, including lateral motion from roll.
        // A centreline normal would inject tangential adhesion off-centre in a bank.
        public DVec SurfaceNormal(double distance,double lateral) {
            distance=Math.Max(0,Math.Min(Length,distance));
            var frame=Evaluate(distance);var a=Evaluate(Math.Max(0,distance-.25));var b=Evaluate(Math.Min(Length,distance+.25));
            var along=(b.position-a.position+(b.right-a.right)*lateral)*(1/(b.distance-a.distance));
            return DVec.Cross(frame.right,along).Unit(frame.normal);
        }
        internal GraphFrame Project(DVec position, double from, double to, out double score) {
            using var profile=GraphRuntimeProfile.Project.Auto();
            from = Math.Max(0, from); to = Math.Min(Length, to); score = double.PositiveInfinity;
            double best = from;
            int lo=0,hi=projectionSegments.Length-1;
            while(lo<hi){int mid=(lo+hi)/2;if(projectionSegments[mid].to<from)lo=mid+1;else hi=mid;}
            for (int i = lo; i < projectionSegments.Length; i++) {
                var segment = projectionSegments[i];
                if(segment.from>to)break;
                if (segment.to < from) continue;
                double t = DVec.Dot(position - segment.origin, segment.delta) / segment.squared;
                double span = segment.span;
                t = Math.Max(Math.Max(0, (from - segment.from) / span),
                    Math.Min(Math.Min(1, (to - segment.from) / span), t));
                double candidate = (position - (segment.origin + segment.delta * t)).Squared;
                if (candidate < score) { score = candidate; best = segment.from + span * t; }
            }
            return Evaluate(best);
        }
    }

    public readonly struct JunctionStation {
        public readonly double z, outer, gapHalf;
        public JunctionStation(double z, double outer, double gapHalf) { this.z = z; this.outer = outer; this.gapHalf = gapHalf; }
    }
    public readonly struct JunctionPanel {
        public readonly DVec a, b, c, d;
        public readonly int arm;
        public JunctionPanel(DVec a, DVec b, DVec c, DVec d, int arm) {
            this.a = a; this.b = b; this.c = c; this.d = d; this.arm = arm;
        }
    }
    public sealed class GraphJunction {
        public readonly int id;
        public readonly DVec origin, tangent, normal, right;
        public readonly bool merging;
        public readonly ReadOnlyCollection<JunctionStation> stations;
        public readonly ReadOnlyCollection<JunctionPanel> panels;
        public double Length => stations[stations.Count - 1].z;
        public GraphJunction(int id, DVec origin, DVec tangent, DVec normal, DVec right,
            bool merging, IEnumerable<JunctionStation> stations) {
            this.id = id; this.origin = origin; this.tangent = tangent; this.normal = normal;
            this.right = right; this.merging = merging;
            var rows = stations?.ToArray(); if (rows == null || rows.Length < 2) throw new ArgumentException("A junction needs two stations");
            this.stations = Array.AsReadOnly(rows);
            var patches = new List<JunctionPanel>();
            for (int i = 0; i + 1 < this.stations.Count; i++) {
                var a = this.stations[i]; var b = this.stations[i + 1];
                // These bands tile the common top exactly. No merge tolerance fills the divider.
                patches.Add(new JunctionPanel(Point(-a.outer, a.z), Point(-b.outer, b.z),
                    Point(-a.gapHalf, a.z), Point(-b.gapHalf, b.z), IndependentGraph.Technical));
                patches.Add(new JunctionPanel(Point(a.gapHalf, a.z), Point(b.gapHalf, b.z),
                    Point(a.outer, a.z), Point(b.outer, b.z), IndependentGraph.Speed));
            }
            panels = Array.AsReadOnly(patches.ToArray());
        }
        public DVec Point(double lateral, double z) => origin + right * lateral + tangent * z;
        public GraphFrame[] Guide(int branch, bool rails) {
            if (branch != IndependentGraph.Technical && branch != IndependentGraph.Speed) throw new ArgumentException("Unknown junction arm");
            double side = branch == IndependentGraph.Technical ? -1 : 1;
            return IndependentGraph.Frames(stations.Select(s => Point(side * (s.outer - IndependentGraph.HalfWidth), s.z)).ToArray(),
                tangent, normal, IndependentGraph.HalfWidth, rails);
        }
    }

    public readonly struct GraphPort {
        public readonly int id;
        public readonly GraphFrame frame;
        internal GraphPort(int id, GraphFrame frame) { this.id = id; this.frame = frame; }
    }
    public readonly struct RouteCursor {
        public readonly int roadId, incomingBranch;
        public readonly double localPhysicalDistance;
        public readonly string graphIdentity;
        public RouteCursor(int roadId, double localPhysicalDistance, int incomingBranch, string graphIdentity) {
            this.roadId = roadId; this.localPhysicalDistance = localPhysicalDistance;
            this.incomingBranch = incomingBranch; this.graphIdentity = graphIdentity;
        }
    }
    public readonly struct GraphProjection {
        public readonly RouteCursor cursor;
        public readonly GraphFrame frame;
        public readonly double squaredDistance, physicalDelta;
        public GraphProjection(RouteCursor cursor, GraphFrame frame, double squaredDistance, double physicalDelta) {
            this.cursor = cursor; this.frame = frame; this.squaredDistance = squaredDistance; this.physicalDelta = physicalDelta;
        }
    }

    // Isolated geometry/traversal foundation. Active Generator11 and TrackRoute do not
    // consume this graph yet. Projection candidates never grant checkpoints or support.
    public sealed class IndependentGraph {
        public const int Schema = 2, NativeRevision = 12;
        public const int Approach = 0, Technical = 1, Speed = 2, Exit = 5;
        public const int SplitTechnical = 10, SplitSpeed = 11, MergeTechnical = 12, MergeSpeed = 13;
        public const double HalfWidth = 8.5, Separation = 30, JunctionLength = 220, Radius = 180;
        public readonly ReadOnlyCollection<GraphRoad> roads;
        public readonly ReadOnlyCollection<GraphJunction> junctions;
        public readonly ReadOnlyCollection<GraphPort> ports;
        public readonly string Identity, guardrailMode;
        public readonly ReadOnlyCollection<GraphJump> jumps;
        public readonly bool legacy;
        public double CommonLength {get;}
        readonly Dictionary<int, GraphRoad> byId;

        IndependentGraph(IEnumerable<GraphRoad> roads, IEnumerable<GraphJunction> junctions, string guardrailMode, bool legacy = false, IEnumerable<GraphJump> jumps = null) {
            this.roads = Array.AsReadOnly(roads.OrderBy(r => r.id).ToArray());
            this.junctions = Array.AsReadOnly(junctions.OrderBy(j => j.id).ToArray());
            this.guardrailMode = guardrailMode; this.legacy = legacy;
            this.jumps = Array.AsReadOnly((jumps ?? Array.Empty<GraphJump>()).OrderBy(j=>j.roadId).ThenBy(j=>j.runwayStart).ToArray());
            CommonLength=this.roads.Count==0?0:this.roads.Max(r=>r.progressEnd);
            byId = this.roads.ToDictionary(r => r.id);
            var p = new Dictionary<int, GraphPort>();
            foreach (var road in this.roads) {
                if (!p.ContainsKey(road.fromPort)) p.Add(road.fromPort, new GraphPort(road.fromPort, road.samples[0]));
                if (!p.ContainsKey(road.toPort)) p.Add(road.toPort, new GraphPort(road.toPort, road.samples[road.samples.Count - 1]));
            }
            ports = Array.AsReadOnly(p.Values.OrderBy(v => v.id).ToArray());
            Identity = Hash();
        }
        public GraphRoad Road(int id) => byId.TryGetValue(id, out var road) ? road : throw new ArgumentException("Unknown road ID", nameof(id));
        // Structural import validation. Whole-track clearance and physical support
        // certification remain required when a runtime generator consumes this model.
        public static IndependentGraph FromGeometry(IEnumerable<GraphRoad> roads, IEnumerable<GraphJunction> junctions, string mode, bool legacy = false, IEnumerable<GraphJump> jumps = null) {
            var graph = new IndependentGraph(roads, junctions, mode, legacy, jumps);
            var errors = graph.Validate();
            if (errors.Length != 0) throw new ArgumentException("Invalid graph: " + string.Join(",", errors));
            return graph;
        }
        public RouteCursor Cursor(int road, double distance, int incomingBranch = 0) {
            var r = Road(road);
            if (!Finite(distance) || distance < 0 || distance > r.Length ||
                (incomingBranch != 0 && incomingBranch != Technical && incomingBranch != Speed) ||
                (r.branch != 0 && incomingBranch != 0 && incomingBranch != r.branch) ||
                (!legacy && road == Exit && incomingBranch == 0) || (road == Approach && incomingBranch != 0)) throw new ArgumentException("Invalid cursor");
            return new RouteCursor(road, distance, r.branch != 0 ? r.branch : incomingBranch, Identity);
        }
        public bool IsCurrent(RouteCursor cursor) => cursor.graphIdentity == Identity && byId.TryGetValue(cursor.roadId, out var r) &&
            Finite(cursor.localPhysicalDistance) && cursor.localPhysicalDistance >= 0 && cursor.localPhysicalDistance <= r.Length &&
            (cursor.incomingBranch == 0 || cursor.incomingBranch == Technical || cursor.incomingBranch == Speed) &&
            (r.branch == 0 || cursor.incomingBranch == r.branch) &&
            (legacy || cursor.roadId != Exit || cursor.incomingBranch != 0) && (cursor.roadId != Approach || cursor.incomingBranch == 0);
        public double RaceProgress(RouteCursor cursor) {
            if (!IsCurrent(cursor)) throw new ArgumentException("Stale or invalid cursor");
            return Road(cursor.roadId).Progress(cursor.localPhysicalDistance);
        }
        public GraphFrame Evaluate(RouteCursor cursor) {
            if (!IsCurrent(cursor)) throw new ArgumentException("Stale or invalid cursor");
            return Road(cursor.roadId).Evaluate(cursor.localPhysicalDistance);
        }
        // Candidate generation only. Physical gate crossing/support admission is an
        // independent runtime concern; proximity must never mutate the accepted cursor.
        public ReadOnlyCollection<GraphProjection> ProjectCandidates(RouteCursor previous, DVec position, double window) {
            var result = new List<GraphProjection>();
            ProjectCandidatesInto(previous, position, window, result);
            return Array.AsReadOnly(result.ToArray());
        }
        // Caller-owned scratch storage. The public snapshot remains immutable;
        // runtime admission can reuse its own list without publishing it.
        public void ProjectCandidatesInto(RouteCursor previous, DVec position, double window, List<GraphProjection> result) {
            if (!IsCurrent(previous) || !Finite(window) || window < 0 || !Finite(position)) throw new ArgumentException("Invalid projection");
            if (result == null) throw new ArgumentNullException(nameof(result));
            result.Clear();
            var current = Road(previous.roadId);
            AddProjection(result, current, previous, position, previous.localPhysicalDistance - window,
                previous.localPhysicalDistance + window, previous.localPhysicalDistance, 0);
            for (int i = 0; i < roads.Count; i++) {
                var other = roads[i];
                if (other.id == current.id || (previous.incomingBranch != 0 && other.branch != 0 && other.branch != previous.incomingBranch)) continue;
                double remaining = current.Length - previous.localPhysicalDistance;
                if (current.toPort == other.fromPort && remaining <= window)
                    AddProjection(result, other, previous, position, 0, window - remaining, 0, remaining);
                if (current.fromPort == other.toPort && previous.localPhysicalDistance <= window)
                    AddProjection(result, other, previous, position, other.Length - (window - previous.localPhysicalDistance), other.Length, other.Length, -previous.localPhysicalDistance);
            }
            SortProjections(result);
        }
        void AddProjection(List<GraphProjection> result, GraphRoad road, RouteCursor previous, DVec position,
            double from, double to, double anchor, double baseDelta) {
            var frame = road.Project(position, from, to, out double score);
            int history = road.id == Approach ? 0 : road.branch != 0 ? road.branch : previous.incomingBranch;
            result.Add(new GraphProjection(Cursor(road.id, frame.distance, history), frame, score, baseDelta + frame.distance - anchor));
        }
        public static void SortProjections(List<GraphProjection> result) {
            // Stable insertion order preserves OrderBy(score).ThenBy(roadId),
            // including duplicate candidates and exactly equal native doubles.
            for (int i = 1; i < result.Count; i++) {
                var value = result[i]; int at = i;
                while (at > 0) {
                    var before = result[at - 1];
                    int order = before.squaredDistance.CompareTo(value.squaredDistance);
                    if (order < 0 || order == 0 && before.cursor.roadId <= value.cursor.roadId) break;
                    result[at] = before; at--;
                }
                result[at] = value;
            }
        }
        public bool CanCrossPort(RouteCursor from, RouteCursor to, bool forward, double physicalWindow) {
            if (!IsCurrent(from) || !IsCurrent(to) || !Finite(physicalWindow) || physicalWindow < 0) return false;
            var a = Road(from.roadId); var b = Road(to.roadId);
            if (a.id == b.id) return false;
            if (from.incomingBranch != 0 && b.branch != 0 && from.incomingBranch != b.branch) return false;
            int expectedHistory = b.id == Approach ? 0 : b.branch != 0 ? b.branch : from.incomingBranch;
            if (to.incomingBranch != expectedHistory) return false;
            return forward ? a.toPort == b.fromPort && a.Length - from.localPhysicalDistance + to.localPhysicalDistance <= physicalWindow
                : a.fromPort == b.toPort && from.localPhysicalDistance + b.Length - to.localPhysicalDistance <= physicalWindow;
        }
        public GraphFrame[] Path(int branch) {
            int[] ids;
            if (legacy) ids = new[] { Approach };
            else if (branch == Technical) ids = new[] { Approach, SplitTechnical, Technical, MergeTechnical, Exit };
            else if (branch == Speed) ids = new[] { Approach, SplitSpeed, Speed, MergeSpeed, Exit };
            else throw new ArgumentException("Unknown branch");
            var result = new List<GraphFrame>(); double offset = 0;
            foreach (int id in ids) {
                var road = Road(id);
                for (int i = 0; i < road.samples.Count; i++) {
                    if (result.Count > 0 && i == 0) continue;
                    var f = road.samples[i];
                    result.Add(new GraphFrame(offset + f.distance, f.position, f.tangent, f.normal, f.right, f.halfWidth, f.railLeft, f.railRight));
                }
                offset += road.Length;
            }
            return result.ToArray();
        }
        public static IndependentGraph SingleRoad(Definition definition) {
            if (definition == null || definition.samples == null || definition.samples.Length < 2) throw new ArgumentException("Invalid legacy track");
            // Preserve the old source metric exactly; this adapter does not change replay or runtime.
            var frames = definition.samples.Select(s => new GraphFrame(s.distance, s.position, s.tangent, s.normal, s.right,
                s.halfWidth, s.railLeft, s.railRight));
            return FromGeometry(new[] { new GraphRoad(Approach, "legacy", 0, 1, 0, frames, 0, definition.totalLength) },
                Array.Empty<GraphJunction>(), definition.guardrailMode, true);
        }
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        internal static bool Finite(DVec value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        internal static bool Basis(DVec tangent, DVec normal, DVec right) => Finite(tangent) && Finite(normal) && Finite(right) &&
            Math.Abs(tangent.Squared - 1) < 1e-8 && Math.Abs(normal.Squared - 1) < 1e-8 && Math.Abs(right.Squared - 1) < 1e-8 &&
            Math.Abs(DVec.Dot(tangent, normal)) < 1e-8 && (DVec.Cross(tangent, normal) - right).Squared < 1e-16;
        internal static GraphFrame[] Frames(DVec[] positions, DVec endTangent, DVec normal, double width, bool rails) {
            var frames = new GraphFrame[positions.Length]; double distance = 0;
            for (int i = 0; i < frames.Length; i++) {
                if (i > 0) distance += Math.Sqrt((positions[i] - positions[i - 1]).Squared);
                var tangent = i == 0 || i == frames.Length - 1 ? endTangent : (positions[i + 1] - positions[i - 1]).Unit(endTangent);
                frames[i] = new GraphFrame(distance, positions[i], tangent, normal, DVec.Cross(tangent, normal).Unit(), width, rails, rails);
            }
            return frames;
        }
        static GraphFrame[] Resample(List<DVec> points, DVec origin, DVec tangent, DVec normal, DVec right, bool rails) {
            var cumulative = new double[points.Count];
            for (int i = 1; i < points.Count; i++) cumulative[i] = cumulative[i - 1] + Math.Sqrt((points[i] - points[i - 1]).Squared);
            int steps = Math.Max(2, (int)Math.Ceiling(cumulative[cumulative.Length - 1] / 5));
            var positions = new DVec[steps + 1]; int at = 0;
            for (int i = 0; i <= steps; i++) {
                double target = cumulative[cumulative.Length - 1] * i / steps;
                while (at + 1 < cumulative.Length - 1 && cumulative[at + 1] < target) at++;
                double t = (target - cumulative[at]) / Math.Max(1e-12, cumulative[at + 1] - cumulative[at]);
                var p = points[at] * (1 - t) + points[at + 1] * t;
                positions[i] = origin + right * p.x + tangent * p.z;
            }
            return Frames(positions, tangent, normal, HalfWidth, rails);
        }
        static double Blend(double t) => t * t * t * (10 + t * (-15 + 6 * t));
        public static IndependentGraph Create(DVec origin, DVec tangent, DVec normal, DVec right, string guardrailMode = "normal", double speedStraightLength = 180, double commonLength = 160) {
            if (!Finite(origin) || !Basis(tangent, normal, right)) throw new ArgumentException("Graph origin/basis must be finite and orthonormal");
            if (guardrailMode != "normal" && guardrailMode != "full" && guardrailMode != "none") throw new ArgumentException("Unknown rail mode");
            if (!Finite(speedStraightLength) || speedStraightLength < 0 || speedStraightLength > 500) throw new ArgumentException("Invalid speed straight length");
            if (!Finite(commonLength) || commonLength < 160) throw new ArgumentException("Invalid common length");
            bool rails = guardrailMode != "none";
            const double span = Radius * 4; double approachLength=commonLength, exitLength=commonLength;
            var technical = new List<DVec>(); var speed = new List<DVec>();
            for (int i = 0; i <= 1440; i++) {
                double t = i / 1440.0, envelope = Math.Sin(Math.PI * t);
                // Zero endpoint curvature gives both roads a calm entry into the shared nodes.
                technical.Add(new DVec(-Separation - 70 * (1 - Math.Cos(4 * Math.PI * t)) * envelope * envelope, 0, span * t));
            }
            double x = Separation, z = 0;
            void Arc(int kind) {
                double ox = x, oz = z;
                for (int i = 0; i <= 720; i++) {
                    double angle = Math.PI * .5 * i / 720;
                    double dx = kind == 0 ? Radius * (1 - Math.Cos(angle)) : kind == 1 ? Radius * Math.Sin(angle)
                        : kind == 2 ? -Radius * (1 - Math.Cos(angle)) : -Radius * Math.Sin(angle);
                    double dz = kind == 0 || kind == 2 ? Radius * Math.Sin(angle) : Radius * (1 - Math.Cos(angle));
                    speed.Add(new DVec(ox + dx, 0, oz + dz));
                }
                x = speed[speed.Count - 1].x; z = speed[speed.Count - 1].z;
            }
            void Straight(double direction) { double ox = x; int steps = (int)Math.Ceiling(speedStraightLength / .5);
                for (int i = 1; i <= steps; i++) speed.Add(new DVec(ox + direction * speedStraightLength * i / steps, 0, z));
                x = speed[speed.Count - 1].x; }
            Arc(0); Straight(1); Arc(1); Arc(2); Straight(-1); Arc(3);
            var roadOrigin = origin + tangent * JunctionLength;
            var a = Resample(technical, roadOrigin, tangent, normal, right, rails);
            var b = Resample(speed, roadOrigin, tangent, normal, right, rails);
            GraphJunction Junction(int id, DVec position, bool merging) {
                var stations = new List<JunctionStation>();
                for (int i = 0; i <= JunctionLength * 2; i++) {
                    double dz = i * .5, c = Separation * Blend(merging ? 1 - dz / JunctionLength : dz / JunctionLength);
                    stations.Add(new JunctionStation(dz, c + HalfWidth, Math.Max(0, c - HalfWidth)));
                }
                double lo = 0, hi = 1;
                for (int i = 0; i < 64; i++) { double mid = (lo + hi) * .5; if (Separation * Blend(mid) < HalfWidth) lo = mid; else hi = mid; }
                double tip = JunctionLength * (merging ? 1 - (lo + hi) * .5 : (lo + hi) * .5);
                stations.RemoveAll(s => Math.Abs(s.z - tip) < 1e-10);
                stations.Add(new JunctionStation(tip, 2 * HalfWidth, 0));
                return new GraphJunction(id, position, tangent, normal, right, merging, stations.OrderBy(s => s.z));
            }
            var split = Junction(3, origin, false); var merge = Junction(4, origin + tangent * (JunctionLength + span), true);
            var leftSplit = split.Guide(Technical, rails); var rightSplit = split.Guide(Speed, rails);
            var leftMerge = merge.Guide(Technical, rails); var rightMerge = merge.Guide(Speed, rails);
            var approach = Frames(new[] { origin - tangent * approachLength, origin }, tangent, normal, HalfWidth, rails);
            var exitOrigin = merge.Point(0, JunctionLength);
            var exit = Frames(new[] { exitOrigin, exitOrigin + tangent * exitLength }, tangent, normal, HalfWidth, rails);
            double p1 = approachLength, p2 = p1 + leftSplit.Last().distance, p3 = p2 + a.Last().distance,
                p4 = p3 + leftMerge.Last().distance;
            return FromGeometry(new[] {
                new GraphRoad(Approach,"approach",0,10,0,approach,0,p1),
                new GraphRoad(SplitTechnical,"split-guide",10,11,Technical,leftSplit,p1,p2),
                new GraphRoad(SplitSpeed,"split-guide",10,12,Speed,rightSplit,p1,p2),
                new GraphRoad(Technical,"technical",11,21,Technical,a,p2,p3),
                new GraphRoad(Speed,"speed",12,22,Speed,b,p2,p3),
                new GraphRoad(MergeTechnical,"merge-guide",21,30,Technical,leftMerge,p3,p4),
                new GraphRoad(MergeSpeed,"merge-guide",22,30,Speed,rightMerge,p3,p4),
                new GraphRoad(Exit,"exit",30,40,0,exit,p4,p4+exitLength)
            }, new[] { split, merge }, guardrailMode);
        }
        public string[] Validate() {
            var reasons = new List<string>();
            if (guardrailMode != "normal" && guardrailMode != "full" && guardrailMode != "none") reasons.Add("rail-mode");
            if (legacy) {
                if (roads.Count != 1 || roads[0].id != Approach || junctions.Count != 0) reasons.Add("legacy-topology");
            } else {
                int[] ids = { Approach, Technical, Speed, Exit, SplitTechnical, SplitSpeed, MergeTechnical, MergeSpeed };
                if (!roads.Select(r => r.id).OrderBy(i => i).SequenceEqual(ids.OrderBy(i => i)) ||
                    !junctions.Select(j => j.id).SequenceEqual(new[] { 3, 4 })) reasons.Add("pair-topology");
                int[][] topology = { new[] {Approach,0,10,0}, new[] {SplitTechnical,10,11,Technical}, new[] {SplitSpeed,10,12,Speed},
                    new[] {Technical,11,21,Technical}, new[] {Speed,12,22,Speed}, new[] {MergeTechnical,21,30,Technical},
                    new[] {MergeSpeed,22,30,Speed}, new[] {Exit,30,40,0} };
                foreach (var edge in topology) if (!byId.TryGetValue(edge[0], out var r) || r.fromPort != edge[1] || r.toPort != edge[2] || r.branch != edge[3]) reasons.Add("road-topology-" + edge[0]);
            }
            foreach (var road in roads) {
                if (road.samples.Count < 2 || !Finite(road.Length) || road.Length <= 0 || road.samples[0].distance != 0 ||
                    !Finite(road.progressStart) || !Finite(road.progressEnd) || road.progressEnd <= road.progressStart) reasons.Add("road-metric-" + road.id);
                for (int i = 0; i < road.samples.Count; i++) {
                    var f = road.samples[i];
                    if (!Finite(f.position) || !Basis(f.tangent, f.normal, f.right) || !Finite(f.halfWidth) || f.halfWidth <= 0) reasons.Add("road-frame-" + road.id);
                    if (i > 0 && (f.distance <= road.samples[i - 1].distance || !Finite(f.distance))) reasons.Add("road-order-" + road.id);
                    if (!legacy && i > 0 && Math.Abs(f.distance - road.samples[i - 1].distance - Math.Sqrt((f.position - road.samples[i - 1].position).Squared)) > 1e-7) reasons.Add("road-physical-distance-" + road.id);
                }
                foreach (var next in roads.Where(r => road.toPort == r.fromPort)) {
                    var a = road.samples.Last(); var b = next.samples.First();
                    if ((a.position - b.position).Squared > 1e-16 || (a.tangent - b.tangent).Squared > 1e-16 ||
                        (a.normal - b.normal).Squared > 1e-16 || (a.right - b.right).Squared > 1e-16 || Math.Abs(a.halfWidth - b.halfWidth) > 1e-8 ||
                        Math.Abs(road.progressEnd - next.progressStart) > 1e-8) reasons.Add("port-seam-" + road.toPort);
                }
            }
            foreach (var node in junctions) {
                if (node.merging != (node.id == 4)) reasons.Add("junction-role-" + node.id);
                for (int i = 0; i < node.stations.Count; i++) {
                    var s = node.stations[i];
                    if (!Finite(s.z) || !Finite(s.outer) || !Finite(s.gapHalf) || s.outer <= s.gapHalf || s.gapHalf < 0 ||
                        (i > 0 && s.z <= node.stations[i - 1].z) ||
                        Math.Abs(s.gapHalf - Math.Max(0, s.outer - 2 * HalfWidth)) > 1e-8) reasons.Add("junction-station-" + node.id);
                }
                if (!Basis(node.tangent, node.normal, node.right) || !Finite(node.origin)) reasons.Add("junction-basis-" + node.id);
                foreach (int branch in new[] { Technical, Speed }) {
                    int roadId = node.merging ? (branch == Technical ? MergeTechnical : MergeSpeed) : (branch == Technical ? SplitTechnical : SplitSpeed);
                    if (!byId.TryGetValue(roadId, out var guide)) { reasons.Add("junction-guide-" + node.id); continue; }
                    var expected = node.Guide(branch, guardrailMode != "none");
                    if (expected.Length != guide.samples.Count || expected.Where((f,i) => i < guide.samples.Count &&
                        ((f.position - guide.samples[i].position).Squared > 1e-16 || Math.Abs(f.distance - guide.samples[i].distance) > 1e-8 ||
                         (f.tangent - guide.samples[i].tangent).Squared > 1e-16 || (f.normal - guide.samples[i].normal).Squared > 1e-16 ||
                         (f.right - guide.samples[i].right).Squared > 1e-16 || Math.Abs(f.halfWidth - guide.samples[i].halfWidth) > 1e-8 ||
                         f.railLeft != guide.samples[i].railLeft || f.railRight != guide.samples[i].railRight)).Any()) reasons.Add("junction-guide-" + node.id);

                }
            }
            foreach(var jump in jumps) if(!jump.Valid(this)) reasons.Add("jump-contract-"+jump.id);
            foreach(var roadJumps in jumps.GroupBy(j=>j.roadId)) {
                var ordered=roadJumps.OrderBy(j=>j.runwayStart).ToArray();
                for(int i=1;i<ordered.Length;i++) if(ordered[i].rampStart-ordered[i-1].tacticalLanding<450 || ordered[i].runwayStart<ordered[i-1].runwayEnd) reasons.Add("jump-spacing");
            }
            if(jumps.Select(j=>j.id).Distinct().Count()!=jumps.Count)reasons.Add("jump-id");
            return reasons.Distinct().ToArray();
        }
        string Hash() {
            using (var buffer = new MemoryStream()) {
                using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true)) {
                    void Vector(DVec v) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
                    void Frame(GraphFrame f) { writer.Write(f.distance); Vector(f.position); Vector(f.tangent); Vector(f.normal); Vector(f.right); writer.Write(f.halfWidth); writer.Write(f.railLeft); writer.Write(f.railRight); }
                    writer.Write("star-racing-independent-graph"); writer.Write(Schema); writer.Write(NativeRevision); writer.Write(legacy); writer.Write(guardrailMode);
                    writer.Write(roads.Count);
                    foreach (var road in roads) {
                        writer.Write(road.id); writer.Write(road.kind); writer.Write(road.fromPort); writer.Write(road.toPort); writer.Write(road.branch);
                        writer.Write(road.progressStart); writer.Write(road.progressEnd); writer.Write(road.samples.Count);
                        foreach (var frame in road.samples) Frame(frame);
                    }
                    writer.Write(jumps.Count); foreach(var j in jumps) {writer.Write(j.id);writer.Write(j.roadId);writer.Write(j.runwayStart);writer.Write(j.mandatory);writer.Write(j.lateralCenter);writer.Write(j.lateralHalfWidth);writer.Write(j.runwayEnd);writer.Write(j.rampStart);writer.Write(j.launch);writer.Write(j.gapStart);writer.Write(j.gapEnd);writer.Write(j.tacticalLanding);writer.Write(j.ballisticLanding);writer.Write(GraphJump.Profile);}
                    writer.Write(ports.Count); foreach (var port in ports) { writer.Write(port.id); Frame(port.frame); }
                    writer.Write(junctions.Count);
                    foreach (var node in junctions) {
                        writer.Write(node.id); Vector(node.origin); Vector(node.tangent); Vector(node.normal); Vector(node.right); writer.Write(node.merging);
                        writer.Write(node.stations.Count); foreach (var s in node.stations) { writer.Write(s.z); writer.Write(s.outer); writer.Write(s.gapHalf); }
                        writer.Write(node.panels.Count); foreach (var p in node.panels) { writer.Write(p.arm); Vector(p.a); Vector(p.b); Vector(p.c); Vector(p.d); }
                    }
                }
                using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(buffer.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
