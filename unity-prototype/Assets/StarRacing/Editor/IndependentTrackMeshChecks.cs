using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
 public static class IndependentTrackMeshChecks {
  static int assertions;
  static void Check(bool ok,string why){assertions++;if(!ok)throw new Exception("Graph mesh: "+why);}
  static void Case(IndependentGraph graph,string label,string output) {
   var root=new GameObject("Graph QA "+label);int supports=0,internalCaps=0,commonWalls=0;
   int[] paired={10,11,12,21,22,30};var cooked=new HashSet<int>();
   try {
    var owner=IndependentTrackMesh.Build(root.transform,graph,37,null,null,(context,tri)=>{
     Check(context!=null && context.parent==root.transform,"explicit support coordinate context");
     supports++;var cell=tri.owner;var apex=tri.SelectApex(out _);
     Check(cell.ContainsApex(apex),"support apex remains inside exact owner");
     // Exercise the actual PhysX hull adapter once per physical surface, in the
     // same coordinate context as production. Other triangles retain exact checks.
     if(cooked.Add(cell.face.geometryId)) {
      var collider=RoadVolumeMesh.CreateSupport(context,tri);
      Check(collider.convex && collider.sharedMesh!=null && collider.transform.parent==context,"actual local support hull cooked");
      var hull=collider.sharedMesh.vertices;var authored=new[]{tri.a,tri.b,tri.c,apex};
      for(int i=0;i<4;i++)Check((collider.transform.localPosition+hull[i]).Equals(authored[i]),"support hull preserves exact authored local vertex");
     }
     if(tri.side>=0 && cell.sections[tri.side]) {
      int[] side=cell.sides[tri.side];bool start=side[0]==0 || side[0]==2;
      int port=start?cell.face.startPort:cell.face.endPort;
      if(paired.Contains(port))internalCaps++;
     }
     if(cell.face.geometryId==3 || cell.face.geometryId==4) {
      // Compiler support coordinates are in its shared planar local frame.
      float xa=tri.a.x,xb=tri.b.x,xc=tri.c.x;
      if(Mathf.Abs(xa)<1e-7f && Mathf.Abs(xb)<1e-7f && Mathf.Abs(xc)<1e-7f)commonWalls++;
     }
    });
    Check(cooked.Count==6,"actual support hull cooking covers six physical surfaces");
    Check(supports>1000,"actual exterior support inventory");Check(internalCaps==0,"paired port seams have no exterior cap/support");Check(commonWalls==0,"common node x=0 has no internal wall");
    var top=root.GetComponentsInChildren<TrackSurface>().Single();
    var mesh=top.GetComponent<MeshCollider>().sharedMesh;var vertices=mesh.vertices;var indices=mesh.triangles;
    Check(top.triangleFaces.Length==indices.Length/3,"face metadata covers collider triangles");
    var ids=top.triangleFaces.Select(f=>f.geometryId).Distinct().OrderBy(i=>i).ToArray();
    Check(ids.SequenceEqual(new[]{0,1,2,3,4,5}),"six physical surfaces; no guide ribbons");
    for(int i=0;i<indices.Length;i+=3)Check(Vector3.Cross(vertices[indices[i+1]]-vertices[indices[i]],vertices[indices[i+2]]-vertices[indices[i]]).sqrMagnitude>1e-12f,"no degenerate top triangle");
    foreach(int port in paired) {
     var groups=new Dictionary<int,HashSet<Vector3>>();
     for(int q=0;q<top.triangleFaces.Length/2;q++) {
      var face=top.triangleFaces[q*2];bool start=face.startPort==port,end=face.endPort==port;
      if(!start&&!end)continue;
      if(!groups.TryGetValue(face.geometryId,out var set))groups[face.geometryId]=set=new HashSet<Vector3>();
      set.Add(vertices[q*4+(start?0:1)]);set.Add(vertices[q*4+(start?2:3)]);
     }
     Check(groups.Count==2,"exactly two physical surfaces per port");var sets=groups.Values.ToArray();
     Check(sets[0].SetEquals(sets[1]),"bit-exact shared port vertex registry port="+port+" counts="+sets[0].Count+"/"+sets[1].Count);
     var frame=graph.ports.Single(p=>p.id==port).frame;
     foreach(float along in new[]{-1f,0f,1f})foreach(float lane in new[]{-7f,-2f,2f,7f}) {
      var point=frame.position.Float+frame.tangent.Float*along+frame.right.Float*lane;
      Check(owner.Raycast(new Ray(point+frame.normal.Float*2,-frame.normal.Float),4,new[]{0,1,2,3,4,5},graph.Identity,37,out var hit,out _),"footprint support across each port");
      Check(Mathf.Abs(Vector3.Dot(hit.point-point,frame.normal.Float))<.001f,"port top has no vertical step");
     }
    }
    foreach(int roadId in new[]{1,2}) {
     var road=graph.Road(roadId);var f=road.Evaluate(road.Length*.43);
     Check(owner.Raycast(new Ray(f.position.Float+f.normal.Float*2,-f.normal.Float),4,new[]{roadId},graph.Identity,37,out _,out var face) && face.geometryId==roadId,"physical road query returns correct metadata");
     Check(!owner.Raycast(new Ray(f.position.Float+f.normal.Float*2,-f.normal.Float),4,new[]{roadId==1?2:1},graph.Identity,37,out _,out _),"wrong road whitelist cannot admit surface");
     Check(owner.TryBounds(f,roadId,0,out var left,out var right) && left< -8 && right>8,"actual compiled-road transverse bounds");
     Check(!owner.Raycast(new Ray(f.position.Float+f.normal.Float*2,-f.normal.Float),4,new[]{roadId},graph.Identity,36,out _,out _),"stale runtime build rejected despite same geometry identity");
    }
    foreach(var node in graph.junctions) {
     double z=node.merging?60:160;var point=node.Point(0,z);
     Check(!owner.Raycast(new Ray(point.Float+node.normal.Float*2,-node.normal.Float),4,new[]{node.id},graph.Identity,37,out _,out _),"physical divider gap never has top support");
     var carrier=new GraphFrame(z,point,node.tangent,node.normal,node.right,8.5);
     Check(!owner.TryBounds(carrier,node.id,0,out _,out _),"pavement bounds do not fill node divider");
     var guide=graph.Road(node.merging?12:10).Evaluate(graph.Road(node.merging?12:10).Length*.5);
     Check(owner.TryBounds(guide,node.id,0,out var left,out var right) && right-left>15,"node bounds use real panels in guide transverse plane");
    }
    ProbePorts(root,graph);
    int railCount=root.GetComponentsInChildren<MeshFilter>().Count(f=>f.sharedMesh.name=="Independent guard rails");
    Check(railCount==(graph.guardrailMode=="none"?0:1),"rail mode respected");
    File.WriteAllText(Path.Combine(output,label+".txt"),$"GRAPH_MESH_OK assertions={assertions} supports={supports} topTriangles={indices.Length/3} internalCaps={internalCaps} commonWalls={commonWalls} identity={graph.Identity}\n");
   } finally {UnityEngine.Object.DestroyImmediate(root);}
  }
  static void ProbePorts(GameObject root,IndependentGraph graph) {
   var scene=root.scene;
   var mode=Physics.simulationMode;
   try {
    Physics.simulationMode=SimulationMode.Script;
    var physics=scene.GetPhysicsScene();Physics.SyncTransforms();
    foreach(int port in new[]{10,11,12,21,22,30}) {
     var frame=graph.ports.Single(p=>p.id==port).frame;
     var probe=GameObject.CreatePrimitive(PrimitiveType.Sphere);SceneManager.MoveGameObjectToScene(probe,scene);
     try {
      probe.transform.position=frame.position.Float-frame.tangent.Float*3+frame.normal.Float*.51f;
      var body=probe.AddComponent<Rigidbody>();body.useGravity=false;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;body.linearDamping=0;
      body.linearVelocity=frame.tangent.Float*20;Physics.SyncTransforms();
      Check((body.position-probe.transform.position).sqrMagnitude<1e-10f,"probe initial pose synchronized");
      for(int i=0;i<40;i++){body.AddForce(-frame.normal.Float*9.81f,ForceMode.Acceleration);physics.Simulate(.01f);}
      Check(Vector3.Dot(body.position-frame.position.Float,frame.tangent.Float)>3,"dynamic body passes port without hidden cap");
      float height=Vector3.Dot(body.position-frame.position.Float,frame.normal.Float);
      Debug.Log($"GRAPH_PORT_PROBE port={port} identity={graph.Identity} height={height:R} position={body.position:G9} velocity={body.linearVelocity:G9}");
      Check(height>.45f && height<.7f,"dynamic body retains top support at port="+port+" height="+height.ToString("R"));
     } finally {UnityEngine.Object.DestroyImmediate(probe);}
    }
   } finally {Physics.simulationMode=mode;}
  }
  static void TinyGap(IndependentGraph graph) {
   var root=new GameObject("Tiny gap namespace");var vertices=new List<Vector3>();var faces=new List<RoadFace>();var triangles=new List<int>();int inner=0;
   try {
    foreach(int arm in new[]{1,2}) {
     float l=arm==1?-8.5f:.00002f,r=arm==1?-.00002f:8.5f;
     var face=new RoadFace(0,vertices.Count/4,0,.5f,l,r,l,r,false){geometryId=3,arm=arm,leftBoundaryTag=arm==2?2:0,rightBoundaryTag=arm==1?1:0};
     int at=vertices.Count;vertices.AddRange(new[]{new Vector3(-l,0,0),new Vector3(-l,0,.5f),new Vector3(-r,0,0),new Vector3(-r,0,.5f)});
     triangles.AddRange(new[]{at,at+2,at+1,at+2,at+3,at+1});faces.Add(face);faces.Add(face);
    }
    var go=new GameObject("Tiny graph top");go.transform.SetParent(root.transform,false);
    var mesh=new Mesh{name="Tiny graph top",vertices=vertices.ToArray(),triangles=triangles.ToArray()};mesh.RecalculateNormals();mesh.RecalculateBounds();
    go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshCollider>().sharedMesh=mesh;go.AddComponent<TrackSurface>();
    RoadVolumeMesh.BuildGraph(root.transform,(face,d)=>new TrackFrame{position=new Vector3(0,0,d),distance=d,tangent=Vector3.forward,normal=Vector3.up,right=Vector3.right,halfWidth=8.5f},
     vertices,faces,triangles,null,tri=>{
      tri.SelectApex(out _);
      if(Mathf.Abs(tri.a.x)<.00003f && Mathf.Abs(tri.b.x)<.00003f && Mathf.Abs(tri.c.x)<.00003f)inner++;
     });
    var collider=root.GetComponentInChildren<TrackSurface>().GetComponent<MeshCollider>();
    Check(inner==4,"rounded scalar keys cannot swallow two tiny-gap exterior walls");
    Check(!collider.Raycast(new Ray(new Vector3(0,1,.25f),Vector3.down),out _,2),"tiny actual divider retains no top collider");
   } finally {
    var meshes=new HashSet<Mesh>();foreach(var filter in root.GetComponentsInChildren<MeshFilter>())meshes.Add(filter.sharedMesh);
    foreach(var collider in root.GetComponentsInChildren<MeshCollider>())meshes.Add(collider.sharedMesh);
    UnityEngine.Object.DestroyImmediate(root);foreach(var mesh in meshes)if(mesh!=null)UnityEngine.Object.DestroyImmediate(mesh);
   }
  }
  static void FailureContracts(IndependentGraph graph) {
   var root=new GameObject("Graph failure QA");Mesh emitted=null;
   try {
    bool rejected=false;
    try {IndependentTrackMesh.Build(root.transform,graph,37,null,null,(context,tri)=>{
     emitted=context.GetComponentInChildren<MeshFilter>().sharedMesh;throw new InvalidOperationException("injected observer failure");
    });}catch(InvalidOperationException e){rejected=e.Message=="injected observer failure";}
    Check(rejected && emitted==null && root.transform.childCount==0,"failed compilation disposes its emitted mesh and child");
    Check(root.GetComponent<GraphRoadSurface>()==null,"failed compilation grants no surface binding");
    foreach(bool width in new[]{true,false}) {
     var roads=graph.roads.Select(road=>{
      if(road.id!=IndependentGraph.Technical)return road;
      var frames=road.samples.ToArray();var f=frames[frames.Length/2];
      frames[frames.Length/2]=new GraphFrame(f.distance,f.position,f.tangent,f.normal,f.right,width?9:f.halfWidth,width?f.railLeft:!f.railLeft,f.railRight);
      return new GraphRoad(road.id,road.kind,road.fromPort,road.toPort,road.branch,frames,road.progressStart,road.progressEnd);
     });
     var unsupported=IndependentGraph.FromGeometry(roads,graph.junctions,graph.guardrailMode);
     Check(unsupported.Validate().Length==0,"domain-valid imported graph for compiler rejection fixture");
     rejected=false;try{IndependentTrackMesh.Build(root.transform,unsupported,37,null,null);}catch(ArgumentException){rejected=true;}
     Check(rejected && root.transform.childCount==0,"unsupported width or rail metadata rejected before allocation");
    }
   }finally{UnityEngine.Object.DestroyImmediate(root);}
  }
  public static void Run() {
   string output=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_MESH_QA_DIR");if(string.IsNullOrEmpty(output))throw new Exception("Graph mesh QA directory required");Directory.CreateDirectory(output);assertions=0;
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   var t=new DVec(0,0,1);var n=new DVec(0,1,0);var r=new DVec(-1,0,0);
   string filter=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_MESH_CASE");
   if(string.IsNullOrEmpty(filter) || filter=="normal")Case(IndependentGraph.Create(new DVec(),t,n,r,"normal"),"normal",output);
   if(string.IsNullOrEmpty(filter) || filter=="none")Case(IndependentGraph.Create(new DVec(),t,n,r,"none"),"none",output);
   var axis=new DVec(.3,.7,.2).Unit();
   if(string.IsNullOrEmpty(filter) || filter=="transformed-full")Case(IndependentGraph.Create(new DVec(123,45,-67),t.Rotate(axis,.81),n.Rotate(axis,.81),r.Rotate(axis,.81),"full"),"transformed-full",output);
   FailureContracts(IndependentGraph.Create(new DVec(),t,n,r));
   TinyGap(IndependentGraph.Create(new DVec(),t,n,r));
   RoadVolumeChecks.ValidateFlatClosure();RoadCellCertificationChecks.Run();
   File.WriteAllText(Path.Combine(output,"result.txt"),$"INDEPENDENT_MESH_CHECKS_OK assertions={assertions} physics=collider-raycasts-and-dynamic-port-probes support=exact-contained legacy=flat-closure-and-certification traversal=deferred\n");
   Debug.Log($"INDEPENDENT_MESH_CHECKS_OK assertions={assertions}");
  }
 }
}
