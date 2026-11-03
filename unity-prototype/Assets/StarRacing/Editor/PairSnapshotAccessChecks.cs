using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Contact=StarRacingPrototype.RacePhysicsStepper.ContactVehicle;

namespace StarRacingPrototype {
 public static class PairSnapshotAccessChecks {
  [Serializable] sealed class CarData {public Vector3 velocity,center;public float radius;public int seat;public bool eligible;}
  [Serializable] sealed class SampleData {public CarData[] cars;public float remaining,limit;}
  [Serializable] sealed class Inputs {public SampleData[] samples;}
  sealed class Sample {public Contact[] cars;public List<Contact> original;public float remaining,limit;}
  [Serializable] sealed class Timing {
   public string group;public int samples,rounds=9,repetitions=128;public double[] candidateMs,originalMs,combinedCandidateMs,combinedOriginalMs;
   public double reduction,combinedReduction;public long candidateAllocations,originalAllocations;public bool utility;
  }
  [Serializable] sealed class Report {public string baseline="630b06d2e64710333eebdbfe7758be654d3e9621",limitation="Exclusive Editor evaluator and snapshot-fill utility only, not Player FPS or native physics cost.";public bool parity,utility;public int realSnapshots,legacySyntheticCases,extraSyntheticCases;public Timing[] timings;}
  static readonly List<Sample> real=new List<Sample>(),large=new List<Sample>();
  static int legacy,extra;static float sink;
  static Contact[] buffer=new Contact[128];static List<Contact> originalBuffer=new List<Contact>(128);
  static string Output=>Environment.GetEnvironmentVariable("STAR_RACING_PAIR_ACCESS_DIR");
  static void Check(bool ok,string message){if(!ok)throw new Exception("Pair snapshot access: "+message);}
  static Sample Make(Contact[] cars,float remaining=.00125f,float limit=.00125f)=>new Sample{cars=cars,original=new List<Contact>(cars),remaining=remaining,limit=limit};
  static void Equal(Sample s) {
   float expected=RacePhysicsStepper.EditorOriginalPairLimit(s.original,s.remaining,s.limit,out int a,out int b);
   float actual=RacePhysicsStepper.EditorPairLimit(s.cars,s.cars.Length,s.remaining,s.limit,out int x,out int y);
   Check(BitConverter.SingleToInt32Bits(expected)==BitConverter.SingleToInt32Bits(actual)&&a==x&&b==y,$"float bits/seats count={s.cars.Length} {expected:R}/{actual:R} seats {a},{b}/{x},{y}");
   // A reused buffer must ignore stale trailing entries at every partial count.
   Array.Copy(s.cars,buffer,s.cars.Length);
   actual=RacePhysicsStepper.EditorPairLimit(buffer,s.cars.Length,s.remaining,s.limit,out x,out y);
   Check(BitConverter.SingleToInt32Bits(expected)==BitConverter.SingleToInt32Bits(actual)&&a==x&&b==y,"partial reused buffer");
  }
  static float Adjacent(float x,int offset)=>BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(x)+offset);
  static void Synthetic(){
   var random=new System.Random(211933141);
   void Legacy(List<Contact> cars,float remaining,float limit){Equal(Make(cars.ToArray(),remaining,limit));legacy++;}
   // Unchanged 2888-case boundary/random coverage from historical PairStepMinimumChecks.
   foreach(float speed in new[]{0f,12f,Adjacent(12f,1),20f,200f,float.PositiveInfinity,float.NaN})
   foreach(float distance in new[]{0f,1e-6f,.1f,10f,float.PositiveInfinity,float.NaN})
   foreach(float limit in new[]{.00125f,.00001f,.02f/speed,Adjacent(.02f/speed,-1),Adjacent(.02f/speed,1),0f,BitConverter.Int32BitsToSingle(int.MinValue),-1f,float.PositiveInfinity,float.NaN}){
    var cars=new List<Contact>{new Contact(Vector3.zero,Vector3.zero,.5f,7),new Contact(new Vector3(-speed,0,0),new Vector3(distance,0,0),.5f,3),new Contact(new Vector3(speed,0,0),new Vector3(-distance,0,0),.5f,11)};
    Legacy(cars,.00125f,limit);cars.Reverse();Legacy(cars,.00125f,limit);
   }
   for(int n=0;n<2048;n++){
    var cars=new List<Contact>();for(int i=0;i<n%33;i++){
     Vector3 V(float scale)=>new Vector3((float)random.NextDouble()*scale,(float)random.NextDouble()*scale,(float)random.NextDouble()*scale);
     cars.Add(new Contact(V(200)-Vector3.one*100,V(5),.2f+(float)random.NextDouble(),i,n%7!=i%7));
    }Legacy(cars,.00125f,n%2==0?.00125f:.00001f);
   }
   Check(legacy==2888,"legacy coverage count");
   for(int n=0;n<2048;n++){
    int count=n<8?new[]{0,1,2,63,64,65,96,128}[n]:64;
    var cars=new Contact[count];
    for(int i=0;i<count;i++) {
     Vector3 V(float scale)=>new Vector3((float)random.NextDouble()*scale,(float)random.NextDouble()*scale,(float)random.NextDouble()*scale);
     cars[i]=new Contact(V(200)-Vector3.one*100,V(n%3==0?200:5),.2f+(float)random.NextDouble(),count-i,n%7!=i%7);
    }
    var s=Make(cars,.00125f,n%2==0?.00125f:.00001f);Equal(s);extra++;
    if(n>=8&&large.Count<32)large.Add(s);
    var reversed=(Contact[])cars.Clone();Array.Reverse(reversed);Equal(Make(reversed,s.remaining,s.limit));extra++;
   }
   // Explicit equal winners preserve iteration order with arbitrary seat order at 64.
   var tied=new Contact[64];for(int i=0;i<64;i++)tied[i]=new Contact(new Vector3(i%2==0?100:-100,0,0),Vector3.zero,2,97-i);
   foreach(float limit in new[]{.00125f,.0001f,Adjacent(.0001f,-1),Adjacent(.0001f,1)}){Equal(Make(tied,.00125f,limit));extra++;}
  }
  static double Measure(List<Sample> samples,bool candidate,bool combined,int repetitions,out long allocations){
   long allocated=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();float value=0;int seats=0;
   for(int n=0;n<repetitions;n++)for(int i=0;i<samples.Count;i++){
    var s=samples[i];int a,b;float result;
    if(candidate){
     var cars=s.cars;
     if(combined){for(int j=0;j<cars.Length;j++)buffer[j]=cars[j];cars=buffer;}
     result=RacePhysicsStepper.EditorPairLimit(cars,s.cars.Length,s.remaining,s.limit,out a,out b);
    }else{
     var cars=s.original;
     if(combined){originalBuffer.Clear();for(int j=0;j<s.cars.Length;j++)originalBuffer.Add(s.cars[j]);cars=originalBuffer;}
     result=RacePhysicsStepper.EditorOriginalPairLimit(cars,s.remaining,s.limit,out a,out b);
    }
    value+=result;seats^=a^b;
   }
   double ms=(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency;allocations=GC.GetAllocatedBytesForCurrentThread()-allocated;sink=value+seats;return ms;
  }
  static double Median(double[] values){var x=(double[])values.Clone();Array.Sort(x);return x[x.Length/2];}
  static Timing Benchmark(string name,List<Sample> samples){
   foreach(var s in samples)Equal(s);
   var r=new Timing{group=name,samples=samples.Count,candidateMs=new double[9],originalMs=new double[9],combinedCandidateMs=new double[9],combinedOriginalMs=new double[9]};
   foreach(bool combined in new[]{false,true}){
    Measure(samples,true,combined,16,out _);Measure(samples,false,combined,16,out _);
    for(int i=0;i<9;i++){
     long ca,oa;double c,o;
     if(i%2==0){c=Measure(samples,true,combined,128,out ca);o=Measure(samples,false,combined,128,out oa);}
     else{o=Measure(samples,false,combined,128,out oa);c=Measure(samples,true,combined,128,out ca);}
     (combined?r.combinedCandidateMs:r.candidateMs)[i]=c;(combined?r.combinedOriginalMs:r.originalMs)[i]=o;r.candidateAllocations+=ca;r.originalAllocations+=oa;
    }
   }
   r.reduction=1-Median(r.candidateMs)/Median(r.originalMs);r.combinedReduction=1-Median(r.combinedCandidateMs)/Median(r.combinedOriginalMs);
   r.utility=r.reduction>=.2&&r.combinedReduction>=.2&&r.candidateAllocations==0&&r.originalAllocations==0;return r;
  }
  public static void Run(){
   Check(!string.IsNullOrEmpty(Output),"output required");Directory.CreateDirectory(Output);
   try{
    real.Clear();large.Clear();legacy=extra=0;
    var input=JsonUtility.FromJson<Inputs>(File.ReadAllText(Environment.GetEnvironmentVariable("STAR_RACING_PAIR_INPUTS")));
    foreach(var d in input.samples){var cars=new Contact[d.cars.Length];for(int i=0;i<cars.Length;i++){var c=d.cars[i];cars[i]=new Contact(c.velocity,c.center,c.radius,c.seat,c.eligible);}var s=Make(cars,d.remaining,d.limit);Equal(s);real.Add(s);}
    Check(real.Count==227,"original real snapshots count");Synthetic();
    var report=new Report{parity=true,realSnapshots=real.Count,legacySyntheticCases=legacy,extraSyntheticCases=extra};
    File.WriteAllText(Path.Combine(Output,"parity.json"),JsonUtility.ToJson(report,true));
    report.timings=new[]{Benchmark("historical-real",real),Benchmark("synthetic64",large)};
    report.utility=report.timings[0].utility&&report.timings[1].utility;
    File.WriteAllText(Path.Combine(Output,"utility.json"),JsonUtility.ToJson(report,true));
    UnityEngine.Debug.Log("PAIR_SNAPSHOT_PARITY_OK real="+real.Count+" legacy="+legacy+" extra="+extra);
    Check(report.utility,"technical20percent utility gate failed; no Player after STOP");
    UnityEngine.Debug.Log("PAIR_SNAPSHOT_UTILITY_OK");
   }catch(Exception e){File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());throw;}
  }
 }
}
