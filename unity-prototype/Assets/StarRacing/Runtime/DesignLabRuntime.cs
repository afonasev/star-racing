using System;
using System.IO;
using System.Collections;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
namespace StarRacingPrototype {
 public static class DesignLabRuntime {
  static LabLibrary current;
#if UNITY_EDITOR
  public static Func<LabLibrary> OverrideFactory;
#endif
  public static LabLibrary Current {get{
   if(current!=null)return current;
#if UNITY_EDITOR
   if(OverrideFactory!=null)return current=OverrideFactory();
   return current=new LabLibrary(Package(),new LabFileStore(Path.GetFullPath(Path.Combine(Application.dataPath,"../../design-lab"))),true);
#else
   return current=new LabLibrary(Package(),new LabFileStore(Path.Combine(Application.persistentDataPath,"design-lab")),false);
#endif
  }}
  static int revision;
  public static IEnumerator Prepare(){
   if(current!=null)yield break;
#if UNITY_EDITOR
   if(OverrideFactory!=null){current=OverrideFactory();yield break;}
   string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../design-lab"));bool author=true;
#else
   string path=Path.Combine(Application.persistentDataPath,"design-lab");bool author=false;
#endif
   string text=Resources.Load<TextAsset>("DesignLabCatalog")?.text??throw new FormatException("Не найден каталог лаборатории");
   int expected=revision;
   var task=Task.Run(()=>new LabLibrary(JsonConvert.DeserializeObject<LabBundle>(text),new LabFileStore(path),author));
   // Observe even a cancelled owner's task; workers never touch Unity or publish Current.
   _=task.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
   while(!task.IsCompleted)yield return null;
   if(expected!=revision)throw new OperationCanceledException("Каталог был перезагружен");
   if(current==null)current=task.GetAwaiter().GetResult();
  }
  public static LabBundle Package()=>JsonConvert.DeserializeObject<LabBundle>(Resources.Load<TextAsset>("DesignLabCatalog")?.text??throw new FormatException("Не найден каталог лаборатории"));
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  public static void Reload(){revision++;current=null;}
 }
}
