using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace StarRacingPrototype
{
    [InitializeOnLoad]
    public static class ExhaustAppearanceChecks
    {
        const string Pending="StarRacing.ExhaustProof.Pending", Output="StarRacing.ExhaustProof.Output", Result="StarRacing.ExhaustProof.Result";
        static IEnumerator routine;
        static ExhaustAppearanceProof proof;
        static double started;
        static ExhaustAppearanceChecks(){EditorApplication.playModeStateChanged+=OnState;}
        public static void Run()
        {
            string output=Environment.GetEnvironmentVariable("STAR_RACING_EXHAUST_PROOF");
            if(string.IsNullOrEmpty(output))throw new Exception("Set STAR_RACING_EXHAUST_PROOF");
            Directory.CreateDirectory(output);
            var shader=Shader.Find("StarRacing/CloudlineExhaust");
            if(shader==null||ShaderUtil.ShaderHasError(shader))throw new Exception("Shipping exhaust shader failed compilation");
            // Complete affected fixture, including actual countdown/pause/input/reset integration.
            AudioListener.volume=0;CloudlineVehicleChecks.Run();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            SessionState.SetString(Output,output);SessionState.SetInt(Result,1);SessionState.SetBool(Pending,true);
            EditorApplication.isPlaying=true;
        }
        static void OnState(PlayModeStateChange state)
        {
            if(!SessionState.GetBool(Pending,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode){
                AudioListener.volume=0;proof=new ExhaustAppearanceProof(SessionState.GetString(Output,""));
                routine=proof.Proof();started=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;
            }
            if(state==PlayModeStateChange.EnteredEditMode){
                SessionState.SetBool(Pending,false);EditorApplication.Exit(SessionState.GetInt(Result,1));
            }
        }
        static void Tick()
        {
            try{
                if(EditorApplication.timeSinceStartup-started>120)throw new Exception("Exhaust Play Mode proof timed out");
                if(routine.MoveNext())return;
                Debug.Log("EXHAUST_APPEARANCE_PLAYMODE_OK assertions="+proof.Assertions+" sizes=960,480,1280 states=gas,nitro animated=true");
                Finish(true);
            }catch(Exception error){Debug.LogException(error);Finish(false);}
        }
        static void Finish(bool success){EditorApplication.update-=Tick;SessionState.SetInt(Result,success?0:1);EditorApplication.isPlaying=false;}
    }

    sealed class ExhaustAppearanceProof
    {
        readonly string output;
        StaticVehicleVisual visual;
        Camera camera;
        public int Assertions {get;private set;}
        public ExhaustAppearanceProof(string output){this.output=output;}
        void Check(bool valid,string label){Assertions++;if(!valid)throw new Exception("EXHAUST_APPEARANCE "+label);}
        public IEnumerator Proof()
        {
            var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/StarRacing/Generated/PrototypePipeline.asset");
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.gray;
            visual=StaticVehicleVisual.Create(new GameObject("Exhaust view").transform,new Color(.55f,.65f,.8f),true);
            var sun=new GameObject("Proof sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=2;sun.transform.rotation=Quaternion.Euler(45,-30,0);
            camera=new GameObject("Proof camera").AddComponent<Camera>();camera.transform.position=new Vector3(4,4,-9);camera.transform.LookAt(new Vector3(0,.3f,-1.2f));
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.17f,.21f,.28f);
            foreach(bool nitro in new[]{false,true}){
                visual.Exhaust.ResetEffect();
                for(int i=0;i<240;i++){visual.Exhaust.Step(1,nitro,true,1f/60);yield return null;}
                string mode=nitro?"nitro":"gas";
                foreach(int width in new[]{960,480,1280})Capture(mode+"-"+width,width);
                var first=Capture(mode+"-motion-a",960);
                var size=visual.Exhaust.transform.Find("Exhaust plume 0").localScale;
                for(int i=0;i<10;i++){visual.Exhaust.Step(1,nitro,true,1f/60);yield return null;}
                var second=Capture(mode+"-motion-b",960);int changed=0;
                for(int i=0;i<first.Length;i++)if(Math.Abs(first[i].r-second[i].r)+Math.Abs(first[i].g-second[i].g)+Math.Abs(first[i].b-second[i].b)>3)changed++;
                Check(changed>20,mode+" detailed artwork visibly moves");
                Check(Mathf.Abs(visual.Exhaust.transform.Find("Exhaust plume 0").localScale.x-size.x)<.001f,mode+" flow preserves width");
                Check(Mathf.Abs(visual.Exhaust.Envelope.Length-(nitro?2.5919f:.9881f))<.003f,mode+" flow preserves sustained length");
                visual.Exhaust.Step(0,false,false,0);Check(!visual.Exhaust.Emitting,mode+" suppression clears effect");
            }
            var shader=Shader.Find("StarRacing/CloudlineExhaust");Check(shader!=null&&!ShaderUtil.ShaderHasError(shader),"shipping shader compiles");
        }
        Color32[] Capture(string name,int width)
        {
            camera.fieldOfView=width==1280?35:50;
            var target=new RenderTexture(width,width*9/16,24,RenderTextureFormat.ARGB32);
            var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            try{
                camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();
                File.WriteAllBytes(Path.Combine(output,name+".png"),pixels.EncodeToPNG());return pixels.GetPixels32();
            }finally{
                camera.targetTexture=null;RenderTexture.active=previous;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);
            }
        }
    }
}
