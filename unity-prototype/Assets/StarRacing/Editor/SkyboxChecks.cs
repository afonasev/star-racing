using System;
using UnityEngine;
namespace StarRacingPrototype {
 public static class SkyboxChecks {
  static void Check(bool valid,string message){if(!valid)throw new Exception("SKYBOX: "+message);}
  public static void CheckAll(){PrototypeChecks.RunWithFixtureEquivalence();Run();EnvironmentTextureChecks.Run();}
  public static void BuildCheckedMac(){CheckAll();PrototypeBuilder.BuildMac();}
  static void CheckPlanetTexture(EnvironmentPlan plan,TrackRoute route){
   var holder=new GameObject("Planet variant check");TrackSky sky=null;
   try{sky=new TrackSky(plan,route,holder.transform);Check(sky.Material.GetTexture("_PlanetTex")==Resources.Load<Texture2D>("Environment/Sky/"+plan.SkyPlanetTexture),"selected planet texture");}
   finally{sky?.Clear();UnityEngine.Object.DestroyImmediate(holder);}
  }
  public static void Run(){
   var previous=RenderSettings.skybox;var root=new GameObject("Skybox contract checks");
   TrackEnvironmentBuilder active=null,prepared=null;
   try {
    foreach(var name in new[]{"TrackSky","CloudDeck","CloudMist"}) {var shader=Resources.Load<Shader>("Environment/Sky/"+name);Check(shader!=null&&!UnityEditor.ShaderUtil.ShaderHasError(shader),"shader compile "+name);}
    foreach(var name in new[]{"PlanetSky","PlanetGas","PlanetRock","PlanetIce","CloudSea"}) {var texture=Resources.Load<Texture2D>("Environment/Sky/"+name);Check(texture!=null&&texture.mipmapCount>1,"asset "+name);}
    var variants=new System.Collections.Generic.HashSet<string>();
    for(uint seed=77;seed<=80;seed++){
     var route=new TrackRoute(Procedural.Generator.Generate(seed,"normal","space-station",false,true));
     var plan=EnvironmentPlan.Create(route,seed);var repeat=EnvironmentPlan.Create(route,seed);
     Check(plan.hash==repeat.hash&&plan.SkyPlanetTexture==repeat.SkyPlanetTexture,"repeat seed changed planet");
     variants.Add(plan.SkyPlanetTexture);
     CheckPlanetTexture(plan,route);
    }
    Check(variants.Count==EnvironmentPlan.SkyPlanetVariantCount,"consecutive seeds must show distinct planets");
    foreach(var theme in new[]{"cloud-city","space-station","cloud-city"}) {
     var track=root.AddComponent<TrackBuilder>();track.Build(new TrackRoute(Procedural.Generator.Generate(77,"normal",theme,false,true)));
     var before=RenderSettings.skybox;prepared=new TrackEnvironmentBuilder();prepared.Build(track.Route,root.transform);
     Check(RenderSettings.skybox==before,"staging changed global sky");
     Check(prepared.Root.Find("Distant planet")==null&&prepared.Root.Find("Star")==null,"duplicate far geometry");
     prepared.Sky.Activate();var current=RenderSettings.skybox;Check(current!=null&&current==prepared.Sky.Material,"activation");
     active?.Clear();Check(RenderSettings.skybox==current,"old cleanup erased new sky");active=prepared;prepared=null;
     if(theme=="cloud-city") {var deck=active.Root.Find("Cloud sea");Check(deck!=null&&deck.GetComponent<Collider>()==null,"render only deck");foreach(var sample in track.Route.Samples)Check(sample.position.y-deck.position.y>=214.9f,"road clearance");Check(active.Plan.lighting.kind=="day","sunny city");}
    }
    active.Clear();active=null;Check(RenderSettings.skybox==null,"active cleanup");
    Debug.Log("SKYBOX_CHECKS_OK staging activation disposal city-space-city clearance assets");
   } finally {prepared?.Clear();active?.Clear();UnityEngine.Object.DestroyImmediate(root);RenderSettings.skybox=previous;}
  }
 }
}
