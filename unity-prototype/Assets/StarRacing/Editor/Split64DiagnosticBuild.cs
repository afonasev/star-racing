using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
namespace StarRacingPrototype {
 public static class Split64DiagnosticBuild {
  public static void Run(){
   string output=Environment.GetEnvironmentVariable("STAR_RACING_SPLIT64_BUILD");
   if(string.IsNullOrEmpty(output)||!Path.IsPathRooted(output)||Directory.Exists(output))throw new Exception("Fresh absolute local build required");
   string identity=PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Standalone),product=PlayerSettings.productName;
   try{
    PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone,"local.starracing.split64");PlayerSettings.productName="Star Racing Split64 QA";
    var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/StarRacing/Generated/Prototype.unity"},locationPathName=output,target=BuildTarget.StandaloneOSX,extraScriptingDefines=new[]{"STAR_RACING_TEST_CHANNEL"}});
    if(result.summary.result!=BuildResult.Succeeded)throw new Exception("Split64 diagnostic build: "+result.summary.result);
    UnityEngine.Debug.Log("SPLIT64_DIAGNOSTIC_BUILD_OK "+output);
   }finally{PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone,identity);PlayerSettings.productName=product;AssetDatabase.SaveAssets();}
  }
 }
}
