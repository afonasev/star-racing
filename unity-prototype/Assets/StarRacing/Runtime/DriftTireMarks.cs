using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace StarRacingPrototype {
 // Presentation only. One race-owned mesh; no writes to physics, input or handling state.
 public sealed class DriftTireMarks : MonoBehaviour {
  public const float Lifetime=8f,SampleInterval=.02f,Width=.30f,SurfaceOffset=.012f;
  public const int MaxCars=64,Capacity=MaxCars*2*(400+2);
  const float MinSpeed=5f,MinLateral=1.5f,MinDistance=.12f,MaxDistance=4f;
  [StructLayout(LayoutKind.Sequential)] struct Vertex {public Vector3 position;public Vector2 uv,age;}
  struct Trail {public bool valid;public Vector3 point,normal,side;public float sampled,distance;}
  readonly Trail[] trails=new Trail[MaxCars*2];
  readonly bool[] skidding=new bool[MaxCars];
  readonly int[] revisions=new int[MaxCars];
  readonly long[] produced=new long[MaxCars];
  readonly Vertex[] vertices=new Vertex[Capacity*4];
  readonly float[] births=new float[Capacity];
  Mesh mesh;Material material;MeshRenderer view;
  int next,oldest,count,dirtyStart,dirtyCount;
  static readonly int Clock=Shader.PropertyToID("_MarkTime");
  const MeshUpdateFlags UploadFlags=MeshUpdateFlags.DontRecalculateBounds|MeshUpdateFlags.DontValidateIndices;
  public int LiveSegments=>count;
  public long TotalSegments {get;private set;}
  public int RejectedSegments {get;private set;}
  public Mesh RenderMesh=>mesh;
  public long TotalFor(int car)=>car>=0&&car<MaxCars?produced[car]:0;
  public void Initialize(Bounds bounds) {
   var shader=Resources.Load<Shader>("Vehicle/DriftTireMarks");
   if(shader==null)throw new InvalidOperationException("Missing drift tire mark shader");
   var rubber=Resources.Load<Texture2D>("Vehicle/DriftRubber");
   if(rubber==null)throw new InvalidOperationException("Missing drift rubber texture");
   material=new Material(shader){name="Shared drift rubber"};material.SetTexture("_RubberTex",rubber);
   mesh=new Mesh{name="Bounded drift tire marks",indexFormat=IndexFormat.UInt32};mesh.MarkDynamic();
   mesh.SetVertexBufferParams(vertices.Length,
    new VertexAttributeDescriptor(VertexAttribute.Position,VertexAttributeFormat.Float32,3),
    new VertexAttributeDescriptor(VertexAttribute.TexCoord0,VertexAttributeFormat.Float32,2),
    new VertexAttributeDescriptor(VertexAttribute.TexCoord1,VertexAttributeFormat.Float32,2));
   mesh.SetIndexBufferParams(Capacity*6,IndexFormat.UInt32);
   var indices=new int[Capacity*6];
   for(int i=0;i<Capacity;i++){int v=i*4,t=i*6;indices[t]=v;indices[t+1]=v+2;indices[t+2]=v+1;indices[t+3]=v+1;indices[t+4]=v+2;indices[t+5]=v+3;}
   mesh.SetIndexBufferData(indices,0,0,indices.Length,UploadFlags);mesh.subMeshCount=1;
   mesh.SetSubMesh(0,new SubMeshDescriptor(0,indices.Length){bounds=bounds,vertexCount=vertices.Length},UploadFlags);mesh.bounds=bounds;
   gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
   view=gameObject.AddComponent<MeshRenderer>();view.sharedMaterial=material;view.shadowCastingMode=ShadowCastingMode.Off;view.receiveShadows=false;
   view.lightProbeUsage=LightProbeUsage.Off;view.reflectionProbeUsage=ReflectionProbeUsage.Off;Clear();
  }
  public static float SlipIntensity(Vector3 velocity,Vector3 forward,Vector3 normal,ref bool active) {
   if(normal.sqrMagnitude<.5f){active=false;return 0;}normal.Normalize();
   Vector3 planar=Vector3.ProjectOnPlane(velocity,normal),body=Vector3.ProjectOnPlane(forward,normal).normalized;
   float speed=planar.magnitude,along=Vector3.Dot(planar,body),lateral=Mathf.Abs(Vector3.Dot(planar,Vector3.Cross(normal,body)));
   float angle=Mathf.Atan2(lateral,Mathf.Abs(along))*Mathf.Rad2Deg;
   if(speed<MinSpeed||lateral<MinLateral||body.sqrMagnitude<.5f)active=false;
   else if(active)active=angle>5;else active=angle>=8;
   return active?Mathf.Lerp(.28f,.88f,Mathf.InverseLerp(5,35,angle)):0;
  }
  public void Observe(int car,int revision,Vector3 velocity,Vector3 forward,Vector3 normal,
   CheckpointAdmission.Support left,CheckpointAdmission.Support right,TrackBuilder track,bool allowed,float now) {
   if(car<0||car>=MaxCars)return;
   if(revisions[car]!=revision){Break(car);revisions[car]=revision;}
   float intensity=allowed?SlipIntensity(velocity,forward,normal,ref skidding[car]):0;
   if(intensity<=0){Break(car);return;}
   Contact(car*2,left,track,intensity,now);Contact(car*2+1,right,track,intensity,now);
  }
  void Contact(int stream,CheckpointAdmission.Support support,TrackBuilder track,float intensity,float now) {
   var hit=support.hit;var surface=hit.collider==null?null:hit.collider.GetComponent<TrackSurface>();
   // The suspension ray has a safety margin. A distant hit is not tyre contact.
   if(surface==null||surface.owner!=track||surface.buildRevision!=track.BuildRevision||hit.distance>=1.12f||hit.normal.sqrMagnitude<.5f){trails[stream]=default;return;}
   Sample(stream,hit.point,hit.normal,intensity,now);
  }
  // Geometry fixture entry; production only samples validated current road contacts.
  public void Sample(int stream,Vector3 point,Vector3 normal,float intensity,float now) {
   if(stream<0||stream>=trails.Length)return;
   if(!Finite(point)||!Finite(normal)||normal.sqrMagnitude<.5f||!float.IsFinite(now)||!float.IsFinite(intensity)||intensity<=0){trails[stream]=default;return;}
   normal.Normalize();var previous=trails[stream];
   if(!previous.valid){trails[stream]=new Trail{valid=true,point=point,normal=normal,sampled=now};return;}
   if(now-previous.sampled<SampleInterval-.000001f)return;
   Vector3 delta=point-previous.point;
   if(delta.sqrMagnitude>MaxDistance*MaxDistance||Vector3.Dot(normal,previous.normal)<.75f||now-previous.sampled>.15f){
    trails[stream]=new Trail{valid=true,point=point,normal=normal,sampled=now};return;
   }
   if(delta.sqrMagnitude<MinDistance*MinDistance)return;
   Vector3 side=Vector3.Cross(normal,delta).normalized*(Width*.5f);
   Vector3 previousSide=previous.side.sqrMagnitude>0?previous.side:Vector3.Cross(previous.normal,delta).normalized*(Width*.5f);
   Expire(now);
   if(count==Capacity){RejectedSegments++;trails[stream]=default;return;} // never evict a live 8-second segment
   // Continuous metres prevent stretching/restarting the texture at every physics sample.
   float distance=previous.distance+delta.magnitude;
   float from=previous.distance/.8f+stream*.371f,to=distance/.8f+stream*.371f;
   int v=next*4;
   Put(v,previous.point-previousSide+previous.normal*SurfaceOffset,0,from,now,intensity);
   Put(v+1,previous.point+previousSide+previous.normal*SurfaceOffset,1,from,now,intensity);
   Put(v+2,point-side+normal*SurfaceOffset,0,to,now,intensity);
   Put(v+3,point+side+normal*SurfaceOffset,1,to,now,intensity);
   births[next]=now;if(dirtyCount==0)dirtyStart=next;
   dirtyCount++;next=(next+1)%Capacity;count++;TotalSegments++;produced[stream/2]++;
   trails[stream]=new Trail{valid=true,point=point,normal=normal,side=side,sampled=now,distance=distance};
  }
  void Put(int v,Vector3 point,float x,float y,float birth,float intensity) {
   vertices[v]=new Vertex{position=transform.InverseTransformPoint(point),uv=new Vector2(x,y),age=new Vector2(birth,Mathf.Clamp01(intensity))};
  }
  static bool Finite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
  void Expire(float now){while(count>0&&now-births[oldest]>=Lifetime){oldest=(oldest+1)%Capacity;count--;}}
  public void Break(int car){if(car<0||car>=MaxCars)return;trails[car*2]=trails[car*2+1]=default;skidding[car]=false;}
  public void BreakAll(){Array.Clear(trails,0,trails.Length);Array.Clear(skidding,0,skidding.Length);}
  public void Clear(){
   BreakAll();Array.Clear(vertices,0,vertices.Length);Array.Clear(births,0,births.Length);Array.Clear(produced,0,produced.Length);
   next=oldest=count=dirtyStart=0;dirtyCount=Capacity;TotalSegments=0;RejectedSegments=0;Flush(Time.time);
  }
  public void Flush(float now){
   if(mesh==null)return;Expire(now);
   if(dirtyCount>0){
    if(dirtyCount>=Capacity)mesh.SetVertexBufferData(vertices,0,0,vertices.Length,0,UploadFlags);
    else{
     int first=Mathf.Min(dirtyCount,Capacity-dirtyStart);
     mesh.SetVertexBufferData(vertices,dirtyStart*4,dirtyStart*4,first*4,0,UploadFlags);
     if(first<dirtyCount)mesh.SetVertexBufferData(vertices,0,0,(dirtyCount-first)*4,0,UploadFlags);
    }
    dirtyCount=0;
   }
   material.SetFloat(Clock,now);view.enabled=count>0;
  }
  void LateUpdate()=>Flush(Time.time);
  void OnDisable()=>BreakAll();
  void OnDestroy(){Dispose(mesh);Dispose(material);}
  static void Dispose(UnityEngine.Object value){if(value==null)return;if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
 }
}
