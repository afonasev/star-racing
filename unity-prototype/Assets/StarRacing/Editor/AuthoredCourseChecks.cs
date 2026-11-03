using System;
using System.IO;
using System.Linq;
using UnityEngine;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
 public static class AuthoredCourseChecks {
  public static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_AUTHORED_QA_DIR")??throw new Exception("STAR_RACING_AUTHORED_QA_DIR required");
  public static bool Orbital=>Environment.GetEnvironmentVariable("STAR_RACING_AUTHORED_COURSE")==AuthoredOrbitalRiftCourse.Id;
  public static LocalRaceConfig SelectedConfig(int entrants=8,int humans=0)=>Orbital?OrbitalRiftChecks.Config(entrants,humans):Config(entrants,humans);
  static int assertions;
  [Serializable] class ColliderEntry {public string name;public int vertices;public uint indices;public int cooking;}
  [Serializable] class ColliderReport {public ColliderEntry[] meshes;public int supportProbes;}
  public static void BuildDiagnostic(){CheckColliders();BuildingRenderDiagnosticBuild.Run();}
  public static void CheckColliders(){
   Directory.CreateDirectory(Output);var go=new GameObject("Authored collision inventory");try{
    var course=CourseSource.Generate(Config(),"cloud-city");var track=go.AddComponent<TrackBuilder>();track.BuildNativeCourse(course);Physics.SyncTransforms();
    var colliders=go.GetComponentsInChildren<MeshCollider>().Where(c=>c.sharedMesh!=null).ToArray();
    var report=new ColliderReport{meshes=colliders.Select(c=>new ColliderEntry{name=c.sharedMesh.name,vertices=c.sharedMesh.vertexCount,indices=c.sharedMesh.GetIndexCount(0),cooking=(int)c.cookingOptions}).ToArray()};
    File.WriteAllText(Path.Combine(Output,"colliders.json"),JsonUtility.ToJson(report,true));
    foreach(var c in colliders)if(c.sharedMesh.GetIndexCount(0)>2097152)Check((c.cookingOptions&MeshColliderCookingOptions.UseFastMidphase)==0,"safe large mesh cooking "+c.name);
    var bottom=colliders.Single(c=>c.sharedMesh.name==RoadVolumeMesh.BottomName);
    foreach(var section in course.Single.composition){var frame=track.Route.Evaluate(30+(section.startIndex+section.endIndex)*2.5f);
     foreach(float lane in new[]{-.6f,0,.6f}){var point=frame.position+frame.right*frame.halfWidth*lane;
      Check(track.RaycastRoad(point+frame.normal*3,-frame.normal,6,frame.distance,out var top),"top support "+section.role);
      Check(bottom.Raycast(new Ray(point-frame.normal*3,frame.normal),out var under,6),"bottom support "+section.role);report.supportProbes+=2;
     }
    }
    File.WriteAllText(Path.Combine(Output,"colliders.json"),JsonUtility.ToJson(report,true));Debug.Log("AUTHORED_COLLIDERS_OK supportProbes="+report.supportProbes);
   }finally{UnityEngine.Object.DestroyImmediate(go);}
  }
  static void Check(bool pass,string message){assertions++;if(!pass)throw new Exception("Authored course: "+message);}
  static void Reject(Action action){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}Check(rejected,"invalid identity must be rejected");}
  public static LocalRaceConfig Config(int entrants=8,int humans=0)=>new LocalRaceConfig{courseId=AuthoredCloudCityCourse.Id,courseRevision=AuthoredCloudCityCourse.Revision,theme="cloud-city",rails="full",jumps=false,seed="77",entrants=entrants,humans=humans};
  public static void Run(){
   Directory.CreateDirectory(Output);assertions=0;
   var config=Config();Check(config.Validate(out _),"valid authored selection");
   Check(config.Copy().replay==null,"copy preserves missing replay");
   Check(LocalRaceConfig.Normalize(JsonUtility.FromJson<LocalRaceConfig>(JsonUtility.ToJson(config))).replay==null,"saved empty replay remains absent");
   var invalidReplay=Config();invalidReplay.replay=new NativeCourseReplay{courseId=AuthoredCloudCityCourse.Id,contentRevision=99};Check(LocalRaceConfig.Normalize(invalidReplay).replay!=null,"populated incompatible replay retained for rejection");
   var course=CourseSource.Generate(config,"cloud-city");var repeat=CourseSource.Generate(config,"cloud-city");
   Check(course.IsAuthored&&course.UsesBasis,"authored City uses qualified native basis with canonical source");Check(course.GeometryIdentity==repeat.GeometryIdentity,"deterministic complete geometry");
   var d=course.Single;Check(AuthoredCloudCityCourse.Validate(d).Length==0,"all geometry contracts");
   var route=new TrackRoute(d);
   var blocked=AuthoredCityArchitecture.Create().Where(p=>!EnvironmentPlan.ClearOfRoute(route,p.center,p.size*.5f,EnvironmentPlan.CorridorMargin)).ToArray();
   Check(blocked.Length==0,"architectural clearance: "+string.Join(", ",blocked.Select(p=>p.name+" "+p.center)));
   Check(d.composition.Length==8,"eight named sections");Check(d.samples.Min(s=>s.tangent.y)<-.95,"near vertical dive");
   Check(d.samples.Max(s=>s.position.y)-d.samples.Min(s=>s.position.y)>650,"three-dimensional relief");
   Check(d.samples.Where(s=>s.segmentIndex==6).Max(s=>s.position.y)-d.samples.Where(s=>s.segmentIndex==6).Min(s=>s.position.y)>500,"double tower ascent");
   config.seed="54321";Check(course.GeometryIdentity==CourseSource.Generate(config,"cloud-city").GeometryIdentity,"race seed does not change authored course");
   config.courseRevision++;Check(!config.Validate(out _),"unsupported content revision");Reject(()=>CourseSource.Generate(config,"cloud-city"));
   var root=new GameObject("Authored replay check");try{
    var track=root.AddComponent<TrackBuilder>();track.BuildNativeCourse(course);
    var replay=NativeCourseReplay.Capture(course,track);Check(replay.schema==2&&replay.Matches(Config(),"cloud-city"),"authored replay source identity");Reject(()=>replay.Verify(repeat,track));track.BuildNativeCourse(repeat);replay.Verify(repeat,track);
    repeat.Single.samples[20].halfWidth-=.1;Reject(()=>repeat.ReplayIdentity(track));
   }finally{UnityEngine.Object.DestroyImmediate(root);}
   // Adjacent legacy control: compute the exact original constructor bytes independently.
   foreach(int revision in new[]{10,11,12}){
    var c=NativeTrackGenerator.Generate(77,new GraphCourseSettings("cloud-city","full",false),revision);
    Check(!c.IsAuthored&&c.ContentRevision==0,"procedural identity remains procedural");
    using(var buffer=new MemoryStream()){
     using(var w=new BinaryWriter(buffer,System.Text.Encoding.UTF8,true)){w.Write(NativeTrackCourse.Schema);w.Write(c.GeneratorRevision);w.Write(c.Family);w.Write(c.RequestedSeed);w.Write(c.ActualSeed);w.Write(c.UsedFallback);w.Write(c.Attempt);w.Write(c.Settings.theme);w.Write(c.Settings.rails);w.Write(c.Settings.jumps);w.Write(c.Graph.Identity);w.Write(c.Independent?.Identity??JsonUtility.ToJson(c.Single));}
     using(var sha=System.Security.Cryptography.SHA256.Create())Check(c.GeometryIdentity==BitConverter.ToString(sha.ComputeHash(buffer.ToArray())).Replace("-","").ToLowerInvariant(),"original procedural identity byte contract");
    }
   }
   File.WriteAllText(Path.Combine(Output,"geometry.json"),JsonUtility.ToJson(d,true));
   File.WriteAllText(Path.Combine(Output,"geometry-result.txt"),$"AUTHORED_GEOMETRY_OK assertions={assertions} length={d.totalLength} samples={d.samples.Length} identity={course.GeometryIdentity}\n");Debug.Log("AUTHORED_GEOMETRY_OK assertions="+assertions);
  }
 }
}
