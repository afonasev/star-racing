using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
 public static class GraphCommonVerticalChecks {
  static int assertions;
  static void Check(bool ok,string why){assertions++;if(!ok)throw new Exception("Graph vertical: "+why);}
  // Canonical sampled hill with exact endpoint frames. No gap/jump claim.
  public static IndependentGraph Geometry(IndependentGraph graph,int roadId=0,double height=4) {
   var old=graph.Road(roadId);var a=old.samples[0];var b=old.samples.Last();
   const double projectedLength=320,flat=80,hillLength=160;
   var from=roadId==0?b.position-a.tangent*projectedLength:a.position;
   var to=roadId==5?a.position+a.tangent*projectedLength:b.position;
   var frames=new List<GraphFrame>();double distance=0;DVec previous=default;
   for(int i=0;i<=64;i++) {
    double z=i*5,t=Math.Max(0,Math.Min(1,(z-flat)/hillLength)),sin=Math.Sin(Math.PI*t),cos=Math.Cos(Math.PI*t);
    double elevation=z<=flat||z>=flat+hillLength?0:height*sin*sin*sin*sin;
    double slope=z<=flat||z>=flat+hillLength?0:4*height*Math.PI*sin*sin*sin*cos/hillLength;
    var p=i==0?from:i==64?to:from+a.tangent*z+a.normal*elevation;
    if(i>0)distance+=Math.Sqrt((p-previous).Squared);
    var tangent=(a.tangent+a.normal*slope).Unit();var normal=DVec.Cross(a.right,tangent).Unit();
    if(elevation==0&&slope==0){tangent=a.tangent;normal=a.normal;}
    frames.Add(new GraphFrame(distance,p,tangent,normal,a.right,a.halfWidth,a.railLeft,a.railRight));previous=p;
   }
   double extra=distance-old.Length;
   var roads=graph.roads.Select(r=>new GraphRoad(r.id,r.kind,r.fromPort,r.toPort,r.branch,r.id==roadId?frames:r.samples,
    r.progressStart+(r.progressStart>=old.progressEnd?extra:0),r.progressEnd+(r.progressEnd>=old.progressEnd?extra:0)));
   return IndependentGraph.FromGeometry(roads,graph.junctions,graph.guardrailMode);
  }
  public static IndependentGraph Geometry(bool transformed=false,string mode="normal") {
   var t=new DVec(0,0,1);var n=new DVec(0,1,0);var r=new DVec(-1,0,0);var origin=new DVec();
   if(transformed){var axis=new DVec(.3,.7,.2).Unit();t=t.Rotate(axis,.81);n=n.Rotate(axis,.81);r=r.Rotate(axis,.81);origin=new DVec(200,70,-350);}
   return Geometry(Geometry(IndependentGraph.Create(origin,t,n,r,mode)),5);
  }
  static void Case(bool transformed,string mode,string output) {
   var graph=Geometry(transformed,mode);var root=new GameObject("Common vertical mesh");int supports=0,caps=0;var cooked=new HashSet<int>();
   string Key(RoadFace f)=>$"{f.geometryId}/{f.arm}/{f.sample}/{f.startDistance:R}/{f.endDistance:R}/{f.leftStart:R}/{f.rightStart:R}/{f.leftEnd:R}/{f.rightEnd:R}";
   var owners=new Dictionary<string,RoadVolumeMesh.SupportCell>();
   try {
    bool rejected=false;try{IndependentTrackMesh.Build(root.transform,graph,41,null,null);}catch(ArgumentException){rejected=true;}
    Check(rejected&&root.transform.childCount==0,"planar entry rejects elevation before allocation");
    var surface=IndependentTrackMesh.BuildCommonVertical(root.transform,graph,41,null,null,(context,tri)=>{
     supports++;owners[Key(tri.owner.face)]=tri.owner;var apex=tri.SelectApex(out _);Check(tri.owner.ContainsApex(apex),"exact 3D support apex remains in emitted owner");
     if(cooked.Add(tri.owner.face.geometryId)){var hull=RoadVolumeMesh.CreateSupport(context,tri);Check(hull.convex&&hull.sharedMesh!=null,"actual convex support cooked");}
     if(tri.side>=0&&tri.owner.sections[tri.side]){var side=tri.owner.sides[tri.side];int port=(side[0]==0||side[0]==2)?tri.owner.face.startPort:tri.owner.face.endPort;if(new[]{10,11,12,21,22,30}.Contains(port))caps++;}
    });
    Physics.SyncTransforms();Check(caps==0&&cooked.Count==6,"no internal port caps; six physical owners");
    var tops=root.GetComponentsInChildren<TrackSurface>();var addresses=new HashSet<string>();int matched=0;
    foreach(var top in tops)for(int q=0;q<top.sourceVertices.Length/4;q++) {
     var face=top.triangleFaces[q*2];Check(face.quad==q,"chunk-local metadata indexes the actual visual quad");Check(addresses.Add(Key(face)),"each physical quad emitted once across collider chunks");
     if(owners.TryGetValue(Key(face),out var owner)){matched++;for(int corner=0;corner<4;corner++)Check(top.sourceVertices[q*4+corner].Equals(owner.corners[corner]),"actual support owner and emitted top retain exact same local vertex");}
    }
    Check(matched==owners.Count,"every exterior support owner maps to exactly one physical top quad");
    Check(tops.SelectMany(t=>t.triangleFaces).Select(f=>f.geometryId).Distinct().OrderBy(i=>i).SequenceEqual(new[]{0,1,2,3,4,5}),"six unique physical geometries and no guide pavement");
    foreach(int id in new[]{0,5}) {
     var road=graph.Road(id);var f=road.Evaluate(road.Length*.5);var up=f.normal.Float;
     Check(surface.Raycast(new Ray(f.position.Float+up*2,-up),4,new[]{id},graph.Identity,41,out var centre,out var face)&&face.geometryId==id,"elevated physical centre top present");
     Check(Mathf.Abs(Vector3.Dot(centre.point-f.position.Float,up))<.001f,"real top at authored elevation");
     var flat=road.samples[0];Check(DVec.Dot(new DVec(centre.point.x,centre.point.y,centre.point.z)-flat.position,flat.normal)>3.9,"elevation is physical, not metadata");
     for(double at=2;at<road.Length-2;at+=2) {
      var frame=road.Evaluate(at);var point=frame.position.Float;
      foreach(float lane in new[]{-7f,0f,7f}) {
       var p=point+frame.right.Float*lane;
       Check(surface.Raycast(new Ray(p+frame.normal.Float*2,-frame.normal.Float),4,new[]{id},graph.Identity,41,out var hit,out _),"real wheel-lane raycast");
       Check(Vector3.Dot(hit.normal,frame.normal.Float)>.999f,"emitted smooth normal used by support");
       Check(Mathf.Abs(Vector3.Dot(hit.point-p,frame.normal.Float))<.001f,"canonical sample corner retained");
      }
     }
     Check(surface.TryBounds(f,id,0,out float left,out float right)&&left< -8&&right>8,"elevated emitted bounds");
    }
    foreach(int port in new[]{10,30}) {
     var sets=new Dictionary<int,HashSet<Vector3>>();
     foreach(var top in tops)for(int q=0;q<top.triangleFaces.Length/2;q++){var f=top.triangleFaces[q*2];bool start=f.startPort==port,end=f.endPort==port;if(!start&&!end)continue;if(!sets.TryGetValue(f.geometryId,out var set))sets[f.geometryId]=set=new HashSet<Vector3>();set.Add(top.sourceVertices[q*4+(start?0:1)]);set.Add(top.sourceVertices[q*4+(start?2:3)]);}
     var vertices=sets.Values.ToArray();Check(vertices.Length==2&&vertices[0].SetEquals(vertices[1]),"bit-exact common/planar port cuts");
    }
    typeof(IndependentTrackMeshChecks).GetMethod("ProbePorts",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{root,graph});
    ProbeChunkBoundaries(root,graph);
    var env=new GraphEnvironmentGeometry(graph);Check(env.Bounds.Contains(graph.Road(0).Evaluate(graph.Road(0).Length*.5).position.Float),"environment includes elevated graph");
    Check(!env.Clear(graph.Road(5).Evaluate(graph.Road(5).Length*.5).position.Float,Vector3.one,0),"continuous scenery clearance excludes vertical road");
    var course=new GraphRaceCourse(surface,0,(float)(graph.Road(5).progressStart+140));
    Check(course.Gates.Count>40&&course.Identity==new GraphRaceCourse(surface,0,(float)(graph.Road(5).progressStart+140)).Identity,"physical gate course deterministic on elevated geometry");
    File.WriteAllText(Path.Combine(output,$"vertical-{transformed}-{mode}.txt"),$"COMMON_VERTICAL_OK supports={supports} caps={caps} graph={graph.Identity} course={course.Identity}\n");
   }finally{UnityEngine.Object.DestroyImmediate(root);}
  }
  static void ProbeChunkBoundaries(GameObject root,IndependentGraph graph) {
   var before=Physics.simulationMode;Physics.simulationMode=SimulationMode.Script;
   try{var physics=root.scene.GetPhysicsScene();
    foreach(int id in new[]{0,5})foreach(double z in new[]{80d,240d}) {
     var road=graph.Road(id);var f=road.samples.First(sample=>Math.Abs(DVec.Dot(sample.position-road.samples[0].position,road.samples[0].tangent)-z)<1e-6);
     var probe=GameObject.CreatePrimitive(PrimitiveType.Sphere);UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(probe,root.scene);
     try{probe.transform.position=f.position.Float-f.tangent.Float*3+f.normal.Float*.51f;var body=probe.AddComponent<Rigidbody>();body.useGravity=false;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;body.linearDamping=0;body.linearVelocity=f.tangent.Float*20;Physics.SyncTransforms();
      for(int i=0;i<40;i++){body.AddForce(-f.normal.Float*9.81f,ForceMode.Acceleration);physics.Simulate(.01f);}
      var p=body.position;var frame=graph.ProjectCandidates(graph.Cursor(id,f.distance,id==5?1:0),new DVec(p.x,p.y,p.z),10).Single(candidate=>candidate.cursor.roadId==id).frame;float height=Vector3.Dot(p-frame.position.Float,frame.normal.Float);
      Debug.Log($"GRAPH_VERTICAL_CHUNK_PROBE road={id} station={f.distance:R} height={height:R} identity={graph.Identity}");
      Check(frame.distance>f.distance+3,"dynamic CCD sphere passes the actual planar/elevated chunk seam");Check(height>.45f&&height<.7f,"chunk seam retains original physical support height bounds");
     }finally{UnityEngine.Object.DestroyImmediate(probe);}
    }
   }finally{Physics.simulationMode=before;}
  }
  static void Rejection() {
   var baseGraph=IndependentGraph.Create(new DVec(),new DVec(0,0,1),new DVec(0,1,0),new DVec(-1,0,0));
   var root=new GameObject("Vertical rejected input");try {
    var roads=baseGraph.roads.Select(r=>{if(r.id!=1)return r;var frames=r.samples.ToArray();int mid=frames.Length/2;var f=frames[mid];frames[mid]=new GraphFrame(f.distance,f.position,f.tangent,new DVec(0,1,0).Rotate(f.tangent,.1),f.right.Rotate(f.tangent,.1),f.halfWidth,f.railLeft,f.railRight);return new GraphRoad(r.id,r.kind,r.fromPort,r.toPort,r.branch,frames,r.progressStart,r.progressEnd);});
    var unsupported=IndependentGraph.FromGeometry(roads,baseGraph.junctions,"normal");
    bool rejected=false;try{IndependentTrackMesh.BuildCommonVertical(root.transform,unsupported,1,null,null);}catch(ArgumentException){rejected=true;}
    Check(rejected&&root.transform.childCount==0&&root.GetComponent<GraphRoadSurface>()==null,"nonplanar independent road rejected before allocation");
    rejected=false;try{IndependentTrackMesh.BuildCommonVertical(root.transform,Geometry(),1,null,null,(context,tri)=>{throw new InvalidOperationException("owned failure");});}catch(InvalidOperationException e){rejected=e.Message=="owned failure";}
    Check(rejected&&root.transform.childCount==0,"nonplanar failure disposes owned geometry");
   }finally{UnityEngine.Object.DestroyImmediate(root);}
  }
  public static void DiagnosePrecision() {
   string output=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_VERTICAL_QA_DIR");Directory.CreateDirectory(output);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   var report=new System.Text.StringBuilder();
   foreach(bool elevated in new[]{false,true})foreach(bool compiledVertical in new[]{false,true}) {
    if(elevated&&!compiledVertical)continue;
    var g=Geometry(Geometry(IndependentGraph.Create(new DVec(200,70,-350),new DVec(0,0,1).Rotate(new DVec(.3,.7,.2).Unit(),.81),new DVec(0,1,0).Rotate(new DVec(.3,.7,.2).Unit(),.81),new DVec(-1,0,0).Rotate(new DVec(.3,.7,.2).Unit(),.81),"full"),0,elevated?4:0),5,elevated?4:0);
    var root=new GameObject("Precision diagnosis");try {
     var surface=compiledVertical?IndependentTrackMesh.BuildCommonVertical(root.transform,g,41,null,null,(_,__)=>{}):IndependentTrackMesh.Build(root.transform,g,41,null,null,(_,__)=>{});
     var top=root.GetComponentsInChildren<TrackSurface>().Single(s=>s.triangleFaces.Any(f=>f.geometryId==5&&f.startPort==30));var verts=top.sourceVertices;
     for(int q=0;q<top.triangleFaces.Length/2;q++) {var f=top.triangleFaces[q*2];if(f.geometryId!=5||f.startDistance>2)continue;report.AppendLine($"elevated={elevated} vertical={compiledVertical} d={f.startDistance:R}..{f.endDistance:R} a={verts[q*4]:G9} b={verts[q*4+1]:G9} normal={top.sourceNormals[q*4]:G9}");}
     try{typeof(IndependentTrackMeshChecks).GetMethod("ProbePorts",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{root,g});report.AppendLine($"ports passed elevated={elevated} vertical={compiledVertical}");}catch(Exception e){report.AppendLine($"ports FAILED elevated={elevated} vertical={compiledVertical} {e.InnerException?.Message??e.Message}");}
     if(elevated)foreach(string mask in new[]{"no-rails","top-only"}) {
      foreach(var c in root.GetComponentsInChildren<MeshCollider>())if(mask=="top-only"?c.GetComponent<TrackSurface>()==null:c.sharedMesh.name=="Independent guard rails")c.enabled=false;
      try{typeof(IndependentTrackMeshChecks).GetMethod("ProbePorts",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{root,g});report.AppendLine("ports passed mask="+mask);}catch(Exception e){report.AppendLine("ports FAILED mask="+mask+" "+(e.InnerException?.Message??e.Message));}
     }
    }finally{UnityEngine.Object.DestroyImmediate(root);}
   }
   File.WriteAllText(Path.Combine(output,"precision-diagnosis.txt"),report.ToString());
  }
  public static void RunBindings() {
   GraphVehicleRouteChecks.Run();
   typeof(GraphEnvironmentChecks).GetMethod("MarkContacts",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,null);
   File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_VEHICLE_QA_DIR"),"marks-and-rollback.txt"),"GRAPH_MARK_CONTACTS_AND_ROLLBACK_OK existing-contract-unchanged\n");
  }
  public static void Run() {
   string output=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_VERTICAL_QA_DIR");if(string.IsNullOrEmpty(output))throw new Exception("Vertical evidence directory required");Directory.CreateDirectory(output);assertions=0;EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   Case(false,"normal",output);Case(true,"full",output);Case(false,"none",output);Rejection();
   Debug.Log("COMMON_VERTICAL_CHECKS_OK assertions="+assertions);File.WriteAllText(Path.Combine(output,"result.txt"),"COMMON_VERTICAL_CHECKS_OK assertions="+assertions+"\n");
  }
 }
}
