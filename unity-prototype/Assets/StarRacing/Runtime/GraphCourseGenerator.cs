using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
namespace StarRacingPrototype.Procedural {
 public sealed class GraphCourseSettings {
  public readonly string theme,rails;public readonly bool jumps;
  public GraphCourseSettings(string theme="cloud-city",string rails="normal",bool jumps=true) {
   if(theme!="cloud-city"&&theme!="space-station"||rails!="normal"&&rails!="none"&&rails!="full")throw new ArgumentException("Unsupported graph settings");
   this.theme=theme;this.rails=rails;this.jumps=jumps;
  }
 }
 public sealed class GraphCourseDefinition {
  public const int Schema=1;public const string Family="path-choice";
  public readonly IndependentGraph graph;public readonly GraphCourseSettings settings;
  public readonly uint requestedSeed,actualSeed;public readonly bool usedFallback;public readonly int attempt,compositionProfile;
  // Geometry/settings identity; GraphRaceCourse composes actual gate content.
  public readonly string Identity;
  internal GraphCourseDefinition(IndependentGraph graph,GraphCourseSettings settings,uint requested,uint actual,bool fallback,int attempt):this(graph,settings,requested,actual,fallback,attempt,GraphCommonCatalog.Profile){}
  internal GraphCourseDefinition(IndependentGraph graph,GraphCourseSettings settings,uint requested,uint actual,bool fallback,int attempt,int profile) {
   this.graph=graph;this.settings=settings;requestedSeed=requested;actualSeed=actual;usedFallback=fallback;this.attempt=attempt;compositionProfile=profile;
   using(var buffer=new MemoryStream()) {
    using(var writer=new BinaryWriter(buffer,Encoding.UTF8,true)){writer.Write(Schema);writer.Write(IndependentGraph.NativeRevision);writer.Write(Family);writer.Write(compositionProfile);writer.Write(GraphCourseSafety.Profile);writer.Write(requested);writer.Write(actual);writer.Write(fallback);writer.Write(attempt);writer.Write(settings.theme);writer.Write(settings.rails);writer.Write(settings.jumps);writer.Write(graph.Identity);}
    using(var sha=SHA256.Create())Identity=BitConverter.ToString(sha.ComputeHash(buffer.ToArray())).Replace("-","").ToLowerInvariant();
   }
  }
 }
 public static class GraphCourseGenerator {
  public static IndependentGraph Candidate(uint seed,GraphCourseSettings settings) {
   if(settings==null)throw new ArgumentNullException(nameof(settings));
   // Keep the approved pair dimensions intact. This isolated profile varies
   // only rigid yaw; nativecatalog phrase selection remains anactivation gate.
   double angle=(seed%360)*Math.PI/180;var normal=new DVec(0,1,0);var tangent=new DVec(Math.Sin(angle),0,Math.Cos(angle));
   return GraphCommonCatalog.Create(new DVec(0,120,0),tangent,normal,DVec.Cross(tangent,normal).Unit(),settings.rails,settings.jumps);
  }
  public static GraphCourseDefinition Generate(uint requested,GraphCourseSettings settings)=>Select(requested,settings,false,seed=>Candidate(seed,settings));
  public static GraphCourseDefinition GenerateFallback(uint requested,GraphCourseSettings settings)=>Select(requested,settings,true,seed=>Candidate(seed,settings));
  internal static GraphCourseDefinition Select(uint requested,GraphCourseSettings settings,bool fallbackOnly,Func<uint,IndependentGraph> candidate)=>SelectProfile(requested,settings,fallbackOnly,candidate,null);
  internal static GraphCourseDefinition SelectProfile(uint requested,GraphCourseSettings settings,bool fallbackOnly,Func<uint,IndependentGraph> candidate,Func<uint,int> profile) {
   if(settings==null||candidate==null)throw new ArgumentNullException();string[] errors=Array.Empty<string>();
   for(int phase=fallbackOnly?1:0;phase<2;phase++) {
    uint basis=phase==0?(settings.theme=="space-station"?requested^0x5a710a1eu:requested):(settings.theme=="space-station"?0x51a7faceu^0x5a710a1eu:0x51a7faceu);
    for(int attempt=0;attempt<(phase==0?32:384);attempt++) {
     uint seed=unchecked(basis+(uint)attempt*0x9e3779b9u);var graph=candidate(seed);
     errors=GraphCourseSafety.Validate(graph);
     if(errors.Length!=0)continue;
     if(graph.guardrailMode!=settings.rails||graph.jumps.Count!=(settings.jumps?4:0))throw new InvalidOperationException("Candidate settings mismatch");
     return new GraphCourseDefinition(graph,settings,requested,seed,phase==1,attempt,profile==null?GraphCommonCatalog.Profile:profile(seed));
    }
   }
   throw new InvalidOperationException("No safe path-choice candidate: "+string.Join(",",errors.Take(8)));
  }
 }
}
