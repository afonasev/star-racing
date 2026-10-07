using UnityEditor;
namespace StarRacingPrototype
{
    public sealed class StrideAssetImporter : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (assetPath != "Assets/StarRacing/Resources/Vehicle/Stride.fbx") return;
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
