using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using StarRacingPrototype.Procedural;
using UnityEditor.SceneManagement;
namespace StarRacingPrototype {
 public static class GraphChoiceCpuChecks {
  static int assertions;
  static void Check(bool ok,string why){assertions++;if(!ok)throw new Exception("Choice CPU exactness: "+why);}
  static readonly FieldInfo CursorField=typeof(GraphRaceObservation).GetField("Cursor");
  static GraphAiObservation Synthetic(RouteCursor cursor,float speed){object route=default(GraphRaceObservation);CursorField.SetValue(route,cursor);return new GraphAiObservation(new AiObservation{Id=0,Speed=speed},(GraphRaceObservation)route);}
  static int Index(int road)=>road==0?0:road==10||road==11?1:road==1||road==2?2:road==12||road==13?3:road==5?4:-1;
  static double Coordinate(IndependentGraph graph,RouteCursor cursor,int branch){
   var path=branch==1?new[]{0,10,1,12,5}:new[]{0,11,2,13,5};int index=Index(cursor.roadId);double result=0;
   for(int i=0;i<index;i++)result+=graph.Road(path[i]).Length;
   var own=graph.Road(cursor.roadId);return result+graph.Road(path[index]).PhysicalDistance(own.Progress(cursor.localPhysicalDistance));
  }
  // Frozen 5cf095d4 choice horizon. LocalSpeed/At remain the original public
  // implementation, independent of the optimized sequential sample traversal.
  static double Reference(GraphAiWorldSnapshot world,GraphAiObservation self,int branch,ReleaseBalance balance){
   var nav=world.Navigation;var graph=nav.Graph;float acceleration=balance["acceleration"]+balance["nitroAcceleration"],baseLimit=balance["baseSpeedKmh"]/3.6f;
   double total=0;float velocity=Mathf.Max(1,self.Motion.Speed);var start=nav.At(self.Route.Cursor,branch,0);
   double end=Coordinate(graph,graph.Cursor(branch==1?12:13,graph.Road(branch==1?12:13).Length),branch)-Coordinate(graph,start,branch);
   for(double ahead=0;ahead<end;ahead+=5){
    float ds=(float)Math.Min(5,end-ahead),limit=nav.LocalSpeed(start,branch,ahead);
    float next=Mathf.Min(limit,Mathf.Sqrt(velocity*velocity+2*acceleration*ds));total+=2*ds/Mathf.Max(2,velocity+next);velocity=next;
   }
   for(int i=0;i<world.Count;i++){
    var other=world[i];if(other.Motion.Id==self.Motion.Id||other.Motion.Finished||!other.Valid)continue;
    int actual=other.Route.Branch!=0?other.Route.Branch:other.Intent;if(actual!=branch)continue;
    if(nav.PhysicalGap(self,other,branch,out float gap)&&gap>0&&gap<end)total+=Math.Max(0,baseLimit-Math.Max(0,other.Motion.Speed))/Math.Max(1,baseLimit)*Math.Min(12,120/Math.Max(1,other.Motion.Speed));
   }
   return total;
  }
  static void Compare(GraphAiWorldSnapshot world,GraphAiObservation self,int branch,ReleaseBalance balance){
   double expected=Reference(world,self,branch,balance),actual=world.Navigation.ChoiceSeconds(world,self,branch);
   Check(BitConverter.DoubleToInt64Bits(expected)==BitConverter.DoubleToInt64Bits(actual),$"double bits road={self.Route.Cursor.roadId} at={self.Route.Cursor.localPhysicalDistance:R} branch={branch} speed={self.Motion.Speed:R} expected={expected:R} actual={actual:R}");
  }
  public static void Run(){
   if(!Environment.GetCommandLineArgs().Contains("-noaudio"))throw new Exception("Choice QA requires -noaudio");
   string output=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_CHOICE_QA_DIR");Directory.CreateDirectory(output);assertions=0;EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   var balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);
   foreach(bool transformed in new[]{false,true})foreach(string mode in new[]{"normal","none","full"}){
    var graph=GraphCommonCatalogChecks.Geometry(mode,transformed:transformed);var root=new GameObject("Domain navigation parity");
    try{
     var surface=root.AddComponent<GraphRoadSurface>();typeof(GraphRoadSurface).GetMethod("Initialize",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(surface,new object[]{graph,71});
     var world=new GraphAiWorldSnapshot(surface,1,balance);
     foreach(var road in graph.roads){
      var distances=new SortedSet<double>{0,1e-8,Math.Max(0,road.Length-1e-8),road.Length};
      for(int i=0;i<road.samples.Count;i+=17)foreach(double offset in new[]{-1e-8,0,1e-8})distances.Add(Math.Max(0,Math.Min(road.Length,road.samples[i].distance+offset)));
      foreach(var jump in graph.jumps.Where(j=>j.roadId==road.id))foreach(double at in new[]{jump.rampStart,jump.launch,jump.gapEnd,jump.tacticalLanding})foreach(double offset in new[]{-1e-8,0,1e-8})distances.Add(Math.Max(0,Math.Min(road.Length,at+offset)));
      foreach(double at in distances)foreach(float speed in new[]{-20f,0f,.03f,8f,20f,45f,83f,float.PositiveInfinity,float.NaN})foreach(int branch in new[]{1,2})Compare(world,Synthetic(graph.Cursor(road.id,at,road.id==5?1:0),speed),branch,balance);
     }
     var rng=new System.Random(7108);for(int i=0;i<512;i++){var road=graph.roads[rng.Next(graph.roads.Count)];Compare(world,Synthetic(graph.Cursor(road.id,rng.NextDouble()*road.Length,road.id==5?2:0),(float)(rng.NextDouble()*140-30)),1+i%2,balance);}
     foreach(var cursor in new[]{new RouteCursor(0,0,0,"foreign"),new RouteCursor(0,double.NaN,0,graph.Identity),new RouteCursor(999,0,0,graph.Identity)}){
      bool old=false,current=false;try{Reference(world,Synthetic(cursor,20),1,balance);}catch(ArgumentException){old=true;}try{world.Navigation.ChoiceSeconds(world,Synthetic(cursor,20),1);}catch(ArgumentException){current=true;}Check(old&&current,"invalid cursor still rejected");
     }
    }finally{UnityEngine.Object.DestroyImmediate(root);}
   }
   // Real supported observations exercise the unchanged dynamic traffic term.
   var physical=new GameObject("Physical choice traffic parity");try{
    var graph=IndependentGraph.Create(new Procedural.DVec(),new Procedural.DVec(0,0,1),new Procedural.DVec(0,1,0),new Procedural.DVec(-1,0,0));var surface=IndependentTrackMesh.Build(physical.transform,graph,81,null,null,(_,__)=>{});Physics.SyncTransforms();
    var seen=typeof(GraphAiChecks).GetMethod("Seen",BindingFlags.Static|BindingFlags.NonPublic);var world=new GraphAiWorldSnapshot(surface,9,balance);var cars=(GraphAiObservation[])typeof(GraphAiWorldSnapshot).GetField("cars",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(world);
    for(int round=0;round<16;round++){
     for(int i=0;i<cars.Length;i++)cars[i]=(GraphAiObservation)seen.Invoke(null,new object[]{surface,i,i==0?0:1+i%2,i==0?40d:50d+i*8,(float)((round+i)%5*9),0f,0});
     foreach(int branch in new[]{1,2})Compare(world,cars[0],branch,balance);
    }
   }finally{UnityEngine.Object.DestroyImmediate(physical);}
   File.WriteAllText(Path.Combine(output,"exact.txt"),$"GRAPH_CHOICE_EXACT_OK assertions={assertions} frozen5cf095d4 exactDoubleBits modes3 transforms2 boundarySamples jumps speedChanges traffic invalidCursor randomSeed7108\n");
   Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_AI_QA_DIR",Path.Combine(output,"ai"));GraphAiChecks.Run();
   File.WriteAllText(Path.Combine(output,"result.txt"),$"GRAPH_CHOICE_AFFECTED_OK assertions={assertions} graphAI legacy5400Commands183600Bytes\n");Debug.Log("GRAPH_CHOICE_AFFECTED_OK assertions="+assertions);
  }
 }
}
