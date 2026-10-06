using UnityEngine;

namespace StarRacingPrototype {
 // Observes PhysX/session state only. Does not set input, pose, forces or rules.
 // Native fall/contact classification is an adapter requiring driving QA; it
 public sealed class NativeAudioVehicleObserver : MonoBehaviour {
  RaceAudioCoordinator coordinator;MagneticVehicle car;bool finished;
  public float SlipIntensity {get;private set;}
  public bool Falling => car!=null&&car.IsFalling;
  public void Bind(RaceAudioCoordinator owner,MagneticVehicle vehicle){
   coordinator=owner;car=vehicle;finished=false;SlipIntensity=0;
  }
  void LateUpdate(){
   if(coordinator==null||car==null||!coordinator.Running||coordinator.Generation!=coordinator.Director.RaceGeneration)return;
   var racer=coordinator.Director.Session.Racers[car.Seat];
   if(racer.Finished&&!finished)coordinator.Emit(RaceSound.Finish);finished=racer.Finished;
   SlipIntensity=car.DrivingIntent.SlipIntensity;
  }
 }
}
