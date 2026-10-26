using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace StarRacingPrototype {
 public static class RaceComparisonBuilder {
  public static void Build(){
   string output=Environment.GetEnvironmentVariable("STAR_RACING_COMPARISON_BUILD");
   if(string.IsNullOrEmpty(output)||!Path.IsPathRooted(output)||Directory.Exists(output))throw new ArgumentException("Fresh absolute STAR_RACING_COMPARISON_BUILD required");
   PrototypeBuilder.Prepare();
   PlayerSettings.companyName="Star Racing QA";PlayerSettings.productName="Star Racing Comparison16";
   PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone,"local.starracing.comparison16");
   var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/StarRacing/Generated/Prototype.unity"},locationPathName=output,target=BuildTarget.StandaloneOSX,options=BuildOptions.None});
   if(result.summary.result!=BuildResult.Succeeded)throw new Exception("Comparison build failed "+result.summary.result);
   Debug.Log("COMPARISON16_BUILD_OK "+output);
  }
 }
}
