using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
namespace StarRacingPrototype {
 public static class ApplicationIconChecks {
  public static void Run(){
   var previous=PlayerSettings.GetIcons(NamedBuildTarget.Standalone,IconKind.Any);
   try{
    var icon=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/StarRacing/Resources/Star-Racing-Icon.png");
    if(icon==null||icon.width!=1024||icon.height!=1024)throw new Exception("1024px application icon required");
    PrototypeBuilder.ConfigureApplicationIcon();var slots=PlayerSettings.GetIcons(NamedBuildTarget.Standalone,IconKind.Any);
    if(slots.Length==0||Array.Exists(slots,slot=>slot!=icon))throw new Exception("Application icon slots not assigned");
    Debug.Log("APPLICATION_ICON_CHECKS_OK slots="+slots.Length);
   }finally{PlayerSettings.SetIcons(NamedBuildTarget.Standalone,previous,IconKind.Any);}
  }
 }
}
