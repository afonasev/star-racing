using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace StarRacingPrototype {
 [InitializeOnLoad] public static class LoadingVisualCapture {
  const string Key="StarRacing.LoadingVisual";static double deadline;static int frames;
  public static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_LOADING_QA_DIR");
  static LoadingVisualCapture(){EditorApplication.playModeStateChanged+=Changed;}
  public static void Run(){
   if(!Environment.GetCommandLineArgs().Contains("-noaudio")||!Environment.GetCommandLineArgs().Contains("-muted"))throw new Exception("Visual capture requires silent startup");
   Directory.CreateDirectory(Path.Combine(Output,"frames"));SessionState.SetBool(Key,true);SessionState.SetBool(Key+"OK",false);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var view=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));view.position=new Rect(0,0,1000,600);view.Show();frames=0;EditorApplication.update+=Enter;
  }
  static void Enter(){if(++frames<12)return;EditorApplication.update-=Enter;EditorApplication.isPlaying=true;}
  static void Changed(PlayModeStateChange state){if(!SessionState.GetBool(Key,false))return;if(state==PlayModeStateChange.EnteredPlayMode){deadline=EditorApplication.timeSinceStartup+120;EditorApplication.update+=Watch;new GameObject("Native loading capture").AddComponent<LoadingVisualDriver>();}if(state==PlayModeStateChange.EnteredEditMode){EditorApplication.update-=Watch;SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"OK",false)?0:1);}}
  static void Watch(){EditorApplication.QueuePlayerLoopUpdate();if(EditorApplication.timeSinceStartup>deadline)Finish(false);}
  public static void Finish(bool success){SessionState.SetBool(Key+"OK",success);EditorApplication.isPlaying=false;}
 }
 public sealed class LoadingVisualDriver:MonoBehaviour {
  bool background;string settingsPath;UserSettings settings;
  [Serializable] sealed class Receipt{public bool success=true,playerBuilt=false;public string source="Unity Editor IMGUI, actual realtime animation, simulation paused, native screenshot, resized only when encoding the preview";public float[] timestamps,positions;public int renderedFrames;}
  IEnumerator Start(){
   background=Application.runInBackground;Application.runInBackground=true;settingsPath=UserSettingsStore.OverridePath;settings=UserSettingsStore.Current;UserSettingsStore.OverridePath=Path.Combine(LoadingVisualCapture.Output,"settings.json");UserSettingsStore.Replace(new UserSettings());Time.timeScale=0;
   var loading=gameObject.AddComponent<RaceLoading>();loading.Status="Готовим трассу";for(int i=0;i<4;i++)yield return null;
   var receipt=new Receipt{timestamps=new float[96],positions=new float[96]};bool success=true;
   for(int i=0;i<receipt.timestamps.Length;i++){
    yield return new WaitForEndOfFrame();
    receipt.timestamps[i]=loading.AnimationTime;receipt.positions[i]=loading.SweepPosition;
    try{Save(Path.Combine(LoadingVisualCapture.Output,"frames","frame-"+i.ToString("D4")+".png"));}catch(Exception e){File.WriteAllText(Path.Combine(LoadingVisualCapture.Output,"failure.txt"),e.ToString());success=false;break;}
    float next=Time.realtimeSinceStartup+.05f;while(Time.realtimeSinceStartup<next)yield return null;
   }
   receipt.renderedFrames=loading.RenderedFrames;success&=receipt.renderedFrames>3&&receipt.positions.Distinct().Count()>20&&Time.timeScale==0&&SilentStartup.Enabled;
   receipt.success=success;
   File.WriteAllText(Path.Combine(LoadingVisualCapture.Output,"receipt.json"),JsonUtility.ToJson(receipt,true));
   LoadingVisualCapture.Finish(success);
  }
  static void Save(string path){ScreenCapture.CaptureScreenshot(path);}
  void OnDestroy(){Time.timeScale=1;Application.runInBackground=background;UserSettingsStore.OverridePath=settingsPath;if(settings!=null)UserSettingsStore.Replace(settings);}
 }
}
