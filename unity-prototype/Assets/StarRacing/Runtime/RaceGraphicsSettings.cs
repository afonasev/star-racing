using System;
using UnityEngine;
namespace StarRacingPrototype {
 [Serializable] public sealed class RaceGraphicsSettings {
  public const string PreferenceKey="StarRacing.Graphics.v1";
  public int version=1;
  public bool contactShadows=true,shadows=true,bloom=true,mist=true;
  public static RaceGraphicsSettings Parse(string json){var value=new RaceGraphicsSettings();try{if(!string.IsNullOrWhiteSpace(json))JsonUtility.FromJsonOverwrite(json,value);}catch(ArgumentException){return new RaceGraphicsSettings();}return value.version==1?value:new RaceGraphicsSettings();}
  public static RaceGraphicsSettings Load()=>Parse(PlayerPrefs.GetString(PreferenceKey,""));
  public void Save(){PlayerPrefs.SetString(PreferenceKey,JsonUtility.ToJson(this));PlayerPrefs.Save();}
 }
}
