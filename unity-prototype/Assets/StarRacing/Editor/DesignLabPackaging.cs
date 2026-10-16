using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
namespace StarRacingPrototype {
 // Runs only during an explicitly requested build. Does not build or publish by itself.
 public sealed class DesignLabPackaging : IPreprocessBuildWithReport {
  public int callbackOrder=>-100;
  public void OnPreprocessBuild(BuildReport report)=>Export();
  public static void Export(){
   var store=new LabFileStore(Path.GetFullPath(Path.Combine(Application.dataPath,"../../design-lab")));
   var library=new LabLibrary(DesignLabRuntime.Package(),store,true);
   var bundle=library.Export();
   LabFileStore.Atomic(Path.Combine(Application.dataPath,"StarRacing/Resources/DesignLabCatalog.json"),JsonConvert.SerializeObject(bundle,Formatting.Indented));
   AssetDatabase.ImportAsset("Assets/StarRacing/Resources/DesignLabCatalog.json");
  }
 }
}
