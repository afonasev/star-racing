using System;
using System.IO;
using UnityEngine;
using System.Linq;
using UnityEditor.SceneManagement;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
 public static class GraphEnvironmentChecks {
  public static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_ENV_QA_DIR");
  static string Legacy(){var text="";foreach(uint seed in new uint[]{77,78,139})foreach(var theme in new[]{"cloud-city","space-station"}){var route=new TrackRoute(Procedural.Generator.Generate(seed,"full",theme,true,true));var plan=EnvironmentPlan.Create(route);text+=seed+" "+theme+" "+plan.hash+"\n";}return text;}
  static int assertions;
  static void Check(bool ok,string why){assertions++;if(!ok)throw new Exception("Graph environment: "+why);}
  static IndependentGraph Graph(bool rotated,string mode="normal") {var q=rotated?Quaternion.Euler(13,41,7):Quaternion.identity;var origin=rotated?new Vector3(321,57,-112):Vector3.zero;DVec D(Vector3 v)=>new DVec(v.x,v.y,v.z);var t=D(q*Vector3.forward).Unit(new DVec(0,0,1));var n=D(q*Vector3.up);n=(n-t*DVec.Dot(n,t)).Unit(new DVec(0,1,0));return IndependentGraph.Create(D(origin),t,n,DVec.Cross(t,n).Unit(new DVec(-1,0,0)),mode);}
  static Pose PoseAt(IndependentGraph graph,int road,double at){var f=graph.Road(road).Evaluate(at);return new Pose((f.position+f.normal*VehicleGeometry.AdheredBodyHeight).Float,Quaternion.LookRotation(f.tangent.Float,f.normal.Float));}
  public static void Run() {
   Directory.CreateDirectory(Output);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);assertions=0;
   Check(Legacy()==File.ReadAllText(Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_ENV_BASELINE")),"six exact legacy hashes");
   foreach(bool rotated in new[]{false,true})foreach(var mode in new[]{"normal","full","none"})foreach(var theme in new[]{"cloud-city","space-station"}) {
    var graph=Graph(rotated,mode);var geometry=new GraphEnvironmentGeometry(graph);var plan=EnvironmentPlan.Create(geometry,77,theme);Check(plan.hash==EnvironmentPlan.Create(geometry,77,theme).hash&&plan.hash.Contains(graph.Identity),"stable graph-bound plan");
    var middle=graph.Road(0).Evaluate(80);Check(!geometry.Clear(middle.position.Float,Vector3.one,0),"continuous sparse approach clearance");
    foreach(var tower in plan.towers)Check(geometry.Clear(tower.position,new Vector3(tower.width*.56f,tower.height*.5f+3,tower.width*.56f),EnvironmentPlan.CorridorMargin),"all-road tower/trim clearance");
    Check(geometry.Clear(plan.station,Vector3.one*170,EnvironmentPlan.CorridorMargin),"station envelope clearance");
    var root=new GameObject("Graph environment fixture");var environment=new TrackEnvironmentBuilder();try {
     var surface=IndependentTrackMesh.Build(root.transform,graph,91,null,null,(_,__)=>{});var colliders=root.GetComponentsInChildren<Collider>();var sources=root.GetComponentsInChildren<TrackSurface>();Check(sources.Length==1&&sources.SelectMany(x=>x.triangleFaces).Select(x=>x.geometryId).Distinct().OrderBy(x=>x).SequenceEqual(new[]{0,1,2,3,4,5}),"one combined top inventory with six physical geometry IDs");
     var filters=sources.Select(x=>x.GetComponent<MeshFilter>()).Concat(root.GetComponentsInChildren<MeshFilter>().Where(x=>x.sharedMesh.name=="Independent guard rails")).ToArray();var sourceMeshes=filters.Select(x=>x.sharedMesh).ToArray();var triangles=sourceMeshes.Select(x=>x.triangles).ToArray();var vertices=sourceMeshes.Select(x=>x.vertices).ToArray();var enabled=filters.Select(x=>x.GetComponent<MeshRenderer>().enabled).ToArray();filters[0].GetComponent<MeshRenderer>().enabled=false;enabled[0]=false;
     for(int cycle=0;cycle<2;cycle++) {
      environment.BuildIndependent(surface,77,theme);Check(environment.Root.GetComponentsInChildren<Collider>().Length==0&&root.GetComponentsInChildren<Collider>().SequenceEqual(colliders),"render-only collider inventory");
      var views=environment.Root.GetComponentsInChildren<MeshFilter>().Where(x=>x.sharedMesh.name.StartsWith("Graph environment ")).ToArray();Check(views.Length==filters.Length,"one visual clone per physical surface");
      for(int i=0;i<filters.Length;i++) {Check(sourceMeshes[i]==filters[i].sharedMesh&&vertices[i].SequenceEqual(sourceMeshes[i].vertices)&&triangles[i].SequenceEqual(sourceMeshes[i].triangles),"collision mesh unchanged");Check(!filters[i].GetComponent<MeshRenderer>().enabled,"source visual hidden");Check(vertices[i].SequenceEqual(views[i].sharedMesh.vertices)&&triangles[i].SequenceEqual(views[i].sharedMesh.triangles),"exact visual topology");
       var visualVertices=views[i].sharedMesh.vertices;
       if(filters[i].GetComponent<TrackSurface>() is TrackSurface top){var mapping=views[i].sharedMesh.uv;foreach(var face in top.triangleFaces.Where((_,index)=>index%2==0)){Check(mapping[face.quad*4]==new Vector2(face.leftStart/8f,face.startDistance/8f)&&mapping[face.quad*4+3]==new Vector2(face.rightEnd/8f,face.endDistance/8f),"physical road texture scale /8");}}
       for(int v=0;v<vertices[i].Length;v+=37){var world=filters[i].transform.TransformPoint(vertices[i][v]);Check((world-views[i].transform.TransformPoint(visualVertices[v])).sqrMagnitude<1e-6,"source transform retained");Check(geometry.Bounds.Contains(world),"all-world road bounds");}}
      if(theme=="space-station") {Check(environment.LampCount>0,"road lamps exist");foreach(var lamp in environment.Lamps){var n=graph.junctions[0].normal.Float;Check(geometry.Clear(lamp.basePoint+n*3,new Vector3(Math.Abs(n.x),Math.Abs(n.y),Math.Abs(n.z))*3.4f+Vector3.one*.8f,0),"entire lamp envelope clear");}}
      environment.Clear();for(int i=0;i<filters.Length;i++)Check(filters[i].GetComponent<MeshRenderer>().enabled==enabled[i],"exact enabled state restored");
     }
    }finally{environment.Clear();UnityEngine.Object.DestroyImmediate(root);}
   }
   MarkContacts();EnvironmentTextureChecks.Run();DriftTireMarkChecks.Run();
   File.WriteAllText(Path.Combine(Output,"result.txt"),"GRAPH_ENVIRONMENT_OK assertions="+assertions+" cases=12 legacyHashes=6 legacyEnvironmentAndMarks=passed\n");Debug.Log("GRAPH_ENVIRONMENT_OK assertions="+assertions);
  }
  static void MarkContacts() {
   var root=new GameObject("Graph tyre contacts");try {var track=root.AddComponent<TrackBuilder>();var graph=Graph(false);track.BuildIndependent(graph);Physics.SyncTransforms();var marks=track.TireMarks;Check(marks!=null,"graph mark buffer created");
    foreach(int road in new[]{1,2}) {
     marks.Clear();double at=graph.Road(road).Length*.5;Check(GraphVehicleRoute.TryCreate(track.GraphSurface,graph.Cursor(road,at),PoseAt(graph,road,at),1,0,out var route),"marks binding spawn");
     for(int tick=0;tick<4;tick++){var pose=PoseAt(graph,road,at+tick*.4);if(tick>0)Check(route.Observe(pose,1,tick),"mark support movement");var f=graph.Evaluate(route.Cursor);var origin=pose.position+f.normal.Float*.5f;Check(route.Raycast(origin,-f.normal.Float,2,out var hit),"suspension road hit");var support=new CheckpointAdmission.Support(origin,-f.normal.Float,2,hit);marks.Observe(0,1,f.tangent.Float*25+f.right.Float*7,f.tangent.Float,f.normal.Float,support,support,track,true,tick*.02f,route);}
     Check(marks.TotalSegments>0,"actual supported branch marks");long before=marks.TotalSegments;
     var other=graph.Road(road==1?2:1).Evaluate(at);Check(track.GraphSurface.Raycast(new Ray(other.position.Float+other.normal.Float*.8f,-other.normal.Float),2,new[]{road==1?2:1},graph.Identity,track.BuildRevision,out var foreign,out _),"foreign road hit exists");var invalid=new CheckpointAdmission.Support(foreign.point+foreign.normal*.8f,-foreign.normal,2,foreign);marks.Observe(0,1,Vector3.forward*25+Vector3.right*7,Vector3.forward,Vector3.up,invalid,invalid,track,true,.1f,route);Check(marks.TotalSegments==before,"foreign binding cannot mark");
     marks.Observe(0,2,Vector3.forward*25+Vector3.right*7,Vector3.forward,Vector3.up,invalid,invalid,track,true,.12f,route);Check(marks.TotalSegments==before,"stale revision cannot bridge");
     void Emit(double position,long tick,float time) {var pose=PoseAt(graph,road,position);route.Observe(pose,1,tick);var f=route.Frame;var o=pose.position+f.normal.Float*.5f;Check(route.Raycast(o,-f.normal.Float,2,out var hit),"continuity fixture hit");var support=new CheckpointAdmission.Support(o,-f.normal.Float,2,hit);marks.Observe(0,1,f.tangent.Float*25+f.right.Float*7,f.tangent.Float,f.normal.Float,support,support,track,true,time,route);}
     Emit(at+1.6,4,.14f);Emit(at+2,5,.16f);before=marks.TotalSegments;Emit(at+2.4,8,.18f);Emit(at+2.8,9,.20f);Check(marks.TotalSegments==before,"tick gap and next anchor cannot bridge previous trail");Emit(at+3.2,10,.22f);Check(marks.TotalSegments>before,"accepted movement resumes new trail");
    }
    marks.Clear();Check(GraphVehicleRoute.TryCreate(track.GraphSurface,graph.Cursor(0,158),PoseAt(graph,0,158),1,0,out var seam),"mark seam binding");
    foreach(var step in new[]{(road:0,at:159d,tick:1L),(road:10,at:.5d,tick:2L),(road:10,at:1d,tick:3L)}){var pose=PoseAt(graph,step.road,step.at);Check(seam.Observe(pose,1,step.tick),"legal seam observation");var f=seam.Frame;var o=pose.position+f.normal.Float*.5f;Check(seam.Raycast(o,-f.normal.Float,2,out var hit),"seam contact");var support=new CheckpointAdmission.Support(o,-f.normal.Float,2,hit);marks.Observe(0,1,f.tangent.Float*25+f.right.Float*7,f.tangent.Float,f.normal.Float,support,support,track,true,step.tick*.02f,seam);}
    Check(marks.TotalSegments>=2,"legal physical seam continues tyre trail");
    var initialize=(Action<DriftTireMarks,Bounds>)((_,__)=>throw new InvalidOperationException("fixture mark initialization failure"));var method=typeof(TrackBuilder).GetMethod("BuildIndependent",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);bool failed=false;try{method.Invoke(track,new object[]{graph,initialize});}catch(System.Reflection.TargetInvocationException e){failed=e.InnerException.Message=="fixture mark initialization failure";}
    Check(failed&&track.TireMarks==null&&track.GraphSurface==null&&root.transform.childCount==0,"failed mark initialization fully rolls back build");
   }finally{UnityEngine.Object.DestroyImmediate(root);}
  }
  public static void Render() {
   Directory.CreateDirectory(Output);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   foreach(var theme in new[]{"cloud-city","space-station"}) {var root=new GameObject("Rendered graph environment");var environment=new TrackEnvironmentBuilder();Camera camera=null;Light light=null;try {var graph=Graph(false);var track=root.AddComponent<TrackBuilder>();track.BuildIndependent(graph);var surface=track.GraphSurface;environment.BuildIndependent(surface,77,theme);environment.Sky.Activate();RenderSettings.ambientLight=environment.Plan.lighting.ambientColor;light=new GameObject("QA sunlight").AddComponent<Light>();light.type=LightType.Directional;light.intensity=environment.Plan.lighting.sunIntensity;light.transform.rotation=environment.Plan.lighting.sunRotation;RaceLightingController.Apply(environment.Plan.lighting,light,null,null);
    camera=new GameObject("QA camera").AddComponent<Camera>();camera.farClipPlane=6000;var bounds=environment.GraphGeometry.Bounds;camera.transform.position=bounds.center+new Vector3(450,1250,-750);camera.transform.LookAt(bounds.center);camera.clearFlags=CameraClearFlags.Skybox;camera.fieldOfView=65;var rt=new RenderTexture(1280,900,24);var previous=RenderTexture.active;var image=new Texture2D(1280,900,TextureFormat.RGB24,false);try {camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,900),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,theme+".png"),image.EncodeToPNG());
    var node=graph.junctions.Single(n=>!n.merging);var target=node.Point(0,node.Length*.6).Float;camera.transform.position=target+node.normal.Float*100-node.tangent.Float*140+node.right.Float*40;camera.transform.LookAt(target,node.normal.Float);camera.fieldOfView=60;camera.Render();image.ReadPixels(new Rect(0,0,1280,900),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,theme+"-fork.png"),image.EncodeToPNG());}finally{camera.targetTexture=null;RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);}
   }finally{environment.Clear();if(camera!=null)UnityEngine.Object.DestroyImmediate(camera.gameObject);if(light!=null)UnityEngine.Object.DestroyImmediate(light.gameObject);UnityEngine.Object.DestroyImmediate(root);}}
   File.WriteAllText(Path.Combine(Output,"result.txt"),"GRAPH_ENV_RENDER_OK themes=2\n");
  }
  public static void CaptureLegacy(){Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"legacy-environment.txt"),Legacy());Debug.Log("GRAPH_ENV_LEGACY_CAPTURE_OK cases=6");}
 }
}
