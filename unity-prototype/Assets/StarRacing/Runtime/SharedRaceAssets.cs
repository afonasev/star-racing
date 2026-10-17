using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace StarRacingPrototype {
 // Imported sources only. Generated meshes/materials and all physics resources
 // belong to the current world, never to this session cache. Main-thread API.
 public static class SharedRaceAssets {
  sealed class Entry {public UnityEngine.Object Asset;public ResourceRequest Pending;}
  readonly struct Source {
   public readonly Type Type;public readonly string Path;
   public Source(Type type,string path){Type=type;Path=path;}
  }
  static readonly Dictionary<(Type,string),Entry> entries=new Dictionary<(Type,string),Entry>();
  static readonly Source[] sources=Manifest();
  static int users,epoch,cursor;
  internal static int RetainedCount=>entries.Count;
  internal static int LoadCount {get;private set;}
  internal static bool Warm=>cursor==sources.Length;
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void Reset(){epoch++;users=0;Clear();}
  static void Clear(){entries.Clear();cursor=0;LoadCount=0;}
  public static IDisposable Acquire(){users++;return new Lease(epoch);}
  sealed class Lease:IDisposable {
   readonly int generation;bool disposed;
   public Lease(int generation){this.generation=generation;}
   public void Dispose(){if(disposed)return;disposed=true;if(generation!=epoch)return;if(--users==0)Clear();}
  }
  static Entry Find(Type type,string path){var key=(type,path);if(!entries.TryGetValue(key,out var entry))entries[key]=entry=new Entry();return entry;}
  public static T Load<T>(string path) where T:UnityEngine.Object {
   if(users==0)return Resources.Load<T>(path);
   return (T)Read(typeof(T),path);
  }
  static UnityEngine.Object Read(Type type,string path){
   var entry=Find(type,path);if(entry.Asset!=null)return entry.Asset;
   // Explicit synchronous tooling can finish the SAME request by reading asset.
   // Async callers run EnsureSteps before constructors; they never block here.
   if(entry.Pending!=null){entry.Asset=entry.Pending.asset;entry.Pending=null;}
   else {LoadCount++;entry.Asset=Resources.Load(path,type);}
   return entry.Asset;
  }
  public static bool TickPrewarm(){
   if(users==0)return false;
   while(cursor<sources.Length){
    var source=sources[cursor];var entry=Find(source.Type,source.Path);
    if(entry.Asset!=null){cursor++;continue;}
    if(entry.Pending==null){LoadCount++;entry.Pending=Resources.LoadAsync(source.Path,source.Type);return true;}
    if(!entry.Pending.isDone)return true;
    entry.Asset=entry.Pending.asset;entry.Pending=null;
    if(entry.Asset==null)throw new InvalidOperationException("Missing shared race asset: "+source.Path);
    cursor++;
   }
   return false;
  }
  public static IEnumerator EnsureSteps(bool background){
   if(background){while(TickPrewarm())yield return null;}
   else {foreach(var source in sources)if(Read(source.Type,source.Path)==null)throw new InvalidOperationException("Missing shared race asset: "+source.Path);cursor=sources.Length;}
  }
  static Source[] Manifest(){
   var result=new List<Source>();
   void Add<T>(string path) where T:UnityEngine.Object=>result.Add(new Source(typeof(T),path));
   Add<GameObject>("Vehicle/Cloudline");Add<Material>("Vehicle/CloudlineExhaust");Add<TextAsset>("Vehicle/CloudlineContactProfile");
   foreach(var name in new[]{"Brake_Ceramic","Cyan_Energy","Dark_Forged_Rim","Thin_Red_Taillight","Satin_Titanium","Graphite_Aero","Midnight_Glass","Liquid_Silver","Tire_Rubber","Titanium_Caliper"})Add<Material>("Vehicle/CloudlineMaterials/"+name);
   foreach(var name in new[]{"Road","Hull","Facade","RoadPanels","RoadCeramic","RoadRibbed","RoadBlend","RoadBlendFinish","RoadBlendNormal","RailPanels","RailCeramic","RailRibbed","RailBlend","RailBlendFinish","RailBlendNormal","SpaceHull","SpaceHullNormal","SpaceRock","SpaceRockNormal"})Add<Texture2D>("Environment/Textures/"+name);
   foreach(var name in new[]{"CloudSea","PlanetSky","PlanetIce","PlanetGas","PlanetRock"})Add<Texture2D>("Environment/Sky/"+name);
   foreach(var name in new[]{"DaylightReflection","SpaceReflection"})Add<Cubemap>("Environment/Sky/"+name);
   foreach(var name in new[]{"TrackSky","CloudDeck","CloudMist"})Add<Shader>("Environment/Sky/"+name);
   Add<Shader>("Environment/SpaceRock");
   Add<Shader>("Vehicle/DriftTireMarks");Add<Texture2D>("Vehicle/DriftRubber");
   return result.ToArray();
  }
 }
}
