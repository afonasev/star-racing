using UnityEditor;

namespace StarRacingPrototype {
    // All source textures are retained verbatim; import is deterministic across worktrees/builds.
    public sealed class EnvironmentTextureImporter : AssetPostprocessor {
        void OnPreprocessTexture() {
            if (!assetPath.StartsWith("Assets/StarRacing/Resources/Environment/Textures/") ||
                !assetPath.EndsWith(".png")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.wrapMode = UnityEngine.TextureWrapMode.Repeat;
            importer.filterMode = UnityEngine.FilterMode.Trilinear;
            // Long cyclic blend atlases need metre-scale texel density at grazing angles.
            bool blendAtlas = assetPath.EndsWith("/RoadBlend.png") || assetPath.EndsWith("/RailBlend.png");
            importer.anisoLevel = blendAtlas ? 16 : 8;
            importer.mipmapEnabled = true;
            importer.isReadable = false;
            importer.maxTextureSize = blendAtlas ? 8192 : 1024;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
        }
    }
}
