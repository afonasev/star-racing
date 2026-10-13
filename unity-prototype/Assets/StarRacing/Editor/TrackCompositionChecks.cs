using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
 public static class TrackCompositionChecks {
  public static string Output => Environment.GetEnvironmentVariable("STAR_RACING_COMPOSITION_QA_DIR");
  static int assertions;
  public static int Assertions => assertions;
  public static void Check(bool ok,string why){assertions++;if(!ok)throw new Exception("Track composition: "+why);}
  [Serializable] public sealed class Corpus {public List<Definition> tracks=new List<Definition>();}
  public static uint[] CorpusSeeds() {
   var seeds=new List<uint>();
   foreach(string family in Generator.DrivingFamilies) {
    for(uint seed=1;seeds.Count(x=>Generator.FamilyFor(x,"cloud-city")==family)<3;seed++)
     if(Generator.FamilyFor(seed,"cloud-city")==family)seeds.Add(seed);
   }
   return seeds.ToArray();
  }
  public static void Run() {
   if(string.IsNullOrEmpty(Output))throw new Exception("STAR_RACING_COMPOSITION_QA_DIR required");
   Directory.CreateDirectory(Output);assertions=0;var corpus=new Corpus();
   foreach(uint seed in CorpusSeeds()) {
    bool jumps=seed%2==0;
    var d=Generator.Generate(seed,"normal","cloud-city",jumps,true);
    Debug.Log($"COMPOSITION_CANDIDATE requested={seed} actual={d.seed} family={d.family} hash={d.hash}");
    Check(d.version==11 && d.family==Generator.FamilyFor(seed,"cloud-city"),"identity/family");
    Check(Validator.Validate(d).Length==0,"safety "+string.Join(",",Validator.Validate(d)));
    Check(JsonUtility.ToJson(d)==JsonUtility.ToJson(Generator.Generate(seed,"normal","cloud-city",jumps,true)),"exact definition replay");
    var route=new TrackRoute(d);
    foreach(var s in d.samples.Where(s=>s.index%13==0)) {
     var f=route.Evaluate(30+(float)s.distance);
     Check(Mathf.Abs(f.halfWidth-(float)s.halfWidth)<.001f,"route width disagrees");
     var projected=route.Project(f.position,f.distance,20);
     Check(Mathf.Abs(projected.distance-f.distance)<.05f,"local progress projection");
     foreach(var span in Layout.Paved(d,s))Check(route.TryRoadBounds(f.distance,-(float)span.offset,out var left,out var right) && right-left>2.9f,"AI paved corridor");
    }
    var main=d.composition.Single(x=>x.role=="main");
    Check(main.startIndex>0&&main.endIndex<d.samples.Length-1,"main surrounded by recovery");
    if(d.family=="technical") {
     var linked=d.patterns.Single(p=>p.kind=="connected-three");int changes=0,previous=0;
     for(int i=linked.startIndex+1;i<=linked.endIndex;i++) {
      double yaw=DVec.Dot(d.samples[i].normal,DVec.Cross(d.samples[i-1].tangent,d.samples[i].tangent));
      if(Math.Abs(yaw)<.0001)continue;int sign=Math.Sign(yaw);if(previous!=0&&previous!=sign)changes++;previous=sign;
     }
     Check(changes==2,"three connected corners");
    }
    foreach(var pattern in d.patterns.Where(p=>p.kind=="tightening-radius"||p.kind=="opening-radius")) {
     double early=0,late=0;int count=pattern.endIndex-pattern.startIndex;
     for(int k=1;k<=count;k++) {
      var current=d.samples[pattern.startIndex+k];var prior=d.samples[pattern.startIndex+k-1];
      double angle=Math.Acos(Math.Max(-1,Math.Min(1,DVec.Dot(current.tangent,prior.tangent))));
      if(k>=count*.2&&k<count*.45)early+=angle;
      if(k>=count*.55&&k<count*.8)late+=angle;
     }
     Check(pattern.kind=="tightening-radius"?late>early*1.25:early>late*1.25,"radius profile collapsed to constant bend");
    }
    var width=d.samples[main.startIndex+5];double saved=width.halfWidth;width.halfWidth+=2;
    Check(Validator.Validate(d,true).Contains("composition-width-slope"),"abrupt narrowing accepted");width.halfWidth=saved;
    string role=width.compositionRole;width.compositionRole="recovery";
    Check(Validator.Validate(d,true).Contains("composition-metadata"),"geometry/metadata divergence accepted");width.compositionRole=role;
    corpus.tracks.Add(d);
   }
   foreach(string family in Generator.DrivingFamilies) {
    uint seed=CorpusSeeds().First(x=>Generator.FamilyFor(x,"cloud-city")==family);
    foreach(bool jumps in new[]{false,true}) {
     var fallback=Generator.GenerateFallback(seed,"normal","cloud-city",jumps,true);
     Check(fallback.usedFallback&&fallback.family==family,"fallback erased family");
     Check(Validator.Validate(fallback).Length==0,"fallback safety");
    }
    foreach(string theme in new[]{"cloud-city","space-station"})foreach(string mode in new[]{"normal","full","none"}) {
     var d=Generator.Generate(seed,mode,theme,true,true);
     Check(d.family==Generator.FamilyFor(seed,theme)&&Validator.Validate(d).Length==0,"theme/rails/jump contract");
    }
   }
   var legacy=Generator.Generate(77,"normal","cloud-city",true,true,10);
   Check(legacy.version==10&&legacy.family==null&&Validator.Validate(legacy).Length==0,"explicit revision10 replay");
   NativeRunwayChecks.Run();CheckpointAdmissionChecks.Run();
   File.WriteAllText(Path.Combine(Output,"corpus.json"),JsonUtility.ToJson(corpus,true));
   File.WriteAllText(Path.Combine(Output,"result.txt"),$"COMPOSITION_CHECKS_OK assertions={assertions} corpus={corpus.tracks.Count} revision=11\n");
   Debug.Log($"COMPOSITION_CHECKS_OK assertions={assertions} corpus={corpus.tracks.Count}");
  }
 }
}
