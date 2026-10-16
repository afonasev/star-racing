using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StarRacingPrototype {
    public static class DaylightVisualChecks {
        static int assertions;
        static void Check(bool valid,string message){assertions++;if(!valid)throw new Exception("DAYLIGHT: "+message);}
        static bool SameColor(Color a,Color b)=>((Vector4)(a-b)).sqrMagnitude<.000001f;
        public static void Run() {
            assertions=0;
            foreach(string name in new[]{"Cyan_Energy","Thin_Red_Taillight"}) {
                var light=Resources.Load<Material>("Vehicle/CloudlineMaterials/"+name);
                Check(light!=null&&light.IsKeywordEnabled("_EMISSION")&&(light.globalIlluminationFlags&MaterialGlobalIlluminationFlags.AnyEmissive)!=0,"vehicle emission enabled "+name);
                Check(light.GetColor("_EmissionColor").maxColorComponent>1.1f&&light.GetColor("_EmissionColor").maxColorComponent<2.2f,"bounded HDR vehicle accent "+name);
            }
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/StarRacing/Generated/PrototypePipeline.asset");
            Check(pipeline.mainLightShadowmapResolution==2048&&pipeline.shadowCascadeCount==4&&pipeline.shadowDistance==160,"existing shadow budget");
            var rendererData=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/StarRacing/Generated/PrototypeRenderer.asset");
            var ssao=rendererData.rendererFeatures.Find(feature=>feature is ScreenSpaceAmbientOcclusion) as ScreenSpaceAmbientOcclusion;
            Check(ssao!=null&&ssao.isActive,"built-in contact SSAO active");
            var ssaoSettings=new SerializedObject(ssao);
            Check(Mathf.Approximately(ssaoSettings.FindProperty("m_Settings.DirectLightingStrength").floatValue,0),"SSAO does not attenuate direct daylight");
            Check(ssaoSettings.FindProperty("m_Settings.Source").intValue==1&&!ssaoSettings.FindProperty("m_Settings.AfterOpaque").boolValue,"DepthNormals before opaque");
            Check(rendererData.rendererFeatures.Count(feature=>feature is NitroEdgeBlurRendererFeature)==1,"Nitro renderer feature preserved");
            Check(pipeline.shadowDepthBias>0&&pipeline.shadowDepthBias<1&&pipeline.shadowNormalBias>0&&pipeline.shadowNormalBias<1,"grounded shadow bias");
            var previousSky=RenderSettings.skybox;var previousCube=RenderSettings.customReflectionTexture;
            var previousMode=RenderSettings.defaultReflectionMode;float previousIntensity=RenderSettings.reflectionIntensity;
            var ambientMode=RenderSettings.ambientMode;var ambientSky=RenderSettings.ambientSkyColor;
            var ambientEquator=RenderSettings.ambientEquatorColor;var ambientGround=RenderSettings.ambientGroundColor;
            var previousSun=RenderSettings.sun;bool fog=RenderSettings.fog;var fogMode=RenderSettings.fogMode;float fogDensity=RenderSettings.fogDensity;var fogColor=RenderSettings.fogColor;
            var root=new GameObject("Daylight lifecycle checks");TrackSky active=null,prepared=null;
            try {
                foreach(string name in new[]{"DaylightReflection","SpaceReflection"}) {
                    var cube=Resources.Load<Cubemap>("Environment/Sky/"+name);
                    Check(cube!=null&&cube.width==128&&cube.mipmapCount==8,"reflection cube budget/mips "+name);
                    var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(cube));
                    var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);
                    Check(!importer.sRGBTexture&&!importer.isReadable&&settings.cubemapConvolution==TextureImporterCubemapConvolution.Specular,"linear filtered non-readable cube "+name);
                }
                var sunGo=new GameObject("Sun");sunGo.transform.SetParent(root.transform);var sun=sunGo.AddComponent<Light>();sun.type=LightType.Directional;
                var fillGo=new GameObject("Fill");fillGo.transform.SetParent(root.transform);var fill=fillGo.AddComponent<Light>();fill.type=LightType.Directional;
                foreach(string theme in new[]{"cloud-city","cloud-city","space-station","cloud-city"}) {
                    var route=new TrackRoute(Procedural.Generator.Generate(77,"normal",theme,false,true));
                    var plan=EnvironmentPlan.Create(route,77);var profile=plan.lighting;
                    // Generator safety fallback may return a different effective seed. Lighting uses that identity.
                    Check(profile.HashCode()==RaceLightingProfile.Create(plan.seed,plan.theme).HashCode(),"repeat profile");
                    var changed=profile;changed.ambientEquatorColor.r+=.01f;Check(profile.HashCode()!=changed.HashCode(),"equator missing in profile identity");
                    changed=profile;changed.ambientGroundColor.b+=.01f;Check(profile.HashCode()!=changed.HashCode(),"ground missing in profile identity");
                    changed=profile;changed.reflectionIntensity+=.01f;Check(profile.HashCode()!=changed.HashCode(),"reflection missing in profile identity");
                    changed=profile;changed.fogDensity+=.0001f;Check(profile.HashCode()!=changed.HashCode(),"fog density missing in profile identity");
                    changed=profile;changed.fogColor.g+=.01f;Check(profile.HashCode()!=changed.HashCode(),"fog color missing in profile identity");
                    var sky=RenderSettings.skybox;var cube=RenderSettings.customReflectionTexture;float intensity=RenderSettings.reflectionIntensity;
                    prepared=new TrackSky(plan,route,root.transform);
                    Check(RenderSettings.skybox==sky&&RenderSettings.customReflectionTexture==cube&&RenderSettings.reflectionIntensity==intensity,"staging mutated active globals");
                    prepared.Activate();var current=prepared.Material;var reflection=prepared.Reflection;
                    Check(RenderSettings.customReflectionTexture==reflection&&RenderSettings.defaultReflectionMode==DefaultReflectionMode.Custom,"activate reflection");
                    Check(Mathf.Abs(RenderSettings.reflectionIntensity-profile.reflectionIntensity)<.001f,"theme intensity");
                    active?.Clear();active?.Clear();
                    Check(RenderSettings.skybox==current&&RenderSettings.customReflectionTexture==reflection,"old cleanup erased new sky/reflection");
                    Check(reflection!=null,"cleanup destroyed shared Resources cube");
                    active=prepared;prepared=null;
                    RaceLightingController.Apply(profile,sun,fill,null);
                    Check(RenderSettings.fog&&RenderSettings.fogMode==FogMode.ExponentialSquared&&
                        Mathf.Approximately(RenderSettings.fogDensity,profile.fogDensity)&&SameColor(RenderSettings.fogColor,profile.fogColor),"fog binding/theme reset");
                    if(theme=="cloud-city") {
                        float near=Mathf.Exp(-Mathf.Pow(profile.fogDensity*100,2));
                        // Metropolis request extends visible atmospheric depth to the surrounding districts.
                        float far=Mathf.Exp(-Mathf.Pow(profile.fogDensity*1800,2));
                        Check(near>.97f&&far<.5f&&near>far,"near road legibility / metropolis horizon attenuation at 1800m");
                    } else Check(Mathf.Approximately(profile.fogDensity,.00007f)&&SameColor(profile.fogColor,profile.skyColor),"space fog unchanged");
                    Check(RenderSettings.ambientMode==AmbientMode.Trilight,"ambient mode");
                    Check(SameColor(RenderSettings.ambientSkyColor,profile.ambientColor)&&
                        SameColor(RenderSettings.ambientEquatorColor,profile.ambientEquatorColor)&&
                        SameColor(RenderSettings.ambientGroundColor,profile.ambientGroundColor),"directional ambient binding");
                    Check(profile.ambientColor.maxColorComponent>profile.ambientGroundColor.maxColorComponent,"top/bottom distinction");
                    Check(fill.enabled==(theme=="space-station")&&sun.shadows==LightShadows.Soft,"existing directional lights");
                    Check(profile.kind==(theme=="cloud-city"?"day":"space"),"theme kind");
                }
                var shared=active.Reflection;active.Clear();active.Clear();active=null;
                Check(RenderSettings.skybox==null&&RenderSettings.customReflectionTexture==null,"active cleanup globals");
                Check(shared!=null,"shared cube lifetime");
                Check(root.GetComponentsInChildren<ReflectionProbe>().Length==0,"runtime reflection capture added");
                var paint=Resources.Load<Material>("Vehicle/CloudlineMaterials/Liquid_Silver");
                foreach(var color in CloudlineSkin.PlayerColors) {
                    var holder=new GameObject("Color check");holder.transform.SetParent(root.transform);
                    var visual=StaticVehicleVisual.Create(holder.transform,color,true);
                    var renderer=Array.Find(visual.GetComponentsInChildren<MeshRenderer>(),r=>r.name=="Liquid_Silver");
                    var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
                    Check(renderer.sharedMaterial==paint&&block.GetColor("_BaseColor")==color,"shared paint/participant color");
                    UnityEngine.Object.DestroyImmediate(holder);
                }
                Debug.Log("DAYLIGHT_CHECKS_OK assertions="+assertions+" staging activation oldCleanup sharing import colors");
            } finally {
                prepared?.Clear();active?.Clear();UnityEngine.Object.DestroyImmediate(root);
                RenderSettings.skybox=previousSky;RenderSettings.customReflectionTexture=previousCube;RenderSettings.defaultReflectionMode=previousMode;RenderSettings.reflectionIntensity=previousIntensity;
                RenderSettings.ambientMode=ambientMode;RenderSettings.ambientSkyColor=ambientSky;RenderSettings.ambientEquatorColor=ambientEquator;RenderSettings.ambientGroundColor=ambientGround;
                RenderSettings.sun=previousSun;RenderSettings.fog=fog;RenderSettings.fogMode=fogMode;RenderSettings.fogDensity=fogDensity;RenderSettings.fogColor=fogColor;
            }
        }
    }
}
