using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StarRacingPrototype {
 // Construction-time render partition only. Never used as collider/source geometry.
 public static class VisualMeshChunks {
  public static List<Mesh> Create(Mesh source,float size=512) {
   if(source.subMeshCount!=1)throw new System.ArgumentException("Single-material visual required");
   foreach(var attribute in source.GetVertexAttributes())
    if(attribute.attribute!=VertexAttribute.Position&&attribute.attribute!=VertexAttribute.Normal&&attribute.attribute!=VertexAttribute.TexCoord0&&attribute.attribute!=VertexAttribute.Tangent)
     throw new System.ArgumentException("Unsupported visual vertex stream: "+attribute.attribute);
   var vertices=source.vertices;var normals=source.normals;var uv=source.uv;var tangents=source.tangents;
   var triangles=source.triangles;var groups=new Dictionary<Vector2Int,Chunk>();
   for(int i=0;i<triangles.Length;i+=3){
    var center=(vertices[triangles[i]]+vertices[triangles[i+1]]+vertices[triangles[i+2]])/3;
    var key=new Vector2Int(Mathf.FloorToInt(center.x/size),Mathf.FloorToInt(center.z/size));
    if(!groups.TryGetValue(key,out var chunk))groups.Add(key,chunk=new Chunk());
    for(int j=0;j<3;j++){
     int original=triangles[i+j];
     if(!chunk.map.TryGetValue(original,out int at)){
      at=chunk.vertices.Count;chunk.map.Add(original,at);chunk.vertices.Add(vertices[original]);
      if(normals.Length>0)chunk.normals.Add(normals[original]);
      if(uv.Length>0)chunk.uv.Add(uv[original]);
      if(tangents.Length>0)chunk.tangents.Add(tangents[original]);
     }
     chunk.triangles.Add(at);
    }
   }
   var result=new List<Mesh>();
   foreach(var chunk in groups.Values){
    var mesh=new Mesh{name=source.name+" section",indexFormat=chunk.vertices.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
    mesh.SetVertices(chunk.vertices);if(normals.Length>0)mesh.SetNormals(chunk.normals);
    if(uv.Length>0)mesh.SetUVs(0,chunk.uv);if(tangents.Length>0)mesh.SetTangents(chunk.tangents);
    mesh.SetTriangles(chunk.triangles,0);mesh.RecalculateBounds();
    // Float center/extents reconstruction can put boundary vertices just outside.
    var bounds=mesh.bounds;bounds.Expand(.02f);mesh.bounds=bounds;result.Add(mesh);
   }
   return result;
  }
  sealed class Chunk {
   public readonly Dictionary<int,int> map=new Dictionary<int,int>();
   public readonly List<Vector3> vertices=new List<Vector3>(),normals=new List<Vector3>();
   public readonly List<Vector2> uv=new List<Vector2>();public readonly List<Vector4> tangents=new List<Vector4>();
   public readonly List<int> triangles=new List<int>();
  }
 }
}
