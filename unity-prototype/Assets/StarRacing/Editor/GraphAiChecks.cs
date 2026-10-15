using System;
using System.IO;
using System.Reflection;
using System.Linq;
using StarRacingPrototype.Procedural;
using UnityEngine;
using UnityEditor.SceneManagement;
namespace StarRacingPrototype {
 public static class GraphAiChecks {
  public static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_AI_QA_DIR");
  static int assertions;
  static void Check(bool ok,string why){assertions++;if(!ok)throw new Exception("Graph AI: "+why);}
  static byte[] LegacyReplay() {
   var balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);
   using(var bytes=new MemoryStream())using(var writer=new BinaryWriter(bytes)) {
    foreach(bool generated in new[]{false,true})foreach(uint seed in new uint[]{77,108,139})foreach(var profile in new[]{DriverProfile.Rookie,DriverProfile.Racer,DriverProfile.Ace}) {
     var route=new TrackRoute(generated?Procedural.Generator.Generate(77,"normal","cloud-city",false,true):RoadVolumeChecks.FlatRoad());
     var world=new AiWorldSnapshot(3);var cars=(AiObservation[])typeof(AiWorldSnapshot).GetField("cars",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(world);
     var driver=new AiDriver(0,profile,seed,balance);
     for(int tick=0;tick<300;tick++) {
      float distance=80+tick*2,speed=tick%80<10?8:35,offset=(float)Math.Sin(tick*.05)*2;
      for(int i=0;i<3;i++) {float at=distance+(i==0?0:i==1?18+(tick%100)*.2f:-15);var f=route.Evaluate(at);float lane=i==0?offset:i==1?0:2;
       cars[i]=new AiObservation{Id=i,Revision=tick<170?1:2,Distance=at,Speed=i==1?12:speed,Lateral=lane,Charge=100,Grounded=true,Position=f.position+f.right*lane,Forward=f.tangent,Up=f.normal,LateralExtent=VehicleGeometry.HalfWidth};}
      typeof(AiWorldSnapshot).GetProperty("Time").SetValue(world,10+tick*.1);
      var c=driver.Step(world,route,.1f);
      writer.Write(c.throttle);writer.Write(c.brake);writer.Write(c.steer);writer.Write(c.nitro);writer.Write(c.drift);
      writer.Write(driver.DesiredSpeed);writer.Write(driver.Lane);writer.Write(driver.Target);writer.Write((int)driver.Mode);writer.Write((int)driver.Maneuver);
     }
    }
    return bytes.ToArray();
   }
  }
  static ReleaseBalance Balance()=>ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);
  static Pose PoseAt(IndependentGraph graph,int road,double at,float lateral=0) {
   var f=graph.Road(road).Evaluate(at);return new Pose((f.position-f.right*lateral+f.normal*VehicleGeometry.AdheredBodyHeight).Float,Quaternion.LookRotation(f.tangent.Float,f.normal.Float));
  }
  static GraphAiObservation Seen(GraphRoadSurface surface,int id,int road,double at,float speed=20,float lateral=0,int history=0) {
   var graph=surface.Graph;var first=Math.Max(0,at-.02);var cursor=graph.Cursor(road,first,road==5?history==0?1:history:0);
   Check(GraphVehicleRoute.TryCreate(surface,cursor,PoseAt(graph,road,first,lateral),1,0,out var route),"physical snapshot spawn");
   Check(route.Observe(PoseAt(graph,road,Math.Max(0,at-.01),lateral),1,1),"snapshot first anchor");
   Check(route.Observe(PoseAt(graph,road,at,lateral),1,2),"snapshot second anchor");
   var f=graph.Evaluate(route.Cursor);var pose=route.RaceObservation.Position;
   var m=new AiObservation{Id=id,Revision=1,Distance=(float)route.Cursor.localPhysicalDistance,Speed=speed,Lateral=Vector3.Dot(pose-f.position.Float,-f.right.Float),Position=pose,Forward=f.tangent.Float,Up=f.normal.Float,Grounded=true,Charge=100,LateralExtent=VehicleGeometry.HalfWidth};
   return new GraphAiObservation(m,route.RaceObservation,route.CommittedBranch);
  }
  static GraphAiWorldSnapshot World(GraphRoadSurface surface,params GraphAiObservation[] observations) {
   var world=new GraphAiWorldSnapshot(surface,observations.Length,Balance());var values=(GraphAiObservation[])typeof(GraphAiWorldSnapshot).GetField("cars",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(world);
   Array.Copy(observations,values,observations.Length);typeof(GraphAiWorldSnapshot).GetProperty("Time").SetValue(world,10d);return world;
  }
  static DrivingContactObservation Contact(GraphAiObservation o) {
   var f=o.Route.Surface.Graph.Evaluate(o.Route.Cursor);
   return new DrivingContactObservation{Id=o.Motion.Id,Generation=1,Revision=o.Motion.Revision,Span=-1,GraphMode=true,Graph=o.Route,Position=o.Motion.Position,Velocity=o.Velocity,Right=f.right.Float,Tangent=f.tangent.Float,Normal=f.normal.Float};
  }
  static GraphAiObservation Identity(GraphAiObservation o,int id){var m=o.Motion;m.Id=id;return new GraphAiObservation(m,o.Route,o.Intent,o.Velocity);}
  static AiDriver Driver(int id=0)=>new AiDriver(id,DriverProfile.Racer,77,Balance());
  static void Decisions(GraphRoadSurface surface) {
   var graph=surface.Graph;var self=Seen(surface,0,0,40);
   var free=World(surface,self);var driver=Driver();driver.StepGraph(free,.1f,false);int choice=driver.GraphChoice;
   var others=new GraphAiObservation[9];others[0]=self;for(int i=1;i<others.Length;i++)others[i]=Seen(surface,i,choice,50+i*8,0);
   var busy=World(surface,others);var alternate=Driver();alternate.StepGraph(busy,.1f,false);
   Check(alternate.GraphChoice!=choice,"controlled congestion changes useful road choice "+driver.GraphTechnicalSeconds+"/"+driver.GraphSpeedSeconds);
   var repeat=Driver();var command=repeat.StepGraph(busy,.1f,false);Check(command.Equals(repeat.StepGraph(busy,.1f,false))&&alternate.GraphChoice==repeat.GraphChoice,"deterministic snapshot/duplicate choice");
   GraphAiObservation technical=Seen(surface,0,1,graph.Road(1).Length*.5,15),speed=Seen(surface,1,2,graph.Road(2).Length*.5,0);
   var separate=Driver();separate.StepGraph(World(surface,technical,speed),.1f,false);var lone=Driver();lone.StepGraph(World(surface,technical),.1f,false);
   Check(!separate.TrafficBlocked&&!separate.ContactPressure&&separate.DesiredSpeed==lone.DesiredSpeed,"foreign branch common progress is not traffic/pressure");
   GraphAiObservation approach=Seen(surface,0,0,159),split=Seen(surface,1,10,1,0);
   var seam=World(surface,approach,split);Check(seam.Navigation.PhysicalGap(approach,split,1,out float physical)&&Math.Abs(physical-2)<.02,"physical gap across port");
   GraphAiObservation merged0=Seen(surface,0,12,free.Navigation.MergeConflictStation-20),merged1=Seen(surface,1,13,free.Navigation.MergeConflictStation-20);
   var merge=World(surface,merged0,merged1);var first=Driver(0);var second=Driver(1);first.StepGraph(merge,.1f,false);second.StepGraph(merge,.1f,false);
   Check(first.MergeYieldTo==-1&&second.MergeYieldTo==0,"one deterministic merge loser");
   var stopped=Seen(surface,1,13,graph.Road(13).Length-10,0);var moving=Seen(surface,0,12,graph.Road(12).Length-16,20);
   var obstacleWorld=World(surface,moving,stopped);Check(obstacleWorld.Navigation.PredictedObstacle(obstacleWorld,moving,1,0,.8f,16,out _),"fixture stopped footprint intersects moving path");var guard=Driver();guard.StepGraph(obstacleWorld,.1f);Check(guard.TrafficBlocked&&!guard.NitroCommand,"stopped opposite-arm shared obstacle remains visible station="+free.Navigation.MergeConflictStation+" length="+graph.Road(12).Length+" own="+moving.Motion.Position+" stopped="+stopped.Motion.Position+" blocked="+guard.TrafficBlocked+" corridor="+guard.CorridorBlocked+" desired="+guard.DesiredSpeed);
   Check(GraphNavigation.SweptConflict(Vector3.zero,new Vector3(0,0,16),new Vector3(0,0,8),Vector3.zero,Vector3.forward,Vector3.right,Vector3.up,3),"crossing gap between interval endpoints");
   var mild=Seen(surface,1,13,graph.Road(13).Length-5,18);var severe=Identity(stopped,2);
   var slideMotion=stopped.Motion;var movingFrame=graph.Evaluate(moving.Route.Cursor);
   slideMotion.Forward=(movingFrame.tangent.Float-movingFrame.right.Float).normalized;slideMotion.Speed=20/Mathf.Sqrt(2);
   var sliding=new GraphAiObservation(slideMotion,stopped.Route,2,-movingFrame.right.Float*20);
   var slideDriver=Driver();slideDriver.StepGraph(World(surface,moving,sliding),.1f,false);
   Check(slideDriver.TrafficBlocked&&slideDriver.DesiredSpeed<=guard.DesiredSpeed,"sliding yaw cannot invent forward obstacle velocity");
   var orderA=Driver();orderA.StepGraph(World(surface,moving,mild,severe),.1f,false);
   var orderB=Driver();orderB.StepGraph(World(surface,moving,Identity(severe,1),Identity(mild,2)),.1f,false);
   Check(orderA.DesiredSpeed<=guard.DesiredSpeed&&orderA.DesiredSpeed==orderB.DesiredSpeed,"all conflicts constrain speed independent of entrant order");
   var contactA=Contact(Seen(surface,0,5,30,30,0,1));var contactB=Contact(Seen(surface,1,5,33,0,0,1));
   var policy=new DrivingContactPolicy();policy.BeginStep(new[]{contactA,contactB});Check(policy.Vehicle(0,1,out _)&&!policy.Vehicle(0,1,out _),"graph contact one audible onset");
   policy.BeginStep(new[]{contactA,contactB});Check(!policy.Vehicle(0,1,out _),"graph continuous contact no repeat");
   var invalidContact=contactB;invalidContact.Revision++;Check(!DrivingContactPolicy.Compatible(contactA,invalidContact),"graph contact rejects stale position revision");
   Check(!DrivingContactPolicy.Compatible(Contact(technical),Contact(speed)),"separate road contact suppressed");
   Check(DrivingContactPolicy.Compatible(Contact(merged0),Contact(merged1)),"merge arms share physical contact domain");
   var gapContact=contactB;gapContact.Position+=contactA.Tangent*30;policy.BeginStep(new[]{contactA,gapContact});policy.BeginStep(new[]{contactA,contactB});Check(policy.Vehicle(0,1,out _),"graph physical gap rearms contact");
   var late=Seen(surface,0,10,78,25);var lateDriver=Driver();lateDriver.StepGraph(World(surface,late),.1f,false);
   Check(lateDriver.GraphChoice==late.Route.Cursor.incomingBranch||lateDriver.CorridorBlocked,"late first choice cannot cross divider");
   // Four arrivals formerly created a pairwise-tolerance cycle. Every arrow now
   // follows a single transitive key, and at least one entrant keeps priority.
   var pack=new GraphAiObservation[4];for(int id=0;id<4;id++)pack[id]=Seen(surface,id,id%2==0?12:13,free.Navigation.MergeConflictStation-(1.6-id*.2)*20,20);
   var four=World(surface,pack);var drivers=Enumerable.Range(0,4).Select(Driver).ToArray();foreach(var d in drivers)d.StepGraph(four,.1f,false);
   Check(drivers.Any(d=>d.MergeYieldTo<0),"merge arbitration has free winner");
   for(int id=0;id<4;id++){int at=id;var visited=new bool[4];while(at>=0){Check(!visited[at],"acyclic merge reservations");visited[at]=true;at=drivers[at].MergeYieldTo;}}
   var lead=Seen(surface,0,5,30,0,0,1);var rear=Seen(surface,1,5,25.7,0,0,1);
   var release=Driver();var releaseCommand=release.StepGraph(World(surface,lead,rear),.1f,false);
   Check(releaseCommand.throttle>0&&!release.TrafficBlocked,"rear buffer cannot deadlock free leader");
   var touchingRear=Seen(surface,1,5,25.9,0,0,1);var restingRelease=Driver();var restingCommand=restingRelease.StepGraph(World(surface,lead,touchingRear),.1f,false);Check(restingCommand.throttle>0&&!restingRelease.TrafficBlocked,"resting rear buffer permits straight supported leader to leave");
   var jitterMotion=lead.Motion;jitterMotion.Speed=-.03f;var jitterLead=new GraphAiObservation(jitterMotion,lead.Route,lead.Intent,jitterMotion.Forward*-.03f);var jitterRelease=Driver();Check(jitterRelease.StepGraph(World(surface,jitterLead,touchingRear),.1f,false).throttle>0&&!jitterRelease.TrafficBlocked,"stop deadband does not trap supported leader behind rounded endpoint");
   Check(!driver.GraphStopIntent,"free driving does not request stop actuation");
   surface.enabled=false;var safe=driver.StepGraph(free,.1f);Check(!safe.nitro&&safe.throttle==0&&driver.GraphChoice==0&&driver.GraphStopIntent,"stale build immediately invalidates commands");
   Check(driver.ApplyGraphControl(null,safe,.01f).Equals(default(DrivingInput)),"invalid receiver fails closed during stop intent");
   driver.StepGraph(free,0);Check(!driver.GraphStopIntent,"nonpositive decision step clears stop intent");
   driver.StepGraph(free,.1f);Check(driver.GraphStopIntent,"invalid binding still requests safe stop");
   driver.Reset();Check(!driver.GraphStopIntent,"reset clears stop intent");
   surface.enabled=true;var rebound=World(surface,Seen(surface,0,0,40));driver.StepGraph(rebound,.1f,false);
   Check(!driver.GraphStopIntent,"new valid route clears stale stop intent");
   var cachedFree=driver.StepGraph(rebound,.1f,false);
   driver.StepGraph(rebound,0);var afterZero=driver.StepGraph(rebound,.1f,false);
   Check(!driver.GraphStopIntent&&cachedFree.Equals(afterZero),"same-snapshot decision after zero dt restores consistent command and intent");

   var receiver=new GameObject("stop actuator receiver");try {
    var car=receiver.AddComponent<MagneticVehicle>();car.Body=receiver.AddComponent<Rigidbody>();car.Body.useGravity=false;car.Seat=0;
    car.Frame=new TrackFrame{normal=Vector3.up,tangent=Vector3.forward,right=Vector3.right};
    typeof(MagneticVehicle).GetProperty("Drive").SetValue(car,new DrivingBalance(Balance()));
    typeof(MagneticVehicle).GetProperty("PositionRevision").SetValue(car,rebound[0].Route.PositionRevision);
    typeof(MagneticVehicle).GetField("graphRoute",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(car,rebound[0].Route.Owner);
    // Isolate application from decision policy; real binding, body and Observe are used.
    typeof(AiDriver).GetField("graphStopIntent",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(driver,true);
    var raw=new DrivingInput{throttle=1,brake=1,nitro=true,steer=.25f};
    car.Body.rotation=Quaternion.identity;car.Body.linearVelocity=new Vector3(3,8,-4);
    var applied=driver.ApplyGraphControl(car,raw,.01f);
    Check(applied.brake==0&&applied.throttle==1&&!applied.nitro&&applied.steer==raw.steer,"actual receiver uses fresh signed planar magnitude");
    car.Body.linearVelocity=new Vector3(.06f,8,-.06f);applied=driver.ApplyGraphControl(car,raw,.01f);
    Check(applied.throttle>0&&applied.throttle<1&&applied.brake==0,"fresh planar speed crosses stop band even when forward dot does not");
    car.Body.linearVelocity=Vector3.forward*.001f;applied=driver.ApplyGraphControl(car,raw,.01f);
    Check(applied.throttle==0&&applied.brake==0,"same cached stop intent follows fresh neutral velocity");
    car.SetInput(applied);Check(car.CurrentInput.Equals(applied),"production receiver receives adjusted stop input");
    car.Body.linearVelocity=Vector3.forward;applied=driver.ApplyGraphControl(car,raw,.01f);
    Check(applied.brake==1&&applied.throttle==0,"same cached stop intent follows fresh positive velocity");
    car.Seat=1;Check(driver.ApplyGraphControl(car,raw,.01f).Equals(default(DrivingInput)),"different owner cannot consume stop command");car.Seat=0;
    typeof(MagneticVehicle).GetProperty("PositionRevision").SetValue(car,car.PositionRevision+1);
    Check(driver.ApplyGraphControl(car,raw,.01f).Equals(default(DrivingInput)),"changed position revision rejects stale stop binding");
    typeof(MagneticVehicle).GetProperty("PositionRevision").SetValue(car,rebound[0].Route.PositionRevision);
    car.Body.isKinematic=true;Check(driver.ApplyGraphControl(car,raw,.01f).Equals(default(DrivingInput)),"held or paused body rejects actuation");car.Body.isKinematic=false;
    typeof(MagneticVehicle).GetProperty("FinishedCoasting").SetValue(car,true);
    Check(driver.ApplyGraphControl(car,raw,.01f).Equals(default(DrivingInput)),"finished receiver rejects actuation");
   }finally{UnityEngine.Object.DestroyImmediate(receiver);}

  }
  static float Bits(int value)=>BitConverter.Int32BitsToSingle(value);
  static Vector3 BitsVector(int x,int y,int z)=>new Vector3(Bits(x),Bits(y),Bits(z));
  static void NumericRearContacts() {
   var method=typeof(GraphNavigation).GetMethod("NumericRearContact",BindingFlags.Static|BindingFlags.NonPublic);
   Check(method!=null,"numeric contact runtime predicate exists");
   bool Release(float rear,float future,float side,float end,float width=3,bool supported=true,float heading=1,float target=0,float travel=2)
    =>(bool)method.Invoke(null,new object[]{rear,future,side,end,width,supported,heading,target,travel});
   void Frozen(int seq,Vector3 own,Vector3 other,Vector3 velocity,Vector3 end,Vector3 tangent,Vector3 right,float horizon,float travel,float width,float heading,int rearBits,int futureBits,int sideBits,int targetBits,bool expected) {
    var otherEnd=other+velocity*horizon;
    float rear=Vector3.Dot(other-own,tangent),future=Vector3.Dot(otherEnd-end,tangent),side=Vector3.Dot(other-own,right),sideEnd=Vector3.Dot(otherEnd-end,right),target=Vector3.Dot(end-own,right);
    Check(BitConverter.SingleToInt32Bits(rear)==rearBits,"frozen rear bits request="+seq);
    Check(BitConverter.SingleToInt32Bits(future)==futureBits,"frozen future bits request="+seq);
    Check(BitConverter.SingleToInt32Bits(Mathf.Abs(side))==sideBits,"frozen lateral bits request="+seq);
    Check(BitConverter.SingleToInt32Bits(Mathf.Abs(target))==targetBits,"frozen own target bits request="+seq);
    Check(Release(rear,future,side,sideEnd,width,true,heading,target,travel)==expected,"frozen numeric release request="+seq);
   }
   Frozen(5675,BitsVector(-997773607,1123058891,-991345156),BitsVector(-997754705,1123058891,-991316340),BitsVector(1065720022,-1156456539,1072130351),BitsVector(-997780448,1123058890,-991357004),BitsVector(1056964608,0,1063105495),BitsVector(1063105495,-2147483648,-1090519040),Bits(1061997773),Bits(1070973719),Bits(1077558743),Bits(1065353216),-1064933789,-1064933661,1047868309,947230910,true);
   Frozen(1926,BitsVector(-997690615,1123058891,-991202104),BitsVector(-997671713,1123058891,-991173288),BitsVector(1065720022,-1181363064,1072130351),BitsVector(-997697455,1123058890,-991213952),BitsVector(1056964608,0,1063105495),BitsVector(1063105495,-2147483648,-1090519040),Bits(1061997773),Bits(1070973719),Bits(1077558743),Bits(1065353216),-1064933789,-1064933789,1047868309,944099056,true);
   Frozen(236,BitsVector(-997653257,1123058890,-991137671),BitsVector(-997610666,1123058890,-991067285),BitsVector(1094713017,-1173660357,1101823622),BitsVector(-997689564,1123058890,-991200556),BitsVector(1056964608,0,1063105495),BitsVector(1063105495,-2147483648,-1090519040),Bits(1061997773),Bits(1091424987),Bits(1077724368),Bits(1065353083),-1054825016,1062565642,1045658725,940657258,false);
   Frozen(989,BitsVector(-997669765,1123058890,-991166164),BitsVector(-997650863,1123058890,-991137348),BitsVector(-1096240099,-1155745462,-1089328169),BitsVector(-997669765,1123058890,-991166164),BitsVector(1056964608,0,1063105495),BitsVector(1063105495,-2147483648,-1090519040),Bits(1061997773),Bits(0),Bits(1077558743),Bits(1065353216),-1064933789,-1063827826,1047868309,0,false);
   int lengthBits=BitConverter.SingleToInt32Bits(VehicleGeometry.Length);
   foreach(int ulps in new[]{1,3,8,9}) {
    float rear=-Bits(lengthBits-ulps);bool expected=ulps<=8;
    Check(Release(rear,rear,.2f,.2f)==expected,"inner numeric boundary ULP="+ulps);
    Check(!Release(rear,Bits(BitConverter.SingleToInt32Bits(rear)-1),.2f,.2f),"one ULP longitudinal closing rejected="+ulps);
   }
   Check(!Release(-VehicleGeometry.Length,-VehicleGeometry.Length,.2f,.2f),"new branch excludes exact old boundary");
   Check(!Release(-VehicleGeometry.Length+.001f,-VehicleGeometry.Length+.001f,.2f,.2f),"actual overlap outside numeric band");
   float near=-Bits(lengthBits-3);
   Check(Release(near,near,-.2f,-.3f),"negative-side lateral separation");
   Check(Release(near,near,0,0),"equal centered contact");
   Check(!Release(near,near,.2f,.1f),"positive-side lateral closing");
   Check(!Release(near,near,-.2f,-.1f),"negative-side lateral closing");
   Check(!Release(near,near,.2f,-.2f),"lateral crossing");
   Check(!Release(near,near,.2f,.2f,supported:false),"unsupported or flight contact excluded");
   Check(!Release(near,near,.2f,.2f,heading:.95f),"yawing departure excluded");
   Check(!Release(near,near,.2f,.2f,target:.25f),"own lateral departure excluded");
   Check(!Release(near,near,.2f,.2f,travel:-1),"reverse target excluded");
   Check(!Release(near,near,3,3),"outside footprint width excluded");
  }
  static void MandatoryColumnAlignment() {
   var method=typeof(AiDriver).GetMethod("NeedsRunwayAlignment",BindingFlags.Static|BindingFlags.NonPublic);
   Check(method!=null,"actual runway alignment predicate exists");
   var root=new GameObject("mandatory alignment domain fixture");try {
    double angle=20*Math.PI/180;var n=new DVec(0,1,0);var t=new DVec(Math.Sin(angle),0,Math.Cos(angle));
    var graph=GraphNativeCatalog.Create(new DVec(0,120,0),t,n,DVec.Cross(t,n).Unit(),"normal",true,1);
    var surface=root.AddComponent<GraphRoadSurface>();typeof(GraphRoadSurface).GetMethod("Initialize",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(surface,new object[]{graph,81});
    var nav=new GraphNavigation(surface,Balance());var jump=graph.jumps.Single(j=>j.roadId==0&&j.mandatory);
    GraphAiObservation Domain(double at,float lane) {
     var cursor=graph.Cursor(0,at);var pose=PoseAt(graph,0,at,lane);
     // Domain-only constructor: no support or race credit is claimed by this fixture.
     var owner=(GraphVehicleRoute)Activator.CreateInstance(typeof(GraphVehicleRoute),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{surface,cursor,pose.position,1,0L},null);
     var frame=graph.Evaluate(cursor);var m=new AiObservation{Id=0,Revision=1,Lateral=lane,Position=pose.position,Forward=frame.tangent.Float,Up=frame.normal.Float};
     return new GraphAiObservation(m,owner.RaceObservation);
    }
    bool Required(GraphAiObservation self,GraphJump j)=>(bool)method.Invoke(null,new object[]{nav,self,1,j});
    foreach(double at in new[]{1080d,1083.3545264749416,1084d}) {
     var safe=Domain(at,6.052092f);Check(!Required(safe,jump),"recorded safe mandatory column does not require impossible alignment at="+at);
     Check(nav.HasCorridor(safe,1,safe.Motion.Lateral,(float)(jump.gapEnd+2-at)),"same actual corridor is proved through full mandatory gap");
     Check(!Required(Domain(at,-6.052092f),jump),"opposite safe mandatory column is released at="+at);
     Check(Required(Domain(at,6.7f),jump),"unsafe mandatory edge retains required alignment at="+at);
    }
    var optional=graph.jumps.Single(j=>j.roadId==0&&!j.mandatory);
    Check(Required(Domain(optional.rampStart-2,0),optional),"optional ramp never inherits mandatory column exemption");
    Check(Required(Domain(jump.rampStart-2,-6.7f),jump),"opposite unsafe edge retains alignment requirement");
   }finally{UnityEngine.Object.DestroyImmediate(root);}
  }
  static void StopActuationModels() {
   var method=typeof(AiDriver).GetMethod("StoppedGraphInput",BindingFlags.Static|BindingFlags.NonPublic);
   Check(method!=null,"actual stop actuator exists");
   DrivingInput Input(float v,float a,float dt,DrivingInput source)=>(DrivingInput)method.Invoke(null,new object[]{v,a,dt,source});
   var original=new DrivingInput{throttle=1,brake=1,steer=-.375f,drift=true,nitro=true};
   string source=Resources.Load<TextAsset>("balance-config").text;
   foreach(float acceleration in new[]{1f,30f,100f}) {
    string json=System.Text.RegularExpressions.Regex.Replace(source,@"(""acceleration""\s*:\s*)[-0-9.eE+]+",m=>m.Groups[1].Value+acceleration.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
    var balance=ReleaseBalance.Parse(json);Check(balance["acceleration"]==acceleration,"real bounded acceleration fixture");
    foreach(float dt in new[]{Bits(1008981763),.01f,.02f,.05f,1f/60,1f/120})
    foreach(float speed in new[]{0f,Bits(unchecked((int)0x80000000)),float.Epsilon,-float.Epsilon,.001f,-.001f,.08f,-.08f,
     Bits(BitConverter.SingleToInt32Bits(.08f)-1),Bits(BitConverter.SingleToInt32Bits(.08f)+1),
     -Bits(BitConverter.SingleToInt32Bits(.08f)-1),-Bits(BitConverter.SingleToInt32Bits(.08f)+1),.673174858f,-.673174858f,.562f,-.562f,1f,-1f,40f,-40f,(balance["nitroMaxSpeedKmh"]/3.6f),-(balance["nitroMaxSpeedKmh"]/3.6f)})
    foreach(double grip in new[]{0d,.5d,1d})foreach(double slip in new[]{0d,HumanHandling.MaxSlip*.5,HumanHandling.MaxSlip}) {
     var state=new HumanHandlingState{gripBlend=grip,slipAngle=slip};var drive=new DrivingBalance(balance);
     var command=Input(speed,acceleration,dt,original);
     Check(command.steer==original.steer&&command.drift==original.drift&&!command.nitro,"only stop pedals and nitro change");
     Check(command.throttle>=0&&command.throttle<=1&&command.brake>=0&&command.brake<=1,"ordinary input envelope");
     if(speed>.08f)Check(command.brake==1&&command.throttle==0,"positive stop uses canonical brake");
     else if(speed<-.08f)Check(command.brake==0&&(command.throttle*acceleration)*dt<=-speed,"reverse gas bounded by native float product");
     else Check(command.brake==0&&command.throttle==0,"existing neutral band never requests reverse");
     float actual=drive.StepHuman(command,speed,dt,true,true,1,8,state);
     double accelerated=(double)speed+actual*dt; // Exact production operation after StepHuman.
     if(speed>.08f)Check(accelerated>=-.08f&&accelerated<=speed,"actual native positive stop remains in existing neutral band");
     else if(speed<-.08f)Check(accelerated<=.08f&&accelerated>=speed,"actual native reverse stop cannot drive through neutral band");
     else Check(Math.Abs(accelerated)<=.08f,"actual native neutral input stays neutral");
     Check(!drive.Active,"stop cannot activate boost");
    }
   }
   // Model seeds from the recorded sign changes; these are not frozen motor observations.
   var defaults=Balance();float v=-.673174858f;var model=new DrivingBalance(defaults);var h=new HumanHandlingState();bool settled=false;
   for(int tick=0;tick<22;tick++){
    var c=Input(v,defaults["acceleration"],.01f,original);float a=model.StepHuman(c,v,.01f,true,true,1,8,h);v+=a*.01f;
    if(Math.Abs(v)<=.08f)settled=true;
    if(settled)Check(v<=.08f,"fresh repeated stop never continues gas after stopping");
   }
   Check(settled,"frozen reverse approach reaches existing neutral band");
   v=1.804f;model=new DrivingBalance(defaults);h=new HumanHandlingState();settled=false;
   for(int tick=0;tick<22;tick++){
    var c=Input(v,defaults["acceleration"],.01f,original);float a=model.StepHuman(c,v,.01f,true,true,1,8,h);v+=a*.01f;
    if(Math.Abs(v)<=.08f)settled=true;
    if(settled)Check(v>=-.08f,"fresh repeated stop never holds brake into reverse");
   }
   Check(settled,"frozen forward approach reaches existing neutral band");
   foreach(float bad in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity}){
    var c=Input(bad,defaults["acceleration"],.01f,original);Check(c.throttle==0&&c.brake==0&&!c.nitro,"invalid physical speed fails closed");
   }
   var idle=Driver();Check(original.Equals(idle.ApplyGraphControl(null,original,.01f)),"no stop intent preserves all ordinary/legacy command fields");
  }
  public static void Run() {
   if(string.IsNullOrEmpty(Output))throw new Exception("Evidence directory required");Directory.CreateDirectory(Output);assertions=0;EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   NumericRearContacts();StopActuationModels();MandatoryColumnAlignment();
   var expected=File.ReadAllBytes(Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_AI_BASELINE"));var actual=LegacyReplay();Check(expected.SequenceEqual(actual),"5400 exact legacy commands changed");
   Environment.SetEnvironmentVariable("STAR_RACING_AI_QA_DIR",Output);
   typeof(AiRacingChecks).GetMethod("Decisions",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);FairAiChecks.RunContracts(Output);
   var root=new GameObject("graph AI contracts");try {var graph=IndependentGraph.Create(new DVec(),new DVec(0,0,1),new DVec(0,1,0),new DVec(-1,0,0));var surface=IndependentTrackMesh.Build(root.transform,graph,81,null,null,(_,__)=>{});Physics.SyncTransforms();Decisions(surface);}
   finally{UnityEngine.Object.DestroyImmediate(root);}
   File.WriteAllText(Path.Combine(Output,"result.txt"),"GRAPH_AI_CONTRACTS_OK assertions="+assertions+" legacyCommands=5400 exactBytes="+actual.Length+"\n");Debug.Log("GRAPH_AI_CONTRACTS_OK assertions="+assertions);
  }
  public static void CaptureLegacyBaseline() {
   if(string.IsNullOrEmpty(Output))throw new Exception("Evidence directory required");Directory.CreateDirectory(Output);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   byte[] bytes=LegacyReplay();File.WriteAllBytes(Path.Combine(Output,"legacy-replay.bin"),bytes);
   File.WriteAllText(Path.Combine(Output,"result.txt"),"LEGACY_AI_BASELINE_OK commands=5400 bytes="+bytes.Length+"\n");Debug.Log("LEGACY_AI_BASELINE_OK commands=5400 bytes="+bytes.Length);
  }
 }
}
