using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace StarRacingPrototype {
 [DisallowMultipleRendererFeature("Nitro Edge Blur")]
 public sealed class NitroEdgeBlurRendererFeature : ScriptableRendererFeature {
  static readonly int StrengthId=Shader.PropertyToID("_Strength");
  Material material;

  public override void Create(){
   var shader=Resources.Load<Shader>("Effects/NitroEdgeBlur");
   if(shader==null){Debug.LogError("Missing Resources/Effects/NitroEdgeBlur shader.");return;}
   if(material==null)material=CoreUtils.CreateEngineMaterial(shader);
  }

  public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData renderingData){
   var camera=renderingData.cameraData.camera;
   if(material==null||camera.cameraType!=CameraType.Game||!camera.TryGetComponent<NitroEdgeBlur>(out var effect)||effect.Strength<=0.0001f)return;
   renderer.EnqueuePass(new NitroEdgeBlurPass(material,effect.Strength));
  }

  protected override void Dispose(bool disposing){CoreUtils.Destroy(material);material=null;}

  public static void EnsureConfigured(UniversalRendererData renderer){
   if(renderer==null)return;
   var features=renderer.rendererFeatures;
   if(features==null)return;
   if(features.Exists(feature=>feature is NitroEdgeBlurRendererFeature))return;
   var feature=CreateInstance<NitroEdgeBlurRendererFeature>();
   feature.name="Nitro Edge Blur";
   renderer.rendererFeatures.Add(feature);
   AssetDatabaseHelper.AddSubAsset(feature,renderer);
   AssetDatabaseHelper.MarkDirty(renderer);
  }

  sealed class NitroEdgeBlurPass:ScriptableRenderPass {
   readonly Material material;
   readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
   readonly float strength;

   public NitroEdgeBlurPass(Material material,float strength){
    this.material=material;
    this.strength=Mathf.Clamp01(strength);
    renderPassEvent=RenderPassEvent.AfterRenderingPostProcessing;
    requiresIntermediateTexture=true;
   }

   public override void RecordRenderGraph(RenderGraph renderGraph,ContextContainer frameData){
    if(material==null)return;
    var resources=frameData.Get<UniversalResourceData>();
    var cameraData=frameData.Get<UniversalCameraData>();
    if(!resources.cameraColor.IsValid())return;
    var descriptor=cameraData.cameraTargetDescriptor;
    descriptor.width=Mathf.Max(1,cameraData.scaledWidth);
    descriptor.height=Mathf.Max(1,cameraData.scaledHeight);
    descriptor.depthBufferBits=0;
    descriptor.msaaSamples=1;
    descriptor.bindMS=false;
    descriptor.useMipMap=false;
    descriptor.autoGenerateMips=false;
    var destination=UniversalRenderer.CreateRenderGraphTexture(renderGraph,descriptor,"_NitroEdgeBlurColor",false);
    properties.SetFloat(StrengthId,strength);
    var parameters=new RenderGraphUtils.BlitMaterialParameters(resources.cameraColor,destination,material,0,properties);
    renderGraph.AddBlitPass(parameters,passName:"Nitro Edge Blur");
    resources.cameraColor=destination;
   }
  }
 }

 // Keep renderer asset mutation in the Editor builder while this feature stays runtime-safe.
 static class AssetDatabaseHelper {
  public static void AddSubAsset(ScriptableRendererFeature feature,UniversalRendererData renderer){
#if UNITY_EDITOR
   UnityEditor.AssetDatabase.AddObjectToAsset(feature,renderer);
#endif
  }
  public static void MarkDirty(UniversalRendererData renderer){
#if UNITY_EDITOR
   UnityEditor.EditorUtility.SetDirty(renderer);
   renderer.SetDirty();
   UnityEditor.AssetDatabase.SaveAssets();
#endif
  }
 }
}
