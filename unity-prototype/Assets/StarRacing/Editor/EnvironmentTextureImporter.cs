using UnityEditor;

namespace StarRacingPrototype {
    // All source textures are retained verbatim; import is deterministic across worktrees/builds.
    public sealed class EnvironmentTextureImporter : AssetPostprocessor {
        void OnPreprocessTexture() {
            if (!assetPath.StartsWith("Assets/StarRacing/Resources/Environment/Textures/") ||
                !assetPath.EndsWith(".png")) return;
            var importer = (TextureImporter)assetImporter;
            bool normal = assetPath.EndsWith("Normal.png");
            bool space = System.IO.Path.GetFileNameWithoutExtension(assetPath) is "SpaceHull" or "SpaceHullNormal" or "SpaceRock" or "SpaceRockNormal";
            // Preserve generated source detail (1254px); never silently downsample it to 1024.
            if (space) importer.npotScale = TextureImporterNPOTScale.None;
            bool finish = assetPath.EndsWith("Finish.png");
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.convertToNormalmap = space && normal;
            if (space) {
                importer.textureShape = TextureImporterShape.Texture2D;
                if (normal) importer.heightmapScale = .035f;
            }
            importer.sRGBTexture = !normal && !finish;
            importer.alphaSource = finish ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;
            importer.wrapMode = UnityEngine.TextureWrapMode.Repeat;
            importer.filterMode = UnityEngine.FilterMode.Trilinear;
            // Long cyclic blend atlases need metre-scale texel density at grazing angles.
            bool blendAtlas = assetPath.Contains("/RoadBlend") || assetPath.Contains("/RailBlend");
            importer.anisoLevel = blendAtlas ? 16 : 8;
            importer.mipmapEnabled = true;
            importer.isReadable = false;
            importer.maxTextureSize = space ? 2048 : normal || finish ? 4096 : blendAtlas ? 8192 : 1024;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
        }
    }
}
