using System;
using UnityEngine;
namespace StarRacingPrototype {
 public struct AiObservation {
  public int Id,Revision; public float Distance,Speed,Lateral,LateralSpeed,Charge; public bool Grounded,Recovering,Finished;
  public Vector3 Position,Forward,Up; public float YawRate;
 }
 // Populated once by the coordinator, then read by every driver before forces are applied.
 public sealed class AiWorldSnapshot {
  readonly AiObservation[] cars;
  public int Count=>cars.Length;public AiObservation this[int i]=>cars[i];
  public double Time {get;private set;}
  public AiWorldSnapshot(int count){cars=new AiObservation[count];}
  public void Capture(MagneticVehicle[] vehicles,TrackRoute route,double time) {
   Time=time;
   for(int i=0;i<cars.Length;i++){
    var c=vehicles[i];var f=route.Evaluate(c.Distance);
    cars[i]=new AiObservation{Id=i,Revision=c.PositionRevision,Distance=c.Distance,Speed=Vector3.Dot(c.Body.linearVelocity,c.transform.forward),Lateral=Vector3.Dot(c.Body.position-f.position,f.right),LateralSpeed=Vector3.Dot(c.Body.linearVelocity,f.right),Charge=c.Drive==null?0:c.Drive.Charge,Grounded=c.Telemetry.grounded,Recovering=c.Telemetry.recovering,Finished=c.FinishedCoasting,Position=c.Body.position,Forward=c.transform.forward,Up=c.transform.up,YawRate=Vector3.Dot(c.Body.angularVelocity,f.normal)};
   }
  }
 }
 public enum AiMode { Pace, Attack, Recovery }
 [Flags] public enum AiNitroHazard { None=0, Speed=1, Heading=2, Unsupported=4, Recovery=8, Traffic=16, Corridor=32 }
 public enum AiSpeedLimitSource { CommonLimit, Geometry, Traffic, Following, Heading, Corridor }
 public struct AiNitroEvent {
  public double Time,Planned,Actual; public int Plan; public string Kind,Reason;
  public float Speed,Desired,Heading; public AiNitroHazard Hazards;
  public AiSpeedLimitSource LimitSource;public int LimitingEntrant;
 }
 public sealed class AiDriver {
  public AiMode Mode {get;private set;}
  public int Target {get;private set;}=-1;
  public float Lane {get;private set;}
  public float DesiredSpeed {get;private set;}
  public bool TrafficBlocked {get;private set;}
  public string NitroReason {get;private set;}="start-slot";
  public double LastFullDecision {get;private set;}
  public Action<AiNitroEvent> NitroEvents;
  public bool NitroCommand {get;private set;}
  public AiNitroHazard NitroHazards {get;private set;}
  public float HeadingError {get;private set;}
  public float HeadingRate=>headingRate;
  public static float CrestSpeedLimit(float curvature,float inwardGravity,float commonLimit){
   if(curvature<=0)return commonLimit;
   // Once all wheels leave a convex crest, only gravity can pull the car back.
   // Inverted sections keep the existing magnetic envelope until flight is measured there.
   // Leave measured suspension/contact reserve: the first Space15838 crest lost all
   // wheel contacts at 40 m/s even with the gravity-only theoretical envelope.
   float budget=inwardGravity>0?Mathf.Min(14f,inwardGravity*.7f):14f;
   return Mathf.Min(commonLimit,Mathf.Sqrt(budget/Mathf.Max(.00001f,curvature)));
  }
  public static float VerticalSpeedLimit(float signedChange,float curvature,float inwardGravity,float commonLimit,bool continuousLoop){
   // The continuous loop has its own measured inward support envelope in MagneticVehicle.
   // Ordinary crests and jump lips still need the contact-loss reserve above.
   return signedChange<0 && !continuousLoop?CrestSpeedLimit(curvature,inwardGravity,commonLimit):commonLimit;
  }
  public AiSpeedLimitSource SpeedLimitSource {get;private set;}
  public float GeometryLimitingDistance {get;private set;}
  public int LimitingEntrant {get;private set;}=-1;
  public int BranchIndex {get;private set;}=-1;
  public int BranchLane {get;private set;}=-1;
  public int MergeYieldTo {get;private set;}=-1;
  public bool CorridorBlocked {get;private set;}
  float branchOffset;bool returningToInterior;int partialJumpIndex=-1;float partialBypassLane;int mergeYieldBranch=-1,mergeYieldRevision=-1;
  double boostStarted,lastHeadingAt;float boostTargetOffset,boostLaneRate,lastHeading,headingRate;int boostBranch,boostBranchLane;
  public int Attacks {get;private set;} public int Overtakes {get;private set;} public int Plans {get;private set;}
  readonly float boostLimit,brake,accelEnvelope;
  readonly ReleaseBalance balance;readonly DriverProfile profile;readonly int id;readonly float personality;
  float stalled,reverseStartDistance;double reverseUntil;
  public bool ReverseActive {get;private set;}
  public string ReverseReason {get;private set;}="none";
  readonly uint decisionSeed;uint random;double recoveryUntil,nextError,errorUntil,boostUntil,nextBoost;int revision=-1,targetRevision;float errorLane;bool boost;
  public AiDriver(int id,DriverProfile profile,uint seed,ReleaseBalance balance){boostLimit=balance["nitroMaxSpeedKmh"]/3.6f;brake=balance["brakeDeceleration"];accelEnvelope=balance["acceleration"]*(profile==DriverProfile.Ace?balance["aceAccelerationMultiplier"]:profile==DriverProfile.Racer?balance["racerAccelerationMultiplier"]:1)+balance["nitroAcceleration"];this.id=id;this.profile=profile;this.balance=balance;random=seed;personality=Random01();decisionSeed=random;Reset();}
  float Random01()=>RaceRoster.Next(ref random)/(float)uint.MaxValue;
  public void Reset(){random=decisionSeed;Mode=AiMode.Pace;Target=-1;revision=-1;stalled=0;reverseUntil=0;ReverseActive=false;ReverseReason="none";Lane=0;BranchIndex=BranchLane=-1;MergeYieldTo=mergeYieldBranch=mergeYieldRevision=-1;partialJumpIndex=-1;CorridorBlocked=false;returningToInterior=false;NitroCommand=false;recoveryUntil=0;nextError=8+personality*13;errorUntil=0;boost=false;boostUntil=0;nextBoost=.25+personality*2.5;lastHeadingAt=double.NegativeInfinity;headingRate=0;LastFullDecision=0;Attacks=Overtakes=Plans=0;}
  float SpeedLimit=>balance["baseSpeedKmh"]/3.6f;
  float BoostLimit=>boostLimit;
  float Brake=>brake;
  float AccelEnvelope=>accelEnvelope;
  struct FrameCache {
   float d0,d1,d2,d3;TrackFrame f0,f1,f2,f3;int count,next;
   float l0,l1,l2,l3;bool hasL0,hasL1,hasL2,hasL3;
   public TrackFrame Get(TrackRoute route,float distance){
    if(count>0&&distance==d0)return f0;
    if(count>1&&distance==d1)return f1;
    if(count>2&&distance==d2)return f2;
    if(count>3&&distance==d3)return f3;
    TrackFrame frame=route.Evaluate(distance);
    switch(next){case 0:d0=distance;f0=frame;hasL0=false;break;case 1:d1=distance;f1=frame;hasL1=false;break;case 2:d2=distance;f2=frame;hasL2=false;break;default:d3=distance;f3=frame;hasL3=false;break;}
    next=(next+1)&3;if(count<4)count++;
    return frame;
   }
   public bool TryLateral(float distance,out float lateral){
    if(count>0&&distance==d0&&hasL0){lateral=l0;return true;}
    if(count>1&&distance==d1&&hasL1){lateral=l1;return true;}
    if(count>2&&distance==d2&&hasL2){lateral=l2;return true;}
    if(count>3&&distance==d3&&hasL3){lateral=l3;return true;}
    lateral=0;return false;
   }
   public void StoreLateral(float distance,float lateral){
    if(count>0&&distance==d0){l0=lateral;hasL0=true;}
    else if(count>1&&distance==d1){l1=lateral;hasL1=true;}
    else if(count>2&&distance==d2){l2=lateral;hasL2=true;}
    else if(count>3&&distance==d3){l3=lateral;hasL3=true;}
   }
  }
  readonly struct PathIntent {
   public readonly float Start,Lateral,Target,Rate,Speed,Acceleration,InitialLocal,Duration;
   public readonly int Branch,BranchLane;
   public PathIntent(float start,float lateral,float initialLocal,float target,float rate,float speed,float acceleration,int branch,int branchLane){Start=start;Lateral=lateral;InitialLocal=initialLocal;Target=target;Rate=rate;Speed=speed;Acceleration=acceleration;Branch=branch;BranchLane=branchLane;Duration=1.5f*Mathf.Abs(target-initialLocal)/Mathf.Max(.01f,rate);}
  }
  float PathLateral(TrackRoute route,float distance,PathIntent intent){
   float travel=Mathf.Max(0,distance-intent.Start);
   if(travel<=.001f)return intent.Lateral;
   float speed=Mathf.Max(1,intent.Speed),acceleration=Mathf.Max(0,intent.Acceleration);
   float time=acceleration>.001f?2*travel/(speed+Mathf.Sqrt(speed*speed+2*acceleration*travel)):travel/speed;
   // The command's MoveTowards corners are not physical steering impulses. A
   // zero-slope start/stop models the car's continuous lateral motion while
   // keeping the same peak lane-change rate as the command.
   float local=intent.Duration>.001f?Mathf.SmoothStep(intent.InitialLocal,intent.Target,time/intent.Duration):intent.Target;
   return BranchCenter(route,distance,intent.Branch,intent.BranchLane)+local;
  }
  float CachedPathLateral(TrackRoute route,float distance,PathIntent intent,ref FrameCache cache){
   if(cache.TryLateral(distance,out float value))return value;
   float lateral=PathLateral(route,distance,intent);
   cache.StoreLateral(distance,lateral);
   return lateral;
  }
  public static float LateralPathCurvature(TrackFrame before,TrackFrame current,TrackFrame after,float beforeLane,float currentLane,float afterLane){
   Vector3 p0=before.position+before.right*beforeLane,p1=current.position+current.right*currentLane,p2=after.position+after.right*afterLane;
   Vector3 first=p1-p0,last=p2-p1;float arc=(first.magnitude+last.magnitude)*.5f;
   return arc>.001f?Mathf.Abs(Vector3.Dot(Vector3.Cross(first.normalized,last.normalized),current.normal))/arc:0;
  }
  public static float NormalPathCurvature(TrackFrame before,TrackFrame current,TrackFrame after,float beforeLane,float currentLane,float afterLane){
   Vector3 p0=before.position+before.right*beforeLane,p1=current.position+current.right*currentLane,p2=after.position+after.right*afterLane;
   Vector3 first=p1-p0,last=p2-p1;float arc=(first.magnitude+last.magnitude)*.5f;
   return arc>.001f?Vector3.Dot(last.normalized-first.normalized,current.normal)/arc:0;
  }
  public static float ReverseSteerForHeading(float angle)=>-Mathf.Clamp(angle/18f,-1,1);
  bool ReversePathSafe(AiWorldSnapshot world,TrackRoute route,AiObservation self){
   if(!self.Grounded||self.Recovering)return false;
   float back=Mathf.Max(0,self.Distance-Mathf.Clamp(Mathf.Abs(self.Speed)*.25f+1,2,4));
   float projectedLane=self.Lateral+self.LateralSpeed*.2f;
   if(route.Definition==null){
    if(!route.SupportsCorridorSegment(back,projectedLane,self.Distance,self.Lateral,2.1f))return false;
   }else {
    if(!route.RoadDeficits(self.Distance,self.Lateral,out float left,out float right)||
       !route.RoadDeficits(back,projectedLane,out float nextLeft,out float nextRight)||
       nextLeft>left+.12f||nextRight>right+.12f)return false;
   }
   for(int i=0;i<world.Count;i++){
    if(i==id||world[i].Finished)continue;
    var other=world[i];
    if(other.Distance<=self.Distance+1&&other.Distance>=back-3&&Mathf.Abs(other.Lateral-projectedLane)<2.6f)return false;
   }
   return true;
  }
  static DrivingInput HoldAtBlockedCorridor(float signedSpeed)=>new DrivingInput{
   brake=signedSpeed>.08f?1:0,throttle=signedSpeed<-.08f?1:0
  };
  static float CachedLateralPathCurvature(TrackFrame before,TrackFrame current,TrackFrame after,float beforeLane,float currentLane,float afterLane,out float signedNormalCurve){
   Vector3 p0=before.position+before.right*beforeLane,p1=current.position+current.right*currentLane,p2=after.position+after.right*afterLane;
   Vector3 first=p1-p0,last=p2-p1;
   float firstLength=first.magnitude,lastLength=last.magnitude,arc=(firstLength+lastLength)*.5f;
   if(arc<=.001f){signedNormalCurve=0;return 0;}
   Vector3 firstUnit=firstLength>1e-5f?first/firstLength:Vector3.zero;
   Vector3 lastUnit=lastLength>1e-5f?last/lastLength:Vector3.zero;
   signedNormalCurve=Vector3.Dot(lastUnit-firstUnit,current.normal)/arc;
   return Mathf.Abs(Vector3.Dot(Vector3.Cross(firstUnit,lastUnit),current.normal))/arc;
  }
  public DrivingInput Step(AiWorldSnapshot world,TrackRoute route,float dt,bool allowNitro=true) {
   var self=world[id];double now=world.Time;NitroCommand=false;NitroHazards=AiNitroHazard.None;if(self.Finished||dt<=0){ReverseActive=false;return default;}
   if(revision!=self.Revision){if(revision>=0){Mode=AiMode.Recovery;recoveryUntil=now+1.1+personality;EndBoost(now,self,"position-recovery");Target=-1;}revision=self.Revision;Lane=self.Lateral;BranchIndex=BranchLane=-1;MergeYieldTo=mergeYieldBranch=mergeYieldRevision=-1;partialJumpIndex=-1;returningToInterior=false;ReverseActive=false;reverseUntil=0;lastHeadingAt=double.NegativeInfinity;headingRate=0;}
   float speed=Mathf.Max(0,self.Speed);
   if(returningToInterior){Mode=AiMode.Recovery;recoveryUntil=Math.Max(recoveryUntil,now+.25);Target=-1;}
   if(Mode==AiMode.Recovery&&now>=recoveryUntil)Mode=AiMode.Pace;
   if(Target>=0 && world[Target].Revision!=targetRevision){Target=-1;Mode=AiMode.Recovery;recoveryUntil=now+.5;}
   if(Target>=0 && (world[Target].Finished || self.Distance>world[Target].Distance+4)){if(!world[Target].Finished&&now>=10&&now<=90)Overtakes++;Target=-1;Mode=AiMode.Recovery;recoveryUntil=now+.6+personality;}
   if(Mode!=AiMode.Recovery && (Target<0 || self.Distance<world[Target].Distance || world[Target].Distance<self.Distance-4)){int nearest=-1;float gap=100+balance["aiOvertakeAggression"]*80;for(int i=0;i<world.Count;i++){var other=world[i];float d=other.Distance-self.Distance;if(i!=id&&!other.Finished&&d>0&&d<gap){nearest=i;gap=d;}}if(nearest!=Target&&nearest>=0)Attacks++;Target=nearest;if(Target>=0)targetRevision=world[Target].Revision;Mode=nearest>=0?AiMode.Attack:AiMode.Pace;}
   if(now>=nextError){float frequency=profile==DriverProfile.Rookie?8:profile==DriverProfile.Racer?18:35;nextError=now+frequency+Random01()*frequency;errorUntil=now+(profile==DriverProfile.Rookie?1.4:.55);errorLane=(Random01()-.5f)*(profile==DriverProfile.Rookie?2.8f:1.2f);}
   float look=Mathf.Clamp(speed*.3f,7,25);SelectBranch(route,world,self,look);
   PreviewBranchLane(route,world,self,out int previewBranch,out int previewLane);
   var previewChoice=new PreviewChoice{Branch=previewBranch,Lane=previewLane,World=world,Self=self};
   if(boost&&BranchIndex>=0&&BranchIndex!=boostBranch)EndBoost(now,self,"branch-change");
   float center=BranchCenter(route,self.Distance+look),wish=center+(personality-.5f)*6;
   if(Target>=0){var target=world[Target];float side=self.Lateral<target.Lateral?-1:1;if(Math.Abs(self.Lateral-target.Lateral)<.5f)side=personality<.5f?-1:1;wish=target.Lateral+side*(profile==DriverProfile.Ace?2.05f:2.7f);}
   if(now<errorUntil&&!returningToInterior)wish+=errorLane;
   if(boost)wish=BranchCenter(route,self.Distance+look,boostBranch,boostBranchLane)+boostTargetOffset;
   bool bypass=PartialBypassTarget(route,self.Distance,self.Lateral,speed,out float bypassLane);
   if(bypass)wish=bypassLane;
   // Offsets are native Frame.right; source spans use the opposite sign.
   wish=SafeLane(route,self.Distance+look,wish);
   if(!bypass&&!boost&&Target>=0&&Mathf.Abs(wish-world[Target].Lateral)<2.0f){float alternative=world[Target].Lateral+(wish>=world[Target].Lateral?-2.7f:2.7f);wish=SafeLane(route,self.Distance+look,alternative);}
   bool traffic=false;int trafficEntrant=-1;
   float mergeEnd=0;bool mergeWatch=false;int mergeNeighbor=-1;float mergeClosest=float.PositiveInfinity;
   if(BranchIndex!=mergeYieldBranch){MergeYieldTo=mergeYieldRevision=-1;mergeYieldBranch=BranchIndex;}
   if(BranchIndex>=0){
    var branch=route.Definition.branches[BranchIndex];
    int transition=Math.Min(22,Math.Max(16,(int)Math.Floor((branch.endIndex-branch.startIndex)*.36)));
    mergeEnd=30+branch.endIndex*5;
    float mergeStart=mergeEnd-transition*5;
    float reach=speed*speed/(2*Brake)+speed*.4f+80;
    mergeWatch=self.Distance<mergeEnd+1.75f&&self.Distance+reach>=mergeStart;
   }
   if(!mergeWatch){MergeYieldTo=mergeYieldRevision=-1;}
   for(int i=0;i<world.Count;i++){
    if(i==id||world[i].Finished)continue;var other=world[i];float gap=other.Distance-self.Distance;
    float predictedGap=gap+(other.Speed-speed)*.5f;
    float corridorMin=Mathf.Min(self.Lateral,wish)-1.95f,corridorMax=Mathf.Max(self.Lateral,wish)+1.95f;
    bool crosses=(wish-other.Lateral)*(self.Lateral-other.Lateral)<0;
    bool approaches=Mathf.Abs(wish-other.Lateral)<Mathf.Abs(self.Lateral-other.Lateral)+.05f;
    float closestGap=gap*predictedGap<=0?0:Mathf.Min(Mathf.Abs(gap),Mathf.Abs(predictedGap));
    if(closestGap<4.4f && (crosses||approaches) && other.Lateral>corridorMin && other.Lateral<corridorMax){wish=SafeLane(route,self.Distance+look,self.Lateral);traffic=true;trafficEntrant=i;}
    if(!mergeWatch)continue;
    var mergeBranch=route.Definition.branches[BranchIndex];float mergeStartDistance=30+mergeBranch.startIndex*5;
    if(other.Distance<mergeStartDistance-1.75f||other.Distance>mergeEnd+1.75f||Mathf.Abs(other.Lateral-self.Lateral)<1.2f)continue;
    int otherLane=NearestBranchLane(route,BranchIndex,other.Distance,other.Lateral);
    if(otherLane==BranchLane&&self.Lateral*other.Lateral>=0)continue;
    float mergeTime=look/Mathf.Max(8,speed),mergeGap=gap+(other.Speed-speed)*mergeTime;
    float mergeProximity=gap*mergeGap<=0?0:Mathf.Min(Mathf.Abs(gap),Mathf.Abs(mergeGap));
    if(mergeProximity<Mathf.Max(7,speed*.3f)&&mergeProximity<mergeClosest){mergeClosest=mergeProximity;mergeNeighbor=i;}
    if(MergeYieldTo>=0||!(gap>1.75f||Mathf.Abs(gap)<=1.75f&&id>i))continue;
    float ownArrival=(mergeEnd-self.Distance)/Mathf.Max(8,speed),otherArrival=(mergeEnd-other.Distance)/Mathf.Max(8,other.Speed);
    if(Mathf.Abs(ownArrival-otherArrival)<.75f&&Mathf.Abs(gap)<Mathf.Max(12,speed*1.8f)){
     MergeYieldTo=i;mergeYieldRevision=other.Revision;
    }
   }
   if(MergeYieldTo>=0&&(world[MergeYieldTo].Finished||world[MergeYieldTo].Revision!=mergeYieldRevision))MergeYieldTo=mergeYieldRevision=-1;
   TrafficBlocked=traffic;
   float laneRate=boost?boostLaneRate:Mode==AiMode.Attack?2.5f:1.6f;
   int intentBranch=boost?boostBranch:BranchIndex,intentLane=boost?boostBranchLane:BranchLane;
   float intentCenter=BranchCenter(route,self.Distance+look,intentBranch,intentLane),intentTarget=wish-intentCenter;
   float currentLocal=BranchIndex>=0?branchOffset:Lane;
   Lane=ProjectedLane(route,self.Distance+look,currentLocal,intentTarget,laneRate,dt,intentBranch,intentLane);
   Lane=SafeLane(route,self.Distance+look,Lane);
   bool mergeGuarded=false;
   if(mergeNeighbor>=0){
    var other=world[mergeNeighbor];float time=look/Mathf.Max(8,speed);
    float occupied=other.Lateral+Mathf.Clamp(other.LateralSpeed*time,-2.5f,2.5f);
    float guarded=self.Lateral>other.Lateral?Mathf.Max(Lane,occupied+2.9f):Mathf.Min(Lane,occupied-2.9f);
    guarded=SafeLane(route,self.Distance+look,guarded);
    if(guarded!=Lane){Lane=guarded;mergeGuarded=true;}
   }
   TrafficBlocked|=mergeGuarded||MergeYieldTo>=0;
   if(BranchIndex>=0)branchOffset=Lane-center;
   CorridorBlocked=!HasCorridor(route,self.Distance,self.Lateral,self.Distance+look,Lane);
   returningToInterior=false;
   bool safeTarget=!CorridorBlocked;
   if(!safeTarget){
    float bestGap=float.PositiveInfinity,best=self.Lateral;bool bestStrict=false;
    void ConsiderReturn(float requested){
     float candidate=SafeLane(route,self.Distance+look,requested),gap=Mathf.Abs(candidate-self.Lateral);
     bool strict=HasCorridor(route,self.Distance,self.Lateral,self.Distance+look,candidate);
     bool recovery=!strict&&self.Grounded&&HasCorridor(route,self.Distance,self.Lateral,self.Distance+look,candidate,true);
     if(!strict&&!recovery)return;
     if(safeTarget&&(bestStrict&&!strict||(bestStrict==strict&&gap>=bestGap)))return;
     best=candidate;bestGap=gap;bestStrict=strict;safeTarget=true;returningToInterior=recovery;
    }
    foreach(var span in route.PavedAt(self.Distance+look)){
     float room=Mathf.Max(0,(float)span.halfWidth-2.6f);
     float spanCenter=-(float)span.offset;
     ConsiderReturn(Mathf.Clamp(self.Lateral,spanCenter-room,spanCenter+room));
     // The nearest point can cross a narrowing branch gap even when a farther
     // point in the committed branch has a connected path from the car.
     if(BranchIndex>=0){ConsiderReturn(spanCenter-room);ConsiderReturn(spanCenter+room);}
    }
    if(BranchIndex>=0){
     float branchCenter=BranchCenter(route,self.Distance+look);
     ConsiderReturn(branchCenter);
    }
    Lane=best;if(BranchIndex>=0)branchOffset=Lane-center;
    if(returningToInterior){Mode=AiMode.Recovery;recoveryUntil=Math.Max(recoveryUntil,now+.25);Target=-1;}
    // A rejected request must not keep the emergency limit after a strict safe return.
    // Recovery-only returns remain blocked until the full footprint fits the road.
    CorridorBlocked=!HasCorridor(route,self.Distance,self.Lateral,self.Distance+look,Lane);
   }
   var frame=route.Evaluate(self.Distance);var targetFrame=route.Evaluate(self.Distance+look);
   Vector3 direction=Vector3.ProjectOnPlane(targetFrame.position+targetFrame.right*Lane-self.Position,frame.normal).normalized;
   float angle=Vector3.SignedAngle(self.Forward,direction,frame.normal);
   HeadingError=angle;
   double headingDt=now-lastHeadingAt;
   headingRate=headingDt>0&&headingDt<=.2?Mathf.DeltaAngle(lastHeading,angle)/(float)headingDt:0;
   lastHeading=angle;lastHeadingAt=now;
   float steer=Mathf.Clamp(angle/18f,-1,1);
   if(ReverseActive){
    bool aligned=Mathf.Abs(angle)<5&&safeTarget;
    bool pathSafe=ReversePathSafe(world,route,self);
    string stop=now>=reverseUntil?"reverse-time-limit":self.Distance<reverseStartDistance-8?"reverse-distance-limit":
     !pathSafe?"reverse-path-blocked":aligned?"reverse-aligned":null;
    if(stop==null){Mode=AiMode.Recovery;Target=-1;EndBoost(now,self,"reverse-recovery");NitroReason="reverse-recovery";return new DrivingInput{brake=1,steer=ReverseSteerForHeading(angle)};}
    ReverseActive=false;reverseUntil=0;ReverseReason=stop;recoveryUntil=Math.Max(recoveryUntil,now+.6);stalled=0;
    Mode=AiMode.Recovery;Target=-1;EndBoost(now,self,stop);NitroReason=stop;
    if(self.Speed<-.08f)return new DrivingInput{throttle=1,steer=safeTarget?steer:0};
   }
   stalled=self.Grounded&&self.Speed>=-.08f&&speed<3?stalled+dt:0;
   if(stalled>2.2f){
    stalled=0;
    if(ReversePathSafe(world,route,self)){
     reverseUntil=now+1.5;reverseStartDistance=self.Distance;ReverseActive=true;ReverseReason="stalled-recovery";
     recoveryUntil=Math.Max(recoveryUntil,reverseUntil+.6);Mode=AiMode.Recovery;Target=-1;
     EndBoost(now,self,"stalled-recovery");NitroReason="stalled-recovery";
     return new DrivingInput{brake=1,steer=ReverseSteerForHeading(angle)};
    }
   }
   if(!safeTarget){NitroHazards=AiNitroHazard.Corridor;NitroReason="no-connected-corridor";SpeedLimitSource=AiSpeedLimitSource.Corridor;LimitingEntrant=-1;DesiredSpeed=0;EndBoost(now,self,"corridor-blocked");return HoldAtBlockedCorridor(self.Speed);}
   // Reachability envelope, including braking before distant hazards. No profile speed cap.
   float desired=BoostLimit,horizon=BoostLimit*BoostLimit/(2*Brake)+BoostLimit*.4f+80;
   var pathIntent=new PathIntent(self.Distance,self.Lateral,self.Lateral-BranchCenter(route,self.Distance,intentBranch,intentLane),intentTarget,laneRate,speed,0,intentBranch,intentLane);
   FrameCache geometryCache=default;
   SpeedLimitSource=AiSpeedLimitSource.CommonLimit;LimitingEntrant=-1;GeometryLimitingDistance=-1;
   for(float ahead=0;ahead<=horizon;ahead+=5){
    // Local speed is nonnegative; once even its zero-speed lower bound cannot
    // improve the minimum, all later samples are dominated by this one.
    if(Mathf.Sqrt(2*Brake*.78f*Mathf.Max(0,ahead-speed*.25f))>=desired)break;
    float at=self.Distance+ahead;float local=GeometrySpeed(route,at,pathIntent,ref geometryCache,ref previewChoice);
    float reachable=Mathf.Sqrt(local*local+2*Brake*.78f*Mathf.Max(0,ahead-speed*.25f));
    if(reachable<desired)GeometryLimitingDistance=at;
    LimitSpeed(ref desired,reachable,AiSpeedLimitSource.Geometry);
   }
   if(partialJumpIndex>=0){
    float ramp=30+route.Definition.jumps[partialJumpIndex].rampStartIndex*5;
    float shift=Mathf.Abs(partialBypassLane-self.Lateral);
    if(self.Distance<ramp&&shift>.05f){
     float timeToLane=shift/1.6f+.75f;
     float safeSpeed=Mathf.Max(0,(ramp-self.Distance-1.75f)/timeToLane);
     // The bypass is a lateral road change, so leave nitro speed for after
     // the full car reaches its paved target lane.
     LimitSpeed(ref desired,Mathf.Min(safeSpeed,SpeedLimit),AiSpeedLimitSource.Corridor);
    }
   }
   bool mergeYield=MergeYieldTo>=0;
   if(mergeYield){
    var other=world[MergeYieldTo];float gap=other.Distance-self.Distance;
    if(gap>0){
     float mergeLimit=FollowingSpeed(Mathf.Max(0,other.Speed),gap);
     float otherArrival=Mathf.Max(0,mergeEnd-other.Distance)/Mathf.Max(8,other.Speed);
     if(self.Distance<mergeEnd)mergeLimit=Mathf.Min(mergeLimit,(mergeEnd-self.Distance)/(otherArrival+.4f));
     LimitSpeed(ref desired,mergeLimit,AiSpeedLimitSource.Traffic,MergeYieldTo);
    }
   }
   if(traffic)LimitSpeed(ref desired,Mathf.Max(12,speed-5),AiSpeedLimitSource.Traffic,trafficEntrant);
   for(int i=0;i<world.Count;i++){if(i==id||world[i].Finished)continue;var other=world[i];float gap=other.Distance-self.Distance;if(gap>0&&gap<Mathf.Max(7,speed*.6f)&&Mathf.Abs(other.Lateral-self.Lateral)<1.9f)LimitSpeed(ref desired,FollowingSpeed(other.Speed,gap),AiSpeedLimitSource.Following,i);}
   // Correct a failed turn through ordinary braking rather than pose manipulation.
   if(Mathf.Abs(angle)>45)LimitSpeed(ref desired,18,AiSpeedLimitSource.Heading);
   if(CorridorBlocked)LimitSpeed(ref desired,18,AiSpeedLimitSource.Corridor);
   DesiredSpeed=desired;
   // Saturating the common speed limit is safe; geometry/traffic limits retain their braking margin.
   if(NeedsBraking(speed,desired))NitroHazards|=AiNitroHazard.Speed;
   if(Mathf.Abs(angle)>12)NitroHazards|=AiNitroHazard.Heading;
   if(!self.Grounded)NitroHazards|=AiNitroHazard.Unsupported;
   if(self.Recovering)NitroHazards|=AiNitroHazard.Recovery;
   if(traffic||mergeGuarded||mergeYield)NitroHazards|=AiNitroHazard.Traffic;
   if(CorridorBlocked)NitroHazards|=AiNitroHazard.Corridor;
   bool immediate=NitroHazards!=AiNitroHazard.None;
   if(boost&&(now>=boostUntil||immediate||self.Charge<=.1f))EndBoost(now,self,immediate?"safety":self.Charge<=.1f?"resource":"plan-complete");
   if(self.Charge>=balance["nitroCapacity"]-.1f)LastFullDecision=now;
   if(!boost){
    if(immediate)NitroReason="immediate-hazard";
    else if(!allowNitro)NitroReason="disabled-reference";
    else if(now<nextBoost)NitroReason="individual-slot";
    else if(Mathf.Abs(angle)>8)NitroReason="alignment-margin";
    else if(Mathf.Abs(angle+Mathf.Clamp(headingRate,-180,180)*.45f)>12)NitroReason="heading-trend";
    else if(self.Charge>=balance["aiNitroReserve"]||self.Charge>=balance["nitroCapacity"]-.1f){
     float available=self.Charge/balance["nitroDrainPerSecond"],duration=0,travel=0,predicted=speed;
     float plannedOffset=wish-center,initialLocal=Lane-center;
     var boostIntent=new PathIntent(self.Distance,self.Lateral,self.Lateral-BranchCenter(route,self.Distance,BranchIndex,BranchLane),plannedOffset,laneRate,speed,AccelEnvelope,BranchIndex,BranchLane);
     for(float t=0;t<available-.0001f;){
      FrameCache boostCache=default;
      float step=Mathf.Min(available-t,Mathf.Min(.1f,2.5f/Mathf.Max(1,predicted+AccelEnvelope*.1f)));t+=step;
      predicted=Mathf.Min(BoostLimit,predicted+AccelEnvelope*step);travel+=predicted*step;
      float safe=GeometrySpeed(route,self.Distance+travel,boostIntent,ref boostCache,ref previewChoice),stop=(predicted+2)*(predicted+2)/(2*Brake*.78f)+predicted*.25f+5;
      for(float d=5;d<=stop;d+=5){
       // Predicted speed is fixed for this inner scan, so this bound rises with d.
       if(Mathf.Sqrt(2*Brake*.78f*Mathf.Max(0,d-predicted*.25f))>=safe)break;
       float v=GeometrySpeed(route,self.Distance+travel+d,boostIntent,ref boostCache,ref previewChoice);
       safe=Mathf.Min(safe,Mathf.Sqrt(v*v+2*Brake*.78f*Mathf.Max(0,d-predicted*.25f)));
      }
      float futureLane=ProjectedLane(route,self.Distance+travel,initialLocal,plannedOffset,laneRate,t,BranchIndex,BranchLane);
      futureLane=SafeLane(route,self.Distance+travel,futureLane);
      if(NeedsBraking(predicted,safe)||PredictedTraffic(world,self,travel,t,futureLane,predicted)||CrossesNewBranch(route,self.Distance,self.Distance+travel)||!HasCorridor(route,self.Distance,self.Lateral,self.Distance+travel,futureLane))break;
      duration=t;
     }
     if(duration>=.45f&&speed*duration+.5f*AccelEnvelope*duration*duration>=balance["aiNitroStraightDistance"]){boost=true;boostTargetOffset=plannedOffset;boostLaneRate=laneRate;boostBranch=BranchIndex;boostBranchLane=BranchLane;boostStarted=now;boostUntil=now+duration;Plans++;NitroReason=duration>=1.4?"long-plan":"short-plan";EmitNitro("start",NitroReason,now,self);}
     else NitroReason="braking-window";
    } else NitroReason="reserve";
   }
   bool braking=speed>desired+1;
   NitroCommand=allowNitro&&boost&&!braking;
   return new DrivingInput{throttle=1,brake=braking?Mathf.Clamp01((speed-desired)/5):0,steer=steer,nitro=NitroCommand};
  }
  bool NeedsBraking(float speed,float limit)=>speed>limit+1 || (limit<BoostLimit && limit<speed+2);
  void LimitSpeed(ref float desired,float candidate,AiSpeedLimitSource source,int entrant=-1){if(candidate<desired){desired=candidate;SpeedLimitSource=source;LimitingEntrant=entrant;}}
  float FollowingSpeed(float speed,float gap)=>Mathf.Sqrt(Mathf.Max(0,speed*speed+2*Brake*.65f*Mathf.Max(0,gap-5)));
  bool CrossesNewBranch(TrackRoute route,float from,float to){if(route.Definition==null)return false;for(int i=0;i<route.Definition.branches.Length;i++){float start=30+route.Definition.branches[i].startIndex*5;if(i!=BranchIndex&&start>=from&&start-25<=to)return true;}return false;}
  float ProjectedLane(TrackRoute route,float distance,float initialLocal,float targetLocal,float rate,float seconds,int branch,int lane)=>BranchCenter(route,distance,branch,lane)+Mathf.MoveTowards(initialLocal,targetLocal,rate*seconds);
  bool PredictedTraffic(AiWorldSnapshot world,AiObservation self,float travel,float time,float lateral,float speed){
   for(int i=0;i<world.Count;i++){
    if(i==id||world[i].Finished)continue;var other=world[i];
    float gap=other.Distance+other.Speed*time-self.Distance-travel;
    float otherLateral=other.Lateral+other.LateralSpeed*time;
    // The commanded lane is a goal; the car still occupies its observed lane until
    // a later physics snapshot confirms the move. Check both during the forecast.
    float occupiedSide=otherLateral-self.Lateral,plannedSide=otherLateral-lateral;
    if(Mathf.Abs(gap)<6&&(Mathf.Abs(occupiedSide)<2.1f||Mathf.Abs(plannedSide)<2.1f))return true;
    if(gap>0&&gap<Mathf.Max(7,speed*.6f)&&(Mathf.Abs(occupiedSide)<1.9f||Mathf.Abs(plannedSide)<1.9f)&&NeedsBraking(speed,FollowingSpeed(other.Speed,gap)))return true;
   }
   return false;
  }
  void EmitNitro(string kind,string reason,double now,AiObservation self){NitroEvents?.Invoke(new AiNitroEvent{Time=now,Plan=Plans,Kind=kind,Reason=reason,Planned=boostUntil-boostStarted,Actual=now-boostStarted,Speed=self.Speed,Desired=DesiredSpeed,Heading=HeadingError,Hazards=NitroHazards,LimitSource=SpeedLimitSource,LimitingEntrant=LimitingEntrant});}
  void EndBoost(double now,AiObservation self,string reason){if(!boost)return;EmitNitro("end",reason,now,self);boost=false;nextBoost=now+.6+personality*.8;}
  struct PreviewChoice {
   public int Branch,Lane;
   public AiWorldSnapshot World;
   public AiObservation Self;
  }
  bool BranchApproachConflict(AiWorldSnapshot world,AiObservation self,Procedural.Branch branch,int lane,float start){
   float destination=-(float)branch.laneOffsets[lane];
   float untilStart=Mathf.Max(0,start-self.Distance)/Mathf.Max(8,Mathf.Abs(self.Speed));
   for(int i=0;i<world.Count;i++){
    if(i==id||world[i].Finished)continue;
    var other=world[i];
    float gap=other.Distance-self.Distance+(other.Speed-self.Speed)*untilStart;
    if(Mathf.Abs(gap)>Mathf.Max(8,Mathf.Abs(self.Speed)*.18f))continue;
    float projected=other.Lateral+Mathf.Clamp(other.LateralSpeed*untilStart,-2,2);
    if(self.Lateral<other.Lateral-1.2f&&destination>other.Lateral+1.2f||
       self.Lateral>other.Lateral+1.2f&&destination<other.Lateral-1.2f||
       self.Lateral<projected-1.2f&&destination>projected+1.2f||
       self.Lateral>projected+1.2f&&destination<projected-1.2f)return true;
   }
   return false;
  }
  int BranchApproachLane(AiWorldSnapshot world,AiObservation self,Procedural.Branch branch,float start){
   int preferred=Mathf.Min(branch.laneOffsets.Length-1,(int)(personality*branch.laneOffsets.Length));
   if(!BranchApproachConflict(world,self,branch,preferred,start))return preferred;
   int choice=preferred;float nearest=float.PositiveInfinity;
   for(int lane=0;lane<branch.laneOffsets.Length;lane++){
    if(BranchApproachConflict(world,self,branch,lane,start))continue;
    float distance=Mathf.Abs(self.Lateral+(float)branch.laneOffsets[lane]);
    if(distance<nearest){nearest=distance;choice=lane;}
   }
   return choice;
  }
  void PreviewBranchLane(TrackRoute route,AiWorldSnapshot world,AiObservation self,out int branch,out int lane){
   branch=BranchIndex;lane=BranchLane;
   if(branch>=0||route.Definition==null)return;
   for(int i=0;i<route.Definition.branches.Length;i++){
    var candidate=route.Definition.branches[i];float start=30+candidate.startIndex*5,end=30+candidate.endIndex*5;
    if(end<self.Distance)continue;
    branch=i;lane=-1;return;
   }
  }
  void SelectBranch(TrackRoute route,AiWorldSnapshot world,AiObservation self,float look){
   if(route.Definition==null){BranchIndex=BranchLane=-1;return;}
   var branches=route.Definition.branches;
   if(BranchIndex>=0&&self.Distance-1.75f>30+branches[BranchIndex].endIndex*5){BranchIndex=BranchLane=-1;}
   if(BranchIndex>=0)return;
   for(int b=0;b<branches.Length;b++){
    var branch=branches[b];float start=30+branch.startIndex*5,end=30+branch.endIndex*5;
    if(self.Distance+look<start||self.Distance-1.75f>end)continue;
    BranchIndex=b;BranchLane=BranchApproachLane(world,self,branch,start);
    float blend=(float)Procedural.Layout.Blend(branch,(self.Distance-30)/5);
    if(self.Distance>start&&blend>.01f){float nearest=float.PositiveInfinity;for(int lane=0;lane<branch.laneOffsets.Length;lane++){float gap=Mathf.Abs(self.Lateral+(float)branch.laneOffsets[lane]*blend);if(gap<nearest){nearest=gap;BranchLane=lane;}}}
    branchOffset=self.Lateral-BranchCenter(route,self.Distance);return;
   }
  }
  float BranchCenter(TrackRoute route,float distance)=>BranchCenter(route,distance,BranchIndex,BranchLane);
  int NearestBranchLane(TrackRoute route,int branch,float distance,float lateral){
   var definition=route.Definition.branches[branch];int nearest=0;float gap=float.PositiveInfinity;
   for(int lane=0;lane<definition.laneOffsets.Length;lane++){
    float next=Mathf.Abs(lateral-BranchCenter(route,distance,branch,lane));
    if(next<gap){gap=next;nearest=lane;}
   }
   return nearest;
  }
  float BranchCenter(TrackRoute route,float distance,int branch,int lane){
   if(branch<0)return 0;var b=route.Definition.branches[branch];
   float index=(distance-30)/5;if(index<b.startIndex||index>b.endIndex)return 0;
   return -(float)(b.laneOffsets[lane]*Procedural.Layout.Blend(b,index));
  }
  bool HasCorridor(TrackRoute route,float from,float fromLane,float to,float toLane,bool edgeRecovery=false){
   if(route.Definition==null)return true;
   float start=from-1.75f,end=to+1.75f,at=start;
   float Local(float d){float t=Mathf.Clamp01((d-from)/(to-from));return BranchCenter(route,d)+Mathf.Lerp(fromLane-BranchCenter(route,from),toLane-BranchCenter(route,to),t);}
   float leftDeficit=0,rightDeficit=0;
   if(edgeRecovery){
    // The rear is an already occupied pose, not a proposed move. Prove its connected
    // center line, then require both deficits to improve from the actual center onward.
    if(!route.SupportsCorridorSegment(start,Local(start),from,fromLane,0)||!route.RoadDeficits(from,fromLane,out leftDeficit,out rightDeficit))return false;
    at=from;
   }
   while(at<end-.001f){
    float next=Mathf.Min(end,30+(Mathf.Floor((at-30)/5)+1)*5);float a=Local(at),b=Local(next);
    if(edgeRecovery){if(!route.SupportsInteriorReturn(at,a,next,b,ref leftDeficit,ref rightDeficit))return false;at=next;continue;}
    if(!route.SupportsCorridorSegment(at,a,next,b,2.1f)){
     bool flight=false;
     foreach(var jump in route.Definition.jumps)if(jump.kind=="mandatory"&&route.Definition.guardrailMode!="full"&&at>=30+jump.launchIndex*5-1.75f&&next<=30+jump.gapEndIndex*5+1.75f&&Mathf.Max(Mathf.Abs(a+(float)jump.lateralCenter),Mathf.Abs(b+(float)jump.lateralCenter))+2.1f<=jump.lateralHalfWidth){flight=true;break;}
     if(!flight)return false;
    }
    at=next;
   }
   return !edgeRecovery||(leftDeficit<=.002f&&rightDeficit<=.002f);
  }
  bool PartialBypassTarget(TrackRoute route,float distance,float lateral,float speed,out float target){
   target=0;
   if(route.Definition==null||route.Definition.guardrailMode=="full"){partialJumpIndex=-1;return false;}
   var jumps=route.Definition.jumps;
   for(int i=0;i<jumps.Length;i++){
    var jump=jumps[i];if(jump.kind!="partial")continue;
    float ramp=30+jump.rampStartIndex*5,landing=30+jump.landingEndIndex*5;
    // The gap starts after the raised ramp. Begin the bypass while ordinary
    // lane-rate steering can still place the full car on the paved side.
    if(distance>landing+5)continue;
    if(partialJumpIndex!=i){
     float gap=30+jump.gapStartIndex*5+2.5f,best=float.PositiveInfinity;
     float candidateLane=0;
     foreach(var span in route.PavedAt(gap)){
      float center=-(float)span.offset,room=Mathf.Max(0,(float)span.halfWidth-2.6f);
      float candidate=Mathf.Clamp(lateral,center-room,center+room),cost=Mathf.Abs(candidate-lateral);
      if(cost<best){best=cost;candidateLane=candidate;}
     }
     if(float.IsPositiveInfinity(best))continue;
     float shiftTime=Mathf.Abs(candidateLane-lateral)/1.6f+.75f;
     float approachSpeed=Mathf.Min(BoostLimit,Mathf.Max(0,speed)+AccelEnvelope*shiftTime);
     float lead=Mathf.Max(180,approachSpeed*shiftTime+10);
     if(distance<ramp-lead)continue;
     partialBypassLane=candidateLane;
     partialJumpIndex=i;
    }
    // A car pushed to another fully paved bypass line has already completed
    // the lateral move. Keep that safe line instead of braking for one old point.
    if(distance<=landing&&Mathf.Abs(lateral-partialBypassLane)>.05f&&
       HasCorridor(route,distance,lateral,landing,lateral)&&
       route.SupportsCorridorSegment(distance,lateral,landing,lateral,2.11f))partialBypassLane=lateral;
    target=partialBypassLane;return true;
   }
   partialJumpIndex=-1;return false;
  }
  float SafeLane(TrackRoute route,float distance,float wish){
   if(BranchIndex>=0){var branch=route.Definition.branches[BranchIndex];float index=(distance-30)/5;if(index>=branch.startIndex&&index<=branch.endIndex){float blend=(float)Procedural.Layout.Blend(branch,index),half=(float)branch.laneHalfWidth+(BranchLane==branch.safeLane?.7f:BranchLane==branch.fastLane?-.55f:0);half=Mathf.Lerp(route.Evaluate(distance).halfWidth,half,blend);float center=BranchCenter(route,distance),room=Mathf.Max(0,half-2.6f);return Mathf.Clamp(wish,center-room,center+room);}}
   var spans=route.PavedAt(distance);float best=wish,score=float.PositiveInfinity;
   foreach(var s in spans){float margin=2.1f,center=-(float)s.offset,room=Mathf.Max(0,(float)s.halfWidth-margin);float candidate=Mathf.Clamp(wish,center-room,center+room);float cost=Mathf.Abs(candidate-wish);if(cost<score){score=cost;best=candidate;}}
   if(route.Definition!=null)foreach(var jump in route.Definition.jumps)if(distance>=30+jump.rampStartIndex*5-12&&distance<=30+jump.landingEndIndex*5){if(jump.kind!="partial")best=Mathf.Clamp(best,-(float)jump.lateralCenter-(float)jump.lateralHalfWidth+1.4f,-(float)jump.lateralCenter+(float)jump.lateralHalfWidth-1.4f);}
   return best;
  }
  bool PlannedBranch(TrackRoute route,float distance,PathIntent intent,ref PreviewChoice preview,out int branch,out int lane,out float offset){
   branch=lane=-1;offset=0;
   if(route.Definition==null)return false;
   var branches=route.Definition.branches;
   for(int i=0;i<branches.Length;i++){
    var candidate=branches[i];float start=30+candidate.startIndex*5,end=30+candidate.endIndex*5;
    if(distance<start-5||distance>end+5)continue;
    branch=i;
    if(i==BranchIndex&&BranchLane>=0)lane=BranchLane;
    else if(i==preview.Branch){
     if(preview.Lane<0)preview.Lane=BranchApproachLane(preview.World,preview.Self,candidate,start);
     lane=preview.Lane;
    }else lane=Mathf.Min(candidate.laneOffsets.Length-1,(int)(personality*candidate.laneOffsets.Length));
    offset=boost&&i==boostBranch?boostTargetOffset:i==BranchIndex?branchOffset:(personality-.5f)*6;
    return true;
   }
   return false;
  }
  float PlannedBranchLateral(TrackRoute route,TrackFrame frame,int branch,int lane,float offset){
   float distance=frame.distance;
   var b=route.Definition.branches[branch];float index=(distance-30)/5;
   if(index<b.startIndex||index>b.endIndex)return offset;
   float blend=(float)Procedural.Layout.Blend(b,index);
   float half=(float)b.laneHalfWidth+(lane==b.safeLane?.7f:lane==b.fastLane?-.55f:0);
   half=Mathf.Lerp(frame.halfWidth,half,blend);
   return BranchCenter(route,distance,branch,lane)+Mathf.Clamp(offset,-Mathf.Max(0,half-2.6f),Mathf.Max(0,half-2.6f));
  }
  float BranchPathCurvature(TrackRoute route,TrackFrame a,TrackFrame b,TrackFrame c,int branch,int lane,float offset,out float signedNormalCurve){
   Vector3 p0=a.position+a.right*PlannedBranchLateral(route,a,branch,lane,offset);
   Vector3 p1=b.position+b.right*PlannedBranchLateral(route,b,branch,lane,offset);
   Vector3 p2=c.position+c.right*PlannedBranchLateral(route,c,branch,lane,offset);
   Vector3 first=p1-p0,last=p2-p1;
   float firstLength=first.magnitude,lastLength=last.magnitude,arc=(firstLength+lastLength)*.5f;
   if(arc<=.001f){signedNormalCurve=0;return 0;}
   Vector3 firstUnit=firstLength>1e-5f?first/firstLength:Vector3.zero;
   Vector3 lastUnit=lastLength>1e-5f?last/lastLength:Vector3.zero;
   signedNormalCurve=Vector3.Dot(lastUnit-firstUnit,b.normal)/arc;
   return Mathf.Abs(Vector3.Dot(Vector3.Cross(firstUnit,lastUnit),b.normal))/arc;
  }
  TrackFrame FrameAt(TrackRoute route,float distance,ref FrameCache cache)=>cache.Get(route,distance);
  float GeometrySpeed(TrackRoute route,float distance,PathIntent intent,ref FrameCache cache,ref PreviewChoice preview){
   bool branchPreview=PlannedBranch(route,distance,intent,ref preview,out int branch,out int lane,out float offset);
   // The plan begins at an observed pose. Its unknown prior derivative must not
   // create a fictitious five-metre kink on an otherwise straight road.
   bool pendingBranch=false;
   if(intent.Branch<0&&route.Definition!=null)
    foreach(var candidate in route.Definition.branches){float start=30+candidate.startIndex*5;if(distance>=start-25&&distance<=30+candidate.endIndex*5+5){pendingBranch=true;break;}}
   // Until commitment, the deterministic branch preview owns this approach;
   // a current-lane extrapolation cannot represent the coming split.
   bool plannedPath=distance>=intent.Start+5&&!pendingBranch&&(!branchPreview||intent.Branch==branch);
   TrackFrame beforeFrame=default,currentFrame=default,afterFrame=default;
   Vector3 tangent,normal,ahead;
   if(branchPreview||plannedPath){
    beforeFrame=FrameAt(route,distance-5,ref cache);currentFrame=FrameAt(route,distance,ref cache);afterFrame=FrameAt(route,distance+5,ref cache);
    tangent=currentFrame.tangent;normal=currentFrame.normal;ahead=afterFrame.tangent;
   }else {route.EvaluateBasis(distance,out tangent,out normal);ahead=route.EvaluateTangent(distance+5);}
   // A 1000kg car has a shared 52kN lateral tyre-force budget. Reserve half for correction/contact.
   float lateralCurve=Mathf.Abs(Vector3.Dot(Vector3.Cross(tangent,ahead),normal))/5;
   float pathNormalCurve=float.PositiveInfinity;
   if(branchPreview)
    lateralCurve=Mathf.Max(lateralCurve,BranchPathCurvature(route,beforeFrame,currentFrame,afterFrame,branch,lane,offset,out pathNormalCurve));
   if(plannedPath){
    float beforeLane=CachedPathLateral(route,distance-5,intent,ref cache);
    float currentLane=CachedPathLateral(route,distance,intent,ref cache);
    float afterLane=CachedPathLateral(route,distance+5,intent,ref cache);
    float pathCurve;

     pathCurve=CachedLateralPathCurvature(beforeFrame,currentFrame,afterFrame,beforeLane,currentLane,afterLane,out float signedNormalCurve);
     pathNormalCurve=Mathf.Min(pathNormalCurve,signedNormalCurve);
        lateralCurve=Mathf.Max(lateralCurve,pathCurve);
   }
   float signedVerticalChange=Vector3.Dot(ahead-tangent,normal);
   float verticalCurve=Mathf.Abs(signedVerticalChange)/5;
   float limit=Mathf.Min(BoostLimit,Mathf.Sqrt(24/Mathf.Max(.00001f,lateralCurve)));
   // Crest support is finite despite magnets; use the same geometry/force envelope for all drivers.
   if(signedVerticalChange<0)
    limit=Mathf.Min(limit,VerticalSpeedLimit(signedVerticalChange,verticalCurve,-Vector3.Dot(Physics.gravity,normal),BoostLimit,route.ContinuousLoopAt(distance)));
   if(pathNormalCurve<0){
    // The centerline can remain concave while a lateral path on a twisted road
    // becomes convex. Only the centerline retains the supported-loop exception.
    float additional=route.ContinuousLoopAt(distance)?pathNormalCurve-signedVerticalChange/5:pathNormalCurve;
    if(additional<0)limit=Mathf.Min(limit,CrestSpeedLimit(-additional,-Vector3.Dot(Physics.gravity,normal),BoostLimit));
   }
   if(route.IsJumpRegion(distance))limit=Mathf.Min(limit,45);
   return Mathf.Max(10,limit);
  }
 }
}
