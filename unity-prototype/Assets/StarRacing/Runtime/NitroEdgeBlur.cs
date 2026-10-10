using UnityEngine;

namespace StarRacingPrototype {
 public static class NitroBlurSettings {
  public const string PreferenceKey="StarRacing.NitroBlur";
  static bool loaded,enabled;
  public static int Revision {get;private set;}
  public static bool Enabled {
   get {if(!loaded)Reload();return enabled;}
   set {
    if(Enabled==value)return;
    enabled=value;Revision++;
    PlayerPrefs.SetInt(PreferenceKey,value?1:0);PlayerPrefs.Save();
   }
  }
  public static void Reload(){enabled=PlayerPrefs.GetInt(PreferenceKey,1)!=0;loaded=true;Revision++;}
 }
 // Read the accepted feedback snapshot after the vehicle has published it.
 [DefaultExecutionOrder(200)]
 [RequireComponent(typeof(Camera), typeof(ChaseCamera))]
 public sealed class NitroEdgeBlur : MonoBehaviour {
  public RaceDirector director;
  readonly NitroBlurEnvelope envelope=new NitroBlurEnvelope();
  ChaseCamera follow;
  MagneticVehicle previousTarget;
  int epoch=-1;
  int settingsRevision=-1;
  bool Suppressed(MagneticVehicle car)=>car==null || Time.timeScale<=0 || car.IsFalling || car.FinishedCoasting ||
   (follow!=null && follow.FinishLocked) || (director!=null && (!director.Started || director.Paused));
  public float Strength {
   get {
    if(!isActiveAndEnabled || !NitroBlurSettings.Enabled || settingsRevision!=NitroBlurSettings.Revision)return 0;
    if(follow==null)follow=GetComponent<ChaseCamera>();
    var car=follow.target;
    return Suppressed(car) || car!=previousTarget || car.PresentationEpoch!=epoch ? 0 : envelope.Strength;
   }
  }
  void LateUpdate()=>UpdateEffect(Time.unscaledDeltaTime);
  void UpdateEffect(float dt){
   if(!NitroBlurSettings.Enabled){ResetEffect();return;}
   if(settingsRevision!=NitroBlurSettings.Revision){envelope.Reset();settingsRevision=NitroBlurSettings.Revision;}
   if(follow==null)follow=GetComponent<ChaseCamera>();
   var car=follow.target;
   if(Suppressed(car)){ResetEffect();return;}
   if(car!=previousTarget || car.PresentationEpoch!=epoch){envelope.Reset();previousTarget=car;epoch=car.PresentationEpoch;}
   envelope.Step(car.Telemetry.speedKmh,car.NitroFeedbackActive,dt);
  }
  public void ResetEffect(){envelope.Reset();previousTarget=null;epoch=-1;settingsRevision=-1;}
  void OnDisable()=>ResetEffect();
 }

 public sealed class NitroBlurEnvelope {
  public float Strength {get;private set;}
  public static float Target(float speedKmh,bool acceptedNitro){
   if(!acceptedNitro)return 0;
   float t=Mathf.Clamp01((speedKmh-300)/150);
   return t*t*(3-2*t);
  }
  public void Step(float speedKmh,bool acceptedNitro,float dt){
   float target=Target(speedKmh,acceptedNitro);
   float time=target>Strength?.12f:.18f;
   Strength=Mathf.Lerp(Strength,target,1-Mathf.Exp(-Mathf.Max(0,dt)/time));
   if(target==0 && Strength<.0001f)Strength=0;
  }
  public void Reset()=>Strength=0;
 }
}
