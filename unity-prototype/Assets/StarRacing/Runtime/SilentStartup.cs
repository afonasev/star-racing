using System;
using UnityEngine;

namespace StarRacingPrototype {
 /// <summary>Applies the command-line silent session before any game scene can create audio.</summary>
 public static class SilentStartup {
  public const string Argument="-muted";
  public static bool Enabled {get;private set;}

  public static bool IsRequested(string[] args){
   if(args==null)return false;
   for(int i=0;i<args.Length;i++)if(args[i]==Argument||args[i]=="--muted"||args[i]=="-noaudio")return true;
   return false;
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetSession(){
   if(Enabled)AudioListener.pause=false;
   Enabled=false;
  }

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
  public static void Initialize(){
   Enabled=IsRequested(Environment.GetCommandLineArgs());
   if(Enabled)AudioListener.pause=true;
  }
 }
}
