using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
namespace StarRacingPrototype {
 // One editor-only1024m candidate. Runtime is wired only if this control passes.
 [InitializeOnLoad] public static class CityVisibilityControl {
  const string Key="StarRacing.CityVisibilityControl";static int frames;
  static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_CITY_CONTROL_DIR");
  static bool Timing=>Environment.GetEnvironmentVariable("STAR_RACING_CITY_CONTROL_TIMING")=="1";
  static CityVisibilityControl(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){
   if(string.IsNullOrEmpty(Output)||Directory.Exists(Output)||!Path.IsPathRooted(Output))throw new Exception("Fresh absolute city control output required");
   Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",true);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.isPlaying=true;
  }
  static void Changed(PlayModeStateChange state){if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){frames=0;EditorApplication.update+=Enter;}
   if(state==PlayModeStateChange.EnteredEditMode){EditorApplication.update-=Enter;SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",true)?1:0);}
  }
  static void Enter(){EditorApplication.QueuePlayerLoopUpdate();if(++frames<4)return;EditorApplication.update-=Enter;Render();}
  [Serializable] sealed class Window {public int views,arm,samples;public bool candidate;public double meanMs,p95Ms;}
  [Serializable] sealed class Difference {public int views,maxByteError;public double meanByteError,largeDifferenceFraction;}
  [Serializable] sealed class Report {
   public bool success,timing,actualPlayMode;public int entrants=16,sourceCityRenderers,candidateCityRenderers,sourceDistricts,candidateDistricts,splitDistricts,fallbackDistricts,colliders;
   public long sourceMeshBytes,candidateMeshBytes;public double candidateCreationMs;public string planHash;
   public string limitation="Frozen Editor city render with16cars,4spatially-separated human camera poses, one1024m candidate, identical synchronous readback fence. Not gameplay FPS or pure GPU timing. Timing threshold is diagnostic.";
   public Window[] windows;public Difference[] images;
  }
  sealed class Group {public MeshRenderer source;public bool enabled;public readonly List<GameObject> parts=new List<GameObject>();}
  static string Corner(Vector3 p,Vector3 n,Vector2 uv){var bytes=new byte[32];var values=new[]{p.x,p.y,p.z,n.x,n.y,n.z,uv.x,uv.y};for(int i=0;i<8;i++){int b=BitConverter.SingleToInt32Bits(values[i]);for(int j=0;j<4;j++)bytes[i*4+j]=(byte)(b>>(8*j));}return Convert.ToBase64String(bytes);}
  static Dictionary<string,int> Triangles(IEnumerable<Mesh> meshes){
   var result=new Dictionary<string,int>();foreach(var mesh in meshes){var p=mesh.vertices;var n=mesh.normals;var uv=mesh.uv;var key=new string[p.Length];for(int i=0;i<p.Length;i++)key[i]=Corner(p[i],n[i],uv[i]);var indices=mesh.triangles;
    for(int i=0;i<indices.Length;i+=3){string a=key[indices[i]],b=key[indices[i+1]],c=key[indices[i+2]];string s0=a+b+c,s1=b+c+a,s2=c+a+b;string k=string.CompareOrdinal(s0,s1)<0?s0:s1;if(string.CompareOrdinal(s2,k)<0)k=s2;result.TryGetValue(k,out int copies);result[k]=copies+1;}
   }return result;
  }
  static long Bytes(Mesh m)=>Enumerable.Range(0,m.vertexBufferCount).Sum(i=>(long)m.GetVertexBufferStride(i)*m.vertexCount)+(long)m.GetIndexCount(0)*(m.indexFormat==IndexFormat.UInt16?2:4);
  static void Render(){
   UnityEngine.Debug.Log("CITY_VISIBILITY_CONTROL_BEGIN");var root=new GameObject("Frozen city visibility control");var environment=new TrackEnvironmentBuilder();var groups=new List<Group>();var parts=new List<Mesh>();
   var rt=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);var pixels=new Texture2D(1600,900,TextureFormat.RGB24,false);var oldRT=RenderTexture.active;var mode=Physics.simulationMode;float scale=Time.timeScale;
   var report=new Report{timing=Timing,actualPlayMode=Application.isPlaying};var windows=new List<Window>();var images=new List<Difference>();VolumeProfile profile=null;
   try{
    Time.timeScale=0;Physics.simulationMode=SimulationMode.Script;rt.Create();
    var trackObject=new GameObject("Track");trackObject.transform.SetParent(root.transform,false);var track=trackObject.AddComponent<TrackBuilder>();track.Build(new TrackRoute(Procedural.Generator.Generate(77,"normal","cloud-city",false,true)));
    environment.Build(track.Route,track.transform,77);environment.Root.transform.SetParent(root.transform,false);environment.Sky.Activate();report.planHash=environment.Plan.hash;
    var sun=new GameObject("Sun").AddComponent<Light>();sun.transform.SetParent(root.transform);sun.type=LightType.Directional;var fill=new GameObject("Fill").AddComponent<Light>();fill.transform.SetParent(root.transform);fill.type=LightType.Directional;
    var volume=new GameObject("Volume").AddComponent<Volume>();volume.transform.SetParent(root.transform);volume.isGlobal=true;profile=ScriptableObject.CreateInstance<VolumeProfile>();volume.profile=profile;var bloom=profile.Add<Bloom>();bloom.intensity.Override(.25f);bloom.threshold.Override(1.1f);profile.Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
    var cameras=new ChaseCamera[4];var balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);var positions=new[]{160f,track.Route.Length*.25f,track.Route.Length*.50f,track.Route.Length*.75f};
    for(int i=0;i<16;i++){
     var go=new GameObject("Frozen racer "+i);go.transform.SetParent(root.transform,false);var car=go.AddComponent<MagneticVehicle>();car.Initialize(track,i,Color.HSVToRGB(i*.618034f%1,.65f,.95f),balance,i<4);car.ResetAt(positions[i%4]+(i/4)*9,0);car.Hold(true);car.enabled=false;
     if(i>=4)continue;var camera=new GameObject("Camera "+i).AddComponent<Camera>();camera.transform.SetParent(root.transform);camera.enabled=false;camera.fieldOfView=65;camera.nearClipPlane=.2f;camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;var chase=camera.gameObject.AddComponent<ChaseCamera>();chase.target=car;chase.enabled=false;cameras[i]=chase;
    }
    RaceLightingController.Apply(environment.Plan.lighting,sun,fill,cameras);foreach(var c in cameras)c.SetSkyline(environment.Plan);
    report.colliders=root.GetComponentsInChildren<Collider>(true).Length;
    var allCity=environment.Root.GetComponentsInChildren<MeshRenderer>().Where(r=>r.name=="City district"||r.name=="City tower"||r.name=="City tower trim").ToArray();var sources=allCity.Where(r=>r.name=="City district").ToArray();report.sourceCityRenderers=allCity.Length;report.sourceDistricts=sources.Length;
    int published=0;int maximum=192-(allCity.Length-sources.Length);var clock=Stopwatch.StartNew();
    for(int at=0;at<sources.Length;at++){
     var source=sources[at];var original=source.GetComponent<MeshFilter>().sharedMesh;report.sourceMeshBytes+=Bytes(original);var meshes=VisualMeshChunks.Create(original,1024);
     if(meshes.Count<=1||published+meshes.Count+(sources.Length-at-1)>maximum){foreach(var m in meshes)Object.DestroyImmediate(m);report.candidateMeshBytes+=Bytes(original);published++;report.fallbackDistricts++;continue;}
     var expected=Triangles(new[]{original});var actual=Triangles(meshes);if(expected.Count!=actual.Count||expected.Any(p=>!actual.TryGetValue(p.Key,out int count)||count!=p.Value))throw new Exception("City oriented position/normal/UV triangle equivalence");
     var group=new Group{source=source,enabled=source.enabled};groups.Add(group);report.splitDistricts++;
     foreach(var mesh in meshes){foreach(var vertex in mesh.vertices)if(!mesh.bounds.Contains(vertex))throw new Exception("Conservative city section bounds");parts.Add(mesh);report.candidateMeshBytes+=Bytes(mesh);
      var go=new GameObject("City section control");go.transform.SetParent(source.transform.parent,false);go.transform.localPosition=source.transform.localPosition;go.transform.localRotation=source.transform.localRotation;go.transform.localScale=source.transform.localScale;go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=source.sharedMaterial;r.shadowCastingMode=source.shadowCastingMode;r.receiveShadows=source.receiveShadows;r.lightProbeUsage=source.lightProbeUsage;r.reflectionProbeUsage=source.reflectionProbeUsage;r.enabled=false;group.parts.Add(go);
     }published+=meshes.Count;
    }
    report.candidateCreationMs=clock.Elapsed.TotalMilliseconds;report.candidateDistricts=published;report.candidateCityRenderers=allCity.Length-sources.Length+published;if(report.candidateCityRenderers>192)throw new Exception("Original192city renderer cap exceeded");
    void Toggle(bool candidate){foreach(var g in groups){g.source.enabled=candidate?false:g.enabled;foreach(var go in g.parts)go.GetComponent<Renderer>().enabled=candidate;}}
    void Capture(int views){for(int i=0;i<views;i++)RenderPipeline.SubmitRenderRequest(cameras[i].GetComponent<Camera>(),new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,1600,900),0,0,false);pixels.Apply(false);}
    var tick=typeof(ChaseCamera).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
    foreach(int views in new[]{1,4}){
     for(int i=0;i<views;i++){var cam=cameras[i].GetComponent<Camera>();cam.rect=RaceViewports.For(views,i);cam.aspect=1600f*cam.rect.width/(900f*cam.rect.height);cameras[i].Snap();tick.Invoke(cameras[i],null);}
     Color32[] before=null;foreach(bool candidate in new[]{false,true}){
      Toggle(candidate);for(int warm=0;warm<6;warm++)Capture(views);var image=pixels.GetPixels32();File.WriteAllBytes(Path.Combine(Output,$"city-{views}-"+(candidate?"candidate":"baseline")+".png"),pixels.EncodeToPNG());if(!candidate){before=image;continue;}
      long sum=0;int max=0,large=0;for(int i=0;i<image.Length;i++){int r=Math.Abs(before[i].r-image[i].r),g=Math.Abs(before[i].g-image[i].g),b=Math.Abs(before[i].b-image[i].b);sum+=r+g+b;int d=Math.Max(r,Math.Max(g,b));max=Math.Max(max,d);if(d>8)large++;}var diff=new Difference{views=views,meanByteError=sum/(double)(image.Length*3),maxByteError=max,largeDifferenceFraction=large/(double)image.Length};images.Add(diff);if(diff.meanByteError>.2||diff.largeDifferenceFraction>.001)throw new Exception("City culling image changed views="+views+" mean="+diff.meanByteError+" fraction="+diff.largeDifferenceFraction);
     }
     if(Timing)for(int arm=0;arm<4;arm++){
      bool candidate=arm==1||arm==2;Toggle(candidate);for(int warm=0;warm<6;warm++)Capture(views);var samples=new List<double>(1024);long steps=RacePhysicsStepper.NativeSteps;var window=Stopwatch.StartNew();
      while(window.Elapsed.TotalSeconds<5){long began=Stopwatch.GetTimestamp();Capture(views);samples.Add((Stopwatch.GetTimestamp()-began)*1000d/Stopwatch.Frequency);}
      if(RacePhysicsStepper.NativeSteps!=steps||root.GetComponentsInChildren<Collider>(true).Length!=report.colliders)throw new Exception("Frozen city physical inventory/steps changed");var sorted=samples.OrderBy(x=>x).ToArray();windows.Add(new Window{views=views,arm=arm,candidate=candidate,samples=samples.Count,meanMs=samples.Average(),p95Ms=sorted[(int)Math.Ceiling(sorted.Length*.95)-1]});
     }
    }
    Toggle(false);if(groups.Any(g=>g.source.enabled!=g.enabled))throw new Exception("City renderer restoration");report.success=true;SessionState.SetBool(Key+"Failed",false);UnityEngine.Debug.Log("CITY_VISIBILITY_CONTROL_OK timing="+Timing);
   }catch(Exception e){File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());UnityEngine.Debug.LogException(e);}
   finally{report.windows=windows.ToArray();report.images=images.ToArray();File.WriteAllText(Path.Combine(Output,"city-control.json"),JsonUtility.ToJson(report,true));Physics.simulationMode=mode;Time.timeScale=scale;RenderTexture.active=oldRT;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(pixels);environment.Clear();Object.DestroyImmediate(root);foreach(var m in parts)Object.DestroyImmediate(m);if(profile!=null)Object.DestroyImmediate(profile);EditorApplication.isPlaying=false;}
  }
 }
}
