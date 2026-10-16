using System;
using System.IO;
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
  public static LabBundle Package()=>JsonConvert.DeserializeObject<LabBundle>(Resources.Load<TextAsset>("DesignLabCatalog")?.text??throw new FormatException("Не найден каталог лаборатории"));
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  public static void Reload(){current=null;}
 }
}
