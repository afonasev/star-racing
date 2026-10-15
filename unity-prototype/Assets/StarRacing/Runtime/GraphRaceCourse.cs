using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
    // Issued only by the physical binding. A copy preserves the exact accepted
    // cursor, position and tick; later queries cannot turn that copy into new credit.
    public readonly struct GraphRaceObservation {
        public readonly GraphVehicleRoute Owner;
        public readonly GraphRoadSurface Surface;
        public readonly RouteCursor Cursor;
        public readonly Vector3 Position;
        public readonly int BuildRevision, PositionRevision, Branch;
        public readonly long Tick;
        public readonly bool Continuous, Flight;
        public readonly double PhysicalDelta, CommonProgress;
        readonly bool supported;
        public bool Valid => supported && Owner!=null && Owner.Current && Owner.BuildRevision==BuildRevision && Owner.PositionRevision==PositionRevision;
        internal GraphRaceObservation(GraphVehicleRoute owner,RouteCursor cursor,Vector3 position,int revision,long tick,bool valid,bool continuous,double delta) {
            Owner=owner;Surface=owner.Surface;BuildRevision=owner.BuildRevision;Cursor=cursor;Position=position;
            PositionRevision=revision;Tick=tick;supported=valid;Continuous=continuous;PhysicalDelta=delta;
            Flight=owner.IsFlying;Branch=owner.CommittedBranch;CommonProgress=owner.Surface.Graph.RaceProgress(cursor);
        }
    }
    public sealed class GraphRaceCourse {
        public const int Schema=2;
        public sealed class Gate {
            public readonly double CommonProgress;
            internal readonly Instance[] instances;
            public bool BranchSpecific => instances.Any(i=>i.branch!=0);
            internal Gate(double progress,Instance[] instances){CommonProgress=progress;this.instances=instances;}
        }
        internal readonly struct Instance {
            public readonly int road, branch;
            public readonly double station, width;
            public readonly DVec position, tangent, normal, right;
            public Instance(GraphRoad road,double station,GraphJunction junction) {
                this.road=road.id;this.station=station;
                var f=road.Evaluate(station);position=f.position;tangent=f.tangent;normal=f.normal;right=f.right;width=f.halfWidth;branch=road.branch;
                if(junction!=null) {
                    double z=DVec.Dot(f.position-junction.origin,junction.tangent);
                    JunctionStation a=junction.stations[0],b=a;
                    foreach(var row in junction.stations){b=row;if(row.z>=z)break;a=row;}
                    double t=b.z>a.z?(z-a.z)/(b.z-a.z):0;
                    double gap=a.gapHalf+(b.gapHalf-a.gapHalf)*t,outer=a.outer+(b.outer-a.outer)*t;
                    tangent=junction.tangent;normal=junction.normal;right=junction.right;
                    if(gap<=1e-8){position=junction.Point(0,z);width=outer;branch=0;}
                    else {double sign=road.branch==IndependentGraph.Technical?-1:1;position=junction.Point(sign*(outer+gap)/2,z);width=(outer-gap)/2;}
                }
            }
        }
        readonly GraphRoadSurface surface;
        readonly IndependentGraph graph;
        readonly int buildRevision;
        public readonly string Identity;
        public readonly ReadOnlyCollection<Gate> Gates;
        public int FirstBranchGate {get;}
        public GraphRaceCourse(GraphRoadSurface surface,float start,float finish) {
            if(surface==null || surface.Graph==null || surface.Graph.legacy || !surface.isActiveAndEnabled)throw new ArgumentException("Active independent surface required");
            this.surface=surface;graph=surface.Graph;buildRevision=surface.BuildRevision;
            if(start<0 || start>=graph.Road(0).progressEnd || finish<=graph.Road(5).progressStart || finish>graph.CommonLength || float.IsNaN(start)||float.IsNaN(finish))throw new ArgumentException("Graph sprint must start on approach and finish on exit");
            var gates=new List<Gate>();
            int[][] stages={new[]{0},new[]{10,11},new[]{1,2},new[]{12,13},new[]{5}};
            foreach(var ids in stages) {
                var roads=ids.Select(graph.Road).ToArray();var exemplar=roads[0];
                double lo=Math.Max(start,exemplar.progressStart),hi=Math.Min(finish,exemplar.progressEnd);
                int divisions=Math.Max(1,(int)Math.Ceiling(roads.Max(r=>r.Length)*(hi-lo)/(exemplar.progressEnd-exemplar.progressStart)/50));
                GraphJunction junction=ids[0]==10?graph.junctions.Single(j=>!j.merging):ids[0]==12?graph.junctions.Single(j=>j.merging):null;
                for(int i=1;i<=divisions;i++) {
                    double progress=lo+(hi-lo)*i/divisions;
                    var instances=roads.Select(r=>new Instance(r,(progress-r.progressStart)/(r.progressEnd-r.progressStart)*r.Length,junction)).ToArray();
                    gates.Add(new Gate(progress,instances));
                }
            }
            Gates=gates.AsReadOnly();FirstBranchGate=gates.FindIndex(g=>g.BranchSpecific);
            // Frozen gate content belongs to replay identity. The captured surface
            // and buildRevision remain exclusively in the runtime authority checks.
            using(var buffer=new MemoryStream()) {
                using(var writer=new BinaryWriter(buffer,Encoding.UTF8,true)) {
                    writer.Write(Schema);writer.Write(graph.Identity);writer.Write(start);writer.Write(finish);writer.Write(FirstBranchGate);
                    writer.Write(gates.Count);
                    foreach(var gate in gates) {
                        writer.Write(gate.CommonProgress);writer.Write(gate.instances.Length);
                        foreach(var i in gate.instances) {
                            writer.Write(i.road);writer.Write(i.branch);writer.Write(i.station);writer.Write(i.width);
                            WriteVector(writer,i.position);WriteVector(writer,i.tangent);WriteVector(writer,i.normal);WriteVector(writer,i.right);
                        }
                    }
                }
                Identity=Digest(buffer.ToArray());
            }
        }
        static void WriteVector(BinaryWriter writer,DVec value){writer.Write(value.x);writer.Write(value.y);writer.Write(value.z);}
        static string Digest(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        // Compose full requested/actual seed/settings metadata with this exact layout.
        // Equality of replay content never grants authority to another physical build.
        public string ReplayIdentity(GraphCourseDefinition definition) {
            if(definition==null||definition.graph.Identity!=graph.Identity)throw new ArgumentException("Replay definition must match course graph content");
            using(var buffer=new MemoryStream()) {
                using(var writer=new BinaryWriter(buffer,Encoding.UTF8,true)){writer.Write(Schema);writer.Write(definition.Identity);writer.Write(Identity);}
                return Digest(buffer.ToArray());
            }
        }

        internal bool Accepts(GraphRaceObservation o)=>o.Owner!=null && ReferenceEquals(o.Surface,surface) &&
            surface!=null && surface.isActiveAndEnabled && ReferenceEquals(surface.Graph,graph) && surface.BuildRevision==buildRevision &&
            o.BuildRevision==buildRevision && o.PositionRevision==o.Owner.PositionRevision && o.Tick==o.Owner.RaceObservation.Tick && o.Owner.Current && graph.IsCurrent(o.Cursor);
        internal bool Continuous(GraphRaceObservation a,GraphRaceObservation b) {
            if(!graph.IsCurrent(a.Cursor)||!graph.IsCurrent(b.Cursor)||double.IsNaN(b.PhysicalDelta)||double.IsInfinity(b.PhysicalDelta))return false;
            var from=graph.Road(a.Cursor.roadId);var to=graph.Road(b.Cursor.roadId);
            double expected;
            if(from.id==to.id)expected=b.Cursor.localPhysicalDistance-a.Cursor.localPhysicalDistance;
            else if((from.id==10||from.id==11)&&(to.id==10||to.id==11)&&a.Branch==0) {
                // Provisional guide remapping has no race authority of its own.
                expected=b.Cursor.localPhysicalDistance-a.Cursor.localPhysicalDistance;
            } else {
                if(!graph.CanCrossPort(a.Cursor,b.Cursor,b.PhysicalDelta>0,GraphVehicleRoute.ProjectionWindow))return false;
                expected=b.PhysicalDelta>0?from.Length-a.Cursor.localPhysicalDistance+b.Cursor.localPhysicalDistance:
                    -a.Cursor.localPhysicalDistance-to.Length+b.Cursor.localPhysicalDistance;
            }
            return Math.Abs(expected-b.PhysicalDelta)<.001 && Vector3.Distance(a.Position,b.Position)<=GraphVehicleRoute.ProjectionWindow;
        }
        internal bool Crossing(Gate gate,GraphRaceObservation a,GraphRaceObservation b,out double fraction) {
            fraction=0;
            foreach(var i in gate.instances) {
                if(i.branch!=0 && b.Cursor.incomingBranch!=i.branch)continue;
                bool starts=a.Cursor.roadId==i.road,ends=b.Cursor.roadId==i.road;
                bool shared=i.branch==0 && (i.road==10||i.road==11) && (a.Cursor.roadId==10||a.Cursor.roadId==11) && (b.Cursor.roadId==10||b.Cursor.roadId==11);
                bool atPort=Math.Abs(i.station-graph.Road(i.road).Length)<1e-8 &&
                    graph.Road(a.Cursor.roadId).fromPort==graph.Road(i.road).toPort &&
                    a.Cursor.roadId==b.Cursor.roadId;
                if(!starts&&!ends&&!shared&&!atPort)continue;
                double from=atPort?i.station+a.Cursor.localPhysicalDistance:starts?a.Cursor.localPhysicalDistance:shared?a.Cursor.localPhysicalDistance:0;
                double to=atPort?i.station+b.Cursor.localPhysicalDistance:ends?b.Cursor.localPhysicalDistance:shared?b.Cursor.localPhysicalDistance:graph.Road(i.road).Length;
                // Off-centre projection need not straddle a gate at the same instant as
                // its physical plane. Only use station proximity to reject remote folds.
                if(Math.Min(Math.Abs(from-i.station),Math.Abs(to-i.station))>GraphVehicleRoute.ProjectionWindow)continue;
                var pa=new DVec(a.Position.x,a.Position.y,a.Position.z);var pb=new DVec(b.Position.x,b.Position.y,b.Position.z);
                double da=DVec.Dot(pa-i.position,i.tangent),db=DVec.Dot(pb-i.position,i.tangent);
                // Unity positions are float even though the authored gate is double.
                if(da>.001 || db<-.001 || db<=da)continue;
                double t=Math.Max(0,Math.Min(1,-da/(db-da)));var hit=pa+(pb-pa)*t-i.position;
                double lateral=DVec.Dot(hit,i.right),height=DVec.Dot(hit,i.normal);
                if(!a.Flight&&!b.Flight)foreach(var jump in graph.jumps.Where(j=>j.roadId==i.road&&j.ContainsLane(lateral)))height-=jump.Height(i.station,graph.guardrailMode);
                if(Math.Abs(lateral)>i.width || Math.Abs(height)>(a.Flight||b.Flight?20:3))continue;
                fraction=t;return true;
            }
            return false;
        }
    }
}
