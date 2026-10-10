using System;
using UnityEditor;
using UnityEngine;
namespace StarRacingPrototype {
 public static class DriftTireMarkChecks {
  static int assertions;
  static void Check(bool valid,string label){assertions++;if(!valid)throw new Exception("DRIFT_MARKS "+label);}
  public static void Run(){
   assertions=0;float scale=Time.timeScale;Time.timeScale=1;
   try{
    bool active=false;
    Check(DriftTireMarks.SlipIntensity(Vector3.zero,Vector3.forward,Vector3.up,ref active)==0,"stationary");
    Check(DriftTireMarks.SlipIntensity(Vector3.forward*30,Vector3.forward,Vector3.up,ref active)==0,"straight and drift-button-only");
    float weak=DriftTireMarks.SlipIntensity(new Vector3(5,0,30),Vector3.forward,Vector3.up,ref active);
    float strong=DriftTireMarks.SlipIntensity(new Vector3(16,0,30),Vector3.forward,Vector3.up,ref active);
    Check(weak>0&&strong>weak,"intensity follows real body-relative slip");
    var rotation=Quaternion.Euler(60,25,10);active=false;
    Check(Mathf.Abs(weak-DriftTireMarks.SlipIntensity(rotation*new Vector3(5,0,30),rotation*Vector3.forward,rotation*Vector3.up,ref active))<.0001f,"slip independent of world-up");
    var root=new GameObject("Drift buffer fixtures");
    try{
     var marks=root.AddComponent<DriftTireMarks>();marks.Initialize(new Bounds(Vector3.zero,Vector3.one*2000));
     var shader=Resources.Load<Shader>("Vehicle/DriftTireMarks");Check(shader!=null&&!ShaderUtil.ShaderHasError(shader),"shader compiles");
     var rubber=Resources.Load<Texture2D>("Vehicle/DriftRubber");
     Check(rubber!=null&&root.GetComponent<MeshRenderer>().sharedMaterial.GetTexture("_RubberTex")==rubber,"shared rubber opacity texture");
     var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(rubber));
     Check(!importer.sRGBTexture&&importer.mipmapEnabled&&importer.wrapModeU==TextureWrapMode.Clamp&&importer.wrapModeV==TextureWrapMode.Repeat,"linear mask, mipmaps and longitudinal repeat");
     marks.Sample(0,Vector3.zero,Vector3.up,.8f,0);marks.Sample(0,Vector3.forward*.4f,Vector3.up,.8f,.02f);
     marks.Sample(0,Vector3.forward*1.2f,Vector3.up,.8f,.04f);marks.Flush(.04f);
     var uv=marks.RenderMesh.uv;
     Check(Mathf.Abs(uv[2].y-uv[4].y)<.00001f,"texture continuous across emitted segments");
     Check(Mathf.Abs((uv[6].y-uv[4].y)-2*(uv[2].y-uv[0].y))<.00001f,"texture scale follows travelled metres");
     marks.BreakAll();marks.Sample(0,Vector3.forward*2,Vector3.up,.8f,.06f);marks.Sample(0,Vector3.forward*2.4f,Vector3.up,.8f,.08f);marks.Flush(.08f);
     Check(Mathf.Abs(marks.RenderMesh.uv[8].y)<.00001f,"new contact resets texture distance");
     foreach(var normal in new[]{Vector3.up,Vector3.right,new Vector3(0,.7f,.7f).normalized}){
      marks.Clear();var along=Vector3.Cross(normal,Vector3.left).normalized;
      if(along.sqrMagnitude<.1f)along=Vector3.forward;
      marks.Sample(0,Vector3.zero,normal,.8f,0);marks.Sample(0,along*.5f,normal,.8f,.02f);marks.Flush(.02f);
      Check(marks.LiveSegments==1,"supported plane segment");var vertices=marks.RenderMesh.vertices;
      for(int i=0;i<4;i++)Check(Mathf.Abs(Vector3.Dot(vertices[i],normal)-DriftTireMarks.SurfaceOffset)<.0001f,"geometry follows local normal");
      marks.Flush(8.019f);Check(marks.LiveSegments==1,"no early eviction");marks.Flush(8.021f);Check(marks.LiveSegments==0,"expires at eight seconds");
     }
     marks.Clear();marks.Sample(0,Vector3.zero,Vector3.up,.8f,0);marks.Sample(0,Vector3.forward*.5f,Vector3.up,.8f,.02f);
     marks.BreakAll();marks.Sample(0,Vector3.forward*2,Vector3.up,.8f,.04f);marks.Flush(.04f);Check(marks.LiveSegments==1,"break retains old mark without bridge");
     marks.Sample(0,Vector3.forward*200,Vector3.up,.8f,.06f);marks.Flush(.06f);Check(marks.LiveSegments==1,"distance discontinuity breaks");
     marks.Clear();
     for(int tick=0;tick<=1200;tick++){
      float now=tick*DriftTireMarks.SampleInterval;
      for(int stream=0;stream<128;stream++)marks.Sample(stream,new Vector3(stream*2,0,tick*.5f),Vector3.up,.8f,now);
      marks.Flush(now);
      Check(marks.LiveSegments<=DriftTireMarks.Capacity,"64-car lifetime bound");
     }
     Check(marks.TotalSegments>DriftTireMarks.Capacity*2&&marks.RejectedSegments==0,"24s all-64 sustained drift no early overwrite");
     Check(root.GetComponentsInChildren<Renderer>().Length==1&&root.GetComponentsInChildren<MeshFilter>().Length==1,"one renderer and mesh");
     marks.Flush(32.1f);Check(marks.LiveSegments==0,"all late geometry retired");marks.Clear();Check(marks.LiveSegments==0&&marks.TotalSegments==0,"clear resets history");
    }finally{UnityEngine.Object.DestroyImmediate(root);}
    foreach(string theme in new[]{"cloud-city","space-station"})ContactFixture(theme);
   }finally{Time.timeScale=scale;}
   Debug.Log("DRIFT_TIRE_MARK_CHECKS_OK assertions="+assertions+" sustained64=24s bothThemes=true");
  }
  public static CheckpointAdmission.Support RoadContact(TrackBuilder track,float distance,float lateral,float rear=-1.175f){
   var frame=track.Route.Evaluate(distance);var origin=frame.position+frame.right*lateral+frame.tangent*rear+frame.normal*.7f;
   if(!track.RaycastRoad(origin,-frame.normal,1.12f,distance,out var hit))throw new Exception("Drift fixture missing road contact");
   return new CheckpointAdmission.Support(origin,-frame.normal,1.12f,hit);
  }
  static void ContactFixture(string theme){
   var root=new GameObject("Drift "+theme);var track=root.AddComponent<TrackBuilder>();
   try{
    track.Build(new TrackRoute(Procedural.Generator.Generate(77,"normal",theme,false,true)));Physics.SyncTransforms();
    var marks=track.TireMarks;var frame=track.Route.Evaluate(80);Vector3 velocity=frame.tangent*30+frame.right*8;
    for(int tick=0;tick<3;tick++){
     float distance=80+tick*.5f;var left=RoadContact(track,distance,-.71f);var right=RoadContact(track,distance,.71f);
     // Same measured movement, separately registered human and AI streams.
     marks.Observe(0,1,velocity,frame.tangent,frame.normal,left,right,track,true,tick*.02f);
     marks.Observe(1,1,velocity,frame.tangent,frame.normal,left,right,track,true,tick*.02f);
    }
    marks.Flush(.04f);Check(marks.LiveSegments==8,"human and AI both emit "+theme);
    long before=marks.TotalSegments;var l=RoadContact(track,82,-.71f);var r=RoadContact(track,82,.71f);
    marks.Observe(0,2,velocity,frame.tangent,frame.normal,l,r,track,true,.06f);Check(marks.TotalSegments==before,"revision breaks "+theme);
    marks.Observe(0,2,velocity,frame.tangent,frame.normal,l,r,track,false,.08f);
    marks.Observe(0,2,velocity,frame.tangent,frame.normal,RoadContact(track,83,-.71f),RoadContact(track,83,.71f),track,true,.10f);
    Check(marks.TotalSegments==before,"jump/fall/hold suppression breaks "+theme);
    marks.Observe(0,2,velocity,frame.tangent,frame.normal,default,RoadContact(track,83.5f,.71f),track,true,.12f);
    Check(marks.TotalSegments==before+1,"independent rear contact "+theme);
    marks.Observe(0,2,velocity,frame.tangent,frame.normal,RoadContact(track,84,-.71f),RoadContact(track,84,.71f),track,true,.14f);
    Check(marks.TotalSegments==before+2,"returning wheel seeds rather than bridges "+theme);
    var distant=RoadContact(track,84.5f,-.71f);var distantHit=distant.hit;distantHit.distance=1.2f;
    marks.Observe(0,2,velocity,frame.tangent,frame.normal,new CheckpointAdmission.Support(distant.origin,distant.direction,distant.length,distantHit),default,track,true,.16f);
    Check(marks.TotalSegments==before+2,"uncompressed suspension safety-ray hit does not emit "+theme);
    track.Build(new TrackRoute(Procedural.Generator.Generate(78,"normal",theme,false,true)));Check(track.TireMarks.LiveSegments==0,"new track clears "+theme);
   }finally{UnityEngine.Object.DestroyImmediate(root);}
  }
 }
}
