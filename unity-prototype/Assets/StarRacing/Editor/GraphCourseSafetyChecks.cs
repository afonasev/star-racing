using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
 public static class GraphCourseSafetyChecks {
  static int assertions;static void Check(bool ok,string why){assertions++;if(!ok)throw new Exception("Graph safety: "+why);}
  public static void Run() {
   string output=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_SAFETY_QA_DIR");if(string.IsNullOrEmpty(output))throw new Exception("Evidence required");Directory.CreateDirectory(output);assertions=0;
   SegmentCases();var corpus=new List<string>();
   foreach(string theme in new[]{"cloud-city","space-station"})foreach(string mode in new[]{"normal","none","full"})foreach(bool jumps in new[]{false,true})foreach(uint seed in new[]{1u,uint.MaxValue}) {
    var settings=new GraphCourseSettings(theme,mode,jumps);var a=GraphCourseGenerator.Generate(seed,settings);var b=GraphCourseGenerator.Generate(seed,settings);var f=GraphCourseGenerator.GenerateFallback(seed,settings);
    Check(a.Identity==b.Identity&&a.graph.Identity==b.graph.Identity,"exact normal generation replay");
    Check(a.requestedSeed==seed&&a.graph.guardrailMode==mode&&a.graph.jumps.Count==(jumps?4:0),"exact requested settings");
    Check(GraphCourseSafety.Validate(a.graph).Length==0&&GraphCourseSafety.Validate(f.graph).Length==0,"both complete geometries certified "+string.Join(",",GraphCourseSafety.Validate(a.graph).Take(3)));
    Check(f.usedFallback&&f.Identity==GraphCourseGenerator.GenerateFallback(seed,settings).Identity&&f.graph.roads.Select(r=>r.id).SequenceEqual(a.graph.roads.Select(r=>r.id)),"fallback exact family/topology replay");
    Check(a.Identity!=f.Identity&&GraphCourseDefinition.Family=="path-choice","fallback identity distinct andfamily fixed");
    corpus.Add($"{seed}|{theme}|{mode}|{jumps}|{a.actualSeed}|{a.Identity}|{a.graph.Identity}|{f.actualSeed}|{f.Identity}|{f.graph.Identity}");
   }
   var graph=GraphCommonCatalog.Create(new DVec(),new DVec(0,0,1),new DVec(0,1,0),new DVec(-1,0,0));
   Check(GraphCommonCatalogChecks.Geometry().Identity=="27c8ede2c7004a6ed56fa496310006ea6cbb767fa851252cc18007778a75b974","previous default catalog bytes preserved");
   var rotated=GraphCommonCatalogChecks.Geometry("full",transformed:true);Check(GraphCourseSafety.Validate(rotated).Length==0,"whole transformed catalog clearance");
   Check(GraphCourseSafety.Validate(IndependentGraph.Create(new DVec(),new DVec(0,0,1),new DVec(0,1,0),new DVec(-1,0,0))).Length==0,"sparse physical segments included, topology joins allowed");
   var t=graph.Road(1);var speed=graph.Road(2);
   var crossing=Replace(graph,2,speed.samples.Select(f=>{double u=f.distance/speed.Length,v=Math.Sin(Math.PI*u);return f.position*(1-v*v)+t.Evaluate(u*t.Length).position*(v*v);}).ToArray());
   var errors=GraphCourseSafety.Validate(crossing);Check(errors.Any(e=>e.StartsWith("clearance-")),"crossing physical alternatives rejected: "+string.Join(",",errors.Take(4)));
   var exit=graph.Road(5);var target=graph.Road(0).Evaluate(300).position+new DVec(0,24,0);
   var intrusion=Replace(graph,5,exit.samples.Select(f=>{double u=Math.Max(0,1-Math.Abs(f.distance-1800)/550);u=u*u*(3-2*u);double v=Math.Max(0,Math.Min(1,(f.distance-1250)/1100));double sideways=1600*32*(v-.5)*v*v*(1-v)*(1-v);
    return f.position*(1-u)+target*u+new DVec(-sideways,0,0);}).ToArray());
   errors=GraphCourseSafety.Validate(intrusion);Check(errors.Any(e=>e.EndsWith("-flight")),"nonlocal common vertical intrusion into flight envelope rejected: "+string.Join(",",errors.Take(4)));
   // No hidden factory acceptance bypass: real unsafe geometry survives structural
   // import and must exhaust normal retries, then pass the same full fallback gate.
   var old=graph.Road(1);var frames=old.samples.Select(f=>new GraphFrame(f.distance,f.position,f.tangent,f.normal.Rotate(f.tangent,.1),f.right.Rotate(f.tangent,.1),f.halfWidth,f.railLeft,f.railRight));
   var unsafeGraph=IndependentGraph.FromGeometry(graph.roads.Select(r=>r.id==1?new GraphRoad(r.id,r.kind,r.fromPort,r.toPort,r.branch,frames.Select((f,i)=>i==0||i==old.samples.Count-1?old.samples[i]:f),r.progressStart,r.progressEnd):r),graph.junctions,graph.guardrailMode,false,graph.jumps);
   var select=typeof(GraphCourseGenerator).GetMethod("Select",BindingFlags.Static|BindingFlags.NonPublic);int calls=0;var config=new GraphCourseSettings();
   Func<uint,IndependentGraph> factory=seed=>++calls<=32?unsafeGraph:GraphCourseGenerator.Candidate(seed,config);
   var result=(GraphCourseDefinition)select.Invoke(null,new object[]{1u,config,false,factory});Check(calls==33&&result.usedFallback&&result.actualSeed==0x51a7faceu&&result.attempt==0,"actual normal rejection traverses bounded fallback phase");
   calls=0;factory=seed=>++calls==1?unsafeGraph:GraphCourseGenerator.Candidate(seed,config);
   result=(GraphCourseDefinition)select.Invoke(null,new object[]{uint.MaxValue,config,false,factory});
   Check(calls==2&&!result.usedFallback&&result.attempt==1&&result.actualSeed==0x9e3779b8u&&result.requestedSeed==uint.MaxValue,"normal retry preserves unchecked overflow seed identity");
   bool invalid=false;try{new GraphCourseSettings("unsupported");}catch(ArgumentException){invalid=true;}Check(invalid,"unsupported settings failclosed");
   Check(GraphCourseSafety.ShellDepth==RoadVolumeMesh.Thickness&&GraphCourseSafety.FlightLow==-.6&&GraphCourseSafety.FlightHigh==20,"existing shell andflight envelope dimensions retained");
   calls=0;bool failed=false;try{select.Invoke(null,new object[]{1u,config,false,(Func<uint,IndependentGraph>)(seed=>{calls++;return unsafeGraph;})});}catch(TargetInvocationException e){failed=e.InnerException is InvalidOperationException;}
   Check(failed&&calls==416,"all unsafe candidates failclosed without family/topology replacement");
   File.WriteAllLines(Path.Combine(output,"corpus.txt"),corpus);File.WriteAllText(Path.Combine(output,"result.txt"),$"GRAPH_COURSE_SAFETY_OK assertions={assertions} settingsCases={corpus.Count} continuousClearance=1 physicalAlternatives=2 flightEnvelope=1 native12=inactive wholeVehicle=pending\n");Debug.Log("GRAPH_COURSE_SAFETY_OK assertions="+assertions);
  }
  static IndependentGraph Replace(IndependentGraph graph,int id,DVec[] positions) {
   var road=graph.Road(id);var frames=new List<GraphFrame>();double distance=0;
   for(int i=0;i<positions.Length;i++) {
    if(i>0)distance+=Math.Sqrt((positions[i]-positions[i-1]).Squared);
    var old=road.samples[i];var tangent=i==0||i==positions.Length-1?old.tangent:(positions[i+1]-positions[i-1]).Unit();
    var normal=(old.normal-tangent*DVec.Dot(old.normal,tangent)).Unit();var right=DVec.Cross(tangent,normal).Unit();
    if(i==0||i==positions.Length-1){normal=old.normal;right=old.right;}
    frames.Add(new GraphFrame(distance,positions[i],tangent,normal,right,old.halfWidth,old.railLeft,old.railRight));
   }
   return IndependentGraph.FromGeometry(graph.roads.Select(r=>r.id==id?new GraphRoad(r.id,r.kind,r.fromPort,r.toPort,r.branch,frames,r.progressStart,r.progressEnd):r),graph.junctions,graph.guardrailMode,false,graph.jumps);
  }
  static void SegmentCases() {
   DVec P(double x,double y,double z)=>new DVec(x,y,z);
   Check(GraphCourseSafety.SegmentSquared(P(-10,0,0),P(10,0,0),P(0,0,-10),P(0,0,10))==0,"continuous crossing between endpoints");
   Check(GraphCourseSafety.SegmentSquared(P(0,0,0),P(10,0,0),P(2,3,0),P(8,3,0))==9,"parallel overlap distance");
   Check(GraphCourseSafety.SegmentSquared(P(0,0,0),P(1,0,0),P(3,4,0),P(5,4,0))==20,"clamped nearest endpoints");
   Check(GraphCourseSafety.SegmentSquared(P(2,3,0),P(2,3,0),P(0,0,0),P(10,0,0))==9,"degenerate point tosegment");
   Check(GraphCourseSafety.SegmentSquared(P(1,2,3),P(1,2,3),P(4,6,3),P(4,6,3))==25,"degenerate pair");
  }
  public static void RunAffected() {
   string output=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_SAFETY_QA_DIR");Run();Environment.SetEnvironmentVariable("STAR_RACING_COMPOSITION_QA_DIR",Path.Combine(output,"legacy"));TrackCompositionChecks.Run();
  }
 }
}
