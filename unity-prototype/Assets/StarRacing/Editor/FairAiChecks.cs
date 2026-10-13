using System;
using System.IO;
using System.Reflection;
using UnityEngine;
namespace StarRacingPrototype {
 // Affected contracts, invoked in the existing isolated AI PlayMode runner.
 public static class FairAiChecks {
  static readonly MethodInfo Tick=typeof(MagneticVehicle).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
  static readonly FieldInfo Packet=typeof(MagneticVehicle).GetField("preparedForces",BindingFlags.Instance|BindingFlags.NonPublic);
  static int assertions;
  static void Check(bool ok,string reason){assertions++;if(!ok)throw new Exception("Fair AI: "+reason);}
  static ReleaseBalance Balance()=>ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);
  static AiWorldSnapshot World(TrackRoute route,params AiObservation[] observations){
   var world=new AiWorldSnapshot(observations.Length);var cars=(AiObservation[])typeof(AiWorldSnapshot).GetField("cars",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(world);
   for(int i=0;i<cars.Length;i++){var o=observations[i];var f=route.Evaluate(o.Distance);o.Id=i;o.Position=f.position+f.right*o.Lateral;o.Forward=f.tangent;o.Up=f.normal;o.Grounded=true;cars[i]=o;}
   typeof(AiWorldSnapshot).GetProperty("Time").SetValue(world,10d);return world;
  }
  public static void RunContracts(string output){
   assertions=0;var balance=Balance();
   foreach(var profile in new[]{DriverProfile.Rookie,DriverProfile.Racer,DriverProfile.Ace}){
    var human=new DrivingBalance(balance);var ai=new DrivingBalance(balance);ai.ConfigureHandicap(profile,true);
    for(int tick=0;tick<600;tick++){
     var input=new DrivingInput{throttle=1,nitro=tick<350,brake=tick>=550?.4f:0};float speed=tick<550?55:30;
     var h=human.Step(input,speed,.01f,true,true,4,8);var a=ai.Step(input,speed,.01f,true,true,4,8);
     Check(h==a&&human.Charge==ai.Charge&&human.Active==ai.Active&&human.Ready==ai.Ready,"profile/legacy handicap changed resources or acceleration");
    }
   }
   Check(!LocalRaceConfig.Normalize(new LocalRaceConfig{handicap=true}).handicap,"legacy setting retained handicap");
   var road=new GameObject("parity road");var track=road.AddComponent<TrackBuilder>();track.Build(new TrackRoute(RoadVolumeChecks.FlatRoad()));
   try{
    foreach(float speed in new[]{0f,25f,80f})foreach(var input in new[]{new DrivingInput{throttle=1},new DrivingInput{throttle=1,steer=.35f},new DrivingInput{throttle=1,steer=-.6f,drift=true},new DrivingInput{brake=.8f,steer=.3f},new DrivingInput{throttle=1,nitro=true}}){
     var a=Prepared(track,balance,true,speed,input);var b=Prepared(track,balance,false,speed,input);
     Check(Vector3.Distance(a.acceleration,b.acceleration)<=.00001f&&Vector3.Distance(a.angularAcceleration,b.angularAcceleration)<=.00001f&&Vector3.Distance(a.force,b.force)<=.00001f&&Vector3.Distance(a.torque,b.torque)<=.00001f,"role changed prepared physical forces");
    }
    var route=track.Route;
    var defense=World(route,new AiObservation{Distance=100,Speed=25,Lateral=0},new AiObservation{Distance=76,Speed=29,Lateral=3});var driver=new AiDriver(0,DriverProfile.Ace,77,balance);driver.Step(defense,route,.1f,false);
    Check(driver.Maneuver==AiManeuver.Defend&&driver.DefendingAgainst==1,"safe closing challenger did not trigger defense");
    var blocked=World(route,new AiObservation{Distance=100,Speed=25},new AiObservation{Distance=76,Speed=29,Lateral=3},new AiObservation{Distance=100,Speed=25,Lateral=3});var guarded=new AiDriver(0,DriverProfile.Ace,77,balance);guarded.Step(blocked,route,.1f,false);
    Check(guarded.Maneuver!=AiManeuver.Defend,"defense crossed occupied lane");
    var observations=(AiObservation[])typeof(AiWorldSnapshot).GetField("cars",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(defense);observations[1].Distance=106;observations[1].Position=route.Evaluate(106).position+route.Evaluate(106).right*3;
    typeof(AiWorldSnapshot).GetProperty("Time").SetValue(defense,10.1d);driver.Step(defense,route,.1f,false);Check(driver.Target==1&&driver.Maneuver==AiManeuver.Pass,"passed defender did not counterattack");
    var fast=new AiDriver(0,DriverProfile.Ace,77,balance);float cruiseLane=((float)typeof(AiDriver).GetField("personality",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(fast)-.5f)*6;var free=World(route,new AiObservation{Distance=100,Speed=50,Charge=100,Lateral=cruiseLane});var gas=fast.Step(free,route,.1f);
    Check(gas.nitro&&gas.throttle==1&&gas.brake==0,"full nitro unused on safe straight");
    for(int tick=0;tick<400;tick++){typeof(AiWorldSnapshot).GetProperty("Time").SetValue(free,10d+tick*.1);var command=fast.Step(free,route,.1f,false);Check(command.steer==0&&command.brake==0,"timer corrupted unobstructed cruise");}
   }finally{UnityEngine.Object.DestroyImmediate(road);}
   AirSteeringChecks.Run();CloudlineChecks.RunConfigurationChecks();
   File.WriteAllText(Path.Combine(output,"fair-contracts.txt"),"FAIR_AI_CONTRACTS_OK assertions="+assertions+"\n");
  }
  static VehicleForceFrame Prepared(TrackBuilder track,ReleaseBalance balance,bool human,float speed,DrivingInput input){
   var go=new GameObject("parity entrant");try{var car=go.AddComponent<MagneticVehicle>();car.Initialize(track,0,Color.cyan,balance,human);car.enabled=false;car.ResetAt(100);car.Hold(false);car.Body.linearVelocity=car.Frame.tangent*speed+car.Frame.right*2;car.Body.angularVelocity=car.Frame.normal*.1f;car.SetRaceContext(true,3,8,10);car.SetInput(input);Physics.SyncTransforms();Tick.Invoke(car,null);return (VehicleForceFrame)Packet.GetValue(car);}finally{UnityEngine.Object.DestroyImmediate(go);Physics.SyncTransforms();}
  }
  [Serializable] public sealed class RaceResult {public string theme;public int seed,boostTicks,passes,respawns;public float minProgress,nearbySeconds,maxStallSeconds;public bool productionRoster;public float[] progress;}
  public static void RunRaces(string output)=>RunRaces(output,8,30,false);
  public static void RunMovingPackRaces(string output)=>RunRaces(output,16,60,true);
  static void RunRaces(string output,int count,int seconds,bool extended){
   foreach(bool production in extended?new[]{true}:new[]{false,true})foreach(string theme in new[]{"cloud-city","space-station"}){
    var root=new GameObject("fair race "+theme);try{
     var road=new GameObject("road");road.transform.SetParent(root.transform);var track=road.AddComponent<TrackBuilder>();track.Build(new TrackRoute(Procedural.Generator.Generate(77,"full",theme,false,true)));
     var balance=Balance();var cars=new MagneticVehicle[count];var drivers=new AiDriver[count];var commands=new DrivingInput[count];var initial=new float[count];var revisions=new int[count];var world=new AiWorldSnapshot(count);var roster=new RaceRoster(count,1,77);roster.Shuffle(77+7919);var stalls=new float[count];var passed=new bool[count,count];var trace=new System.Text.StringBuilder("time,id,distance,lateral,speed,yaw,steer,heading,desired,limit,nitro,grounded,revision,charge,reason,target,maneuver,branch,branchLane,corridor\n");var result=new RaceResult{theme=theme,seed=77,productionRoster=production,progress=new float[count]};
     for(int i=0;i<count;i++){var go=new GameObject("entrant "+i);go.transform.SetParent(root.transform);cars[i]=go.AddComponent<MagneticVehicle>();cars[i].Initialize(track,i,Color.cyan,balance,i==0);cars[i].enabled=false;var entry=roster.Entrants[i];cars[i].ResetAt(production?RaceRoster.GridDistance(track.Route,entry.GridSlot):80+(i/2)*10,production?RaceRoster.GridLateral(track.Route,entry.GridSlot):i%2==0?-2.8f:2.8f);initial[i]=cars[i].Distance;revisions[i]=cars[i].PositionRevision;drivers[i]=new AiDriver(i,production?(entry.Profile==DriverProfile.Human?DriverProfile.Ace:entry.Profile):i%3==0?DriverProfile.Ace:i%3==1?DriverProfile.Racer:DriverProfile.Rookie,production?entry.Seed:(uint)(77+i*31),balance);}
     Physics.SyncTransforms();
     for(int tick=0;tick<seconds*100;tick++){
      double time=10+tick*.01;
      for(int i=0;i<count;i++){int place=1;for(int j=0;j<count;j++)if(cars[j].Distance>cars[i].Distance)place++;cars[i].Hold(false);cars[i].SetRaceContext(true,place,count,time);cars[i].PrepareProjection();}
      if(tick%10==0){world.Capture(cars,track.Route,time);for(int i=0;i<count;i++)commands[i]=drivers[i].Step(world,track.Route,.1f);}
      for(int i=0;i<count;i++){cars[i].SetInput(commands[i]);Tick.Invoke(cars[i],null);if(cars[i].Drive.Active)result.boostTicks++;}
      RacePhysicsStepper.Simulate(.01f);
      if(tick%10==0){world.Capture(cars,track.Route,time);for(int i=0;i<count;i++){var o=world[i];trace.AppendLine($"{tick*.01f:F2},{i},{o.Distance:F3},{o.Lateral:F3},{o.Speed:F3},{o.YawRate:F3},{commands[i].steer:F3},{drivers[i].HeadingError:F3},{drivers[i].DesiredSpeed:F3},{drivers[i].SpeedLimitSource},{cars[i].Drive.Active},{o.Grounded},{o.Revision},{o.Charge:F2},{drivers[i].NitroReason},{drivers[i].Target},{drivers[i].Maneuver},{drivers[i].BranchIndex},{drivers[i].BranchLane},{drivers[i].CorridorBlocked}");}}
      for(int i=0;i<count;i++){stalls[i]=tick>200&&cars[i].Body.linearVelocity.magnitude<3?stalls[i]+.01f:0;result.maxStallSeconds=Mathf.Max(result.maxStallSeconds,stalls[i]);if(cars[i].PositionRevision!=revisions[i]){result.respawns++;revisions[i]=cars[i].PositionRevision;}
       for(int j=0;j<count;j++)if(i!=j){if(Mathf.Abs(cars[i].Distance-cars[j].Distance)<25)result.nearbySeconds+=.01f/count;if(initial[i]<initial[j]-4&&!passed[i,j]&&cars[i].Distance>cars[j].Distance+4){passed[i,j]=true;result.passes++;}}
      }
     }
     result.minProgress=float.PositiveInfinity;for(int i=0;i<count;i++){result.progress[i]=cars[i].Distance-initial[i];result.minProgress=Mathf.Min(result.minProgress,result.progress[i]);}
     File.WriteAllText(Path.Combine(output,(extended?"moving-pack-":"fair-race-")+theme+(production?"-roster":"")+"-trace.csv"),trace.ToString());
     File.WriteAllText(Path.Combine(output,(extended?"moving-pack-":"fair-race-")+theme+(production?"-roster":"")+".json"),JsonUtility.ToJson(result,true));
     if(production)Check(result.maxStallSeconds<5,"production roster sustained jam "+theme);
     Check(result.minProgress>=500,"sustained race lost useful pace "+theme);Check(result.respawns==0,"sustained race recovery "+theme);Check(result.boostTicks>100,"sustained race did not use nitro "+theme);Check(result.passes>0&&result.nearbySeconds>10,"sustained race lacked competition "+theme);
    }finally{UnityEngine.Object.DestroyImmediate(root);}
   }
   File.WriteAllText(Path.Combine(output,extended?"moving-pack-races.txt":"fair-races.txt"),extended?"MOVING_PACK_RACES_OK cases=2 durationSeconds=60 entrants=16 productionRoster\n":"FAIR_AI_RACES_OK cases=4 durationSeconds=30 entrants=8 stressAndProductionRoster\n");
  }
 }
}
