using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor.SceneManagement;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
 public static class GraphCommonCatalogChecks {
  static int assertions;
  static void Check(bool ok,string why){assertions++;if(!ok)throw new Exception("Graph catalog: "+why);}
  public static IndependentGraph Geometry(string mode="normal",bool jumps=true,bool transformed=false) {
   DVec t=new DVec(0,0,1),n=new DVec(0,1,0),r=new DVec(-1,0,0),o=new DVec();
   if(transformed){var axis=new DVec(.3,.7,.2).Unit();t=t.Rotate(axis,.81);n=n.Rotate(axis,.81);r=r.Rotate(axis,.81);o=new DVec(200,70,-350);}
   return GraphCommonCatalog.Create(o,t,n,r,mode,jumps);
  }
  public static void Run() {
   string output=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_CATALOG_QA_DIR");if(string.IsNullOrEmpty(output))throw new Exception("Evidence required");Directory.CreateDirectory(output);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);assertions=0;
   foreach(string mode in new[]{"normal","none","full"})Case(mode,mode=="full",output);
   Check(Geometry(jumps:false).jumps.Count==0&&Geometry(jumps:false).Identity!=Geometry().Identity,"jump toggle retained in catalog identity");
   Rejections();
   File.WriteAllText(Path.Combine(output,"result.txt"),$"GRAPH_COMMON_CATALOG_OK assertions={assertions} modes=3 transformed=true native12=inactive wholeTrackSafety=pending realVehicle=pending\n");Debug.Log("GRAPH_COMMON_CATALOG_OK assertions="+assertions);
  }
  public static void RunAffected() {
   string output=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_CATALOG_QA_DIR");
   Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_CATALOG_QA_DIR",Path.Combine(output,"catalog"));Run();
   Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_VERTICAL_QA_DIR",Path.Combine(output,"longitudinal"));GraphCommonVerticalChecks.Run();
   Environment.SetEnvironmentVariable("STAR_RACING_GRAPH_JUMP_QA_DIR",Path.Combine(output,"jump"));GraphJumpChecks.Run();
   File.WriteAllText(Path.Combine(output,"result.txt"),"COMMON_CATALOG_AFFECTED_OK catalog longitudinal jumps compilation noPlayerBuild\n");
  }
  public static void DiagnoseTransformed() {
   string output=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_CATALOG_QA_DIR");Directory.CreateDirectory(output);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);assertions=0;Case("full",true,output);
  }
  static void Case(string mode,bool transformed,string output) {
   var graph=Geometry(mode,transformed:transformed);var root=new GameObject("Common catalog");int supports=0,caps=0;var cooked=new HashSet<string>();
   string Key(RoadFace f)=>$"{f.geometryId}/{f.arm}/{f.startDistance:R}/{f.endDistance:R}/{f.leftStart:R}/{f.rightStart:R}/{f.leftEnd:R}/{f.rightEnd:R}";
   var owners=new Dictionary<string,RoadVolumeMesh.SupportCell>();
   try {
    Check((graph.Road(5).SurfaceNormal(-10,0)-graph.Road(5).SurfaceNormal(0,0)).Squared==0&&(graph.Road(5).SurfaceNormal(graph.Road(5).Length+10,0)-graph.Road(5).SurfaceNormal(graph.Road(5).Length,0)).Squared==0,"swept normal clamps at actual road endpoints");
    Check(graph.Validate().Length==0&&graph.Identity==Geometry(mode,transformed:transformed).Identity,"deterministic validated geometry");
    Check(graph.jumps.Count==4&&graph.jumps.All(j=>j.Valid(graph)),"all four unchanged480m stable runways with exact native offsets");
    foreach(var j in graph.jumps)Check(j.launch-j.runwayStart==90&&j.gapEnd-j.launch==25&&j.tacticalLanding-j.launch==95&&j.ballisticLanding-j.launch==340,"native runway safety contract");
    foreach(int id in new[]{0,5}) {
     var road=graph.Road(id);var carrier=graph.junctions[0];
     var collar=road.samples.Where(f=>id==0?f.distance>=road.Length-50:f.distance<=50);
     foreach(var f in collar)Check((f.normal-carrier.normal).Squared<1e-16&&(f.tangent-carrier.tangent).Squared<1e-16&&Math.Abs(DVec.Dot(f.position-carrier.origin,carrier.normal))<1e-8,"50m planar junction collars retained");
    }
    Check(graph.Road(0).samples.Any(f=>DVec.Dot(f.normal,graph.junctions[0].normal)<-.999),"loop physically inverted");
    Check(graph.Road(5).samples.Any(f=>Math.Abs(DVec.Dot(f.right,graph.junctions[0].normal))>.99),"bank includes vertical lateral frame");
    bool rejected=false;try{IndependentTrackMesh.BuildCommonVertical(root.transform,graph,51,null,null);}catch(ArgumentException){rejected=true;}
    Check(rejected&&root.transform.childCount==0,"longitudinal compiler guard retained");rejected=false;
    try{IndependentTrackMesh.Build(root.transform,graph,51,null,null);}catch(ArgumentException){rejected=true;}
    Check(rejected&&root.transform.childCount==0,"planar compiler guard retained");
    var surface=IndependentTrackMesh.BuildCommonCatalog(root.transform,graph,51,null,null,(context,tri)=> {
     supports++;owners[Key(tri.owner.face)]=tri.owner;Check(tri.owner.ContainsApex(tri.SelectApex(out _)),"actual support apex remains in emitted owner");
     var face=tri.owner.face;var f=IndependentTrackMesh.Evaluate(graph,face,(face.startDistance+face.endDistance)*.5f);
     string kind=face.geometryId==0&&Vector3.Dot(f.normal,graph.junctions[0].normal.Float)<-.9f?"inverted":face.geometryId==5&&Math.Abs(Vector3.Dot(f.right,graph.junctions[0].normal.Float))>.9f?"bank":face.geometryId.ToString();
     if(cooked.Add(kind)){var hull=RoadVolumeMesh.CreateSupport(context,tri);Check(hull.convex&&hull.sharedMesh!=null,"representative real convex support cooking "+kind);}
     if(tri.side>=0&&tri.owner.sections[tri.side]){var side=tri.owner.sides[tri.side];int port=(side[0]==0||side[0]==2)?face.startPort:face.endPort;if(new[]{10,11,12,21,22,30}.Contains(port))caps++;}
    });
    Physics.SyncTransforms();Check(caps==0&&cooked.Contains("inverted")&&cooked.Contains("bank"),"no internal caps and actual rolled/inverted hulls");
    var addresses=new HashSet<string>();int matched=0;
    foreach(var top in root.GetComponentsInChildren<TrackSurface>())for(int q=0;q<top.sourceVertices.Length/4;q++) {
     var face=top.triangleFaces[q*2];Check(face.quad==q&&addresses.Add(Key(face)),"one authored top quad with chunk-local metadata");
     if(owners.TryGetValue(Key(face),out var owner)){matched++;for(int c=0;c<4;c++)Check(top.transform.TransformPoint(top.sourceVertices[q*4+c]).Equals(root.transform.GetChild(0).TransformPoint(owner.corners[c])),"bit-exact world top/support correspondence");}
    }
    Check(matched==owners.Count,"all actual support cells map to top");
    foreach(int id in new[]{0,5}) {
     var road=graph.Road(id);
     foreach(var frame in road.samples.Where(f=>f.distance>1250&&f.distance<road.Length-60)) {
      foreach(double lane in new[]{-7d,0,7}) {
       var p=(frame.position+frame.right*lane).Float;
       Check(surface.Raycast(new Ray(p+frame.normal.Float*2,-frame.normal.Float),4,new[]{id},graph.Identity,51,out var hit,out var face)&&face.geometryId==id,"actual common catalog wheel-lane support road="+id+" at="+frame.distance.ToString("R")+" lane="+lane+" transformed="+transformed);
       Check(Math.Abs(Vector3.Dot(hit.point-p,frame.normal.Float))<.002f,"actual rolled/inverted top location");
       Check(Vector3.Dot(hit.normal,road.SurfaceNormal(frame.distance,lane).Float)>.999f,"smooth physical normals preserve actual swept ribbon");
      }
     }
     // A conservative continuous environment exclusion covers both faces of the loop/bank.
     var env=new GraphEnvironmentGeometry(graph);
     foreach(var frame in road.samples.Where(f=>f.distance>1250))Check(!env.Clear(frame.position.Float,Vector3.one,0)&&env.Bounds.Contains(frame.position.Float),"continuous environment includes vertical catalog");
    }
    Probe(root,graph,surface,0);Probe(root,graph,surface,5);
    File.WriteAllText(Path.Combine(output,$"catalog-{mode}.txt"),$"COMMON_CATALOG_OK supports={supports} caps={caps} graph={graph.Identity} approachMetres={graph.Road(0).Length:R} exitMetres={graph.Road(5).Length:R}\n");
   }finally{UnityEngine.Object.DestroyImmediate(root);}
  }
  static void Probe(GameObject root,IndependentGraph graph,GraphRoadSurface surface,int id) {
   var road=graph.Road(id);var baseNormal=graph.junctions[0].normal;
   var frame=road.samples.First(f=>id==0?DVec.Dot(f.normal,baseNormal)<-.98:Math.Abs(DVec.Dot(f.right,baseNormal))>.98);
   var previous=Physics.simulationMode;Physics.simulationMode=SimulationMode.Script;
   var probe=GameObject.CreatePrimitive(PrimitiveType.Sphere);
   try {
    probe.transform.position=frame.position.Float+frame.normal.Float*.51f;var body=probe.AddComponent<Rigidbody>();body.useGravity=false;body.linearDamping=0;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;body.linearVelocity=frame.tangent.Float*20;Physics.SyncTransforms();
    for(int i=0;i<40;i++){body.AddForce(-frame.normal.Float*14,ForceMode.Acceleration);root.scene.GetPhysicsScene().Simulate(.01f);}
    var p=body.position;var candidate=graph.ProjectCandidates(graph.Cursor(id,frame.distance,id==5?1:0),new DVec(p.x,p.y,p.z),15).Single(c=>c.cursor.roadId==id).frame;
    float height=Vector3.Dot(p-candidate.position.Float,candidate.normal.Float);
    Check(candidate.distance>frame.distance+5,"real CCD probe traverses rolled/inverted physical strip");Check(height>.45f&&height<.7f,"original physical support height bounds on catalog probe");
   }finally{UnityEngine.Object.DestroyImmediate(probe);Physics.simulationMode=previous;}
  }
  static void Rejections() {
   var g=Geometry();var root=new GameObject("Rejected catalog");
   try {
    foreach(int id in new[]{1,0}) {
     var old=g.Road(id);var frames=old.samples.ToArray();int index=id==0?frames.Length-3:frames.Length/2;var f=frames[index];
     frames[index]=new GraphFrame(f.distance,f.position,f.tangent,f.normal.Rotate(f.tangent,.1),f.right.Rotate(f.tangent,.1),f.halfWidth,f.railLeft,f.railRight);
     var invalid=IndependentGraph.FromGeometry(g.roads.Select(r=>r.id==id?new GraphRoad(r.id,r.kind,r.fromPort,r.toPort,r.branch,frames,r.progressStart,r.progressEnd):r),g.junctions,g.guardrailMode,false,g.jumps);
     bool rejected=false;try{IndependentTrackMesh.BuildCommonCatalog(root.transform,invalid,1,null,null);}catch(ArgumentException){rejected=true;}
     Check(rejected&&root.transform.childCount==0,"catalog protects standalone branches and junction collars");
    }
    bool failed=false;try{IndependentTrackMesh.BuildCommonCatalog(root.transform,g,1,null,null,(context,tri)=>{throw new InvalidOperationException("owned failure");});}catch(InvalidOperationException){failed=true;}
    Check(failed&&root.transform.childCount==0,"failure cleanup remains transactional");
   }finally{UnityEngine.Object.DestroyImmediate(root);}
  }
 }
}
