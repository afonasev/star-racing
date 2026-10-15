using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using StarRacingPrototype.Procedural;

namespace StarRacingPrototype {
    // Per-vehicle traversal state. A guide is provisional on the undivided split floor.
    // This class supplies physical observations, never checkpoint or finish credit.
    public sealed class GraphVehicleRoute {
        public const float ProjectionWindow = 30f;
        const float SupportHeight = 3f;
        readonly GraphRoadSurface surface;
        readonly IndependentGraph graph;
        readonly int buildRevision;
        readonly GraphJunction split, merge;
        Vector3 previousPosition,previousVelocity;
        long previousTick;
        bool continuous, suppressNextCredit;
        readonly List<GraphProjection> candidates = new List<GraphProjection>(8);
        readonly List<GraphProjection> supportCandidates = new List<GraphProjection>(8);
        readonly List<GraphProjection> poseCandidates = new List<GraphProjection>(8);
        readonly int[] singleGeometry = new int[1], segmentGeometry = new int[2];
        readonly List<int> permittedGeometry = new List<int>(8), observationGeometry = new List<int>(8), poseGeometry = new List<int>(8);
        Collider[] overlaps = new Collider[32];
        public RouteCursor Cursor { get; private set; }
        public int CommittedBranch { get; private set; }
        public int PositionRevision { get; private set; }
        public bool LastObservationContinuous { get; private set; }
        public double LastPhysicalDelta { get; private set; }
        public bool RequiresRecovery { get; private set; }
        GraphJump supportedRamp;
        long rampTick,flightTick;float flightStep;
        public GraphJump FlightJump {get;private set;}
        public bool IsFlying=>FlightJump!=null;
        public bool IsJumpRegion=>graph.jumps.Any(j=>j.roadId==Cursor.roadId&&Cursor.localPhysicalDistance>=j.rampStart&&Cursor.localPhysicalDistance<=j.tacticalLanding);
        public GraphJump RampAtContact(Vector3 point) {
            var f=Frame;double distance=Cursor.localPhysicalDistance+DVec.Dot(D(point)-f.position,f.tangent);
            double lateral=DVec.Dot(D(point)-f.position,f.right),height=DVec.Dot(D(point)-f.position,f.normal);
            return graph.jumps.FirstOrDefault(j=>j.roadId==Cursor.roadId&&distance>=j.rampStart&&distance<=j.launch+.1&&j.ContainsLane(lateral)&&height>.05);
        }
        public bool InFlightCorridor(GraphJump jump,Vector3 position,double distance) {
            if(jump==null||jump.roadId!=Cursor.roadId||distance<jump.rampStart||distance>jump.ballisticLanding)return false;
            var f=graph.Road(jump.roadId).Evaluate(distance);double lateral=DVec.Dot(D(position)-f.position,f.right),height=DVec.Dot(D(position)-f.position,f.normal);
            return Math.Abs(lateral)<=f.halfWidth+1&&height>=-.6&&height<20;
        }
        bool CanLaunch(GraphJump jump,Vector3 position,Vector3 velocity) {
            if(jump==null||jump.roadId!=Cursor.roadId)return false;
            var f=graph.Road(jump.roadId).Evaluate(jump.launch);var p=D(position)-f.position;
            return Math.Abs(DVec.Dot(p,f.tangent))<=6&&jump.ContainsLane(DVec.Dot(p,f.right),.5)&&
                DVec.Dot(D(velocity),f.tangent)>5&&DVec.Dot(D(velocity),f.normal)>.1;
        }
        bool ObserveFlight(GraphProjection candidate,Vector3 position,Vector3 velocity,long tick,float dt) {
            if(Vector3.Distance(previousPosition,position)>Mathf.Max(previousVelocity.magnitude,velocity.magnitude)*dt+.1f)return false;
            if(!continuous || candidate.cursor.roadId!=Cursor.roadId || !Finite(velocity) || !Finite(dt) || dt<=0)return false;
            if(FlightJump==null) {
                if(tick-rampTick>2||!CanLaunch(supportedRamp,position,velocity))return false;
                FlightJump=supportedRamp;flightTick=tick;flightStep=dt;
            }
            if(dt!=flightStep || (tick-flightTick)*flightStep>=4 || !InFlightCorridor(FlightJump,position,candidate.cursor.localPhysicalDistance))return false;
            // A real launch retains its current common road only. It cannot cross a port or repair a rejected snapshot.
            bool credit=!suppressNextCredit;Cursor=candidate.cursor;LastObservationContinuous=credit;
            LastPhysicalDelta=credit?candidate.physicalDelta:0;previousPosition=position;previousVelocity=velocity;RequiresRecovery=false;suppressNextCredit=false;
            RaceObservation=new GraphRaceObservation(this,Cursor,position,PositionRevision,tick,true,credit,LastPhysicalDelta);return true;
        }
        public GraphRaceObservation RaceObservation { get; private set; }
        public double CommonProgress => graph.RaceProgress(Cursor);
        public bool Current => surface != null && surface.isActiveAndEnabled && surface.Graph == graph && surface.BuildRevision == buildRevision;
        public GraphFrame Frame => graph.Evaluate(Cursor);
        public int BuildRevision => buildRevision;
        public GraphRoadSurface Surface => surface;

        GraphVehicleRoute(GraphRoadSurface surface, RouteCursor cursor, Vector3 position, int revision, long tick) {
            this.surface = surface; graph = surface.Graph; buildRevision = surface.BuildRevision;
            split = graph.junctions.Single(j => !j.merging); merge = graph.junctions.Single(j => j.merging);
            Cursor = cursor; previousPosition = position; PositionRevision = revision; previousTick = tick;
            CommittedBranch = IsSplit(cursor.roadId) && Gap(split, position) == 0 ? 0 : cursor.incomingBranch;
            continuous = true;suppressNextCredit=true;RefreshSupportGeometry();
            RaceObservation = new GraphRaceObservation(this, cursor, position, revision, tick, false, false, 0);
        }
        public static bool TryCreate(GraphRoadSurface surface, RouteCursor cursor, Pose pose, int positionRevision, long tick,
            out GraphVehicleRoute route, float bodyHeight = VehicleGeometry.AdheredBodyHeight) {
            route = null;
            if (surface == null || surface.Graph == null || surface.Graph.legacy || !surface.Graph.IsCurrent(cursor) ||
                positionRevision < 0 || tick < 0 || !Finite(pose.position) || !Finite(pose.rotation)) return false;
            var candidate = new GraphVehicleRoute(surface, cursor, pose.position, positionRevision, tick);
            if (!candidate.CheckPose(cursor, candidate.CommittedBranch, pose, bodyHeight)) return false;
            route = candidate; return true;
        }
        static bool IsSplit(int road) => road == IndependentGraph.SplitTechnical || road == IndependentGraph.SplitSpeed;
        static bool IsMerge(int road) => road == IndependentGraph.MergeTechnical || road == IndependentGraph.MergeSpeed;
        public static int Geometry(int road) => IsSplit(road) ? 3 : IsMerge(road) ? 4 : road;
        static DVec D(Vector3 v) => new DVec(v.x, v.y, v.z);
        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        static bool Finite(Quaternion q) => Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w) &&
            Mathf.Abs(q.x*q.x + q.y*q.y + q.z*q.z + q.w*q.w - 1f) < .001f;
        static double Z(GraphJunction node, Vector3 point) => DVec.Dot(D(point) - node.origin, node.tangent);
        static double Gap(GraphJunction node, Vector3 point) {
            double z = Z(node, point);
            if (z < 0 || z > node.Length) return 0;
            for (int i = 0; i + 1 < node.stations.Count; i++) {
                var a = node.stations[i]; var b = node.stations[i + 1];
                if (z <= b.z) return a.gapHalf + (b.gapHalf - a.gapHalf) * (z-a.z)/(b.z-a.z);
            }
            return node.stations[node.stations.Count-1].gapHalf;
        }
        bool ArmAllowed(RoadFace face, Vector3 hit, int branch) {
            if (face.geometryId != 3 && face.geometryId != 4) return branch == 0 || face.arm == 0 || face.arm == branch;
            var node = face.geometryId == 3 ? split : merge;
            return branch == 0 || Gap(node, hit) == 0 || face.arm == branch;
        }
        bool Support(Vector3 position, Vector3 normal, IReadOnlyCollection<int> geometry, int branch, out RaycastHit hit, out RoadFace face,GraphRuntimeProfile.SupportProbe probe=GraphRuntimeProfile.SupportProbe.Unspecified) {
            hit = default; face = default;
            if (!Current || !Finite(position) || !Finite(normal) || normal.sqrMagnitude < .9f) return false;
            float maxHeight=SupportHeight+(graph.jumps.Any(j=>j.roadId==Cursor.roadId&&Cursor.localPhysicalDistance>=j.rampStart-6&&Cursor.localPhysicalDistance<=j.gapEnd+6)?2.8f:0);
            if (!surface.RaycastWithOrigin(new Ray(position + normal*.25f, -normal), maxHeight+.25f,
                geometry, graph.Identity, buildRevision, out hit, out face,probe)) return false;
            return Vector3.Dot(position-hit.point,normal) >= -.05f && Vector3.Dot(hit.normal, normal) > .7f && ArmAllowed(face, hit.point, branch);
        }
        void CaptureCandidates(Vector3 position) {
            graph.ProjectCandidatesInto(Cursor, D(position), ProjectionWindow, candidates);
            if (IsSplit(Cursor.roadId) && CommittedBranch == 0) {
                int sibling = Cursor.roadId == IndependentGraph.SplitTechnical ? IndependentGraph.SplitSpeed : IndependentGraph.SplitTechnical;
                var road = graph.Road(sibling);
                var f = road.Project(D(position), Cursor.localPhysicalDistance-ProjectionWindow, Cursor.localPhysicalDistance+ProjectionWindow, out double score);
                candidates.Add(new GraphProjection(graph.Cursor(sibling, f.distance), f, score, f.distance-Cursor.localPhysicalDistance));
            }
            IndependentGraph.SortProjections(candidates);
        }
        static void GeometrySet(List<GraphProjection> projections, List<int> ids) {
            ids.Clear();
            for (int i = 0; i < projections.Count; i++) {
                int id = Geometry(projections[i].cursor.roadId);
                if (!ids.Contains(id)) ids.Add(id);
            }
        }
        void RefreshSupportGeometry() {
            graph.ProjectCandidatesInto(Cursor, D(previousPosition), ProjectionWindow, supportCandidates);
            GeometrySet(supportCandidates, permittedGeometry);
        }
        bool CrossedPort(RouteCursor next, Vector3 position, double delta) {
            if (next.roadId == Cursor.roadId) return true;
            if (IsSplit(next.roadId) && IsSplit(Cursor.roadId) && CommittedBranch == 0) return true;
            bool forward = delta > 0;
            if (!graph.CanCrossPort(Cursor, next, forward, ProjectionWindow)) return false;
            var road = graph.Road(Cursor.roadId);
            var port = graph.ports.Single(p => p.id == (forward ? road.toPort : road.fromPort)).frame;
            double a = DVec.Dot(D(previousPosition)-port.position, port.tangent), b = DVec.Dot(D(position)-port.position, port.tangent);
            if (forward ? !(a <= .001 && b >= -.001 && b > a) : !(a >= -.001 && b <= .001 && b < a)) return false;
            double t = -a/(b-a);
            var crossing = D(previousPosition) + (D(position)-D(previousPosition))*Math.Max(0, Math.Min(1,t));
            return Math.Abs(DVec.Dot(crossing-port.position, port.right)) <= port.halfWidth &&
                Math.Abs(DVec.Dot(crossing-port.position, port.normal)) <= SupportHeight;
        }
        // Exact sign crossing rejects even a sub-sample divider; the support sweep also
        // rejects chords across bends and disconnected surfaces within the 30m window.
        bool CrossesDivider(GraphJunction node, Vector3 to) {
            double a = DVec.Dot(D(previousPosition)-node.origin,node.right), b = DVec.Dot(D(to)-node.origin,node.right);
            if (a*b > 0 || a == b) return false;
            var crossing = Vector3.Lerp(previousPosition, to, (float)(-a/(b-a)));
            return Gap(node, crossing) > 0;
        }
        bool SupportedSegment(Vector3 position, Vector3 normal, IReadOnlyCollection<int> geometry, int branch) {
            if (CrossesDivider(split, position) || CrossesDivider(merge, position)) return false;
            int steps = Math.Max(1, Mathf.CeilToInt(Vector3.Distance(previousPosition, position)/.5f));
            for (int i = 1; i <= steps; i++)
                if (!Support(Vector3.Lerp(previousPosition, position, i/(float)steps), normal, geometry, branch, out _, out _,GraphRuntimeProfile.SupportProbe.ObserveSegment)) return false;
            return true;
        }
        bool Reject(bool recovery=false) { RaceObservation = default; RequiresRecovery = recovery; FlightJump=null;supportedRamp=null;continuous = false; LastObservationContinuous = false; LastPhysicalDelta = 0; return false; }
        public bool Observe(Pose pose, int positionRevision, long tick)=>Observe(pose,positionRevision,tick,Vector3.zero,0);
        public bool Observe(Pose pose, int positionRevision, long tick,Vector3 velocity,float dt) {
            using var profile=GraphRuntimeProfile.Observe.Auto();
            Vector3 position=pose.position;
            LastObservationContinuous = false; LastPhysicalDelta = 0;
            if (!Current || positionRevision != PositionRevision) return Reject(true);
            if(!Finite(position) || !Finite(pose.rotation) || tick<=previousTick)return Reject();
            if (tick != previousTick + 1) continuous = false;
            previousTick = tick;
            if (Vector3.Distance(previousPosition, position) > ProjectionWindow) return Reject(true);
            using var supportBatch = surface.BeginQueryBatch();
            CaptureCandidates(position);
            GeometrySet(candidates, observationGeometry);
            bool lostTransition=false;
            foreach (var candidate in candidates) {
                bool transition = candidate.cursor.roadId != Cursor.roadId;
                bool provisionalReanchor=IsSplit(Cursor.roadId) && IsSplit(candidate.cursor.roadId) && CommittedBranch==0 &&
                    Gap(split,previousPosition)==0 && Gap(split,position)==0;
                if (transition && ((!continuous && !provisionalReanchor) || !CrossedPort(candidate.cursor, position, candidate.physicalDelta))) continue;
                int geometry = Geometry(candidate.cursor.roadId);
                segmentGeometry[0] = Geometry(Cursor.roadId); segmentGeometry[1] = geometry;
                singleGeometry[0] = geometry;
                if (!Support(position, candidate.frame.normal.Float, singleGeometry, CommittedBranch, out var hit, out var face,GraphRuntimeProfile.SupportProbe.ObserveCentre)) continue;
                // On the split the real supported arm chooses the guide; intent/proximity do not.
                if (geometry == 3 && face.arm != candidate.cursor.incomingBranch) continue;
                // A clamped endpoint is a candidate, not permission to project beyond a port.
                if (Math.Abs(DVec.Dot(D(hit.point)-candidate.frame.position,candidate.frame.tangent)) > .75) continue;
                if (!IsFlying && !SupportedSegment(position, candidate.frame.normal.Float, segmentGeometry, CommittedBranch)) continue;
                if(IsFlying && (candidate.cursor.roadId!=Cursor.roadId || !InFlightCorridor(FlightJump,position,candidate.cursor.localPhysicalDistance)))continue;
                int nextBranch=candidate.cursor.roadId==IndependentGraph.Approach ? 0 :
                    IsSplit(candidate.cursor.roadId) && Gap(split,hit.point)==0 ? 0 : candidate.cursor.incomingBranch;
                // Commitment and reverse release are transitions even within the same guide.
                if(nextBranch!=CommittedBranch && !continuous){lostTransition=true;continue;}
                if(!WheelSupport(pose,observationGeometry,nextBranch,GraphRuntimeProfile.SupportProbe.ObserveWheel))continue;
                bool wasContinuous = continuous && !suppressNextCredit;
                Cursor = candidate.cursor;CommittedBranch=nextBranch;
                FlightJump=null;var ramp=face.ramp?RampAtContact(hit.point):null;if(ramp!=null){supportedRamp=ramp;rampTick=tick;}
                LastPhysicalDelta = wasContinuous ? candidate.physicalDelta : 0;
                LastObservationContinuous = wasContinuous;
                previousPosition = position;previousVelocity=velocity; continuous = true;suppressNextCredit=false; RequiresRecovery=false;RefreshSupportGeometry();
                RaceObservation = new GraphRaceObservation(this, Cursor, position, PositionRevision, tick, true, wasContinuous, LastPhysicalDelta); return true;
            }
            GraphProjection airborne = default;
            for (int i = 0; i < candidates.Count; i++) if (candidates[i].cursor.roadId == Cursor.roadId) { airborne = candidates[i]; break; }
            if(airborne.cursor.graphIdentity==graph.Identity && ObserveFlight(airborne,position,velocity,tick,dt))return true;
            // Losing wheel contact on the current road only suspends admission. A
            // skipped port/branch boundary cannot be repaired by new nearby-road hits.
            var current=graph.Road(Cursor.roadId).Project(D(position),Cursor.localPhysicalDistance-ProjectionWindow,
                Cursor.localPhysicalDistance+ProjectionWindow,out _);
            bool sameRoad=Support(position,current.normal.Float,new[]{Geometry(Cursor.roadId)},CommittedBranch,out var sameHit,out _,GraphRuntimeProfile.SupportProbe.ObserveFallback) &&
                Math.Abs(DVec.Dot(D(sameHit.point)-current.position,current.tangent))<=.75;
            return Reject(lostTransition || !sameRoad);
        }
        // Queries used by suspension/presentation do not mutate traversal or grant admission.
        public bool Raycast(Vector3 origin, Vector3 direction, float length, out RaycastHit hit)
            =>RaycastCore(origin,direction,length,out hit,GraphRuntimeProfile.SupportProbe.Unspecified);
        internal bool RaycastWithOrigin(Vector3 origin,Vector3 direction,float length,out RaycastHit hit,GraphRuntimeProfile.SupportProbe probe)
            =>RaycastCore(origin,direction,length,out hit,probe);
        bool RaycastCore(Vector3 origin,Vector3 direction,float length,out RaycastHit hit,GraphRuntimeProfile.SupportProbe probe) {
            hit = default;
            if (!Current || !Finite(origin) || !Finite(direction) || !Finite(length) || length <= 0 || direction.sqrMagnitude < .0001f) return false;
            if (!surface.RaycastWithOrigin(new Ray(origin,direction),length,permittedGeometry,graph.Identity,buildRevision,out var result,out var face,probe) ||
                !ArmAllowed(face,result.point,CommittedBranch) || Vector3.Dot(result.normal,Frame.normal.Float)<.7f) return false;
            // Physical support can precede centre admission at a port, but cannot reach an unrelated section.
            if ((result.point-previousPosition).sqrMagnitude > ProjectionWindow*ProjectionWindow) return false;
            hit=result; return true;
        }
        public GraphFrame ProjectPresentation(Vector3 position) {
            if (!Finite(position) || !Current) return Frame;
            // Presentation never selects a neighbour or rewrites the accepted cursor.
            return graph.Road(Cursor.roadId).Project(D(position),Cursor.localPhysicalDistance-ProjectionWindow,
                Cursor.localPhysicalDistance+ProjectionWindow,out _);
        }
        public GraphFrame LookAhead(double physicalMetres) {
            if (double.IsNaN(physicalMetres) || double.IsInfinity(physicalMetres)) throw new ArgumentException("Invalid lookahead");
            var road=graph.Road(Cursor.roadId); double at=Cursor.localPhysicalDistance+physicalMetres;
            int branch=CommittedBranch!=0?CommittedBranch:Cursor.incomingBranch;
            for(int i=0;i<graph.roads.Count;i++) {
                if(at>=0 && at<=road.Length)return road.Evaluate(at);
                bool forward=at>road.Length;
                var next=graph.roads.FirstOrDefault(r=>r.id!=road.id && (forward?r.fromPort==road.toPort:r.toPort==road.fromPort) &&
                    (r.branch==0 || r.branch==branch));
                if(next==null)return road.Evaluate(at); // no arbitrary choice from approach
                at=forward?at-road.Length:at+next.Length;road=next;
            }
            return road.Evaluate(at);
        }
        bool WheelSupport(Pose pose,IReadOnlyCollection<int> geometry,int branch,GraphRuntimeProfile.SupportProbe probe) {
            Vector3 up=pose.rotation*Vector3.up;
            foreach(var offset in MagneticVehicle.SuspensionOffsets) {
                var origin=pose.position+pose.rotation*(offset+Vector3.up*VehicleGeometry.SuspensionMountHeight);
                if(!surface.RaycastWithOrigin(new Ray(origin,-up),MagneticVehicle.SuspensionTravel+MagneticVehicle.WheelRadius+.25f,
                    geometry,graph.Identity,buildRevision,out var hit,out var face,probe) ||
                    !ArmAllowed(face,hit.point,branch) || Vector3.Dot(hit.normal,up)<.7f)return false;
            }
            return true;
        }
        bool CheckPose(RouteCursor cursor,int branch,Pose pose,float bodyHeight) {
            using var profile=GraphRuntimeProfile.Checkpoint.Auto();
            if (!Current || !Finite(pose.position) || !Finite(pose.rotation) || !Finite(bodyHeight) || bodyHeight <= 0) return false;
            using var supportBatch = surface.BeginQueryBatch();
            var f=graph.Evaluate(cursor);Vector3 up=pose.rotation*Vector3.up;
            if(Vector3.Dot(up,f.normal.Float)<.7f)return false;
            var projected=graph.Road(cursor.roadId).Project(D(pose.position),Math.Max(0,cursor.localPhysicalDistance-.5),
                Math.Min(graph.Road(cursor.roadId).Length,cursor.localPhysicalDistance+.5),out _);
            if(Math.Abs(DVec.Dot(D(pose.position)-projected.position,projected.tangent))>.5)return false;
            graph.ProjectCandidatesInto(cursor,D(pose.position),CheckpointAdmission.HalfLength+1,poseCandidates);
            GeometrySet(poseCandidates, poseGeometry);
            var geometry = poseGeometry;
            if(!WheelSupport(pose,geometry,branch,GraphRuntimeProfile.SupportProbe.CheckPoseWheel))return false;
            for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++) {
                var point=pose.position-up*bodyHeight+pose.rotation*new Vector3(x*CheckpointAdmission.HalfWidth,0,z*CheckpointAdmission.HalfLength);
                if(!Support(point+up*2,up,geometry,branch,out var hit,out var face,GraphRuntimeProfile.SupportProbe.CheckPoseBody) || Vector3.Distance(hit.point,point)>.5f)return false;
                if(face.geometryId==3 && Gap(split,hit.point)>0 && face.arm!=cursor.incomingBranch)return false;
            }
            Vector3 centre=pose.position+pose.rotation*new Vector3(0,(CheckpointAdmission.Bottom+CheckpointAdmission.Top)*.5f,0);
            int count;
            // A saturated nonalloc query may omit a blocking collider. Grow and
            // repeat the same query until the complete original inventory fits.
            while (true) {
                count = Physics.OverlapBoxNonAlloc(centre, new Vector3(CheckpointAdmission.HalfWidth,
                    (CheckpointAdmission.Top-CheckpointAdmission.Bottom)*.5f, CheckpointAdmission.HalfLength),
                    overlaps, pose.rotation, ~0, QueryTriggerInteraction.Ignore);
                if (count < overlaps.Length) break;
                overlaps = new Collider[checked(overlaps.Length * 2)];
            }
            try {
                for (int i = 0; i < count; i++) {
                    var collider = overlaps[i];
                    if(collider!=null && collider.GetComponentInParent<MagneticVehicle>()==null &&
                        (collider.GetComponentInParent<GraphRoadSurface>()!=null || collider.GetComponentInParent<TrackBuilder>()!=null))return false;
                }
            } finally { Array.Clear(overlaps, 0, count); }
            return true;
        }
        public sealed class SafePose {
            internal readonly GraphVehicleRoute owner;
            public readonly RouteCursor cursor;
            public readonly Pose pose;
            public readonly int branch, buildRevision, positionRevision;
            internal readonly float bodyHeight;
            internal SafePose(GraphVehicleRoute owner,Pose pose,float height) {
                this.owner=owner;this.pose=pose;cursor=owner.Cursor;branch=owner.CommittedBranch;
                buildRevision=owner.buildRevision;positionRevision=owner.PositionRevision;bodyHeight=height;
            }
        }
        public bool TryCaptureSafePose(Pose pose,out SafePose safe,float bodyHeight=VehicleGeometry.AdheredBodyHeight) {
            safe=null;
            if(IsFlying || graph.jumps.Any(j=>j.roadId==Cursor.roadId && Cursor.localPhysicalDistance>=j.rampStart-80 && Cursor.localPhysicalDistance<=j.ballisticLanding) || !continuous || !CheckPose(Cursor,CommittedBranch,pose,bodyHeight))return false;
            safe=new SafePose(this,pose,bodyHeight);return true;
        }
        public bool TryRestore(SafePose safe,int newPositionRevision,long tick,out Pose pose) {
            pose=default;
            if(safe==null || safe.owner!=this || safe.buildRevision!=buildRevision || !graph.IsCurrent(safe.cursor) ||
                newPositionRevision<=PositionRevision || tick<previousTick || !CheckPose(safe.cursor,safe.branch,safe.pose,safe.bodyHeight))return false;
            Cursor=safe.cursor;CommittedBranch=safe.branch;PositionRevision=newPositionRevision;previousTick=tick;
            previousPosition=safe.pose.position;previousVelocity=Vector3.zero;FlightJump=null;supportedRamp=null;continuous=true;suppressNextCredit=true;RequiresRecovery=false;RefreshSupportGeometry();LastObservationContinuous=false;LastPhysicalDelta=0;
            RaceObservation = new GraphRaceObservation(this, Cursor, previousPosition, PositionRevision, tick, false, false, 0);
            pose=safe.pose;return true;
        }
    }
}
