using System.Collections.Generic;
using UnityEngine;

namespace StarRacingPrototype {
 // Repair only a contact whose projection is on a certified exterior triangle.
 // Convex tetrahedron closure faces and mesh edge normals are not extra walls.
 public sealed class RoadSupportContact:MonoBehaviour {
  readonly struct Face {
   readonly Vector3 a,u,v;readonly bool repairable;public readonly Vector3 Normal;
   public Face(Vector3 a,Vector3 b,Vector3 c,bool repairable=true){this.repairable=repairable;this.a=a;u=b-a;v=c-a;Normal=Vector3.Cross(u,v).normalized;}
   public bool Contains(Vector3 point) {
    if(!repairable||Normal.sqrMagnitude<.99f)return false;
    var offset=point-a;if(Mathf.Abs(Vector3.Dot(offset,Normal))>.017f)return false;
    double uu=Vector3.Dot(u,u),uv=Vector3.Dot(u,v),vv=Vector3.Dot(v,v),pu=Vector3.Dot(offset,u),pv=Vector3.Dot(offset,v),d=uu*vv-uv*uv;
    if(d<=1e-16)return false;
    double x=(pu*vv-pv*uv)/d,y=(pv*uu-pu*uv)/d;
    return x>=-.0002&&y>=-.0002&&x+y<=1.0002;
   }
  }
  static readonly Dictionary<int,Face[]> surfaces=new Dictionary<int,Face[]>();
  int identity;
  public void Initialize(Collider collider,Vector3 a,Vector3 b,Vector3 c){collider.hasModifiableContacts=true;identity=collider.GetInstanceID();surfaces[identity]=new[]{new Face(a,b,c)};}
  public void InitializeMesh(MeshCollider collider,bool[] repairable){
   collider.hasModifiableContacts=true;identity=collider.GetInstanceID();var mesh=collider.sharedMesh;var vertices=mesh.vertices;var triangles=mesh.triangles;
   for(int i=0;i<vertices.Length;i++)vertices[i]=collider.transform.TransformPoint(vertices[i]);
   var faces=new Face[triangles.Length/3];for(int i=0;i<faces.Length;i++)faces[i]=new Face(vertices[triangles[i*3]],vertices[triangles[i*3+1]],vertices[triangles[i*3+2]],repairable[i]);surfaces[identity]=faces;
  }
  internal static bool TryNormal(int collider,ModifiableContactPair pair,int contact,Vector3 point,out Vector3 normal){
   normal=default;if(!surfaces.TryGetValue(collider,out var faces))return false;
   // GetFaceIndex is defined only for a non-convex mesh, never the support hull.
   uint index=faces.Length==1?0:pair.GetFaceIndex(contact);
   if(index>=faces.Length||!faces[index].Contains(point))return false;
   normal=faces[index].Normal;return true;
  }
  void OnDestroy(){if(identity!=0)surfaces.Remove(identity);}
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
  static void ResetRegistry(){surfaces.Clear();}
 }
}
