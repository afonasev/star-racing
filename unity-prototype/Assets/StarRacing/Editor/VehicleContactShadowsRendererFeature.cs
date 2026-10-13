using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace StarRacingPrototype {
 public static class VehicleContactShadowsRendererFeature {
  public const float Radius=.025f;
  public const float Intensity=1.2f;
  public const float DirectLightingStrength=0f;
  const string SettingsPath="m_Settings.";

  public static ScreenSpaceAmbientOcclusion EnsureConfigured(UniversalRendererData renderer) {
   if(renderer==null)throw new ArgumentNullException(nameof(renderer));
   ScreenSpaceAmbientOcclusion feature=null;
   foreach(var candidate in renderer.rendererFeatures) {
    if(candidate is not ScreenSpaceAmbientOcclusion ssao)continue;
    if(feature!=null)throw new InvalidOperationException("Renderer contains duplicate SSAO features.");
    feature=ssao;
   }
   bool changed=false;
   if(feature==null) {
    feature=ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
    feature.name="Vehicle Contact SSAO";
    renderer.rendererFeatures.Add(feature);
    AssetDatabase.AddObjectToAsset(feature,renderer);
    changed=true;
   }
   if(!feature.isActive){feature.SetActive(true);changed=true;}
   var serialized=new SerializedObject(feature);
   changed|=Set(serialized,SettingsPath+"AOMethod",0);
   changed|=Set(serialized,SettingsPath+"Downsample",false);
   changed|=Set(serialized,SettingsPath+"AfterOpaque",false);
   changed|=Set(serialized,SettingsPath+"Source",1);
   changed|=Set(serialized,SettingsPath+"NormalSamples",1);
   changed|=Set(serialized,SettingsPath+"Intensity",Intensity);
   changed|=Set(serialized,SettingsPath+"DirectLightingStrength",DirectLightingStrength);
   changed|=Set(serialized,SettingsPath+"Radius",Radius);
   changed|=Set(serialized,SettingsPath+"Samples",1);
   changed|=Set(serialized,SettingsPath+"BlurQuality",1);
   changed|=Set(serialized,SettingsPath+"Falloff",100f);
   changed|=Set(serialized,SettingsPath+"SampleCount",-1);
   changed|=serialized.ApplyModifiedProperties();
   if(changed) {
    renderer.SetDirty();
    EditorUtility.SetDirty(renderer);
    AssetDatabase.SaveAssets();
   }
   return feature;
  }

  static bool Set(SerializedObject serialized,string path,float value) {
   var property=serialized.FindProperty(path);
   if(property==null)throw new InvalidOperationException("URP SSAO property not found: "+path);
   if(Mathf.Approximately(property.floatValue,value))return false;
   property.floatValue=value;
   return true;
  }
  static bool Set(SerializedObject serialized,string path,int value) {
   var property=serialized.FindProperty(path);
   if(property==null)throw new InvalidOperationException("URP SSAO property not found: "+path);
   if(property.intValue==value)return false;
   property.intValue=value;
   return true;
  }
  static bool Set(SerializedObject serialized,string path,bool value) {
   var property=serialized.FindProperty(path);
   if(property==null)throw new InvalidOperationException("URP SSAO property not found: "+path);
   if(property.boolValue==value)return false;
   property.boolValue=value;
   return true;
  }
 }
}
