using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StarRacingPrototype
{
    [InitializeOnLoad]
    public static class RoadVolumePlayModeChecks
    {
        const string Key="StarRacing.RoadVolumePlayMode";
        static int ticks;static double started;static GameObject root;static float volume;
        static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_ROAD_QA_DIR");
        static RoadVolumePlayModeChecks() { EditorApplication.playModeStateChanged+=Changed; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void MuteQa() { if(SessionState.GetBool(Key,false))AudioListener.volume=0; }
        public static void Run()
        {
            if(string.IsNullOrEmpty(Output))throw new Exception("STAR_RACING_ROAD_QA_DIR required");
            Directory.CreateDirectory(Output);SessionState.SetFloat(Key+"Volume",AudioListener.volume);AudioListener.volume=0;SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            // Let startup delay callbacks create the default Search database before
            // entering Play Mode; Unity skips that initialization while changing mode.
            ticks=0;EditorApplication.update+=PreparePlay;
        }
        static void PreparePlay()
        {
            if(++ticks<15)return;EditorApplication.update-=PreparePlay;EditorApplication.isPlaying=true;
        }
        static void Changed(PlayModeStateChange state)
        {
            if(!SessionState.GetBool(Key,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode) { ticks=0;started=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick; }
            if(state==PlayModeStateChange.EnteredEditMode) { SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0); }
        }
        static void Tick()
        {
            try {
                if(EditorApplication.timeSinceStartup-started>120)throw new Exception("Road volume Play Mode timed out");
                if(++ticks!=15)return;
                volume=SessionState.GetFloat(Key+"Volume",1);AudioListener.volume=0;
                foreach(var go in SceneManager.GetActiveScene().GetRootGameObjects())UnityEngine.Object.DestroyImmediate(go);
                if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_CELL_DIAGNOSTIC")=="1" || Environment.GetEnvironmentVariable("STAR_RACING_ROAD_CELL_BOUND_DIAGNOSTIC")=="1") {RoadCellCertificationChecks.Run();Finish(false);return;}
                if(Environment.GetEnvironmentVariable("STAR_RACING_SHIPPING_CONTACT_TRACE")=="1") {RoadSmoothnessChecks.DiagnoseShippingLoop();Finish(false);return;}
                if(Environment.GetEnvironmentVariable("STAR_RACING_FORCE_CALIBRATION")=="1") {PhysicsSubstepCalibrationChecks.Run();Finish(false);return;}
                if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_PRODUCTION")=="1") {Debug.Log("ROAD_QA_ROUTE production-exterior-supports");RoadVolumeChecks.Run();Finish(false);return;}
                if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_PRODUCTION_GENERATED")=="1") {Debug.Log("ROAD_QA_ROUTE production-generated-supports");RoadVolumeChecks.RunGenerated();Finish(false);return;}
                if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_PROFILE64")=="1") {Debug.Log("ROAD_QA_ROUTE production-64-car-cost");RoadVolumeChecks.ProfileProduction64();Finish(false);return;}
                if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_PROFILE_CANDIDATE64")=="1") {Debug.Log("ROAD_QA_ROUTE candidate-64-car-cost");RoadVolumeChecks.ProfileCandidate64();Finish(false);return;}
                if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_FACET_DIAGNOSTIC")=="1") {Debug.Log("ROAD_QA_ROUTE exterior-wall-control");RoadVolumeChecks.DiagnoseFacetWalls();Finish(false);return;}
                if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_BOX_DIAGNOSTIC")=="1") {Debug.Log("ROAD_QA_ROUTE box-comparison");RoadVolumeChecks.DiagnoseBox();Finish(false);return;}
                if(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_CCD_SLICES")=="1" || Environment.GetEnvironmentVariable("STAR_RACING_ROAD_SUBSTEPS")!=null || Environment.GetEnvironmentVariable("STAR_RACING_ROAD_SOLID_BOX")=="1") {Debug.Log("ROAD_QA_ROUTE native-integration-control");RoadVolumeChecks.DiagnoseCcdSlices();Finish(false);return;}
                PrototypeChecks.RunWithFixtureEquivalence();
                root=new GameObject("road volume preview");
                var definition=RoadVolumeChecks.FlatRoad();
                definition.branches=new[]{new Procedural.Branch {id="preview-fork",startIndex=4,endIndex=56,
                    laneOffsets=new[]{-10.0,10.0},laneHalfWidth=4,safeLane=0,fastLane=1}};
                var track=root.AddComponent<TrackBuilder>();track.Build(new TrackRoute(definition));
                RoadVolumeChecks.PlayModeFixture(track);
                var lightObject=new GameObject("road light");lightObject.transform.SetParent(root.transform);
                var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;
                lightObject.transform.rotation=Quaternion.Euler(45,-30,0);RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.5f,.55f,.65f);
                var fillObject=new GameObject("underside light");fillObject.transform.SetParent(root.transform);
                var fill=fillObject.AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=1.2f;
                fillObject.transform.rotation=Quaternion.Euler(-35,150,0);
                var cameraObject=new GameObject("road volume camera");cameraObject.transform.SetParent(root.transform);
                var camera=cameraObject.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=new Color(.065f,.08f,.11f);camera.fieldOfView=48;camera.nearClipPlane=.1f;
                var frame=track.Route.Evaluate(180);
                var rt=new RenderTexture(1600,1000,24,RenderTextureFormat.ARGB32);rt.Create();
                try {
                    Capture(camera,rt,frame.position+frame.right*24+frame.normal*2-frame.tangent*22,
                        frame.position-frame.normal*.5f,"side-and-fork.png");
                    Capture(camera,rt,frame.position+frame.right*5-frame.normal*7-frame.tangent*18,
                        frame.position-frame.normal*.5f,"underside.png");
                } finally {rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
                File.WriteAllText(Path.Combine(Output,"playmode.txt"),"ROAD_VOLUME_PLAYMODE_OK seed=authored-flat-fork thickness=1m\n");
                Finish(false);
            } catch(Exception e) { Debug.LogException(e);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());Finish(true); }
        }
        static void Capture(Camera camera,RenderTexture rt,Vector3 from,Vector3 to,string filename)
        {
            camera.transform.position=from;camera.transform.LookAt(to);
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
            var previous=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
            try {image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(Output,filename),image.EncodeToPNG());}
            finally {RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
        }
        static void Finish(bool failed)
        {
            EditorApplication.update-=Tick;SessionState.SetBool(Key+"Failed",failed);
            if(root!=null)UnityEngine.Object.DestroyImmediate(root);AudioListener.volume=volume;EditorApplication.isPlaying=false;
        }
    }
}
