using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using BigInteger = System.Numerics.BigInteger;

namespace StarRacingPrototype
{
    /// <summary>Exterior of the union of road slabs. Only the original top is magnetic road.</summary>
    public static class RoadVolumeMesh
    {
        public const float Thickness = 1f;
        public const string MeshName = "Procedural road shell";
        public const string SupportName = "Road exterior support";
        public const string BottomName = "Road bottom collision";
        public const string ExteriorName = "Road exterior boundary";
        const float Tolerance = .0001f;

        // Integers in units of 2^-149 represent every finite float exactly.
        // These predicates run during track construction, never during physics.
        readonly struct ExactPoint
        {
            readonly BigInteger x,y,z;
            ExactPoint(BigInteger x,BigInteger y,BigInteger z) {this.x=x;this.y=y;this.z=z;}
            public ExactPoint(Vector3 p):this(Integer(p.x),Integer(p.y),Integer(p.z)) {}
            static BigInteger Integer(float value)
            {
                uint bits=(uint)System.BitConverter.SingleToInt32Bits(value);
                int exponent=(int)((bits>>23)&255);if(exponent==255)throw new System.ArgumentException("Nonfinite support coordinate");
                BigInteger mantissa=bits&0x7fffff;if(exponent!=0)mantissa=(mantissa+0x800000)<<(exponent-1);
                return (bits&0x80000000)==0?mantissa:-mantissa;
            }
            public static ExactPoint operator -(ExactPoint a,ExactPoint b)=>new ExactPoint(a.x-b.x,a.y-b.y,a.z-b.z);
            public static ExactPoint Cross(ExactPoint a,ExactPoint b)=>new ExactPoint(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
            public static BigInteger Dot(ExactPoint a,ExactPoint b)=>a.x*b.x+a.y*b.y+a.z*b.z;
        }

        public sealed class SupportCell
        {
            public readonly int id;public readonly RoadFace face;public readonly Vector3[] corners;public readonly int[] triangles;public readonly int[][] sides;public readonly bool[] sections;
            ExactPoint[] exactCorners,exactNormals;
            public bool ContainsApex(Vector3 apex)
            {
                if(exactCorners==null) {
                    exactCorners=new ExactPoint[8];for(int i=0;i<8;i++)exactCorners[i]=new ExactPoint(corners[i]);
                    exactNormals=new ExactPoint[triangles.Length/3];
                    for(int i=0;i<exactNormals.Length;i++)exactNormals[i]=ExactPoint.Cross(exactCorners[triangles[i*3+1]]-exactCorners[triangles[i*3]],exactCorners[triangles[i*3+2]]-exactCorners[triangles[i*3]]);
                }
                var point=new ExactPoint(apex);
                for(int i=0;i<exactNormals.Length;i++)if(ExactPoint.Dot(point-exactCorners[triangles[i*3]],exactNormals[i])>0)return false;
                return true;
            }
            public SupportCell(int id,RoadFace face,Vector3[] top,Vector3[] normals,int[] perimeter,IList<int> topTriangles,int topOffset)
            {
                this.id=id;this.face=face;corners=new Vector3[8];sides=new int[4][];sections=new bool[4];
                for(int i=0;i<4;i++) {corners[i]=top[i];corners[i+4]=top[i]-normals[i]*Thickness;}
                var indices=new List<int>();
                for(int i=0;i<6;i++)indices.Add(topTriangles[topOffset*6+i]-topOffset*4);
                AddQuad(indices,4,5,6,7,-normals[0]);
                var distances=new[]{face.startDistance,face.endDistance,face.startDistance,face.endDistance};
                var laterals=new[]{face.leftStart,face.leftEnd,face.rightStart,face.rightEnd};
                for(int i=0;i<4;i++) {
                    int a=perimeter[i],b=perimeter[i+1];bool section=distances[a]==distances[b];
                    float x=section?laterals[a]:distances[a],y=section?laterals[b]:distances[b];int low=x<y?a:b,high=x<y?b:a;sides[i]=new[]{low,high,low+4,high+4};sections[i]=section;
                    // Match EmitBands' parameter order and diagonal, including reversed edges.
                    AddQuad(indices,low,high,low+4,high+4,Vector3.Cross(top[high]-top[low],normals[low])*(x<y?1:-1));
                }
                triangles=indices.ToArray();
            }
            void AddQuad(List<int> indices,int a,int b,int c,int d,Vector3 outward)
            {
                if(Vector3.Dot(Vector3.Cross(corners[b]-corners[a],corners[c]-corners[a]),outward)>0)indices.AddRange(new[]{a,b,c,c,b,d});
                else indices.AddRange(new[]{a,c,b,c,d,b});
            }
        }
        public readonly struct SupportTriangle
        {
            public readonly SupportCell owner;public readonly int side;public readonly Vector3 a,b,c,diagonal;public readonly Vector2 uvA,uvB,uvC;
            public SupportTriangle(SupportCell owner,int side,Vector3 a,Vector3 b,Vector3 c,Vector2 uvA,Vector2 uvB,Vector2 uvC,Vector3 diagonal) {this.owner=owner;this.side=side;this.a=a;this.b=b;this.c=c;this.uvA=uvA;this.uvB=uvB;this.uvC=uvC;this.diagonal=diagonal;}
            public Vector3 SelectApex(out bool fallback)
            {
                double bx=(double)b.x-a.x,by=(double)b.y-a.y,bz=(double)b.z-a.z,cx=(double)c.x-a.x,cy=(double)c.y-a.y,cz=(double)c.z-a.z;
                double nx=by*cz-bz*cy,ny=bz*cx-bx*cz,nz=bx*cy-by*cx;
                double inset=(side>=0?.02:.5)/System.Math.Sqrt(nx*nx+ny*ny+nz*nz);
                Vector3 apex=new Vector3((float)(diagonal.x-nx*inset),(float)(diagonal.y-ny*inset),(float)(diagonal.z-nz*inset));
                var origin=new ExactPoint(a);var normal=ExactPoint.Cross(new ExactPoint(b)-origin,new ExactPoint(c)-origin);
                fallback=!owner.ContainsApex(apex) || ExactPoint.Dot(new ExactPoint(apex)-origin,normal)>=0;
                if(fallback) {
                    double x=0,y=0,z=0;foreach(var p in owner.corners){x+=p.x*.125;y+=p.y*.125;z+=p.z*.125;}
                    var centroid=new Vector3((float)x,(float)y,(float)z);
                    // A warped boundary midpoint can lie just outside the kernel.
                    // Move toward the certified centroid before taking the full fallback.
                    double dx=x-diagonal.x,dy=y-diagonal.y,dz=z-diagonal.z;
                    double fraction=System.Math.Min(1,.02/System.Math.Sqrt(dx*dx+dy*dy+dz*dz));
                    apex=new Vector3((float)(diagonal.x+dx*fraction),(float)(diagonal.y+dy*fraction),(float)(diagonal.z+dz*fraction));
                    if(!owner.ContainsApex(apex) || ExactPoint.Dot(new ExactPoint(apex)-origin,normal)>=0)apex=centroid;
                }
                if(!owner.ContainsApex(apex) || ExactPoint.Dot(new ExactPoint(apex)-origin,normal)>=0)
                    throw new System.InvalidOperationException("No inward kernel apex for road owner "+owner.id+" side "+side);
                return apex;
            }
        }
        sealed class Edge
        {
            public Vector3 a, b, downA, downB;
            // Height and thickness use the route frame, independently of magnetic support normals.
            public float from, to, heightA, heightB;
            public bool forward;public SupportCell owner;public int side;
            public float T(float at) => Mathf.InverseLerp(from, to, at);
            public float Height(float at) => Mathf.Lerp(heightA, heightB, T(at));
            public Vector3 Top(float at) => Vector3.Lerp(a, b, T(at));
            public Vector3 Down(float at) => Vector3.Lerp(downA, downB, T(at));
            public Vector3 At(float at, float height) => Top(at) + Down(at) * (height - Height(at));
        }
        readonly struct Level
        {
            readonly Edge edge;
            readonly float offset;
            public Level(Edge edge, float offset) { this.edge = edge; this.offset = offset; }
            public float At(float at) => edge.Height(at) + offset;
        }
        readonly struct Band
        {
            public readonly Level low, high;
            public Band(Level low, Level high) { this.low = low; this.high = high; }
        }
        sealed class Geometry
        {
            readonly System.Action<SupportTriangle> observer;SupportCell emittingOwner;int emittingSide,emittingStart;Vector2[] emittingCoordinates;Vector3 emittingDiagonal;
            public Geometry(System.Action<SupportTriangle> observer) {this.observer=observer;}
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int> triangles = new List<int>();
            public int IndexCount=>triangles.Count;
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward,SupportCell owner=null,int side=-1,Vector2[] coordinates=null)
            {
                emittingOwner=owner;emittingSide=side;emittingStart=vertices.Count;emittingCoordinates=coordinates;if(owner!=null)emittingDiagonal=(b+c)*.5f;
                int i = vertices.Count;
                vertices.AddRange(new[] { a, b, c, d });
                float length = Vector3.Distance(a, b) / 4f, width = Vector3.Distance(a, c) / 4f;
                uv.AddRange(new[] { Vector2.zero, new Vector2(length, 0), new Vector2(0, width), new Vector2(length, width) });
                // A trimmed band can close at its first endpoint. Its first
                // triangle then has zero area; use both triangle normals so
                // the surviving triangle still determines the exterior winding.
                Vector3 normal = Vector3.Cross(b-a, c-a) + Vector3.Cross(b-c, d-c);
                if (Vector3.Dot(normal, outward) > 0)
                { Triangle(i,i+1,i+2); Triangle(i+2,i+1,i+3); }
                else { Triangle(i,i+2,i+1); Triangle(i+2,i+3,i+1); }
            }
            void Triangle(int a,int b,int c)
            {
                if(Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]).sqrMagnitude>1e-12f)
                {
                    triangles.AddRange(new[]{a,b,c});
                    if(emittingOwner!=null)observer?.Invoke(new SupportTriangle(emittingOwner,emittingSide,vertices[a],vertices[b],vertices[c],emittingCoordinates[a-emittingStart],emittingCoordinates[b-emittingStart],emittingCoordinates[c-emittingStart],emittingDiagonal));
                }
            }
            public void Create(Transform parent, Material material,int bottomIndices)
            {
                var go = new GameObject(MeshName); go.transform.SetParent(parent, false);
                var mesh = new Mesh { name = MeshName, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = material;
                var bottom=new Mesh {name=BottomName,indexFormat=IndexFormat.UInt32};
                bottom.SetVertices(vertices);bottom.SetTriangles(triangles.GetRange(0,bottomIndices),0);bottom.RecalculateBounds();
                go.AddComponent<MeshCollider>().sharedMesh = bottom;
                // Preserve the exact exterior triangle boundary at hull seams/tips.
                // Contained convex supports supply the small-motion CCD contacts.
                var exterior=new Mesh {name=ExteriorName,indexFormat=IndexFormat.UInt32};
                exterior.SetVertices(vertices);exterior.SetTriangles(triangles.GetRange(bottomIndices,triangles.Count-bottomIndices),0);exterior.RecalculateBounds();
                go.AddComponent<MeshCollider>().sharedMesh=exterior;
            }
        }
        static int Key(float value) => Mathf.RoundToInt(value / Tolerance);

        public static void Build(Transform parent, TrackRoute route, IList<Vector3> top, IList<RoadFace> faces,IList<int> topTriangles, Material material,System.Action<SupportTriangle> observer=null)
        {
            var supports=observer==null?new List<SupportTriangle>():null;
            var shell = new Geometry(observer??(triangle=>supports.Add(triangle)));
            // Section edges may have different subdivisions on their two sides.
            var sections = new Dictionary<int, List<Edge>>();
            // Emission uses the same .5m longitudinal stations for ramps and lanes.
            // Along edges therefore share full station intervals; lateral T-junctions
            // are handled separately by section interval subtraction.
            var along = new Dictionary<(int, int, int, int), List<Edge>>();
            for (int q = 0; q < top.Count / 4; q++)
            {
                RoadFace face = faces[q*2]; int at = q*4;
                var start = route.Evaluate(face.startDistance); var end = route.Evaluate(face.endDistance);
                var points = new[] { top[at], top[at+1], top[at+2], top[at+3] };
                var down = new[] { start.normal, end.normal, start.normal, end.normal };
                var distances = new[] { face.startDistance, face.endDistance, face.startDistance, face.endDistance };
                var laterals = new[] { face.leftStart, face.leftEnd, face.rightStart, face.rightEnd };
                var heights = new float[4];
                for (int i=0;i<4;i++)
                {
                    var frame = i%2==0 ? start : end;
                    heights[i] = Vector3.Dot(points[i] - (frame.position-frame.right*laterals[i]), frame.normal);
                }
                shell.Quad(points[0]-down[0]*Thickness, points[1]-down[1]*Thickness,
                    points[2]-down[2]*Thickness, points[3]-down[3]*Thickness, -start.normal);
                bool positive = Vector3.Dot(Vector3.Cross(points[1]-points[0],points[2]-points[0]), start.normal) > 0;
                int[] perimeter = positive ? new[] {0,1,3,2,0} : new[] {0,2,3,1,0};
                var owner=new SupportCell(q,face,points,down,perimeter,topTriangles,q);
                for (int i=0;i<4;i++)
                {
                    int a=perimeter[i], b=perimeter[i+1];
                    bool section = distances[a] == distances[b];
                    float x=section?laterals[a]:distances[a], y=section?laterals[b]:distances[b];
                    if (Mathf.Abs(y-x)<Tolerance) continue;
                    int low=x<y?a:b, high=x<y?b:a;
                    var edge = new Edge { a=points[low], b=points[high], downA=down[low], downB=down[high],
                        from=Mathf.Min(x,y),to=Mathf.Max(x,y),heightA=heights[low],heightB=heights[high],forward=x<y,owner=owner,side=i };
                    if (section)
                    {
                        int key=Key(distances[a]);
                        if (!sections.TryGetValue(key,out var group)) sections[key]=group=new List<Edge>();
                        group.Add(edge);
                    }
                    else
                    {
                        var key=(Key(distances[low]),Key(laterals[low]),Key(distances[high]),Key(laterals[high]));
                        if (!along.TryGetValue(key,out var group)) along[key]=group=new List<Edge>();
                        group.Add(edge);
                    }
                }
            }
            int bottomIndices=shell.IndexCount;
            foreach (var group in sections.Values) CloseGroup(shell,group);
            foreach (var group in along.Values) CloseGroup(shell,group);
            // Bottom triangles precede the emitted exterior bands; the top remains
            // in its original TrackSurface collider. Wall contacts use convex hulls.
            shell.Create(parent,material,bottomIndices);
            if(supports!=null)foreach(var triangle in supports)CreateSupport(parent,triangle);
        }
        public static MeshCollider CreateSupport(Transform parent,SupportTriangle triangle)
        {
            Vector3 apex=triangle.SelectApex(out _),origin=triangle.a;
            var world=new[]{triangle.a,triangle.b,triangle.c,apex};
            // Sterbenz subtraction is exact when same-sign coordinates differ
            // by at most a factor of two. Use zero on any axis crossing that
            // range, so even supports crossing a world axis roundtrip exactly.
            for(int axis=0;axis<3;axis++) {
                float min=world[0][axis],max=min;foreach(var p in world){min=Mathf.Min(min,p[axis]);max=Mathf.Max(max,p[axis]);}
                if(min<=0 && max>=0 || Mathf.Max(Mathf.Abs(min),Mathf.Abs(max))>2*Mathf.Min(Mathf.Abs(min),Mathf.Abs(max)))origin[axis]=0;
            }
            var mesh=new Mesh {name=SupportName};
            var local=new[]{Vector3.zero,triangle.b-origin,triangle.c-origin,apex-origin};
            local[0]=triangle.a-origin;
            for(int i=0;i<4;i++)if(!(origin+local[i]).Equals(world[i]))throw new System.InvalidOperationException("Support local coordinates changed authored vertex");
            var localOrigin=new ExactPoint(local[0]);
            if(ExactPoint.Dot(new ExactPoint(local[3])-localOrigin,ExactPoint.Cross(new ExactPoint(local[1])-localOrigin,new ExactPoint(local[2])-localOrigin))>=0)
                throw new System.InvalidOperationException("Support local tetrahedron lost volume");
            mesh.vertices=local;
            mesh.triangles=new[]{0,1,2,0,3,1,1,3,2,2,3,0};mesh.RecalculateBounds();
            // Exact predicates above reject coplanar apexes. Each hull has four
            // distinct vertices and four nondegenerate faces; preserve tiny cap tips.
            var options=MeshColliderCookingOptions.CookForFasterSimulation|MeshColliderCookingOptions.UseFastMidphase;
            Physics.BakeMesh(mesh.GetEntityId(),true,options);
            var go=new GameObject(SupportName);go.transform.SetParent(parent,false);go.transform.localPosition=origin;
            var collider=go.AddComponent<MeshCollider>();collider.convex=true;collider.cookingOptions=options;collider.sharedMesh=mesh;
            return collider;
        }
        static void CloseGroup(Geometry shell, List<Edge> edges)
        {
            var cuts=new List<float>();
            foreach(var edge in edges) { cuts.Add(edge.from); cuts.Add(edge.to); }
            cuts.Sort();
            for(int i=0;i<cuts.Count-1;i++)
            {
                float from=cuts[i],to=cuts[i+1]; if(to-from<Tolerance)continue;
                float mid=(from+to)*.5f;
                var active=edges.FindAll(e=>e.from<mid && e.to>mid);
                foreach(var edge in active)
                {
                    var others=active.FindAll(e=>e.forward!=edge.forward);
                    // Split where the relative height ordering changes. This trims the
                    // overlapping parts of a raised ramp/flat shoulder interface too.
                    var slices=new List<float>{from,to};
                    foreach(var other in others)
                    {
                        float a=edge.Height(from)-other.Height(from),b=edge.Height(to)-other.Height(to);
                        foreach(float threshold in new[]{-Thickness,0f,Thickness})
                            if((a-threshold)*(b-threshold)<0)
                                slices.Add(Mathf.Lerp(from,to,(threshold-a)/(b-a)));
                    }
                    slices.Sort();
                    for(int s=0;s<slices.Count-1;s++) EmitBands(shell,edge,others,slices[s],slices[s+1]);
                }
            }
        }
        static void EmitBands(Geometry shell, Edge edge, List<Edge> others, float from, float to)
        {
            if(to-from<Tolerance)return;
            float mid=(from+to)*.5f;
            var bands=new List<Band>{new Band(new Level(edge,-Thickness),new Level(edge,0))};
            foreach(var other in others)
            {
                var low=new Level(other,-Thickness);var high=new Level(other,0);
                var kept=new List<Band>();
                foreach(var band in bands)
                {
                    float l=band.low.At(mid),h=band.high.At(mid),ol=low.At(mid),oh=high.At(mid);
                    if(oh<=l+Tolerance || ol>=h-Tolerance) { kept.Add(band); continue; }
                    if(ol>l+Tolerance)kept.Add(new Band(band.low,low));
                    if(oh<h-Tolerance)kept.Add(new Band(high,band.high));
                }
                bands=kept;
            }
            Vector3 outward=Vector3.Cross(edge.b-edge.a,edge.downA)*(edge.forward?1:-1);
            foreach(var band in bands)
                if(band.high.At(mid)-band.low.At(mid)>Tolerance)
                    shell.Quad(edge.At(from,band.high.At(from)),edge.At(to,band.high.At(to)),
                        edge.At(from,band.low.At(from)),edge.At(to,band.low.At(to)),outward,edge.owner,edge.side,edge.owner==null?null:new[]{new Vector2(edge.T(from),edge.Height(from)-band.high.At(from)),new Vector2(edge.T(to),edge.Height(to)-band.high.At(to)),new Vector2(edge.T(from),edge.Height(from)-band.low.At(from)),new Vector2(edge.T(to),edge.Height(to)-band.low.At(to))});
        }
    }
}
