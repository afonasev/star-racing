using System;
using System.Collections.Generic;
using UnityEngine;
namespace StarRacingPrototype {
 public enum DrivingContactKind { Vehicle, Barrier }
 public readonly struct DrivingContactEvent {
  public readonly DrivingContactKind Kind;
  public readonly int EntrantId,OtherId,Generation,PositionRevision,OtherRevision,Sequence;
  public readonly float Strength;
  public DrivingContactEvent(DrivingContactKind kind,DrivingContactObservation a,DrivingContactObservation b,int sequence,float strength){Kind=kind;EntrantId=a.Id;OtherId=b.Id;Generation=a.Generation;PositionRevision=a.Revision;OtherRevision=b.Revision;Sequence=sequence;Strength=strength;}
 }
 public struct DrivingContactObservation {
  public int Id,Generation,Revision,Span;
  public float Distance,Lateral,Heading,LeftClearance,RightClearance;
  public Vector3 Velocity,Right,Tangent,Normal;
  public bool Suppressed,LeftRail,RightRail;
 }
 // A semantic observer only: never applies separation, damping, forces or pose writes.
 public sealed class DrivingContactPolicy {
  sealed class Pair {public int ARevision,BRevision,Generation,LastContact=-2;public bool Audible;}
  readonly Dictionary<(int,int),Pair> pairs=new Dictionary<(int,int),Pair>();
  readonly Dictionary<int,DrivingContactObservation> current=new Dictionary<int,DrivingContactObservation>();
  readonly Dictionary<int,int> railSides=new Dictionary<int,int>();
  readonly List<(int,int)> expired=new List<(int,int)>();
  readonly float lateralDamping,longitudinalDamping;
  int step,sequence;
  public DrivingContactPolicy(float lateral=.72f,float longitudinal=.3f){lateralDamping=lateral;longitudinalDamping=longitudinal;}
  public void Reset(){pairs.Clear();current.Clear();railSides.Clear();step=sequence=0;}
  public void BeginStep(DrivingContactObservation[] observations){
   step++;var previous=new Dictionary<int,DrivingContactObservation>(current);current.Clear();
   foreach(var a in observations){
    if(current.ContainsKey(a.Id))throw new ArgumentException("Duplicate entrant identity");current.Add(a.Id,a);
    if(a.Suppressed||!previous.TryGetValue(a.Id,out var old)||old.Generation!=a.Generation||old.Revision!=a.Revision)railSides.Remove(a.Id);
    if(railSides.TryGetValue(a.Id,out int side)&&((side<0&&!a.LeftRail)||(side>0&&!a.RightRail)||side*a.Heading<-.03f||(side<0?a.LeftClearance:a.RightClearance)>.75f))railSides.Remove(a.Id);
   }
   expired.Clear();
   foreach(var item in pairs){
    var p=item.Value;
    if(!current.TryGetValue(item.Key.Item1,out var a)||!current.TryGetValue(item.Key.Item2,out var b)||!Compatible(a,b)||p.Generation!=a.Generation||p.ARevision!=a.Revision||p.BRevision!=b.Revision||HasGap(a,b))expired.Add(item.Key);
   }
   foreach(var key in expired)pairs.Remove(key);
  }
  public static bool HasGap(DrivingContactObservation a,DrivingContactObservation b)=>Mathf.Abs(b.Distance-a.Distance)>=VehicleGeometry.PairRearmDistance||Mathf.Abs(b.Lateral-a.Lateral)>=VehicleGeometry.PairRearmLateral;
  public static bool Compatible(DrivingContactObservation a,DrivingContactObservation b)=>!a.Suppressed&&!b.Suppressed&&a.Generation==b.Generation&&!(a.Span>=0&&b.Span>=0&&a.Span!=b.Span)&&Vector3.Dot(a.Normal,b.Normal)>0;
  public static float Score(DrivingContactObservation a,DrivingContactObservation b,float lateral=.72f,float longitudinal=.3f){
   float delta=b.Lateral-a.Lateral,direction=delta==0?(a.Id%2==0?1:-1):Mathf.Sign(delta);
   float closingLateral=Vector3.Dot(b.Velocity-a.Velocity,a.Right)*direction;
   float firstSpeed=Vector3.Dot(a.Velocity,a.Tangent),secondSpeed=Vector3.Dot(b.Velocity,a.Tangent);
   float along=b.Distance-a.Distance;
   float closingLongitudinal=along>0||(along==0&&firstSpeed>=secondSpeed)?firstSpeed-secondSpeed:secondSpeed-firstSpeed;
   return Mathf.Clamp01((Mathf.Max(0,-closingLateral)*lateral*.5f+Mathf.Max(0,closingLongitudinal)*longitudinal*.5f)/12f);
  }
  public bool Vehicle(int first,int second,out DrivingContactEvent result){
   result=default;if(first==second)return false;if(first>second){int swap=first;first=second;second=swap;}
   if(!current.TryGetValue(first,out var a)||!current.TryGetValue(second,out var b)||!Compatible(a,b)||HasGap(a,b))return false;
   var key=(first,second);
   if(!pairs.TryGetValue(key,out var p)){p=new Pair{ARevision=a.Revision,BRevision=b.Revision,Generation=a.Generation};pairs.Add(key,p);}
   bool onset=p.LastContact<step-1;p.LastContact=step;
   float strength=Score(a,b,lateralDamping,longitudinalDamping);
   if(!onset||p.Audible||strength<.18f)return false;
   p.Audible=true;result=new DrivingContactEvent(DrivingContactKind.Vehicle,a,b,++sequence,strength);return true;
  }
  public bool Barrier(int entrant,int side,out DrivingContactEvent result){
   result=default;if(!current.TryGetValue(entrant,out var a)||a.Suppressed||side==0||!(side<0?a.LeftRail:a.RightRail)||side*Vector3.Dot(a.Velocity,a.Right)<=0)return false;
   if(railSides.TryGetValue(entrant,out int previous)&&previous==side)return false;
   railSides[entrant]=side;var other=a;other.Id=-1;
   result=new DrivingContactEvent(DrivingContactKind.Barrier,a,other,++sequence,.25f+Mathf.Abs(Mathf.Sin(a.Heading))*.55f);return true;
  }
 }
}
