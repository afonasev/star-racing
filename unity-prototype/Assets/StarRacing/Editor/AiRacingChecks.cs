using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace StarRacingPrototype {
 // Bounded affected checks, never the aggregate game suite or a Player build.
 [InitializeOnLoad] public static class AiRacingChecks {
  const string Key="StarRacing.CompetitiveAI";
  static readonly MethodInfo VehicleTick=typeof(MagneticVehicle).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
  static readonly FieldInfo Observations=typeof(AiWorldSnapshot).GetField("cars",BindingFlags.Instance|BindingFlags.NonPublic);
  static readonly PropertyInfo SnapshotTime=typeof(AiWorldSnapshot).GetProperty("Time");
  static int startup,assertions;
  static bool Baseline=>SessionState.GetBool(Key+"Baseline",false);
  static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_AI_QA_DIR");
  static AiRacingChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void RunBaseline(){SessionState.SetBool(Key+"Baseline",true);Begin();}
  public static void Run(){SessionState.SetBool(Key+"QueueOnly",false);SessionState.SetBool(Key+"Baseline",false);Begin();}
  public static void RunQueueDiagnostic(){SessionState.SetBool(Key+"QueueOnly",true);SessionState.SetBool(Key+"Baseline",true);Begin();}
  static void Begin(){if(string.IsNullOrEmpty(Output))throw new Exception("STAR_RACING_AI_QA_DIR required");Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);startup=0;EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.update+=Enter;}
  static void Enter(){if(++startup<10)return;EditorApplication.update-=Enter;EditorApplication.isPlaying=true;}
  static void Changed(PlayModeStateChange state){if(!SessionState.GetBool(Key,false))return;if(state==PlayModeStateChange.EnteredPlayMode)EditorApplication.delayCall+=Execute;if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);}}
  static void Check(bool ok,string why){assertions++;if(!ok)throw new Exception("Competitive AI: "+why);}
  static ReleaseBalance Balance()=>ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);
  static Procedural.Definition LongRoad(){var d=RoadVolumeChecks.FlatRoad();var s=new Procedural.Sample[601];for(int i=0;i<s.Length;i++)s[i]=new Procedural.Sample{index=i,distance=i*5,progress=i/600.0,halfWidth=8,position=new Procedural.DVec(1000,120,i*5),tangent=new Procedural.DVec(0,0,1),normal=new Procedural.DVec(0,1,0),right=new Procedural.DVec(-1,0,0),kind="straight"};d.samples=s;d.totalLength=3000;return d;}
  static void Execute(){var mode=Physics.simulationMode;float dt=Time.fixedDeltaTime,volume=AudioListener.volume;assertions=0;var report=new Report();try{Physics.simulationMode=SimulationMode.Script;Time.fixedDeltaTime=.01f;AudioListener.volume=0;
   if(!Baseline)Decisions();
   var failures=new List<string>();
   void RunCase(string label,int count,bool generated,bool stopped){try{report.cases.Add(Race(label,count,generated,stopped));}catch(Exception e){failures.Add(e.Message);Debug.LogError(e.Message);File.WriteAllText(Path.Combine(Output,label+"-failure.txt"),e.ToString());}}
   RunCase("stopped-queue",4,false,true);
   if(!SessionState.GetBool(Key+"QueueOnly",false))RunCase("slow-leader",2,false,false);
   if(!SessionState.GetBool(Key+"QueueOnly",false))foreach(int count in new[]{8,32,64})RunCase("pack-"+count,count,true,false);
   File.WriteAllText(Path.Combine(Output,"races.json"),JsonUtility.ToJson(report,true));
   if(failures.Count>0)throw new Exception(string.Join("; ",failures));
   File.WriteAllText(Path.Combine(Output,"result.txt"),$"AI_{(Baseline?"BASELINE":"AFFECTED_OK")} cases={report.cases.Count} assertions={assertions}\n");
  }catch(Exception e){SessionState.SetBool(Key+"Failed",true);Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());File.WriteAllText(Path.Combine(Output,"races.json"),JsonUtility.ToJson(report,true));}finally{Physics.simulationMode=mode;Time.fixedDeltaTime=dt;AudioListener.volume=volume;EditorApplication.isPlaying=false;}}
  [Serializable] public sealed class Result {public string label;public int count,hits,respawns,passes;public float medianProgress,firstPass=-1;public float stoppedCarSeconds;}
  [Serializable] public sealed class Report {public List<Result> cases=new List<Result>();}
  static Result Race(string label,int count,bool generated,bool stopped){var root=new GameObject(label);try{
   var obj=new GameObject("road");obj.transform.SetParent(root.transform);var track=obj.AddComponent<TrackBuilder>();track.Build(new TrackRoute(generated?Procedural.Generator.Generate(77,"full","cloud-city",true,true):LongRoad()));
   var balance=Balance();var cars=new MagneticVehicle[count];var drivers=new AiDriver[count];var world=new AiWorldSnapshot(count);var commands=new DrivingInput[count];var initial=new float[count];var revisions=new int[count];var seenPass=new bool[count,count];var result=new Result{label=label,count=count};var trace=new System.Text.StringBuilder("time,id,distance,lateral,speed,grounded,heading,steer,desired,lane,maneuver,blocked,reverse,progress,limit,escaping\n");
   for(int i=0;i<count;i++){var go=new GameObject("AI "+i);go.transform.SetParent(root.transform);var c=go.AddComponent<MagneticVehicle>();c.Initialize(track,i,Color.cyan,balance,false);c.enabled=false;c.ResetAt(generated?80+(i/4)*5:100-i*4.7f,generated?(i%4-1.5f)*2.65f:0);c.Body.linearVelocity=c.Frame.tangent*(generated?7:stopped?0:i==0?7:10);initial[i]=c.Distance;revisions[i]=c.PositionRevision;cars[i]=c;drivers[i]=new AiDriver(i,DriverProfile.Racer,(uint)(77+i*31),balance);c.ContactObserved+=hit=>{if(hit.rigidbody!=null)result.hits++;};}
   if(stopped){cars[0].enabled=false;}
   Physics.SyncTransforms();
   for(int tick=0;tick<1200;tick++){
    double now=10+tick*.01;
    for(int i=0;i<count;i++){cars[i].Hold(false);cars[i].SetRaceContext(true,1,count,now);cars[i].PrepareProjection();}
    if(tick%10==0){world.Capture(cars,track.Route,now);for(int i=0;i<count;i++)if(!(stopped&&i==0))commands[i]=!generated&&i==0?new DrivingInput{throttle=1,brake=cars[i].Body.linearVelocity.magnitude>7?1:0}:drivers[i].Step(world,track.Route,.1f,false);}
    for(int i=0;i<count;i++)if(!(stopped&&i==0)){cars[i].SetInput(commands[i]);VehicleTick.Invoke(cars[i],null);}
    RacePhysicsStepper.Simulate(.01f);
    if(!generated&&tick%20==0){world.Capture(cars,track.Route,now);for(int i=1;i<count;i++){var o=world[i];var driver=drivers[i];trace.AppendLine($"{tick*.01f:F2},{i},{o.Distance:F3},{o.Lateral:F3},{o.Speed:F3},{o.Grounded},{driver.HeadingError:F2},{commands[i].steer:F3},{driver.DesiredSpeed:F3},{driver.Lane:F3},{driver.Maneuver},{driver.TrafficBlocked},{driver.ReverseActive},{driver.NoProgressSeconds:F3},{driver.SpeedLimitSource},{driver.Escaping}");}}

    for(int i=0;i<count;i++){if(stopped&&i==0)continue;if(cars[i].PositionRevision!=revisions[i]){result.respawns++;revisions[i]=cars[i].PositionRevision;}if(tick>200&&cars[i].Body.linearVelocity.magnitude<3)result.stoppedCarSeconds+=.01f;for(int j=0;j<count;j++)if(initial[i]<initial[j]-4&&!seenPass[i,j]&&cars[i].Distance>cars[j].Distance+4){seenPass[i,j]=true;result.passes++;if(result.firstPass<0)result.firstPass=tick*.01f;}}
   }
   var progress=new float[count-(stopped?1:0)];for(int i=stopped?1:0;i<count;i++)progress[i-(stopped?1:0)]=cars[i].Distance-initial[i];Array.Sort(progress);result.medianProgress=progress[progress.Length/2];
   Debug.Log($"AI_RACE {label} median={result.medianProgress:F1} passes={result.passes} first={result.firstPass:F2} stopped={result.stoppedCarSeconds:F2} hits={result.hits} respawns={result.respawns}");
   if(!generated)File.WriteAllText(Path.Combine(Output,label+"-trace.csv"),trace.ToString());
   // Persist before assertions so failed behavioral gates remain reviewable.
   File.WriteAllText(Path.Combine(Output,label+".json"),JsonUtility.ToJson(result,true));
   if(!Baseline){Check(result.medianProgress>=30,label+" no useful progress");if(!generated)Check(result.passes>0,label+" did not complete an open-side pass");Check(result.respawns==0,label+" unexpected recovery");}
   return result;
  }finally{UnityEngine.Object.DestroyImmediate(root);}}
  static AiWorldSnapshot Snapshot(TrackRoute route,params AiObservation[] observations){var world=new AiWorldSnapshot(observations.Length);var target=(AiObservation[])Observations.GetValue(world);for(int i=0;i<target.Length;i++){var value=observations[i];var f=route.Evaluate(value.Distance);value.Id=i;value.Position=f.position+f.right*value.Lateral;value.Forward=f.tangent;value.Up=f.normal;value.Grounded=true;target[i]=value;}SnapshotTime.SetValue(world,10d);return world;}
  static void SetDriver(AiDriver driver,string name,object value){typeof(AiDriver).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(driver,value);}
  static void Advance(AiWorldSnapshot world,double time){SnapshotTime.SetValue(world,time);}
  static void Decisions(){
   var balance=Balance();var route=new TrackRoute(LongRoad());
   // A neighboring car closes one side; the other side must be chosen and held.
   foreach(float blockedSide in new[]{-1f,1f}){
    var world=Snapshot(route,new AiObservation{Distance=80,Speed=7},new AiObservation{Distance=85,Speed=7},new AiObservation{Distance=80,Speed=7,Lateral=blockedSide*2.7f});
    var driver=new AiDriver(0,DriverProfile.Racer,77,balance);
    var moving=(AiObservation[])Observations.GetValue(world);
    for(int tick=0;tick<10;tick++){for(int i=0;i<moving.Length;i++){moving[i].Distance+=.7f;moving[i].Position+=route.Evaluate(moving[i].Distance).tangent*.7f;}Advance(world,10+tick*.1);driver.Step(world,route,.1f,false);Check(driver.Maneuver==AiManeuver.Pass&&driver.PassingSide==-blockedSide,"did not choose/hold free side");}
    Check(driver.NoProgressSeconds<.01f,"safe moving maneuver misclassified");
   }
   // The same seed/profile/input stream is exact, including bounded random errors.
   var replay=Snapshot(route,new AiObservation{Distance=80,Speed=7},new AiObservation{Distance=90,Speed=6});
   var a=new AiDriver(0,DriverProfile.Racer,91,balance);var b=new AiDriver(0,DriverProfile.Racer,91,balance);
   for(int tick=0;tick<120;tick++){Advance(replay,10+tick*.1);var x=a.Step(replay,route,.1f);var y=b.Step(replay,route,.1f);Check(x.throttle==y.throttle&&x.brake==y.brake&&x.steer==y.steer&&x.nitro==y.nitro&&a.PassingSide==b.PassingSide,"decision replay mismatch");}
   for(uint seed=0;seed<3;seed++){var driver=new AiDriver(0,DriverProfile.Rookie,seed,balance);Check((int)driver.Temperament==seed,"temperaments collapsed");Check(driver.Temperament==new AiDriver(0,DriverProfile.Ace,seed,balance).Temperament,"temperament coupled to skill");}
   var narrow=LongRoad();foreach(var sample in narrow.samples)sample.halfWidth=4;var corridor=new TrackRoute(narrow);
   // Neither side fits a whole car. Pressure is an ordinary short low-speed input,
   // not nitro or a new force; it cannot restart without a cooldown.
   var pressWorld=Snapshot(corridor,new AiObservation{Distance=80},new AiObservation{Distance=84.7f});var pressing=new AiDriver(0,DriverProfile.Racer,1,balance);
   pressing.Step(pressWorld,corridor,.1f);Check(pressing.ContactPressure&&!pressing.NitroCommand,"assertive driver did not try bounded pressure");
   for(int tick=1;tick<=10;tick++){Advance(pressWorld,10+tick*.1);pressing.Step(pressWorld,corridor,.1f);Check(!pressing.NitroCommand,"nitro during pressure");}
   Check(!pressing.ContactPressure,"pressure outlived its useful window");
   var patient=new AiDriver(0,DriverProfile.Racer,0,balance);Advance(pressWorld,10);patient.Step(pressWorld,corridor,.1f);Check(!patient.ContactPressure,"patient temperament is indistinguishable");
   bool beganReverse=false;double beganAt=0;
   for(int tick=1;tick<40;tick++){Advance(pressWorld,10+tick*.1);patient.Step(pressWorld,corridor,.1f,false);if(patient.ReverseActive){beganReverse=true;beganAt=10+tick*.1;break;}}
   Check(beganReverse,"aligned stopped queue never tried reverse");Advance(pressWorld,beganAt+.1);patient.Step(pressWorld,corridor,.1f,false);Check(patient.ReverseActive,"correct heading cancelled reverse behind blocker");
   var obs=(AiObservation[])Observations.GetValue(pressWorld);obs[0].Revision++;Advance(pressWorld,beganAt+.2);patient.Step(pressWorld,corridor,.1f,false);Check(!patient.ReverseActive&&patient.NoProgressSeconds==0,"revision retained stale maneuver");
   var rearWorld=Snapshot(corridor,new AiObservation{Distance=80},new AiObservation{Distance=84.7f},new AiObservation{Distance=76});var rearBlocked=new AiDriver(0,DriverProfile.Racer,0,balance);
   for(int tick=0;tick<40;tick++){Advance(rearWorld,10+tick*.1);rearBlocked.Step(rearWorld,corridor,.1f,false);Check(!rearBlocked.ReverseActive,"reverse entered occupied rear path");}
   // A distant retained target must not prevent an overtake of a new blocker.
   var retargetWorld=Snapshot(route,new AiObservation{Distance=80,Speed=7},new AiObservation{Distance=90,Speed=6},new AiObservation{Distance=85,Speed=0,Finished=true});var retarget=new AiDriver(0,DriverProfile.Racer,91,balance);
   retarget.Step(retargetWorld,route,.1f,false);var retargetObs=(AiObservation[])Observations.GetValue(retargetWorld);retargetObs[1].Distance=400;retargetObs[2].Finished=false;Advance(retargetWorld,10.1);retarget.Step(retargetWorld,route,.1f,false);Check(retarget.Target==2,"remote target locked out a nearer blocker");
   // A failed side gets a fresh attempt window, rather than flipping every tick.
   var fixedWorld=Snapshot(route,new AiObservation{Distance=80},new AiObservation{Distance=85});var fixedDriver=new AiDriver(0,DriverProfile.Racer,77,balance);int previousSide=0;double changedAt=-10;
   for(int tick=0;tick<70;tick++){double now=10+tick*.1;Advance(fixedWorld,now);fixedDriver.Step(fixedWorld,route,.1f,false);int side=fixedDriver.PassingSide;if(side!=0&&previousSide!=0&&side!=previousSide){Check(now-changedAt>=.5,"side switched without a new attempt window");changedAt=now;}if(side!=0)previousSide=side;}
   // A yield to a different neighbor is invalid after that neighbor respawns.
   var yieldWorld=Snapshot(route,new AiObservation{Distance=80},new AiObservation{Distance=85},new AiObservation{Distance=80,Lateral=3});var yielding=new AiDriver(0,DriverProfile.Racer,77,balance);yielding.Step(yieldWorld,route,.1f,false);
   SetDriver(yielding,"yieldEntrant",2);SetDriver(yielding,"yieldRevision",0);SetDriver(yielding,"yieldUntil",12d);var yieldObs=(AiObservation[])Observations.GetValue(yieldWorld);yieldObs[2].Revision=1;Advance(yieldWorld,10.1);yielding.Step(yieldWorld,route,.1f,false);Check(yielding.Maneuver!=AiManeuver.Yield,"yield survived neighbor revision");
   // Productive merge waiting must not become a reverse maneuver.
   var mergeWorld=Snapshot(corridor,new AiObservation{Distance=80},new AiObservation{Distance=84.7f,Speed=4});var merging=new AiDriver(0,DriverProfile.Racer,0,balance);merging.Step(mergeWorld,corridor,.1f,false);
   for(int tick=1;tick<40;tick++){typeof(AiDriver).GetProperty("MergeYieldTo").SetValue(merging,1);SetDriver(merging,"mergeYieldRevision",0);Advance(mergeWorld,10+tick*.1);merging.Step(mergeWorld,corridor,.1f,false);Check(!merging.ReverseActive,"productive merge wait caused reverse");}
   // Backing, arresting negative velocity, and the free-side escape retain one plan.
   var exitWorld=Snapshot(route,new AiObservation{Distance=80},new AiObservation{Distance=84.7f});var exiting=new AiDriver(0,DriverProfile.Racer,77,balance);
   for(int tick=0;tick<60&&!exiting.ReverseActive;tick++){Advance(exitWorld,10+tick*.1);exiting.Step(exitWorld,route,.1f,false);}
   Check(exiting.ReverseActive&&exiting.Escaping,"jam did not preserve a free-side escape");var exitObs=(AiObservation[])Observations.GetValue(exitWorld);exitObs[0].Distance-=2;exitObs[0].Speed=-3;exitObs[0].Position=route.Evaluate(exitObs[0].Distance).position;Advance(exitWorld,exitWorld.Time+.1);exiting.Step(exitWorld,route,.1f,false);Check(exiting.ReverseActive,"early aligned gap terminated useful backing");
   exitObs[0].Distance-=4.1f;exitObs[0].Speed=-5;exitObs[0].Position=route.Evaluate(exitObs[0].Distance).position;Advance(exitWorld,exitWorld.Time+.1);var arrest=exiting.Step(exitWorld,route,.1f,false);Check(!exiting.ReverseActive&&exiting.Escaping&&arrest.throttle==1&&!arrest.nitro,"escape lost while arresting reverse");
   exitObs[0].Speed=2;Advance(exitWorld,exitWorld.Time+.1);exiting.Step(exitWorld,route,.1f,false);Check(exiting.Escaping&&exiting.DesiredSpeed<=4&&!exiting.NitroCommand,"escape accelerated before actual clearance");
   var offsetRear=Snapshot(route,new AiObservation{Distance=80},new AiObservation{Distance=84.7f},new AiObservation{Distance=76,Lateral=2.8f});var offsetDriver=new AiDriver(0,DriverProfile.Racer,77,balance);
   for(int tick=0;tick<40;tick++){Advance(offsetRear,10+tick*.1);offsetDriver.Step(offsetRear,route,.1f,false);Check(!offsetDriver.ReverseActive,"rear path ignored physical body width");}
   var revisionWorld=Snapshot(corridor,new AiObservation{Distance=80},new AiObservation{Distance=84.7f});var invalidated=new AiDriver(0,DriverProfile.Racer,0,balance);
   for(int tick=0;tick<40&&!invalidated.ReverseActive;tick++){Advance(revisionWorld,10+tick*.1);invalidated.Step(revisionWorld,corridor,.1f,false);}
   Check(invalidated.ReverseActive,"revision test never entered reverse");var revisionObs=(AiObservation[])Observations.GetValue(revisionWorld);revisionObs[0].Speed=-3;revisionObs[1].Revision++;Advance(revisionWorld,revisionWorld.Time+.1);var cancel=invalidated.Step(revisionWorld,corridor,.1f,false);Check(!invalidated.ReverseActive&&cancel.throttle==1&&cancel.steer==0&&!cancel.nitro,"target revision did not cancel and arrest reverse");
   // Low route progress counts even above the old 3 m/s velocity threshold.
   var crawling=Snapshot(corridor,new AiObservation{Distance=80,Speed=4},new AiObservation{Distance=84.7f,Speed=4});var trapped=new AiDriver(0,DriverProfile.Racer,0,balance);var crawlObs=(AiObservation[])Observations.GetValue(crawling);
   for(int tick=0;tick<18;tick++){crawlObs[0].Distance+=.1f;crawlObs[1].Distance+=.1f;Advance(crawling,10+tick*.1);trapped.Step(crawling,corridor,.1f,false);}
   Check(trapped.NoProgressSeconds>1.5f,"slow jam with misleading forward speed was missed");
   // Preserve geometric support envelopes and reset/finished behavior.
   Check(AiDriver.CrestSpeedLimit(.02f,18,100)<100,"crest envelope removed");Check(AiDriver.VerticalSpeedLimit(-1,.02f,18,100,true)==100,"loop envelope altered");
   var finish=(AiObservation[])Observations.GetValue(replay);finish[0].Finished=true;Advance(replay,30);var stopped=a.Step(replay,route,.1f);Check(stopped.throttle==0&&!stopped.nitro&&!a.ReverseActive,"finished car retained commands");
   Debug.Log("AI_DECISIONS_OK assertions="+assertions);
  }
  static void GeometryContracts(){
   int before=assertions;var balance=Balance();int cases=0;
   foreach(string theme in new[]{"cloud-city","space-station"}){
    var route=new TrackRoute(Procedural.Generator.Generate(77,"full",theme,true,true));
    foreach(var branch in route.Definition.branches)foreach(float distance in new[]{30+branch.startIndex*5f,30+branch.endIndex*5f}){
     float lane=-(float)route.PavedAt(distance)[0].offset;var world=Snapshot(route,new AiObservation{Distance=distance,Lateral=lane,Speed=12},new AiObservation{Distance=distance+6,Lateral=lane,Speed=10});var driver=new AiDriver(0,DriverProfile.Racer,77,balance);var input=driver.Step(world,route,.1f,false);
     Check(!float.IsNaN(input.steer)&&!float.IsNaN(driver.DesiredSpeed),"branch produced invalid command");bool paved=false;foreach(var span in route.PavedAt(distance+Mathf.Clamp(12*.3f,7,25)))paved|=Mathf.Abs(driver.Lane+(float)span.offset)+VehicleGeometry.HalfWidth<=span.halfWidth;
     Check(paved,"maneuver left paved branch footprint");cases++;
    }
    foreach(var jump in route.Definition.jumps){float distance=30+(jump.launchIndex+jump.gapEndIndex)*2.5f;float lane=-(float)jump.lateralCenter;var world=Snapshot(route,new AiObservation{Distance=distance,Lateral=lane,Speed=40},new AiObservation{Distance=distance+5,Lateral=lane,Speed=40});var observations=(AiObservation[])Observations.GetValue(world);observations[0].Grounded=false;var driver=new AiDriver(0,DriverProfile.Racer,77,balance);var input=driver.Step(world,route,.1f);
     Check(!input.nitro&&!driver.ReverseActive&&!driver.ContactPressure,"airborne traffic activated grounded maneuver");Check(!float.IsNaN(input.steer)&&!float.IsNaN(driver.DesiredSpeed),"jump produced invalid command");cases++;
    }
   }
   File.WriteAllText(Path.Combine(Output,"geometry-result.txt"),$"AI_GEOMETRY_OK cases={cases} assertions={assertions-before}\n");
  }
  public static void RunPerformance(){if(string.IsNullOrEmpty(Output))throw new Exception("STAR_RACING_AI_QA_DIR required");Directory.CreateDirectory(Output);var route=new TrackRoute(LongRoad());var obs=new AiObservation[64];for(int i=0;i<obs.Length;i++)obs[i]=new AiObservation{Distance=80+i/4*5,Lateral=(i%4-1.5f)*2.65f,Speed=7};var world=Snapshot(route,obs);var balance=Balance();var drivers=new AiDriver[64];for(int i=0;i<64;i++)drivers[i]=new AiDriver(i,DriverProfile.Racer,(uint)(77+i*31),balance);
   for(int s=0;s<100;s++){Advance(world,10+s*.1);foreach(var d in drivers)d.Step(world,route,.1f,false);}
   var samples=new double[300];long bytes=0;var watch=new System.Diagnostics.Stopwatch();for(int s=0;s<samples.Length;s++){Advance(world,20+s*.1);long before=GC.GetAllocatedBytesForCurrentThread();watch.Restart();foreach(var d in drivers)d.Step(world,route,.1f,false);watch.Stop();bytes+=GC.GetAllocatedBytesForCurrentThread()-before;samples[s]=watch.Elapsed.TotalMilliseconds;}Array.Sort(samples);File.WriteAllText(Path.Combine(Output,"performance.json"),$"{{\"samples\":300,\"drivers\":64,\"p95ms\":{samples[284].ToString("R",System.Globalization.CultureInfo.InvariantCulture)},\"p99ms\":{samples[296].ToString("R",System.Globalization.CultureInfo.InvariantCulture)},\"allocatedBytes\":{bytes}}}\n");Debug.Log($"AI_COST p95ms={samples[284]:F4} p99ms={samples[296]:F4} bytes={bytes}");GeometryContracts();
  }
 }
}
