using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace StarRacingPrototype {
 [InitializeOnLoad] public static class AuthoredCoursePreview {
  const string Key="StarRacing.AuthoredPreview";static double deadline;
  static AuthoredCoursePreview(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){
   if(!Environment.GetCommandLineArgs().Contains("-noaudio")||!Environment.GetCommandLineArgs().Contains("-muted"))throw new Exception("Muted startup required");
   Directory.CreateDirectory(AuthoredCourseChecks.Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",true);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.isPlaying=true;
  }
  static void Changed(PlayModeStateChange state){if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){deadline=EditorApplication.timeSinceStartup+360;EditorApplication.update+=Guard;new GameObject("Authored map preview").AddComponent<AuthoredPreviewDriver>();}
   if(state==PlayModeStateChange.EnteredEditMode){EditorApplication.update-=Guard;SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",true)?1:0);}
  }
  static void Guard(){EditorApplication.QueuePlayerLoopUpdate();if(EditorApplication.timeSinceStartup>deadline)Finish(new TimeoutException("Authored preview timeout"));}
  public static void Finish(Exception failure=null){if(failure!=null){File.WriteAllText(Path.Combine(AuthoredCourseChecks.Output,"failure.txt"),failure.ToString());Debug.LogException(failure);}SessionState.SetBool(Key+"Failed",failure!=null);EditorApplication.isPlaying=false;}
 }
 public sealed class AuthoredPreviewDriver:MonoBehaviour {
  TrackEnvironmentBuilder env;GameObject road;Camera camera;Light sun;Volume volume;VolumeProfile profile;
  IEnumerator Start(){
   Exception failure=null;var run=Run();while(true){bool more;try{more=run.MoveNext();}catch(Exception e){failure=e;break;}if(!more)break;yield return run.Current;}
   env?.Clear();if(road!=null)Destroy(road);if(profile!=null)Destroy(profile);if(volume!=null)Destroy(volume.gameObject);if(camera!=null)Destroy(camera.gameObject);if(sun!=null)Destroy(sun.gameObject);
   yield return null;AuthoredCoursePreview.Finish(failure);
  }
  IEnumerator Run(){
   var course=CourseSource.Generate(AuthoredCourseChecks.Config(),"cloud-city");road=new GameObject("Authored road");var track=road.AddComponent<TrackBuilder>();track.BuildNativeCourse(course);Physics.SyncTransforms();
   env=new TrackEnvironmentBuilder();env.Build(track.Route,track.transform);env.Sky.Activate();
   if(env.Root.GetComponentsInChildren<Collider>().Length!=0)throw new Exception("Environment added colliders");
   foreach(var s in course.Single.composition){
    foreach(float fraction in new[]{.1f,.5f,.9f}){
     var f=track.Route.Evaluate(30+Mathf.Lerp(s.startIndex,s.endIndex,fraction)*5);
     foreach(float side in new[]{-.8f,0,.8f}){var p=f.position+f.right*f.halfWidth*side;
      if(!track.RaycastRoad(p+f.normal*3,-f.normal,6,f.distance,out var hit)||Vector3.Distance(p,hit.point)>.2f)throw new Exception("Physical road missing at "+s.role+" "+f.distance);
     }
    }
   }
   sun=new GameObject("Authored preview sun").AddComponent<Light>();sun.type=LightType.Directional;sun.shadowBias=.035f;sun.shadowNormalBias=.15f;
   RaceLightingController.Apply(env.Plan.lighting,sun,null,null);
   volume=new GameObject("Authored tone mapping").AddComponent<Volume>();volume.isGlobal=true;profile=ScriptableObject.CreateInstance<VolumeProfile>();volume.sharedProfile=profile;
   profile.Add<Tonemapping>().mode.Override(TonemappingMode.ACES);var bloom=profile.Add<Bloom>();bloom.intensity.Override(.16f);bloom.threshold.Override(1.2f);
   camera=new GameObject("Authored preview camera").AddComponent<Camera>();camera.enabled=false;camera.fieldOfView=65;camera.nearClipPlane=.2f;camera.farClipPlane=14000;camera.clearFlags=CameraClearFlags.Skybox;camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
   for(int i=0;i<5;i++)yield return null;
   SetView(new Vector3(-2100,2100,400),new Vector3(550,380,2050));Capture(camera,"00-overview");
   SetView(new Vector3(-600,880,1520),AuthoredCloudCityCourse.Eye);Capture(camera,"01-eye-exterior");
   SetView(new Vector3(2100,1450,2300),new Vector3(1010,450,3310));Capture(camera,"02-twin-towers");
   foreach(var section in course.Single.composition){
    float distance=30+(section.startIndex+Mathf.Min(25,(section.endIndex-section.startIndex)*.1f))*5;
    var frame=track.Route.Evaluate(distance);var aim=track.Route.Evaluate(Mathf.Min(distance+95,track.Route.FinishDistance));
    camera.transform.position=frame.position-frame.tangent*13+frame.normal*6;
    camera.transform.rotation=Quaternion.LookRotation(aim.position+aim.normal*3-camera.transform.position,frame.normal);
    Capture(camera,"section-"+section.index);
   }
   var renderers=env.Root.GetComponentsInChildren<MeshRenderer>();
   File.WriteAllText(Path.Combine(AuthoredCourseChecks.Output,"preview-result.json"),JsonUtility.ToJson(new Result{success=true,geometry=course.GeometryIdentity,presentation=env.Plan.presentationHash,roadLength=track.Route.FinishDistance-30,buildingCount=env.Plan.towers.Count,landmarkParts=AuthoredCityArchitecture.Create().Count,environmentRenderers=renderers.Length,triangles=renderers.Sum(r=>r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3),materials=renderers.Select(r=>r.sharedMaterial).Distinct().Count(),images=11},true));
   Debug.Log("AUTHORED_PREVIEW_OK images=11 physicalSupportProbes=72");
  }
  [Serializable] class Result {public bool success;public string geometry,presentation;public float roadLength;public int buildingCount,landmarkParts,environmentRenderers,triangles,materials,images;}
  void SetView(Vector3 position,Vector3 target){camera.transform.position=position;camera.transform.LookAt(target);}
  public static void Capture(Camera camera,string name){
   var rt=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);rt.Create();var previous=RenderTexture.active;var pixels=new Texture2D(1600,900,TextureFormat.RGB24,false);
   try{RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,1600,900),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(AuthoredCourseChecks.Output,name+".png"),pixels.EncodeToPNG());}
   finally{RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(pixels);}
  }
 }
}
