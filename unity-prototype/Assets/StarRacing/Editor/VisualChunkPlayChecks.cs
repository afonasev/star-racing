using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace StarRacingPrototype {
 [InitializeOnLoad] public static class VisualChunkPlayChecks {
  const string Key="StarRacing.VisualChunkPlay";static double deadline;
  public static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_CHUNK_PLAY_QA");
  static VisualChunkPlayChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"failed",true);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.isPlaying=true;}
  static void Changed(PlayModeStateChange state){
   if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){deadline=EditorApplication.timeSinceStartup+300;EditorApplication.update+=Watch;new GameObject("Visual chunk PlayMode QA").AddComponent<VisualChunkPlayDriver>();}
   if(state==PlayModeStateChange.EnteredEditMode){EditorApplication.update-=Watch;SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"failed",true)?1:0);}
  }
  static void Watch(){EditorApplication.QueuePlayerLoopUpdate();if(EditorApplication.timeSinceStartup>deadline){File.WriteAllText(Path.Combine(Output,"failure.txt"),"PlayMode timeout");Finish(false);}}
  public static void Finish(bool success){SessionState.SetBool(Key+"failed",!success);EditorApplication.isPlaying=false;}
 }
 public sealed class VisualChunkPlayDriver:MonoBehaviour {
  TrackEnvironmentBuilder env;int checks;Camera view;Light sun;
  void Check(bool ok,string message){checks++;if(!ok)throw new Exception("VISUAL_CHUNK_PLAY: "+message);}
  IEnumerator Start(){
   var work=Run();Exception error=null;
   while(true){bool more;try{more=work.MoveNext();}catch(Exception e){error=e;break;}if(!more)break;yield return work.Current;}
   env?.Clear();if(error!=null){Debug.LogException(error);File.WriteAllText(Path.Combine(VisualChunkPlayChecks.Output,"failure.txt"),error.ToString());}
   else{File.WriteAllText(Path.Combine(VisualChunkPlayChecks.Output,"result.json"),$"{{\"success\":true,\"checks\":{checks},\"cycles\":2}}");Debug.Log("VISUAL_CHUNK_PLAY_OK checks="+checks);}
   VisualChunkPlayChecks.Finish(error==null);
  }
  IEnumerator Run(){
   Check(SilentStartup.Enabled,"silent startup");
   var builder=gameObject.AddComponent<TrackBuilder>();builder.BuildNativeCourse(NativeTrackGenerator.Generate(211933141,new Procedural.GraphCourseSettings("cloud-city","normal",true),12));
   // TrackBuilder disables and defers deletion of 32 marker colliders in PlayMode.
   // Record the physical baseline only after construction has settled.
   yield return null;yield return null;
   var shell=builder.PresentationRoot.GetComponentsInChildren<MeshFilter>().First(f=>f.sharedMesh.name==RoadVolumeMesh.MeshName).GetComponent<MeshRenderer>();
   bool enabled=shell.enabled;var material=shell.sharedMaterial;var colliders=GetComponentsInChildren<Collider>();
   env=new TrackEnvironmentBuilder();view=new GameObject("Visual chunk camera").AddComponent<Camera>();view.enabled=false;view.fieldOfView=65;view.nearClipPlane=.2f;
   var chase=view.gameObject.AddComponent<ChaseCamera>();sun=new GameObject("Visual chunk sun").AddComponent<Light>();
   string hash=null;
   for(int cycle=0;cycle<2;cycle++){
    env.Build(builder.PresentationRoute,builder.PresentationRoot);env.Sky.Activate();RaceLightingController.Apply(env.Plan.lighting,sun,null,null);chase.SetSkyline(env.Plan);
    if(hash==null)hash=env.Plan.hash;Check(env.Plan.hash==hash,"repeat plan hash");
    Check(!shell.enabled,"shell source suppressed");var current=GetComponentsInChildren<Collider>();
    var missing=colliders.Except(current).Select(c=>c==null?"destroyed":c.name+":"+c.GetInstanceID()).ToArray();var extra=current.Except(colliders).Select(c=>c.name+":"+c.GetInstanceID()).ToArray();
    File.AppendAllText(Path.Combine(VisualChunkPlayChecks.Output,"colliders.txt"),"cycle="+cycle+" baseline="+colliders.Length+" current="+current.Length+" missing="+string.Join(",",missing)+" extra="+string.Join(",",extra)+"\n");
    Check(env.Root.GetComponentsInChildren<Collider>().Length==0&&current.SequenceEqual(colliders),"collider instances unchanged; cycle="+cycle+" missing="+missing.Length+" extra="+extra.Length);
    var filters=env.Root.GetComponentsInChildren<MeshFilter>();var meshes=filters.Select(f=>f.sharedMesh).Distinct().ToArray();var materials=env.Root.GetComponentsInChildren<MeshRenderer>().SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
    Check(filters.Count(f=>f.sharedMesh.name.EndsWith(" section"))>3,"spatial visuals published");
    if(cycle==0){
     for(int i=0;i<3;i++){var frame=builder.PresentationRoute.Samples[i*builder.PresentationRoute.Samples.Length/3];view.transform.position=frame.position-frame.tangent*12+frame.normal*8;view.transform.LookAt(frame.position+frame.tangent*100+frame.normal*3);chase.SetSkyline(env.Plan);yield return null;Capture("view-"+i);}
    }
    env.Clear();Check(env.Root==null&&shell.enabled==enabled,"original renderer restored immediately");yield return null;yield return null;
    Check(meshes.All(m=>m==null)&&materials.All(m=>m==null),"deferred render resource teardown");Check(material!=null&&shell.sharedMaterial==material,"TrackBuilder material preserved");
   }
   Destroy(view.gameObject);Destroy(sun.gameObject);
  }
  void Capture(string name){
   var target=new RenderTexture(1280,800,24);var image=new Texture2D(1280,800,TextureFormat.RGB24,false);var old=RenderTexture.active;
   try{view.targetTexture=target;view.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1280,800),0,0);image.Apply();File.WriteAllBytes(Path.Combine(VisualChunkPlayChecks.Output,name+".png"),image.EncodeToPNG());}
   finally{view.targetTexture=null;RenderTexture.active=old;target.Release();Destroy(target);Destroy(image);}
  }
 }
}
