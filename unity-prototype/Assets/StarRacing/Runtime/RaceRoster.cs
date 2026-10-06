using System;
using UnityEngine;
namespace StarRacingPrototype {
 public enum DriverProfile { Human, Rookie, Racer, Ace }
 public sealed class RaceRoster {
  public sealed class Entrant {
   public readonly int Id, HumanSeat; public readonly DriverProfile Profile; public readonly uint Seed;
   public int GridSlot {get;internal set;}
   public string Name => HumanSeat>=0?"PLAYER "+(HumanSeat+1):Profile.ToString().ToUpperInvariant()+" "+(Id+1);
   public Entrant(int id,int seat,DriverProfile profile,uint seed){Id=id;HumanSeat=seat;Profile=profile;Seed=seed;}
  }
  public readonly Entrant[] Entrants;
  public uint GridSeed {get;private set;}
  public RaceRoster(int count,int humans,uint seed) {
   if(humans<1||humans>4||count<humans||count>64)throw new ArgumentOutOfRangeException(nameof(count));
   Entrants=new Entrant[count];uint random=seed;int n=count-humans;
   var quotas=new[]{n*.25f,n*.5f,n*.25f};var amounts=new[]{n/4,n/2,n/4};
   int left=n-amounts[0]-amounts[1]-amounts[2];
   while(left-->0){float best=-1;int chosen=0;for(int i=0;i<3;i++){float f=quotas[i]-amounts[i];if(f>best+.0001f||(Math.Abs(f-best)<.0001f&&(Next(ref random)&1)==0)){best=f;chosen=i;}}amounts[chosen]++;}
   var profiles=new DriverProfile[n];int k=0;for(int i=0;i<3;i++)for(int j=0;j<amounts[i];j++)profiles[k++]=(DriverProfile)(i+1);
   for(int i=n-1;i>0;i--){int j=(int)(Next(ref random)%(uint)(i+1));var p=profiles[i];profiles[i]=profiles[j];profiles[j]=p;}
   for(int i=0;i<count;i++)Entrants[i]=new Entrant(i,i<humans?i:-1,i<humans?DriverProfile.Human:profiles[i-humans],Next(ref random));
   Shuffle(seed^0xa51237u);
  }
  public void Shuffle(uint seed){GridSeed=seed;uint random=seed;var slots=new int[Entrants.Length];for(int i=0;i<slots.Length;i++)slots[i]=i;for(int i=slots.Length-1;i>0;i--){int j=(int)(Next(ref random)%(uint)(i+1));int t=slots[i];slots[i]=slots[j];slots[j]=t;}for(int i=0;i<slots.Length;i++)Entrants[i].GridSlot=slots[i];}
  public static uint Next(ref uint state){if(state==0)state=0x6d2b79f5;state^=state<<13;state^=state>>17;state^=state<<5;return state;}
  public static float GridDistance(TrackRoute route,int slot)=>route.StartDistance+(slot/2)*10;
  public static float GridLateral(TrackRoute route,int slot) {
   float distance=GridDistance(route,slot),wish=(slot%2==0?-2.8f:2.8f),best=0,error=float.PositiveInfinity;
   foreach(var span in route.PavedAt(distance)){
    if(span.halfWidth<VehicleGeometry.HalfWidth+.08f)continue;
    float center=-(float)span.offset,room=(float)span.halfWidth-VehicleGeometry.HalfWidth-.08f;
    float lane=Mathf.Clamp(wish,center-room,center+room),delta=Mathf.Abs(lane-wish);
    if(delta<error){error=delta;best=lane;}
   }
   if(float.IsInfinity(error))throw new InvalidOperationException("No safe grid pavement at "+distance);
   for(float offset=-VehicleGeometry.HalfLength;offset<=VehicleGeometry.HalfLength+.001f;offset+=VehicleGeometry.HalfLength){bool safe=false;foreach(var s in route.PavedAt(distance+offset))if(Mathf.Abs(best+(float)s.offset)+VehicleGeometry.HalfWidth+.08f<s.halfWidth)safe=true;if(!safe)throw new InvalidOperationException("Grid footprint leaves pavement");}
   return best;
  }
 }
}
