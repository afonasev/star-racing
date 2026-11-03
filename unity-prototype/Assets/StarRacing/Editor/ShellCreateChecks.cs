using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
namespace StarRacingPrototype {
 // Frozen original upload oracle; exercises CreateSteps rather than BuildCore.
 public static class ShellCreateChecks {
  const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
  static readonly Type Geometry=typeof(RoadVolumeMesh).GetNestedType("Geometry",BindingFlags.NonPublic);
  static int checks,queries,hits,penetrations,penetrationHits;
  static void Require(bool value,string message){checks++;if(!value)throw new Exception(message);}
  static bool Bits(float a,float b)=>BitConverter.SingleToInt32Bits(a)==BitConverter.SingleToInt32Bits(b);
  static bool Bits(Vector3 a,Vector3 b)=>Bits(a.x,b.x)&&Bits(a.y,b.y)&&Bits(a.z,b.z);
  static void EqualMesh(Mesh a,Mesh b){
   Require(a.name==b.name&&a.indexFormat==b.indexFormat&&a.subMeshCount==b.subMeshCount,"Mesh metadata");
   Require(a.vertices.Zip(b.vertices,Bits).All(x=>x)&&a.vertexCount==b.vertexCount,"Ordered vertices");
   Require(a.normals.Length==b.normals.Length&&a.normals.Zip(b.normals,Bits).All(x=>x),"Ordered normals");
   Require(a.uv.Length==b.uv.Length&&a.uv.Zip(b.uv,(x,y)=>Bits(x.x,y.x)&&Bits(x.y,y.y)).All(x=>x),"Ordered uv");
   Require(a.triangles.SequenceEqual(b.triangles),"Ordered indices");
   Require(Bits(a.bounds.center,b.bounds.center)&&Bits(a.bounds.size,b.bounds.size),"Mesh bounds");
   for(int i=0;i<a.subMeshCount;i++){
    var x=a.GetSubMesh(i);var y=b.GetSubMesh(i);
    Require(x.indexStart==y.indexStart&&x.indexCount==y.indexCount&&x.topology==y.topology&&x.baseVertex==y.baseVertex&&x.firstVertex==y.firstVertex&&x.vertexCount==y.vertexCount&&Bits(x.bounds.center,y.bounds.center)&&Bits(x.bounds.size,y.bounds.size),"Full submesh descriptor");
   }
  }
  static GameObject Original(Transform parent,List<Vector3> v,List<Vector2> uv,List<int> indices,int bottomCount,bool graph,bool renderOnly,bool authored){
   var go=new GameObject(RoadVolumeMesh.MeshName);go.transform.SetParent(parent,false);
   var visual=new Mesh{name=RoadVolumeMesh.MeshName,indexFormat=IndexFormat.UInt32};visual.SetVertices(v);visual.SetUVs(0,uv);visual.SetTriangles(indices,0);visual.RecalculateNormals();visual.RecalculateBounds();
   if(renderOnly){go.AddComponent<MeshFilter>().sharedMesh=visual;go.AddComponent<MeshRenderer>();return go;}
   var collision=go;if(graph){collision=new GameObject("Graph shell collision");collision.transform.SetParent(go.transform,false);}
   var bottomCollider=collision.AddComponent<MeshCollider>();var exteriorCollider=collision.AddComponent<MeshCollider>();
   go.AddComponent<MeshFilter>().sharedMesh=visual;go.AddComponent<MeshRenderer>();
   var bottom=new Mesh{name=RoadVolumeMesh.BottomName,indexFormat=IndexFormat.UInt32};bottom.SetVertices(v);bottom.SetTriangles(indices.GetRange(0,bottomCount),0);bottom.RecalculateBounds();
   if(graph||(authored&&bottomCount>2097152))bottomCollider.cookingOptions&=~MeshColliderCookingOptions.UseFastMidphase;bottomCollider.sharedMesh=bottom;
   var exterior=new Mesh{name=RoadVolumeMesh.ExteriorName,indexFormat=IndexFormat.UInt32};exterior.SetVertices(v);exterior.SetTriangles(indices.GetRange(bottomCount,indices.Count-bottomCount),0);exterior.RecalculateBounds();
   if(graph||(authored&&indices.Count-bottomCount>2097152))exteriorCollider.cookingOptions&=~MeshColliderCookingOptions.UseFastMidphase;exteriorCollider.sharedMesh=exterior;
   collision.AddComponent<RoadSupportContact>().InitializeMesh(exteriorCollider,new bool[(indices.Count-bottomCount)/3]);return go;
  }
  static object Data(out List<Vector3> v,out List<Vector2> uv,out List<int> indices){
   var data=Activator.CreateInstance(Geometry,new object[]{null,0});
   v=(List<Vector3>)Geometry.GetField("vertices",Private).GetValue(data);
   uv=(List<Vector2>)Geometry.GetField("uv",Private).GetValue(data);
   indices=(List<int>)Geometry.GetField("triangles",Private).GetValue(data);
   v.AddRange(new[]{new Vector3(-2,-1,-2),new Vector3(2,-1,-2),new Vector3(-2,-1,2),new Vector3(2,-1,2),new Vector3(-2,0,-2),new Vector3(2,0,-2),new Vector3(-2,0,2),new Vector3(2,0,2),new Vector3(137,29,-63)});
   foreach(var point in v)uv.Add(new Vector2(point.x,point.z));
   indices.AddRange(new[]{0,1,2,2,1,3, 0,4,1,1,4,5, 1,5,3,3,5,7, 3,7,2,2,7,6, 2,6,0,0,6,4});
   var repair=(List<bool>)Geometry.GetField("repairable",Private).GetValue(data);for(int i=0;i<indices.Count/3;i++)repair.Add(false);return data;
  }
  static IEnumerator Steps(object data,Transform parent,bool graph,bool renderOnly,bool authored,int bottomCount=6)=>(IEnumerator)Geometry.GetMethod("CreateSteps").Invoke(data,new object[]{parent,null,bottomCount,graph,renderOnly,authored});
  static void DestroyRoot(GameObject root){
   var meshes=new HashSet<Mesh>(root.GetComponentsInChildren<MeshFilter>(true).Select(x=>x.sharedMesh));foreach(var c in root.GetComponentsInChildren<MeshCollider>(true))meshes.Add(c.sharedMesh);
   UnityEngine.Object.DestroyImmediate(root);foreach(var m in meshes)if(m!=null)UnityEngine.Object.DestroyImmediate(m);
  }
  public static void Run(){
   checks=queries=hits=penetrations=penetrationHits=0;
   foreach(bool graph in new[]{false,true})foreach(bool renderOnly in new[]{false,true})foreach(bool authored in new[]{false,true})foreach(int split in new[]{0,6,30,-1}){
    var root=new GameObject("Shell create oracle");root.transform.SetPositionAndRotation(new Vector3(17,-9,4),Quaternion.Euler(13,27,-9));root.transform.localScale=new Vector3(1.1f,.9f,1.3f);
    try{
     var data=Data(out var v,out var uv,out var indices);if(split<0)indices.Clear();int bottomCount=Math.Max(0,split);var original=Original(root.transform,v,uv,indices,bottomCount,graph,renderOnly,authored);
     using(var work=new LoadingWork(Steps(data,root.transform,graph,renderOnly,authored,bottomCount))){while(work.MoveNext()){}Require(work.Succeeded&&work.Error==null,"Synchronous drain");}
     var candidate=root.transform.GetChild(1).gameObject;EqualMesh(original.GetComponent<MeshFilter>().sharedMesh,candidate.GetComponent<MeshFilter>().sharedMesh);
     var a=original.GetComponentsInChildren<MeshCollider>();var b=candidate.GetComponentsInChildren<MeshCollider>();Require(a.Length==b.Length,"Collider count");
     for(int i=0;i<a.Length;i++){EqualMesh(a[i].sharedMesh,b[i].sharedMesh);Require(a[i].cookingOptions==b[i].cookingOptions&&a[i].hasModifiableContacts==b[i].hasModifiableContacts,"Cooking/contact flags");if(a[i].sharedMesh.triangles.Length>0)CompareQueries(a[i],b[i]);}
    }finally{DestroyRoot(root);}
   }
   // Existing ownership is unchanged: disposing the iterator stops future work;
   // its parent/world owner destroys already-published meshes and registry entries.
   foreach(int stage in new[]{1,2}){
    var root=new GameObject("Shell create cancellation");var data=Data(out _,out _,out _);
    try{using(var work=new LoadingWork(Steps(data,root.transform,true,false,false))){for(int i=0;i<stage;i++)Require(work.MoveNext(),"Missing create checkpoint");var c=root.GetComponentsInChildren<MeshCollider>();Require(c.Length==2&&(c[0].sharedMesh!=null)==(stage==2)&&c[1].sharedMesh==null,"Checkpoint mesh inventory");work.Dispose();Require(!work.MoveNext()&&!work.Succeeded,"Late CreateSteps continuation");}}
    finally{DestroyRoot(root);}
   }
   Directory.CreateDirectory(Environment.GetEnvironmentVariable("STAR_RACING_WORLD_QA_DIR"));File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("STAR_RACING_WORLD_QA_DIR"),"shell-create-contracts.json"),$"{{\"checks\":{checks},\"queries\":{queries},\"hits\":{hits},\"penetrations\":{penetrations},\"penetrationHits\":{penetrationHits}}}");
   UnityEngine.Debug.Log($"SHELL_CREATE_CONTRACTS_OK checks={checks} queries={queries} hits={hits}");
  }
  // Run after the cold timer. Rebuild ONLY the original bottom/exterior native
  // uploads from actual ordered collider inventory, preserving all unused vertices, source
  // index order, flags and exact parent/local transform. No pre-bake is involved.
  public static void CompareWorld(Component track,string output){
   checks=queries=hits=penetrations=penetrationHits=0;
   var colliders=track.GetComponentsInChildren<MeshCollider>(true).Where(c=>c.sharedMesh!=null&&(c.sharedMesh.name==RoadVolumeMesh.BottomName||c.sharedMesh.name==RoadVolumeMesh.ExteriorName)).ToArray();
   Require(colliders.Length==2,"Actual shell collider inventory");
   foreach(var candidate in colliders){
    var vertices=new List<Vector3>(candidate.sharedMesh.vertices);
    var triangles=new List<int>(colliders.Single(c=>c.sharedMesh.name==RoadVolumeMesh.BottomName).sharedMesh.triangles);
    triangles.AddRange(colliders.Single(c=>c.sharedMesh.name==RoadVolumeMesh.ExteriorName).sharedMesh.triangles);
    int bottomCount=colliders.Single(c=>c.sharedMesh.name==RoadVolumeMesh.BottomName).sharedMesh.triangles.Length;
    var original=new Mesh{name=candidate.sharedMesh.name,indexFormat=IndexFormat.UInt32};var go=new GameObject("Original shell native control");
    try{
     original.SetVertices(vertices);original.SetTriangles(candidate.sharedMesh.name==RoadVolumeMesh.BottomName?triangles.GetRange(0,bottomCount):triangles.GetRange(bottomCount,triangles.Count-bottomCount),0);original.RecalculateBounds();
     go.transform.SetParent(candidate.transform.parent,false);go.transform.localPosition=candidate.transform.localPosition;go.transform.localRotation=candidate.transform.localRotation;go.transform.localScale=candidate.transform.localScale;
     var control=go.AddComponent<MeshCollider>();control.cookingOptions=candidate.cookingOptions;control.sharedMesh=original;
     EqualMesh(original,candidate.sharedMesh);CompareQueries(control,candidate);

    }finally{UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(original);}
   }
   File.WriteAllText(Path.Combine(output,"shell-native-parity.json"),$"{{\"checks\":{checks},\"queries\":{queries},\"hits\":{hits},\"penetrations\":{penetrations},\"penetrationHits\":{penetrationHits}}}");
   UnityEngine.Debug.Log($"SHELL_NATIVE_PARITY_OK checks={checks} queries={queries} hits={hits}");
  }
  public static void CompareQueries(MeshCollider a,MeshCollider b){
   var v=a.sharedMesh.vertices;var indices=a.sharedMesh.triangles;int count=indices.Length/3;int step=Math.Max(1,count/1024);int localHits=0;
   var probe=new GameObject("Shell penetration probe");var sphere=probe.AddComponent<SphereCollider>();sphere.radius=.5f;
   try{
   for(int triangle=0;triangle<count;triangle+=step){var x=v[indices[triangle*3]];var y=v[indices[triangle*3+1]];var z=v[indices[triangle*3+2]];var normal=Vector3.Cross(y-x,z-x).normalized;if(normal.sqrMagnitude==0)continue;
    foreach(var weights in new[]{new Vector3(.34f,.33f,.33f),new Vector3(.98f,.01f,.01f),new Vector3(.01f,.49f,.5f)}){
     var point=x*weights.x+y*weights.y+z*weights.z;var n=a.transform.TransformDirection(normal);var p=a.transform.TransformPoint(point);var ray=new Ray(p+n*2,-n);
     bool expected=a.Raycast(ray,out var e,4),actual=b.Raycast(ray,out var h,4);queries++;
     Require(expected==actual&&(!expected||Bits(e.distance,h.distance)&&Bits(e.point,h.point)&&Bits(e.normal,h.normal)&&Bits(e.barycentricCoordinate,h.barycentricCoordinate)&&e.triangleIndex==h.triangleIndex),"Native shell raycast parity");
     if(expected){hits++;localHits++;}
     var center=p+n*.05f;
     bool sourceOverlap=Physics.ComputePenetration(sphere,center,Quaternion.identity,a,a.transform.position,a.transform.rotation,out var sourceDirection,out float sourceDistance);
     bool candidateOverlap=Physics.ComputePenetration(sphere,center,Quaternion.identity,b,b.transform.position,b.transform.rotation,out var candidateDirection,out float candidateDistance);
     penetrations++;if(sourceOverlap)penetrationHits++;
     Require(sourceOverlap==candidateOverlap&&(!sourceOverlap||Bits(sourceDirection,candidateDirection)&&Bits(sourceDistance,candidateDistance)),"Native shell penetration parity");
    }
   }
   Require(localHits>0,"Missing native shell hits");
   }finally{UnityEngine.Object.DestroyImmediate(probe);}
  }
 }
}
