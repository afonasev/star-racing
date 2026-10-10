using System;
using UnityEngine;
namespace StarRacingPrototype {
 public static class NativeRunwayChecks {
  static void Check(bool ok,string reason){if(!ok)throw new Exception("Native runway: "+reason);}
  public static void Run(){int cases=0;
   foreach(uint seed in new uint[]{77,2273265884})foreach(string theme in new[]{"cloud-city","space-station"})foreach(string mode in new[]{"normal","full","none"}) {
    var definition=Procedural.Generator.Generate(seed,mode,theme,true,true);
    Check(definition.version==10&&definition.jumps.Length==4,"revision/jump count");
    Check(Procedural.Generator.AddedRunwayLength(definition)==880,"approved additional course length");
    Check(Procedural.Validator.Validate(definition).Length==0,"generated course invalid");
    Check(definition.hash==Procedural.Generator.Generate(seed,mode,theme,true,true).hash,"nondeterministic generation");
    foreach(var jump in definition.jumps){
     var run=Procedural.Generator.Runs(definition.samples,"jump-straight").Find(x=>jump.launchIndex>=x.start&&jump.launchIndex<x.start+x.length);
     Check(run.length*5==480,"straight not extended");Check(jump.launchIndex-run.start==18&&jump.gapEndIndex-jump.launchIndex==5,"ramp/gap moved within segment");
     Check(jump.landingEndIndex-jump.launchIndex==19,"AI tactical region silently expanded");
     Check(jump.ballisticLandingEndIndex-jump.launchIndex==68&&(run.start+run.length-jump.ballisticLandingEndIndex)*5>=50,"flight envelope lacks turn margin");
    }
    // Corrupt the flight budget and ensure validation rejects it without weakening duration/geometry contracts.
    var first=definition.jumps[0];int saved=first.ballisticLandingEndIndex;first.ballisticLandingEndIndex+=11;Check(Procedural.Validator.Validate(definition,true).Length>0,"unsafe flight envelope accepted");first.ballisticLandingEndIndex=saved;
    var firstRun=Procedural.Generator.Runs(definition.samples,"jump-straight")[0];var sample=definition.samples[firstRun.start];string kind=sample.kind;sample.kind="straight";Check(Procedural.Validator.Validate(definition,true).Length>0,"unapproved runway length accepted");sample.kind=kind;
    double duration=definition.estimatedDuration;definition.estimatedDuration+=100;Check(Procedural.Validator.Validate(definition,true).Length>0,"composition duration guard weakened");definition.estimatedDuration=duration;
    cases++;Debug.Log($"NATIVE_RUNWAY_CASE_OK requested={seed} actual={definition.seed} theme={theme} rails={mode} revision={definition.version} hash={definition.hash} estimatedDuration={definition.estimatedDuration:F2} addedRunway=880m");
   }
   Debug.Log("NATIVE_RUNWAY_CHECKS_OK cases="+cases+" tacticalBoundaryUnchanged source8Comparison=separateEvidence");
  }
 }
}
