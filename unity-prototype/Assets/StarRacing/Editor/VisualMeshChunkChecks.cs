using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
namespace StarRacingPrototype {
 public static class VisualMeshChunkChecks {
  static int checks;
  static void Check(bool value,string message){checks++;if(!value)throw new Exception("VISUAL_CHUNKS: "+message);}
  public static void RunAndPreview(){Run();DistantCityPreviewChecks.Run();}
  public static void Run(){
   string output=Environment.GetEnvironmentVariable("STAR_RACING_CHUNK_QA");Directory.CreateDirectory(output);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);checks=0;
   var root=new GameObject("Exact render chunks fixture");var builder=root.AddComponent<TrackBuilder>();var env=new TrackEnvironmentBuilder();int sources=0,parts=0,total=0;
   try{
    builder.Build(new TrackRoute(Procedural.Generator.Generate(77,"full","cloud-city",true,true)));
    env.Build(builder.PresentationRoute,builder.PresentationRoot);
    foreach(var filter in root.GetComponentsInChildren<MeshFilter>().Concat(env.Root.GetComponentsInChildren<MeshFilter>())){
     var mesh=filter.sharedMesh;if(mesh==null||!(mesh.name=="Environment road visual"||mesh.name=="Environment rail visual"||mesh.name==RoadVolumeMesh.MeshName))continue;
     var beforeVertices=mesh.vertices;var beforeIndices=mesh.triangles;var chunks=VisualMeshChunks.Create(mesh);sources++;parts+=chunks.Count;total+=beforeIndices.Length/3;
     Verify(mesh,chunks);
     Check(mesh.vertices.SequenceEqual(beforeVertices)&&mesh.triangles.SequenceEqual(beforeIndices),"source unchanged");
     foreach(var chunk in chunks)UnityEngine.Object.DestroyImmediate(chunk);
    }
    Check(sources==3,"road, rails and shell verified");
    File.WriteAllText(Path.Combine(output,"exact.json"),$"{{\"success\":true,\"checks\":{checks},\"sources\":{sources},\"chunks\":{parts},\"triangles\":{total}}}");
    Debug.Log("VISUAL_CHUNKS_EXACT_OK checks="+checks);
   }finally{env.Clear();UnityEngine.Object.DestroyImmediate(root);}
  }
  public static void RunAllShipping(){RunShipping();CloudCityChecks.RunGeometry();}
  public static void RunShipping(){
   string output=Environment.GetEnvironmentVariable("STAR_RACING_CHUNK_QA");Directory.CreateDirectory(output);checks=0;
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   foreach(bool native in new[]{false,true}){
    var root=new GameObject("Shipping visual fixture");var builder=root.AddComponent<TrackBuilder>();var env=new TrackEnvironmentBuilder{PartitionRoadVisuals=false};var references=new List<Mesh>();
    try{
     if(native)builder.BuildNativeCourse(NativeTrackGenerator.Generate(211933141,new Procedural.GraphCourseSettings("cloud-city","normal",true),12));
     else builder.Build(new TrackRoute(Procedural.Generator.Generate(77,"full","cloud-city",true,true)));
     var colliders=root.GetComponentsInChildren<Collider>();var physical=root.GetComponentsInChildren<MeshFilter>();var physicalMeshes=physical.Select(f=>f.sharedMesh).ToArray();var physicalVertices=physicalMeshes.Select(m=>m.vertices).ToArray();var physicalTriangles=physicalMeshes.Select(m=>m.triangles).ToArray();
     env.Build(builder.PresentationRoute,builder.PresentationRoot);
     foreach(var f in env.Root.GetComponentsInChildren<MeshFilter>())if(f.sharedMesh.name=="Environment road visual"||f.sharedMesh.name=="Environment rail visual")references.Add(UnityEngine.Object.Instantiate(f.sharedMesh));
     var shell=builder.PresentationRoot.GetComponentsInChildren<MeshFilter>().First(f=>f.sharedMesh.name==RoadVolumeMesh.MeshName);var shellRenderer=shell.GetComponent<MeshRenderer>();bool originalEnabled=shellRenderer.enabled;references.Add(UnityEngine.Object.Instantiate(shell.sharedMesh));
     string hash=env.Plan.hash;env.Clear();env.PartitionRoadVisuals=true;env.Build(builder.PresentationRoute,builder.PresentationRoot);
     Check(env.Plan.hash==hash,"same environment identity");Check(root.GetComponentsInChildren<Collider>().SequenceEqual(colliders)&&env.Root.GetComponentsInChildren<Collider>().Length==0,"same collider instances");
     for(int i=0;i<physical.Length;i++)Check(physical[i].sharedMesh==physicalMeshes[i]&&physicalMeshes[i].vertices.SequenceEqual(physicalVertices[i])&&physicalMeshes[i].triangles.SequenceEqual(physicalTriangles[i]),"untouched physical/source mesh");
     var parts=env.Root.GetComponentsInChildren<MeshFilter>();
     foreach(var reference in references){string original=reference.name.Replace("(Clone)","");var selected=parts.Where(f=>f.sharedMesh.name==original+" section").ToArray();Check(selected.Length>1,"partition published "+original);Verify(reference,selected.Select(f=>f.sharedMesh).ToList());}
     Check(!shellRenderer.enabled,"original shell hidden");var owned=parts.Select(f=>f.sharedMesh).Distinct().ToArray();
     env.Clear();Check(shellRenderer.enabled==originalEnabled,"shell renderer restored");Check(owned.All(m=>m==null),"owned section meshes disposed");
    }finally{env.Clear();foreach(var mesh in references)UnityEngine.Object.DestroyImmediate(mesh);UnityEngine.Object.DestroyImmediate(root);}
   }
   File.WriteAllText(Path.Combine(output,"shipping.json"),$"{{\"success\":true,\"checks\":{checks},\"cases\":2}}");Debug.Log("VISUAL_CHUNKS_SHIPPING_OK checks="+checks);
  }
  static void Verify(Mesh source,List<Mesh> chunks){
   var identities=new Dictionary<(Vector3,Vector3,Vector2,Vector4),int>();
   var counts=new Dictionary<(int,int,int),int>();
   int[] Ids(Mesh mesh){var v=mesh.vertices;var n=mesh.normals;var uv=mesh.uv;var t=mesh.tangents;var ids=new int[v.Length];
    Check(n.Length==0||n.Length==v.Length,"normals cardinality");Check(uv.Length==0||uv.Length==v.Length,"uv cardinality");Check(t.Length==0||t.Length==v.Length,"tangent cardinality");
    for(int i=0;i<v.Length;i++){var key=(v[i],n.Length==0?Vector3.zero:n[i],uv.Length==0?Vector2.zero:uv[i],t.Length==0?Vector4.zero:t[i]);if(!identities.TryGetValue(key,out var id)){id=identities.Count;identities.Add(key,id);}ids[i]=id;}return ids;}
   var original=Ids(source);var triangles=source.triangles;
   for(int i=0;i<triangles.Length;i+=3){var key=(original[triangles[i]],original[triangles[i+1]],original[triangles[i+2]]);counts.TryGetValue(key,out int count);counts[key]=count+1;}
   foreach(var chunk in chunks){
    Check(chunk.subMeshCount==1,"one material per original visual");Check(chunk.normals.Length==0== (source.normals.Length==0),"normal stream retained");Check(chunk.tangents.Length==0== (source.tangents.Length==0),"tangent stream retained");
    var ids=Ids(chunk);var t=chunk.triangles;var v=chunk.vertices;
    for(int i=0;i<t.Length;i+=3){var key=(ids[t[i]],ids[t[i+1]],ids[t[i+2]]);Check(counts.TryGetValue(key,out int count)&&count>0,"exact oriented triangle position/normal/UV/tangent multiset");counts[key]=count-1;}
    foreach(var point in v)Check(chunk.bounds.Contains(point),"tight bounds contain every vertex");
   }
   Check(counts.Values.All(x=>x==0),"no lost triangles");
  }
 }
}
