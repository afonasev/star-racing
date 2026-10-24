using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace StarRacingPrototype {
 public static class BasisCompatibilityChecks {
  public static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_BASIS_COMPAT_DIR");
  static int checks;
  internal static void Check(bool value,string message){checks++;if(!value)throw new Exception("BASIS_COMPAT "+message);}
  static void Reject(Action action,string label){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}Check(rejected,label);}
  public static NativeTrackCourse Course(bool candidate,uint seed=211933141,string theme="cloud-city",int revision=12){
   var before=BasisNativeExperiment.EditorOverride;
   try{BasisNativeExperiment.EditorOverride=candidate;return NativeTrackGenerator.Generate(seed,new Procedural.GraphCourseSettings(theme,"normal",true),revision);}
   finally{BasisNativeExperiment.EditorOverride=before;}
  }
  public static void RunDefaultSelection(){
   checks=0;var before=BasisNativeExperiment.EditorOverride;
   try{
    BasisNativeExperiment.EditorOverride=null;
    bool expected=Array.IndexOf(Environment.GetCommandLineArgs(),"--race-basis-baseline")<0;
    Check(BasisNativeExperiment.Enabled==expected,"process default/diagnostic opt-out");
    var settings=new Procedural.GraphCourseSettings("cloud-city","normal",true);
    var actual=NativeTrackGenerator.Generate(211933141,settings);
    Check(actual.UsesBasis==expected,"implicit native12 Single selection");
    var control=Course(false);var candidate=Course(true);
    Check(!control.UsesBasis&&candidate.UsesBasis,"explicit Editor overrides");
    Check(actual.GeometryIdentity==control.GeometryIdentity&&actual.GeometryIdentity==candidate.GeometryIdentity,"canonical identity unchanged");
    Check(JsonUtility.ToJson(actual.Single)==JsonUtility.ToJson(control.Single),"canonical source unchanged");
    BasisNativeExperiment.EditorOverride=true;
    foreach(int revision in new[]{10,11})Check(!NativeTrackGenerator.Generate(77,settings,revision).UsesBasis,"legacy excluded "+revision);
    uint graphSeed=1;while(NativeTrackGenerator.FamilyFor(graphSeed,"cloud-city")!="path-choice")graphSeed++;
    var graph=NativeTrackGenerator.Generate(graphSeed,settings);
    Check(graph.IsIndependent&&!graph.UsesBasis,"independent graph excluded");
    Debug.Log("BASIS_DEFAULT_SELECTION_OK checks="+checks+" enabled="+expected+" geometry="+actual.GeometryIdentity);
   }finally{BasisNativeExperiment.EditorOverride=before;}
  }
  static string MeshDigest(Mesh mesh){
   using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream)){
    foreach(var v in mesh.vertices){w.Write(v.x);w.Write(v.y);w.Write(v.z);}
    foreach(var v in mesh.normals){w.Write(v.x);w.Write(v.y);w.Write(v.z);}
    foreach(var v in mesh.tangents){w.Write(v.x);w.Write(v.y);w.Write(v.z);w.Write(v.w);}
    foreach(var v in mesh.uv){w.Write(v.x);w.Write(v.y);}
    for(int i=0;i<mesh.subMeshCount;i++)foreach(var t in mesh.GetIndices(i))w.Write(t);
    using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(stream.ToArray()));
   }
  }
  public static string[] VisualInventory(Transform root)=>root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled).Select(r=>{
   var mesh=r.GetComponent<MeshFilter>().sharedMesh;
   return r.name+"|"+r.transform.position.ToString("R")+"/"+r.transform.rotation.ToString("R")+"|"+r.transform.lossyScale.ToString("R")+"|"+MeshDigest(mesh)+"|"+r.shadowCastingMode+"|"+r.receiveShadows+"|"+string.Join(";",r.sharedMaterials.Select(MaterialDigest));
  }).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
  static string MaterialDigest(Material m){
   var rows=new List<string>{m.shader.name,m.renderQueue.ToString(),m.enableInstancing.ToString(),string.Join(",",m.shaderKeywords.OrderBy(x=>x))};
   for(int i=0;i<m.shader.GetPropertyCount();i++){
    string name=m.shader.GetPropertyName(i);var type=m.shader.GetPropertyType(i);string value;
    switch(type){
     case UnityEngine.Rendering.ShaderPropertyType.Color:value=m.GetColor(name).ToString("R");break;
     case UnityEngine.Rendering.ShaderPropertyType.Vector:value=m.GetVector(name).ToString("R");break;
     case UnityEngine.Rendering.ShaderPropertyType.Texture:var t=m.GetTexture(name);value=(t==null?"null":t.name+":"+t.width+":"+t.height)+":"+m.GetTextureScale(name).ToString("R")+":"+m.GetTextureOffset(name).ToString("R");break;
     case UnityEngine.Rendering.ShaderPropertyType.Int:value=m.GetInteger(name).ToString();break;
     default:value=m.GetFloat(name).ToString("R");break;
    }rows.Add(name+"="+value);
   }return string.Join(";",rows);
  }
  sealed class ColliderRecord {
   public string name,material;public Type type;public bool convex;public MeshColliderCookingOptions cooking;public int layer;public Vector3[] points;public int[] triangles;
  }
  static ColliderRecord[] CollisionInventory(TrackBuilder track)=>track.GetComponentsInChildren<Collider>().Where(c=>c.enabled).Select(c=>{
   var m=c as MeshCollider;var b=c as BoxCollider;var vertices=m!=null?m.sharedMesh.vertices:new[]{b.center-b.size*.5f,b.center+b.size*.5f};
   return new ColliderRecord{name=c.name,type=c.GetType(),layer=c.gameObject.layer,convex=m!=null&&m.convex,cooking=m==null?0:m.cookingOptions,material=c.sharedMaterial==null?"null":c.sharedMaterial.staticFriction.ToString("R")+"|"+c.sharedMaterial.dynamicFriction.ToString("R")+"|"+c.sharedMaterial.bounciness.ToString("R")+"|"+c.sharedMaterial.frictionCombine+"|"+c.sharedMaterial.bounceCombine,points=vertices.Select(c.transform.TransformPoint).ToArray(),triangles=m==null?Array.Empty<int>():m.sharedMesh.triangles};
  }).ToArray();
  public static void Run(){
   Directory.CreateDirectory(Output);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);checks=0;
   var baseline=Course(false);var candidate=Course(true);
   Check(!baseline.UsesBasis&&candidate.UsesBasis,"native12 single representation selected");
   Check(baseline.GeometryIdentity=="5cfad017df8d6cf2478ea7f83b23f988da715faeb8519a44e11f6aa072e155b6","historical canonical geometry identity");
   Check(baseline.GeometryIdentity==candidate.GeometryIdentity&&JsonUtility.ToJson(baseline.Single)==JsonUtility.ToJson(candidate.Single),"canonical identity/content exact");
   var root=new GameObject("Basis compatibility");var decor=new TrackEnvironmentBuilder();
   try{
    var track=root.AddComponent<TrackBuilder>();track.BuildNativeCourse(baseline);var replay=NativeCourseReplay.Capture(baseline,track);
    Check(replay.fullIdentity=="9457ab6020ce3ec1f73f3f6634f66fb51c15b8515054f358e4497125cd9718ba","historical canonical full replay");
    var physical=CollisionInventory(track);var visual=VisualInventory(track.transform);
    decor.Build(track.PresentationRoute,track.PresentationRoot,211933141);var environment=VisualInventory(decor.Root);var plan=JsonUtility.ToJson(decor.Plan);decor.Clear();
    track.BuildNativeCourse(candidate);var actual=CollisionInventory(track);Check(physical.Length==actual.Length,"collider count");
    float maxDelta=0;for(int i=0;i<actual.Length;i++){
     var a=physical[i];var b=actual[i];Check(a.name==b.name&&a.type==b.type&&a.layer==b.layer&&a.convex==b.convex&&a.cooking==b.cooking&&a.material==b.material,"collider settings "+i);
     Check(a.triangles.SequenceEqual(b.triangles)&&a.points.Length==b.points.Length,"collider topology "+i);
     for(int n=0;n<a.points.Length;n++){float delta=(a.points[n]-BasisNativeExperiment.ToCanonical(b.points[n])).magnitude;maxDelta=Mathf.Max(maxDelta,delta);Check(delta<=.002f,"original2mm geometry budget "+i+"/"+n);}
    }
    Check(track.PresentationRoot.GetComponentsInChildren<Collider>(true).Length==0,"render-only no colliders");
    Check(track.PresentationRoot.GetComponentsInChildren<TrackSurface>(true).Length==0,"render-only no physical surface registration");
    Check(visual.SequenceEqual(VisualInventory(track.transform)),"exact canonical rendered meshes normals tangents UV material transform inventory");
    decor.Build(track.PresentationRoute,track.PresentationRoot,211933141);
    Check(plan==JsonUtility.ToJson(decor.Plan),"exact environment plan");Check(environment.SequenceEqual(VisualInventory(decor.Root)),"exact environment rendering");
    replay.Verify(candidate,track);Check(JsonUtility.ToJson(replay)==JsonUtility.ToJson(NativeCourseReplay.Capture(candidate,track)),"old replay accepted; new capture exact");
    var canonical=candidate.Single.samples[20];double width=canonical.halfWidth;canonical.halfWidth+=.01;Reject(()=>replay.Verify(candidate,track),"canonical mutation rejected");canonical.halfWidth=width;
    var runtime=track.Route.Definition.samples[20];width=runtime.halfWidth;runtime.halfWidth+=.01;Reject(()=>replay.Verify(candidate,track),"runtime mutation rejected");runtime.halfWidth=width;
    var sample=track.Route.Samples[20];track.Route.Samples[20].position+=Vector3.one;Reject(()=>replay.Verify(candidate,track),"runtime cache mutation rejected");track.Route.Samples[20]=sample;
    replay.Verify(candidate,track);decor.Clear();track.BuildNativeCourse(candidate);replay.Verify(candidate,track);Check(visual.SequenceEqual(VisualInventory(track.transform)),"rebuild exact canonical rendering");
    track.Build(new TrackRoute(track.Route.Definition));Reject(()=>replay.Verify(candidate,track),"unbound/stale build rejected");
    foreach(int revision in new[]{10,11})Check(!Course(true,77,"cloud-city",revision).UsesBasis,"legacy path unchanged "+revision);
    string result="BASIS_COMPAT_OK checks="+checks+" colliders="+actual.Length+" visual="+visual.Length+" environment="+environment.Length+" maxCollisionDelta="+maxDelta.ToString("R")+" replay=historical-exact mutation=reject canonical+runtime+cache+stale\n";
    File.WriteAllText(Path.Combine(Output,"structural-pass.txt"),result);Debug.Log(result);
   }finally{decor.Clear();UnityEngine.Object.DestroyImmediate(root);BasisNativeExperiment.EditorOverride=null;}
  }
 }
}
