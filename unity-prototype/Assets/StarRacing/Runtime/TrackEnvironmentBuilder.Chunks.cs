using System.Collections.Generic;
using UnityEngine;
namespace StarRacingPrototype {
 public sealed partial class TrackEnvironmentBuilder {
  readonly List<(MeshRenderer renderer,bool enabled)> sectionSources=new List<(MeshRenderer,bool)>();
  bool RoadSections=>PartitionRoadVisuals&&Plan.theme=="cloud-city";
  void PublishRoadVisual(string name,Mesh visual,Material material){
   if(!RoadSections){meshes.Add(visual);AddRoadSection(name,visual,material,null);return;}
   try{foreach(var mesh in VisualMeshChunks.Create(visual)){meshes.Add(mesh);AddRoadSection(name,mesh,material,null);}}
   finally{Dispose(visual);}
  }
  void BuildShellSections(Transform roadRoot){
   if(!RoadSections)return;
   foreach(var source in roadRoot.GetComponentsInChildren<MeshFilter>()){
    if(source.sharedMesh==null||source.sharedMesh.name!=RoadVolumeMesh.MeshName)continue;
    var renderer=source.GetComponent<MeshRenderer>();if(renderer==null||!renderer.enabled)continue;
    // The environment must own its materials; TrackBuilder retains the original.
    var material=new Material(renderer.sharedMaterial);materials.Add(material);
    foreach(var mesh in VisualMeshChunks.Create(source.sharedMesh)){meshes.Add(mesh);AddRoadSection("Visual road shell",mesh,material,renderer);}
    sectionSources.Add((renderer,renderer.enabled));renderer.enabled=false;
   }
  }
  void AddRoadSection(string name,Mesh mesh,Material material,MeshRenderer source){
   var go=new GameObject(name);go.transform.SetParent(root.transform,false);
   go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
   if(source!=null){
    go.layer=source.gameObject.layer;go.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);go.transform.localScale=source.transform.lossyScale;
    renderer.shadowCastingMode=source.shadowCastingMode;renderer.receiveShadows=source.receiveShadows;
    renderer.lightProbeUsage=source.lightProbeUsage;renderer.reflectionProbeUsage=source.reflectionProbeUsage;
    renderer.renderingLayerMask=source.renderingLayerMask;renderer.motionVectorGenerationMode=source.motionVectorGenerationMode;
   }
  }
 }
}
