using System;
using System.Collections;
using UnityEngine;
namespace StarRacingPrototype {
 public sealed partial class TrackEnvironmentBuilder {
  IEnumerator BuildAuthoredCitySteps(TrackRoute route){
   var finishes=new[]{
    Lit(new Color(.92f,.97f,1f),.45f,texture:"EyeGlass",smoothness:.5f),
    Lit(new Color(.88f,.91f,.92f),.32f,texture:"Hull",smoothness:.32f),
    Lit(new Color(.92f,.85f,.70f),.08f,texture:"RoadCeramic",smoothness:.26f),
    Lit(new Color(.76f,.40f,.16f),.62f,texture:"RailPanels",smoothness:.4f),
    Lit(new Color(.13f,.20f,.27f),.5f,texture:"RoadRibbed",smoothness:.35f),
    Lit(new Color(.25f,.76f,.89f),.1f,true),
    Lit(new Color(.38f,.65f,.80f),.48f,texture:"EyeGlass",smoothness:.55f),
    Lit(new Color(.23f,.43f,.28f),.02f,texture:"SpaceRock",smoothness:.12f)
   };
   var batches=new CityBatches(this,true);int index=0;
   foreach(var part in AuthoredCityArchitecture.Create()){
    if(!EnvironmentPlan.ClearOfRoute(route,part.center,part.size*.5f,EnvironmentPlan.CorridorMargin))throw new InvalidOperationException("Authored landmark overlaps road corridor: "+part.name+" at "+part.center);
    batches.Box(finishes[part.finish],part.center,part.size,part.tile);
    if(++index%16==0)yield return null;
   }
   yield return batches.FlushSteps();
  }
 }
}
