using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StarRacingPrototype {
    // Opt-in affected contracts for adopting a physics broadphase setting.
    [InitializeOnLoad]
    public static class BroadphaseAffectedChecks {
        const string Key="StarRacing.BroadphaseAffected";
        static int startup;
        public static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_BROADPHASE_QA_DIR");
        static BroadphaseAffectedChecks(){EditorApplication.playModeStateChanged+=Changed;}
        public static void Run(){
            Prepare(false);
        }
        public static void RunGravityRecovery(){Prepare(true);}
        public static void RunContactRecovery(){Prepare(true);SessionState.SetBool(Key+"ContactRecoveryOnly",true);}
        static void Prepare(bool gravityRecoveryOnly){
            if(string.IsNullOrEmpty(Output))throw new ArgumentException("STAR_RACING_BROADPHASE_QA_DIR required");
            Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",false);
            SessionState.SetBool(Key+"GravityRecoveryOnly",gravityRecoveryOnly);
            SessionState.SetBool(Key+"ContactRecoveryOnly",false);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);startup=0;EditorApplication.update+=Enter;
        }
        static void Enter(){if(++startup<15)return;EditorApplication.update-=Enter;EditorApplication.isPlaying=true;}
        static void Changed(PlayModeStateChange state){
            if(!SessionState.GetBool(Key,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode){startup=0;EditorApplication.update+=Check;}
            if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",false)?1:0);}
        }
        static void Check(){
            if(++startup<15)return;EditorApplication.update-=Check;
            try {
                AudioListener.volume=0;
                var settings=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0]);
                var broadphase=settings.FindProperty("m_BroadphaseType");
                if(broadphase.intValue!=2||broadphase.enumDisplayNames[broadphase.enumValueIndex]!="Automatic Box Pruning")throw new Exception("Expected loaded ABP setting");
                // Existing contracts remain intact; road volume includes flat native
                // impacts and both generated themes/all rail modes. No full-suite route.
                if(!SessionState.GetBool(Key+"GravityRecoveryOnly",false))RoadVolumeChecks.Run();
                if(!SessionState.GetBool(Key+"ContactRecoveryOnly",false))GravityChecks.Run();
                RecoveryGhostChecks.Run();
                new GameObject("live broadphase recovery").AddComponent<BroadphaseLiveRecoveryCheck>();
            }catch(Exception e){Fail(e);}
        }
        public static void Fail(Exception e){SessionState.SetBool(Key+"Failed",true);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());Debug.LogException(e);EditorApplication.isPlaying=false;}
        public static void Complete(int assertions){
            File.WriteAllText(Path.Combine(Output,"result.txt"),"BROADPHASE_AFFECTED_OK liveClock=true assertions="+assertions+"\n");
            Debug.Log("BROADPHASE_AFFECTED_OK liveClock=true assertions="+assertions);EditorApplication.isPlaying=false;
        }
    }

    [DefaultExecutionOrder(-100)]
    public sealed class BroadphaseLiveRecoveryCheck : MonoBehaviour {
        MagneticVehicle a,b;double began;bool sentA,sentB,overlapExpiry,clockSaved;int assertions,falls,respawns;
        readonly List<RecoveryEvent> events=new List<RecoveryEvent>(4);
        float previousDt,previousScale,previousMaximum;
        void Require(bool condition,string label){assertions++;if(!condition)throw new Exception("LIVE_RECOVERY "+label);}
        void Start(){
            try {
                previousDt=Time.fixedDeltaTime;previousScale=Time.timeScale;previousMaximum=Time.maximumDeltaTime;clockSaved=true;
                Time.fixedDeltaTime=.01f;Time.timeScale=1;Time.maximumDeltaTime=.1f;
                var road=new GameObject("live road");road.transform.SetParent(transform);
                var track=road.AddComponent<TrackBuilder>();track.Build(new TrackRoute(RoadVolumeChecks.FlatRoad()));
                var balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config").text);
                MagneticVehicle Car(int seat){var obj=new GameObject("live entrant "+seat);obj.transform.SetParent(transform);var car=obj.AddComponent<MagneticVehicle>();car.Initialize(track,seat,Color.cyan,balance,true);car.ResetAt(80+seat*6,seat==0?-2.5f:2.5f);Require(car.HasProvenCheckpoint,"checkpoint admitted");return car;}
                a=Car(0);b=Car(1);Physics.SyncTransforms();gameObject.AddComponent<RacePhysicsStepper>();began=Time.timeAsDouble;
            }catch(Exception e){BroadphaseAffectedChecks.Fail(e);}
        }
        void Depart(MagneticVehicle car){var position=car.Frame.position+car.Frame.right*30;car.Body.position=position;car.transform.position=position;car.ResetPresentation();}
        void Prepare(MagneticVehicle car,double elapsed){car.PrepareProjection();car.SetRaceContext(true,1,2,elapsed);car.SetInput(default);}
        void FixedUpdate(){
            if(a==null||b==null)return;
            try {
                double elapsed=Time.timeAsDouble-began;
                if(!sentA&&elapsed>=.1){Depart(a);sentA=true;}
                if(!sentB&&elapsed>=.45){Depart(b);sentB=true;}
                Prepare(a,elapsed);Prepare(b,elapsed);
            }catch(Exception e){BroadphaseAffectedChecks.Fail(e);}
        }
        void LateUpdate(){
            if(a==null||b==null)return;
            try {
                double elapsed=Time.timeAsDouble-began;events.Clear();a.DrainRecoveryEvents(events);b.DrainRecoveryEvents(events);
                foreach(var e in events){if(e.Kind==RecoveryEventKind.Fall)falls++;else {respawns++;var car=e.Entrant==0?a:b;Require(Vector3.Distance(car.RenderPose.position,car.Body.position)<2,"teleport presentation contains no stale off-road pose");}}
                bool ignored=Physics.GetIgnoreCollision(a.GetComponent<Collider>(),b.GetComponent<Collider>());
                if(a.IsFalling||b.IsFalling||a.IsGhosting||b.IsGhosting)Require(ignored,"real-clock recovery protects pair");
                if(sentB&&respawns==2&&!a.IsGhosting&&!a.IsFalling&&b.IsGhosting){overlapExpiry=true;Require(ignored,"earlier expiry preserves peer protection");}
                if(sentB&&respawns==2&&!a.IsFalling&&!b.IsFalling&&!a.IsGhosting&&!b.IsGhosting){
                    Require(elapsed>3,"production recovery timers advanced on actual clock");Require(falls==2,"two real falls");Require(overlapExpiry,"staggered expiry observed");Require(!ignored,"last expiry restores collision");BroadphaseAffectedChecks.Complete(assertions);a=b=null;
                }
                else if(elapsed>20)throw new Exception("Live recovery timeout falls="+falls+" respawns="+respawns);
            }catch(Exception e){BroadphaseAffectedChecks.Fail(e);}
        }
        void OnDestroy(){if(clockSaved){Time.fixedDeltaTime=previousDt;Time.timeScale=previousScale;Time.maximumDeltaTime=previousMaximum;}}
    }
}
