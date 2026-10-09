using System;
using System.Collections.Generic;
using UnityEngine;

namespace StarRacingPrototype {
 [Serializable]
 public sealed class DisplaySettings {
  public const string PreferenceKey="StarRacing.DisplaySettings.v1";
  public bool fullscreen=true;
  public int width,height;
  public static DisplaySettings Load(){
   var value=new DisplaySettings();
   try{if(PlayerPrefs.HasKey(PreferenceKey))value=JsonUtility.FromJson<DisplaySettings>(PlayerPrefs.GetString(PreferenceKey))??value;}catch(ArgumentException){}
   return value;
  }
  public static Vector2Int[] Options(Resolution[] supported,Vector2Int native){
   var sizes=new List<Vector2Int>();
   foreach(var r in supported){var size=new Vector2Int(r.width,r.height);if(size.x>0&&size.y>0&&!sizes.Contains(size))sizes.Add(size);}
   if(native.x>0&&native.y>0&&!sizes.Contains(native))sizes.Add(native);
   sizes.Sort((a,b)=>a.x==b.x?a.y.CompareTo(b.y):a.x.CompareTo(b.x));
   sizes.Insert(0,Vector2Int.zero);return sizes.ToArray();
  }
  public int Selected(Vector2Int[] options){return Array.IndexOf(options,new Vector2Int(width,height));}
  public void Validate(Vector2Int[] options){if(Selected(options)<0){width=0;height=0;}}
  public void Save(){PlayerPrefs.SetString(PreferenceKey,JsonUtility.ToJson(this));PlayerPrefs.Save();}
  public void Apply(Vector2Int native){
   // The Editor renders the same menu but must never change the host display.
   if(Application.isEditor)return;
   int w=width>0?width:native.x,h=height>0?height:native.y;
   Screen.SetResolution(Mathf.Max(1,w),Mathf.Max(1,h),fullscreen?FullScreenMode.FullScreenWindow:FullScreenMode.Windowed);
  }
  public static Vector2Int NativeSize(){var r=Screen.currentResolution;return new Vector2Int(r.width>0?r.width:Screen.width,r.height>0?r.height:Screen.height);}
 }
}
