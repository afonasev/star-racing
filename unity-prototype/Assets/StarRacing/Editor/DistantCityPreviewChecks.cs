using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
namespace StarRacingPrototype {
 public static class DistantCityPreviewChecks {
  public static void Run(){
   string output=Environment.GetEnvironmentVariable("STAR_RACING_DISTANT_PREVIEW");Directory.CreateDirectory(output);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   var route=new TrackRoute(Procedural.Generator.Generate(77,"full","cloud-city",true,true));var root=new GameObject("Distant preview");var env=new TrackEnvironmentBuilder();var copies=new List<Mesh>();
   var sun=new GameObject("Preview sun").AddComponent<Light>();var camera=new GameObject("Preview camera").AddComponent<Camera>();
   try{
    env.Build(route,root.transform);env.Sky.Activate();RaceLightingController.Apply(env.Plan.lighting,sun,null,null);
    camera.fieldOfView=65;camera.nearClipPlane=.2f;camera.clearFlags=CameraClearFlags.Skybox;
    var f=route.Samples[route.Samples.Length/3];var forward=Vector3.ProjectOnPlane(f.tangent,Vector3.up).normalized;
    camera.transform.position=f.position-forward*15+Vector3.up*7;camera.transform.LookAt(f.position+forward*110+Vector3.up*8);camera.gameObject.AddComponent<ChaseCamera>().SetSkyline(env.Plan);
    Capture(camera,Path.Combine(output,"baseline.png"));int removed=0;
    foreach(var filter in env.Root.GetComponentsInChildren<MeshFilter>()){
     if(filter.name!="City district"&&filter.name!="City tower trim")continue;
     var source=filter.sharedMesh;var v=source.vertices;var tris=source.triangles;var keep=new List<int>();
     for(int i=0;i<tris.Length;i+=3){int start=tris[i]/24*24;float min=float.MaxValue,max=float.MinValue;var center=Vector3.zero;
      for(int j=start;j<Mathf.Min(start+24,v.Length);j++){min=Mathf.Min(min,v[j].y);max=Mathf.Max(max,v[j].y);center+=v[j]/24;}
      bool omit=max-min<1&&Vector3.Distance(camera.transform.position,filter.transform.TransformPoint(center))>1500;
      if(omit)removed++;else{keep.Add(tris[i]);keep.Add(tris[i+1]);keep.Add(tris[i+2]);}
     }
     var clone=UnityEngine.Object.Instantiate(source);clone.triangles=keep.ToArray();filter.sharedMesh=clone;copies.Add(clone);
    }
    Capture(camera,Path.Combine(output,"distant-rings-removed.png"));
    File.WriteAllText(Path.Combine(output,"preview.json"),$"{{\"diagnosticOnly\":true,\"removedTriangles\":{removed},\"distance\":1500,\"farClip\":{camera.farClipPlane.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}");
   }finally{env.Clear();foreach(var mesh in copies)UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(camera.gameObject);UnityEngine.Object.DestroyImmediate(sun.gameObject);UnityEngine.Object.DestroyImmediate(root);}
  }
  static void Capture(Camera camera,string path){
   var rt=new RenderTexture(1280,800,24);var texture=new Texture2D(1280,800,TextureFormat.RGB24,false);var old=RenderTexture.active;
   try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,1280,800),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());}
   finally{camera.targetTexture=null;RenderTexture.active=old;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(texture);}
  }
 }
}
