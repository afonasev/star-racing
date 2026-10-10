using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
namespace StarRacingPrototype
{
    // Preview only. Production exhaust/envelope are never modified by the study.
    public static class ExhaustArtStudies
    {
        public static void Render()
        {
            string output=Environment.GetEnvironmentVariable("STAR_RACING_EXHAUST_ART_PROOF");
            if(string.IsNullOrEmpty(output))throw new Exception("Set STAR_RACING_EXHAUST_ART_PROOF");
            Directory.CreateDirectory(output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/StarRacing/Generated/PrototypePipeline.asset");
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.gray;
            var root=new GameObject("Exhaust art study");
            var visual=StaticVehicleVisual.Create(root.transform,new Color(.55f,.65f,.8f),true);
            var sun=new GameObject("Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=2;sun.transform.rotation=Quaternion.Euler(45,-30,0);
            var camera=new GameObject("Study camera").AddComponent<Camera>();camera.transform.position=new Vector3(4,4,-9);camera.transform.LookAt(new Vector3(0,.3f,-1.2f));
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.17f,.21f,.28f);camera.fieldOfView=35;
            var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/StarRacing/Editor/ExhaustStudies/ExhaustArtStudy.shader");
            if(shader==null)throw new Exception("Missing study shader");
            string repo=Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
            foreach(string style in new[]{"1-natural","2-plasma"})
            {
                var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
                if(!texture.LoadImage(File.ReadAllBytes(Path.Combine(repo,"art/exhaust-art-studies",style+".png"))))throw new Exception("Invalid study atlas");
                texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Bilinear;
                var material=new Material(shader);material.SetTexture("_MainTex",texture);
                foreach(var renderer in visual.Exhaust.GetComponentsInChildren<MeshRenderer>()){
                    renderer.sharedMaterial=material;renderer.SetPropertyBlock(null);
                }
                foreach(bool nitro in new[]{false,true})
                {
                    visual.Exhaust.ResetEffect();for(int i=0;i<400;i++)visual.Exhaust.Step(1,nitro,true,.01f);
                    material.SetFloat("_Cell",nitro?1:0);
                    foreach(var renderer in visual.Exhaust.GetComponentsInChildren<MeshRenderer>())renderer.SetPropertyBlock(null);
                    var target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);var previous=RenderTexture.active;
                    try{
                        camera.targetTexture=target;camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();
                        File.WriteAllBytes(Path.Combine(output,style+(nitro?"-nitro":"-gas")+".png"),pixels.EncodeToPNG());
                    }finally{
                        camera.targetTexture=null;RenderTexture.active=previous;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);
                    }
                }
                UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(texture);
            }
            if(ShaderUtil.ShaderHasError(shader))throw new Exception("Study shader compilation failed");
            Debug.Log("EXHAUST_ART_STUDIES_OK styles=2 states=gas,nitro current-envelope-and-scale phase=0.38");
        }
    }
}
