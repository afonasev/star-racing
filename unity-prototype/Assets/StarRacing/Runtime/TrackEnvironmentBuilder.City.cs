using System.Collections.Generic;
using UnityEngine;
namespace StarRacingPrototype {
    public sealed partial class TrackEnvironmentBuilder {
        // Ordinary Lit meshes, grouped once at environment construction. No frame-time scans or copies.
        sealed class CityBatches {
            const float ChunkSize=2048;
            readonly TrackEnvironmentBuilder owner;
            readonly Dictionary<(int x,int z,Material material),Batch> batches=new Dictionary<(int,int,Material),Batch>();
            readonly Vector3[] vertices,normals;
            readonly int[] triangles;
            public CityBatches(TrackEnvironmentBuilder owner) {
                this.owner=owner;vertices=owner.cube.vertices;normals=owner.cube.normals;triangles=owner.cube.triangles;
            }
            public void Box(Material material,Vector3 position,Vector3 scale,float tile) {
                int x=Mathf.FloorToInt(position.x/ChunkSize),z=Mathf.FloorToInt(position.z/ChunkSize);
                var key=(x,z,material);
                if(!batches.TryGetValue(key,out var batch))batches.Add(key,batch=new Batch{origin=new Vector3(x*ChunkSize,0,z*ChunkSize),material=material});
                int start=batch.vertices.Count;
                for(int i=0;i<vertices.Length;i++) {
                    batch.vertices.Add(position-batch.origin+Vector3.Scale(vertices[i],scale));batch.normals.Add(normals[i]);
                    batch.uv.Add(CubeUv(vertices[i],normals[i],scale,tile));
                }
                foreach(int index in triangles)batch.triangles.Add(start+index);
            }
            public void Flush() {
                foreach(var batch in batches.Values) {
                    var mesh=new Mesh{name="City district mesh"};
                    if(batch.vertices.Count>65535)mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
                    mesh.SetVertices(batch.vertices);mesh.SetNormals(batch.normals);mesh.SetUVs(0,batch.uv);mesh.SetTriangles(batch.triangles,0);mesh.RecalculateBounds();
                    owner.meshes.Add(mesh);owner.Shape("City district",mesh,batch.material,batch.origin,Quaternion.identity,Vector3.one,false);
                }
            }
            sealed class Batch {
                public Material material;public Vector3 origin;
                public readonly List<Vector3> vertices=new List<Vector3>(),normals=new List<Vector3>();
                public readonly List<Vector2> uv=new List<Vector2>();public readonly List<int> triangles=new List<int>();
            }
        }
    }
}
