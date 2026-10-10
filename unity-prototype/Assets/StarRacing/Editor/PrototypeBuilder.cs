using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace StarRacingPrototype {
 public static class PrototypeBuilder {
  public static void BuildMac(){Prepare();Build(BuildTarget.StandaloneOSX,"Builds/macOS/Star Racing.app");}
  public static void BuildWindows(){Prepare();Build(BuildTarget.StandaloneWindows64,"Builds/Windows/Star Racing.exe");}
  public static void Prepare(){
   ReleaseBalance.Parse(File.ReadAllText("Assets/StarRacing/Resources/balance-config.json"));
   Directory.CreateDirectory("Assets/StarRacing/Generated");
   var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/StarRacing/Generated/PrototypeRenderer.asset");
   if(renderer==null){renderer=ScriptableObject.CreateInstance<UniversalRendererData>();AssetDatabase.CreateAsset(renderer,"Assets/StarRacing/Generated/PrototypeRenderer.asset");}
   var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/StarRacing/Generated/PrototypePipeline.asset");
   if(pipeline==null){pipeline=UniversalRenderPipelineAsset.Create(renderer);AssetDatabase.CreateAsset(pipeline,"Assets/StarRacing/Generated/PrototypePipeline.asset");}
   pipeline.shadowDistance=160;pipeline.shadowCascadeCount=4;pipeline.msaaSampleCount=2;pipeline.renderScale=1;pipeline.mainLightShadowmapResolution=2048;
   GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
   PlayerSettings.companyName="Star Racing";PlayerSettings.productName="Star Racing";PlayerSettings.bundleVersion=Environment.GetEnvironmentVariable("STAR_RACING_VERSION")??"0.2.0";
   PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
   PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone,"local.starracing.prototype");
   ConfigureApplicationIcon();
   PlayerSettings.defaultScreenWidth=1600;PlayerSettings.defaultScreenHeight=900;PlayerSettings.fullScreenMode=FullScreenMode.FullScreenWindow;PlayerSettings.runInBackground=false;PlayerSettings.resizableWindow=true;
   PlayerSettings.SetArchitecture(NamedBuildTarget.Standalone,2);
   var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);new GameObject("Star Racing").AddComponent<RaceStartup>();
   EditorSceneManager.SaveScene(scene,"Assets/StarRacing/Generated/Prototype.unity");EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/StarRacing/Generated/Prototype.unity",true)};AssetDatabase.SaveAssets();
  }
  public static void ConfigureApplicationIcon(){
   var icon=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/StarRacing/Resources/Star-Racing-Icon.png");
   if(icon==null)throw new Exception("Star Racing application icon missing");
   var sizes=PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone,IconKind.Any);
   if(sizes.Length==0)throw new Exception("Standalone icon slots missing");
   PlayerSettings.SetIcons(NamedBuildTarget.Standalone,Array.ConvertAll(sizes,_=>icon),IconKind.Any);
  }
  static void Build(BuildTarget target,string output){Directory.CreateDirectory(Path.GetDirectoryName(output));var defines=Environment.GetEnvironmentVariable("STAR_RACING_RELEASE_TRACK")=="test"?new[]{"STAR_RACING_TEST_CHANNEL"}:Environment.GetEnvironmentVariable("STAR_RACING_UNSIGNED_PRODUCTION")=="1"?new[]{"STAR_RACING_UNSIGNED_PRODUCTION"}:Array.Empty<string>();var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/StarRacing/Generated/Prototype.unity"},locationPathName=output,target=target,extraScriptingDefines=defines});if(result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Build failed: "+result.summary.result);Debug.Log("PROTOTYPE_BUILD_OK "+output);}
 }
}
