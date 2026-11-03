using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
namespace StarRacingPrototype {
 [InitializeOnLoad] public static class AuthoredCourseLifecycleChecks {
  const string Key="StarRacing.AuthoredLifecycle";static double deadline;
  static AuthoredCourseLifecycleChecks(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){AuthoredCourseChecks.Run();SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",true);EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.isPlaying=true;}
  static void Changed(PlayModeStateChange state){if(!SessionState.GetBool(Key,false))return;
   if(state==PlayModeStateChange.EnteredPlayMode){deadline=EditorApplication.timeSinceStartup+360;EditorApplication.update+=Guard;new GameObject("Authored lifecycle checks").AddComponent<AuthoredLifecycleDriver>();}
   if(state==PlayModeStateChange.EnteredEditMode){EditorApplication.update-=Guard;SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",true)?1:0);}
  }
  static void Guard(){EditorApplication.QueuePlayerLoopUpdate();if(EditorApplication.timeSinceStartup>deadline)Finish(new TimeoutException("Lifecycle deadline"));}
  public static void Finish(Exception error=null){if(error!=null){File.WriteAllText(Path.Combine(AuthoredCourseChecks.Output,"failure.txt"),error.ToString());Debug.LogException(error);}SessionState.SetBool(Key+"Failed",error!=null);EditorApplication.isPlaying=false;}
 }
 public sealed class AuthoredLifecycleDriver:MonoBehaviour {
  bool had,restored;string saved,path;UserSettings settings;Gamepad pad;GameObject host;
  IEnumerator Start(){had=PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey);saved=PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey);settings=UserSettingsStore.Current;path=UserSettingsStore.OverridePath;
   UserSettingsStore.OverridePath=Path.Combine(AuthoredCourseChecks.Output,"settings.json");UserSettingsStore.Replace(new UserSettings());pad=InputSystem.AddDevice<Gamepad>();var cfg=AuthoredCourseChecks.Config(8,1);cfg.devices[0]=SessionControllers.Slot(pad);cfg.Save();
   Exception error=null;var run=Run();while(true){bool more;try{more=run.MoveNext();}catch(Exception e){error=e;break;}if(!more)break;yield return run.Current;}Restore();yield return null;AuthoredCourseLifecycleChecks.Finish(error);
  }
  IEnumerator Run(){
   host=new GameObject("Normal startup");host.AddComponent<RaceStartup>();RaceDirector director=null;while(director==null||!director.Ready){director=host.GetComponent<RaceDirector>();yield return null;}
   var menu=host.GetComponent<RaceMenu>();string identity=null;
   foreach(string mode in AuthoredCourseChecks.Orbital?new[]{"orbital","authored","space-station","orbital"}:new[]{"authored","cloud-city","space-station","authored"}){
    bool authored=mode=="authored"||mode=="orbital";string id=mode=="orbital"?AuthoredOrbitalRiftCourse.Id:mode=="authored"?AuthoredCloudCityCourse.Id:"";
    menu.Open(RaceMenuScreen.LocalSetup);menu.SelectCourse(id);if(!authored)menu.Selected.theme=mode;
    menu.StartSelected();while(menu.Loading)yield return null;
    if(!director.Started||director.Course.IsAuthored!=authored||(director.Course.CourseId??"")!=id||director.Course.Settings.theme!=(authored?AuthoredCourses.Theme(id):mode))throw new Exception("Wrong world after menu selection: "+mode+" "+menu.Error);
    director.SetPaused(true);director.AppliedConfig.replay.Verify(director.Course,director.Track);
    if(mode==(AuthoredCourseChecks.Orbital?"orbital":"authored")){
     if(identity!=null&&identity!=director.Course.GeometryIdentity)throw new Exception("Authored identity changed on return");identity=director.Course.GeometryIdentity;
     var chase=director.CameraFor(0);
     foreach(var section in director.Course.Single.composition){director.Cars[0].ResetAt(30+section.startIndex*5+45,0);chase.Snap();yield return null;yield return null;AuthoredPreviewDriver.Capture(chase.GetComponent<Camera>(),"section-"+section.index);}
     if(mode=="orbital"){
      var orbit=director.Course.Single.composition[5];
      for(int i=0;i<=5;i++){director.Cars[0].ResetAt(30+Mathf.Lerp(orbit.startIndex,orbit.endIndex,i/5f)*5,0);chase.Snap();yield return null;yield return null;AuthoredPreviewDriver.Capture(chase.GetComponent<Camera>(),"orbit-"+i);}
     }
     var oldTrack=director.Track;menu.RepeatRace();while(menu.Loading)yield return null;director.SetPaused(true);if(director.Track!=oldTrack)throw new Exception("Same world repeat replaced track");
    }
    File.AppendAllText(Path.Combine(AuthoredCourseChecks.Output,"lifecycle.txt"),mode+" "+director.Course.GeometryIdentity+" PASS\n");
    if(!director.ExitToMenu())throw new Exception("Exit to setup failed");yield return null;
   }
   Debug.Log("AUTHORED_LIFECYCLE_OK selected authored world transitions, replay and repeat");
  }
  void Restore(){if(restored)return;restored=true;if(had)PlayerPrefs.SetString(LocalRaceConfig.PreferenceKey,saved);else PlayerPrefs.DeleteKey(LocalRaceConfig.PreferenceKey);PlayerPrefs.Save();UserSettingsStore.OverridePath=path;UserSettingsStore.Replace(settings);if(host!=null)Destroy(host);if(pad!=null&&pad.added)InputSystem.RemoveDevice(pad);Time.timeScale=1;}
  void OnDestroy()=>Restore();
 }
}
