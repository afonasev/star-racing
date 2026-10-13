using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace StarRacingPrototype {
    // Offline, camera-independent radiance. Unity's importer performs the glossy convolution.
    public static class DaylightReflectionBaker {
        public static void Run() {
            Bake("DaylightReflection", new Color(.32f,.49f,.73f), new Color(.68f,.75f,.82f), new Color(.16f,.19f,.23f));
            Bake("SpaceReflection", new Color(.035f,.065f,.12f), new Color(.24f,.32f,.43f), new Color(.025f,.035f,.06f));
            AssetDatabase.SaveAssets();
            Debug.Log("DAYLIGHT_REFLECTION_BAKE_OK linear=true glossyConvolution=true cameraIndependent=true");
        }
        static void Bake(string name, Color sky, Color horizon, Color ground) {
            const int width=512, height=256;
            var image=new Texture2D(width,height,TextureFormat.RGBAFloat,false,true);
            try {
                var pixels=new Color[width*height];
                for(int y=0;y<height;y++) {
                    float elevation=Mathf.Sin(((y+.5f)/height-.5f)*Mathf.PI);
                    Color color=elevation>=0?Color.Lerp(horizon,sky,Mathf.Sqrt(elevation)):Color.Lerp(horizon,ground,Mathf.Sqrt(-elevation));
                    for(int x=0;x<width;x++)pixels[y*width+x]=color;
                }
                image.SetPixels(pixels);image.Apply(false);
                string path="Assets/StarRacing/Resources/Environment/Sky/"+name+".exr";
                File.WriteAllBytes(path,image.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
                AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureShape=TextureImporterShape.TextureCube;
                importer.generateCubemap=TextureImporterGenerateCubemap.Cylindrical;
                importer.sRGBTexture=false;importer.mipmapEnabled=true;importer.isReadable=false;
                importer.maxTextureSize=128;importer.textureCompression=TextureImporterCompression.Uncompressed;
                importer.filterMode=FilterMode.Trilinear;importer.wrapMode=TextureWrapMode.Clamp;
                var settings=new TextureImporterSettings();importer.ReadTextureSettings(settings);
                settings.cubemapConvolution=TextureImporterCubemapConvolution.Specular;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
                var cube=AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                if(cube==null||cube.mipmapCount<7)throw new Exception("Reflection cube import failed: "+name);
            } finally {UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
