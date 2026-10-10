using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace StarRacingPrototype {
 [DefaultExecutionOrder(-100)]
 public sealed class RaceDirector : MonoBehaviour {
  public ReleaseBalance Balance {get;private set;}
  public string BalanceError {get;private set;}="";
  public MagneticVehicle[] Cars {get;private set;}
  public TrackBuilder Track {get;private set;}
  public LocalInputRouter Input {get;private set;}
  public RaceSession Session {get;private set;}
  public RaceRoster Roster {get;private set;}
  public AiDriver[] Drivers {get;private set;}
  public AiWorldSnapshot World {get;private set;}
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
  void Awake(){
   if(GetComponent<RacePhysicsStepper>()==null)gameObject.AddComponent<RacePhysicsStepper>();
   try {Balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config")?.text);}catch(Exception e){BalanceError=e.Message;Debug.LogError("BALANCE_ERROR "+e.Message);}
   Application.targetFrameRate=120;QualitySettings.vSyncCount=0;Time.fixedDeltaTime=.01f;Time.maximumDeltaTime=.1f;
   var initial=LocalRaceConfig.Load();initial.humans=Mathf.Max(1,initial.humans);ApplyValues(initial);
   Track=new GameObject("Track").AddComponent<TrackBuilder>();BuildTrack();
   for(int i=0;i<4;i++){
    var c=new GameObject("Camera "+(i+1)).AddComponent<Camera>();c.rect=new Rect(i*.5f,0,.5f,1);c.fieldOfView=65;c.nearClipPlane=.2f;c.farClipPlane=1800;
    c.backgroundColor=new Color(.018f,.027f,.06f);c.GetUniversalAdditionalCameraData().renderPostProcessing=true;
    cameras[i]=c.gameObject.AddComponent<ChaseCamera>();
   }
   sun=new GameObject("Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.shadows=LightShadows.Soft;
   spaceFill=new GameObject("Space fill").AddComponent<Light>();spaceFill.type=LightType.Directional;spaceFill.shadows=LightShadows.None;
   RenderSettings.ambientMode=AmbientMode.Flat;ApplyLighting();
   var volume=new GameObject("Color and bloom").AddComponent<Volume>();volume.isGlobal=true;volume.profile=ScriptableObject.CreateInstance<VolumeProfile>();
   var bloom=volume.profile.Add<Bloom>();bloom.intensity.Override(.25f);bloom.threshold.Override(1.1f);volume.profile.Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
   CreateRoster();gameObject.AddComponent<RaceMenu>().director=this;gameObject.AddComponent<RaceHud>().director=this;Restart(false);gameObject.AddComponent<RaceAudioCoordinator>();
  }
  void ApplyLighting(){if(sun!=null){environment.Sky.Activate();RaceLightingController.Apply(environment.Plan.lighting,sun,spaceFill,cameras);}}
  void BuildTrack(){
   string theme=TrackThemePreference=="random"?(TrackSeed%2==0?"cloud-city":"space-station"):TrackThemePreference;
   var definition=Procedural.Generator.Generate(TrackSeed,TrackRailMode,theme,TrackJumps,true);
   environment.Clear();Track.Build(new TrackRoute(definition));environment.Build(Track.Route,Track.transform,TrackSeed);ApplyLighting();
   TrackSummary=$"SEED {TrackSeed} · {theme} · {definition.totalLength:0} м";
  }
  void CreateRoster(){
   if(Cars!=null)foreach(var car in Cars){car.gameObject.SetActive(false);Destroy(car.gameObject);}
   Roster=new RaceRoster(EntrantCount,HumanCount,TrackSeed);Cars=new MagneticVehicle[EntrantCount];Drivers=new AiDriver[EntrantCount];observations=new RaceObservation[EntrantCount];World=new AiWorldSnapshot(EntrantCount);
   decisionTimers=new float[EntrantCount];lastDecisionTimes=new double[EntrantCount];decisionRevisions=new int[EntrantCount];botCommands=new DrivingInput[EntrantCount];
   contactObservations=new DrivingContactObservation[EntrantCount];contactPolicy=new DrivingContactPolicy();
   for(int i=0;i<EntrantCount;i++){
    var e=Roster.Entrants[i];Cars[i]=new GameObject(e.Name).AddComponent<MagneticVehicle>();Cars[i].Initialize(Track,i,EntrantColor(i),Balance,e.HumanSeat>=0);
    int index=i;Cars[i].ContactObserved+=collision=>ObserveContact(index,collision);
    if(Balance!=null){Cars[i].Drive.ConfigureHandicap(e.Profile,Handicap);if(e.HumanSeat<0)Drivers[i]=new AiDriver(i,e.Profile,e.Seed,Balance);}
   }
   Session=new RaceSession(Track.Route.StartDistance,Track.Route.FinishDistance,EntrantCount);
   for(int i=0;i<4;i++){cameras[i].target=i<HumanCount?Cars[i]:null;cameras[i].enabled=i<HumanCount;cameras[i].GetComponent<Camera>().enabled=Started&&i<HumanCount;if(i<HumanCount)cameras[i].GetComponent<Camera>().rect=RaceViewports.For(HumanCount,i);}
  }
  void ApplyValues(LocalRaceConfig config){
   AppliedConfig=config.Copy();EntrantCount=config.entrants;HumanCount=config.humans;Handicap=config.handicap;
   TrackSeed=config.Seed;TrackThemePreference=config.theme;TrackRailMode=config.rails;TrackJumps=config.jumps;
  }
  public bool TryStartSelected(LocalRaceConfig config,out string error){
   error="";if(Started){error="Гонка уже идёт";return false;}
   if(Balance==null){error=BalanceError;return false;}
   var input=new LocalInputRouter();if(!input.TryBind(config,out error))return false;
   GameObject root=null;TrackEnvironmentBuilder decor=null;
   try{
    var snapshot=config.Copy();int count=snapshot.entrants,humans=snapshot.humans,generation=RaceGeneration+1;
    string theme=snapshot.theme=="random"?(snapshot.Seed%2==0?"cloud-city":"space-station"):snapshot.theme;
    var route=new TrackRoute(Procedural.Generator.Generate(snapshot.Seed,snapshot.rails,theme,snapshot.jumps,true));
    var roster=new RaceRoster(count,humans,snapshot.Seed);roster.Shuffle(unchecked(snapshot.Seed+(uint)generation*7919));
    var distances=new float[count];var lanes=new float[count];
    for(int i=0;i<count;i++){distances[i]=RaceRoster.GridDistance(route,roster.Entrants[i].GridSlot);lanes[i]=RaceRoster.GridLateral(route,roster.Entrants[i].GridSlot);}
    // Build the road inventory synchronously while active (its contract requires active colliders).
    // No frame/physics step occurs before the staging root is disabled and published.
    root=new GameObject("Prepared race");
    var trackObject=new GameObject("Track");trackObject.transform.SetParent(root.transform);var track=trackObject.AddComponent<TrackBuilder>();track.Build(route);
    decor=new TrackEnvironmentBuilder();decor.Build(route,track.transform,snapshot.Seed);decor.Root.SetParent(root.transform,true);root.SetActive(false);
    var cars=new MagneticVehicle[count];var drivers=new AiDriver[count];var seen=new RaceObservation[count];
    for(int i=0;i<count;i++){
     var entrant=roster.Entrants[i];var go=new GameObject(entrant.Name);go.transform.SetParent(root.transform);
     cars[i]=go.AddComponent<MagneticVehicle>();cars[i].Initialize(track,i,i<humans?CloudlineSkin.PlayerColors[i]:Color.HSVToRGB((i*.618034f)%1,.55f,.9f),Balance,entrant.HumanSeat>=0);
     cars[i].Drive.ConfigureHandicap(entrant.Profile,snapshot.handicap);cars[i].SetDrivingGeneration(generation);cars[i].ResetAt(distances[i],lanes[i]);cars[i].Hold(true);
     if(entrant.HumanSeat<0)drivers[i]=new AiDriver(i,entrant.Profile,entrant.Seed,Balance);
     int index=i;cars[i].ContactObserved+=collision=>ObserveContact(index,collision);seen[i]=new RaceObservation(distances[i],cars[i].PositionRevision,true);
    }
    var session=new RaceSession(route.StartDistance,route.FinishDistance,count);session.Reset(seen);
    var timers=new float[count];var revisions=new int[count];for(int i=0;i<count;i++){timers[i]=i<humans?0:AiDecisionInterval*(i-humans)/Mathf.Max(1,count-humans);revisions[i]=cars[i].PositionRevision;}
    var world=new AiWorldSnapshot(count);var contacts=new DrivingContactObservation[count];var policy=new DrivingContactPolicy();
    var times=new double[count];var commands=new DrivingInput[count];
    var oldTrack=Track;var oldCars=Cars;var oldDecor=environment;var oldRoot=activeRaceRoot;
    // Publish only after generation, bindings, placement and resource construction succeeded.
    ApplyValues(snapshot);Track=track;Cars=cars;Drivers=drivers;Roster=roster;Session=session;World=world;Input=input;environment=decor;
    observations=seen;decisionTimers=timers;decisionRevisions=revisions;lastDecisionTimes=times;botCommands=commands;contactObservations=contacts;contactPolicy=policy;
    RaceGeneration=generation;activeRaceRoot=root;root=null;decor=null;
    oldTrack.gameObject.SetActive(false);foreach(var car in oldCars)car.gameObject.SetActive(false);
    activeRaceRoot.SetActive(true);Physics.SyncTransforms();
    for(int i=0;i<count;i++){cars[i].ResetAt(distances[i],lanes[i]);cars[i].Hold(true);observations[i]=Observation(i);decisionRevisions[i]=cars[i].PositionRevision;}
    Session.Reset(observations);Session.Begin();Input.PrepareStart();Started=true;Paused=false;Time.timeScale=1;
    pendingRecoveryEvents.Clear();vehicleRecoveryEvents.Clear();
    for(int i=0;i<4;i++){cameras[i].target=i<humans?cars[i]:null;cameras[i].enabled=i<humans;cameras[i].GetComponent<Camera>().enabled=i<humans;if(i<humans){cameras[i].GetComponent<Camera>().rect=RaceViewports.For(humans,i);cameras[i].Snap();}}
    ApplyLighting();TrackSummary=$"SEED {TrackSeed} · {theme} · {route.Definition.totalLength:0} м";
    oldDecor.Clear();if(oldRoot!=null)Destroy(oldRoot);else{Destroy(oldTrack.gameObject);foreach(var car in oldCars)Destroy(car.gameObject);}
    StartError="";return true;
   }catch(Exception e){if(root!=null)root.SetActive(false);if(decor!=null)decor.Clear();if(root!=null)Destroy(root);error="Не удалось подготовить гонку. Измените параметры трассы.";StartError=error;Debug.LogException(e);return false;}
  }
  public RaceObservation Observation(int entrant){var car=Cars[entrant];return new RaceObservation(car.Distance,car.PositionRevision,Vector3.Distance(car.Body.position,car.Frame.position)<car.Frame.halfWidth+3f);}
  public void Restart(bool start=true){
   Track.TireMarks?.Clear();
   GetComponent<RaceMenu>()?.ResetPauseSettings();
   if(Balance==null)start=false;RaceGeneration++;pendingRecoveryEvents.Clear();vehicleRecoveryEvents.Clear();contactPolicy.Reset();
   Started=start;Paused=!start;Time.timeScale=start?1:0;Input=Input??new LocalInputRouter();Input.Block();
   Roster.Shuffle(unchecked(TrackSeed+(uint)RaceGeneration*7919));
   for(int i=0;i<Cars.Length;i++){
    Cars[i].SetDrivingGeneration(RaceGeneration);int slot=Roster.Entrants[i].GridSlot;
    Cars[i].ResetAt(RaceRoster.GridDistance(Track.Route,slot),RaceRoster.GridLateral(Track.Route,slot));Cars[i].Hold(true);Drivers[i]?.Reset();observations[i]=Observation(i);
    decisionTimers[i]=i<HumanCount?0:AiDecisionInterval*(i-HumanCount)/Mathf.Max(1,Cars.Length-HumanCount);decisionRevisions[i]=Cars[i].PositionRevision;lastDecisionTimes[i]=0;botCommands[i]=default;
   }
   Session.Reset(observations);for(int i=0;i<4;i++){cameras[i].GetComponent<Camera>().enabled=start&&i<HumanCount;if(i<HumanCount)cameras[i].Snap();}
   if(start){Session.Begin();Input.PrepareStart();}
  }
  public void SetPaused(bool paused){Track.TireMarks?.BreakAll();if(!paused)GetComponent<RaceMenu>()?.ResetPauseSettings();Paused=paused;Time.timeScale=paused?0:1;Input.Block();Session.Paused=paused;}
  public bool ExitToMenu(){if(!Started||(!Paused&&Session.Phase!=RacePhase.Results))return false;Restart(false);GetComponent<RaceMenu>()?.Open(RaceMenuScreen.LocalSetup);return true;}
  public void StartRace(){
   if(Balance==null)return;
   if(!Started){GetComponent<RaceMenu>()?.StartSelected();return;}
   if(Session.Phase==RacePhase.Results){Restart();return;}if(Paused&&!Input.MissingDevice)SetPaused(false);
  }
  void OnApplicationFocus(bool focus){if(!focus&&Input!=null&&Started)SetPaused(true);}
  void OnApplicationPause(bool paused){if(paused&&Input!=null&&Started)SetPaused(true);}
  void Update(){
   if(Input==null)return;Input.Refresh();var k=Keyboard.current;
   if(!Started)return;
   if(Input.MissingDevice&&!Paused)SetPaused(true);
   bool submenuBack=Input.PausePressed||(k!=null&&k.escapeKey.wasPressedThisFrame);
   foreach(var pad in Gamepad.all)submenuBack|=pad.buttonEast.wasPressedThisFrame;
   if(GetComponent<RaceMenu>().ConsumePauseSettingsBack(submenuBack))return;
   if(Input.PausePressed||(k!=null&&k.escapeKey.wasPressedThisFrame)){if(!Paused)SetPaused(true);else if(!Input.MissingDevice)SetPaused(false);}
   if(k!=null){if(k.f5Key.wasPressedThisFrame&&!Input.MissingDevice)Restart();if(k.backspaceKey.wasPressedThisFrame)ExitToMenu();}
   for(int i=0;i<HumanCount;i++){
    var command=Input.Read(i);bool driving=Session.CanDrive(i)&&!Paused;
    Cars[i].SetInput(driving?command:default);
    Cars[i].SetPresentationInput(command.throttle,!Paused&&(Session.Phase==RacePhase.Countdown||Session.CanDrive(i)));
   }
  }
  void FixedUpdate(){
   if(Paused||Cars==null)return;
   for(int i=0;i<Cars.Length;i++){Cars[i].PrepareProjection();observations[i]=Observation(i);}Session.Tick(Time.fixedDeltaTime,observations);World.Capture(Cars,Track.Route,Session.Elapsed);
   for(int i=0;i<Cars.Length;i++){
    var car=Cars[i];car.SetRaceContext(Balance!=null&&Session.CanDrive(i),Session.Racers[i].Place,Cars.Length,Session.Elapsed);
    if(Session.Racers[i].Finished){if(!car.FinishedCoasting){car.BeginFinishCoast();if(i<HumanCount)cameras[i].LockAtFinish();}}else car.Hold(!Session.CanDrive(i));
    if(Drivers[i]!=null){
     if(Session.CanDrive(i)){
      decisionTimers[i]+=Time.fixedDeltaTime;
      if(decisionTimers[i]>=AiDecisionInterval || decisionRevisions[i]!=car.PositionRevision){
       botCommands[i]=Drivers[i].Step(World,Track.Route,Mathf.Max(Time.fixedDeltaTime,(float)(Session.Elapsed-lastDecisionTimes[i])));
       lastDecisionTimes[i]=Session.Elapsed;
       decisionTimers[i]=0;decisionRevisions[i]=car.PositionRevision;
      }
      car.SetInput(botCommands[i]);
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
     Suppressed=!Session.CanDrive(i)||car.IsGhosting||car.FinishedCoasting||car.ActiveJumpId.Length>0||car.OffRouteSeconds>.05f||selected<0||error>VehicleGeometry.HalfWidth};
   }
   contactPolicy.BeginStep(contactObservations);
  }
  void ObserveContact(int index,Collision collision) {
   if(Paused||Session==null||contactPolicy==null)return;
   var car=Cars[index];var snapshot=contactObservations[index];
   if(snapshot.Generation!=RaceGeneration||snapshot.Revision!=car.PositionRevision||car.IsGhosting||car.FinishedCoasting||car.ActiveJumpId.Length>0||car.OffRouteSeconds>.05f)return;
   var other=collision.rigidbody==null?null:collision.rigidbody.GetComponent<MagneticVehicle>();
   DrivingContactEvent contact;
   if(other!=null){
    int otherIndex=System.Array.IndexOf(Cars,other);if(otherIndex<0||contactObservations[otherIndex].Revision!=other.PositionRevision||other.IsGhosting||other.FinishedCoasting||other.ActiveJumpId.Length>0||other.OffRouteSeconds>.05f)return;
    if(contactPolicy.Vehicle(snapshot.Id,Roster.Entrants[otherIndex].Id,out contact))DrivingContact?.Invoke(contact);
   }else{
    string label=collision.gameObject.name.ToLowerInvariant();if(!label.Contains("rail")&&!label.Contains("barrier"))return;
    int side=collision.contactCount==0?0:(Vector3.Dot(collision.GetContact(0).point-car.Body.position,snapshot.Right)<0?-1:1);
    if(contactPolicy.Barrier(snapshot.Id,side,out contact))DrivingContact?.Invoke(contact);
   }
  }
  void OnDestroy(){Time.timeScale=1;environment.Clear();}
 }
}
