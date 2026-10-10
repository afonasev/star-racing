using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using System.Reflection;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
namespace StarRacingPrototype
{
    public static class CloudlineVehicleChecks
    {
        static int count;
        static void Check(bool ok, string label) { count++;if(!ok)throw new Exception("CLOUDLINE_VEHICLE "+label); }
        static ExhaustEnvelope Held(float throttle, bool nitro, float seconds, float dt=.01f)
        { var e=new ExhaustEnvelope();for(int i=0;i<Mathf.RoundToInt(seconds/dt);i++)e.Step(throttle,nitro,true,dt);return e; }
        public static void Run()
        {
            count=0;StrideVisualChecks.Run();
            var tap=new ExhaustEnvelope();tap.Step(1,true,true,1f/240);
            Check(tap.GasLength>=.35f&&tap.NitroLength>=1.4f,"single high-FPS frame has visible gas and nitro onset");
            tap.Step(0,false,true,1f/60);Check(tap.NitroSignal>.5f&&tap.GasSignal>=.35f,"tap remains visible to audio block");
            tap.Step(0,false,false,.01f);Check(tap.GasSignal==0&&tap.NitroSignal==0,"suppression clears pulse immediately");
            var zero=Held(0,false,2);Check(zero.Length==0,"idle off");
            var quarter=Held(.25f,false,1);var half=Held(.5f,false,1);var full=Held(1,false,1);
            Check(quarter.Length>0&&quarter.Length<half.Length&&half.Length<full.Length,"analog throttle strength");
            Check(Held(1,false,.1f).Length<Held(1,false,.5f).Length&&Held(1,false,.5f).Length<full.Length,"hold duration buildup");
            var capped=Held(1,false,8);Check(capped.Length<=1f&&capped.Length>.97f,"compact gas bounded");
            var nitro=Held(1,true,1);Check(nitro.Length>full.Length&&nitro.NitroBlend>.99f,"active nitro longer and blue");
            Check(Held(1,true,.2f).Length<nitro.Length&&Held(1,true,8).Length<=2.6f&&Held(1,true,8).Length>2.55f,"compact nitro duration and cap");
            float before=nitro.Length;nitro.Step(0,false,true,0);Check(nitro.Length==before,"dt zero freezes");
            for(int i=0;i<150;i++)nitro.Step(0,false,true,.01f);Check(nitro.Length==0&&nitro.NitroHold==0,"release extinguishes");
            Check(Mathf.Abs(Held(.75f,true,1,.02f).Length-Held(.75f,true,1,.01f).Length)<.015f,"frame partition tolerance");
            nitro=Held(1,true,1);nitro.Step(1,true,false,.01f);Check(nitro.Length==0&&nitro.GasHold==0&&nitro.NitroHold==0,"suppression resets all");
            nitro=Held(1,true,1);nitro.Reset();Check(nitro.Length==0,"revision reset");
            var balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);var drive=new DrivingBalance(balance);float charge=drive.Charge;
            var requested=new DrivingInput{throttle=1,nitro=true};var e=new ExhaustEnvelope();
            for(int i=0;i<150;i++){Check(drive.Step(requested,0,.01f,false,false,1,8)==0,"countdown zero acceleration");e.Step(1,drive.Active,true,.01f);}
            Check(e.GasLength>0&&e.NitroLength==0&&drive.Charge==charge,"countdown rev no boost or charge use");
            var root=new GameObject("held C05 check");
            try {
                var body=root.AddComponent<Rigidbody>();body.isKinematic=true;
                var visual=StaticVehicleVisual.Create(root.transform,Color.magenta,true);Vector3 position=body.position;
                for(int i=0;i<100;i++)visual.Exhaust.Step(.8f,false,true,.01f);
                Check(visual.Exhaust.Emitting&&body.position==position&&body.isKinematic,"presentation never unlocks body");
                var plume=visual.Exhaust.transform.Find("Exhaust plume 0");
                Check(plume!=null&&Mathf.Abs(plume.localScale.x-.36f)<.001f,"compact gas silhouette");
                for(int i=0;i<100;i++)visual.Exhaust.Step(1,true,true,.01f);
                Check(Mathf.Abs(plume.localScale.x-.46f)<.001f&&plume.localScale.z>2.2f,"compact nitro silhouette");
                var properties=new MaterialPropertyBlock();
                plume.GetComponent<MeshRenderer>().GetPropertyBlock(properties);
                Check(properties.GetFloat("_Nitro")>.99f,"active nitro selects blue artwork");
                float phase=properties.GetFloat("_FlowPhase");
                visual.Exhaust.Step(1,true,true,0);
                plume.GetComponent<MeshRenderer>().GetPropertyBlock(properties);
                Check(properties.GetFloat("_FlowPhase")==phase,"zero dt freezes drawn flame flow");
                visual.Exhaust.Step(1,true,true,float.NaN);
                plume.GetComponent<MeshRenderer>().GetPropertyBlock(properties);
                Check(properties.GetFloat("_FlowPhase")==phase,"invalid dt preserves finite flame flow");
                var other=new MaterialPropertyBlock();
                visual.Exhaust.transform.Find("Exhaust plume 1").GetComponent<MeshRenderer>().GetPropertyBlock(other);
                Check(other.GetFloat("_JetPhase")!=properties.GetFloat("_JetPhase"),"twin flames have independent detail phases");
                visual.Exhaust.ResetEffect();
                for(int i=0;i<100;i++)visual.Exhaust.Step(1,false,true,.01f);
                plume.GetComponent<MeshRenderer>().GetPropertyBlock(properties);
                Check(properties.GetFloat("_Nitro")==0,"gas selects warm artwork");
                Check(float.IsFinite(properties.GetFloat("_FlowPhase")),"drawn flame phase is finite");
                var flameMaterial=plume.GetComponent<MeshRenderer>().sharedMaterial;
                Check(flameMaterial.GetTexture("_FlameAtlas")==Resources.Load<Texture2D>("Vehicle/CloudlineFlameAtlas"),"material uses selected natural artwork");
                visual.Exhaust.ResetEffect();Check(!visual.Exhaust.Emitting,"hidden immediately on reset");
            }finally{UnityEngine.Object.DestroyImmediate(root);}
            Integration();
            Debug.Log("CLOUDLINE_VEHICLE_CHECKS_OK assertions="+count);
        }
        static void Integration()
        {
            var root=new GameObject("C05 actual presentation fixture");var pad=InputSystem.AddDevice<Gamepad>();
            float scale=Time.timeScale;
            try {
                Time.timeScale=1;
                var trackRoot=new GameObject("fixture track");trackRoot.transform.SetParent(root.transform);
                var track=trackRoot.AddComponent<TrackBuilder>();track.Build(new TrackRoute(Procedural.Generator.Generate(77,"normal","cloud-city",false,true)));Physics.SyncTransforms();
                var carRoot=new GameObject("fixture car");carRoot.transform.SetParent(root.transform);
                var car=carRoot.AddComponent<MagneticVehicle>();var balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);car.Initialize(track,0,Color.cyan,balance);car.ResetAt(80);car.Hold(true);
                // Keep the director fixture inactive so Awake does not create a second
                // full race or pause the controlled physics/presentation fixture.
                var directorRoot=new GameObject("fixture director");directorRoot.SetActive(false);directorRoot.transform.SetParent(root.transform);
                var director=directorRoot.AddComponent<RaceDirector>();directorRoot.AddComponent<RaceMenu>().director=director;
                var router=new LocalInputRouter();Check(router.TryBind(new LocalRaceConfig{humans=1,devices=new[]{0,-1,0,1}},null,new[]{pad},out _),"fixture bind");router.PrepareStart();
                var session=new RaceSession(track.Route.StartDistance,track.Route.FinishDistance,1);session.Begin();
                Set(director,"Track",track);Set(director,"Input",router);Set(director,"Session",session);Set(director,"Cars",new[]{car});Set(director,"HumanCount",1);Set(director,"Started",true);Set(director,"Paused",false);
                var update=typeof(RaceDirector).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic);
                var tick=typeof(MagneticVehicle).GetMethod("UpdateExhaust",BindingFlags.Instance|BindingFlags.NonPublic);
                var physics=typeof(MagneticVehicle).GetMethod("FixedUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
                var exhaust=car.GetComponentInChildren<VehicleExhaust>();var position=car.Body.position;float charge=car.Drive.Charge;
                InputSystem.QueueStateEvent(pad,new GamepadState{rightTrigger=.25f});PumpInput();update.Invoke(director,null);
                for(int i=0;i<30;i++)tick.Invoke(car,new object[]{.01f});float partial=exhaust.Envelope.Length;
                InputSystem.QueueStateEvent(pad,new GamepadState{rightTrigger=1}.WithButton(GamepadButton.East));PumpInput();update.Invoke(director,null);
                for(int i=0;i<100;i++){tick.Invoke(car,new object[]{.01f});physics.Invoke(car,null);}
                Check(partial>0&&exhaust.Envelope.Length>partial,"director countdown analog reaches exhaust");
                Check(car.CurrentInput.throttle==0&&!car.CurrentInput.nitro&&car.Body.isKinematic&&car.Body.position==position&&car.Body.linearVelocity==Vector3.zero,"director countdown withholds propulsion");
                Check(car.Drive.Charge==charge&&!car.Drive.Active&&exhaust.Envelope.NitroLength==0,"director countdown blocks requested nitro");
                director.SetPaused(true);update.Invoke(director,null);tick.Invoke(car,new object[]{.01f});Check(!exhaust.Emitting,"director pause stops flame");
                director.SetPaused(false);update.Invoke(director,null);for(int i=0;i<100;i++)tick.Invoke(car,new object[]{.01f});Check(!exhaust.Emitting,"resume held input blocked");
                InputSystem.QueueStateEvent(pad,new GamepadState());PumpInput();update.Invoke(director,null);
                InputSystem.QueueStateEvent(pad,new GamepadState{rightTrigger=1});PumpInput();update.Invoke(director,null);tick.Invoke(car,new object[]{.01f});Check(exhaust.Emitting,"neutral release rearms countdown gas");
                car.ResetAt(80);car.Hold(true);tick.Invoke(car,new object[]{.01f});Check(!exhaust.Emitting&&exhaust.Envelope.GasHold==0,"reset clears actual presentation");
                update.Invoke(director,null);tick.Invoke(car,new object[]{.01f});Check(car.HasProvenCheckpoint,"fixture has admitted recovery checkpoint");car.ResetToCheckpoint();tick.Invoke(car,new object[]{.01f});Check(exhaust.Envelope.GasHold<=.011f,"recovery revision clears duration");
                if(Application.isPlaying){
                InputSystem.QueueStateEvent(pad,new GamepadState());PumpInput();
                InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(GamepadButton.East).WithButton(GamepadButton.RightShoulder));
                InputSystem.QueueStateEvent(pad,new GamepadState());PumpInput();
                var tap=router.Read(0);Check(tap.nitro&&tap.throttle==1,"press-release in one input update survives gas="+tap.throttle+" nitro="+tap.nitro);
                car.Hold(false);car.SetRaceContext(true,1,8,0);car.Body.linearVelocity=car.Frame.tangent*20;Physics.SyncTransforms();
                car.SetInput(tap);car.SetInput(default);float tapCharge=car.Drive.Charge;
                physics.Invoke(car,null);Check(car.Drive.Active&&car.Drive.Charge<tapCharge,"released tap reaches propulsion once");
                // A second substep may turn boost off before the renderer samples it.
                physics.Invoke(car,null);Check(!car.Drive.Active,"tap does not stick in following physics step");
                car.SetPresentationInput(tap.throttle,true);tick.Invoke(car,new object[]{1f/240});
                Check(car.DriveFeedback==exhaust.Envelope&&exhaust.Envelope.NitroSignal>.5f,"accepted substep reaches shared audio/visual envelope");
                car.SetInput(tap);car.SetInput(default);car.SetPresentationInput(0,false);physics.Invoke(car,null);tick.Invoke(car,new object[]{.01f});
                Check(!car.Drive.Active&&car.DriveFeedback.Length==0,"suppression drops pending tap and effect");
                }
            } finally { UnityEngine.Object.DestroyImmediate(root);InputSystem.RemoveDevice(pad);Time.timeScale=scale; }
        }
        // EditMode defaults to EditorUpdate, which swaps on every event and has no
        // player frame boundary. Exercise the shipping Dynamic update for tap edges.
        static void PumpInput() {
            if(Application.isPlaying)typeof(InputSystem).GetMethod("Update",BindingFlags.Static|BindingFlags.NonPublic,null,new[]{typeof(InputUpdateType)},null).Invoke(null,new object[]{InputUpdateType.Dynamic});
            else InputSystem.Update();
        }
        static void Set(object target,string property,object value) => target.GetType().GetProperty(property).SetValue(target,value);
        // Offscreen Editor evidence using the shipping mesh/material/shader; no Player build.
        public static void RenderReadability()
        {
            string output=Environment.GetEnvironmentVariable("STAR_RACING_EXHAUST_PROOF");
            if(string.IsNullOrEmpty(output))throw new Exception("Set STAR_RACING_EXHAUST_PROOF");
            Directory.CreateDirectory(output);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var pipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/StarRacing/Generated/PrototypePipeline.asset");
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=Color.gray;
            var root=new GameObject("Exhaust readability fixture");
            var visual=StaticVehicleVisual.Create(root.transform,new Color(.55f,.65f,.8f),true);
            var sun=new GameObject("Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=2;sun.transform.rotation=Quaternion.Euler(45,-30,0);
            var camera=new GameObject("Proof camera").AddComponent<Camera>();
            camera.transform.position=new Vector3(4,4,-9);camera.transform.LookAt(new Vector3(0,.3f,-1.2f));
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.17f,.21f,.28f);camera.fieldOfView=50;
            foreach(int width in new[]{960,480,1280})foreach(bool nitro in new[]{false,true})
            {
                camera.fieldOfView=width==1280?35:50;
                visual.Exhaust.ResetEffect();for(int i=0;i<400;i++)visual.Exhaust.Step(1,nitro,true,.01f);
                var target=new RenderTexture(width,width*9/16,24,RenderTextureFormat.ARGB32);
                var pixels=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                var previous=RenderTexture.active;
                try {
                    camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                    pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();
                    File.WriteAllBytes(Path.Combine(output,(nitro?"nitro-":"gas-")+width+".png"),pixels.EncodeToPNG());
                }finally{
                    camera.targetTexture=null;RenderTexture.active=previous;target.Release();
                    UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);
                }
            }
            var shader=Shader.Find("StarRacing/CloudlineExhaust");
            if(shader==null||ShaderUtil.ShaderHasError(shader))throw new Exception("Exhaust shader compilation failed");
            Debug.Log("EXHAUST_READABILITY_RENDER_OK full=960x540 split=480x270 detail=1280x720 states=gas,nitro");
        }
    }
}
