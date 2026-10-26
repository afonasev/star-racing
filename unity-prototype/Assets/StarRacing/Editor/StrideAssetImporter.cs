using UnityEditor;
namespace StarRacingPrototype
{
    public sealed class StrideAssetImporter : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (assetPath != "Assets/StarRacing/Resources/Vehicle/Stride.fbx" &&
                assetPath != "Assets/StarRacing/Resources/Vehicle/Cloudline.fbx" &&
                assetPath != "Assets/StarRacing/Resources/Vehicle/CloudlineLOD1.fbx" &&
                assetPath != "Assets/StarRacing/Resources/Vehicle/CloudlineLOD2.fbx") return;
            var importer = (ModelImporter)assetImporter;
            importer.importAnimation = false;
            importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importNormals = ModelImporterNormals.Import;
            importer.globalScale = 1;
            importer.useFileScale = true;
        }
    }
}
