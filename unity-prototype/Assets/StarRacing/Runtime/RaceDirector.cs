using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace StarRacingPrototype {
 [DefaultExecutionOrder(-100)]
 public sealed class RaceDirector : MonoBehaviour {
  public ReleaseBalance Balance {get;private set;}
  public LabSnapshot RaceBalanceSnapshot {get;private set;}
  public RaceGraphicsSettings Graphics {get;private set;}
  RaceGraphicsBindings.Lease graphicsLease;Bloom graphicsBloom;Volume graphicsVolume;VolumeProfile graphicsVolumeProfile;
  public bool ContactShadowsActive=>graphicsLease!=null&&graphicsLease.Active;
  public bool ShadowsActive=>sun!=null&&sun.shadows!=LightShadows.None;
  public bool BloomActive=>graphicsBloom!=null&&graphicsBloom.active;
  public bool MistActive=>environment.Sky!=null&&environment.Sky.MistEnabled;
  public void SaveGraphics(){UserSettingsStore.Current.graphics=Graphics;UserSettingsStore.Save();ApplyGraphicsSettings();}
  public int DefaultMsaaSamples=>(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset)?.msaaSampleCount??1;
  public void ApplyUserSettings(){
   var preferences=UserSettingsStore.Current;Application.targetFrameRate=preferences.frameLimit==0?-1:preferences.frameLimit;
   Graphics=preferences.graphics;ApplyGraphicsSettings();
   foreach(var camera in cameras)if(camera!=null){var renderCamera=camera.GetComponent<Camera>();renderCamera.allowMSAA=preferences.antiAliasing<0;var data=renderCamera.GetUniversalAdditionalCameraData();data.antialiasing=preferences.antiAliasing==1?AntialiasingMode.FastApproximateAntialiasing:preferences.antiAliasing==2?AntialiasingMode.SubpixelMorphologicalAntiAliasing:AntialiasingMode.None;}
  }
  void ApplyGraphicsSettings(){if(Graphics==null)return;graphicsLease?.Apply(Graphics.contactShadows);if(sun!=null)sun.shadows=Graphics.shadows?LightShadows.Soft:LightShadows.None;if(graphicsBloom!=null)graphicsBloom.active=Graphics.bloom;environment.Sky?.SetMistEnabled(Graphics.mist);}
  public string BalanceError {get;private set;}="";
  public MagneticVehicle[] Cars {get;private set;}
  public TrackBuilder Track {get;private set;}
  public NativeTrackCourse Course {get;private set;}
  public LocalInputRouter Input {get;private set;}
  public RaceSession Session {get;private set;}
  public RaceRoster Roster {get;private set;}
  public AiDriver[] Drivers {get;private set;}
  public AiWorldSnapshot World {get;private set;}
  public GraphAiWorldSnapshot GraphWorld {get;private set;}
  public bool Started {get;private set;}
  public bool InPreparationMenu=>!Started;
  public bool Paused {get;private set;}=true;
  public int RaceGeneration {get;private set;}
  public int EntrantCount {get;private set;}=8;
  public int HumanCount {get;private set;}=2;
  public LocalRaceConfig AppliedConfig {get;private set;}
  public string StartError {get;private set;}="";
  public bool Handicap {get;private set;}
  public uint TrackSeed {get;private set;}=77;
  public string TrackThemePreference {get;private set;}="random";
  public string TrackRailMode {get;private set;}="normal";
  public bool TrackJumps {get;private set;}
  public string TrackSummary {get;private set;}="";
  public float RaceTime=>(float)(Session?.Elapsed??0);
  public Color[] Colors={new Color(.15f,.85f,1),new Color(1,.42f,.12f)};
  public event Action<DrivingContactEvent> DrivingContact;
  readonly ChaseCamera[] cameras=new ChaseCamera[4];
  TrackEnvironmentBuilder environment=new TrackEnvironmentBuilder();
  GameObject activeRaceRoot;
  readonly List<RecoveryEvent> pendingRecoveryEvents=new List<RecoveryEvent>();
  readonly List<RecoveryEvent> vehicleRecoveryEvents=new List<RecoveryEvent>();
  const float AiDecisionInterval=.1f;
  float[] decisionTimers;
  double[] lastDecisionTimes;
  int[] decisionRevisions;
  DrivingInput[] botCommands;
  RaceObservation[] observations;
  DrivingContactObservation[] contactObservations;
  DrivingContactPolicy contactPolicy;
  Light sun,spaceFill;
  public int AudioHumanSeat(int entrant)=>Roster!=null&&entrant>=0&&entrant<Roster.Entrants.Length?Roster.Entrants[entrant].HumanSeat:-1;
  public ChaseCamera CameraFor(int seat)=>cameras[seat];
  public Color EntrantColor(int i)=>i<HumanCount?CloudlineSkin.PlayerColors[i]:Color.HSVToRGB((i*.618034f)%1,.55f,.9f);
  public void ConsumeRecoveryEvents(List<RecoveryEvent> destination){destination.AddRange(pendingRecoveryEvents);pendingRecoveryEvents.Clear();}
  internal static bool StartupAudioDisabled(string[] arguments)=>SilentStartup.IsRequested(arguments);
  public bool Ready {get;private set;}
  LoadingWork activePreparation;bool pauseDuringPreparation;
  public void BeginLoading(){pauseDuringPreparation=false;}
  public void ApplyLoadingPause(){if(pauseDuringPreparation&&Started&&Session!=null){Paused=true;Session.Paused=true;Input?.Block();Time.timeScale=0;}}
  void Awake(){if(GetComponent<RaceStartup>()==null)LoadingWork.Drain(InitializeSteps(false));}
  LoadingWork OwnPreparation(IEnumerator steps){if(activePreparation!=null&&activePreparation.IsPending)throw new InvalidOperationException("Подготовка уже идёт");if(GetComponent<RaceMenu>()?.Loading!=true)BeginLoading();return activePreparation=new LoadingWork(steps);}
  public LoadingWork InitializeForLoading()=>OwnPreparation(InitializeSteps(true));
  IEnumerator InitializeSteps(bool background){
   using(var freeze=new LoadingWork.Freeze()) {
   yield return "Загружаем меню";
   if(background)yield return DesignLabRuntime.Prepare();
   if(GetComponent<RacePhysicsStepper>()==null)gameObject.AddComponent<RacePhysicsStepper>();
   try {var active=DesignLabRuntime.Current.Active;Balance=ReleaseBalance.Parse(active?.Json??Resources.Load<TextAsset>("balance-config")?.text);if(active==null)BalanceError=DesignLabRuntime.Current.Warning;}catch(Exception e){BalanceError=e.Message;Debug.LogError("BALANCE_ERROR "+e.Message);}
   Application.targetFrameRate=UserSettingsStore.Current.frameLimit==0?-1:UserSettingsStore.Current.frameLimit;Time.fixedDeltaTime=.01f;Time.maximumDeltaTime=.1f;
   Graphics=UserSettingsStore.Current.graphics;var bindings=Resources.Load<RaceGraphicsBindings>("GraphicsRuntimeBindings");if(bindings==null)throw new InvalidOperationException("GraphicsRuntimeBindings resource missing");graphicsLease=bindings.Acquire();graphicsLease.Apply(Graphics.contactShadows);
   var initial=LocalRaceConfig.Load();initial.humans=Mathf.Max(1,initial.humans);ApplyValues(initial);
   Track=new GameObject("Track").AddComponent<TrackBuilder>();
   yield return GenerateInitialSteps(background);
   yield return Track.BuildNativeSteps(Course);
   yield return BuildDecorSteps(environment,Track,Course,TrackSeed,ResolvedTheme(AppliedConfig),background);
   for(int i=0;i<4;i++){
    var c=new GameObject("Camera "+(i+1)).AddComponent<Camera>();c.rect=new Rect(i*.5f,0,.5f,1);c.fieldOfView=65;c.nearClipPlane=.2f;c.farClipPlane=1800;
    c.backgroundColor=new Color(.018f,.027f,.06f);c.GetUniversalAdditionalCameraData().renderPostProcessing=true;
    cameras[i]=c.gameObject.AddComponent<ChaseCamera>();
    c.gameObject.AddComponent<NitroEdgeBlur>().director=this;
   }
   sun=new GameObject("Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.shadows=LightShadows.Soft;
   spaceFill=new GameObject("Space fill").AddComponent<Light>();spaceFill.type=LightType.Directional;spaceFill.shadows=LightShadows.None;
   ApplyLighting();
   var volume=new GameObject("Color and bloom").AddComponent<Volume>();volume.isGlobal=true;volume.profile=ScriptableObject.CreateInstance<VolumeProfile>();
   graphicsVolume=volume;graphicsVolumeProfile=volume.profile;var bloom=volume.profile.Add<Bloom>();graphicsBloom=bloom;bloom.intensity.Override(.25f);bloom.threshold.Override(1.1f);volume.profile.Add<Tonemapping>().mode.Override(TonemappingMode.ACES);ApplyGraphicsSettings();
   ApplyUserSettings();yield return CreateRosterSteps();gameObject.AddComponent<RaceMenu>().director=this;gameObject.AddComponent<RaceHud>().director=this;Restart(false);if(!StartupAudioDisabled(Environment.GetCommandLineArgs()))gameObject.AddComponent<RaceAudioCoordinator>();
   Ready=true;freeze.Keep(0);
   }
  }
  static string ResolvedTheme(LocalRaceConfig config)=>config.theme=="random"?(config.Seed%2==0?"cloud-city":"space-station"):config.theme;
  IEnumerator GenerateInitialSteps(bool background){
   string theme=ResolvedTheme(AppliedConfig);
   if(background){var seed=TrackSeed;var settings=new Procedural.GraphCourseSettings(theme,AppliedConfig.rails,AppliedConfig.jumps);int revision=AppliedConfig.generatorRevision;
    var task=LoadingWork.Background(()=>NativeTrackGenerator.GenerateData(seed,settings,revision));while(!task.IsCompleted)yield return null;Course=task.GetAwaiter().GetResult().Complete();}
   else Course=GenerateCourse(AppliedConfig,theme,false);
   TrackSummary=$"SEED {TrackSeed} · {theme} · {Course.Finish-Course.Start:0} м";
  }
  static IEnumerator BuildDecorSteps(TrackEnvironmentBuilder decor,TrackBuilder track,NativeTrackCourse course,uint seed,string theme,bool background)=>course.IsIndependent?decor.BuildIndependentSteps(track.GraphSurface,seed,theme,null,background):decor.BuildSteps(track.Route,track.transform,seed,background);
  void ApplyLighting(){if(sun!=null){environment.Sky.Activate();RaceLightingController.Apply(environment.Plan.lighting,sun,spaceFill,cameras);foreach(var camera in cameras)if(camera!=null)camera.SetSkyline(environment.Plan);ApplyGraphicsSettings();}}
  NativeTrackCourse GenerateCourse(LocalRaceConfig config,string theme,bool replay)=>NativeTrackGenerator.Generate(config.Seed,new Procedural.GraphCourseSettings(theme,config.rails,config.jumps),config.generatorRevision,replay&&config.replay!=null&&config.replay.Matches(config,theme)&&config.replay.fallback);
  static float GridDistance(NativeTrackCourse course,TrackBuilder track,int slot)=>course.IsIndependent?course.Start-(slot/2)*10:RaceRoster.GridDistance(track.Route,slot);
  static float GridLateral(NativeTrackCourse course,TrackBuilder track,int slot)=>course.IsIndependent?(slot%2==0?-3.5f:3.5f):RaceRoster.GridLateral(track.Route,slot);
  static void ResetCar(MagneticVehicle car,NativeTrackCourse course,TrackBuilder track,float distance,float lane){if(course.IsIndependent)car.ResetOnGraph(course.Graph.Cursor(0,distance),lane);else car.ResetAt(distance,lane);}
  static void InitializeCar(MagneticVehicle car,NativeTrackCourse course,TrackBuilder track,int seat,Color color,ReleaseBalance balance,bool human,float distance){if(course.IsIndependent)car.InitializeIndependent(track,course.Graph.Cursor(0,distance),seat,color,balance,human);else car.Initialize(track,seat,color,balance,human);}
  IEnumerator CreateRosterSteps(){
   if(Cars!=null)foreach(var car in Cars){car.gameObject.SetActive(false);Destroy(car.gameObject);}
   Roster=new RaceRoster(EntrantCount,HumanCount,TrackSeed);Cars=new MagneticVehicle[EntrantCount];Drivers=new AiDriver[EntrantCount];observations=new RaceObservation[EntrantCount];World=new AiWorldSnapshot(EntrantCount);
   decisionTimers=new float[EntrantCount];lastDecisionTimes=new double[EntrantCount];decisionRevisions=new int[EntrantCount];botCommands=new DrivingInput[EntrantCount];
   contactObservations=new DrivingContactObservation[EntrantCount];contactPolicy=new DrivingContactPolicy();
   for(int i=0;i<EntrantCount;i++){
    yield return null;
    var e=Roster.Entrants[i];Cars[i]=new GameObject(e.Name).AddComponent<MagneticVehicle>();InitializeCar(Cars[i],Course,Track,i,EntrantColor(i),Balance,e.HumanSeat>=0,GridDistance(Course,Track,e.GridSlot));
    int index=i;Cars[i].ContactObserved+=collision=>ObserveContact(index,collision);
    if(Balance!=null){Cars[i].Drive.ConfigureHandicap(e.Profile,Handicap);if(e.HumanSeat<0)Drivers[i]=new AiDriver(i,e.Profile,e.Seed,Balance);}
   }
   Session=CreateSessionForTrack(Track,Course.Start,Course.Finish,EntrantCount,HumanCount);
   for(int i=0;i<4;i++){cameras[i].target=i<HumanCount?Cars[i]:null;cameras[i].enabled=i<HumanCount;cameras[i].GetComponent<Camera>().enabled=Started&&i<HumanCount;if(i<HumanCount)cameras[i].GetComponent<Camera>().rect=RaceViewports.For(HumanCount,i);}
  }
  void ApplyValues(LocalRaceConfig config){
   AppliedConfig=config.Copy();EntrantCount=config.entrants;HumanCount=config.humans;Handicap=false;
   TrackSeed=config.Seed;TrackThemePreference=config.theme;TrackRailMode=config.rails;TrackJumps=config.jumps;
  }
  public bool TryStartSelected(LocalRaceConfig config,out string error){
   using(var work=PrepareForLoading(config,false)){while(work.MoveNext()){}error=PreparationError(work);return work.Error==null&&Started&&string.IsNullOrEmpty(error);}
  }
  public LoadingWork PrepareForLoading(LocalRaceConfig config,bool background=true)=>OwnPreparation(PrepareSteps(config,background));
  public string PreparationError(LoadingWork work){
   if(work.Error!=null){StartError="Не удалось подготовить гонку. Измените параметры трассы.";Debug.LogException(work.Error);}
   return StartError;
  }
  IEnumerator PrepareSteps(LocalRaceConfig config,bool background){
   StartError="";if(Started){StartError="Гонка уже идёт";yield break;}
   LabSnapshot balanceSnapshot=null;ReleaseBalance nextBalance=null;
   try{balanceSnapshot=DesignLabRuntime.Current.Capture();nextBalance=ReleaseBalance.Parse(balanceSnapshot.Json);}catch(Exception e){StartError=e.Message;}
   if(!string.IsNullOrEmpty(StartError))yield break;
   var input=new LocalInputRouter();if(!input.TryBind(config,out var bindingError)){StartError=bindingError;yield break;}
   GameObject root=null;TrackEnvironmentBuilder decor=null;
   using(var freeze=new LoadingWork.Freeze())try{
    var snapshot=config.Copy();snapshot.handicap=false;int count=snapshot.entrants,humans=snapshot.humans,generation=RaceGeneration+1;
    string theme=snapshot.theme=="random"?(snapshot.Seed%2==0?"cloud-city":"space-station"):snapshot.theme;
    yield return "Рассчитываем трассу";
    NativeTrackCourse course;
    if(background){var settings=new Procedural.GraphCourseSettings(theme,snapshot.rails,snapshot.jumps);bool fallback=snapshot.replay!=null&&snapshot.replay.Matches(snapshot,theme)&&snapshot.replay.fallback;
     var task=LoadingWork.Background(()=>NativeTrackGenerator.GenerateData(snapshot.Seed,settings,snapshot.generatorRevision,fallback));while(!task.IsCompleted)yield return null;course=task.GetAwaiter().GetResult().Complete();}
    else course=GenerateCourse(snapshot,theme,true);
    var roster=new RaceRoster(count,humans,snapshot.Seed);roster.Shuffle(unchecked(snapshot.Seed+(uint)generation*7919));
    var route=course.IsIndependent?null:new TrackRoute(course.Single);
    var distances=new float[count];var lanes=new float[count];
    for(int i=0;i<count;i++){distances[i]=course.IsIndependent?course.Start-(roster.Entrants[i].GridSlot/2)*10:RaceRoster.GridDistance(route,roster.Entrants[i].GridSlot);lanes[i]=course.IsIndependent?(roster.Entrants[i].GridSlot%2==0?-3.5f:3.5f):RaceRoster.GridLateral(route,roster.Entrants[i].GridSlot);}
    // Staging colliders stay active for admission; the freeze lease prevents all simulation until publication.
    root=new GameObject("Prepared race");
    var trackObject=new GameObject("Track");trackObject.transform.SetParent(root.transform);var track=trackObject.AddComponent<TrackBuilder>();
    yield return "Строим трассу";yield return track.BuildNativeSteps(course);
    decor=new TrackEnvironmentBuilder();yield return "Оформляем мир";
    yield return BuildDecorSteps(decor,track,course,snapshot.Seed,theme,background);decor.Root.SetParent(root.transform,true);
    var cars=new MagneticVehicle[count];var drivers=new AiDriver[count];var seen=new RaceObservation[count];
    yield return "Готовим участников";
    for(int i=0;i<count;i++){
     yield return null;
     var entrant=roster.Entrants[i];var go=new GameObject(entrant.Name);go.transform.SetParent(root.transform);
     cars[i]=go.AddComponent<MagneticVehicle>();InitializeCar(cars[i],course,track,i,i<humans?CloudlineSkin.PlayerColors[i]:Color.HSVToRGB((i*.618034f)%1,.55f,.9f),nextBalance,entrant.HumanSeat>=0,distances[i]);
     cars[i].Drive.ConfigureHandicap(entrant.Profile,snapshot.handicap);cars[i].SetDrivingGeneration(generation);ResetCar(cars[i],course,track,distances[i],lanes[i]);cars[i].Hold(true);
     if(entrant.HumanSeat<0)drivers[i]=new AiDriver(i,entrant.Profile,entrant.Seed,nextBalance);
     int index=i;cars[i].ContactObserved+=collision=>ObserveContact(index,collision);seen[i]=ObserveVehicle(cars[i]);
    }
    var session=CreateSessionForTrack(track,course.Start,course.Finish,count,humans);session.Reset(seen);
    var timers=new float[count];var revisions=new int[count];for(int i=0;i<count;i++){timers[i]=i<humans?0:AiDecisionInterval*(i-humans)/Mathf.Max(1,count-humans);revisions[i]=cars[i].PositionRevision;}
    var world=new AiWorldSnapshot(count);var contacts=new DrivingContactObservation[count];var policy=new DrivingContactPolicy();
    var times=new double[count];var commands=new DrivingInput[count];
    if(snapshot.replay!=null&&snapshot.replay.Matches(snapshot,theme))snapshot.replay.Verify(course,track);snapshot.replay=NativeCourseReplay.Capture(course,track);
    yield return "Проверяем старт";
    if(!input.TryBind(snapshot,out bindingError))throw new InvalidOperationException(bindingError);
    var oldTrack=Track;var oldCars=Cars;var oldDecor=environment;var oldRoot=activeRaceRoot;
    // Publish only after generation, bindings, placement and resource construction succeeded.
    Balance=nextBalance;RaceBalanceSnapshot=balanceSnapshot;ApplyValues(snapshot);Course=course;Track=track;GraphWorld=null;Cars=cars;Drivers=drivers;Roster=roster;Session=session;World=world;Input=input;environment=decor;
    observations=seen;decisionTimers=timers;decisionRevisions=revisions;lastDecisionTimes=times;botCommands=commands;contactObservations=contacts;contactPolicy=policy;
    RaceGeneration=generation;activeRaceRoot=root;root=null;decor=null;
    oldTrack.gameObject.SetActive(false);foreach(var car in oldCars)car.gameObject.SetActive(false);
    activeRaceRoot.SetActive(true);Physics.SyncTransforms();
    for(int i=0;i<count;i++){ResetCar(cars[i],course,track,distances[i],lanes[i]);cars[i].Hold(true);observations[i]=Observation(i);decisionRevisions[i]=cars[i].PositionRevision;}
    Session.Reset(observations);Session.Begin();Input.PrepareStart();Started=true;Paused=pauseDuringPreparation;Session.Paused=Paused;Time.timeScale=0;
    pendingRecoveryEvents.Clear();vehicleRecoveryEvents.Clear();
    for(int i=0;i<4;i++){cameras[i].target=i<humans?cars[i]:null;cameras[i].enabled=i<humans;cameras[i].GetComponent<Camera>().enabled=i<humans;if(i<humans){cameras[i].GetComponent<Camera>().rect=RaceViewports.For(humans,i);cameras[i].Snap();}}
    ApplyLighting();TrackSummary=$"SEED {TrackSeed} · {theme} · {course.Finish-course.Start:0} м";config.replay=snapshot.replay;AppliedConfig.Save();
    oldDecor.Clear();if(oldRoot!=null)Destroy(oldRoot);else{Destroy(oldTrack.gameObject);foreach(var car in oldCars)Destroy(car.gameObject);}
    StartError="";freeze.Keep(Paused?0:1);
   }finally{if(root!=null)root.SetActive(false);if(decor!=null)decor.Clear();if(root!=null)Destroy(root);}
  }
  // Experimental graph callers supply common start/finish; normal Generator11
  // still selects the exact legacy session and never activates independent roads.
  public static RaceSession CreateSessionForTrack(TrackBuilder track,float start,float finish,int count,int humans) =>
   track.GraphSurface!=null?new RaceSession(track.GraphSurface,start,finish,count,humans):new RaceSession(start,finish,count,humans);
  public RaceObservation Observation(int entrant)=>ObserveVehicle(Cars[entrant]);
  public static RaceObservation ObserveVehicle(MagneticVehicle car) {
   if(car.GraphRoute!=null) {
    var observation=car.GraphRoute.RaceObservation;
    return new RaceObservation(observation.Tick==MagneticVehicle.GraphFixedTick &&
     observation.PositionRevision==car.PositionRevision && !car.IsFalling ? observation : default);
   }
   return new RaceObservation(car.Distance,car.PositionRevision,Vector3.Distance(car.Body.position,car.Frame.position)<car.Frame.halfWidth+3f);
  }
  public void Restart(bool start=true)=>LoadingWork.Drain(RestartSteps(start));
  public LoadingWork RepeatForLoading(){StartError="";return OwnPreparation(RestartSteps(true));}
  IEnumerator RestartSteps(bool start){
   using(var freeze=new LoadingWork.Freeze()) {
   try {
    yield return "Готовим повтор гонки";
    if(Balance==null)start=false;
    int generation=RaceGeneration+1;
    var plannedRoster=new RaceRoster(EntrantCount,HumanCount,TrackSeed);plannedRoster.Shuffle(unchecked(TrackSeed+(uint)generation*7919));
    var distances=new float[Cars.Length];var lanes=new float[Cars.Length];
    // Plan placement while the live roster/session/cars remain unchanged. Cancel
    // is safe at every yield; the final in-place reset is a single atomic commit.
    for(int i=0;i<Cars.Length;i++){
     int slot=plannedRoster.Entrants[i].GridSlot;distances[i]=GridDistance(Course,Track,slot);lanes[i]=GridLateral(Course,Track,slot);yield return null;
    }
    Track.TireMarks?.Clear();GetComponent<RaceMenu>()?.ResetPauseSettings();
    RaceGeneration=generation;pendingRecoveryEvents.Clear();vehicleRecoveryEvents.Clear();contactPolicy.Reset();
    Started=start;Paused=!start||pauseDuringPreparation;Time.timeScale=0;Input=Input??new LocalInputRouter();Input.Block();
    Roster.Shuffle(unchecked(TrackSeed+(uint)generation*7919));
    for(int i=0;i<Cars.Length;i++){
     Cars[i].SetDrivingGeneration(generation);ResetCar(Cars[i],Course,Track,distances[i],lanes[i]);Cars[i].Hold(true);Drivers[i]?.Reset();observations[i]=Observation(i);
     decisionTimers[i]=i<HumanCount?0:AiDecisionInterval*(i-HumanCount)/Mathf.Max(1,Cars.Length-HumanCount);decisionRevisions[i]=Cars[i].PositionRevision;lastDecisionTimes[i]=0;botCommands[i]=default;
    }
    Session.Reset(observations);for(int i=0;i<4;i++){cameras[i].GetComponent<Camera>().enabled=start&&i<HumanCount;if(i<HumanCount)cameras[i].Snap();}
    if(start){Session.Begin();Input.PrepareStart();}Session.Paused=Paused;
    freeze.Keep(Paused?0:1);
   }finally{if(pauseDuringPreparation&&Started){Paused=true;Session.Paused=true;freeze.Keep(0);}}
   }
  }
  public void SetPaused(bool paused){
   if(LoadingWork.SimulationFrozen){pauseDuringPreparation|=paused;return;}
   Track.TireMarks?.BreakAll();if(!paused){pauseDuringPreparation=false;GetComponent<RaceMenu>()?.ResetPauseSettings();}Paused=paused;Time.timeScale=paused?0:1;
   if(paused)foreach(var camera in cameras)camera?.GetComponent<NitroEdgeBlur>()?.ResetEffect();Input.Block();Session.Paused=paused;
  }
  public bool ExitToMenu(){if(!Started||(!Paused&&Session.Phase!=RacePhase.Results))return false;Restart(false);GetComponent<RaceMenu>()?.Open(RaceMenuScreen.LocalSetup);return true;}
  public void StartRace(){
   if(Balance==null)return;
   if(!Started){GetComponent<RaceMenu>()?.StartSelected();return;}
   if(Session.Phase==RacePhase.Results){GetComponent<RaceMenu>().RepeatRace();return;}if(Paused&&!Input.MissingDevice)SetPaused(false);
  }
  void RequestBackgroundPause(){if(activePreparation?.IsPending==true||GetComponent<RaceMenu>()?.Loading==true)pauseDuringPreparation=true;else if(Input!=null&&Started)SetPaused(true);}
  void OnApplicationFocus(bool focus){if(!focus)RequestBackgroundPause();}
  void OnApplicationPause(bool paused){if(paused)RequestBackgroundPause();}
  void Update(){
   if(!Ready||LoadingWork.SimulationFrozen||Input==null||GetComponent<RaceMenu>().Loading)return;Input.Refresh();var k=Keyboard.current;
   if(!Started)return;
   if(Input.MissingDevice&&!Paused)SetPaused(true);
   bool submenuBack=Input.PausePressed||(k!=null&&k.escapeKey.wasPressedThisFrame);
   foreach(var pad in Gamepad.all)submenuBack|=pad.buttonEast.wasPressedThisFrame;
   if(GetComponent<RaceMenu>().ConsumePauseSettingsBack(submenuBack))return;
   if(Input.PausePressed||(k!=null&&k.escapeKey.wasPressedThisFrame)){if(!Paused)SetPaused(true);else if(!Input.MissingDevice)SetPaused(false);}
   if(k!=null){if(k.f5Key.wasPressedThisFrame&&!Input.MissingDevice)GetComponent<RaceMenu>().RepeatRace();if(k.backspaceKey.wasPressedThisFrame)ExitToMenu();}
   for(int i=0;i<HumanCount;i++){
    var command=Input.Read(i);bool driving=Session.CanDrive(i)&&!Paused;
    Cars[i].SetInput(driving?command:default);
    Cars[i].SetPresentationInput(command.throttle,!Paused&&(Session.Phase==RacePhase.Countdown||Session.CanDrive(i)));
   }
  }
  void FixedUpdate(){
   if(!Ready||LoadingWork.SimulationFrozen||Paused||Cars==null)return;
   for(int i=0;i<Cars.Length;i++){Cars[i].PrepareProjection();observations[i]=Observation(i);}Session.Tick(Time.fixedDeltaTime,observations);
   if(Track.GraphSurface!=null) {
    if(GraphWorld==null || !ReferenceEquals(GraphWorld.Navigation.Surface,Track.GraphSurface) || !GraphWorld.Navigation.Current)GraphWorld=new GraphAiWorldSnapshot(Track.GraphSurface,Cars.Length,Balance);
    GraphWorld.Capture(Cars,Drivers,Session.Elapsed);
   }else World.Capture(Cars,Track.Route,Session.Elapsed);
   for(int i=0;i<Cars.Length;i++){
    var car=Cars[i];car.SetRaceContext(Balance!=null&&Session.CanDrive(i),Session.Racers[i].Place,Cars.Length,Session.Elapsed);
    if(Session.Racers[i].Finished){if(!car.FinishedCoasting){car.BeginFinishCoast();if(i<HumanCount)cameras[i].LockAtFinish();}}else car.Hold(!Session.CanDrive(i));
    if(Drivers[i]!=null){
     if(Session.CanDrive(i)){
      decisionTimers[i]+=Time.fixedDeltaTime;
      if(decisionTimers[i]>=AiDecisionInterval || decisionRevisions[i]!=car.PositionRevision || Track.GraphSurface!=null&&!GraphWorld[i].Valid){
       float decisionDt=Mathf.Max(Time.fixedDeltaTime,(float)(Session.Elapsed-lastDecisionTimes[i]));
       botCommands[i]=Track.GraphSurface!=null?Drivers[i].StepGraph(GraphWorld,decisionDt):Drivers[i].Step(World,Track.Route,decisionDt);
       lastDecisionTimes[i]=Session.Elapsed;
       decisionTimers[i]=0;decisionRevisions[i]=car.PositionRevision;
      }
      var applied=Track.GraphSurface!=null?Drivers[i].ApplyGraphControl(car,botCommands[i],Time.fixedDeltaTime):botCommands[i];
      car.SetInput(applied);
     }else car.SetInput(default);
     car.SetPresentationInput(botCommands[i].throttle,Session.CanDrive(i)&&!Paused);
    }
    vehicleRecoveryEvents.Clear();car.DrainRecoveryEvents(vehicleRecoveryEvents);foreach(var e in vehicleRecoveryEvents)if(e.Generation==RaceGeneration&&!Session.Racers[i].Finished)pendingRecoveryEvents.Add(e);
   }
   CaptureContacts();
  }
  void CaptureContacts() {
   for(int i=0;i<Cars.Length;i++) {
    var car=Cars[i];var frame=car.Frame;
    if(car.GraphRoute!=null){contactObservations[i]=GraphContactObservation(car,Roster.Entrants[i].Id,RaceGeneration,Session.CanDrive(i));continue;}
    // TrackRoute flips source right; retain the source sign for score/tie/rail rules.
    Vector3 sourceRight=-frame.right;
    float lateral=Vector3.Dot(car.Body.position-frame.position,sourceRight);
    float heading=Mathf.Atan2(Vector3.Dot(car.transform.forward,sourceRight),Vector3.Dot(car.transform.forward,frame.tangent));
    var spans=Track.Route.PavedAt(car.Distance);int selected=-1;float error=float.PositiveInfinity;
    for(int j=0;j<spans.Length;j++){float gap=Mathf.Abs(lateral-(float)spans[j].offset)-(float)spans[j].halfWidth;if(gap<error){error=gap;selected=j;}}
    var span=selected<0?default:spans[selected];float local=lateral-(float)span.offset;
    float limit=Mathf.Max(.2f,(float)span.halfWidth-VehicleGeometry.LateralExtent(heading)-.08f);
    contactObservations[i]=new DrivingContactObservation{Id=Roster.Entrants[i].Id,Generation=RaceGeneration,Revision=car.PositionRevision,
     Distance=car.Distance,Lateral=lateral,Heading=heading,Velocity=car.Body.linearVelocity,Right=sourceRight,Tangent=frame.tangent,Normal=frame.normal,
     Span=spans.Length>1?selected:-1,LeftRail=Track.Route.Definition==null||span.railLeft,RightRail=Track.Route.Definition==null||span.railRight,
     LeftClearance=limit+local,RightClearance=limit-local,
     SolidSuppressed=!Session.CanDrive(i)||car.IsGhosting||car.FinishedCoasting,
     Suppressed=!Session.CanDrive(i)||car.IsGhosting||car.FinishedCoasting||car.ActiveJumpId.Length>0||car.OffRouteSeconds>.05f||selected<0||error>VehicleGeometry.HalfWidth};
   }
   contactPolicy.BeginStep(contactObservations);
  }
  public static DrivingContactObservation GraphContactObservation(MagneticVehicle car,int id,int generation,bool driving) {
   var frame=car.Frame;var right=-frame.right;float lateral=Vector3.Dot(car.Body.position-frame.position,right);
   float heading=Mathf.Atan2(Vector3.Dot(car.transform.forward,right),Vector3.Dot(car.transform.forward,frame.tangent));
   float room=Mathf.Max(.2f,frame.halfWidth-VehicleGeometry.LateralExtent(heading)-.08f);var route=ObserveVehicle(car).graph;
   return new DrivingContactObservation{Id=id,Generation=generation,Revision=car.PositionRevision,Span=-1,GraphMode=true,Graph=route,Position=car.Body.position,
    Distance=car.Distance,Lateral=lateral,Heading=heading,Velocity=car.Body.linearVelocity,Right=right,Tangent=frame.tangent,Normal=frame.normal,
    LeftClearance=room+lateral,RightClearance=room-lateral,LeftRail=car.GraphRoute.Frame.railLeft,RightRail=car.GraphRoute.Frame.railRight,
    SolidSuppressed=!driving||car.IsGhosting||car.FinishedCoasting,
    Suppressed=!driving||!route.Valid||car.IsGhosting||car.FinishedCoasting||car.ActiveJumpId.Length>0||car.OffRouteSeconds>.05f};
  }
  void ObserveContact(int index,Collision collision) {
   if(Paused||Session==null||contactPolicy==null)return;
   var car=Cars[index];var snapshot=contactObservations[index];
   if(snapshot.Generation!=RaceGeneration||snapshot.Revision!=car.PositionRevision||car.IsGhosting||car.FinishedCoasting)return;
   var other=collision.rigidbody==null?null:collision.rigidbody.GetComponent<MagneticVehicle>();
   DrivingContactEvent contact;
   if(other!=null){
    if(car.ActiveJumpId.Length>0||car.OffRouteSeconds>.05f)return;
    int otherIndex=System.Array.IndexOf(Cars,other);if(otherIndex<0||contactObservations[otherIndex].Revision!=other.PositionRevision||other.IsGhosting||other.FinishedCoasting||other.ActiveJumpId.Length>0||other.OffRouteSeconds>.05f)return;
    if(contactPolicy.Vehicle(snapshot.Id,Roster.Entrants[otherIndex].Id,out contact))DrivingContact?.Invoke(contact);
   }else{
    float speed=DrivingContactPolicy.SolidNormalSpeed(collision,snapshot.Normal);
    if(speed>=0&&contactPolicy.Solid(snapshot.Id,speed,out contact))DrivingContact?.Invoke(contact);
   }
  }
  public void CancelPendingInitialization(){if(!Ready){activePreparation?.Dispose();ReleaseOwnedResources();}}
  void OnDisable(){activePreparation?.Dispose();if(!Ready)ReleaseOwnedResources();}
  void OnDestroy(){activePreparation?.Dispose();Time.timeScale=1;ReleaseOwnedResources();}
  void ReleaseOwnedResources(){
   graphicsLease?.Dispose();graphicsLease=null;
   if(graphicsVolume!=null)Destroy(graphicsVolume.gameObject);if(graphicsVolumeProfile!=null)Destroy(graphicsVolumeProfile);environment.Clear();
   if(activeRaceRoot!=null)Destroy(activeRaceRoot);else {if(Track!=null)Destroy(Track.gameObject);if(Cars!=null)foreach(var car in Cars)if(car!=null)Destroy(car.gameObject);}
   foreach(var camera in cameras)if(camera!=null)Destroy(camera.gameObject);if(sun!=null)Destroy(sun.gameObject);if(spaceFill!=null)Destroy(spaceFill.gameObject);
   activeRaceRoot=null;Track=null;Cars=null;System.Array.Clear(cameras,0,cameras.Length);sun=spaceFill=null;graphicsVolume=null;graphicsVolumeProfile=null;
  }
 }
}
