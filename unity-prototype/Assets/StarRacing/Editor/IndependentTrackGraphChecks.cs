using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using StarRacingPrototype.Procedural;

namespace StarRacingPrototype {
    public static class IndependentTrackGraphChecks {
        static int assertions;
        static void Check(bool value, string reason) { assertions++; if (!value) throw new Exception("Independent graph: " + reason); }
        static void Near(double a, double b, double tolerance, string reason) => Check(Math.Abs(a - b) <= tolerance, reason + $" ({a:R}/{b:R})");
        static void Point(DVec a, DVec b, string reason) => Near(Math.Sqrt((a - b).Squared), 0, 1e-7, reason);
        static void Reject(Action action, string reason) {
            bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, reason);
        }
        [Serializable] sealed class Metrics {
            public int graphAssertions, legacyAssertions, graphRevision, activeRevision;
            public double technicalLength, speedLength, technicalMaxTurnPer5m, speedMaxTurnPer5m, minimumRoadSeparation;
            public string identity, status;
        }
        static double Turn(GraphRoad road) {
            double largest = 0;
            for (int i = 1; i < road.samples.Count; i++) {
                var a = road.samples[i - 1]; var b = road.samples[i];
                double angle = Math.Acos(Math.Max(-1, Math.Min(1, DVec.Dot(a.tangent, b.tangent))));
                largest = Math.Max(largest, angle * 5 / (b.distance - a.distance));
            }
            return largest;
        }
        static double SegmentDistance(DVec a, DVec b, DVec c, DVec d) {
            // Independent Euclidean clearance oracle; no graph progress/projection is used.
            var u = b - a; var v = d - c; var w = a - c;
            double aa = DVec.Dot(u,u), bb = DVec.Dot(u,v), cc = DVec.Dot(v,v), dd = DVec.Dot(u,w), ee = DVec.Dot(v,w);
            double denominator = aa * cc - bb * bb;
            double s = denominator > 1e-12 ? Math.Max(0, Math.Min(1, (bb * ee - cc * dd) / denominator)) : 0;
            double t = (bb * s + ee) / cc;
            if (t < 0) { t = 0; s = Math.Max(0, Math.Min(1, -dd / aa)); }
            else if (t > 1) { t = 1; s = Math.Max(0, Math.Min(1, (bb - dd) / aa)); }
            return Math.Sqrt((a + u * s - c - v * t).Squared);
        }
        static double Clearance(GraphRoad a, GraphRoad b) {
            double nearest = double.PositiveInfinity;
            for (int i = 0; i + 1 < a.samples.Count; i++) for (int j = 0; j + 1 < b.samples.Count; j++)
                nearest = Math.Min(nearest, SegmentDistance(a.samples[i].position,a.samples[i+1].position,b.samples[j].position,b.samples[j+1].position));
            return nearest;
        }
        static void Geometry(IndependentGraph graph) {
            Check(graph.Validate().Length == 0, "factory graph validation");
            Check(graph.roads.Count == 8 && graph.junctions.Count == 2 && graph.ports.Count == 8, "one split/rejoin with explicit ports");
            Check(graph.Road(IndependentGraph.Speed).Length > graph.Road(IndependentGraph.Technical).Length * 1.2, "speed road must be meaningfully longer");
            foreach (var road in graph.roads) {
                double measured = 0;
                for (int i = 1; i < road.samples.Count; i++) {
                    measured += Math.Sqrt((road.samples[i].position - road.samples[i-1].position).Squared);
                    Near(road.samples[i].distance, measured, 1e-8, "physical metres agree with canonical geometry");
                }
                Near(road.Length, measured, 1e-8, "physical road length");
                for (double distance = 0; distance <= road.Length; distance += 7.3) {
                    var cursor = graph.Cursor(road.id, distance, road.id == IndependentGraph.Exit ? IndependentGraph.Technical : 0);
                    var frame = graph.Evaluate(cursor);
                    var projections = graph.ProjectCandidates(cursor, frame.position, 20);
                    var same = projections.Single(p => p.cursor.roadId == road.id);
                    Near(same.cursor.localPhysicalDistance, distance, 1e-7, "evaluate/project round trip");
                    Near(same.squaredDistance, 0, 1e-12, "on canonical road");
                    Check(projections.All(p => Math.Abs(p.physicalDelta) <= 20 + 1e-8), "candidate window is physical metres");
                }
                foreach (var next in graph.roads.Where(r => road.toPort == r.fromPort)) {
                    var a = road.samples.Last(); var b = next.samples.First();
                    Point(a.position,b.position,"shared port position"); Point(a.tangent,b.tangent,"shared port tangent");
                    Point(a.normal,b.normal,"shared port normal"); Point(a.right,b.right,"shared port lateral basis");
                    Near(a.halfWidth,b.halfWidth,1e-10,"shared port cross-section");
                    Near(road.progressEnd,next.progressStart,1e-8,"progress at port");
                }
            }
            foreach (var node in graph.junctions) {
                Check(node.panels.Count == (node.stations.Count - 1) * 2, "one physical pair of top panels per station interval");
                Check(node.stations.Count(s => s.gapHalf == 0 && Math.Abs(s.outer - 17) < 1e-10) == 1, "exact divider tip station retained");
                for (int i = 0; i + 1 < node.stations.Count; i++) {
                    var a = node.stations[i]; var b = node.stations[i+1];
                    double dz = b.z - a.z; Check(dz > 0, "no duplicate/zero station interval");
                    var left = node.panels[i*2]; var right = node.panels[i*2+1];
                    double Area(JunctionPanel p) => .5 * (Math.Sqrt(DVec.Cross(p.b-p.a,p.c-p.a).Squared) + Math.Sqrt(DVec.Cross(p.b-p.c,p.d-p.c).Squared));
                    double expected = dz * (a.outer-a.gapHalf+b.outer-b.gapHalf);
                    Near(Area(left)+Area(right),expected,1e-7,"unique panel area neither overlaps nor fills gap");
                    Check(Area(left)>0 && Area(right)>0,"no degenerate top polygon");
                    Near(DVec.Dot(left.c-node.origin,node.right),-a.gapHalf,1e-7,"left divider boundary");
                    Near(DVec.Dot(right.a-node.origin,node.right),a.gapHalf,1e-7,"right divider boundary");
                    if (a.gapHalf == 0) Point(left.c,right.a,"common top tiles with exact shared vertex");
                    else Check((right.a-left.c).Squared > 0,"open divider exists physically");
                }
            }
            var technical = graph.Road(IndependentGraph.Technical); var speed = graph.Road(IndependentGraph.Speed);
            Check(Turn(technical) < .12 && Turn(speed) < .12, "source bend envelope retained; not physical acceptance");
            Check(Clearance(technical,speed) >= 2*IndependentGraph.HalfWidth+6,"independent roads have physical clearance");
            foreach (var road in new[] { technical, speed }) {
                for (int i = 0; i + 1 < road.samples.Count; i++) for (int j = i + 1; j + 1 < road.samples.Count; j++) {
                    if (road.samples[j].distance - road.samples[i+1].distance < 80) continue;
                    Check(SegmentDistance(road.samples[i].position,road.samples[i+1].position,road.samples[j].position,road.samples[j+1].position) > 23,
                        "road cannot overlap its distant self");
                }
            }
        }
        static void Traversal(IndependentGraph graph) {
            foreach (int branch in new[] { IndependentGraph.Technical, IndependentGraph.Speed }) {
                int[] path = branch == IndependentGraph.Technical
                    ? new[] { IndependentGraph.Approach,IndependentGraph.SplitTechnical,branch,IndependentGraph.MergeTechnical,IndependentGraph.Exit }
                    : new[] { IndependentGraph.Approach,IndependentGraph.SplitSpeed,branch,IndependentGraph.MergeSpeed,IndependentGraph.Exit };
                for (int i = 0; i + 1 < path.Length; i++) {
                    var a = graph.Road(path[i]); var b = graph.Road(path[i+1]);
                    var before = graph.Cursor(a.id,a.Length-2,a.id==IndependentGraph.Exit?branch:0);
                    var after = graph.Cursor(b.id,2,b.id==IndependentGraph.Exit?branch:0);
                    Check(graph.CanCrossPort(before,after,true,4.00001),"forward ordinary connected transition");
                    Check(!graph.CanCrossPort(before,after,true,3.99),"physical jump beyond admission window rejected");
                    Check(graph.CanCrossPort(after,before,false,4.00001),"reverse returns through incoming port");
                    var start = graph.Cursor(b.id,0,b.id==IndependentGraph.Exit?branch:0);
                    var end = graph.Cursor(a.id,a.Length,a.id==IndependentGraph.Exit?branch:0);
                    Near(graph.RaceProgress(start),graph.RaceProgress(end),1e-8,"no normalized progress jump at seam");
                }
                var exit = graph.Cursor(IndependentGraph.Exit,2,branch);
                var reverse = graph.ProjectCandidates(exit,graph.Evaluate(exit).position,20);
                Check(reverse.All(p=>p.cursor.incomingBranch==branch),"merge reverse preserves provenance");
                int wrong = branch == IndependentGraph.Technical ? IndependentGraph.MergeSpeed : IndependentGraph.MergeTechnical;
                Check(reverse.All(p=>p.cursor.roadId!=wrong),"nearest other branch does not create legal candidate");
                Check(!graph.CanCrossPort(exit,graph.Cursor(wrong,graph.Road(wrong).Length-1),false,30),"wrong incoming arm rejected at merge");
                var middle = graph.Cursor(branch,graph.Road(branch).Length*.5);
                var wrongRoad = graph.Road(branch==IndependentGraph.Technical?IndependentGraph.Speed:IndependentGraph.Technical);
                var candidates = graph.ProjectCandidates(middle,wrongRoad.Evaluate(wrongRoad.Length*.5).position,30);
                Check(candidates.Count==1 && candidates[0].cursor.roadId==branch,"mid-road wrong surface cannot rebind cursor");
                Near(middle.localPhysicalDistance,graph.Road(branch).Length*.5,0,"projection does not mutate accepted cursor");
                var far = graph.ProjectCandidates(middle,graph.Road(branch).samples.Last().position,10);
                Check(far.All(p=>Math.Abs(p.physicalDelta)<=10+1e-8),"projection cannot skip ahead on the same road");
                var complete = graph.Path(branch);
                Near(complete.Last().distance,path.Sum(id=>graph.Road(id).Length),1e-8,"path view accumulated physical distance");
                Check(complete.Zip(complete.Skip(1),(a,b)=>b.distance>a.distance).All(x=>x),"path excludes duplicate seam samples");
                var saved = complete[0]; complete[0]=default;
                Point(graph.Path(branch)[0].position,saved.position,"path view does not mutate graph");
            }
            for (int i = 0; i <= 10; i++) {
                double fraction = i/10.0;
                var a = graph.Road(IndependentGraph.Technical); var b = graph.Road(IndependentGraph.Speed);
                Near(graph.RaceProgress(graph.Cursor(a.id,a.Length*fraction)),graph.RaceProgress(graph.Cursor(b.id,b.Length*fraction)),1e-8,"fair progress interval across different physical lengths");
                Near(b.PhysicalDistance(b.Progress(b.Length*fraction)),b.Length*fraction,1e-8,"progress inverse keeps physical metres");
            }
            var speed = graph.Road(IndependentGraph.Speed); int straight = 0;
            for (int i = 0; i + 1 < speed.samples.Count; i++) {
                var a = speed.samples[i]; var b = speed.samples[i+1];
                if (Math.Abs(a.position.z-b.position.z)>1e-10) continue;
                straight++; Check(speed.Progress(b.distance)>speed.Progress(a.distance),"speed straight with constant carrier z still advances race progress");
            }
            Check(straight>50,"constant-z straight exercised");
            var stale = new RouteCursor(IndependentGraph.Technical,1,IndependentGraph.Technical,"stale-build");
            Check(!graph.IsCurrent(stale),"stale geometry cursor rejected");
            Reject(()=>graph.Evaluate(stale),"stale evaluation rejected");
            Reject(()=>graph.ProjectCandidates(stale,new DVec(),20),"stale projection rejected");
            Reject(()=>graph.Cursor(IndependentGraph.Exit,0),"merge exit must retain incoming branch");
            Reject(()=>graph.Cursor(IndependentGraph.Technical,1,IndependentGraph.Speed),"inconsistent branch history rejected");
            Reject(()=>graph.Cursor(IndependentGraph.Speed,double.NaN),"NaN cursor rejected");
            Reject(()=>graph.ProjectCandidates(graph.Cursor(IndependentGraph.Technical,1),new DVec(double.NaN,0,0),20),"nonfinite projection rejected");
            var approach = graph.Cursor(IndependentGraph.Approach,graph.Road(IndependentGraph.Approach).Length-2);
            var choices = graph.ProjectCandidates(approach,graph.Evaluate(approach).position,20);
            Check(choices.Select(p=>p.cursor.roadId).OrderBy(i=>i).SequenceEqual(new[]{IndependentGraph.Approach,IndependentGraph.SplitTechnical,IndependentGraph.SplitSpeed}),"both outgoing arms are provisional candidates at legal entry");
        }
        static void Identity(IndependentGraph graph) {
            var t = new DVec(0,0,1); var n = new DVec(0,1,0); var r = new DVec(-1,0,0);
            var replay = IndependentGraph.Create(new DVec(),t,n,r);
            Check(graph.Identity==replay.Identity,"exact deterministic standalone identity");
            var reorder = IndependentGraph.FromGeometry(graph.roads.Reverse(),graph.junctions.Reverse(),graph.guardrailMode);
            Check(graph.Identity==reorder.Identity,"canonical road/node ordering");
            var alternative = IndependentGraph.Create(new DVec(),t,n,r,"normal",230);
            Check(alternative.Identity!=graph.Identity,"alternative-only geometry changes identity");
            foreach (int id in graph.roads.Select(x=>x.id).Where(id=>id!=IndependentGraph.Speed))
                Check(graph.Road(id).samples.Select(f=>f.position).SequenceEqual(alternative.Road(id).samples.Select(f=>f.position)),"alternative-only change leaves common/technical geometry fixed");
            var node = graph.junctions[0]; var stations = node.stations.ToArray();
            int at = Array.FindIndex(stations,s=>s.z>120); var row=stations[at];
            stations[at] = new JunctionStation(row.z,row.outer+.01,row.gapHalf+.01);
            var modified = new GraphJunction(node.id,node.origin,node.tangent,node.normal,node.right,node.merging,stations);
            var changedRoads = graph.roads.Select(road=> road.id==IndependentGraph.SplitTechnical || road.id==IndependentGraph.SplitSpeed
                ? new GraphRoad(road.id,road.kind,road.fromPort,road.toPort,road.branch,modified.Guide(road.branch,true),road.progressStart,road.progressEnd) : road).ToArray();
            var changed = IndependentGraph.FromGeometry(changedRoads,new[]{modified,graph.junctions[1]},graph.guardrailMode);
            Check(changed.Identity!=graph.Identity,"junction-only change including its stored guides changes identity");
            Reject(()=>IndependentGraph.FromGeometry(graph.roads,new[]{modified,graph.junctions[1]},graph.guardrailMode),"patch/guide divergence rejected");
            var fakeMerge = new GraphJunction(4,node.origin,node.tangent,node.normal,node.right,false,node.stations);
            Reject(()=>IndependentGraph.FromGeometry(graph.roads,new[]{node,fakeMerge},graph.guardrailMode),"split cannot impersonate merge node");
            var guide = graph.Road(IndependentGraph.SplitTechnical);
            for(int mutation=0;mutation<3;mutation++) {
                var frames=guide.samples.ToArray();int k=frames.Length/2;var f=frames[k];
                var tangent=mutation==1?f.tangent.Rotate(f.normal,.01):f.tangent;
                frames[k]=new GraphFrame(f.distance,f.position,tangent,f.normal,DVec.Cross(tangent,f.normal).Unit(),
                    f.halfWidth+(mutation==0?.1:0),mutation==2?!f.railLeft:f.railLeft,f.railRight);
                var corrupt=new GraphRoad(guide.id,guide.kind,guide.fromPort,guide.toPort,guide.branch,frames,guide.progressStart,guide.progressEnd);
                Reject(()=>IndependentGraph.FromGeometry(graph.roads.Select(x=>x.id==guide.id?corrupt:x),graph.junctions,graph.guardrailMode),
                    "guide width/basis/rail mutation cannot diverge from physical patch");
            }
            var road0=graph.Road(IndependentGraph.Technical);
            var wrongTopology=new GraphRoad(road0.id,road0.kind,99,road0.toPort,road0.branch,road0.samples,road0.progressStart,road0.progressEnd);
            Reject(()=>IndependentGraph.FromGeometry(graph.roads.Select(x=>x.id==road0.id?wrongTopology:x),graph.junctions,graph.guardrailMode),"disconnected road metadata rejected");
            Reject(()=>IndependentGraph.Create(new DVec(),t*2,n,r),"nonunit source basis rejected");
            Reject(()=>IndependentGraph.Create(new DVec(),t,t,r),"nonorthogonal source basis rejected");
            Reject(()=>IndependentGraph.Create(new DVec(),t,n,r*-1),"wrong source handedness rejected");
            Reject(()=>IndependentGraph.Create(new DVec(double.PositiveInfinity,0,0),t,n,r),"nonfinite origin rejected");
        }
        static void Legacy() {
            uint[] seeds={7,8,9,1,6,10,2,3,4}; string[] hashes={"637b2ea0","44f12b26","372c0ea9","de66b63e","2ba88b15","d5251e5c","86ddaced","5e163d7d","7c6d5307"};
            Check(Generator.NativeRevision==11,"domain foundation must not activate unfinished revision12");
            for (int i=0;i<seeds.Length;i++) {
                var d=Generator.Generate(seeds[i],"normal","cloud-city",seeds[i]%2==0,true,11);
                Check(d.hash==hashes[i],"revision11 golden identity from first-stage evidence");
                string before=JsonUtility.ToJson(d);
                var graph=IndependentGraph.SingleRoad(d);
                Check(graph.legacy && graph.roads.Count==1 && graph.junctions.Count==0,"single-road legacy adapter");
                Near(graph.CommonLength,d.totalLength,0,"legacy canonical length preserved");
                var path=graph.Path(0);
                for (int k=0;k<d.samples.Length;k++) {
                    Near(path[k].distance,d.samples[k].distance,0,"legacy source metric unchanged");
                    Point(path[k].position,d.samples[k].position,"legacy source position unchanged");
                }
                Check(JsonUtility.ToJson(d)==before,"legacy adapter does not mutate definition");
            }
        }
        static void Export(IndependentGraph graph,string output) {
            using (var csv=new StreamWriter(Path.Combine(output,"graph-geometry.csv"))) {
                csv.WriteLine("road,distance,x,y,z,tx,ty,tz,nx,ny,nz,rx,ry,rz,halfWidth,leftRail,rightRail");
                string Number(double d)=>d.ToString("R",CultureInfo.InvariantCulture);
                foreach(var road in graph.roads)foreach(var f in road.samples) csv.WriteLine(string.Join(",",new[]{road.id.ToString(),Number(f.distance),Number(f.position.x),Number(f.position.y),Number(f.position.z),Number(f.tangent.x),Number(f.tangent.y),Number(f.tangent.z),Number(f.normal.x),Number(f.normal.y),Number(f.normal.z),Number(f.right.x),Number(f.right.y),Number(f.right.z),Number(f.halfWidth),f.railLeft?"1":"0",f.railRight?"1":"0"}));
            }
            using(var csv=new StreamWriter(Path.Combine(output,"junction-stations.csv"))) {
                csv.WriteLine("junction,z,outer,gapHalf");
                foreach(var node in graph.junctions)foreach(var s in node.stations)csv.WriteLine(FormattableString.Invariant($"{node.id},{s.z:R},{s.outer:R},{s.gapHalf:R}"));
            }
        }
        public static void Run() {
            string output=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_QA_DIR");
            if(string.IsNullOrEmpty(output))throw new Exception("STAR_RACING_GRAPH_QA_DIR required");
            Directory.CreateDirectory(output); assertions=0;
            var t=new DVec(0,0,1); var n=new DVec(0,1,0); var r=new DVec(-1,0,0);
            var graph=IndependentGraph.Create(new DVec(),t,n,r);
            Geometry(graph); Traversal(graph); Identity(graph); Legacy();
            var axis=new DVec(.3,.7,.2).Unit(); const double angle=.81;
            var origin=new DVec(123,45,-67);
            foreach(string mode in new[]{"normal","full","none"}) {
                var transformed=IndependentGraph.Create(origin,t.Rotate(axis,angle),n.Rotate(axis,angle),r.Rotate(axis,angle),mode);
                Geometry(transformed);
                foreach(var road in transformed.roads)for(int i=0;i<road.samples.Count;i++) {
                    var original=graph.Road(road.id).samples[i]; var sample=road.samples[i];
                    Point(sample.position,origin+original.position.Rotate(axis,angle),"rigid transform invariant geometry");
                    Near(sample.distance,original.distance,1e-7,"rigid transform invariant physical metric");
                    Check(sample.railLeft==(mode!="none") && sample.railRight==(mode!="none"),"rail mode metadata");
                }
                Check(transformed.Identity!=graph.Identity,"full geometry/settings included in identity");
            }
            Export(graph,output);
            TrackCompositionChecks.Run();
            var metrics=new Metrics{graphAssertions=assertions,legacyAssertions=TrackCompositionChecks.Assertions,graphRevision=IndependentGraph.NativeRevision,
                activeRevision=Generator.NativeRevision,technicalLength=graph.Road(IndependentGraph.Technical).Length,speedLength=graph.Road(IndependentGraph.Speed).Length,
                technicalMaxTurnPer5m=Turn(graph.Road(IndependentGraph.Technical)),speedMaxTurnPer5m=Turn(graph.Road(IndependentGraph.Speed)),
                minimumRoadSeparation=Clearance(graph.Road(IndependentGraph.Technical),graph.Road(IndependentGraph.Speed)),identity=graph.Identity,
                status="domain-foundation-only; physical runtime integration and acceptance pending"};
            File.WriteAllText(Path.Combine(output,"metrics.json"),JsonUtility.ToJson(metrics,true));
            File.WriteAllText(Path.Combine(output,"result.txt"),$"INDEPENDENT_GRAPH_CHECKS_OK assertions={assertions} legacy={metrics.legacyAssertions} activeRevision=11 domainRevision=12\n");
            Debug.Log($"INDEPENDENT_GRAPH_CHECKS_OK assertions={assertions} legacy={metrics.legacyAssertions}");
        }
    }
}
