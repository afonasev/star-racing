using System;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
namespace StarRacingPrototype {
 // Bounded read-only probe at the existing callback boundary, with no native objects or extra event subscription.
 static class BasisContactBuffer {
  struct Row {public bool ccd,after;public int a,b,index;public Vector3 point,normal,target;public float separation,friction,bounce,maxImpulse;public ModifiableMassProperties mass;}
  static Row[] rows;static int count;static string output;
  public static void Begin(string path){rows=new Row[32768];count=0;output=path;RacePhysicsStepper.EditorContactDiagnostic=Capture;}
  static void Capture(ModifiableContactPair p,bool ccd,bool after){
   for(int i=0;i<p.contactCount;i++){int at=Interlocked.Increment(ref count)-1;if(at>=rows.Length)return;
    rows[at]=new Row{ccd=ccd,after=after,a=p.colliderInstanceID,b=p.otherColliderInstanceID,index=i,point=p.GetPoint(i),normal=p.GetNormal(i),separation=p.GetSeparation(i),friction=p.GetDynamicFriction(i),bounce=p.GetBounciness(i),target=p.GetTargetVelocity(i),maxImpulse=p.GetMaxImpulse(i),mass=p.massProperties};
   }
  }
  public static void Flush(int tick){if(rows==null)return;int n=count;count=0;if(n>rows.Length)throw new Exception("Contact buffer overflow");
   if(tick>4)return;var s=new StringBuilder();s.AppendLine($"tick={tick} native={RacePhysicsStepper.NativeSteps} contacts={n}");
   for(int i=0;i<n;i++){var r=rows[i];s.AppendLine($"ccd={r.ccd} after={r.after} colliders={r.a},{r.b} i={r.index} point={r.point:G9} normal={r.normal:G9} separation={r.separation:R} friction={r.friction:R} bounce={r.bounce:R} target={r.target:G9} maxImpulse={r.maxImpulse:R} mass={r.mass.inverseMassScale:R},{r.mass.inverseInertiaScale:R},{r.mass.otherInverseMassScale:R},{r.mass.otherInverseInertiaScale:R}");}
   File.AppendAllText(Path.Combine(output,"contact-buffer.txt"),s.ToString());
  }
  public static void End(){RacePhysicsStepper.EditorContactDiagnostic=null;rows=null;count=0;}
 }
}
