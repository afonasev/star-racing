using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
 public readonly struct GraphAiObservation {
  public readonly AiObservation Motion;
  public readonly GraphRaceObservation Route;
  public readonly int Intent;
  public readonly Vector3 Velocity;
  public double CommonProgress=>Route.CommonProgress;
  public bool Valid=>Route.Valid && Motion.Revision==Route.PositionRevision && Route.Owner.RaceObservation.Tick==Route.Tick;
  public GraphAiObservation(AiObservation motion,GraphRaceObservation route,int intent=0,Vector3? velocity=null){Motion=motion;Route=route;Intent=intent;Velocity=velocity??motion.Forward*motion.Speed;}
 }
 // Captured as a whole before any driver's decision. Intent is from the previous
 // decision; sequential driver evaluation never mutates this snapshot.
 public sealed class GraphAiWorldSnapshot {
  readonly GraphAiObservation[] cars;
  public readonly GraphNavigation Navigation;
  public int Count=>cars.Length;
  public GraphAiObservation this[int id]=>cars[id];
  public double Time {get;private set;}
  public GraphAiWorldSnapshot(GraphRoadSurface surface,int count,ReleaseBalance balance) {
   if(count<1||count>64)throw new ArgumentOutOfRangeException(nameof(count));cars=new GraphAiObservation[count];Navigation=new GraphNavigation(surface,balance);
  }
  public void Capture(MagneticVehicle[] vehicles,AiDriver[] drivers,double time) {
   if(vehicles.Length!=Count || drivers!=null&&drivers.Length!=Count)throw new ArgumentException("Graph roster mismatch");Time=time;
   for(int i=0;i<Count;i++) {
    var c=vehicles[i];var f=c.Frame;var route=RaceDirector.ObserveVehicle(c).graph;
    var m=new AiObservation{Id=i,Revision=c.PositionRevision,Distance=c.Distance,Speed=Vector3.Dot(c.Body.linearVelocity,c.transform.forward),Lateral=Vector3.Dot(c.Body.position-f.position,f.right),LateralSpeed=Vector3.Dot(c.Body.linearVelocity,f.right),Charge=c.Drive==null?0:c.Drive.Charge,Grounded=c.Telemetry.grounded,Recovering=c.Telemetry.recovering,Finished=c.FinishedCoasting,Position=c.Body.position,Forward=c.transform.forward,Up=c.transform.up,YawRate=Vector3.Dot(c.Body.angularVelocity,f.normal),LateralExtent=VehicleGeometry.LateralExtent(Mathf.Atan2(Vector3.Dot(c.transform.forward,f.right),Vector3.Dot(c.transform.forward,f.tangent)))};
    cars[i]=new GraphAiObservation(m,route,drivers==null?0:drivers[i]?.GraphChoice??0,c.Body.linearVelocity);
   }
  }
 }
 // Domain-only physical path queries. No collider scans, support/admission writes,
 // closest-road selection or mutable route cursor. Prepared once per world.
 public sealed class GraphNavigation {
  public readonly GraphRoadSurface Surface;
  public readonly IndependentGraph Graph;
  public readonly int BuildRevision;
  readonly Dictionary<int,float[]> limits=new Dictionary<int,float[]>();
  readonly int[] technical={0,10,1,12,5},speed={0,11,2,13,5};
  readonly float brake,acceleration,baseLimit;
  readonly GraphRoad[] technicalRoads,speedRoads;
  readonly GraphJunction split,merge;
  public readonly double MergeConflictStation;
  public bool Current=>Surface!=null&&Surface.isActiveAndEnabled&&Surface.Graph==Graph&&Surface.BuildRevision==BuildRevision;
  public GraphNavigation(GraphRoadSurface surface,ReleaseBalance balance) {
   if(surface==null||surface.Graph==null||surface.Graph.legacy)throw new ArgumentException("Independent surface required");Surface=surface;Graph=surface.Graph;BuildRevision=surface.BuildRevision;
   brake=balance["brakeDeceleration"];acceleration=balance["acceleration"]+balance["nitroAcceleration"];baseLimit=balance["baseSpeedKmh"]/3.6f;
   float boost=balance["nitroMaxSpeedKmh"]/3.6f;
   foreach(var junction in Graph.junctions){if(junction.merging)merge=junction;else split=junction;}
   foreach(var road in Graph.roads) {
    var values=new float[road.samples.Count];
    for(int i=0;i<values.Length;i++) {
     var a=road.samples[Math.Max(0,i-1)];var b=road.samples[Math.Min(values.Length-1,i+1)];var f=road.samples[i];
     float span=(float)Math.Max(.001,b.distance-a.distance);
     float lateral=(float)Math.Abs(DVec.Dot(DVec.Cross(a.tangent,b.tangent),f.normal))/span;
     float vertical=(float)DVec.Dot(b.tangent-a.tangent,f.normal)/span;
     float limit=AiDriver.HandlingCurveSpeedFor(balance,lateral);
     if(road.id==10||road.id==11||road.id==12||road.id==13)limit=Mathf.Min(limit,Mathf.Sqrt(24/Mathf.Max(.00001f,lateral)));
     if(vertical<0)limit=Mathf.Min(limit,AiDriver.CrestSpeedLimit(-vertical,-Vector3.Dot(Physics.gravity,f.normal.Float),boost));
     if(Graph.jumps.Any(j=>j.roadId==road.id&&f.distance>=j.rampStart&&f.distance<=j.tacticalLanding))limit=Mathf.Min(limit,45);
     values[i]=Mathf.Max(10,limit);
    }
    // The same braking reserve as ordinary AI. Cross-port hazards are additionally
    // covered by the decision horizon; no normalized progress enters this pass.
    for(int i=values.Length-2;i>=0;i--)values[i]=Mathf.Min(values[i],Mathf.Sqrt(values[i+1]*values[i+1]+2*brake*.78f*(float)(road.samples[i+1].distance-road.samples[i].distance)));
    limits.Add(road.id,values);
   }
   technicalRoads=technical.Select(Graph.Road).ToArray();speedRoads=speed.Select(Graph.Road).ToArray();
   var mergeRoad=Graph.Road(12);MergeConflictStation=mergeRoad.Length;
   for(int i=0;i<mergeRoad.samples.Count;i++) {
    var own=mergeRoad.samples[i];var other=Graph.Road(13).Evaluate(own.distance);
    if(Math.Sqrt((own.position-other.position).Squared)<=VehicleGeometry.Width+1){MergeConflictStation=own.distance;break;}
   }
  }
  static int Index(int road)=>road==0?0:road==10||road==11?1:road==1||road==2?2:road==12||road==13?3:road==5?4:-1;
  int[] Path(int branch)=>branch==1?technical:branch==2?speed:throw new ArgumentException("Branch required");
  public RouteCursor At(RouteCursor cursor,int branch,double ahead) {
   int index=Index(cursor.roadId);if(index<0||!Graph.IsCurrent(cursor))throw new ArgumentException("Current cursor required");var path=Path(branch);
   var old=Graph.Road(cursor.roadId);var road=Graph.Road(path[index]);
   double at=old.id==road.id?cursor.localPhysicalDistance:road.PhysicalDistance(old.Progress(cursor.localPhysicalDistance));at+=ahead;
   while(at>road.Length&&index<4){at-=road.Length;road=Graph.Road(path[++index]);}
   while(at<0&&index>0){road=Graph.Road(path[--index]);at+=road.Length;}
   return Graph.Cursor(road.id,Math.Max(0,Math.Min(road.Length,at)),road.id==5?branch:0);
  }
  public GraphFrame Frame(RouteCursor cursor,int branch,double ahead)=>Graph.Evaluate(At(cursor,branch,ahead));
  public float LocalSpeed(RouteCursor cursor,int branch,double ahead) {
   var at=At(cursor,branch,ahead);var road=Graph.Road(at.roadId);var a=limits[road.id];int lo=0,hi=a.Length-1;
   while(lo<hi){int mid=(lo+hi+1)/2;if(road.samples[mid].distance<=at.localPhysicalDistance)lo=mid;else hi=mid-1;}
   if(lo==a.Length-1)return a[lo];double t=(at.localPhysicalDistance-road.samples[lo].distance)/(road.samples[lo+1].distance-road.samples[lo].distance);return Mathf.Lerp(a[lo],a[lo+1],(float)t);
  }
  double Coordinate(RouteCursor cursor,int branch) {
   var path=Path(branch);int index=Index(cursor.roadId);double result=0;
   for(int i=0;i<index;i++)result+=Graph.Road(path[i]).Length;
   var own=Graph.Road(cursor.roadId);return result+Graph.Road(path[index]).PhysicalDistance(own.Progress(cursor.localPhysicalDistance));
  }
  public bool PhysicalGap(GraphAiObservation self,GraphAiObservation other,int branch,out float gap) {
   gap=0;if(!self.Valid||!other.Valid||!ReferenceEquals(self.Route.Surface,Surface)||!ReferenceEquals(other.Route.Surface,Surface))return false;
   int road=other.Route.Cursor.roadId;
   if(road!=0&&road!=5 && other.Route.Branch!=0&&other.Route.Branch!=branch)return false;
   // Opposite admitted arms do not share following/pressure even inside a junction.
   if(self.Route.Branch!=0&&other.Route.Branch!=0&&self.Route.Branch!=other.Route.Branch && self.Route.Cursor.roadId!=5&&road!=5)return false;
   gap=(float)(Coordinate(other.Route.Cursor,branch)-Coordinate(self.Route.Cursor,branch));return true;
  }
  public double ToMerge(GraphAiObservation observation) {
   int branch=observation.Route.Branch!=0?observation.Route.Branch:observation.Intent;
   if(branch!=1&&branch!=2)return double.PositiveInfinity;
   return Coordinate(Graph.Cursor(branch==1?12:13,MergeConflictStation),branch)-Coordinate(observation.Route.Cursor,branch);
  }
  static double Gap(GraphJunction node,double z,out double outer) {
   var a=node.stations[0];var b=a;foreach(var row in node.stations){b=row;if(row.z>=z)break;a=row;}
   double t=b.z>a.z?Math.Max(0,Math.Min(1,(z-a.z)/(b.z-a.z))):0;outer=a.outer+(b.outer-a.outer)*t;return a.gapHalf+(b.gapHalf-a.gapHalf)*t;
  }
  static bool JunctionPoint(GraphJunction node,Vector3 point,int branch) {
   var p=new DVec(point.x,point.y,point.z)-node.origin;double z=DVec.Dot(p,node.tangent);if(z<0||z>node.Length)return true;
   double lateral=DVec.Dot(p,node.right),gap=Gap(node,z,out double outer),extent=VehicleGeometry.HalfWidth+.2;
   if(Math.Abs(lateral)+extent>outer)return false;
   return gap<=1e-8 || Math.Abs(lateral)-extent>=gap && (branch==1?lateral<0:lateral>0);
  }
  static bool DividerChord(GraphJunction node,Vector3 start,Vector3 end) {
   var a=new DVec(start.x,start.y,start.z)-node.origin;var b=new DVec(end.x,end.y,end.z)-node.origin;
   double x=DVec.Dot(a,node.right),y=DVec.Dot(b,node.right);if(x*y>0||x==y)return false;
   var hit=a+(b-a)*(-x/(y-x));double z=DVec.Dot(hit,node.tangent);return z>=0&&z<=node.Length&&Gap(node,z,out _)>0;
  }
  public bool HasCorridor(GraphAiObservation self,int branch,float lane,float look) {
   using var profile=GraphRuntimeProfile.HasCorridor.Auto();
   var target=Frame(self.Route.Cursor,branch,look);Vector3 end=target.position.Float-target.right.Float*lane+target.normal.Float*VehicleGeometry.AdheredBodyHeight;
   if(DividerChord(split,self.Motion.Position,end)||DividerChord(merge,self.Motion.Position,end))return false;
   int steps=Math.Max(1,(int)Math.Ceiling(look/2));
   for(int i=0;i<=steps;i++) {
    float t=i/(float)steps;var p=Vector3.Lerp(self.Motion.Position,end,t);
    if(!JunctionPoint(split,p,branch)||!JunctionPoint(merge,p,branch))return false;
    var at=At(self.Route.Cursor,branch,look*t);var road=Graph.Road(at.roadId);
    GraphFrame frame;using(GraphRuntimeProfile.CorridorProject.Auto())frame=road.Project(new DVec(p.x,p.y,p.z),Math.Max(0,at.localPhysicalDistance-30),Math.Min(road.Length,at.localPhysicalDistance+30),out _);
    double lateral=DVec.Dot(new DVec(p.x,p.y,p.z)-frame.position,frame.right);
    if(Math.Abs(lateral)+VehicleGeometry.HalfWidth+.2>frame.halfWidth)return false;
    foreach(var jump in Graph.jumps.Where(j=>j.roadId==road.id && at.localPhysicalDistance>=j.launch-1.75&&at.localPhysicalDistance<=j.gapEnd+1.75)) {
     if(Graph.guardrailMode=="full")continue;
     if(jump.mandatory){if(!jump.ContainsLane(lateral,2.1))return false;}
     else if(Math.Abs(lateral-jump.lateralCenter)<jump.lateralHalfWidth+2.1)return false;
    }
   }
   return true;
  }
  public bool CanEnter(GraphAiObservation self,int branch) {
   if(self.Route.Branch!=0)return self.Route.Branch==branch;
   if(self.Route.Cursor.roadId==10||self.Route.Cursor.roadId==11) {
    if(self.Route.Cursor.incomingBranch!=branch) {
     double z=DVec.Dot(new DVec(self.Motion.Position.x,self.Motion.Position.y,self.Motion.Position.z)-split.origin,split.tangent),tip=split.Length;
     foreach(var row in split.stations)if(row.gapHalf>0){tip=row.z;break;}
     var desired=Frame(self.Route.Cursor,branch,0);double shift=Vector3.Distance(self.Motion.Position,desired.position.Float+desired.normal.Float*VehicleGeometry.AdheredBodyHeight);
     if(tip-z<Math.Max(8,self.Motion.Speed)*(shift/1.6+.75)+VehicleGeometry.Length)return false;
    }
   }
   return HasCorridor(self,branch,0,Mathf.Clamp(Math.Abs(self.Motion.Speed)*.55f,12,35));
  }
  public static bool SweptConflict(Vector3 ownStart,Vector3 ownEnd,Vector3 otherStart,Vector3 otherEnd,Vector3 tangent,Vector3 right,Vector3 normal,float width) {
   var a=otherStart-ownStart;var b=otherEnd-ownEnd;
   if(Math.Min(Math.Abs(Vector3.Dot(a,normal)),Math.Abs(Vector3.Dot(b,normal)))>3)return false;
   float enter=0,leave=1;
   bool Axis(float start,float end,float extent) {
    float delta=end-start;if(Math.Abs(delta)<.00001f)return Math.Abs(start)<extent;
    float lo=(-extent-start)/delta,hi=(extent-start)/delta;if(lo>hi){float swap=lo;lo=hi;hi=swap;}
    enter=Mathf.Max(enter,lo);leave=Mathf.Min(leave,hi);return enter<=leave;
   }
   return Axis(Vector3.Dot(a,tangent),Vector3.Dot(b,tangent),VehicleGeometry.Length+.25f)&&Axis(Vector3.Dot(a,right),Vector3.Dot(b,right),width)&&enter<=1&&leave>=0;
  }
  // Only the numerical contact band inside the existing exact rear exemption.
  // Keep separating comparisons exact: neither axis may deepen its overlap.
  static bool NumericRearContact(float rear,float futureRear,float sideStart,float sideEnd,float width,bool supported,float heading,float targetSide,float travel) {
   return rear> -VehicleGeometry.Length&&Mathf.Approximately(-rear,VehicleGeometry.Length)&&futureRear<=rear&&
    supported&&travel>=0&&heading>.95f&&Mathf.Abs(targetSide)<.25f&&Mathf.Abs(sideStart)<width&&
    ((sideStart<=0&&sideEnd<=sideStart)||(sideStart>=0&&sideEnd>=sideStart));
  }
  public bool PredictedObstacle(GraphAiWorldSnapshot world,GraphAiObservation self,int branch,float lane,float time,float travel,out int conflict,int from=0) {
   using var profile=GraphRuntimeProfile.PredictedObstacle.Auto();
   conflict=-1;var f=Graph.Evaluate(self.Route.Cursor);var target=Frame(self.Route.Cursor,branch,travel);
   Vector3 end=target.position.Float-target.right.Float*lane+target.normal.Float*VehicleGeometry.AdheredBodyHeight;
   for(int i=from;i<world.Count;i++) {
    var other=world[i];if(i==self.Motion.Id||!other.Valid||other.Motion.Finished)continue;
    // Independent roads are separated by actual space; converging and stationary
    // opposite-arm footprints remain obstacles irrespective of race history.
    Vector3 otherEnd=other.Motion.Position+other.Velocity*time;
    // A rear footprint already behind the actual body and separating cannot
    // be made safer by stopping the leader. Keep every new/crossing encounter.
    float rear=Vector3.Dot(other.Motion.Position-self.Motion.Position,f.tangent.Float);
    float futureRear=Vector3.Dot(otherEnd-end,f.tangent.Float);
    if(rear<=-VehicleGeometry.Length && futureRear<=rear)continue;
    float width=Mathf.Max(VehicleGeometry.HalfWidth,self.Motion.LateralExtent)+Mathf.Max(VehicleGeometry.HalfWidth,other.Motion.LateralExtent)+.15f;
    if(rear> -VehicleGeometry.Length&&Mathf.Approximately(-rear,VehicleGeometry.Length)&&NumericRearContact(rear,futureRear,
     Vector3.Dot(other.Motion.Position-self.Motion.Position,-f.right.Float),Vector3.Dot(otherEnd-end,-f.right.Float),width,
     self.Motion.Grounded&&other.Motion.Grounded&&!self.Route.Flight&&!other.Route.Flight,
     Vector3.Dot(self.Motion.Forward,f.tangent.Float),Vector3.Dot(end-self.Motion.Position,-f.right.Float),travel))continue;
    // Two resting bumpers can sit inside the conservative query buffer. A
    // supported leader moving straight ahead leaves that existing buffer;
    // holding it only deadlocks the follower. Use actual resting velocities
    // (the existing .08 stop deadband), not a rounded projection endpoint.
    // Retain closing/crossing traffic and every airborne encounter.
    bool restingRear=self.Motion.Grounded&&other.Motion.Grounded&&!self.Route.Flight&&!other.Route.Flight&&
     self.Velocity.sqrMagnitude<.25f&&other.Velocity.sqrMagnitude<.25f&&rear< -VehicleGeometry.HalfLength&&travel>=0&&
     Vector3.Dot(other.Velocity-self.Velocity,f.tangent.Float)<=.08f&&Vector3.Dot(self.Motion.Forward,f.tangent.Float)>.95f&&
     Mathf.Abs(Vector3.Dot(other.Motion.Position-self.Motion.Position,-f.right.Float))<width&&
     Mathf.Abs(Vector3.Dot(end-self.Motion.Position,-f.right.Float))<.25f;
    if(restingRear)continue;
    if(SweptConflict(self.Motion.Position,end,other.Motion.Position,otherEnd,f.tangent.Float,-f.right.Float,f.normal.Float,width)){conflict=i;return true;}
   }
   return false;
  }
  public double ChoiceSeconds(GraphAiWorldSnapshot world,GraphAiObservation self,int branch) {
   using var profile=GraphRuntimeProfile.ChoiceSeconds.Auto();
   double total=0;float velocity=Mathf.Max(1,self.Motion.Speed);var start=At(self.Route.Cursor,branch,0);
   // Remaining approach/split/branch/merge in physical metres under the shared
   // acceleration/braking envelope. The exit is identical for both candidates.
   double end=Coordinate(Graph.Cursor(branch==1?12:13,Graph.Road(branch==1?12:13).Length),branch)-Coordinate(start,branch);
   // This horizon is monotonic and its graph/limits are immutable. Resolve
   // samples locally; do not retain observation-dependent results across calls.
   var path=branch==1?technicalRoads:speedRoads;int startIndex=Index(start.roadId),sampleRoad=-1,sampleIndex=0;float[] values=null;
   for(double ahead=0;ahead<end;ahead+=5) {
    int index=startIndex;var road=path[index];double at=start.localPhysicalDistance;at+=ahead;
    // Recompute from start in the original subtraction order. Incrementally
    // accumulating distance or prefix sums would change floating-point bits.
    while(at>road.Length&&index<4){at-=road.Length;road=path[++index];}
    at=Math.Max(0,Math.Min(road.Length,at));
    if(sampleRoad!=index){sampleRoad=index;sampleIndex=0;values=limits[road.id];}
    while(sampleIndex+1<values.Length&&road.samples[sampleIndex+1].distance<=at)sampleIndex++;
    float limit;
    if(sampleIndex==values.Length-1)limit=values[sampleIndex];
    else {double t=(at-road.samples[sampleIndex].distance)/(road.samples[sampleIndex+1].distance-road.samples[sampleIndex].distance);limit=Mathf.Lerp(values[sampleIndex],values[sampleIndex+1],(float)t);}
    float ds=(float)Math.Min(5,end-ahead);
    float next=Mathf.Min(limit,Mathf.Sqrt(velocity*velocity+2*acceleration*ds));total+=2*ds/Mathf.Max(2,velocity+next);velocity=next;
   }
   for(int i=0;i<world.Count;i++) {
    var other=world[i];if(other.Motion.Id==self.Motion.Id||other.Motion.Finished||!other.Valid)continue;
    int actual=other.Route.Branch!=0?other.Route.Branch:other.Intent;
    if(actual!=branch)continue;
    if(PhysicalGap(self,other,branch,out float gap)&&gap>0&&gap<end)total+=Math.Max(0,baseLimit-Math.Max(0,other.Motion.Speed))/Math.Max(1,baseLimit)*Math.Min(12,120/Math.Max(1,other.Motion.Speed));
   }
   return total;
  }
 }
}
