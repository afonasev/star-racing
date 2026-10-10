using UnityEngine;
using UnityEngine.Rendering;
namespace StarRacingPrototype {
 // Resources belong to one prepared environment; activation happens only after race publication.
 public sealed class TrackSky {
  public Material Material {get;private set;}
  Material deckMaterial,mistMaterial;Mesh plane;
  const string Path="Environment/Sky/";
  public TrackSky(EnvironmentPlan plan,TrackRoute route,Transform parent) {
   bool space=plan.theme=="space-station";
   var skyShader=Required<Shader>("TrackSky");
   var texture=Required<Texture2D>(space?plan.SkyPlanetTexture:"CloudSea");
   var deckShader=space?null:Required<Shader>("CloudDeck");
   var mistShader=space?null:Required<Shader>("CloudMist");
   try {
   Material=new Material(skyShader){name="Track sky "+plan.theme};
   Material.SetFloat("_Space",space?1:0);Material.SetFloat("_CloudY",plan.cityBaseY+25);
   Material.SetVector("_SunDirection",plan.lighting.sunPosition.normalized);
   if(space) {
    Material.SetTexture("_PlanetTex",texture);
    Vector3 direction=plan.skyPlanetDirection;
    Vector3 right=Vector3.Cross(Vector3.up,direction).normalized;
    Material.SetVector("_PlanetDirection",direction);Material.SetVector("_PlanetRight",right);Material.SetVector("_PlanetUp",Vector3.Cross(direction,right));
    Material.SetFloat("_PlanetScale",.55f);
   } else {
    var clouds=texture;Material.SetTexture("_CloudTex",clouds);
    deckMaterial=new Material(deckShader){name="Cloud sea"};deckMaterial.SetTexture("_CloudTex",clouds);
    mistMaterial=new Material(mistShader){name="Tower cloud mist"};mistMaterial.SetTexture("_CloudTex",clouds);
    plane=new Mesh{name="Render-only cloud plane"};plane.vertices=new[]{new Vector3(-.5f,0,-.5f),new Vector3(-.5f,0,.5f),new Vector3(.5f,0,.5f),new Vector3(.5f,0,-.5f)};
    plane.uv=new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right};plane.triangles=new[]{0,1,2,0,2,3};plane.RecalculateNormals();plane.RecalculateBounds();
    var bounds=new Bounds(route.Samples[0].position,Vector3.zero);foreach(var sample in route.Samples)bounds.Encapsulate(sample.position);
    AddPlane(parent,"Cloud sea",deckMaterial,new Vector3(bounds.center.x,plan.cityBaseY+25,bounds.center.z),new Vector3(bounds.size.x+3640,1,bounds.size.z+3640));
    foreach(var tower in plan.towers) for(int layer=0;layer<3;layer++)
     AddPlane(parent,"Tower cloud mist",mistMaterial,new Vector3(tower.position.x,plan.cityBaseY+29+layer*6,tower.position.z),new Vector3(tower.width*7+layer*13,1,tower.width*7+layer*13));
   }
   } catch {Clear();throw;}
  }
  static T Required<T>(string name) where T:Object {
   var value=Resources.Load<T>(Path+name);if(value==null)throw new System.InvalidOperationException("Missing sky resource: "+name);return value;
  }
  void AddPlane(Transform parent,string name,Material material,Vector3 position,Vector3 scale) {
   var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.position=position;go.transform.localScale=scale;
   go.AddComponent<MeshFilter>().sharedMesh=plane;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
  }
  public void Activate(){RenderSettings.skybox=Material;}
  public void Clear(){if(RenderSettings.skybox==Material)RenderSettings.skybox=null;Dispose(Material);Dispose(deckMaterial);Dispose(mistMaterial);Dispose(plane);Material=null;}
  static void Dispose(Object value){if(value==null)return;if(Application.isPlaying)Object.Destroy(value);else Object.DestroyImmediate(value);}
 }
}
