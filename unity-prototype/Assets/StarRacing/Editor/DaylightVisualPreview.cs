using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StarRacingPrototype {
    [InitializeOnLoad] public static class DaylightVisualPreview {
        const string Key="StarRacing.DaylightPreview";
        static int ticks;
        static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_DAYLIGHT_PREVIEW_DIR");
        static DaylightVisualPreview(){EditorApplication.playModeStateChanged+=Changed;}
        public static void Baseline(){Begin(false,false);}
        public static void Verify(){Begin(true,true);}
        public static void VerifyPresentation(){Begin(true,false);}
        public static void VerifySurfaces(){EnvironmentTextureChecks.Run();Begin(true,false);}
        public static void CompareVehicleContactShadows(){BeginContactComparison();}
        public static void CaptureVehicleContactShadowDiagnostics(){BeginContactDiagnostics();}
        public static void CompareAtmosphericDepth(){SkyboxChecks.Run();SessionState.SetBool(Key+"AtmosphereComparison",true);Begin(true,false);}
        public static void CompareEmissiveAccents(){CloudlineVehicleChecks.Run();SessionState.SetBool(Key+"EmissiveComparison",true);Begin(true,false);}
        public static void CompareVehicleGlassRubber(){CloudlineVehicleChecks.Run();DaylightVisualChecks.Run();SessionState.SetBool(Key+"GlassRubberComparison",true);Begin(false,false);}
        static void Begin(bool verify,bool existingChecks) {
            if(string.IsNullOrEmpty(Output))throw new Exception("STAR_RACING_DAYLIGHT_PREVIEW_DIR required");
            Directory.CreateDirectory(Output);
            if(verify) {
                if(existingChecks){SkyboxChecks.Run();EnvironmentTextureChecks.Run();CloudlineVehicleChecks.Run();}
                DaylightVisualChecks.Run();
            }
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/StarRacing/Generated/PrototypePipeline.asset");
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);SessionState.SetBool(Key+"Verify",verify);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            ticks=0;EditorApplication.update+=Enter;
        }
        static void BeginContactComparison() {
            if(string.IsNullOrEmpty(Output))throw new Exception("STAR_RACING_DAYLIGHT_PREVIEW_DIR required");
            Directory.CreateDirectory(Output);
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/StarRacing/Generated/PrototypeRenderer.asset");
            var ssao=VehicleContactShadowsRendererFeature.EnsureConfigured(renderer);
            VehicleContactShadowsChecks.Run();
            DaylightVisualChecks.Run();
            GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/StarRacing/Generated/PrototypePipeline.asset");
            QualitySettings.renderPipeline=GraphicsSettings.defaultRenderPipeline;
            ssao.SetActive(false);
            SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);SessionState.SetBool(Key+"Verify",true);SessionState.SetBool(Key+"ContactComparison",true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            ticks=0;EditorApplication.update+=Enter;
        }
        static void BeginContactDiagnostics() {
            if(string.IsNullOrEmpty(Output))throw new Exception("STAR_RACING_DAYLIGHT_PREVIEW_DIR required");
            Directory.CreateDirectory(Output);
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/StarRacing/Generated/PrototypeRenderer.asset");
            var ssao=VehicleContactShadowsRendererFeature.EnsureConfigured(renderer);
            ssao.SetActive(true);
            VehicleContactShadowsChecks.Run();
            DaylightVisualChecks.Run();
            GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/StarRacing/Generated/PrototypePipeline.asset");
            QualitySettings.renderPipeline=GraphicsSettings.defaultRenderPipeline;
            SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);SessionState.SetBool(Key+"Verify",true);SessionState.SetBool(Key+"ContactDiagnostics",true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            ticks=0;EditorApplication.update+=Enter;
        }
        static void Enter(){if(++ticks<15)return;EditorApplication.update-=Enter;EditorApplication.isPlaying=true;}
        static void Changed(PlayModeStateChange state) {
            if(!SessionState.GetBool(Key,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode){ticks=0;EditorApplication.update+=Draw;}
            if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);}
        }
        static void Draw() {
            if(++ticks<15)return;EditorApplication.update-=Draw;
            try {
                AudioListener.volume=0;
                if(SessionState.GetBool(Key+"EmissiveComparison",false)) {
                    RenderEmissiveComparison();SessionState.SetBool(Key+"EmissiveComparison",false);
                    File.WriteAllText(Path.Combine(Output,"result.txt"),"EMISSIVE_ACCENTS_PREVIEW_OK editorPlayMode=true themes=city-space-city layouts=1,4 entrants=64 commonTarget=true states=idle,gas,nitro details=front,rear,motion playerBuild=false\n");
                    Debug.Log("EMISSIVE_ACCENTS_PREVIEW_OK editorPlayMode=true layouts=1,4 entrants=64 commonTarget=true states=idle,gas,nitro");
                } else if(SessionState.GetBool(Key+"GlassRubberComparison",false)) {
                    RenderGlassRubberComparison();SessionState.SetBool(Key+"GlassRubberComparison",false);
                    File.WriteAllText(Path.Combine(Output,"result.txt"),"CLOUDLINE_GLASS_RUBBER_PREVIEW_OK editorPlayMode=true themes=city,space layouts=1,4 entrants=64 variants=baseline,candidate-a,candidate-b,candidate-c,selected closeups=glass,tire playerBuild=false\n");
                    Debug.Log("CLOUDLINE_GLASS_RUBBER_PREVIEW_OK editorPlayMode=true themes=city,space layouts=1,4 entrants=64 variants=baseline,candidate-a,candidate-b,candidate-c,selected");
                } else if(SessionState.GetBool(Key+"AtmosphereComparison",false)) {
                    foreach(string phase in new[]{"depth-before","depth-after","depth-strong"})
                        foreach(string theme in new[]{"cloud-city","space-station","cloud-city"})RenderTheme(theme,phase);
                    SessionState.SetBool(Key+"AtmosphereComparison",false);
                    File.WriteAllText(Path.Combine(Output,"result.txt"),"ATMOSPHERIC_DEPTH_PREVIEW_OK editorPlayMode=true themes=city-space-city layouts=1,4 entrants=64 commonTarget=true variants=before,after,strong playerBuild=false\n");
                    Debug.Log("ATMOSPHERIC_DEPTH_PREVIEW_OK editorPlayMode=true layouts=1,4 entrants=64 commonTarget=true variants=before,after,strong");
                } else if(SessionState.GetBool(Key+"ContactDiagnostics",false)) {
                    RenderTheme("cloud-city","diagnostic");
                    SessionState.SetBool(Key+"ContactDiagnostics",false);
                    File.WriteAllText(Path.Combine(Output,"diagnostic-result.txt"),"VEHICLE_CONTACT_SSAO_DIAGNOSTICS_OK actualPlayMode=true commonTarget=true 64cars=true cameraMotion=true inclinedRoad=true\n");
                    Debug.Log("VEHICLE_CONTACT_SSAO_DIAGNOSTICS_OK cameraMotion=true inclinedRoad=true");
                } else if(SessionState.GetBool(Key+"ContactComparison",false)) {
                    foreach(string theme in new[]{"cloud-city","space-station","cloud-city"})RenderTheme(theme,"before");
                    var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/StarRacing/Generated/PrototypeRenderer.asset");
                    var ssao=renderer.rendererFeatures.Find(feature=>feature is ScreenSpaceAmbientOcclusion) as ScreenSpaceAmbientOcclusion;
                    if(ssao==null)throw new Exception("Configured SSAO feature disappeared before after capture.");
                    ssao.SetActive(true);
                    foreach(string theme in new[]{"cloud-city","space-station","cloud-city"})RenderTheme(theme,"after");
                    SessionState.SetBool(Key+"ContactComparison",false);
                    File.WriteAllText(Path.Combine(Output,"result.txt"),"VEHICLE_CONTACT_SSAO_PREVIEW_OK editorPlayMode=true themes=city-space-city layouts=1,4 entrants=64 commonTarget=true beforeAfter=true playerBuild=false\n");
                    Debug.Log("VEHICLE_CONTACT_SSAO_PREVIEW_OK editorPlayMode=true layouts=1,4 entrants=64 commonTarget=true beforeAfter=true");
                } else {
                    foreach(string theme in new[]{"cloud-city","space-station","cloud-city"})RenderTheme(theme);
                    if(!SessionState.GetBool(Key+"Verify",false))DaylightReflectionBaker.Run();
                    File.WriteAllText(Path.Combine(Output,"result.txt"),"DAYLIGHT_PREVIEW_OK editorPlayMode=true themes=city-space-city layouts=1,4 entrants=64 playerBuild=false\n");
                    Debug.Log("DAYLIGHT_PREVIEW_OK editorPlayMode=true layouts=1,4 entrants=64");
                }
            } catch(Exception e){SessionState.SetBool(Key+"Failed",true);Debug.LogException(e);}
            finally{EditorApplication.isPlaying=false;}
        }
        static void RenderEmissiveComparison() {
            var head=Resources.Load<Material>("Vehicle/CloudlineMaterials/Cyan_Energy");
            var tail=Resources.Load<Material>("Vehicle/CloudlineMaterials/Thin_Red_Taillight");
            var exhaust=Resources.Load<Material>("Vehicle/CloudlineExhaust");
            Color headNew=head.GetColor("_EmissionColor"),tailNew=tail.GetColor("_EmissionColor");float exhaustNew=exhaust.GetFloat("_Intensity");
            var headFlags=head.globalIlluminationFlags;var tailFlags=tail.globalIlluminationFlags;
            try {
                foreach(string phase in new[]{"accent-before","accent-after"}) {
                    bool before=phase=="accent-before";
                    if(before){head.DisableKeyword("_EMISSION");tail.DisableKeyword("_EMISSION");}
                    else {head.EnableKeyword("_EMISSION");tail.EnableKeyword("_EMISSION");}
                    head.globalIlluminationFlags=before?MaterialGlobalIlluminationFlags.EmissiveIsBlack:headFlags;
                    tail.globalIlluminationFlags=before?MaterialGlobalIlluminationFlags.EmissiveIsBlack:tailFlags;
                    head.SetColor("_EmissionColor",before?new Color(.003f,.6f,3):headNew);
                    tail.SetColor("_EmissionColor",before?new Color(1.6f,.006f,.016f):tailNew);
                    exhaust.SetFloat("_Intensity",before?1.7f:exhaustNew);
                    foreach(string theme in new[]{"cloud-city","space-station","cloud-city"})RenderTheme(theme,phase);
                }
            } finally {
                head.SetColor("_EmissionColor",headNew);tail.SetColor("_EmissionColor",tailNew);exhaust.SetFloat("_Intensity",exhaustNew);
                head.globalIlluminationFlags=headFlags;tail.globalIlluminationFlags=tailFlags;head.EnableKeyword("_EMISSION");tail.EnableKeyword("_EMISSION");
            }
        }
        static void RenderGlassRubberComparison() {
            var glass=Resources.Load<Material>("Vehicle/CloudlineMaterials/Midnight_Glass");
            var tire=Resources.Load<Material>("Vehicle/CloudlineMaterials/Tire_Rubber");
            if(glass==null||tire==null)throw new Exception("Cloudline glass/rubber materials missing");
            foreach(string theme in new[]{"cloud-city","space-station"})RenderTheme(theme,"glass-rubber-comparison");
        }
        static void RenderTheme(string theme,string phase=null) {
            var root=new GameObject("Daylight fixture");var environment=new TrackEnvironmentBuilder();
            try {
                var trackGo=new GameObject("Track");trackGo.transform.SetParent(root.transform,false);
                bool includeRamps=phase=="diagnostic";
                var track=trackGo.AddComponent<TrackBuilder>();track.Build(new TrackRoute(Procedural.Generator.Generate(77,"normal",theme,includeRamps,true)));
                environment.Build(track.Route,track.transform,77);environment.Sky.Activate();
                var sunGo=new GameObject("Sun");sunGo.transform.SetParent(root.transform,false);var sun=sunGo.AddComponent<Light>();sun.type=LightType.Directional;
                var fillGo=new GameObject("Fill");fillGo.transform.SetParent(root.transform,false);var fill=fillGo.AddComponent<Light>();fill.type=LightType.Directional;
                var volumeGo=new GameObject("Color and bloom");volumeGo.transform.SetParent(root.transform,false);
                var volume=volumeGo.AddComponent<Volume>();volume.isGlobal=true;volume.profile=ScriptableObject.CreateInstance<VolumeProfile>();
                var bloom=volume.profile.Add<Bloom>();bloom.intensity.Override(.25f);bloom.threshold.Override(1.1f);volume.profile.Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
                var cameras=new ChaseCamera[4];var cars=new MagneticVehicle[64];var balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);
                for(int i=0;i<64;i++) {
                    var carGo=new GameObject("Car "+i);carGo.transform.SetParent(root.transform,false);
                    var car=carGo.AddComponent<MagneticVehicle>();car.Initialize(track,i,i<4?CloudlineSkin.PlayerColors[i]:Color.HSVToRGB((i*.618034f)%1,.55f,.9f),balance,i<4);
                    cars[i]=car;
                    car.ResetAt(160+(i/4)*9,(i%4-1.5f)*2.9f);car.Hold(true);
                    if(i>=4)continue;
                    var viewGo=new GameObject("Camera "+i);viewGo.transform.SetParent(root.transform,false);var camera=viewGo.AddComponent<Camera>();camera.enabled=false;
                    camera.fieldOfView=65;camera.nearClipPlane=.2f;camera.farClipPlane=1800;camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
                    cameras[i]=viewGo.AddComponent<ChaseCamera>();cameras[i].target=car;
                }
                RaceLightingController.Apply(environment.Plan.lighting,sun,fill,cameras);
                if(theme=="cloud-city") {
                    // Reproduce the previous fog only; every other setting is the delivered profile.
                    if(phase=="depth-before"){RenderSettings.fogDensity=.00105f;RenderSettings.fogColor=environment.Plan.lighting.skyColor;}
                    if(phase=="depth-strong")RenderSettings.fogDensity=.00205f;
                }
                if(phase!=null&&phase.StartsWith("depth-"))Debug.Log($"ATMOSPHERIC_DEPTH_PROFILE phase={phase} theme={theme} fogDensity={RenderSettings.fogDensity:F5} fogColor={RenderSettings.fogColor}");
                if(phase!=null&&phase.StartsWith("accent-"))CaptureEmissiveStates(cars,cameras,theme,phase);
                else foreach(int seats in new[]{1,4})Capture(cameras,seats,theme,phase);
                if(phase=="glass-rubber-comparison")CaptureGlassRubberCandidates(cars,cameras,theme);
                int objects=root.GetComponentsInChildren<Transform>().Length;
                int meshes=root.GetComponentsInChildren<MeshFilter>().Length;
                long triangles=root.GetComponentsInChildren<MeshFilter>().Sum(f=>(long)f.sharedMesh.triangles.Length/3);
                int colliders=root.GetComponentsInChildren<Collider>().Length;
                string inventory=$"objects={objects} meshes={meshes} triangles={triangles} colliders={colliders}\n";
                File.WriteAllText(Path.Combine(Output,(string.IsNullOrEmpty(phase)?"":phase+"-")+theme+"-inventory.txt"),inventory);Debug.Log("DAYLIGHT_INVENTORY phase="+(phase??"daylight")+" "+theme+" "+inventory.Trim());
                if(phase=="diagnostic") {
                    var jump=track.Route.Definition.jumps.FirstOrDefault();
                    if(jump==null)throw new Exception("Diagnostic route did not contain an authored ramp.");
                    float rampStart=30+jump.rampStartIndex*5,launch=30+jump.launchIndex*5;
                    float distance=Mathf.Lerp(rampStart,launch,.5f),epsilon=.1f;
                    float slope=(RampHeight(jump,distance+epsilon)-RampHeight(jump,distance-epsilon))/(2*epsilon);
                    float angle=Mathf.Atan(Mathf.Abs(slope))*Mathf.Rad2Deg;
                    if(angle<2f)throw new Exception("Diagnostic ramp did not contain an inclined road surface.");
                    var frame=track.Route.Evaluate(distance);
                    float lateral=(float)jump.lateralCenter;
                    cars[0].ResetAt(distance,lateral);
                    var body=cars[0].GetComponent<Rigidbody>();
                    float bodyHeight=Vector3.Dot(body.position-frame.position,frame.normal)+RampHeight(jump,distance);
                    Vector3 position=frame.position+frame.right*lateral+frame.normal*bodyHeight;
                    Vector3 surfaceTangent=(frame.tangent+frame.normal*slope).normalized;
                    Vector3 surfaceNormal=(frame.normal-frame.tangent*slope).normalized;
                    var rotation=Quaternion.LookRotation(surfaceTangent,surfaceNormal);
                    body.position=position;body.rotation=rotation;cars[0].transform.SetPositionAndRotation(position,rotation);
                    cameras[0].Snap();
                    Capture(cameras,1,theme,"inclined-road");
                    Debug.Log($"VEHICLE_CONTACT_INCLINE distance={distance:F2} angleDegrees={angle:F2} ramp={jump.id} car=0");
                    for(int i=0;i<4;i++) {
                        var camera=cameras[i].GetComponent<Camera>();
                        camera.transform.position+=camera.transform.right*.35f;
                        camera.transform.rotation=Quaternion.AngleAxis(1.5f,Vector3.up)*camera.transform.rotation;
                    }
                    Capture(cameras,4,theme,"camera-motion");
                    Debug.Log("VEHICLE_CONTACT_CAMERA_MOTION offsetMeters=0.35 yawDegrees=1.5 views=4");
                }
                UnityEngine.Object.DestroyImmediate(volume.profile);
            } finally {environment.Clear();UnityEngine.Object.DestroyImmediate(root);}
        }
        static void CaptureGlassRubberCandidates(MagneticVehicle[] cars,ChaseCamera[] cameras,string theme) {
            var glass=Resources.Load<Material>("Vehicle/CloudlineMaterials/Midnight_Glass");
            var tire=Resources.Load<Material>("Vehicle/CloudlineMaterials/Tire_Rubber");
            float glassMetal=glass.GetFloat("_Metallic"),glassSmooth=glass.GetFloat("_Smoothness");
            float tireMetal=tire.GetFloat("_Metallic"),tireSmooth=tire.GetFloat("_Smoothness");
            try {
                foreach(string variant in new[]{"baseline","candidate-a","candidate-b","candidate-c","selected"}) {
                    if(variant=="baseline"){glass.SetFloat("_Metallic",.28f);glass.SetFloat("_Smoothness",.82f);tire.SetFloat("_Metallic",0);tire.SetFloat("_Smoothness",.22f);}
                    else if(variant=="candidate-a"){glass.SetFloat("_Metallic",.08f);glass.SetFloat("_Smoothness",.91f);tire.SetFloat("_Metallic",0);tire.SetFloat("_Smoothness",.10f);}
                    else if(variant=="candidate-b"){glass.SetFloat("_Metallic",.12f);glass.SetFloat("_Smoothness",.76f);tire.SetFloat("_Metallic",0);tire.SetFloat("_Smoothness",.15f);}
                    else if(variant=="candidate-c"){glass.SetFloat("_Metallic",.18f);glass.SetFloat("_Smoothness",.86f);tire.SetFloat("_Metallic",0);tire.SetFloat("_Smoothness",.12f);}
                    else {glass.SetFloat("_Metallic",glassMetal);glass.SetFloat("_Smoothness",glassSmooth);tire.SetFloat("_Metallic",tireMetal);tire.SetFloat("_Smoothness",tireSmooth);}
                    string capturePhase="gr-material-"+variant;
                    foreach(int seats in new[]{1,4})Capture(cameras,seats,theme,capturePhase);
                    CaptureGlassRubberDetails(cars[0],cameras[0],theme,capturePhase);
                    Debug.Log($"CLOUDLINE_MATERIAL_CANDIDATE theme={theme} variant={variant} glassMetal={glass.GetFloat("_Metallic"):F2} glassSmoothness={glass.GetFloat("_Smoothness"):F2} tireSmoothness={tire.GetFloat("_Smoothness"):F2}");
                }
            } finally {
                glass.SetFloat("_Metallic",glassMetal);glass.SetFloat("_Smoothness",glassSmooth);
                tire.SetFloat("_Metallic",tireMetal);tire.SetFloat("_Smoothness",tireSmooth);
            }
        }
        static void CaptureEmissiveStates(MagneticVehicle[] cars,ChaseCamera[] cameras,string theme,string phase) {
            foreach(string state in new[]{"idle","gas","nitro"}) {
                var effects=cars.Select(car=>car.GetComponentInChildren<StaticVehicleVisual>().Exhaust).ToArray();
                foreach(var effect in effects) {
                    effect.ResetEffect();
                    if(state!="idle")for(int step=0;step<240;step++)effect.Step(1,state=="nitro",true,1f/60);
                }
                foreach(int seats in new[]{1,4})Capture(cameras,seats,theme,phase+"-"+state);
                if(state=="idle")CaptureAccentDetail(cars[0],cameras[0],theme,phase+"-front",true);
                else {
                    CaptureAccentDetail(cars[0],cameras[0],theme,phase+"-rear-"+state,false);
                    float length=effects[0].Envelope.Length;var size=effects[0].transform.Find("Exhaust plume 0").localScale;
                    for(int step=0;step<10;step++)foreach(var effect in effects)effect.Step(1,state=="nitro",true,1f/60);
                    if(Mathf.Abs(effects[0].Envelope.Length-length)>.001f||Mathf.Abs(effects[0].transform.Find("Exhaust plume 0").localScale.x-size.x)>.001f)
                        throw new Exception("Accent motion capture changed sustained exhaust dimensions.");
                    CaptureAccentDetail(cars[0],cameras[0],theme,phase+"-rear-"+state+"-motion",false);
                }
            }
        }
        static void CaptureAccentDetail(MagneticVehicle car,ChaseCamera follow,string theme,string phase,bool front) {
            var camera=follow.GetComponent<Camera>();var position=camera.transform.position;var rotation=camera.transform.rotation;float fov=camera.fieldOfView;
            try {
                camera.fieldOfView=40;
                camera.transform.position=car.transform.position+car.transform.forward*(front?7:-7)+car.transform.right*(front?1.5f:2.5f)+car.transform.up*2;
                camera.transform.LookAt(car.transform.position+car.transform.up*.2f);
                Capture(new[]{follow},1,theme,phase);
            } finally {camera.transform.SetPositionAndRotation(position,rotation);camera.fieldOfView=fov;}
        }
        static void CaptureGlassRubberDetails(MagneticVehicle car,ChaseCamera follow,string theme,string phase) {
            var camera=follow.GetComponent<Camera>();var position=camera.transform.position;var rotation=camera.transform.rotation;float fov=camera.fieldOfView;
            try {
                camera.fieldOfView=34;
                camera.transform.position=car.transform.position+car.transform.forward*1.2f+car.transform.up*2.4f;
                camera.transform.LookAt(car.transform.position+car.transform.up*1.25f);
                Capture(new[]{follow},1,theme,phase+"-detail-glass");
                camera.transform.position=car.transform.position+car.transform.right*2.5f+car.transform.forward*.4f+car.transform.up*1.2f;
                camera.transform.LookAt(car.transform.position+car.transform.up*.45f+car.transform.forward*.4f);
                Capture(new[]{follow},1,theme,phase+"-detail-tire");
            } finally {camera.transform.SetPositionAndRotation(position,rotation);camera.fieldOfView=fov;}
        }
        static float RampHeight(Procedural.Jump jump,float distance) {
            float start=30+jump.rampStartIndex*5,launch=30+jump.launchIndex*5,end=30+jump.gapEndIndex*5;
            if(distance<=launch) {
                float t=Mathf.Clamp01((distance-start)/(launch-start));
                return 2.8f*(3*t*t-2*t*t*t)+.12f*(launch-start)*(t*t*t-t*t);
            }
            float u=Mathf.Clamp01((distance-launch)/(end-launch));
            return (2*u*u*u-3*u*u+1)*2.8f+(u*u*u-2*u*u+u)*.12f*(end-launch);
        }
        static void Capture(ChaseCamera[] follows,int seats,string theme,string phase=null) {
            if(string.IsNullOrEmpty(phase)){CaptureIndependentViews(follows,seats,theme);return;}
            const int totalWidth=1600,totalHeight=900;
            var image=new Texture2D(totalWidth,totalHeight,TextureFormat.RGB24,false);
            var tick=typeof(ChaseCamera).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
            string prefix=string.IsNullOrEmpty(phase)?"":phase+"-";
            var sharedTarget=new RenderTexture(totalWidth,totalHeight,24,RenderTextureFormat.ARGB32);sharedTarget.Create();
            var oldRects=new Rect[seats];var oldAspects=new float[seats];
            var previous=RenderTexture.active;
            try {
                for(int seat=0;seat<seats;seat++) {
                    var camera=follows[seat].GetComponent<Camera>();var rect=RaceViewports.For(seats,seat);oldRects[seat]=camera.rect;oldAspects[seat]=camera.aspect;
                    int width=(int)(totalWidth*rect.width),height=(int)(totalHeight*rect.height);
                    camera.rect=rect;camera.aspect=(float)width/height;
                    bool detail=phase!=null&&(phase.EndsWith("-front")||phase.Contains("-rear-")||phase.Contains("-detail-"));
                    if(phase!="camera-motion"&&!detail){follows[seat].Snap();tick.Invoke(follows[seat],null);}
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=sharedTarget});
                }
                RenderTexture.active=sharedTarget;image.ReadPixels(new Rect(0,0,totalWidth,totalHeight),0,0);image.Apply();
                File.WriteAllBytes(Path.Combine(Output,$"{prefix}{theme}-{seats}-players.png"),image.EncodeToPNG());
                Debug.Log($"VEHICLE_CONTACT_CAPTURE phase={phase??"daylight"} theme={theme} seats={seats} resolution={totalWidth}x{totalHeight} commonTarget=true");
            } finally {
                RenderTexture.active=previous;
                for(int seat=0;seat<seats;seat++){var camera=follows[seat].GetComponent<Camera>();camera.rect=oldRects[seat];camera.aspect=oldAspects[seat];}
                sharedTarget.Release();UnityEngine.Object.DestroyImmediate(sharedTarget);UnityEngine.Object.DestroyImmediate(image);
            }
        }
        static void CaptureIndependentViews(ChaseCamera[] follows,int seats,string theme) {
            const int totalWidth=1600,totalHeight=900;
            var image=new Texture2D(totalWidth,totalHeight,TextureFormat.RGB24,false);
            var tick=typeof(ChaseCamera).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
            try {
                for(int seat=0;seat<seats;seat++) {
                    var camera=follows[seat].GetComponent<Camera>();var rect=RaceViewports.For(seats,seat);camera.rect=rect;
                    int width=(int)(totalWidth*rect.width),height=(int)(totalHeight*rect.height);
                    camera.aspect=(float)width/height;follows[seat].Snap();tick.Invoke(follows[seat],null);camera.rect=new Rect(0,0,1,1);
                    var rt=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);rt.Create();var previous=RenderTexture.active;
                    var part=new Texture2D(width,height,TextureFormat.RGB24,false);
                    try {
                        RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                        RenderTexture.active=rt;part.ReadPixels(new Rect(0,0,width,height),0,0);part.Apply();
                        image.SetPixels((int)(totalWidth*rect.x),(int)(totalHeight*rect.y),width,height,part.GetPixels());
                    } finally {RenderTexture.active=previous;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(part);}
                }
                image.Apply();File.WriteAllBytes(Path.Combine(Output,$"{theme}-{seats}-players.png"),image.EncodeToPNG());
            } finally {UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
