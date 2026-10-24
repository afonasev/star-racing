using System;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace StarRacingPrototype {
 public static class NativePhysicsStageDiscovery {
  public static void RunAndBuild(){
   Run();
   var output=Environment.GetEnvironmentVariable("STAR_RACING_NATIVE_STAGE_BUILD");
   if(string.IsNullOrEmpty(output)||!Path.IsPathRooted(output)||Directory.Exists(output))throw new Exception("Fresh absolute diagnostic build required");
   var result=UnityEditor.BuildPipeline.BuildPlayer(new UnityEditor.BuildPlayerOptions{
    scenes=new[]{"Assets/StarRacing/Generated/Prototype.unity"},locationPathName=output,target=UnityEditor.BuildTarget.StandaloneOSX,
    options=UnityEditor.BuildOptions.Development,extraScriptingDefines=new[]{"STAR_RACING_TEST_CHANNEL"}
   });
   if(result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Native stage diagnostic build failed: "+result.summary.result);
   Debug.Log("NATIVE_STAGE_DEVELOPMENT_BUILD_OK "+output);
  }
  public static void Run(){
   string output=Environment.GetEnvironmentVariable("STAR_RACING_NATIVE_STAGE_DIR");if(string.IsNullOrEmpty(output)||!Path.IsPathRooted(output))throw new Exception("Absolute native-stage output required");Directory.CreateDirectory(output);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var prior=Physics.simulationMode;var root=new GameObject("Native stage discovery");
   try{Physics.simulationMode=SimulationMode.Script;root.AddComponent<BoxCollider>();var body=root.AddComponent<Rigidbody>();body.useGravity=false;body.linearVelocity=Vector3.right;for(int i=0;i<8;i++)Physics.Simulate(.01f/8);
    var markers=NativePhysicsStageDiagnostics.Inventory();File.WriteAllText(Path.Combine(output,"editor-inventory.json"),JsonUtility.ToJson(new NativePhysicsStageDiagnostics.Report{editor=true,development=Debug.isDebugBuild,markers=markers},true));if(markers.Length==0)throw new Exception("No native Physics profiler markers");Debug.Log("NATIVE_STAGE_DISCOVERY_OK markers="+markers.Length+" unchanged100Hz8base=true");
   }finally{UnityEngine.Object.DestroyImmediate(root);Physics.simulationMode=prior;}
  }
 }
}
