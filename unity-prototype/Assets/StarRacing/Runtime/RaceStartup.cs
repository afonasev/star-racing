using System.Collections;
using UnityEngine;
namespace StarRacingPrototype {
 // Scene entry point: keep the first rendered frame free of race construction.
 public sealed class RaceStartup : MonoBehaviour {
  RaceLoading loading;
  void Awake(){loading=gameObject.AddComponent<RaceLoading>();}
  IEnumerator Start(){
   yield return RaceLoading.Present();
   gameObject.AddComponent<RaceDirector>();
   loading.enabled=false;Destroy(loading);Destroy(this);
  }
 }
}
