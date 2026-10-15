using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
 public static class GraphSupportCpuChecks {
  static int assertions;
  static bool Reference(Bounds bounds,Ray ray,float distance){
   var lo=bounds.min;var hi=bounds.max;var origin=ray.origin;var direction=ray.direction;
   double magnitude=Math.Max(Math.Max(Math.Abs(lo.x),Math.Abs(hi.x)),Math.Max(Math.Max(Math.Abs(lo.y),Math.Abs(hi.y)),Math.Max(Math.Abs(lo.z),Math.Abs(hi.z))));
   double padding=.001+magnitude*1e-6,enter=0,leave=distance;
   for(int axis=0;axis<3;axis++){
    double a=lo[axis]-padding,b=hi[axis]+padding,o=origin[axis],d=direction[axis];
    if(d==0){if(o<a||o>b)return false;continue;}
    double x=(a-o)/d,y=(b-o)/d;if(x>y){var swap=x;x=y;y=swap;}
    enter=Math.Max(enter,x);leave=Math.Min(leave,y);if(enter>leave)return false;
   }return true;
  }
  public static void RunInventory(){
   if(!Environment.GetCommandLineArgs().Contains("-noaudio"))throw new Exception("Support inventory QA requires -noaudio");
   string output=Environment.GetEnvironmentVariable("STAR_RACING_SUPPORT_CPU_QA_DIR");Directory.CreateDirectory(output);
   Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_RAYCAST_QA_DIR",Path.Combine(output,"raycast"));GraphRaycastOptimizationChecks.Run();
   Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_VEHICLE_QA_DIR",Path.Combine(output,"admission"));GraphVehicleRouteChecks.Run();
   File.WriteAllText(Path.Combine(output,"result.txt"),"GRAPH_SUPPORT_INVENTORY_AFFECTED_OK exactNativeRaycast orderedGroups admission legacyNeighbors=passed\n");
  }
  public static void RunOrigins(){RunInventory();RunOriginParity();}
  public static void RunPreparation(){
   RunOrigins();string output=Environment.GetEnvironmentVariable("STAR_RACING_SUPPORT_CPU_QA_DIR");
   Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_RACE_QA_DIR",Path.Combine(output,"race"));GraphRaceChecks.RunCourseContracts();
   Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_AI_QA_DIR",Path.Combine(output,"ai"));GraphAiChecks.Run();
   File.WriteAllText(Path.Combine(output,"result.txt"),"GRAPH_SUPPORT_PREPARATION_AFFECTED_OK exactNativeRaycast admission courseContracts graphAI exactLegacyCommands=5400 originParity=passed\n");
  }
  public static void RunOriginParity(){
   if(!Environment.GetCommandLineArgs().Contains("-noaudio"))throw new Exception("Origin QA requires -noaudio");
   string output=Environment.GetEnvironmentVariable("STAR_RACING_SUPPORT_CPU_QA_DIR");Directory.CreateDirectory(output);int checks=0;
   void Assert(bool ok,string why){checks++;if(!ok)throw new Exception("Support origin parity: "+why);}
   var probeType=typeof(GraphRoadSurface).Assembly.GetType("StarRacingPrototype.GraphRuntimeProfile+SupportProbe");
   var tagged=typeof(GraphRoadSurface).GetMethod("RaycastWithOrigin",BindingFlags.Instance|BindingFlags.NonPublic);
   var routeTagged=typeof(GraphVehicleRoute).GetMethod("RaycastWithOrigin",BindingFlags.Instance|BindingFlags.NonPublic);
   var begin=typeof(GraphRoadSurface).GetMethod("BeginQueryBatch",BindingFlags.Instance|BindingFlags.NonPublic);
   var epoch=typeof(GraphRoadSurface).GetField("queryEpoch",BindingFlags.Instance|BindingFlags.NonPublic);
   var depth=typeof(GraphRoadSurface).GetField("queryDepth",BindingFlags.Instance|BindingFlags.NonPublic);
   void Hit(RaycastHit a,RaycastHit b){Assert(a.distance.Equals(b.distance)&&a.point.Equals(b.point)&&a.normal.Equals(b.normal)&&a.barycentricCoordinate.Equals(b.barycentricCoordinate)&&a.triangleIndex==b.triangleIndex&&a.collider==b.collider,"all native hit fields unchanged");}
   var root=new GameObject("Origin diagnostic contracts");try{
    var graph=IndependentGraph.Create(new DVec(),new DVec(0,0,1),new DVec(0,1,0),new DVec(-1,0,0));
    var surface=IndependentTrackMesh.Build(root.transform,graph,91,null,null,(_,__)=>{});Physics.SyncTransforms();
    var f=graph.Road(0).Evaluate(graph.Road(0).Length*.5);var cursor=graph.Cursor(0,f.distance);
    var pose=new Pose((f.position+f.normal*VehicleGeometry.AdheredBodyHeight).Float,Quaternion.LookRotation(f.tangent.Float,f.normal.Float));
    Assert(GraphVehicleRoute.TryCreate(surface,cursor,pose,1,0,out var route),"actual route bound");
    var ray=new Ray((f.position+f.normal*1.5).Float,-f.normal.Float);
    foreach(bool scoped in new[]{false,true}){
     var batch=scoped?(IDisposable)begin.Invoke(surface,null):null;try{
      foreach(var name in Enum.GetNames(probeType).Where(n=>n!="Count")){
       var probe=Enum.Parse(probeType,name);var beforeEpoch=epoch.GetValue(surface);var beforeDepth=depth.GetValue(surface);
       bool expected=surface.Raycast(ray,4,new[]{0},graph.Identity,91,out var expectedHit,out var expectedFace);
       object[] args={ray,4f,new[]{0},graph.Identity,91,default(RaycastHit),default(RoadFace),probe};
       bool actual=(bool)tagged.Invoke(surface,args);Assert(expected&&actual,"surface tag preserves supported ray");Hit(expectedHit,(RaycastHit)args[5]);
       foreach(var field in typeof(RoadFace).GetFields(BindingFlags.Instance|BindingFlags.Public))Assert(Equals(field.GetValue(expectedFace),field.GetValue(args[6])),"all face fields unchanged");
       bool routeExpected=route.Raycast(ray.origin,ray.direction,4,out var routeHit);object[] routeArgs={ray.origin,ray.direction,4f,default(RaycastHit),probe};
       bool routeActual=(bool)routeTagged.Invoke(route,routeArgs);Assert(routeExpected&&routeActual,"route tag preserves supported ray");Hit(routeHit,(RaycastHit)routeArgs[3]);
       Assert(Equals(beforeEpoch,epoch.GetValue(surface))&&Equals(beforeDepth,depth.GetValue(surface)),"tag opens no batch/changes no epoch");
      }
     }finally{batch?.Dispose();}
    }
    if(!GraphRoadSurface.SupportCountersEnabled){
     var profile=typeof(GraphRoadSurface).Assembly.GetType("StarRacingPrototype.GraphRuntimeProfile");
     var counters=profile.GetField("SupportCounters",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
     foreach(var field in counters.GetType().GetFields())Assert(Convert.ToInt64(field.GetValue(counters))==0,"default disabled totals remain zero");
     var rows=(Array)profile.GetField("SupportProbes",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
     foreach(var row in rows)foreach(var field in row.GetType().GetFields())Assert(Convert.ToInt64(field.GetValue(row))==0,"default disabled origins remain zero");
    }
   }finally{UnityEngine.Object.DestroyImmediate(root);}
   File.WriteAllText(Path.Combine(output,"origins.txt"),$"GRAPH_SUPPORT_ORIGIN_EXACT_OK assertions={checks} enabled={GraphRoadSurface.SupportCountersEnabled} publicTaggedHitFace epochDepth scopedUnscoped=passed\n");
  }
  public static void Run(){
   if(!Environment.GetCommandLineArgs().Contains("-noaudio"))throw new Exception("Support CPU QA requires -noaudio");
   string output=Environment.GetEnvironmentVariable("STAR_RACING_SUPPORT_CPU_QA_DIR");Directory.CreateDirectory(output);assertions=0;
   var type=typeof(GraphRoadSurface).GetNestedType("BoundsSlab",BindingFlags.NonPublic);
   var ctor=type.GetConstructor(new[]{typeof(Bounds)});var method=type.GetMethod("Intersects");var rng=new System.Random(761240);
   for(int i=0;i<10000;i++){
    float Next()=> (float)(rng.NextDouble()*20000-10000);
    var bounds=new Bounds(new Vector3(Next(),Next(),Next()),new Vector3(Math.Abs(Next()),Math.Abs(Next()),Math.Abs(Next())));
    var instance=ctor.Invoke(new object[]{bounds});
    foreach(var origin in new[]{bounds.min,bounds.max,bounds.center,new Vector3(Next(),Next(),Next())})
    foreach(var direction in new[]{Vector3.right,-Vector3.right,Vector3.up,Vector3.zero,new Vector3(Next(),Next(),Next())})
    foreach(float length in new[]{0f,4f,10000f,float.PositiveInfinity,float.NaN}){
     var ray=new Ray(origin,direction);bool expected=Reference(bounds,ray,length);bool actual=(bool)method.Invoke(instance,new object[]{ray,length});assertions++;
     if(expected!=actual)throw new Exception("Frozen padded bounds parity index="+i+" length="+length);
    }
   }
   File.WriteAllText(Path.Combine(output,"slab.txt"),$"GRAPH_BOUNDS_SLAB_EXACT_OK assertions={assertions} frozenOriginalArithmetic=passed seed=761240\n");
   Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_RAYCAST_QA_DIR",Path.Combine(output,"raycast"));GraphRaycastOptimizationChecks.Run();
   Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_VEHICLE_QA_DIR",Path.Combine(output,"admission"));GraphVehicleRouteChecks.Run();
   File.WriteAllText(Path.Combine(output,"result.txt"),$"GRAPH_SUPPORT_CPU_AFFECTED_OK slabAssertions={assertions} exactNativeRaycast admission legacyNeighbors=passed\n");Debug.Log("GRAPH_SUPPORT_CPU_AFFECTED_OK assertions="+assertions);
  }
 }
}
