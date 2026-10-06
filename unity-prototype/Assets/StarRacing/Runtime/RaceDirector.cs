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
  public bool Handicap {get;private set;}
  public uint TrackSeed {get;private set;}=77;
  public string TrackThemePreference {get;private set;}="random";
  public string TrackRailMode {get;private set;}="normal";
  public bool TrackJumps {get;private set;}
  public string TrackSummary {get;private set;}="";
  public float RaceTime=>(float)(Session?.Elapsed??0);
  public Color[] Colors={new Color(.15f,.85f,1),new Color(1,.42f,.12f)};
  public event Action<DrivingContactEvent> DrivingContact;
  readonly ChaseCamera[] cameras=new ChaseCamera[2];
  readonly TrackEnvironmentBuilder environment=new TrackEnvironmentBuilder();
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
  bool freshTrackOnStart;
  public int AudioHumanSeat(int entrant)=>Roster!=null&&entrant>=0&&entrant<Roster.Entrants.Length?Roster.Entrants[entrant].HumanSeat:-1;
  public ChaseCamera CameraFor(int seat)=>cameras[seat];
  public Color EntrantColor(int i)=>i<2?Colors[i]:Color.HSVToRGB((i*.618034f)%1,.55f,.9f);
  public void ConsumeRecoveryEvents(List<RecoveryEvent> destination){destination.AddRange(pendingRecoveryEvents);pendingRecoveryEvents.Clear();}
  void Awake(){
   try {Balance=ReleaseBalance.Parse(Resources.Load<TextAsset>("balance-config")?.text);}catch(Exception e){BalanceError=e.Message;Debug.LogError("BALANCE_ERROR "+e.Message);}
   Application.targetFrameRate=120;QualitySettings.vSyncCount=0;Time.fixedDeltaTime=.01f;Time.maximumDeltaTime=.1f;
   EntrantCount=Mathf.Clamp(PlayerPrefs.GetInt("StarRacing.Entrants",8),2,64);
   Handicap=PlayerPrefs.GetInt("StarRacing.AiHandicap",0)!=0;
   TrackThemePreference=PlayerPrefs.GetString("StarRacing.TrackTheme","random");
   if(TrackThemePreference!="cloud-city"&&TrackThemePreference!="space-station")TrackThemePreference="random";
   TrackRailMode=PlayerPrefs.GetString("StarRacing.TrackRails","normal");if(TrackRailMode!="full"&&TrackRailMode!="none")TrackRailMode="normal";
   TrackJumps=PlayerPrefs.GetInt("StarRacing.TrackJumps",0)!=0;
   Track=new GameObject("Track").AddComponent<TrackBuilder>();BuildTrack();
   for(int i=0;i<2;i++){
    var c=new GameObject("Camera "+(i+1)).AddComponent<Camera>();c.rect=new Rect(i*.5f,0,.5f,1);c.fieldOfView=65;c.nearClipPlane=.2f;c.farClipPlane=1800;
    c.backgroundColor=new Color(.018f,.027f,.06f);c.GetUniversalAdditionalCameraData().renderPostProcessing=true;
    cameras[i]=c.gameObject.AddComponent<ChaseCamera>();
   }
   sun=new GameObject("Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.shadows=LightShadows.Soft;
   spaceFill=new GameObject("Space fill").AddComponent<Light>();spaceFill.type=LightType.Directional;spaceFill.shadows=LightShadows.None;
   RenderSettings.ambientMode=AmbientMode.Flat;ApplyLighting();
   var volume=new GameObject("Color and bloom").AddComponent<Volume>();volume.isGlobal=true;volume.profile=ScriptableObject.CreateInstance<VolumeProfile>();
   var bloom=volume.profile.Add<Bloom>();bloom.intensity.Override(.25f);bloom.threshold.Override(1.1f);volume.profile.Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
   CreateRoster();gameObject.AddComponent<RaceHud>().director=this;Restart(false);gameObject.AddComponent<RaceAudioCoordinator>();
  }
  void ApplyLighting(){if(sun!=null)RaceLightingController.Apply(environment.Plan.lighting,sun,spaceFill,cameras);}
  void BuildTrack(){
   string theme=TrackThemePreference=="random"?(TrackSeed%2==0?"cloud-city":"space-station"):TrackThemePreference;
   var definition=Procedural.Generator.Generate(TrackSeed,TrackRailMode,theme,TrackJumps,true);
   environment.Clear();Track.Build(new TrackRoute(definition));environment.Build(Track.Route,Track.transform);ApplyLighting();
   TrackSummary=$"SEED {TrackSeed} · {theme} · {definition.totalLength:0} м";
  }
  void CreateRoster(){
   if(Cars!=null)foreach(var car in Cars){car.gameObject.SetActive(false);Destroy(car.gameObject);}
   Roster=new RaceRoster(EntrantCount,2,TrackSeed);Cars=new MagneticVehicle[EntrantCount];Drivers=new AiDriver[EntrantCount];observations=new RaceObservation[EntrantCount];World=new AiWorldSnapshot(EntrantCount);
   decisionTimers=new float[EntrantCount];lastDecisionTimes=new double[EntrantCount];decisionRevisions=new int[EntrantCount];botCommands=new DrivingInput[EntrantCount];
   contactObservations=new DrivingContactObservation[EntrantCount];contactPolicy=new DrivingContactPolicy();
   for(int i=0;i<EntrantCount;i++){
    var e=Roster.Entrants[i];Cars[i]=new GameObject(e.Name).AddComponent<MagneticVehicle>();Cars[i].Initialize(Track,i,EntrantColor(i),Balance,e.HumanSeat>=0);
    int index=i;Cars[i].ContactObserved+=collision=>ObserveContact(index,collision);
    if(Balance!=null){Cars[i].Drive.ConfigureHandicap(e.Profile,Handicap);if(e.HumanSeat<0)Drivers[i]=new AiDriver(i,e.Profile,e.Seed,Balance);}
   }
   Session=new RaceSession(Track.Route.StartDistance,Track.Route.FinishDistance,EntrantCount);
   for(int i=0;i<2;i++)cameras[i].target=Cars[i];
  }
  public void ConfigureRoster(int count,bool handicap){
   if(Started)return;EntrantCount=Mathf.Clamp(count,2,64);Handicap=handicap;CreateRoster();Restart(false);
   PlayerPrefs.SetInt("StarRacing.Entrants",EntrantCount);PlayerPrefs.SetInt("StarRacing.AiHandicap",Handicap?1:0);PlayerPrefs.Save();
  }
  public void ConfigureTrack(uint seed,string theme,string rails,bool jumps){
   if(Started||!(theme=="random"||theme=="cloud-city"||theme=="space-station")||!(rails=="normal"||rails=="full"||rails=="none"))return;
   TrackSeed=seed;TrackThemePreference=theme;TrackRailMode=rails;TrackJumps=jumps;BuildTrack();CreateRoster();Restart(false);
   PlayerPrefs.SetString("StarRacing.TrackTheme",theme);PlayerPrefs.SetString("StarRacing.TrackRails",rails);PlayerPrefs.SetInt("StarRacing.TrackJumps",jumps?1:0);PlayerPrefs.Save();
   GetComponent<RaceHud>()?.RefreshTrackSettings();
  }
  public void NewTrack(){TrackSeed=unchecked(TrackSeed+1);BuildTrack();CreateRoster();Restart(false);GetComponent<RaceHud>()?.RefreshTrackSettings();}
  public RaceObservation Observation(int entrant){var car=Cars[entrant];return new RaceObservation(car.Distance,car.PositionRevision,Vector3.Distance(car.Body.position,car.Frame.position)<car.Frame.halfWidth+3f);}
  public void Restart(bool start=true){
   if(Balance==null)start=false;RaceGeneration++;pendingRecoveryEvents.Clear();vehicleRecoveryEvents.Clear();contactPolicy.Reset();
   Started=start;Paused=!start;Time.timeScale=start?1:0;Input=Input??new LocalInputRouter();Input.Block();
   Roster.Shuffle(unchecked(TrackSeed+(uint)RaceGeneration*7919));
   for(int i=0;i<Cars.Length;i++){
    Cars[i].SetDrivingGeneration(RaceGeneration);int slot=Roster.Entrants[i].GridSlot;
    Cars[i].ResetAt(RaceRoster.GridDistance(Track.Route,slot),RaceRoster.GridLateral(Track.Route,slot));Cars[i].Hold(true);Drivers[i]?.Reset();observations[i]=Observation(i);
    decisionTimers[i]=i<2?0:AiDecisionInterval*(i-2)/Mathf.Max(1,Cars.Length-2);decisionRevisions[i]=Cars[i].PositionRevision;lastDecisionTimes[i]=0;botCommands[i]=default;
   }
   Session.Reset(observations);for(int i=0;i<2;i++)cameras[i].Snap();
   if(start){Session.Begin();Input.PrepareStart();}freshTrackOnStart=false;
  }
  public void SetPaused(bool paused){Paused=paused;Time.timeScale=paused?0:1;Input.Block();Session.Paused=paused;}
  public bool ExitToMenu(){if(!Started||(!Paused&&Session.Phase!=RacePhase.Results))return false;Restart(false);freshTrackOnStart=true;GetComponent<RaceHud>()?.RefreshTrackSettings();return true;}
  public void StartRace(){
   if(Balance==null)return;
   if(!Started){if(freshTrackOnStart)NewTrack();Restart();return;}
   if(Session.Phase==RacePhase.Results){Restart();return;}if(Paused)SetPaused(false);
  }
  void OnApplicationFocus(bool focus){if(!focus&&Input!=null&&Started)SetPaused(true);}
  void OnApplicationPause(bool paused){if(paused&&Input!=null&&Started)SetPaused(true);}
  void Update(){
   if(Input==null)return;Input.Refresh();var k=Keyboard.current;
   if(k!=null&&!GetComponent<RaceHud>().UpdateDialogOpen){if(k.escapeKey.wasPressedThisFrame&&Started)SetPaused(!Paused);if(k.enterKey.wasPressedThisFrame&&!GetComponent<RaceHud>().EditingTrackSeed)StartRace();if(k.f5Key.wasPressedThisFrame)Restart();if(k.backspaceKey.wasPressedThisFrame)ExitToMenu();}
   for(int i=0;i<2;i++)Cars[i].SetInput(Session.CanDrive(i)&&!Paused?Input.Read(i):default);
  }
  void FixedUpdate(){
   if(Paused||Cars==null)return;
   for(int i=0;i<Cars.Length;i++){Cars[i].PrepareProjection();observations[i]=Observation(i);}Session.Tick(Time.fixedDeltaTime,observations);World.Capture(Cars,Track.Route,Session.Elapsed);
   for(int i=0;i<Cars.Length;i++){
    var car=Cars[i];car.SetRaceContext(Balance!=null&&Session.CanDrive(i),Session.Racers[i].Place,Cars.Length,Session.Elapsed);
    if(Session.Racers[i].Finished){if(!car.FinishedCoasting){car.BeginFinishCoast();if(i<2)cameras[i].LockAtFinish();}}else car.Hold(!Session.CanDrive(i));
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
