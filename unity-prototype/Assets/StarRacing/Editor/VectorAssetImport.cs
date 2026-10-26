using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
namespace StarRacingPrototype {
 public static class VectorAssetImport {
  public const string MeshPath="Assets/StarRacing/Resources/Vehicle/VectorMesh.asset";
  public const string ModelPath="Assets/StarRacing/Resources/Vehicle/Vector.prefab";
  public static string SourceRoot=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../art/candidates/vector-v05"));
  [Serializable] sealed class Anchors {public float[] Exhaust_L,Exhaust_R;}
  [Serializable] sealed class Document {public Anchors anchors;}
  [StructLayout(LayoutKind.Sequential)] struct GeometryVertex {public Vector3 position,normal;}
  [StructLayout(LayoutKind.Sequential)] struct SurfaceVertex {public Vector2 uv0,uv1,uv2,uv3,uv4;}
  // The canonical payload stays unchanged and remains the independent geometry oracle.
  public static Mesh CreateSourceMesh(){
   Mesh mesh=new Mesh{name="VectorFullResolution",indexFormat=IndexFormat.UInt32};
   using(var reader=new BinaryReader(File.OpenRead(Path.Combine(SourceRoot,"runtime.meshbin")))){
    if(System.Text.Encoding.ASCII.GetString(reader.ReadBytes(8))!="VECTOR05")throw new Exception("Vector payload signature");
    int n=reader.ReadInt32(),count=reader.ReadInt32();var positions=new Vector3[n];var normals=new Vector3[n];var channels=new List<Vector2>[5];for(int c=0;c<5;c++)channels[c]=new List<Vector2>(n);
    for(int v=0;v<n;v++){positions[v]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());normals[v]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());for(int c=0;c<5;c++)channels[c].Add(new Vector2(reader.ReadSingle(),reader.ReadSingle()));}
    var indices=new int[count];for(int i=0;i<count;i++)indices[i]=reader.ReadInt32();if(reader.BaseStream.Position!=reader.BaseStream.Length)throw new Exception("Trailing mesh payload");
    mesh.vertices=positions;mesh.normals=normals;for(int c=0;c<5;c++)mesh.SetUVs(c,channels[c]);mesh.SetTriangles(indices,0,true);
   }
   return mesh;
  }
  public static void Prepare(){
   var mesh=CreateSourceMesh();
   mesh.OptimizeIndexBuffers();mesh.OptimizeReorderVertexBuffer();
   SeparateStreams(mesh);
   var existing=AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);if(existing==null)AssetDatabase.CreateAsset(mesh,MeshPath);else{existing.Clear(false);EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;}
   const string materialPath="Assets/StarRacing/Resources/Vehicle/VectorPacked.mat";
   var shader=Shader.Find("StarRacing/VectorPacked");if(shader==null)throw new Exception("Vector shader missing");
   var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);if(material==null){material=new Material(shader);AssetDatabase.CreateAsset(material,materialPath);}material.shader=shader;material.enableInstancing=true;material.SetColor("_BaseColor",Color.white);EditorUtility.SetDirty(material);
   var root=new GameObject("Vector V05");
   try{
    var geometry=new GameObject("VectorFullResolution");geometry.transform.SetParent(root.transform,false);geometry.AddComponent<MeshFilter>().sharedMesh=mesh;geometry.AddComponent<MeshRenderer>().sharedMaterial=material;
    var doc=JsonUtility.FromJson<Document>(File.ReadAllText(Path.Combine(SourceRoot,"runtime-export.json")));
    void Anchor(string name,float[] p){if(p==null||p.Length!=3)throw new Exception("Vector anchor payload");var a=new GameObject(name);a.transform.SetParent(root.transform,false);a.transform.localPosition=new Vector3(p[0],p[1],p[2]);}
    Anchor("Exhaust_L",doc.anchors.Exhaust_L);Anchor("Exhaust_R",doc.anchors.Exhaust_R);
    PrefabUtility.SaveAsPrefabAsset(root,ModelPath);
   }finally{UnityEngine.Object.DestroyImmediate(root);}
   // Save exact mesh data first; disable the duplicate CPU copy in the shipped asset.
   AssetDatabase.SaveAssets();var serialized=new SerializedObject(mesh);var readable=serialized.FindProperty("m_IsReadable");if(readable==null)throw new Exception("Mesh readability setting unavailable");readable.boolValue=false;serialized.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssets();
   Debug.Log("VECTOR_IMPORT_READY vertices="+mesh.vertexCount+" triangles="+mesh.GetIndexCount(0)/3);
  }
  static void SeparateStreams(Mesh mesh){
   var positions=mesh.vertices;var normals=mesh.normals;var indices=mesh.triangles;var bounds=mesh.bounds;
   var uv=new List<Vector2>[5];for(int c=0;c<5;c++){uv[c]=new List<Vector2>();mesh.GetUVs(c,uv[c]);}
   var geometry=new GeometryVertex[positions.Length];var surface=new SurfaceVertex[positions.Length];
   for(int i=0;i<positions.Length;i++){
    geometry[i]=new GeometryVertex{position=positions[i],normal=normals[i]};
    surface[i]=new SurfaceVertex{uv0=uv[0][i],uv1=uv[1][i],uv2=uv[2][i],uv3=uv[3][i],uv4=uv[4][i]};
   }
   mesh.SetVertexBufferParams(positions.Length,
    new VertexAttributeDescriptor(VertexAttribute.Position,VertexAttributeFormat.Float32,3,0),
    new VertexAttributeDescriptor(VertexAttribute.Normal,VertexAttributeFormat.Float32,3,0),
    new VertexAttributeDescriptor(VertexAttribute.TexCoord0,VertexAttributeFormat.Float32,2,1),
    new VertexAttributeDescriptor(VertexAttribute.TexCoord1,VertexAttributeFormat.Float32,2,1),
    new VertexAttributeDescriptor(VertexAttribute.TexCoord2,VertexAttributeFormat.Float32,2,1),
    new VertexAttributeDescriptor(VertexAttribute.TexCoord3,VertexAttributeFormat.Float32,2,1),
    new VertexAttributeDescriptor(VertexAttribute.TexCoord4,VertexAttributeFormat.Float32,2,1));
   mesh.SetVertexBufferData(geometry,0,0,geometry.Length,0,MeshUpdateFlags.DontRecalculateBounds);
   mesh.SetVertexBufferData(surface,0,0,surface.Length,1,MeshUpdateFlags.DontRecalculateBounds);
   mesh.SetIndexBufferParams(indices.Length,IndexFormat.UInt32);mesh.SetIndexBufferData(indices,0,0,indices.Length,MeshUpdateFlags.DontRecalculateBounds);
   mesh.subMeshCount=1;mesh.SetSubMesh(0,new SubMeshDescriptor(0,indices.Length){bounds=bounds,vertexCount=positions.Length},MeshUpdateFlags.DontRecalculateBounds);mesh.bounds=bounds;
  }
 }
}
