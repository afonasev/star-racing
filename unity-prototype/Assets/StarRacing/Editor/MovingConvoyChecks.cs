using System;
using System.IO;
using System.Reflection;
using UnityEngine;
namespace StarRacingPrototype {
 // Moving convoys exercise relative progress; stopped-jam fixtures remain separate.
 public static class MovingConvoyChecks {
  static readonly MethodInfo Tick=typeof(MagneticVehicle).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
  static readonly FieldInfo Observations=typeof(AiWorldSnapshot).GetField("cars",BindingFlags.Instance|BindingFlags.NonPublic);
  static readonly PropertyInfo Clock=typeof(AiWorldSnapshot).GetProperty("Time");
  static int assertions;
  static void Check(bool ok,string why,bool baseline){assertions++;if(!ok&&!baseline)throw new Exception("Moving convoy: "+why);}
  static float Speed(MagneticVehicle c)=>Vector3.Dot(c.Body.linearVelocity,c.transform.forward);
  static float Lateral(MagneticVehicle c)=>Vector3.Dot(c.Body.position-c.Frame.position,c.Frame.right);
  static ReleaseBalance Balance()=>ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);
  static TrackRoute Straight(){var d=RoadVolumeChecks.FlatRoad();var samples=new Procedural.Sample[801];for(int i=0;i<samples.Length;i++)samples[i]=new Procedural.Sample{index=i,distance=i*5,progress=i/800.0,halfWidth=8,position=new Procedural.DVec(1000,120,i*5),tangent=new Procedural.DVec(0,0,1),normal=new Procedural.DVec(0,1,0),right=new Procedural.DVec(-1,0,0),kind="straight"};d.samples=samples;d.totalLength=4000;return new TrackRoute(d);}
  static AiWorldSnapshot World(TrackRoute route,params AiObservation[] cars){var w=new AiWorldSnapshot(cars.Length);var buffer=(AiObservation[])Observations.GetValue(w);for(int i=0;i<cars.Length;i++){var o=cars[i];var f=route.Evaluate(o.Distance);o.Id=i;o.Position=f.position+f.right*o.Lateral;o.Forward=f.tangent;o.Up=f.normal;o.Grounded=true;buffer[i]=o;}Clock.SetValue(w,10d);return w;}
  public static void Run(string output,bool baseline){
   assertions=0;var route=Straight();var balance=Balance();var log=new System.Text.StringBuilder();
   // First discover the individual side preference, then place a further moving
   // neighbor in that lane. It is outside the immediate collision horizon.
   var initial=World(route,new AiObservation{Distance=100,Speed=50},new AiObservation{Distance=115,Speed=50});var probe=new AiDriver(0,DriverProfile.Ace,77,balance);probe.Step(initial,route,.1f,false);int preferred=probe.PassingSide;
   var crowded=World(route,new AiObservation{Distance=100,Speed=50},new AiObservation{Distance=115,Speed=50},new AiObservation{Distance=130,Speed=48,Lateral=preferred*3.05f});var choosing=new AiDriver(0,DriverProfile.Ace,77,balance);choosing.Step(crowded,route,.1f,false);
   log.AppendLine("crowdedSide="+preferred+" selectedSide="+choosing.PassingSide);Check(choosing.PassingSide==-preferred,"preferred side queues behind further neighbor despite free opposite lane",baseline);
   // A target drifting toward the driver's selected endpoint must not drag the
   // endpoint across the road. A safe still-clear commitment is branch-relative.
   var stable=new AiDriver(0,DriverProfile.Ace,77,balance);stable.Step(initial,route,.1f,false);float endpoint=(float)typeof(AiDriver).GetField("maneuverLane",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(stable);var obs=(AiObservation[])Observations.GetValue(initial);obs[1].Lateral=-stable.PassingSide*.8f;obs[1].Position+=route.Evaluate(115).right*obs[1].Lateral;Clock.SetValue(initial,10.1d);stable.Step(initial,route,.1f,false);float moved=(float)typeof(AiDriver).GetField("maneuverLane",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(stable);
   log.AppendLine("endpointBefore="+endpoint+" after="+moved);Check(Mathf.Abs(endpoint-moved)<.01f,"pass endpoint chased leader lateral movement",baseline);
   // A future slower neighbor does not justify abandoning an ongoing attempt
   // immediately. Once that attempt has no relative result, drop hysteresis
   // and select the measurably faster opposite corridor.
   var stalled=new AiDriver(0,DriverProfile.Ace,77,balance);var equal=World(route,new AiObservation{Distance=100,Speed=50},new AiObservation{Distance=115,Speed=50},new AiObservation{Distance=126,Speed=70,Finished=true});stalled.Step(equal,route,.1f,false);int first=stalled.PassingSide;var equalObs=(AiObservation[])Observations.GetValue(equal);equalObs[0].Lateral=first*3.4f;equalObs[0].Position+=route.Evaluate(100).right*equalObs[0].Lateral;equalObs[2].Finished=false;equalObs[2].Lateral=first*3.2f;equalObs[2].Position+=route.Evaluate(126).right*equalObs[2].Lateral;bool replanned=false;
   Clock.SetValue(equal,10.1d);stalled.Step(equal,route,.1f,false);Check(stalled.PassingSide==first,"minor future restriction immediately broke pass commitment",baseline);
   for(int i=2;i<=100;i++){Clock.SetValue(equal,10d+i*.1);stalled.Step(equal,route,.1f,false);if(stalled.PassingSide!=first&&stalled.PassingSide!=0)replanned=true;}
   log.AppendLine("movingReplanned="+replanned);Check(replanned,"moving pass did not reconsider a slower corridor",baseline);
   foreach(float pace in new[]{50f,83.33333f}){
    var free=World(route,new AiObservation{Distance=100,Speed=pace},new AiObservation{Distance=115,Speed=pace});var steady=new AiDriver(0,DriverProfile.Ace,77,balance);steady.Step(free,route,.1f,false);int side=steady.PassingSide;var freeObs=(AiObservation[])Observations.GetValue(free);freeObs[0].Lateral=side*3.4f;freeObs[0].Position+=route.Evaluate(100).right*freeObs[0].Lateral;
    for(int i=1;i<=200;i++){Clock.SetValue(free,10d+i*.1);steady.Step(free,route,.1f,false);Check(steady.PassingSide==side,"equally clear lanes caused periodic sweeping",baseline);}
   }
   var departing=World(route,new AiObservation{Distance=100,Speed=50},new AiObservation{Distance=115,Speed=50},new AiObservation{Distance=130,Speed=100,Lateral=preferred*3.2f});var fastNeighbor=new AiDriver(0,DriverProfile.Ace,77,balance);fastNeighbor.Step(departing,route,.1f,false);Check(fastNeighbor.PassingSide==preferred,"departing fast neighbor penalized a free corridor",baseline);
   var guarded=World(route,new AiObservation{Distance=100,Speed=50},new AiObservation{Distance=115,Speed=50});var movingTarget=new AiDriver(0,DriverProfile.Ace,77,balance);movingTarget.Step(guarded,route,.1f,false);var guardedObs=(AiObservation[])Observations.GetValue(guarded);guardedObs[1].Lateral=movingTarget.PassingSide*2;guardedObs[1].Position+=route.Evaluate(115).right*guardedObs[1].Lateral;Clock.SetValue(guarded,10.1d);movingTarget.Step(guarded,route,.1f,false);float guardedEndpoint=(float)typeof(AiDriver).GetField("maneuverLane",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(movingTarget);Check(Mathf.Abs(guardedEndpoint-guardedObs[1].Lateral)>=VehicleGeometry.Width+.1f,"unsafe committed endpoint retained through target intrusion",baseline);
   foreach(bool resource in new[]{false,true}){
    var capTrain=World(route,new AiObservation{Distance=100,Speed=83.33333f,Charge=resource?100:0},new AiObservation{Distance=125,Speed=83.33333f},new AiObservation{Distance=150,Speed=83.33333f,Lateral=preferred*3.2f});var charged=new AiDriver(0,DriverProfile.Ace,77,balance);var command=charged.Step(capTrain,route,.1f,true);
    log.AppendLine("charged="+resource+" capTrainSide="+charged.PassingSide);Check(charged.PassingSide==(resource?-preferred:preferred),"base-cap train ignored resourceful clear corridor or invented nitro without charge",baseline);Check(!command.nitro,"potential lane forecast accelerated before measured clearance",baseline);
   }
   // Future pavement can invalidate boost after a valid lane entry. The
   // rejected forecast must not reward a congested, slower shoulder lane.
   var narrowing=Straight();for(int i=65;i<narrowing.Definition.samples.Length;i++)narrowing.Definition.samples[i].halfWidth=5.4;narrowing=new TrackRoute(narrowing.Definition);
   var narrowedTrain=World(narrowing,new AiObservation{Distance=100,Speed=83.33333f,Charge=100,Lateral=-3.8f},new AiObservation{Distance=125,Speed=83.33333f,Lateral=-2},new AiObservation{Distance=145,Speed=83.33333f,Lateral=-5.2f});var narrowDriver=new AiDriver(0,DriverProfile.Ace,77,balance);narrowDriver.Step(narrowedTrain,narrowing,.1f,true);
   var paceMethod=typeof(AiDriver).GetMethod("PassingPace",BindingFlags.Instance|BindingFlags.NonPublic);var paceArgs=new object[]{narrowedTrain,narrowing,narrowedTrain[0],-5.2f,25f,true,0f,0f};float deniedPace=(float)paceMethod.Invoke(narrowDriver,paceArgs);
   log.AppendLine("futureCorridorDeniedPace="+deniedPace+" narrowedTrainSide="+narrowDriver.PassingSide);Check(deniedPace<=83.34f,"boost forecast ignored pavement after lane entry",baseline);Check(narrowDriver.PassingSide==1,"geometry-denied crowded lane beat a clear boostable alternative",baseline);
   var remote=World(route,new AiObservation{Distance=100,Speed=83.33333f,Charge=100},new AiObservation{Distance=204,Speed=83.33333f,Lateral=3.2f},new AiObservation{Distance=130,Speed=83.33333f,Finished=true});var retarget=new AiDriver(0,DriverProfile.Ace,77,balance);retarget.Step(remote,route,.1f,false);Check(retarget.Target==1,"remote-target setup failed",baseline);((AiObservation[])Observations.GetValue(remote))[2].Finished=false;Clock.SetValue(remote,10.1d);retarget.Step(remote,route,.1f,true);log.AppendLine("followingHorizonTarget="+retarget.Target);Check(retarget.Target==2&&retarget.Maneuver==AiManeuver.Pass,"remote target masked a 30 m actual moving blocker",baseline);
   foreach(bool lateralMotion in new[]{false,true}){
    var jitter=World(route,new AiObservation{Distance=100,Speed=-.2f,LateralSpeed=lateralMotion?10:0,MotionSpeed=lateralMotion?10:.2f});var recovering=new AiDriver(0,DriverProfile.Ace,77,balance);bool reversed=false;
    for(int i=0;i<30;i++){Clock.SetValue(jitter,10d+i*.1);recovering.Step(jitter,route,.1f,false);reversed|=recovering.ReverseActive;}
    Check(reversed!=lateralMotion,"reverse jitter prevented safe recovery or sideways motion falsely triggered reverse",baseline);
   }
   File.WriteAllText(Path.Combine(output,"convoy-decisions.txt"),log.ToString());
   foreach(float speed in new[]{35f,50f})foreach(bool nitro in new[]{false,true})RunPhysical(output,route,balance,speed,nitro,baseline);
   File.WriteAllText(Path.Combine(output,"convoy-result.txt"),"MOVING_CONVOYS_OK assertions="+assertions+" cases=4 durationSeconds=20 entrants=8\n");
  }
  [Serializable] public sealed class Result {public float leaderSpeed,minProgress,maxQueueSeconds;public bool nitro;public int passes,respawns;}
  static void RunPhysical(string output,TrackRoute route,ReleaseBalance balance,float leaderSpeed,bool nitro,bool baseline){
   var root=new GameObject("moving convoy");try{var road=new GameObject("road");road.transform.SetParent(root.transform);var track=road.AddComponent<TrackBuilder>();track.Build(route);const int count=8;var cars=new MagneticVehicle[count];var drivers=new AiDriver[count];var commands=new DrivingInput[count];var revisions=new int[count];var initial=new float[count];var passed=new bool[count];var queue=new float[count];var roster=new RaceRoster(count,1,77);var world=new AiWorldSnapshot(count);var result=new Result{leaderSpeed=leaderSpeed,nitro=nitro};var trace=new System.Text.StringBuilder("time,id,distance,lateral,speed,lane,maneuver,limit,nitro\n");
    for(int i=0;i<count;i++){var go=new GameObject("entrant "+i);go.transform.SetParent(root.transform);cars[i]=go.AddComponent<MagneticVehicle>();cars[i].Initialize(track,i,Color.cyan,balance,i==0);cars[i].enabled=false;cars[i].ResetAt(220-i*12,0);cars[i].Body.linearVelocity=cars[i].Frame.tangent*leaderSpeed;initial[i]=cars[i].Distance;revisions[i]=cars[i].PositionRevision;drivers[i]=new AiDriver(i,DriverProfile.Ace,roster.Entrants[i].Seed,balance);}
    Physics.SyncTransforms();
    for(int tick=0;tick<2000;tick++){double now=10+tick*.01;for(int i=0;i<count;i++){int place=1;for(int j=0;j<count;j++)if(cars[j].Distance>cars[i].Distance)place++;cars[i].Hold(false);cars[i].SetRaceContext(true,place,count,now);cars[i].PrepareProjection();}
     if(tick%10==0){world.Capture(cars,route,now);commands[0]=new DrivingInput{throttle=Speed(cars[0])<leaderSpeed?1:0,brake=Speed(cars[0])>leaderSpeed+.5f?.2f:0};for(int i=1;i<count;i++)commands[i]=drivers[i].Step(world,route,.1f,nitro);}
     for(int i=0;i<count;i++){cars[i].SetInput(commands[i]);Tick.Invoke(cars[i],null);}RacePhysicsStepper.Simulate(.01f);
     if(tick%10==0){world.Capture(cars,route,now);for(int i=0;i<count;i++)trace.AppendLine($"{tick*.01f:F2},{i},{world[i].Distance:F3},{world[i].Lateral:F3},{world[i].Speed:F3},{drivers[i].Lane:F3},{drivers[i].Maneuver},{drivers[i].SpeedLimitSource},{commands[i].nitro}");}
     for(int i=1;i<count;i++){if(!passed[i]&&cars[i].Distance>cars[0].Distance+4){passed[i]=true;result.passes++;}if(cars[i].PositionRevision!=revisions[i]){result.respawns++;revisions[i]=cars[i].PositionRevision;}bool following=false;for(int j=0;j<count;j++)if(i!=j){float gap=cars[j].Distance-cars[i].Distance;if(gap>0&&gap<25&&Mathf.Abs(Lateral(cars[j])-Lateral(cars[i]))<2.6f&&Mathf.Abs(Speed(cars[j])-Speed(cars[i]))<4)following=true;}queue[i]=following?queue[i]+.01f:0;result.maxQueueSeconds=Mathf.Max(result.maxQueueSeconds,queue[i]);}
    }
    result.minProgress=float.PositiveInfinity;for(int i=1;i<count;i++)result.minProgress=Mathf.Min(result.minProgress,cars[i].Distance-initial[i]);string name="convoy-"+leaderSpeed+(nitro?"-nitro":"-ordinary");File.WriteAllText(Path.Combine(output,name+".json"),JsonUtility.ToJson(result,true));File.WriteAllText(Path.Combine(output,name+"-trace.csv"),trace.ToString());
    Check(result.respawns==0,"moving pack left road",baseline);Check(result.minProgress>=leaderSpeed*15,"moving pack lost pace",baseline);Check(result.passes>=5,"moving pack did not clear slower leader",baseline);Check(result.maxQueueSeconds<8,"moving pack sustained a same-lane queue",baseline);
   }finally{UnityEngine.Object.DestroyImmediate(root);Physics.SyncTransforms();}
  }
 }
}
