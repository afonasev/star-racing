using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace StarRacingPrototype {
 public sealed partial class TrackEnvironmentBuilder {
  // Static detail is grouped spatially, so local lights and culling remain local too.
  sealed class OrbitalBatches {
   readonly TrackEnvironmentBuilder owner;readonly Vector3[] v,n;readonly int[] t;
   readonly Dictionary<(Vector3Int,Material),Part> parts=new Dictionary<(Vector3Int,Material),Part>();
   sealed class Part {public readonly List<Vector3> v=new List<Vector3>(),n=new List<Vector3>();public readonly List<Vector2> uv=new List<Vector2>();public readonly List<int> t=new List<int>();}
   public OrbitalBatches(TrackEnvironmentBuilder owner){this.owner=owner;v=owner.cube.vertices;n=owner.cube.normals;t=owner.cube.triangles;}
   public void Box(Material material,Vector3 center,Vector3 size,Quaternion rotation,float tile=12){
    var key=(new Vector3Int(Mathf.FloorToInt(center.x/512),Mathf.FloorToInt(center.y/512),Mathf.FloorToInt(center.z/512)),material);
    if(!parts.TryGetValue(key,out var p))parts.Add(key,p=new Part());int at=p.v.Count;
    for(int i=0;i<v.Length;i++){p.v.Add(center+rotation*Vector3.Scale(v[i],size));p.n.Add(rotation*n[i]);p.uv.Add(CubeUv(v[i],n[i],size,tile));}
    foreach(int index in t)p.t.Add(at+index);
   }
   public void Beam(Material m,Vector3 a,Vector3 b,float width)=>Box(m,(a+b)*.5f,new Vector3(width,width,Vector3.Distance(a,b)),Quaternion.LookRotation(b-a));
   public IEnumerator Flush(){foreach(var pair in parts){var p=pair.Value;var mesh=new Mesh{name="Orbital service district",indexFormat=IndexFormat.UInt32};mesh.SetVertices(p.v);mesh.SetNormals(p.n);mesh.SetUVs(0,p.uv);mesh.SetTriangles(p.t,0);mesh.RecalculateTangents();mesh.RecalculateBounds();owner.meshes.Add(mesh);owner.Shape("Orbital service district",mesh,pair.Key.Item2,Vector3.zero,Quaternion.identity,Vector3.one);yield return null;}}
  }
  Material OrbitalMetal(Color tint,float metal,float smooth,string texture="SpaceHull"){
   var m=Lit(tint,metal,texture:texture,smoothness:smooth);m.name="Orbital "+texture+" "+tint;
   if(texture=="SpaceHull"){m.EnableKeyword("_NORMALMAP");m.SetTexture("_BumpMap",SharedRaceAssets.Load<Texture2D>("Environment/Textures/SpaceHullNormal"));m.SetFloat("_BumpScale",.45f);}
   return m;
  }
  Material OrbitalStone(Color tint){
   var shader=SharedRaceAssets.Load<Shader>("Environment/OrbitalStone");if(shader==null)throw new System.InvalidOperationException("Orbital stone shader missing");
   var m=new Material(shader){name="Orbital fractured stone"};m.SetColor("_BaseColor",tint);m.SetTexture("_BaseMap",SharedRaceAssets.Load<Texture2D>("Environment/Textures/SpaceRock"));m.SetTexture("_BumpMap",SharedRaceAssets.Load<Texture2D>("Environment/Textures/SpaceRockNormal"));materials.Add(m);return m;
  }
  void OrbitalLight(string name,Vector3 at,Color color,float range,float intensity){
   var obj=new GameObject("Orbital light "+name);obj.transform.SetParent(root.transform,false);obj.transform.position=at;
   var light=obj.AddComponent<Light>();light.type=LightType.Point;light.color=color;light.range=range;light.intensity=intensity*250f;light.shadows=LightShadows.None;
  }
  void OrbitalMesh(string name,List<Vector3> v,List<Vector2> uv,List<int> t,Material m){
   var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateTangents();mesh.RecalculateBounds();
   try{foreach(var chunk in VisualMeshChunks.Create(mesh,160)){meshes.Add(chunk);Shape(name,chunk,m,Vector3.zero,Quaternion.identity,Vector3.one);}}finally{Dispose(mesh);}
  }
 }
}
