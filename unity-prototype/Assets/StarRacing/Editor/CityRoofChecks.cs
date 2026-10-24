using System;
using System.IO;
using System.Linq;
using System.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
namespace StarRacingPrototype {
 [InitializeOnLoad] public static class CityRoofChecks {
  const string Key="StarRacing.CityRoofQA";
  public static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_CITY_ROOF_QA");
  static double deadline;
  static CityRoofChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){
   if(string.IsNullOrEmpty(Output))throw new Exception("Roof QA output required");
   Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",true);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.isPlaying=true;
  }
  static void Changed(PlayModeStateChange state){
   if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){deadline=EditorApplication.timeSinceStartup+300;EditorApplication.update+=Watch;new GameObject("City roof QA").AddComponent<CityRoofDriver>();}
   if(state==PlayModeStateChange.EnteredEditMode){EditorApplication.update-=Watch;SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",true)?1:0);}
  }
  static void Watch(){if(EditorApplication.timeSinceStartup>deadline){Debug.LogError("CITY_ROOF_TIMEOUT");Finish(false);}}
  public static void Finish(bool ok){SessionState.SetBool(Key+"Failed",!ok);EditorApplication.isPlaying=false;}
 }
 public sealed class CityRoofDriver:MonoBehaviour {
  TrackEnvironmentBuilder env;Camera camera;Light sun;
  [Serializable] public sealed class Result {public bool success,baseline;public int towers,duplicateRoofs,roofSamples,emissiveRenderers,checks;public string planHash;public float farClip;}
  IEnumerator Start(){
   Exception error=null;try{Setup();}catch(Exception e){error=e;}
   if(error==null){for(int i=0;i<4;i++)yield return null;try{VerifyAndCapture();}catch(Exception e){error=e;}}
   env?.Clear();if(camera!=null)Destroy(camera.gameObject);if(sun!=null)Destroy(sun.gameObject);
   if(error!=null){Debug.LogException(error);File.WriteAllText(Path.Combine(CityRoofChecks.Output,"failure.txt"),error.ToString());}
   CityRoofChecks.Finish(error==null);
  }
  void Setup(){
   var route=new TrackRoute(Procedural.Generator.Generate(77,"full","cloud-city",true,true));
   env=new TrackEnvironmentBuilder();env.Build(route,transform);env.Sky.Activate();
   var light=env.Plan.lighting;RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=light.ambientColor;RenderSettings.ambientEquatorColor=light.ambientEquatorColor;RenderSettings.ambientGroundColor=light.ambientGroundColor;RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogDensity=light.fogDensity;RenderSettings.fogColor=light.fogColor;
   sun=new GameObject("Roof QA sun").AddComponent<Light>();sun.type=LightType.Directional;sun.transform.rotation=light.sunRotation;sun.color=light.sunColor;sun.intensity=light.sunIntensity;
   camera=new GameObject("Roof QA camera").AddComponent<Camera>();camera.nearClipPlane=.2f;camera.fieldOfView=45;camera.allowMSAA=true;camera.gameObject.AddComponent<ChaseCamera>().SetSkyline(env.Plan);
  }
  void VerifyAndCapture(){
   bool baseline=Environment.GetEnvironmentVariable("STAR_RACING_CITY_ROOF_BASELINE")=="1";
   var result=new Result{baseline=baseline,towers=env.Plan.towers.Count,planHash=env.Plan.hash,farClip=camera.farClipPlane};
   var filters=env.Root.GetComponentsInChildren<MeshFilter>();
   var faces=filters.Where(f=>f.GetComponent<MeshRenderer>().enabled).SelectMany(f=>{
    var m=f.sharedMesh;var v=m.vertices;var t=m.triangles;var a=new System.Collections.Generic.List<Vector3[]>();
    for(int i=0;i<t.Length;i+=3){var p=f.transform.TransformPoint(v[t[i]]);var q=f.transform.TransformPoint(v[t[i+1]]);var r=f.transform.TransformPoint(v[t[i+2]]);if(Vector3.Cross(q-p,r-p).y>0&&Mathf.Abs(p.y-q.y)<.002f&&Mathf.Abs(p.y-r.y)<.002f)a.Add(new[]{p,q,r});}return a;
   }).ToArray();
   foreach(var tower in env.Plan.towers){
    float top=tower.position.y+tower.height*.5f;
    foreach(var offset in new[]{new Vector2(.13f,.19f),new Vector2(-.27f,.11f),new Vector2(.21f,-.32f)}){
     var point=new Vector2(tower.position.x+offset.x*tower.width,tower.position.z+offset.y*tower.width);
     int count=faces.Count(t=>Mathf.Abs(t[0].y-top)<.002f&&Inside(point,t));result.roofSamples++;
     if(count>1)result.duplicateRoofs++;
     if(!baseline&&count!=1)throw new Exception("Roof must have exactly one visible top surface; count="+count);
     result.checks++;
    }
   }
   result.emissiveRenderers=env.Root.GetComponentsInChildren<MeshRenderer>().Count(r=>r.sharedMaterial.IsKeywordEnabled("_EMISSION")&&(r.name=="City district"||r.name=="City tower"||r.name=="City tower trim"));
   if(!baseline&&result.emissiveRenderers!=0)throw new Exception("Roof beacons remain");result.checks++;
   if(env.Root.GetComponentsInChildren<Collider>().Length!=0)throw new Exception("Rendering introduced colliders");result.checks++;
   foreach(int index in new[]{0,12,env.Plan.towers.Count-1}){
    var tower=env.Plan.towers[index];var top=tower.position+Vector3.up*(tower.height*.5f);float distance=tower.width*2.1f;
    for(int frame=0;frame<5;frame++){
     camera.transform.position=top+new Vector3(distance*.7f+frame*.08f,distance*.65f,distance);camera.transform.LookAt(top);camera.GetComponent<ChaseCamera>().SetSkyline(env.Plan);
     Capture("roof-"+index+"-"+frame);
    }
   }
   result.success=true;File.WriteAllText(Path.Combine(CityRoofChecks.Output,"result.json"),JsonUtility.ToJson(result,true));Debug.Log("CITY_ROOF_OK "+JsonUtility.ToJson(result));
  }
  static bool Inside(Vector2 p,Vector3[] t){
   var a=new Vector2(t[0].x,t[0].z);var b=new Vector2(t[1].x,t[1].z);var c=new Vector2(t[2].x,t[2].z);
   float Cross(Vector2 u,Vector2 v)=>u.x*v.y-u.y*v.x;
   float x=Cross(b-a,p-a),y=Cross(c-b,p-b),z=Cross(a-c,p-c);return (x>=0&&y>=0&&z>=0)||(x<=0&&y<=0&&z<=0);
  }
  void Capture(string name){
   var rt=new RenderTexture(1280,800,24){antiAliasing=2};var image=new Texture2D(1280,800,TextureFormat.RGB24,false);var old=RenderTexture.active;
   try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,800),0,0);image.Apply();File.WriteAllBytes(Path.Combine(CityRoofChecks.Output,name+".png"),image.EncodeToPNG());}
   finally{camera.targetTexture=null;RenderTexture.active=old;rt.Release();Destroy(rt);Destroy(image);}
  }
 }
}
