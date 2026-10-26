using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;
namespace StarRacingPrototype {
 public static class VectorRuntimeChecks {
  static int assertions;
  static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_VEHICLE_RENDER_DIR");
  static void Check(bool ok,string why){assertions++;if(!ok)throw new Exception("VECTOR "+why);}
  public static void PrepareAndCheck(){VectorAssetImport.Prepare();Run();PrototypeBuilder.Prepare();}
  public static void Run(){
   assertions=0;Directory.CreateDirectory(Output);VisualContracts();CheckPayload();
   PhysicalContactChecks.CheckBakedProfile();StrideVisualChecks.RunLegacy();CloudlineVehicleChecks.Run();VehiclePresentationChecks.Run();
   int Cache(string name)=>(int)typeof(SharedRaceAssets).GetProperty(name,BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
   using(SharedRaceAssets.Acquire()){
    var steps=SharedRaceAssets.EnsureSteps(false);while(steps.MoveNext()){}var p=SharedRaceAssets.Load<GameObject>("Vehicle/Vector");var m=SharedRaceAssets.Load<Material>("Vehicle/VectorPacked");Check(p!=null&&m!=null,"shared cache prewarm");
    Check(Cache("LoadCount")==36&&Cache("RetainedCount")==36,"36 explicit shared source entries, no old visual LOD/material loads");
    var parent=new GameObject("cached rebuild");StaticVehicleVisual.Create(parent.transform,Color.cyan,false);Object.DestroyImmediate(parent);
    Check(Cache("LoadCount")==36&&p==SharedRaceAssets.Load<GameObject>("Vehicle/Vector")&&m==SharedRaceAssets.Load<Material>("Vehicle/VectorPacked"),"rebuild retains sources without duplicate loads");
   }
   Check(Cache("RetainedCount")==0,"last cache owner releases imported references");
   var mat=AssetDatabase.LoadAssetAtPath<Material>("Assets/StarRacing/Resources/Vehicle/VectorPacked.mat");Check(!ShaderUtil.ShaderHasError(mat.shader),"shader compiler errors");
   File.WriteAllText(Path.Combine(Output,"contracts.json"),"{\"success\":true,\"assertions\":"+assertions+",\"bodyRenderers\":1,\"materials\":1,\"triangles\":79318,\"collisionProfileUnchanged\":true}");
   Debug.Log("VECTOR_RUNTIME_CHECKS_OK assertions="+assertions);
  }
  public static void VisualContracts(){
   var a=new GameObject("Vector red");var b=new GameObject("Vector blue rotated");
   try{
    b.transform.SetPositionAndRotation(new Vector3(12,7,-9),Quaternion.Euler(15,73,4));var red=StaticVehicleVisual.Create(a.transform,Color.red,true);var blue=StaticVehicleVisual.Create(b.transform,Color.blue,false);
    var ar=red.CanonicalRoot.GetComponentsInChildren<MeshRenderer>();var br=blue.CanonicalRoot.GetComponentsInChildren<MeshRenderer>();Check(ar.Length==1&&br.Length==1,"one baked renderer");var mesh=ar[0].GetComponent<MeshFilter>().sharedMesh;
    Check(mesh.subMeshCount==1&&mesh.GetIndexCount(0)==79318*3,"all authored triangles in one submesh");Check(!mesh.isReadable,"runtime CPU mesh copy disabled");
    Check(mesh==br[0].GetComponent<MeshFilter>().sharedMesh&&ar[0].sharedMaterial==br[0].sharedMaterial,"shared geometry/material");Check(ar[0].sharedMaterial.enableInstancing&&ar[0].sharedMaterial.shader.name=="StarRacing/VectorPacked","instanced packed PBR");
    Check(red.GetComponentsInChildren<Collider>().Length==0&&red.GetComponentsInChildren<Rigidbody>().Length==0,"visual has no physics");
    Check(Mathf.Abs(red.LocalBounds.min.y-VehicleGeometry.ChassisMinY)<.001f,"attachment clearance");Check(red.LocalBounds.size.x<=VehicleGeometry.Width+.001f&&Mathf.Abs(red.LocalBounds.size.z-VehicleGeometry.Length)<.001f,"fit");Check(red.LocalBounds.size.y>1&&red.LocalBounds.size.y<1.3f,"up axis");
    Check(Vector3.Distance(red.LocalBounds.center,blue.LocalBounds.center)<.001f&&Vector3.Distance(red.LocalBounds.size,blue.LocalBounds.size)<.001f,"parent-independent fit");
    var block=new MaterialPropertyBlock();ar[0].GetPropertyBlock(block);Check(block.GetColor("_BaseColor")==Color.red,"red paint");br[0].GetPropertyBlock(block);Check(block.GetColor("_BaseColor")==Color.blue,"independent blue paint");
    Check(red.DetailGroup.GetLODs().Length==1&&red.DetailGroup.GetLODs()[0].screenRelativeTransitionHeight==0&&red.DetailGroup.fadeMode==LODFadeMode.None,"full quality at all distances; no doubled crossfade");
    var anchors=red.GetComponentsInChildren<Transform>().Where(t=>t.name=="Exhaust_L"||t.name=="Exhaust_R").ToArray();Check(anchors.Length==2&&anchors.All(t=>a.transform.InverseTransformPoint(t.position).z < -1),"two rear anchors");Check(red.Exhaust!=null&&!red.Exhaust.Emitting,"exhaust initially off");
   }finally{Object.DestroyImmediate(a);Object.DestroyImmediate(b);}
  }
  static void WriteFloat(byte[] bytes,int offset,float value){
   int bits=BitConverter.SingleToInt32Bits(value);for(int b=0;b<4;b++)bytes[offset+b]=(byte)(bits>>(8*b));
  }
  static (int,int,int) Oriented(int a,int b,int c){
   var key=(a,b,c);var next=(b,c,a);if(next.CompareTo(key)<0)key=next;next=(c,a,b);return next.CompareTo(key)<0?next:key;
  }
  public static void InspectLayout(){
   Directory.CreateDirectory(Output);var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(VectorAssetImport.MeshPath);
   string Describe()=>"readable="+mesh.isReadable+" buffers="+mesh.vertexBufferCount+" strides="+string.Join(",",Enumerable.Range(0,mesh.vertexBufferCount).Select(mesh.GetVertexBufferStride))+" attrs="+string.Join(";",mesh.GetVertexAttributes().Select(a=>a.attribute+":"+a.format+":"+a.dimension+":"+a.stream));
   var before=Describe();Debug.Log("VECTOR_LAYOUT_LOADED "+before);
   AssetDatabase.ImportAsset(VectorAssetImport.MeshPath,ImportAssetOptions.ForceUpdate);mesh=AssetDatabase.LoadAssetAtPath<Mesh>(VectorAssetImport.MeshPath);
   var after=Describe();Debug.Log("VECTOR_LAYOUT_REIMPORTED "+after);File.WriteAllText(Path.Combine(Output,"layout.txt"),before+"\n"+after+"\n");
  }
  static void CheckPayload(){
   var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(VectorAssetImport.MeshPath);var positions=mesh.vertices;var normals=mesh.normals;var channels=new List<Vector2>[5];for(int c=0;c<5;c++){channels[c]=new List<Vector2>();mesh.GetUVs(c,channels[c]);}
   Check(mesh.vertexBufferCount==2&&mesh.GetVertexBufferStride(0)==24&&mesh.GetVertexBufferStride(1)==40,"separate exact geometry24/surface40 byte streams; actual buffers="+mesh.vertexBufferCount+" stride0="+mesh.GetVertexBufferStride(0));
   Check(mesh.GetVertexAttributes().Where(a=>a.attribute==VertexAttribute.Position||a.attribute==VertexAttribute.Normal).All(a=>a.stream==0),"geometry-only stream");
   Check(mesh.GetVertexAttributes().Where(a=>(int)a.attribute>=(int)VertexAttribute.TexCoord0&&(int)a.attribute<=(int)VertexAttribute.TexCoord4).All(a=>a.stream==1),"surface-only stream");
   using(var reader=new BinaryReader(File.OpenRead(Path.Combine(VectorAssetImport.SourceRoot,"runtime.meshbin")))){
    Check(System.Text.Encoding.ASCII.GetString(reader.ReadBytes(8))=="VECTOR05","canonical signature");int n=reader.ReadInt32(),count=reader.ReadInt32();Check(n==mesh.vertexCount&&count==mesh.GetIndexCount(0),"payload counts");
    var canonical=new byte[n][];var lookup=new Dictionary<string,int>();
    for(int i=0;i<n;i++){canonical[i]=reader.ReadBytes(64);Check(canonical[i].Length==64,"complete canonical vertex");lookup.Add(Convert.ToBase64String(canonical[i]),i);}
    var remap=new int[n];var seen=new HashSet<int>();var bytes=new byte[64];
    for(int i=0;i<n;i++){
     WriteFloat(bytes,0,positions[i].x);WriteFloat(bytes,4,positions[i].y);WriteFloat(bytes,8,positions[i].z);WriteFloat(bytes,12,normals[i].x);WriteFloat(bytes,16,normals[i].y);WriteFloat(bytes,20,normals[i].z);
     for(int c=0;c<5;c++){WriteFloat(bytes,24+c*8,channels[c][i].x);WriteFloat(bytes,28+c*8,channels[c][i].y);}
     Check(lookup.TryGetValue(Convert.ToBase64String(bytes),out int original),"bit-exact canonical vertex including signed zero");remap[i]=original;Check(seen.Add(original),"bijective lossless vertex permutation");var source=canonical[original];
     float F(int at)=>BitConverter.ToSingle(source,at);
     Check(positions[i].Equals(new Vector3(F(0),F(4),F(8))),"exact source positions after permutation");Check(normals[i].Equals(new Vector3(F(12),F(16),F(20))),"exact source normals after permutation");
     for(int c=0;c<5;c++)Check(channels[c][i].Equals(new Vector2(F(24+c*8),F(28+c*8))),"exact PBR floats after permutation");
    }
    var sourceTriangles=new Dictionary<(int,int,int),int>();
    for(int i=0;i<count;i+=3){var triangle=Oriented(reader.ReadInt32(),reader.ReadInt32(),reader.ReadInt32());sourceTriangles.TryGetValue(triangle,out int copies);sourceTriangles[triangle]=copies+1;}
    Check(reader.BaseStream.Position==reader.BaseStream.Length,"canonical payload fully consumed");var indices=mesh.triangles;
    for(int i=0;i<count;i+=3){
     for(int c=0;c<3;c++)Check(indices[i+c]>=0&&indices[i+c]<n&&seen.Contains(remap[indices[i+c]]),"exact source corner binding");
     var key=Oriented(remap[indices[i]],remap[indices[i+1]],remap[indices[i+2]]);Check(sourceTriangles.TryGetValue(key,out int copies)&&copies>0,"oriented source triangle and multiplicity");if(copies==1)sourceTriangles.Remove(key);else sourceTriangles[key]=copies-1;
    }
    Check(sourceTriangles.Count==0&&seen.Count==n,"all original vertices/triangles conserved; no simplified geometry");
   }
  }
 }
}
