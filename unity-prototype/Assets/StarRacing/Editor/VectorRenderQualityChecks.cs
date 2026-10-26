using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
namespace StarRacingPrototype {
 public static class VectorRenderQualityChecks {
  static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_VEHICLE_RENDER_DIR");
  [Serializable] sealed class Comparison {public string view;public double meanByteError,largeDifferenceFraction;public int maxByteError,referenceSubmeshes,optimizedSubmeshes;}
  [Serializable] sealed class Report {public bool success,actualPlayMode;public Comparison[] comparisons;}
  [Serializable] sealed class Timing {public int view,repeat;public string arm;public double medianMs,p95Ms;public string scope="Editor single-car render submission plus synchronous pixel fence; not gameplay FPS";}
  [Serializable] sealed class Timings {public Timing[] windows;}
  public static void Run(){
   var root=new GameObject("Vector quality fixture");var rt=new RenderTexture(1600,900,24);var pixels=new Texture2D(1600,900,TextureFormat.RGB24,false);Mesh reference=null;Material floorMaterial=null;
   var reports=new List<Comparison>();var windows=new List<Timing>();
   try{
    rt.Create();var visual=StaticVehicleVisual.Create(root.transform,new Color(.98f,.64f,.025f),true);var renderer=visual.CanonicalRoot.GetComponentInChildren<MeshRenderer>();var filter=renderer.GetComponent<MeshFilter>();var canonical=filter.sharedMesh;var material=renderer.sharedMaterial;
    reference=Object.Instantiate(canonical);reference.name="Unbatched identical surface reference";
    var uv=new List<Vector2>[5];for(int c=0;c<5;c++){uv[c]=new List<Vector2>();canonical.GetUVs(c,uv[c]);}
    var groups=new Dictionary<string,List<int>>();var indices=canonical.triangles;
    for(int t=0;t<indices.Length;t+=3){int v=indices[t];string key=string.Join("|",Enumerable.Range(0,5).Select(c=>uv[c][v].x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+uv[c][v].y.ToString("R",System.Globalization.CultureInfo.InvariantCulture)));if(!groups.TryGetValue(key,out var list))groups[key]=list=new List<int>();list.Add(indices[t]);list.Add(indices[t+1]);list.Add(indices[t+2]);}
    if(groups.Count!=14)throw new Exception("Vector material surface payload lost");reference.subMeshCount=groups.Count;int sub=0;foreach(var list in groups.Values)reference.SetTriangles(list,sub++,false);
    var sun=new GameObject("Sun").AddComponent<Light>();sun.transform.SetParent(root.transform);sun.type=LightType.Directional;sun.intensity=1.5f;sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(35,-35,0);RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.3f,.35f,.4f);
    var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.transform.SetParent(root.transform);floor.transform.position=Vector3.down*.23f;floor.transform.localScale=Vector3.one*100;Object.DestroyImmediate(floor.GetComponent<Collider>());floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMaterial.SetColor("_BaseColor",new Color(.12f,.15f,.18f));floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;
    var cam=new GameObject("Vector proof camera").AddComponent<Camera>();cam.transform.SetParent(root.transform);cam.enabled=false;cam.fieldOfView=50;cam.aspect=16f/9;cam.nearClipPlane=.1f;cam.farClipPlane=1000;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.025f,.035f,.05f);cam.GetUniversalAdditionalCameraData().renderPostProcessing=true;
    var views=new[]{new Vector3(5,2.5f,7),new Vector3(5,2.5f,-7),new Vector3(7,1.5f,0)};
    for(int view=0;view<views.Length;view++){
     cam.transform.position=views[view];cam.transform.LookAt(new Vector3(0,.35f,0));filter.sharedMesh=reference;renderer.sharedMaterials=Enumerable.Repeat(material,groups.Count).ToArray();for(int warm=0;warm<4;warm++)Capture(cam,rt,pixels);var before=pixels.GetPixels32();File.WriteAllBytes(Path.Combine(Output,$"reference-{view}.png"),pixels.EncodeToPNG());
     filter.sharedMesh=canonical;renderer.sharedMaterials=new[]{material};for(int warm=0;warm<4;warm++)Capture(cam,rt,pixels);var after=pixels.GetPixels32();File.WriteAllBytes(Path.Combine(Output,$"optimized-{view}.png"),pixels.EncodeToPNG());
     long sum=0;int max=0,large=0;for(int i=0;i<before.Length;i++){int r=Math.Abs(before[i].r-after[i].r),g=Math.Abs(before[i].g-after[i].g),b=Math.Abs(before[i].b-after[i].b);sum+=r+g+b;int d=Math.Max(r,Math.Max(g,b));max=Math.Max(max,d);if(d>8)large++;}
     var result=new Comparison{view=view.ToString(),meanByteError=sum/(double)(before.Length*3),largeDifferenceFraction=large/(double)before.Length,maxByteError=max,referenceSubmeshes=groups.Count,optimizedSubmeshes=1};reports.Add(result);
     File.WriteAllText(Path.Combine(Output,"quality.json"),JsonUtility.ToJson(new Report{success=false,actualPlayMode=Application.isPlaying,comparisons=reports.ToArray()},true));
     if(result.meanByteError>.2||result.largeDifferenceFraction>.001)throw new Exception("Vector combined/reference render mismatch view="+view+" mean="+result.meanByteError+" fraction="+result.largeDifferenceFraction);
     if(Environment.GetEnvironmentVariable("STAR_RACING_VECTOR_TIMING")=="1"){
      for(int repeat=0;repeat<3;repeat++)foreach(string arm in new[]{"reference","optimized","reference-return"}){
       bool combined=arm=="optimized";filter.sharedMesh=combined?canonical:reference;renderer.sharedMaterials=combined?new[]{material}:Enumerable.Repeat(material,groups.Count).ToArray();
       for(int warm=0;warm<8;warm++)Capture(cam,rt,pixels);
       var samples=new double[24];for(int f=0;f<samples.Length;f++){var clock=Stopwatch.StartNew();Capture(cam,rt,pixels);samples[f]=clock.Elapsed.TotalMilliseconds;}Array.Sort(samples);
       windows.Add(new Timing{view=view,repeat=repeat,arm=arm,medianMs=samples[12],p95Ms=samples[22]});
       File.WriteAllText(Path.Combine(Output,"render-timings.json"),JsonUtility.ToJson(new Timings{windows=windows.ToArray()},true));
      }
      filter.sharedMesh=canonical;renderer.sharedMaterials=new[]{material};
     }
    }
    // Actual independent camera views and shared geometry, with four entrant colors.
    var colors=new[]{Color.red,Color.cyan,Color.green,new Color(.7f,.2f,1)};for(int i=0;i<4;i++){var car=new GameObject("Rival "+i);car.transform.SetParent(root.transform);car.transform.position=new Vector3((i%2==0?-1:1)*3.1f,0,4+i*4);StaticVehicleVisual.Create(car.transform,colors[i],false);}
    cam.fieldOfView=65;cam.transform.position=new Vector3(0,5.9f,-10);cam.transform.LookAt(new Vector3(0,1.1f,9));Capture(cam,rt,pixels);File.WriteAllBytes(Path.Combine(Output,"chase-field.png"),pixels.EncodeToPNG());
    for(int seat=0;seat<4;seat++){cam.transform.position=new Vector3((seat%2==0?-1:1)*3.1f,3,-6+seat*4);cam.transform.LookAt(new Vector3(0,.5f,8+seat*4));Capture(cam,rt,pixels);File.WriteAllBytes(Path.Combine(Output,$"viewport-{seat}.png"),pixels.EncodeToPNG());}
    File.WriteAllText(Path.Combine(Output,"quality.json"),JsonUtility.ToJson(new Report{success=true,actualPlayMode=Application.isPlaying,comparisons=reports.ToArray()},true));UnityEngine.Debug.Log("VECTOR_RENDER_QUALITY_OK");
   }finally{rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(pixels);Object.DestroyImmediate(root);if(reference!=null)Object.DestroyImmediate(reference);if(floorMaterial!=null)Object.DestroyImmediate(floorMaterial);}
  }
  static void Capture(Camera camera,RenderTexture rt,Texture2D pixels){var old=RenderTexture.active;try{RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0,false);pixels.Apply(false);}finally{RenderTexture.active=old;}}
 }
}
