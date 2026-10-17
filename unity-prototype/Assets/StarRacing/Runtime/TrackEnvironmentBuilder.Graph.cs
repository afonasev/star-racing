using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
 public sealed partial class TrackEnvironmentBuilder {
  readonly List<(MeshRenderer renderer,bool enabled)> graphRenderers=new List<(MeshRenderer,bool)>();
  public GraphEnvironmentGeometry GraphGeometry {get;private set;}
  void ClearGraphRenderers(){foreach(var item in graphRenderers)if(item.renderer!=null)item.renderer.enabled=item.enabled;graphRenderers.Clear();GraphGeometry=null;}
  public void BuildIndependent(GraphRoadSurface surface,uint seed,string theme,uint? skySeed=null)=>LoadingWork.Drain(BuildIndependentSteps(surface,seed,theme,skySeed,false));
  internal IEnumerator BuildIndependentSteps(GraphRoadSurface surface,uint seed,string theme,uint? skySeed=null,bool background=true) {
   if(surface==null||!surface.isActiveAndEnabled||surface.Graph==null)throw new ArgumentException("Current graph surface required");
   Clear();
   if(background){var graph=surface.Graph;var task=LoadingWork.Background(()=>{var geometry=new GraphEnvironmentGeometry(graph);return (geometry,plan:EnvironmentPlan.Create(geometry,seed,theme,skySeed));});while(!task.IsCompleted)yield return null;var result=task.GetAwaiter().GetResult();GraphGeometry=result.geometry;Plan=result.plan;}
   else {GraphGeometry=new GraphEnvironmentGeometry(surface.Graph);Plan=EnvironmentPlan.Create(GraphGeometry,seed,theme,skySeed);}
   root=new GameObject("Graph environment "+theme);cube=CreateCube();sphere=CreateSphere(12,16);
   bool complete=false;
   try {
    var road=SurfaceMaterial("RoadBlend",false);road.mainTextureScale=new Vector2(8f/32f,8f/288f);
    var rail=SurfaceMaterial("RailBlend",true);rail.mainTextureScale=new Vector2(4f/288f,4f/1.3f);
    foreach(var source in surface.GetComponentsInChildren<MeshFilter>()) {
     yield return null;
     bool top=source.GetComponent<TrackSurface>()!=null,isRail=source.sharedMesh!=null&&source.sharedMesh.name=="Independent guard rails";
     if(!top&&!isRail)continue;
     var visual=UnityEngine.Object.Instantiate(source.sharedMesh);visual.name=top?"Graph environment road visual":"Graph environment rail visual";meshes.Add(visual);
     if(top){var uv=new Vector2[visual.vertexCount];foreach(var face in source.GetComponent<TrackSurface>().triangleFaces){int at=face.quad*4;uv[at]=new Vector2(face.leftStart/8f,face.startDistance/8f);uv[at+1]=new Vector2(face.leftEnd/8f,face.endDistance/8f);uv[at+2]=new Vector2(face.rightStart/8f,face.startDistance/8f);uv[at+3]=new Vector2(face.rightEnd/8f,face.endDistance/8f);}visual.uv=uv;}
     if(isRail){var uv=visual.uv;var normals=visual.normals;for(int i=0;i<uv.Length;i++)if(normals[i].y>.9f)uv[i].y=1.3f/4f*.95f;visual.uv=uv;}
     var go=new GameObject(visual.name);go.transform.SetParent(root.transform,false);go.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);go.transform.localScale=source.transform.lossyScale;
     go.AddComponent<MeshFilter>().sharedMesh=visual;go.AddComponent<MeshRenderer>().sharedMaterial=top?road:rail;
     var renderer=source.GetComponent<MeshRenderer>();if(renderer!=null){graphRenderers.Add((renderer,renderer.enabled));renderer.enabled=false;}
    }
    yield return BuildGraphBoundariesSteps(theme=="space-station");
    if(theme=="cloud-city")yield return BuildCitySteps(null);else yield return BuildSpaceScenerySteps();
    yield return null;
    Sky=new TrackSky(Plan,GraphGeometry.Bounds,root.transform);
    complete=true;
   }finally{if(!complete)Clear();}
  }
  IEnumerator BuildGraphBoundariesSteps(bool lamps) {
   var budget=new LoadingWork.Budget();
   var paint=Lit(new Color(.18f,.86f,1),.1f,true);var pole=lamps?Lit(new Color(.22f,.31f,.44f),.63f):null;
   var vertices=new List<Vector3>();var triangles=new List<int>();var uv=new List<Vector2>();
   for(int chainIndex=0;chainIndex<GraphGeometry.Boundaries.Count;chainIndex++) {
    var chain=GraphGeometry.Boundaries[chainIndex];var normals=GraphGeometry.BoundaryNormals[chainIndex];
    float distance=0,nextLamp=40;
    for(int i=0;i+1<chain.Length;i++) {
     if(budget.Due){yield return null;budget.Restart();}
     var a=chain[i];var b=chain[i+1];var tangent=(b-a).normalized;var normal=normals[i];var nextNormal=normals[i+1];var side=Vector3.Cross(normal,tangent).normalized*.10f;var nextSide=Vector3.Cross(nextNormal,tangent).normalized*.10f;int v=vertices.Count;
     vertices.AddRange(new[]{a-side,b-nextSide,a+side,b+nextSide});uv.AddRange(new[]{Vector2.zero,Vector2.up,Vector2.right,Vector2.one});triangles.AddRange(new[]{v,v+1,v+2,v+2,v+1,v+3});
     float length=Vector3.Distance(a,b);
     while(lamps&&nextLamp<=distance+length) {
      float fraction=(nextLamp-distance)/length;var point=Vector3.Lerp(a,b,fraction);var lampNormal=Vector3.Lerp(normal,nextNormal,fraction).normalized;nextLamp+=80;
      // Try either exterior side, then validate the entire pole volume against every road.
      var right=Vector3.Cross(tangent,lampNormal).normalized;
      for(int sign=-1;sign<=1;sign+=2){var basePoint=point+right*(sign*2);if(!GraphGeometry.Clear(basePoint+lampNormal*3,new Vector3(Math.Abs(lampNormal.x),Math.Abs(lampNormal.y),Math.Abs(lampNormal.z))*3.4f+Vector3.one*.8f,0))continue;
       Shape("Graph road lamp",cube,pole,basePoint+lampNormal*3,Quaternion.FromToRotation(Vector3.up,lampNormal),new Vector3(.42f,6,.42f),false);
       Shape("Graph road lamp emitter",cube,paint,basePoint+lampNormal*6,Quaternion.FromToRotation(Vector3.up,lampNormal),new Vector3(1.4f,.7f,1.4f),false);
       Lamps.Add(new LampPlacement{distance=distance,side=sign,basePoint=basePoint});LampCount++;break;}
     }distance+=length;
    }
   }
   var mesh=new Mesh{name="Graph boundary markings",indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();meshes.Add(mesh);
   var go=new GameObject("Graph boundary markings");go.transform.SetParent(root.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=paint;
  }
 }
}
