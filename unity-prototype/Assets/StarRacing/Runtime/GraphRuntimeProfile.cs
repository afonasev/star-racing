using System;
using Unity.Profiling;
namespace StarRacingPrototype {
 internal static class GraphRuntimeProfile {
  // Totals are diagnostic only and never participate in gameplay decisions.
  internal static readonly bool SupportCountersEnabled=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_SUPPORT_COUNTERS")=="yes";
  internal static SupportCounterSnapshot SupportCounters;
  [Serializable] internal struct SupportCounterSnapshot {
   public long batches,raycasts,validRaycasts,indexedRaycasts,groupedRaycasts;
   public long inventoryBuilds,inventoryEntries,candidateBuilds,candidateEntries,groupBuilds,groupsBuilt;
   public long boundsRequests,boundsSnapshots,eligibilityRequests,eligibilitySnapshots;
   public long groupedCandidateSlots,groupTests,groupsRejected,groupCandidateSlotsSkipped,groupedChunkTests;
   public long chunkTests,chunkRejects,nativeTests,nativeHits,acceptedHits,normalInterpolations;
  }
  internal enum SupportProbe { Unspecified,GravityRoadBelow,RoadProtection,SuspensionWheel,VehicleSafePointCentre,VehicleSafePointWheel,ObserveCentre,ObserveSegment,ObserveWheel,ObserveFallback,CheckPoseWheel,CheckPoseBody,Count }
  [Serializable] internal struct SupportProbeCounters { public long rays,validRaycasts,indexedRaycasts,groupedRaycasts,outsideBatch,inBatchUnsupportedCollection,chunkTests,chunkRejects,nativeTests,nativeHits,acceptedHits,boundsRequests,boundsSnapshots,indexedBoundsRequests,indexedBoundsSnapshots,unindexedBoundsRequests,unindexedBoundsSnapshots; }
  internal static readonly SupportProbeCounters[] SupportProbes=new SupportProbeCounters[(int)SupportProbe.Count];
  [Serializable] internal struct SupportProbeReport { public string origin;public SupportProbeCounters counts; }
  internal static int ProbeIndex(SupportProbe probe)=>(uint)probe<(uint)SupportProbe.Count?(int)probe:0;
  internal static SupportProbeReport[] ProbeReports() {
   var rows=new SupportProbeReport[SupportProbes.Length];
   for(int i=0;i<rows.Length;i++)rows[i]=new SupportProbeReport{origin=((SupportProbe)i).ToString(),counts=SupportProbes[i]};
   return rows;
  }
  [Serializable] internal sealed class SupportCounterReport {
   public bool enabled;public int warmupTicks,sampleTicks,groupSize;
   public SupportCounterSnapshot counts;public SupportProbeReport[] probes;public string limitation;
  }
  internal static void ResetSupportCounters(){SupportCounters=default;Array.Clear(SupportProbes,0,SupportProbes.Length);}
  internal static void ValidateSupportCounters() {
   if(!SupportCountersEnabled)return;
   var c=SupportCounters;
   if(c.groupTests<=0||c.groupsRejected<=0||c.nativeHits<=0||c.boundsSnapshots<=0||
      c.groupedCandidateSlots!=c.groupedChunkTests+c.groupCandidateSlotsSkipped||
      c.groupsRejected>c.groupTests||c.groupedRaycasts>c.indexedRaycasts||c.indexedRaycasts>c.validRaycasts||c.validRaycasts>c.raycasts||
      c.inventoryBuilds>c.batches||c.candidateBuilds>c.indexedRaycasts||c.groupBuilds>c.groupedRaycasts||c.groupsBuilt>c.candidateEntries||
      c.boundsSnapshots>c.boundsRequests||c.eligibilitySnapshots>c.eligibilityRequests||c.chunkRejects>c.chunkTests||
      c.nativeTests>c.chunkTests-c.chunkRejects||c.nativeHits>c.nativeTests||c.acceptedHits>c.nativeHits||c.normalInterpolations>c.acceptedHits)
    throw new InvalidOperationException("Support counter accounting/window invalid");
   var sum=default(SupportProbeCounters);
   foreach(var row in SupportProbes) {
    sum.rays+=row.rays;
    sum.validRaycasts+=row.validRaycasts;
    sum.indexedRaycasts+=row.indexedRaycasts;
    sum.groupedRaycasts+=row.groupedRaycasts;
    sum.outsideBatch+=row.outsideBatch;
    sum.inBatchUnsupportedCollection+=row.inBatchUnsupportedCollection;
    sum.chunkTests+=row.chunkTests;
    sum.chunkRejects+=row.chunkRejects;
    sum.nativeTests+=row.nativeTests;
    sum.nativeHits+=row.nativeHits;
    sum.acceptedHits+=row.acceptedHits;
    sum.boundsRequests+=row.boundsRequests;
    sum.boundsSnapshots+=row.boundsSnapshots;
    sum.indexedBoundsRequests+=row.indexedBoundsRequests;
    sum.indexedBoundsSnapshots+=row.indexedBoundsSnapshots;
    sum.unindexedBoundsRequests+=row.unindexedBoundsRequests;
    sum.unindexedBoundsSnapshots+=row.unindexedBoundsSnapshots;
   }
   if(sum.rays!=c.raycasts||sum.validRaycasts!=c.validRaycasts||sum.indexedRaycasts!=c.indexedRaycasts||sum.groupedRaycasts!=c.groupedRaycasts||
      sum.outsideBatch+sum.inBatchUnsupportedCollection!=c.validRaycasts-c.indexedRaycasts||
      sum.chunkTests!=c.chunkTests||sum.chunkRejects!=c.chunkRejects||sum.nativeTests!=c.nativeTests||sum.nativeHits!=c.nativeHits||sum.acceptedHits!=c.acceptedHits||
      sum.boundsRequests!=c.boundsRequests||sum.boundsSnapshots!=c.boundsSnapshots||sum.indexedBoundsRequests+sum.unindexedBoundsRequests!=c.boundsRequests||
      sum.indexedBoundsSnapshots+sum.unindexedBoundsSnapshots!=c.boundsSnapshots)
    throw new InvalidOperationException("Support origin reconciliation invalid");

  }
  // Per-chunk detail perturbs CPU timings; opt in only for attribution.
  static readonly bool DetailedSupport=Environment.GetEnvironmentVariable("STAR_RACING_GRAPH_SUPPORT_DETAIL")=="yes";
  internal readonly struct DetailScope:IDisposable {
   readonly bool enabled;readonly ProfilerMarker.AutoScope scope;
   internal DetailScope(ProfilerMarker marker){enabled=DetailedSupport;scope=enabled?marker.Auto():default;}
   public void Dispose(){if(enabled)scope.Dispose();}
  }
  public static DetailScope Detail(ProfilerMarker marker)=>new DetailScope(marker);
  public static readonly ProfilerMarker Project=new ProfilerMarker(ProfilerCategory.Scripts,"StarRacing.Graph.Project");
  public static readonly ProfilerMarker Support=new ProfilerMarker(ProfilerCategory.Scripts,"StarRacing.Graph.Support");
  public static readonly ProfilerMarker Observe=new ProfilerMarker(ProfilerCategory.Scripts,"StarRacing.Graph.Observe");
  public static readonly ProfilerMarker Checkpoint=new ProfilerMarker(ProfilerCategory.Scripts,"StarRacing.Graph.Checkpoint");
  public static readonly ProfilerMarker HasCorridor=new ProfilerMarker(ProfilerCategory.Scripts,"StarRacing.Graph.HasCorridor");
  public static readonly ProfilerMarker PredictedObstacle=new ProfilerMarker(ProfilerCategory.Scripts,"StarRacing.Graph.PredictedObstacle");
  public static readonly ProfilerMarker ChoiceSeconds=new ProfilerMarker(ProfilerCategory.Scripts,"StarRacing.Graph.ChoiceSeconds");
  public static readonly ProfilerMarker NativePhysics=new ProfilerMarker(ProfilerCategory.Scripts,"StarRacing.Graph.NativePhysics");
  public static readonly ProfilerMarker SupportGroups=new ProfilerMarker(ProfilerCategory.Scripts,"StarRacing.Graph.SupportGroups");
  public static readonly ProfilerMarker SupportPrepare=new ProfilerMarker(ProfilerCategory.Scripts,"StarRacing.Graph.SupportPrepare");
  public static readonly ProfilerMarker SupportScan=new ProfilerMarker(ProfilerCategory.Scripts,"StarRacing.Graph.SupportScan");
  public static readonly ProfilerMarker SupportNative=new ProfilerMarker(ProfilerCategory.Scripts,"StarRacing.Graph.SupportNative");
  public static readonly ProfilerMarker SupportNormal=new ProfilerMarker(ProfilerCategory.Scripts,"StarRacing.Graph.SupportNormal");
  public static readonly ProfilerMarker CorridorProject=new ProfilerMarker(ProfilerCategory.Scripts,"StarRacing.Graph.CorridorProject");
  public static readonly ProfilerMarker SupportInventory=new ProfilerMarker(ProfilerCategory.Scripts,"StarRacing.Graph.SupportInventory");
  public static readonly ProfilerMarker SupportCandidates=new ProfilerMarker(ProfilerCategory.Scripts,"StarRacing.Graph.SupportCandidates");
  public static readonly ProfilerMarker SupportEligibility=new ProfilerMarker(ProfilerCategory.Scripts,"StarRacing.Graph.SupportEligibility");
  public static readonly ProfilerMarker SupportBoundsSnapshot=new ProfilerMarker(ProfilerCategory.Scripts,"StarRacing.Graph.SupportBoundsSnapshot");
  public static readonly ProfilerMarker AI=new ProfilerMarker(ProfilerCategory.Scripts,"StarRacing.Graph.AI");
 }
}
