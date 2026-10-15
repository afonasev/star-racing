using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
 public static class GraphAllocationOptimizationChecks {
  delegate GraphFrame Projection(GraphRoad road,DVec position,double from,double to,out double score);
  static readonly Projection Project=(Projection)Delegate.CreateDelegate(typeof(Projection),typeof(GraphRoad).GetMethod("Project",BindingFlags.Instance|BindingFlags.NonPublic));
  static int assertions;
  static void Check(bool ok,string why){assertions++;if(!ok)throw new Exception("Reusable projection: "+why);}
  static void Exact(double a,double b)=>Check(BitConverter.DoubleToInt64Bits(a)==BitConverter.DoubleToInt64Bits(b),"double bits");
  static void Vector(DVec a,DVec b){Exact(a.x,b.x);Exact(a.y,b.y);Exact(a.z,b.z);}
  // Frozen allocating algorithm. Candidate ordering/provenance and physical
  // deltas are compared independently of the new caller-owned list path.
  static GraphProjection[] Reference(IndependentGraph graph,RouteCursor previous,DVec position,double window){
   var current=graph.Road(previous.roadId);var result=new List<GraphProjection>();
   void Add(GraphRoad road,double from,double to,double anchor,double delta){
    var frame=Project(road,position,from,to,out double score);
    int history=road.id==IndependentGraph.Approach?0:road.branch!=0?road.branch:previous.incomingBranch;
    result.Add(new GraphProjection(graph.Cursor(road.id,frame.distance,history),frame,score,delta+frame.distance-anchor));
   }
   Add(current,previous.localPhysicalDistance-window,previous.localPhysicalDistance+window,previous.localPhysicalDistance,0);
   foreach(var other in graph.roads){
    if(other.id==current.id||(previous.incomingBranch!=0&&other.branch!=0&&other.branch!=previous.incomingBranch))continue;
    double remaining=current.Length-previous.localPhysicalDistance;
    if(current.toPort==other.fromPort&&remaining<=window)Add(other,0,window-remaining,0,remaining);
    if(current.fromPort==other.toPort&&previous.localPhysicalDistance<=window)Add(other,other.Length-(window-previous.localPhysicalDistance),other.Length,other.Length,-previous.localPhysicalDistance);
   }
   return result.OrderBy(p=>p.squaredDistance).ThenBy(p=>p.cursor.roadId).ToArray();
  }
  static void Equal(GraphProjection a,GraphProjection b){
   Check(a.cursor.roadId==b.cursor.roadId&&a.cursor.incomingBranch==b.cursor.incomingBranch&&a.cursor.graphIdentity==b.cursor.graphIdentity,"ordered cursor identity");
   Exact(a.cursor.localPhysicalDistance,b.cursor.localPhysicalDistance);Exact(a.squaredDistance,b.squaredDistance);Exact(a.physicalDelta,b.physicalDelta);
   Exact(a.frame.distance,b.frame.distance);Vector(a.frame.position,b.frame.position);Vector(a.frame.tangent,b.frame.tangent);Vector(a.frame.normal,b.frame.normal);Vector(a.frame.right,b.frame.right);Exact(a.frame.halfWidth,b.frame.halfWidth);
   Check(a.frame.railLeft==b.frame.railLeft&&a.frame.railRight==b.frame.railRight,"rails");
  }
  public static void RunOverlapContracts(){
   string output=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_ALLOCATION_QA_DIR");Directory.CreateDirectory(output);assertions=0;
   var graph=IndependentGraph.Create(new DVec(),new DVec(0,0,1),new DVec(0,1,0),new DVec(-1,0,0));
   var root=new GameObject("Overlap saturation road");var unrelated=new GameObject("Unrelated overlap inventory");
   try{
    var surface=IndependentTrackMesh.Build(root.transform,graph,99,null,null,(_,__)=>{});var f=graph.Road(0).Evaluate(50);
    var pose=new Pose(f.position.Float+f.normal.Float*VehicleGeometry.AdheredBodyHeight,Quaternion.LookRotation(f.tangent.Float,f.normal.Float));
    Physics.SyncTransforms();Check(GraphVehicleRoute.TryCreate(surface,graph.Cursor(0,50),pose,1,0,out var route),"initial supported route");
    Vector3 centre=pose.position+pose.rotation*new Vector3(0,(CheckpointAdmission.Bottom+CheckpointAdmission.Top)*.5f,0);
    for(int i=0;i<80;i++){var box=new GameObject("Ignored collider "+i);box.transform.SetParent(unrelated.transform);box.transform.position=centre;box.AddComponent<BoxCollider>().size=Vector3.one*.02f;}
    Physics.SyncTransforms();
    Check(Physics.OverlapBox(centre,new Vector3(CheckpointAdmission.HalfWidth,(CheckpointAdmission.Top-CheckpointAdmission.Bottom)*.5f,CheckpointAdmission.HalfLength),pose.rotation,~0,QueryTriggerInteraction.Ignore).Length>=80,"actual overlap exceeds initial and doubled buffer");
    Check(route.TryCaptureSafePose(pose,out _),"all unrelated colliders preserve original admission");
    var blocker=new GameObject("Track obstruction");blocker.transform.SetParent(root.transform);blocker.transform.position=centre;var collider=blocker.AddComponent<BoxCollider>();collider.size=Vector3.one*.1f;
    Physics.SyncTransforms();Check(!route.TryCaptureSafePose(pose,out _),"blocking track collider retained after saturation");
    collider.isTrigger=true;Check(route.TryCaptureSafePose(pose,out _),"trigger does not block original query");
    collider.isTrigger=false;Check(!route.TryCaptureSafePose(pose,out _),"live trigger mutation restores blocking");
    collider.enabled=false;Check(route.TryCaptureSafePose(pose,out _),"live disabled collider removed");
    UnityEngine.Object.DestroyImmediate(unrelated);unrelated=null;collider.enabled=true;Physics.SyncTransforms();Check(!route.TryCaptureSafePose(pose,out _),"smaller reused buffer retains obstruction");
    collider.enabled=false;Check(route.TryCaptureSafePose(pose,out _),"buffer does not retain stale hits");
    File.WriteAllText(Path.Combine(output,"overlap.txt"),$"GRAPH_OVERLAP_SATURATION_OK assertions={assertions} actualIgnoredColliders=80 blocking trigger disabled shrinkingInventory=passed\n");
   }finally{if(unrelated!=null)UnityEngine.Object.DestroyImmediate(unrelated);UnityEngine.Object.DestroyImmediate(root);}
  }
  public static void Run(){
   if(!Environment.GetCommandLineArgs().Contains("-noaudio"))throw new Exception("Affected QA requires -noaudio");
   string output=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_ALLOCATION_QA_DIR");Directory.CreateDirectory(output);assertions=0;
   RunOverlapContracts();assertions=0;
   var scratch=new List<GraphProjection>(8);
   foreach(bool transformed in new[]{false,true})foreach(string mode in new[]{"normal","none","full"}){
    var graph=GraphCommonCatalogChecks.Geometry(mode,transformed:transformed);
    foreach(var road in graph.roads)foreach(var frame in road.samples)foreach(double window in new[]{0d,.5,20,30,100})foreach(double lane in new[]{-8d,0,8}){
     var cursor=graph.Cursor(road.id,frame.distance,road.id==5?1:0);var point=frame.position+frame.right*lane+frame.normal*.259;
     var expected=Reference(graph,cursor,point,window);graph.ProjectCandidatesInto(cursor,point,window,scratch);
     Check(expected.Length==scratch.Count,"complete candidates, buffer clears between calls");
     for(int i=0;i<expected.Length;i++)Equal(expected[i],scratch[i]);
    }
    var prior=graph.ProjectCandidates(graph.Cursor(0,graph.Road(0).Length),graph.Road(0).samples.Last().position,30);var frozen=prior.ToArray();
    graph.ProjectCandidatesInto(graph.Cursor(1,10),graph.Road(1).Evaluate(10).position,0,scratch);
    Check(prior.Count==frozen.Length,"public snapshot independent of scratch");for(int i=0;i<frozen.Length;i++)Equal(frozen[i],prior[i]);
   }
   File.WriteAllText(Path.Combine(output,"candidates.txt"),$"GRAPH_CANDIDATES_EXACT_OK assertions={assertions} modes=3 transforms=2 windows=5 lanes=3 frozenAllocatingOracle=exactDoubleBits\n");
   Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_OPTIMIZATION_QA_DIR",Path.Combine(output,"existing-projection"));GraphProjectionOptimizationChecks.Run();
   Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_RAYCAST_QA_DIR",Path.Combine(output,"scoped-raycast"));GraphRaycastOptimizationChecks.Run();
   File.WriteAllText(Path.Combine(output,"result.txt"),$"GRAPH_ALLOCATION_AFFECTED_OK assertions={assertions} candidates projection binding gates AI legacy exactColliderOracle=passed\n");Debug.Log("GRAPH_ALLOCATION_AFFECTED_OK assertions="+assertions);
  }
 }
}
