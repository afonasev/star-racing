using System;
using UnityEditor;
using UnityEngine;
namespace StarRacingPrototype {
 public static class PrototypeChecks {
  static int assertions;
  [Serializable] sealed class Receipt { public string token,balanceHash,utc; public int assertions,rosterCases,ghostAssertions; public double methodSeconds; }
  static void Require(bool valid,string message){assertions++;if(!valid)throw new Exception(message);}
  public static void RunWithFixtureEquivalence(){
   // All fixtures are local and destroyed by Run before pure geometry regressions start.
   Run();RosterFixtureEquivalenceChecks.Run();
  }
  public static void Run(){
   assertions=0;int rosterCases=0;var watch=System.Diagnostics.Stopwatch.StartNew();
   string token=Environment.GetEnvironmentVariable("STAR_RACING_QA_TOKEN")??Guid.NewGuid().ToString();
   Debug.Log("PROTOTYPE_QA_BEGIN "+JsonUtility.ToJson(new Receipt{token=token,utc=DateTime.UtcNow.ToString("O")}));
   var balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);Require(balance.Count==50,"balance schema");
   foreach(string theme in new[]{"cloud-city","space-station"}){
    // Local geometry fixture: roster reads it, never mutates it. No cross-run cache.
    var route=new TrackRoute(Procedural.Generator.Generate(77,"normal",theme,false,true));
    foreach(int count in new[]{2,8,64}){
    var roster=new RaceRoster(count,2,77);rosterCases++;
    var slots=new bool[count];foreach(var e in roster.Entrants){Require(!slots[e.GridSlot],"duplicate grid slot");slots[e.GridSlot]=true;float d=RaceRoster.GridDistance(route,e.GridSlot);float lane=RaceRoster.GridLateral(route,e.GridSlot);Require(float.IsFinite(lane)&&d<route.FinishDistance,"unsafe grid");}
    Require(roster.Entrants[0].HumanSeat==0&&roster.Entrants[1].HumanSeat==1,"human seats");for(int i=2;i<count;i++)Require(roster.Entrants[i].HumanSeat<0,"AI seat");
   }
   }
   // Real race rules: countdown, pause, checkpoints, natural crossings and finish window.
   var session=new RaceSession(0,100,8);var observations=new RaceObservation[8];for(int i=0;i<8;i++)observations[i]=new RaceObservation(0);session.Reset(observations);session.Begin();session.Tick(3,observations);Require(session.Phase==RacePhase.Racing,"countdown");session.Paused=true;session.Tick(2,observations);Require(session.Elapsed==0,"pause advances clock");session.Paused=false;
   for(int d=10;d<=100;d+=10){for(int i=0;i<8;i++)observations[i]=new RaceObservation(Mathf.Max(0,d-i));session.Tick(.2,observations);}Require(session.Racers[0].Finished&&!session.Racers[7].Finished,"crossing finish");session.Tick(11,observations);Require(session.Phase==RacePhase.Results,"finish window");session.Reset(observations);Require(session.Phase==RacePhase.Ready&&!session.Racers[0].Finished,"reset");
   var recovering=new RaceSession(0,100);recovering.Begin();recovering.Tick(3,new RaceObservation(0),new RaceObservation(0));
   recovering.Tick(.2,new RaceObservation(25),new RaceObservation(25));recovering.Tick(.2,new RaceObservation(45),new RaceObservation(45));
   recovering.Tick(.2,new RaceObservation(52,1),new RaceObservation(52,1));Require(!recovering.Racers[0].Finished,"recovery grants finish");
   recovering.Tick(.2,new RaceObservation(60,1),new RaceObservation(60,1));recovering.Tick(.2,new RaceObservation(80,1),new RaceObservation(80,1));recovering.Tick(.2,new RaceObservation(101,1),new RaceObservation(101,1));Require(recovering.Racers[0].Finished,"straddled checkpoint stalls race");
   var teleport=new RaceSession(0,100);teleport.Begin();teleport.Tick(3,new RaceObservation(0),new RaceObservation(0));teleport.Tick(.2,new RaceObservation(90,1),new RaceObservation(90,1));teleport.Tick(.2,new RaceObservation(101,1),new RaceObservation(101,1));Require(!teleport.Racers[0].Finished,"large teleport grants finish");
   var checkpointObject=new GameObject("checkpoint checks");var checkpointTrack=checkpointObject.AddComponent<TrackBuilder>();
   try{
    checkpointTrack.Build(new TrackRoute(Procedural.Generator.Generate(77,"normal","cloud-city",false,true)));Physics.SyncTransforms();
    var frame=checkpointTrack.Route.Evaluate(80);var pose=frame.position+frame.normal*1.1f;
    Require(CheckpointAdmission.CheckPose(checkpointTrack,pose,frame.Rotation,80).accepted,"road checkpoint refused");
    Require(!CheckpointAdmission.CheckPose(checkpointTrack,pose+frame.right*frame.halfWidth,frame.Rotation,80).accepted,"edge checkpoint accepted");
    var obstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);obstacle.transform.SetParent(checkpointTrack.transform);obstacle.transform.position=pose;Physics.SyncTransforms();
    Require(!CheckpointAdmission.CheckPose(checkpointTrack,pose,frame.Rotation,80).accepted,"blocked checkpoint accepted");
   }finally{UnityEngine.Object.DestroyImmediate(checkpointObject);}
   RecoveryGhostChecks.Run();
   PrototypeBuilder.Prepare();Debug.Log("PROTOTYPE_QA_END "+JsonUtility.ToJson(new Receipt{token=token,balanceHash=balance.Hash,assertions=assertions,rosterCases=rosterCases,ghostAssertions=RecoveryGhostChecks.AssertionCount,methodSeconds=watch.Elapsed.TotalSeconds}));Debug.Log("PROTOTYPE_CHECKS_OK roster2/8/64 bothThemes countdown pause checkpoint finish results reset resources");
  }
 }
}
