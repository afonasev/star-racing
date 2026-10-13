using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace StarRacingPrototype {
 public static class VehicleContactShadowsChecks {
  static int assertions;
  static void Check(bool valid,string message){assertions++;if(!valid)throw new Exception("VEHICLE_CONTACT_SSAO: "+message);}

  public static void Run() {
   assertions=0;
   var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/StarRacing/Generated/PrototypeRenderer.asset");
   Check(renderer!=null,"generated renderer exists");
   NitroEdgeBlurRendererFeature.EnsureConfigured(renderer);
   var nitro=renderer.rendererFeatures.SingleOrDefault(feature=>feature is NitroEdgeBlurRendererFeature);
   Check(nitro!=null,"existing Nitro renderer feature exists");
   int count=renderer.rendererFeatures.Count;
   bool hadSsao=renderer.rendererFeatures.Any(feature=>feature is ScreenSpaceAmbientOcclusion);
   var ssao=VehicleContactShadowsRendererFeature.EnsureConfigured(renderer);
   Check(renderer.rendererFeatures.Count==count+(hadSsao?0:1),"configuration adds no duplicate feature");
   count=renderer.rendererFeatures.Count;
   var second=VehicleContactShadowsRendererFeature.EnsureConfigured(renderer);
   Check(ReferenceEquals(ssao,second)&&renderer.rendererFeatures.Count==count,"repeated application is idempotent");
   Check(renderer.rendererFeatures.Count(feature=>feature is ScreenSpaceAmbientOcclusion)==1,"SSAO is unique");
   Check(renderer.rendererFeatures.IndexOf(nitro)<renderer.rendererFeatures.IndexOf(ssao),"Nitro feature and order are preserved");
   var serialized=new SerializedObject(ssao);
   Check(ssao.isActive,"SSAO is active");
   Check(Mathf.Approximately(serialized.FindProperty("m_Settings.Radius").floatValue,VehicleContactShadowsRendererFeature.Radius),"small selected radius");
   Check(Mathf.Approximately(serialized.FindProperty("m_Settings.Intensity").floatValue,VehicleContactShadowsRendererFeature.Intensity),"moderate selected intensity");
   Check(Mathf.Approximately(serialized.FindProperty("m_Settings.DirectLightingStrength").floatValue,0),"direct lighting strength stays zero");
   Check(serialized.FindProperty("m_Settings.Source").intValue==1,"DepthNormals source");
   Check(!serialized.FindProperty("m_Settings.AfterOpaque").boolValue,"before opaque injection");
   Check(serialized.FindProperty("m_Settings.SampleCount").intValue==-1,"current URP sample option is active");
   var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/StarRacing/Generated/PrototypePipeline.asset");
   Check(pipeline!=null&&pipeline.mainLightShadowmapResolution==2048&&pipeline.shadowCascadeCount==4&&Mathf.Approximately(pipeline.shadowDistance,160),"directional shadow budget unchanged");
   Debug.Log("VEHICLE_CONTACT_SSAO_OK assertions="+assertions+" package=URP17.3 radius="+VehicleContactShadowsRendererFeature.Radius+" intensity="+VehicleContactShadowsRendererFeature.Intensity+" directLighting=0 source=DepthNormals afterOpaque=false");
  }
 }
}
