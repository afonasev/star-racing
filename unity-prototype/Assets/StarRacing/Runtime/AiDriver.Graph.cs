using System;
using UnityEngine;
using StarRacingPrototype.Procedural;
namespace StarRacingPrototype {
 public sealed partial class AiDriver {
  public int GraphChoice {get;private set;}
  public double GraphTechnicalSeconds {get;private set;}
  public double GraphSpeedSeconds {get;private set;}
  GraphVehicleRoute graphOwner;int graphPositionRevision=-1;long graphTick=-1;
  GraphAiWorldSnapshot graphWorld;double graphTime=double.NaN;DrivingInput graphCommand;
  GraphJump graphRunwayJump;float graphRunwayLane;
  bool graphStopIntent;public bool GraphStopIntent=>graphStopIntent;
  bool graphChoiceLocked;int graphYieldRevision=-1;double graphYieldAt;
  void ResetGraphState(){graphStopIntent=false;graphRunwayJump=null;GraphChoice=0;graphOwner=null;graphPositionRevision=-1;graphWorld=null;graphTime=double.NaN;graphTick=-1;graphChoiceLocked=false;graphYieldRevision=-1;}
  // A stopped graph decision is intent, not a pedal held across sign changes.
  // Sampling here uses the same cached frame and physical body as StepHuman.
  public DrivingInput ApplyGraphControl(MagneticVehicle car,DrivingInput command,float dt) {
   if(!graphStopIntent)return command;
   if(car==null||car.Body==null||car.Drive==null||car.Seat!=id||car.FinishedCoasting||car.Body.isKinematic||graphOwner==null||!graphOwner.Current||
    !ReferenceEquals(car.GraphRoute,graphOwner)||car.PositionRevision!=graphPositionRevision)return default;
   var observed=HumanHandlingForces.Observe(car.Body.linearVelocity,car.Body.rotation*Vector3.forward,car.Body.angularVelocity,car.Frame);
   return StoppedGraphInput((float)observed.Speed,car.Drive.Snapshot["acceleration"],dt,command);
  }
  static DrivingInput StoppedGraphInput(float speed,float acceleration,float dt,DrivingInput command) {
   command.throttle=command.brake=0;command.nitro=false;
   if(float.IsNaN(speed)||float.IsInfinity(speed)||float.IsNaN(dt)||float.IsInfinity(dt)||dt<=0||
    float.IsNaN(acceleration)||float.IsInfinity(acceleration)||acceleration<=0)return command;
   if(speed>.08f)command.brake=1;
   else if(speed<-.08f) {
    command.throttle=(float)Math.Min(1,-(double)speed/((double)acceleration*dt));
    // Round toward zero only if native float products could overshoot the stop.
    while(command.throttle>0&&(command.throttle*acceleration)*dt> -speed)
     command.throttle=BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(command.throttle)-1);
   }
   return command;
  }
  static float GraphSide(GraphNavigation nav,GraphAiObservation self,GraphAiObservation other,int branch,float gap) {
   var f=nav.Frame(self.Route.Cursor,branch,gap);return Vector3.Dot(other.Motion.Position-f.position.Float,-f.right.Float);
  }
  void SelectGraphChoice(GraphAiWorldSnapshot world,GraphAiObservation self) {
   if(self.Route.Branch!=0){GraphChoice=self.Route.Branch;graphChoiceLocked=true;return;}
   if(graphChoiceLocked&&GraphChoice!=0 && world.Navigation.CanEnter(self,GraphChoice))return;
   GraphTechnicalSeconds=world.Navigation.ChoiceSeconds(world,self,1);GraphSpeedSeconds=world.Navigation.ChoiceSeconds(world,self,2);
   bool technical=world.Navigation.CanEnter(self,1),speed=world.Navigation.CanEnter(self,2);
   int preferred=!technical?2:!speed?1:GraphTechnicalSeconds<GraphSpeedSeconds?1:2;
   if(technical&&speed&&Math.Abs(GraphTechnicalSeconds-GraphSpeedSeconds)<.15)preferred=((decisionSeed^(uint)id)&1)==0?1:2;
   if(GraphChoice==0 || !world.Navigation.CanEnter(self,GraphChoice) || (preferred!=GraphChoice && Math.Abs(GraphTechnicalSeconds-GraphSpeedSeconds)>1.5))GraphChoice=preferred;
   // Decide before entering the fork; the accepted physical binding remains the
   // authority once a divider commits an arm, including external contact/recovery.
   if(self.Route.Cursor.roadId!=0 || world.Navigation.Graph.Road(0).Length-self.Route.Cursor.localPhysicalDistance<=80)graphChoiceLocked=true;
  }
  static bool GraphPriority(GraphNavigation nav,GraphAiObservation first,GraphAiObservation second) {
   double firstArrival=Math.Max(0,nav.ToMerge(first))/Math.Max(8,first.Motion.Speed),secondArrival=Math.Max(0,nav.ToMerge(second))/Math.Max(8,second.Motion.Speed);
   // One total order, never a pairwise tolerance. Rechecking reservations against
   // this order keeps all current yield edges acyclic, including four-way packs.
   long a=(long)Math.Floor(firstArrival/.1),b=(long)Math.Floor(secondArrival/.1);
   return a<b || a==b&&first.Motion.Id<second.Motion.Id;
  }
  void GraphMerge(GraphAiWorldSnapshot world,GraphAiObservation self,ref float desired) {
   var nav=world.Navigation;float speed=Mathf.Max(0,self.Motion.Speed);double own=nav.ToMerge(self);
   bool watch=own>=-VehicleGeometry.PairRearmDistance && own<=speed*speed/(2*Brake)+speed*.4f+80;
   if(!watch){MergeYieldTo=-1;return;}
   if(MergeYieldTo>=0) {
    var target=world[MergeYieldTo];double remaining=nav.ToMerge(target);
    if(!target.Valid||target.Motion.Finished||target.Motion.Revision!=graphYieldRevision||remaining<-VehicleGeometry.PairRearmDistance||world.Time-graphYieldAt>2&&target.Motion.Speed<2||!GraphPriority(nav,target,self))MergeYieldTo=-1;
   }
   if(MergeYieldTo<0) {
    double nearest=double.PositiveInfinity;
    for(int i=0;i<world.Count;i++) {
     var other=world[i];if(i==id||!other.Valid||other.Motion.Finished||other.Route.Branch==0||other.Route.Branch==self.Route.Branch||other.Motion.Speed<2)continue;
     double distance=nav.ToMerge(other);if(distance< -VehicleGeometry.PairRearmDistance || distance>other.Motion.Speed*other.Motion.Speed/(2*Brake)+other.Motion.Speed*.4f+80)continue;
     double arrival=Math.Max(0,own)/Math.Max(8,speed),otherArrival=Math.Max(0,distance)/Math.Max(8,other.Motion.Speed);
     double gap=Math.Abs(arrival-otherArrival);
     bool conflict=gap<.75;
     bool priority=GraphPriority(nav,other,self);
     if(conflict&&priority&&(gap<nearest || gap==nearest&&i<MergeYieldTo)){nearest=gap;MergeYieldTo=i;graphYieldRevision=other.Motion.Revision;graphYieldAt=world.Time;}
    }
   }
   if(MergeYieldTo>=0) {
    var other=world[MergeYieldTo];double remaining=nav.ToMerge(other),eta=Math.Max(0,remaining)/Math.Max(8,other.Motion.Speed);
    float limit=(float)(Math.Max(0,own)/(eta+.4));LimitSpeed(ref desired,limit,AiSpeedLimitSource.Traffic,MergeYieldTo);TrafficBlocked=true;Maneuver=AiManeuver.Yield;
   }
  }
  bool GraphRunwayLane(GraphNavigation nav,GraphAiObservation self,float speed,out float lane) {
   lane=0;if(nav.Graph.guardrailMode=="full"){graphRunwayJump=null;return false;}
   double distance=self.Route.Cursor.localPhysicalDistance;float room=Mathf.Max(0,(float)nav.Graph.Evaluate(self.Route.Cursor).halfWidth-2.6f);
   foreach(var jump in nav.Graph.jumps) {
    if(jump.roadId!=self.Route.Cursor.roadId||distance>jump.ballisticLanding+5)continue;
    float low=-(float)(jump.lateralCenter+jump.lateralHalfWidth),high=-(float)(jump.lateralCenter-jump.lateralHalfWidth);
    float leftEnd=Mathf.Min(room,low-2.6f),rightStart=Mathf.Max(-room,high+2.6f),target=0,best=float.PositiveInfinity;
    if(leftEnd>=-room){target=Mathf.Clamp(self.Motion.Lateral,-room,leftEnd);best=Mathf.Abs(target-self.Motion.Lateral);}
    if(rightStart<=room){float candidate=Mathf.Clamp(self.Motion.Lateral,rightStart,room),cost=Mathf.Abs(candidate-self.Motion.Lateral);if(cost<best){target=candidate;best=cost;}}
    if(jump.mandatory){target=Mathf.Clamp(self.Motion.Lateral,-room,room);best=Mathf.Abs(target-self.Motion.Lateral);}
    if(float.IsPositiveInfinity(best))continue;
    float shiftTime=best/1.6f+.75f,approachSpeed=Mathf.Min(BoostLimit,speed+AccelEnvelope*shiftTime);
    // Reserve the complete grid/runway approach, as the legacy partial bypass
    // reserves steering time before the raised ramp rather than only the gap.
    // Keep a clear lane through mandatory flight instead of crossing columns
    // at launch; the same ordinary inputs retain all collision/flight authority.
    if(distance<jump.rampStart-Mathf.Max(480,approachSpeed*shiftTime+10))continue;
    if(!ReferenceEquals(graphRunwayJump,jump)){graphRunwayJump=jump;graphRunwayLane=target;}
    bool paved=jump.mandatory?Mathf.Abs(self.Motion.Lateral)<=room:self.Motion.Lateral>=-room&&self.Motion.Lateral<=leftEnd||self.Motion.Lateral>=rightStart&&self.Motion.Lateral<=room;
    if(paved)graphRunwayLane=self.Motion.Lateral;
    lane=graphRunwayLane;return true;
   }
   graphRunwayJump=null;return false;
  }
  void GraphLaunchYield(GraphAiWorldSnapshot world,GraphAiObservation self,float speed,ref float desired) {
   var graph=world.Navigation.Graph;if(graph.guardrailMode=="full")return;
   double distance=self.Route.Cursor.localPhysicalDistance;
   foreach(var jump in graph.jumps) {
    if(!jump.mandatory||jump.roadId!=self.Route.Cursor.roadId||distance>=jump.rampStart||distance<jump.rampStart-(speed*speed/(2*Brake)+speed*.4f+80))continue;
    long ownSlot=(long)Math.Floor(distance/VehicleGeometry.Length);
    for(int i=0;i<world.Count;i++) {
     var other=world[i];if(i==id||!other.Valid||other.Motion.Finished||other.Route.Cursor.roadId!=jump.roadId)continue;
     double at=other.Route.Cursor.localPhysicalDistance;
     if(at>jump.ballisticLanding+VehicleGeometry.Length)continue;
     long otherSlot=(long)Math.Floor(at/VehicleGeometry.Length);
     // A total physical order keeps the reservation acyclic even when two
     // columns arrive together. Wait on the flat road, not at the launch lip.
     if(otherSlot<ownSlot||otherSlot==ownSlot&&i>id)continue;
     float limit=Mathf.Sqrt(2*Brake*.78f*Mathf.Max(0,(float)(jump.rampStart-12-distance)));
     if(limit<desired){LimitSpeed(ref desired,limit,AiSpeedLimitSource.Traffic,i);TrafficBlocked=true;Maneuver=AiManeuver.Yield;}
    }
   }
  }
  static bool NeedsRunwayAlignment(GraphNavigation nav,GraphAiObservation self,int branch,GraphJump jump) {
   // Optional ramps require bypass. A mandatory jump may keep an already safe
   // column, but prove its entire gap with the existing body/corridor margins.
   return !jump.mandatory||!nav.HasCorridor(self,branch,self.Motion.Lateral,(float)Math.Max(0,jump.gapEnd+2-self.Route.Cursor.localPhysicalDistance));
  }
  float GraphReachableSpeed(GraphNavigation nav,RouteCursor cursor,int branch,float speed) {
   float desired=BoostLimit,horizon=BoostLimit*BoostLimit/(2*Brake)+speed*.4f+80;
   for(float ahead=0;ahead<=horizon;ahead+=5) {
    if(Mathf.Sqrt(2*Brake*.78f*Mathf.Max(0,ahead-speed*.25f))>=desired)break;
    float limit=nav.LocalSpeed(cursor,branch,ahead);desired=Mathf.Min(desired,Mathf.Sqrt(limit*limit+2*Brake*.78f*Mathf.Max(0,ahead-speed*.25f)));
   }
   return desired;
  }
  public DrivingInput StepGraph(GraphAiWorldSnapshot world,float dt,bool allowNitro=true) {
            using var profile=GraphRuntimeProfile.AI.Auto();
   var self=world[id];var nav=world.Navigation;
   if(dt<=0||self.Motion.Finished){graphStopIntent=false;graphWorld=null;return default;}
   if(!nav.Current || !self.Valid || !ReferenceEquals(self.Route.Surface,nav.Surface)) {
    if(!nav.Current || graphOwner==null || !graphOwner.Current || graphOwner.PositionRevision!=graphPositionRevision)ResetGraphState();
    graphWorld=null;Target=MergeYieldTo=-1;ContactPressure=NitroCommand=false;Mode=AiMode.Recovery;DesiredSpeed=0;NitroReason="graph-observation-invalid";graphStopIntent=true;return HoldAtBlockedCorridor(self.Motion.Speed);
   }
   if(!ReferenceEquals(graphOwner,self.Route.Owner)||graphPositionRevision!=self.Route.PositionRevision) {
    ResetGraphState();graphOwner=self.Route.Owner;graphPositionRevision=self.Route.PositionRevision;Lane=self.Motion.Lateral;Target=MergeYieldTo=-1;boost=false;nextBoost=world.Time+.25+personality*2.5;Mode=AiMode.Pace;
   }
   if(ReferenceEquals(graphWorld,world)&&graphTime==world.Time&&graphTick==self.Route.Tick)return graphCommand;
   graphWorld=world;graphTime=world.Time;graphTick=self.Route.Tick;LastFullDecision=world.Time;
   SelectGraphChoice(world,self);int branch=GraphChoice;
   var motion=self.Motion;float speed=Mathf.Max(0,motion.Speed),look=Mathf.Clamp(Mathf.Abs(motion.Speed)*.55f,12,35);
   var frame=nav.Graph.Evaluate(self.Route.Cursor);var ahead=nav.Frame(self.Route.Cursor,branch,look);
   TrafficBlocked=ContactPressure=CorridorBlocked=NitroCommand=false;MergeYieldTo=MergeYieldTo>=world.Count?-1:MergeYieldTo;Maneuver=AiManeuver.Cruise;
   Target=-1;double progressGap=100+balance["aiOvertakeAggression"]*80;
   for(int i=0;i<world.Count;i++){var o=world[i];double gap=o.CommonProgress-self.CommonProgress;if(i!=id&&o.Valid&&!o.Motion.Finished&&gap>0&&gap<progressGap){Target=i;progressGap=gap;}}
   Mode=Target>=0?AiMode.Attack:AiMode.Pace;
   float wish=0;int blocker=-1;float nearest=float.PositiveInfinity;
   for(int i=0;i<world.Count;i++) {
    var other=world[i];if(i==id||other.Motion.Finished||!nav.PhysicalGap(self,other,branch,out float gap))continue;
    float side=GraphSide(nav,self,other,branch,gap);
    if(gap>0&&gap<Mathf.Max(14,speed*.9f)&&Mathf.Abs(side-motion.Lateral)<PairClearance(motion,other.Motion)&&gap<nearest){nearest=gap;blocker=i;}
   }
   if(blocker>=0) {
    var other=world[blocker];float side=GraphSide(nav,self,other,branch,nearest),room=Mathf.Max(0,(float)ahead.halfWidth-2.6f);
    int sign=motion.Lateral>=side?1:-1;if(Mathf.Abs(motion.Lateral-side)<.2f)sign=personality<.5f?-1:1;
    bool clear=false;
    for(int attempt=0;attempt<2&&!clear;attempt++,sign=-sign) {
     float candidate=side+sign*PassingClearance;if(Mathf.Abs(candidate)>room)continue;clear=true;
     for(int i=0;i<world.Count;i++) {
      var o=world[i];if(i==id||o.Motion.Finished||!nav.PhysicalGap(self,o,branch,out float gap)||Mathf.Abs(gap)>Mathf.Max(18,speed*1.5f))continue;
      float occupied=GraphSide(nav,self,o,branch,gap);
      // Separating from a slow leader is allowed; crossing an occupied lane is not.
      bool crosses=(candidate-occupied)*(motion.Lateral-occupied)<0 && Math.Abs(motion.Lateral-occupied)>.2;
      float predictedGap=gap+(o.Motion.Speed-speed)*.8f;
      if((gap*predictedGap<=0 || Mathf.Abs(gap)<4.5f||Mathf.Abs(predictedGap)<4.5f)&&(crosses||Math.Abs(candidate-occupied)<PairClearance(motion,o.Motion)))clear=false;
     }
     if(clear && !nav.HasCorridor(self,branch,candidate,look))clear=false;
     if(clear){wish=candidate;PassingSide=sign;Maneuver=AiManeuver.Pass;}
    }
    if(!clear){TrafficBlocked=true;wish=motion.Lateral;PassingSide=0;}
   }
   bool bypass=GraphRunwayLane(nav,self,speed,out float bypassLane);if(bypass)wish=bypassLane;
   // A free-running pair must not converge onto the same centre lane.
   // Plan optional-ramp avoidance before lateral travel consumes the available
   // straight; actual movement still uses ordinary steer/throttle/brake.
   float corridorLook=look,roomForTraffic=Mathf.Max(0,(float)ahead.halfWidth-2.6f);
   foreach(var jump in nav.Graph.jumps)if(!jump.mandatory&&jump.roadId==self.Route.Cursor.roadId){
    double remaining=jump.gapEnd-self.Route.Cursor.localPhysicalDistance;
    if(remaining>0&&remaining<=look+speed*(2*roomForTraffic/1.6f+.75f))corridorLook=Mathf.Max(corridorLook,(float)remaining+2);
   }
   if(!nav.HasCorridor(self,branch,wish,corridorLook)||nav.PredictedObstacle(world,self,branch,wish,.8f,speed*.8f,out _)){
    float selected=wish,best=float.PositiveInfinity;
    for(int candidateIndex=0;candidateIndex<4;candidateIndex++){
     float candidate=candidateIndex==0?0:candidateIndex==1?-roomForTraffic:candidateIndex==2?roomForTraffic:motion.Lateral;
     if(!nav.HasCorridor(self,branch,candidate,corridorLook)||nav.PredictedObstacle(world,self,branch,candidate,.8f,speed*.8f,out _))continue;
     float change=Mathf.Abs(candidate-motion.Lateral);if(change<best){best=change;selected=candidate;}
    }
    if(best<float.PositiveInfinity)wish=selected;
   }
   Lane=Mathf.MoveTowards(Lane,Mathf.Clamp(wish,-Mathf.Max(0,(float)ahead.halfWidth-2.6f),Mathf.Max(0,(float)ahead.halfWidth-2.6f)),Maneuver==AiManeuver.Pass?2.5f*dt:1.6f*dt);
   CorridorBlocked=!nav.HasCorridor(self,branch,Lane,look);
   Vector3 target=ahead.position.Float-ahead.right.Float*Lane;
   HeadingError=Vector3.SignedAngle(motion.Forward,Vector3.ProjectOnPlane(target-motion.Position,frame.normal.Float).normalized,frame.normal.Float);
   float steer=SteeringTo(motion,frame.normal.Float,target);
   float desired=BoostLimit,horizon=BoostLimit*BoostLimit/(2*Brake)+speed*.4f+80;
   SpeedLimitSource=AiSpeedLimitSource.CommonLimit;LimitingEntrant=-1;GeometryLimitingDistance=-1;
   for(float distance=0;distance<=horizon;distance+=5) {
    if(Mathf.Sqrt(2*Brake*.78f*Mathf.Max(0,distance-speed*.25f))>=desired)break;
    float limit=nav.LocalSpeed(self.Route.Cursor,branch,distance),reachable=Mathf.Sqrt(limit*limit+2*Brake*.78f*Mathf.Max(0,distance-speed*.25f));
    if(reachable<desired)GeometryLimitingDistance=distance;LimitSpeed(ref desired,reachable,AiSpeedLimitSource.Geometry);
   }
   if(bypass&&self.Route.Cursor.localPhysicalDistance<graphRunwayJump.rampStart){
    float shift=Mathf.Abs(graphRunwayLane-motion.Lateral);
    if(shift>.05f&&NeedsRunwayAlignment(nav,self,branch,graphRunwayJump))LimitSpeed(ref desired,Mathf.Max(0,(float)(graphRunwayJump.rampStart-self.Route.Cursor.localPhysicalDistance-1.75)/(shift/1.6f+.75f)),AiSpeedLimitSource.Corridor);
   }
   for(int i=0;i<world.Count;i++) {
    var other=world[i];if(i==id||other.Motion.Finished||!nav.PhysicalGap(self,other,branch,out float gap)||gap<=0)continue;
    float side=GraphSide(nav,self,other,branch,gap);
    if(Math.Abs(side-motion.Lateral)<PairClearance(motion,other.Motion)||Math.Abs(side-Lane)<PairClearance(motion,other.Motion)) {
     float limit=FollowingSpeed(Mathf.Max(0,other.Motion.Speed),gap);if(limit<desired){TrafficBlocked=true;LimitSpeed(ref desired,limit,AiSpeedLimitSource.Following,i);}
    }
   }
   GraphLaunchYield(world,self,speed,ref desired);
   GraphMerge(world,new GraphAiObservation(motion,self.Route,branch),ref desired);
   if(Math.Abs(HeadingError)>20)LimitSpeed(ref desired,Mathf.Max(10,speed-8),AiSpeedLimitSource.Heading);
   int obstacleFrom=0;
   while(nav.PredictedObstacle(world,self,branch,Lane,.8f,speed*.8f,out int obstacle,obstacleFrom)) {
    obstacleFrom=obstacle+1;
    var o=world[obstacle];float physical=Vector3.Dot(o.Motion.Position-motion.Position,frame.tangent.Float);
    LimitSpeed(ref desired,physical>0?FollowingSpeed(Mathf.Max(0,Vector3.Dot(o.Velocity,frame.tangent.Float)),physical):0,AiSpeedLimitSource.Traffic,obstacle);TrafficBlocked=true;
   }
   if(CorridorBlocked)LimitSpeed(ref desired,0,AiSpeedLimitSource.Corridor);
   DesiredSpeed=desired;NitroHazards=AiNitroHazard.None;
   if(!motion.Grounded)NitroHazards|=AiNitroHazard.Unsupported;if(motion.Recovering)NitroHazards|=AiNitroHazard.Recovery;
   if(CorridorBlocked)NitroHazards|=AiNitroHazard.Corridor;
   foreach(var jump in nav.Graph.jumps)if(jump.roadId==self.Route.Cursor.roadId&&self.Route.Cursor.localPhysicalDistance>=jump.rampStart-80&&self.Route.Cursor.localPhysicalDistance<=jump.ballisticLanding)NitroHazards|=AiNitroHazard.Unsupported;
   if(TrafficBlocked||MergeYieldTo>=0)NitroHazards|=AiNitroHazard.Traffic;if(Math.Abs(HeadingError)>8)NitroHazards|=AiNitroHazard.Heading;
   if(NeedsBraking(speed,desired))NitroHazards|=AiNitroHazard.Speed;
   bool canBoost=allowNitro&&NitroHazards==AiNitroHazard.None&&motion.Charge>balance["aiNitroReserve"]&&world.Time>=nextBoost;
   // Prove the ordinary acceleration/braking horizon before spending the shared
   // charge. Do not use nitro while entering a new arm or yielding at convergence.
   float travel=0,acceptedTravel=0,predicted=speed;double duration=0;
   if(canBoost)for(int sample=1;sample<=7;sample++) {
    float next=predicted+AccelEnvelope*.2f;travel+=(predicted+next)*.1f;predicted=next;
    var future=nav.At(self.Route.Cursor,branch,travel);float limit=GraphReachableSpeed(nav,future,branch,predicted);
    if(NeedsBraking(predicted,limit)||future.roadId!=self.Route.Cursor.roadId||Math.Abs(Lane-motion.Lateral)>.5 || nav.PredictedObstacle(world,self,branch,Lane,sample*.2f,travel,out _))break;
    duration=sample*.2;acceptedTravel=travel;
   }
   NitroCommand=canBoost&&duration>=.45&&acceptedTravel>=balance["aiNitroStraightDistance"];
   NitroReason=NitroCommand?"graph-safe-horizon":NitroHazards!=0?"graph-hazard":"graph-reserve-or-horizon";
   if(NitroCommand&&!boost){boost=true;Plans++;}else if(!NitroCommand&&boost){boost=false;nextBoost=world.Time+.25+personality*.35;}
   graphStopIntent=desired<=.1f;
   graphCommand=graphStopIntent?HoldAtBlockedCorridor(motion.Speed):new DrivingInput{throttle=1,brake=speed>desired+1?Mathf.Clamp01((speed-desired)/5):0,steer=steer,nitro=NitroCommand};
   if(graphStopIntent)graphCommand.steer=steer;return graphCommand;
  }
 }
}
