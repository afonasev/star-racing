using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StarRacingPrototype {
    // Opt-in observer only: exposed bounds, never PhysX's internal swept arrays.
    [InitializeOnLoad] public static class BroadphaseProjectionCensus {
        const string Key="StarRacing.BroadphaseProjectionCensus";
        static double deadline;
        public static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_PROJECTION_CENSUS_DIR");
        static BroadphaseProjectionCensus(){EditorApplication.playModeStateChanged+=Changed;}
        public static void Run(){
            var args=Environment.GetCommandLineArgs();
            if(!args.Contains("-noaudio")||!args.Contains("-muted")||!Path.IsPathRooted(Output??""))throw new Exception("Silent startup flags and absolute census output required");
            Directory.CreateDirectory(Output);SessionState.SetBool(Key,true);SessionState.SetBool(Key+"Failed",true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);EditorApplication.isPlaying=true;
        }
        static void Changed(PlayModeStateChange state){
            if(!SessionState.GetBool(Key,false))return;
            if(state==PlayModeStateChange.EnteredPlayMode){deadline=EditorApplication.timeSinceStartup+900;EditorApplication.update+=Guard;new GameObject("Passive broadphase projection census").AddComponent<BroadphaseProjectionCensusDriver>();}
            if(state==PlayModeStateChange.EnteredEditMode){EditorApplication.update-=Guard;SessionState.SetBool(Key,false);EditorApplication.Exit(SessionState.GetBool(Key+"Failed",true)?1:0);}
        }
        static void Guard(){EditorApplication.QueuePlayerLoopUpdate();if(EditorApplication.timeSinceStartup>deadline)Finish(new TimeoutException("Projection census deadline"));}
        public static void Finish(Exception failure=null){
            if(failure!=null){File.WriteAllText(Path.Combine(Output,"failure.txt"),failure.ToString());Debug.LogException(failure);}
            SessionState.SetBool(Key+"Failed",failure!=null);EditorApplication.isPlaying=false;
        }
    }

    [DefaultExecutionOrder(2000)] public sealed class BroadphaseProjectionCensusDriver:MonoBehaviour {
        [Serializable] sealed class Snapshot {public int racingTick,shapes,bodies;public double simulatedSeconds;public long nativeSteps;public string file;}
        [Serializable] sealed class Report {
            public string limitation="Read-only exposed Collider.bounds geometry, not native ABP arrays, CCD-expanded bounds, measured native pairs, performance or acceptance. Shared Editor; no performance claim.";
            public string source,unity,config,staticSHA256,settingsSHA256;
            public int broadphase,staticShapes,unknownStaticShapes,unownedMovingShapes,disabledShapes;
            public bool completed,staticInventoryUnchanged,settingsUnchanged,prefsRestored;
            public float fixedDeltaTime,captureDeltaTime;public int baseSlices;
            public List<Snapshot> snapshots=new List<Snapshot>();public List<string> unknown=new List<string>();
        }
        const string Header="id,body,seat,category,name,mesh,type,layer,include,exclude,priority,contactOffset,trigger,kinematic,minX,minY,minZ,maxX,maxY,maxZ";
        RaceDirector director;GameObject host;Keyboard addedKeyboard;bool had,restored,measuring,done;string saved,settingsPath,physicsSettings;int racingTicks;
        readonly Report report=new Report();Collider[] staticColliders;string[] originalRows;
        static string F(float v)=>v.ToString("R",CultureInfo.InvariantCulture);
        static string Csv(string s)=>"\""+(s??"").Replace("\"","\"\"")+"\"";
        static string Hash(string s){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-","").ToLowerInvariant();}
        static Collider[] All()=>UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Include,FindObjectsSortMode.InstanceID).Where(c=>c.gameObject.scene.IsValid()).ToArray();
        static bool Active(Collider c)=>c.enabled&&c.gameObject.activeInHierarchy;
        static string Category(Collider c){
            string mesh=(c as MeshCollider)?.sharedMesh?.name;
            if(mesh==RoadVolumeMesh.SupportName)return "support";
            if(mesh==RoadVolumeMesh.BottomName)return "bottom";
            if(mesh==RoadVolumeMesh.ExteriorName)return "exterior";
            if(c.GetComponent<TrackSurface>()!=null)return "top";
            if(c.gameObject.layer==TrackBuilder.BarrierCollisionLayer||c.name=="Finish wall")return "barrier";
            return "other";
        }
        static string Row(Collider c,int seat){
            Bounds b=c.bounds;Vector3 min=b.min,max=b.max;
            for(int a=0;a<3;a++)if(float.IsNaN(min[a])||float.IsInfinity(min[a])||float.IsNaN(max[a])||float.IsInfinity(max[a])||min[a]>max[a])throw new Exception("Invalid bounds "+c.name);
            if(float.IsNaN(c.contactOffset)||float.IsInfinity(c.contactOffset)||c.contactOffset<0)throw new Exception("Invalid contactOffset");
            var body=c.attachedRigidbody;
            return string.Join(",",new[]{c.GetInstanceID().ToString(),(body!=null?body.GetInstanceID():0).ToString(),seat.ToString(),seat>=0?"vehicle":Category(c),Csv(c.name),Csv((c as MeshCollider)?.sharedMesh?.name),c.GetType().Name,c.gameObject.layer.ToString(),c.includeLayers.value.ToString(),c.excludeLayers.value.ToString(),c.layerOverridePriority.ToString(),F(c.contactOffset),c.isTrigger?"1":"0",body!=null&&body.isKinematic?"1":"0",F(min.x),F(min.y),F(min.z),F(max.x),F(max.y),F(max.z)});
        }
        static void Table(string filename,IEnumerable<string> rows){
            using(var file=File.Create(Path.Combine(BroadphaseProjectionCensus.Output,filename)))using(var zip=new GZipStream(file,System.IO.Compression.CompressionLevel.Optimal))using(var writer=new StreamWriter(zip,new UTF8Encoding(false))){writer.WriteLine(Header);foreach(string row in rows)writer.WriteLine(row);}
        }
        IEnumerator Start(){
            had=PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey);saved=PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey);settingsPath=UserSettingsStore.OverridePath;
            physicsSettings=File.ReadAllText("ProjectSettings/DynamicsManager.asset");report.settingsSHA256=Hash(physicsSettings);report.source=Environment.GetEnvironmentVariable("STAR_RACING_PROFILE_SOURCE");report.unity=Application.unityVersion;
            UserSettingsStore.OverridePath=Path.Combine(BroadphaseProjectionCensus.Output,"isolated-user-settings.json");UserSettingsStore.Reload();Application.runInBackground=true;
            if(Keyboard.current==null)addedKeyboard=InputSystem.AddDevice<Keyboard>();
            var config=new LocalRaceConfig{entrants=32,humans=1,seed="211933141",theme="cloud-city",rails="normal",jumps=true,generatorRevision=12};config.Save();
            var run=Prepare(config);
            while(true){bool more;try{more=run.MoveNext();}catch(Exception e){Fail(e);yield break;}if(!more)break;yield return run.Current;}
        }
        IEnumerator Prepare(LocalRaceConfig config){
            host=new GameObject("Normal census startup");host.AddComponent<RaceStartup>();
            while(director==null||!director.Ready){director=host.GetComponent<RaceDirector>();yield return null;}
            var menu=director.GetComponent<RaceMenu>();typeof(RaceMenu).GetProperty("Selected").SetValue(menu,config);typeof(RaceMenu).GetField("seedText",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(menu,config.seed);menu.StartSelected();
            while(menu.Loading)yield return null;
            if(!director.Started||director.Cars.Length!=32)throw new Exception("Ordinary cloud32 failed "+menu.Error);
            yield return null; // Let the normal preparation teardown finish before the inventory.
            report.config=JsonUtility.ToJson(director.AppliedConfig);report.fixedDeltaTime=Time.fixedDeltaTime;report.baseSlices=RacePhysicsStepper.Substeps;
            var physics=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0]);report.broadphase=physics.FindProperty("m_BroadphaseType").intValue;
            if(report.broadphase!=2||report.baseSlices!=8||Mathf.Abs(Time.fixedDeltaTime-.01f)>1e-8f)throw new Exception("Physics settings changed");
            var all=All();staticColliders=all.Where(c=>Active(c)&&c.attachedRigidbody==null).ToArray();originalRows=staticColliders.Select(c=>Row(c,-1)).ToArray();
            report.staticShapes=staticColliders.Length;report.disabledShapes=all.Count(c=>!Active(c));
            foreach(var c in staticColliders.Where(c=>Category(c)=="other")){report.unknownStaticShapes++;report.unknown.Add(c.GetInstanceID()+":"+c.name+":"+c.transform.parent?.name);}
            var bodies=new HashSet<Rigidbody>(director.Cars.Select(c=>c.Body));report.unownedMovingShapes=all.Count(c=>Active(c)&&c.attachedRigidbody!=null&&!bodies.Contains(c.attachedRigidbody));
            Table("static.csv.gz",originalRows);report.staticSHA256=Hash(string.Join("\n",originalRows));
            if(report.unknownStaticShapes>0||report.unownedMovingShapes>0)throw new Exception("Unknown relevant inventory; see report");
            Time.captureDeltaTime=.01f;report.captureDeltaTime=Time.captureDeltaTime;measuring=true;Save();
        }
        void FixedUpdate(){
            if(!measuring||done||director.Paused||director.Session.Phase!=RacePhase.Racing)return;
            try{
                racingTicks++;
                if(racingTicks!=200&&racingTicks!=800&&racingTicks!=1400)return;
                var all=All();var rows=new List<string>();var bodyLines=new List<string>{"seat,body,velocityX,velocityY,velocityZ,angularX,angularY,angularZ,sleeping,kinematic,ghost,falling,ccd"};
                foreach(var car in director.Cars){
                    var body=car.Body;var colliders=all.Where(c=>Active(c)&&c.attachedRigidbody==body).ToArray();if(colliders.Length!=9)throw new Exception("Expected9shapes for seat"+car.Seat+" got"+colliders.Length);
                    rows.AddRange(colliders.Select(c=>Row(c,car.Seat)));var v=body.linearVelocity;var a=body.angularVelocity;
                    bodyLines.Add(string.Join(",",new[]{car.Seat.ToString(),body.GetInstanceID().ToString(),F(v.x),F(v.y),F(v.z),F(a.x),F(a.y),F(a.z),body.IsSleeping()?"1":"0",body.isKinematic?"1":"0",car.IsGhosting?"1":"0",car.IsFalling?"1":"0",body.collisionDetectionMode.ToString()}));
                }
                if(rows.Count!=288||director.Cars.Select(c=>c.Body).Distinct().Count()!=32)throw new Exception("Expected32bodies/288shapes");
                string file="dynamic-"+racingTicks+".csv.gz";Table(file,rows);File.WriteAllLines(Path.Combine(BroadphaseProjectionCensus.Output,"bodies-"+racingTicks+".csv"),bodyLines);
                report.snapshots.Add(new Snapshot{racingTick=racingTicks,bodies=32,shapes=rows.Count,file=file,simulatedSeconds=RacePhysicsStepper.SimulatedSeconds,nativeSteps=RacePhysicsStepper.NativeSteps});Save();
                if(racingTicks==1400){
                    var current=All().Where(c=>Active(c)&&c.attachedRigidbody==null).Select(c=>Row(c,-1)).ToArray();report.staticInventoryUnchanged=originalRows.SequenceEqual(current);report.settingsUnchanged=physicsSettings==File.ReadAllText("ProjectSettings/DynamicsManager.asset");
                    if(!report.staticInventoryUnchanged||!report.settingsUnchanged)throw new Exception("Static inventory/settings changed");
                    done=true;report.completed=true;Restore();Save();BroadphaseProjectionCensus.Finish();
                }
            }catch(Exception e){Fail(e);}
        }
        void Save()=>File.WriteAllText(Path.Combine(BroadphaseProjectionCensus.Output,"report.json"),JsonUtility.ToJson(report,true));
        void Fail(Exception e){done=true;Restore();Save();BroadphaseProjectionCensus.Finish(e);}
        void Restore(){
            if(restored)return;restored=true;measuring=false;
            if(had)PlayerPrefs.SetString(LocalRaceConfig.PreferenceKey,saved);else PlayerPrefs.DeleteKey(LocalRaceConfig.PreferenceKey);PlayerPrefs.Save();report.prefsRestored=PlayerPrefs.HasKey(LocalRaceConfig.PreferenceKey)==had&&(!had||PlayerPrefs.GetString(LocalRaceConfig.PreferenceKey)==saved);
            Time.captureDeltaTime=0;Time.timeScale=1;UserSettingsStore.OverridePath=settingsPath;UserSettingsStore.Reload();if(addedKeyboard!=null&&addedKeyboard.added)InputSystem.RemoveDevice(addedKeyboard);
        }
        void OnDestroy()=>Restore();
    }
}
