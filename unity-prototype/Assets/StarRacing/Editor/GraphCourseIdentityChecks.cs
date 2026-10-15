using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
 public static class GraphCourseIdentityChecks {
  static int assertions;static void Check(bool ok,string why){assertions++;if(!ok)throw new Exception("Graph replay identity: "+why);}
  static GraphRoadSurface Surface(IndependentGraph graph,int stamp) {
   var s=new GameObject("Content-only surface").AddComponent<GraphRoadSurface>();
   typeof(GraphRoadSurface).GetMethod("Initialize",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(s,new object[]{graph,stamp});return s;
  }
  public static void Run() {
   string output=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_IDENTITY_QA_DIR");if(string.IsNullOrEmpty(output))throw new Exception("Evidence required");Directory.CreateDirectory(output);assertions=0;var corpus=new System.Collections.Generic.List<string>();
   foreach(string theme in new[]{"cloud-city","space-station"})foreach(string mode in new[]{"normal","none","full"})foreach(bool jumps in new[]{false,true}) {
    var settings=new GraphCourseSettings(theme,mode,jumps);var definition=GraphCourseGenerator.Generate(1,settings);var a=Surface(definition.graph,71);var b=Surface(definition.graph,999);var culture=CultureInfo.CurrentCulture;
    try {
     float finish=(float)(definition.graph.CommonLength-10);var first=new GraphRaceCourse(a,0,finish);var second=new GraphRaceCourse(b,0,finish);
     Check(first.Identity==second.Identity,"different build stamps preserve frozen gate content");
     Check(first.ReplayIdentity(definition)==second.ReplayIdentity(definition),"full replay unchanged across physical builds");
     CultureInfo.CurrentCulture=new CultureInfo("fr-FR");Check(first.Identity==new GraphRaceCourse(a,0,finish).Identity,"binary canonical gate layout independent ofculture");
     Check(first.Identity!=new GraphRaceCourse(a,1,finish).Identity&&first.Identity!=new GraphRaceCourse(a,0,finish-1).Identity,"both race boundaries part ofcourse content");
     typeof(GraphRoadSurface).GetMethod("Initialize",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(a,new object[]{definition.graph,72});
     Check(first.Identity==new GraphRaceCourse(a,0,finish).Identity,"same physical object rebuild preserves persistent course content");
     uint otherRequested=unchecked((definition.actualSeed+360)^(theme=="space-station"?0x5a710a1eu:0u));
     var other=GraphCourseGenerator.Generate(otherRequested,settings);
     Check(other.graph.Identity==definition.graph.Identity&&other.Identity!=definition.Identity,"distinct requested seed withsame yaw geometry");
     Check(first.ReplayIdentity(other)!=first.ReplayIdentity(definition),"full replay retains metadata beyond rawgeometry");
     bool bad=false;try{first.ReplayIdentity(GraphCourseGenerator.Generate(2,settings));}catch(ArgumentException){bad=true;}Check(bad,"foreign graph cannotcompose replay identity");
     var fallback=GraphCourseGenerator.GenerateFallback(1,settings);var f=Surface(fallback.graph,81);
     try{var course=new GraphRaceCourse(f,0,(float)(fallback.graph.CommonLength-10));Check(course.ReplayIdentity(fallback)==new GraphRaceCourse(f,0,(float)(fallback.graph.CommonLength-10)).ReplayIdentity(fallback),"fallback exact fullreplay");corpus.Add($"{theme}|{mode}|{jumps}|{definition.Identity}|{first.Identity}|{first.ReplayIdentity(definition)}|{course.ReplayIdentity(fallback)}");}finally{UnityEngine.Object.DestroyImmediate(f.gameObject);}
    }finally{CultureInfo.CurrentCulture=culture;UnityEngine.Object.DestroyImmediate(a.gameObject);UnityEngine.Object.DestroyImmediate(b.gameObject);}
   }
   File.WriteAllLines(Path.Combine(output,"corpus.txt"),corpus);File.WriteAllText(Path.Combine(output,"result.txt"),$"GRAPH_REPLAY_IDENTITY_OK assertions={assertions} settings=12 gateSchema=2 buildStampExcluded=1 runtimeAuthority=separatePhysicalChecks\n");
   Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_RACE_QA_DIR",Path.Combine(output,"race"));GraphRaceChecks.RunCourseContracts();Debug.Log("GRAPH_REPLAY_IDENTITY_OK assertions="+assertions);
  }
 }
}
