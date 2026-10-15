using System;
using System.Linq;
using System.Collections.Generic;
namespace StarRacingPrototype.Procedural {
 // Conservative continuous capsule clearance. This is a geometric candidate gate;
 // actual support/CCD, vehicle traversal and composition contracts remain separate.
 public static class GraphCourseSafety {
  public const int Profile=1;
  public const double Clearance=2.5,LocalWindow=120,FlightLow=-.6,FlightHigh=20,GroundHeight=3,ShellDepth=1,RailOutset=.3;
  sealed class Sweep {
   public int geometry;public double from,to,length,radius;public DVec a,b;
   public int[] startPorts,endPorts;public GraphJump flight;
   public double MinX=>Math.Min(a.x,b.x)-radius;
   public double MaxX=>Math.Max(a.x,b.x)+radius;
  }
  public static string[] Validate(IndependentGraph graph) {
   if(graph==null)return new[]{"missing-graph"};
   var errors=new List<string>(graph.Validate());if(errors.Count>0||graph.legacy)return errors.Concat(graph.legacy?new[]{"independent-required"}:Array.Empty<string>()).ToArray();
   // Exact supported compiler inventory: common nonplanar; planar unique branches/nodes.
   var carrier=graph.junctions[0];
   foreach(var road in graph.roads.Where(r=>new[]{0,1,2,5}.Contains(r.id))) {
    foreach(var f in road.samples) {
     if(f.halfWidth!=IndependentGraph.HalfWidth||f.railLeft!=(graph.guardrailMode!="none")||f.railRight!=(graph.guardrailMode!="none"))errors.Add("physical-metadata-"+road.id);
     if((road.id==1||road.id==2)&&((f.normal-carrier.normal).Squared>1e-16||Math.Abs(DVec.Dot(f.position-carrier.origin,carrier.normal))>1e-8))errors.Add("nonplanar-branch-"+road.id);
     bool collar=road.id==0&&f.distance>=road.Length-50||road.id==5&&f.distance<=50;
     if(collar&&((f.normal-carrier.normal).Squared>1e-16||(f.tangent-carrier.tangent).Squared>1e-16||Math.Abs(DVec.Dot(f.position-carrier.origin,carrier.normal))>1e-8))errors.Add("junction-collar-"+road.id);
    }
    for(int i=0;i+1<road.samples.Count;i++) {
     var a=road.samples[i];var b=road.samples[i+1];var direction=(b.position-a.position).Unit();
     if(DVec.Dot(a.tangent,direction)<.7||DVec.Dot(b.tangent,direction)<.7)errors.Add("folded-ribbon-"+road.id);
    }
    if(road.id==0||road.id==5) {
     var f=road.Evaluate(road.id==0?road.Length-50:50);
     if((f.normal-carrier.normal).Squared>1e-16||(f.tangent-carrier.tangent).Squared>1e-16||Math.Abs(DVec.Dot(f.position-carrier.origin,carrier.normal))>1e-8)errors.Add("junction-collar-"+road.id);
    }
   }
   foreach(var node in graph.junctions)if((node.normal-carrier.normal).Squared>1e-16||Math.Abs(DVec.Dot(node.origin-carrier.origin,carrier.normal))>1e-8)errors.Add("nonplanar-junction");
   if(graph.jumps.Count!=0&&(graph.jumps.Count!=4||new[]{0,5}.Any(id=>graph.jumps.Count(j=>j.roadId==id)!=2)))errors.Add("four-common-runways");
   if(errors.Count>0)return errors.Distinct().ToArray();
   var sweeps=new List<Sweep>();
   foreach(var road in graph.roads.Where(r=>new[]{0,1,2,5}.Contains(r.id)))for(int i=0;i+1<road.samples.Count;i++) {
    var a=road.samples[i];var b=road.samples[i+1];int pieces=(int)Math.Ceiling((b.distance-a.distance)/5);
    for(int k=0;k<pieces;k++) {
     var start=road.Evaluate(a.distance+(b.distance-a.distance)*k/pieces);var end=road.Evaluate(a.distance+(b.distance-a.distance)*(k+1)/pieces);
     sweeps.Add(new Sweep{geometry=road.id,from=start.distance,to=end.distance,length=road.Length,a=start.position,b=end.position,radius=Math.Sqrt(Math.Pow(IndependentGraph.HalfWidth+(graph.guardrailMode=="none"?0:RailOutset),2)+Math.Pow(Math.Max(GroundHeight,ShellDepth),2)),startPorts=new[]{road.fromPort},endPorts=new[]{road.toPort}});
    }
   }
   foreach(var node in graph.junctions)for(int i=0;i+1<node.stations.Count;i++) {
    var a=node.stations[i];var b=node.stations[i+1];
    sweeps.Add(new Sweep{geometry=node.id,from=a.z,to=b.z,length=IndependentGraph.JunctionLength,a=node.Point(0,a.z),b=node.Point(0,b.z),radius=Math.Sqrt(Math.Pow(Math.Max(a.outer,b.outer)+(graph.guardrailMode=="none"?0:RailOutset),2)+GroundHeight*GroundHeight),startPorts=node.merging?new[]{21,22}:new[]{10},endPorts=node.merging?new[]{30}:new[]{11,12}});
   }
   foreach(var jump in graph.jumps) {
    var road=graph.Road(jump.roadId);var a=road.Evaluate(jump.rampStart);var b=road.Evaluate(jump.ballisticLanding);
    // Sphere encloses the complete road width and[-.6,20] normal-height prism.
    double halfHeight=(FlightHigh-FlightLow)/2,mid=(FlightHigh+FlightLow)/2;
    sweeps.Add(new Sweep{geometry=road.id,from=jump.rampStart,to=jump.ballisticLanding,length=road.Length,a=a.position+a.normal*mid,b=b.position+b.normal*mid,radius=Math.Sqrt(IndependentGraph.HalfWidth*IndependentGraph.HalfWidth+halfHeight*halfHeight),startPorts=new[]{road.fromPort},endPorts=new[]{road.toPort},flight=jump});
   }
   sweeps.Sort((a,b)=>a.MinX.CompareTo(b.MinX));
   for(int i=0;i<sweeps.Count;i++)for(int j=i+1;j<sweeps.Count;j++) {
    var a=sweeps[i];var b=sweeps[j];if(b.MinX>a.MaxX+Clearance)break;
    double required=a.radius+b.radius+Clearance;
    if(Math.Max(Math.Min(a.a.y,a.b.y)-Math.Max(b.a.y,b.b.y),Math.Min(b.a.y,b.b.y)-Math.Max(a.a.y,a.b.y))>=required||
       Math.Max(Math.Min(a.a.z,a.b.z)-Math.Max(b.a.z,b.b.z),Math.Min(b.a.z,b.b.z)-Math.Max(a.a.z,a.b.z))>=required)continue;
    if(ExpectedNeighbor(a,b))continue;
    if(SegmentSquared(a.a,a.b,b.a,b.b)<required*required)errors.Add($"clearance-{a.geometry}:{a.from:R}-{b.geometry}:{b.from:R}"+(a.flight!=null||b.flight!=null?"-flight":""));
   }
   return errors.Distinct().ToArray();
  }
  static bool ExpectedNeighbor(Sweep a,Sweep b) {
   if(a.geometry==b.geometry) {
    if(a.flight!=null&&b.flight==null)return b.from>=a.flight.runwayStart&&b.to<=a.flight.runwayEnd;
    if(b.flight!=null&&a.flight==null)return a.from>=b.flight.runwayStart&&a.to<=b.flight.runwayEnd;
    if(a.flight!=null||b.flight!=null)return false;
    return Math.Max(a.from,b.from)-Math.Min(a.to,b.to)<LocalWindow;
   }
   if(a.flight!=null||b.flight!=null)return false;
   double[] da={a.from,a.length-a.to},db={b.from,b.length-b.to};int[][] pa={a.startPorts,a.endPorts},pb={b.startPorts,b.endPorts};
   for(int x=0;x<2;x++)for(int y=0;y<2;y++)if(da[x]+db[y]<LocalWindow&&pa[x].Intersect(pb[y]).Any())return true;
   return false;
  }
  // Closest points on finite segments, including parallel anddegenerate inputs.
  public static double SegmentSquared(DVec p,DVec q,DVec r,DVec s) {
   var u=q-p;var v=s-r;var w=p-r;double a=u.Squared,b=DVec.Dot(u,v),c=v.Squared,d=DVec.Dot(u,w),e=DVec.Dot(v,w),t;
   if(a==0&&c==0)return w.Squared;
   if(a==0)return (p-(r+v*Math.Max(0,Math.Min(1,e/c)))).Squared;
   if(c==0)return (p+u*Math.Max(0,Math.Min(1,-d/a))-r).Squared;
   double den=a*c-b*b,x=den>1e-12*a*c?Math.Max(0,Math.Min(1,(b*e-c*d)/den)):0;
   t=(b*x+e)/c;
   if(t<0){t=0;x=Math.Max(0,Math.Min(1,-d/a));}else if(t>1){t=1;x=Math.Max(0,Math.Min(1,(b-d)/a));}
   return (w+u*x-v*t).Squared;
  }
 }
}
