using System;
using UnityEngine;
namespace StarRacingPrototype {
 public static class RaceSessionChecks {
  static int assertions;
  static void Check(bool value,string message){assertions++;if(!value)throw new Exception(message);}
  public static void Run(){
   assertions=0;
   foreach(int humans in new[]{1,2,3,4})foreach(int count in new[]{8,16,32,64}){
    var session=new RaceSession(0,20,count,humans);var seen=new RaceObservation[count];
    for(int i=0;i<count;i++)seen[i]=new RaceObservation(0);
    session.Reset(seen);session.Begin();session.Tick(3,seen);
    // AI can finish first without starting a timeout for human players.
    seen[count-1]=new RaceObservation(21);session.Tick(1,seen);session.Tick(20,seen);
    Check(session.Phase==RacePhase.Racing,"AI starts results timer");
    for(int i=0;i<humans;i++){
     seen[i]=new RaceObservation(21);session.Tick(1,seen);
     if(i<humans-1){session.Tick(20,seen);Check(session.Phase==RacePhase.Racing,"early human starts results timer");}
    }
    double last=session.Racers[humans-1].FinishTime;
    Check(session.Phase==RacePhase.FinishWindow,"last human must start countdown");
    Check(Math.Abs(session.Remaining-(last+3-session.Elapsed))<1e-8,"timer starts from crossing time");
    session.Paused=true;double elapsed=session.Elapsed;session.Tick(9,seen);
    Check(session.Elapsed==elapsed,"pause advances results timer");session.Paused=false;
    seen[humans]=new RaceObservation(21);session.Tick(.2,seen);
    Check(session.Racers[humans].Finished,"AI cannot finish during grace period");
    session.Tick(session.Remaining-.01,seen);Check(session.Phase==RacePhase.FinishWindow,"results early");
    session.Tick(.01,seen);Check(session.Phase==RacePhase.Results,"results late");
    Check(Math.Abs(session.Elapsed-last-3)<1e-8,"deadline clock mismatch");
    Check(!session.Racers[humans+1].Finished,"unfinished AI must remain DNF");
    for(int i=0;i<count;i++)seen[i]=new RaceObservation(0);
    session.Reset(seen);session.Begin();session.Tick(3,seen);session.Tick(30,seen);
    Check(session.Phase==RacePhase.Racing&&session.Elapsed==30,"reset retained deadline");
   }
   // Everyone finishing together still leaves the full three-second view of the finish.
   var all=new RaceSession(0,20,2,2);all.Begin();all.Tick(3,new RaceObservation(0),new RaceObservation(0));
   all.Tick(1,new RaceObservation(20),new RaceObservation(20));
   Check(all.Phase==RacePhase.FinishWindow&&all.Remaining==3,"all finish skips delay");
   all.Tick(3,new RaceObservation(20),new RaceObservation(20));Check(all.Phase==RacePhase.Results,"all finish results");
   // A large tick discovers the last human and AI crossings in chronological order.
   var coarse=new RaceSession(0,20,3,1);var crossing=new[]{new RaceObservation(30),new RaceObservation(21),new RaceObservation(20.5f)};
   coarse.Begin();coarse.Tick(3,new RaceObservation[3]);coarse.Tick(10,crossing);
   Check(coarse.Phase==RacePhase.Results,"coarse tick missed deadline");
   Check(coarse.Racers[1].Finished&&!coarse.Racers[2].Finished,"AI finish outside deadline accepted");
   // Invalid observations and recovery revisions cannot supply the last human finish.
   var invalid=new RaceSession(0,20,1,1);invalid.Begin();invalid.Tick(3,new[]{new RaceObservation(0)});
   invalid.Tick(1,new[]{new RaceObservation(21,1)});invalid.Tick(20,new[]{new RaceObservation(21,1)});
   Check(invalid.Phase==RacePhase.Racing&&!invalid.Racers[0].Finished,"recovery starts timer");
   Debug.Log("RACE_SESSION_CHECKS_OK assertions="+assertions+" humans=1..4 entrants=8/16/32/64 pause reset AI-first grace DNF all-finished coarse-tick recovery");
  }
 }
}
