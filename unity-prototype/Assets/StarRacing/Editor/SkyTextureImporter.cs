using UnityEditor;
using UnityEngine;
namespace StarRacingPrototype {
 public sealed class SkyTextureImporter : AssetPostprocessor {
  void OnPreprocessTexture() {
   if(!assetPath.StartsWith("Assets/StarRacing/Resources/Environment/Sky/") || !assetPath.EndsWith(".png")) return;
   var t=(TextureImporter)assetImporter;bool planet=System.IO.Path.GetFileName(assetPath).StartsWith("Planet");
   t.textureType=TextureImporterType.Default;t.sRGBTexture=true;t.alphaSource=planet?TextureImporterAlphaSource.FromInput:TextureImporterAlphaSource.None;
   t.alphaIsTransparency=planet;t.wrapMode=planet?TextureWrapMode.Clamp:TextureWrapMode.Mirror;
   t.filterMode=FilterMode.Trilinear;t.mipmapEnabled=true;t.anisoLevel=planet?1:4;t.maxTextureSize=2048;t.isReadable=false;t.textureCompression=TextureImporterCompression.CompressedHQ;
  }
 }
}
