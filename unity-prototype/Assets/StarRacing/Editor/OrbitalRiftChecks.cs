using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
namespace StarRacingPrototype {
 public static class OrbitalRiftChecks {
  public static LocalRaceConfig Config(int entrants=8,int humans=0)=>new LocalRaceConfig{courseId=AuthoredOrbitalRiftCourse.Id,courseRevision=1,theme="space-station",rails="full",jumps=false,seed="77",entrants=entrants,humans=humans};
  static int assertions;static void Check(bool ok,string why){assertions++;if(!ok)throw new Exception("Orbital rift: "+why);}
  public static void Run(){
   Directory.CreateDirectory(AuthoredCourseChecks.Output);assertions=0;
   // The unchanged adjacent fixture checks Eye geometry/replay and legacy procedural bytes.
   AuthoredCourseChecks.Run();
   var config=Config();Check(config.Validate(out _),"selection valid");
   var course=CourseSource.Generate(config,"space-station");var d=course.Single;
   Check(d.composition.Length==8&&AuthoredCloudCityCourse.Validate(d).Length==0,"eight valid sections");
   Check(d.samples.Where(s=>s.segmentIndex==5).Min(s=>s.normal.y)<-.95,"real station underside");
   Check(d.samples.Where(s=>s.segmentIndex==5).Max(s=>s.normal.x)>.95,"real station side");
   config.seed="54321";Check(CourseSource.Generate(config,"space-station").GeometryIdentity==course.GeometryIdentity,"seed independent");
   config.courseRevision=99;Check(!config.Validate(out _),"unknown revision rejected");
   config=Config();config.theme="cloud-city";Check(!config.Validate(out _),"wrong authored theme rejected");
   config=Config();config.courseId="unknown";Check(!config.Validate(out _),"unknown source rejected");
   var road=new GameObject("Orbital road checks");var env=new TrackEnvironmentBuilder();GameObject probe=null;Mesh merged=null;
   try{
    var track=road.AddComponent<TrackBuilder>();track.BuildNativeCourse(course);Physics.SyncTransforms();
    var replay=NativeCourseReplay.Capture(course,track);Check(replay.Matches(Config(),"space-station")&&!replay.Matches(AuthoredCourseChecks.Config(),"cloud-city"),"replay does not alias eye");
    replay.Verify(CourseSource.Generate(Config(),"space-station"),track);
    env.Build(track.Route,track.transform);Check(env.Plan.orbital&&!env.Plan.authored&&env.Root.GetComponentsInChildren<Collider>().Length==0,"dedicated render-only world");
    Check(EnvironmentPlan.Create(track.Route,77).presentationHash==EnvironmentPlan.Create(track.Route,54321).presentationHash,"presentation independent of race seed");
    // Actual rendered meshes must not cut the car/camera corridor. Temporary test collider only.
    merged=new Mesh{indexFormat=IndexFormat.UInt32};merged.CombineMeshes(env.Root.GetComponentsInChildren<MeshFilter>().Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=f.transform.localToWorldMatrix}).ToArray(),true,true);
    probe=new GameObject("QA environment corridor probe");var collider=probe.AddComponent<MeshCollider>();collider.sharedMesh=merged;Physics.SyncTransforms();
    int probes=0;
    foreach(var sample in d.samples.Where((s,i)=>i%4==0)){
     var f=track.Route.Evaluate(30+(float)sample.distance);
     foreach(float side in new[]{-.65f,0,.65f}){
      var p=f.position+f.right*f.halfWidth*side;
      Check(track.RaycastRoad(p+f.normal*3,-f.normal,6,f.distance,out _),"road support at "+f.distance);
      Check(!collider.Raycast(new Ray(p+f.normal*2-f.tangent*5,f.tangent),out var hit,10),"scenery intersects driving corridor at "+f.distance+" hit="+hit.point);
      Check(!collider.Raycast(new Ray(p+f.normal*1,f.normal),out _,8),"scenery intersects camera clearance at "+f.distance);probes++;
     }
    }
    File.WriteAllText(Path.Combine(AuthoredCourseChecks.Output,"orbital-geometry.json"),JsonUtility.ToJson(d,true));
    File.WriteAllText(Path.Combine(AuthoredCourseChecks.Output,"orbital-result.txt"),$"ORBITAL_GEOMETRY_OK assertions={assertions} probes={probes} length={d.totalLength} geometry={course.GeometryIdentity} presentation={env.Plan.presentationHash}\n");
    Debug.Log("ORBITAL_GEOMETRY_OK assertions="+assertions+" probes="+probes);
   }finally{if(probe!=null)UnityEngine.Object.DestroyImmediate(probe);if(merged!=null)UnityEngine.Object.DestroyImmediate(merged);env.Clear();UnityEngine.Object.DestroyImmediate(road);}
  }
 }
}
