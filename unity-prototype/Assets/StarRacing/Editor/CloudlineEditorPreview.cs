using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace StarRacingPrototype
{
    // Offscreen URP evidence from the imported runtime model; does not build a Player.
    public static class CloudlineEditorPreview
    {
        public static void CheckAndRender() { PrototypeChecks.RunWithFixtureEquivalence(); Run(); }
        public static void Run()
        {
            string output=Environment.GetEnvironmentVariable("CLOUDLINE_PREVIEW_DIR");
            if(string.IsNullOrEmpty(output))throw new Exception("CLOUDLINE_PREVIEW_DIR required");
            Directory.CreateDirectory(output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/StarRacing/Generated/PrototypePipeline.asset");
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.48f,.52f,.6f);
            var light=new GameObject("Preview sun").AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.transform.rotation=Quaternion.Euler(45,-35,0);
            var fill=new GameObject("Preview fill").AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=.8f;fill.transform.rotation=Quaternion.Euler(20,160,0);
            var ground=GameObject.CreatePrimitive(PrimitiveType.Cube);ground.transform.position=new Vector3(0,VehicleGeometry.ChassisMinY-.08f,0);ground.transform.localScale=new Vector3(30,.1f,30);
            var floor=new Material(Shader.Find("Universal Render Pipeline/Lit"));floor.SetColor("_BaseColor",new Color(.12f,.16f,.2f));ground.GetComponent<Renderer>().sharedMaterial=floor;
            var camera=new GameObject("Preview camera").AddComponent<Camera>();camera.transform.position=new Vector3(5,2.7f,-8);camera.transform.LookAt(new Vector3(0,.1f,-.6f));camera.fieldOfView=37;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.05f,.07f);camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
            var volume=new GameObject("Preview bloom").AddComponent<Volume>();volume.isGlobal=true;volume.profile=ScriptableObject.CreateInstance<VolumeProfile>();volume.profile.Add<Bloom>().intensity.Override(.25f);volume.profile.Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
            var root=new GameObject("Player 1");var visual=StaticVehicleVisual.Create(root.transform,CloudlineSkin.PlayerColors[0],true);
            var rt=new RenderTexture(1280,800,24,RenderTextureFormat.ARGB32);rt.Create();
            try {
                for(int i=0;i<20;i++)visual.Exhaust.Step(.25f,false,true,.01f);Capture(camera,rt,output,"01-partial-gas");
                visual.Exhaust.ResetEffect();for(int i=0;i<180;i++)visual.Exhaust.Step(1,false,true,.01f);Capture(camera,rt,output,"02-held-gas");
                for(int i=0;i<180;i++)visual.Exhaust.Step(1,true,true,.01f);Capture(camera,rt,output,"03-held-nitro");
                visual.Exhaust.ResetEffect();root.transform.position=new Vector3(-3,0,0);
                for(int i=1;i<4;i++){var other=new GameObject("Player "+(i+1));other.transform.position=new Vector3(-3+i*2,0,i*.6f);StaticVehicleVisual.Create(other.transform,CloudlineSkin.PlayerColors[i],true);}
                camera.transform.position=new Vector3(8,5,-13);camera.transform.LookAt(new Vector3(0,0,.4f));camera.fieldOfView=47;Capture(camera,rt,output,"04-player-colors");
                foreach(var message in ShaderUtil.GetShaderMessages(Shader.Find("StarRacing/CloudlineExhaust")))if(message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)throw new Exception(message.message);
                File.WriteAllText(Path.Combine(output,"preview.json"),"{\"success\":true,\"source\":\"Unity Editor imported C05 and URP shader\",\"player_build\":false,\"human_acceptance\":false}");Debug.Log("CLOUDLINE_EDITOR_PREVIEW_OK");
            } finally {rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(floor);}
        }
        static void Capture(Camera camera,RenderTexture rt,string output,string name)
        {
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
            var previous=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
            try {image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());}
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
