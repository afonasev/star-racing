using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
namespace StarRacingPrototype {
 public static class BuildingRenderDiagnosticBuild {
  public static void Run(){
   var output=Environment.GetEnvironmentVariable("STAR_RACING_RENDER_BUILD");
   if(string.IsNullOrEmpty(output)||!Path.IsPathRooted(output)||Directory.Exists(output))throw new Exception("Fresh absolute local build output required");
   var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{
    scenes=new[]{"Assets/StarRacing/Generated/Prototype.unity"},locationPathName=output,target=BuildTarget.StandaloneOSX,
    extraScriptingDefines=new[]{"STAR_RACING_TEST_CHANNEL"}
   });
   if(result.summary.result!=BuildResult.Succeeded)throw new Exception("Render diagnostic build: "+result.summary.result);
   UnityEngine.Debug.Log("BUILDING_RENDER_DIAGNOSTIC_BUILD_OK "+output);
  }
 }
}
