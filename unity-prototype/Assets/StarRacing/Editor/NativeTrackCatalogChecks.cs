using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
 public static class NativeTrackCatalogChecks {
  static int assertions;static void Check(bool ok,string why){assertions++;if(!ok)throw new Exception("Native12 catalog: "+why);}
  static void Reject(Action body){bool rejected=false;try{body();}catch(ArgumentException){rejected=true;}Check(rejected,"invalid input rejected");}
  public static void RunNeighbors(){
   string output=Environment.GetEnvironmentVariable("STAR_RACING_NATIVE_CATALOG_QA_DIR");Directory.CreateDirectory(output);
   Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_SAFETY_QA_DIR",Path.Combine(output,"safety"));GraphCourseSafetyChecks.RunAffected();
   Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_IDENTITY_QA_DIR",Path.Combine(output,"identity"));GraphCourseIdentityChecks.Run();
   File.WriteAllText(Path.Combine(output,"result.txt"),"NATIVE12_AFFECTED_NEIGHBOURS_OK previous safety fallback exhaustion legacy composition10/11 persistent gate identity race authority\n");
  }
  public static void Run(){
   string output=Environment.GetEnvironmentVariable("STAR_RACING_NATIVE_CATALOG_QA_DIR");Directory.CreateDirectory(output);assertions=0;
   var n=new DVec(0,1,0);var t=new DVec(0,0,1);var right=DVec.Cross(t,n).Unit();var identities=new HashSet<string>();
   foreach(bool jumps in new[]{false,true})foreach(string mode in new[]{"normal","none","full"})for(int profile=1;profile<=4;profile++){
    var graph=GraphNativeCatalog.Create(new DVec(0,120,0),t,n,right,mode,jumps,profile);var errors=GraphCourseSafety.Validate(graph);
    File.WriteAllText(Path.Combine(output,$"profile-{profile}-{mode}-{jumps}.txt"),"length="+graph.CommonLength+" errors="+string.Join(",",errors)+"\n");
    Check(errors.Length==0,"whole safety profile="+profile+" "+string.Join(",",errors));Check(graph.Validate().Length==0,"structural topology");
    var original=IndependentGraph.Create(new DVec(0,120,0),t,n,right,mode,180,jumps?2550:2110);
    foreach(int id in new[]{1,2}){Check(graph.Road(id).Length==original.Road(id).Length,"approved pair exact physical length");Check(graph.Road(id).samples.SequenceEqual(original.Road(id).samples),"approved pair full geometry remains unchanged");}
    Check(graph.jumps.Count==(jumps?4:0)&&graph.jumps.All(j=>j.Valid(graph)),"four480runways/50reserve/offsets retained");
    for(int slot=0;slot<64;slot++){double at=GraphNativeCatalog.GridStart-(slot/2)*10;var frame=graph.Road(0).Evaluate(at);Check(at>=0&&at<=GraphNativeCatalog.GridStart&&DVec.Dot(frame.normal,n)>.999,"64grid supported before first gate/first runway");}
    double duration=(graph.CommonLength-GraphNativeCatalog.GridStart-160-(jumps?880:0))/32.5;Check(duration>=150&&duration<=210,"native composition150..210 duration budget");
    if(mode=="normal"&&!jumps)identities.Add(graph.Identity);
   }
   Check(identities.Count==4,"common phrase variants differ beyond rigid yaw");
   foreach(string theme in new[]{"cloud-city","space-station"}){
    var counts=NativeTrackGenerator.Families.ToDictionary(f=>f,f=>0);for(uint seed=0;seed<256;seed++)counts[NativeTrackGenerator.FamilyFor(seed,theme)]++;
    foreach(var c in counts)Check(c.Value>0,"all four families distributed");File.WriteAllText(Path.Combine(output,theme+"-distribution.txt"),string.Join("\n",counts.Select(c=>c.Key+":"+c.Value))+"\n");
    foreach(string family in NativeTrackGenerator.Families){uint requested=Enumerable.Range(0,256).Select(i=>(uint)i).First(seed=>NativeTrackGenerator.FamilyFor(seed,theme)==family);
     foreach(bool jumps in new[]{false,true})foreach(bool fallback in new[]{false,true}){
      var settings=new GraphCourseSettings(theme,"normal",jumps);var course=NativeTrackGenerator.Generate(requested,settings,12,fallback);var repeat=NativeTrackGenerator.Generate(requested,settings,12,fallback);
      Check(course.Family==family&&course.GeneratorRevision==12&&course.RequestedSeed==requested&&course.Settings==settings,"requested metadata/family preserved");Check(course.GeometryIdentity==repeat.GeometryIdentity&&course.ActualSeed==repeat.ActualSeed&&course.UsedFallback==repeat.UsedFallback,"exact full-content deterministic replay");Check(!fallback||course.UsedFallback,"explicit fallback remains same family");
      if(course.IsIndependent){Check(GraphCourseSafety.Validate(course.Graph).Length==0,"selected graph safety");Check(course.Independent.compositionProfile==100+GraphNativeCatalog.ProfileFor(course.ActualSeed),"actual seed composition identity");var root=new GameObject("Native gate content observer");try{var track=root.AddComponent<TrackBuilder>();var physical=new GameObject("Dedicated native physical inventory");physical.transform.SetParent(root.transform,false);var surface=IndependentTrackMesh.BuildCommonCatalog(physical.transform,course.Graph,81,null,null,(_,__)=>{});typeof(TrackBuilder).GetProperty("GraphSurface").SetValue(track,surface);string replay=course.ReplayIdentity(track);Check(replay==repeat.ReplayIdentity(track)&&replay.Length==64,"actual built gates in full replay hash");}finally{UnityEngine.Object.DestroyImmediate(root);}}
      else{Check(Validator.Validate(course.Single,true).Length==0,"old family composition/runway safety");var candidate=Generator.Candidate(course.ActualSeed,"normal",theme,jumps,true,11,family);candidate.version=12;candidate.hash=course.Single.hash;candidate.requestedSeed=course.Single.requestedSeed;candidate.usedFallback=course.Single.usedFallback;string actual=JsonUtility.ToJson(course.Single),expected=JsonUtility.ToJson(candidate);if(actual!=expected){File.WriteAllText(Path.Combine(output,"unexpected-single.json"),actual);File.WriteAllText(Path.Combine(output,"expected-single.json"),expected);}Check(actual==expected,"old family exact geometry/layout at actual seed, no handling rewrite");}
     }
    }
    foreach(int revision in new[]{10,11})foreach(uint requested in new[]{7u,8u,77u})foreach(bool jumps in new[]{false,true}){var settings=new GraphCourseSettings(theme,"full",jumps);var old=Generator.Generate(requested,settings.rails,theme,jumps,true,revision);var adapted=NativeTrackGenerator.Generate(requested,settings,revision);Check(JsonUtility.ToJson(old)==JsonUtility.ToJson(adapted.Single),"explicit10/11 byte-exact whole definition");Check(adapted.GeneratorRevision==revision&&adapted.Graph.legacy,"old single-road adapter preserved");}
   }
   var singleCourse=NativeTrackGenerator.Generate(Enumerable.Range(0,256).Select(i=>(uint)i).First(seed=>NativeTrackGenerator.FamilyFor(seed,"cloud-city")=="speed"),new GraphCourseSettings(),12);
   var singleRoot=new GameObject("Single-route replay content");try{var track=singleRoot.AddComponent<TrackBuilder>();track.Route=new TrackRoute(singleCourse.Single);string before=singleCourse.ReplayIdentity(track);Check(before==singleCourse.ReplayIdentity(track)&&before.Length==64,"single full identity binds frozen content and actual route gates");singleCourse.Single.samples[20].halfWidth+=.01;Reject(()=>singleCourse.ReplayIdentity(track));}finally{UnityEngine.Object.DestroyImmediate(singleRoot);}
   Reject(()=>NativeTrackGenerator.Generate(77,new GraphCourseSettings(),13));Reject(()=>GraphNativeCatalog.Create(new DVec(),t,n,right,"normal",true,0));
   File.WriteAllText(Path.Combine(output,"result.txt"),$"NATIVE12_CATALOG_DOMAIN_OK assertions={assertions} profiles=4 mode=isolated activeGenerator11 unchanged noPlayer nativeVehicleRuntimeGatePending\n");Debug.Log("NATIVE12_CATALOG_DOMAIN_OK assertions="+assertions);
  }
 }
}
