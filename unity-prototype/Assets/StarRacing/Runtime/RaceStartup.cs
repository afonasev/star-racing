using System.Collections;
using UnityEngine;
namespace StarRacingPrototype {
 // Scene entry point. No heavy Awake runs before the first loading frame.
 public sealed class RaceStartup : MonoBehaviour {
  RaceLoading loading;LoadingWork work;
  void Awake(){loading=gameObject.AddComponent<RaceLoading>();}
  IEnumerator Start(){
   yield return RaceLoading.Present();
   var director=gameObject.AddComponent<RaceDirector>();
   using(work=director.InitializeForLoading()){
    while(work.MoveNext()){loading.Status=work.Status;yield return null;}
    if(!work.Succeeded||!director.Ready){if(work.Error!=null)Debug.LogException(work.Error);director.CancelPendingInitialization();loading.Status="Не удалось загрузить игру";yield break;}
   }
   loading.enabled=false;Destroy(loading);Destroy(this);
  }
  void OnDisable(){if(work?.IsPending==true){work.Dispose();GetComponent<RaceDirector>()?.CancelPendingInitialization();}}
  void OnDestroy(){work?.Dispose();}
 }
}
