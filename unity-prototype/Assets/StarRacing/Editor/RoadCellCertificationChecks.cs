using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using BigInteger=System.Numerics.BigInteger;

namespace StarRacingPrototype
{
    // Diagnostic only. Uses local-origin doubles and never creates support colliders.
    public static class RoadCellCertificationChecks
    {
        const double Epsilon=1e-8;
        readonly struct D3
        {
            public readonly double x,y,z;
            public D3(double x,double y,double z) {this.x=x;this.y=y;this.z=z;}
            public D3(Vector3 p) : this(p.x,p.y,p.z) {}
            public static D3 operator +(D3 a,D3 b)=>new D3(a.x+b.x,a.y+b.y,a.z+b.z);
            public static D3 operator -(D3 a,D3 b)=>new D3(a.x-b.x,a.y-b.y,a.z-b.z);
            public static D3 operator *(D3 a,double b)=>new D3(a.x*b,a.y*b,a.z*b);
            public double Length=>Math.Sqrt(Dot(this,this));
            public D3 Unit=>this*(1/Length);
            public static double Dot(D3 a,D3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
            public static D3 Cross(D3 a,D3 b)=>new D3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
        }
        readonly struct D2
        {
            public readonly double x,y;
            public D2(double x,double y) {this.x=x;this.y=y;}
            public static D2 operator +(D2 a,D2 b)=>new D2(a.x+b.x,a.y+b.y);
            public static D2 operator -(D2 a,D2 b)=>new D2(a.x-b.x,a.y-b.y);
            public static D2 operator *(D2 a,double b)=>new D2(a.x*b,a.y*b);
            public static double Cross(D2 a,D2 b)=>a.x*b.y-a.y*b.x;
        }
        sealed class Owner
        {
            public D3[] points;public Vector3[] raw;public E3[] exact;public int[] weld;public readonly List<int[]> faces=new List<int[]>();
            public bool valid=true,kernel;public string invalidReason="";public double margin=double.PositiveInfinity,volume;
        }
        static List<D2> Clip(List<D2> polygon,D2[] triangle)
        {
            double sign=Math.Sign(D2.Cross(triangle[1]-triangle[0],triangle[2]-triangle[0]));
            for(int edge=0;edge<3 && polygon.Count>0;edge++) {
                var next=new List<D2>();D2 a=triangle[edge],b=triangle[(edge+1)%3],previous=polygon[polygon.Count-1];
                double before=sign*D2.Cross(b-a,previous-a);
                foreach(var current in polygon) {
                    double after=sign*D2.Cross(b-a,current-a);
                    if((after>=0)!=(before>=0))next.Add(previous+(current-previous)*(before/(before-after)));
                    if(after>=0)next.Add(current);previous=current;before=after;
                }polygon=next;
            }return polygon;
        }
        static double Area(List<D2> polygon)
        {
            double area=0;for(int i=0;i<polygon.Count;i++)area+=D2.Cross(polygon[i],polygon[(i+1)%polygon.Count]);return Math.Abs(area)*.5;
        }
        static double Covered(D3[] triangle,D3[] face,bool oriented=false)
        {
            D3 n=D3.Cross(face[1]-face[0],face[2]-face[0]);if(n.Length<1e-12)return 0;n=n.Unit;
            if(oriented && D3.Dot(D3.Cross(triangle[1]-triangle[0],triangle[2]-triangle[0]),n)<=0)return 0;
            foreach(var p in triangle)if(Math.Abs(D3.Dot(p-face[0],n))>Epsilon)return 0;
            D3 u=(face[1]-face[0]).Unit,v=D3.Cross(n,u);
            Func<D3,D2> project=p=>new D2(D3.Dot(p-face[0],u),D3.Dot(p-face[0],v));
            var poly=new List<D2>();foreach(var p in triangle)poly.Add(project(p));
            return Area(Clip(poly,new[]{project(face[0]),project(face[1]),project(face[2])}));
        }
        readonly struct E3
        {
            public readonly BigInteger x,y,z;
            public E3(BigInteger x,BigInteger y,BigInteger z) {this.x=x;this.y=y;this.z=z;}
            public static E3 operator +(E3 a,E3 b)=>new E3(a.x+b.x,a.y+b.y,a.z+b.z);
            public static E3 operator -(E3 a,E3 b)=>new E3(a.x-b.x,a.y-b.y,a.z-b.z);
            public static E3 operator *(E3 a,BigInteger b)=>new E3(a.x*b,a.y*b,a.z*b);
            public bool Zero=>x.IsZero && y.IsZero && z.IsZero;
            public static BigInteger Dot(E3 a,E3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
            public static E3 Cross(E3 a,E3 b)=>new E3(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);
        }
        readonly struct E2
        {
            public readonly BigInteger x,y;
            public E2(BigInteger x,BigInteger y) {this.x=x;this.y=y;}
            public static E2 operator -(E2 a,E2 b)=>new E2(a.x-b.x,a.y-b.y);
            public static BigInteger Cross(E2 a,E2 b)=>a.x*b.y-a.y*b.x;
            public static BigInteger Dot(E2 a,E2 b)=>a.x*b.x+a.y*b.y;
        }
        static (int mantissa,int exponent) FloatParts(float value)
        {
            int bits=BitConverter.SingleToInt32Bits(value),exp=(bits>>23)&255,mantissa=bits&0x7fffff;
            if(exp==255)throw new ArithmeticException("nonfinite geometry input");if(exp!=0)mantissa|=0x800000;return (bits<0?-mantissa:mantissa,exp==0?-149:exp-150);
        }
        static E3[] ExactPoints(Vector3[] raw) => ExactPoints(raw,out _);
        static E3[] ExactPoints(Vector3[] raw,out int exponent)
        {
            int scale=int.MaxValue;foreach(var p in raw)foreach(float v in new[]{p.x,p.y,p.z}) {var parts=FloatParts(v);if(parts.mantissa!=0)scale=Math.Min(scale,parts.exponent);}
            exponent=scale;Func<float,BigInteger> convert=v=> {var parts=FloatParts(v);return parts.mantissa==0?BigInteger.Zero:new BigInteger(parts.mantissa)<<(parts.exponent-scale);};
            var result=new E3[raw.Length];for(int i=0;i<raw.Length;i++)result[i]=new E3(convert(raw[i].x),convert(raw[i].y),convert(raw[i].z));return result;
        }
        static bool Allowed(E3 numerator,BigInteger denominator,List<E3> shared)
        {
            foreach(var p in shared)if((numerator-p*denominator).Zero)return true;
            if(shared.Count==2) {
                E3 edge=shared[1]-shared[0],offset=numerator-shared[0]*denominator;
                BigInteger at=E3.Dot(offset,edge);if(E3.Cross(offset,edge).Zero && at>=0 && at<=E3.Dot(edge,edge)*denominator)return true;
            }return false;
        }
        static bool Inside(E3 numerator,BigInteger denominator,E3[] face)
        {
            E3 n=E3.Cross(face[1]-face[0],face[2]-face[0]);
            for(int i=0;i<3;i++)if(E3.Dot(E3.Cross(face[(i+1)%3]-face[i],numerator-face[i]*denominator),n)<0)return false;return true;
        }
        static bool EdgeContact(E3 a,E3 b,E3 c,E3 d,E3 normal,List<E3> shared)
        {
            int drop=BigInteger.Abs(normal.x)>=BigInteger.Abs(normal.y) && BigInteger.Abs(normal.x)>=BigInteger.Abs(normal.z)?0:BigInteger.Abs(normal.y)>=BigInteger.Abs(normal.z)?1:2;
            Func<E3,E2> project=p=>drop==0?new E2(p.y,p.z):drop==1?new E2(p.x,p.z):new E2(p.x,p.y);
            E2 p=project(a),q=project(c),r=project(b)-p,s=project(d)-q;BigInteger den=E2.Cross(r,s);
            if(!den.IsZero) {
                BigInteger t=E2.Cross(q-p,s),u=E2.Cross(q-p,r);if(den<0){den=-den;t=-t;u=-u;}
                return t>=0 && t<=den && u>=0 && u<=den && !Allowed(a*den+(b-a)*t,den,shared);
            }
            if(!E2.Cross(q-p,r).IsZero)return false;
            BigInteger length=E2.Dot(r,r);if(length.IsZero)return false;
            BigInteger from=E2.Dot(q-p,r),to=E2.Dot(project(d)-p,r),low=BigInteger.Max(0,BigInteger.Min(from,to)),high=BigInteger.Min(length,BigInteger.Max(from,to));
            return low<=high && (!Allowed(a*length+(b-a)*low,length,shared) || !Allowed(a*length+(b-a)*high,length,shared));
        }
        static bool CoplanarSegment(E3 a,E3 b,E3[] face,List<E3> shared)
        {
            if((Inside(a,1,face) && !Allowed(a,1,shared)) || (Inside(b,1,face) && !Allowed(b,1,shared)))return true;
            E3 normal=E3.Cross(face[1]-face[0],face[2]-face[0]);for(int i=0;i<3;i++)if(EdgeContact(a,b,face[i],face[(i+1)%3],normal,shared))return true;return false;
        }
        static bool ExactPierces(E3 a,E3 b,E3[] face,List<E3> shared)
        {
            E3 n=E3.Cross(face[1]-face[0],face[2]-face[0]);BigInteger da=E3.Dot(a-face[0],n),db=E3.Dot(b-face[0],n),den=da-db;
            if(den.IsZero)return da.IsZero && CoplanarSegment(a,b,face,shared);
            BigInteger t=da;if(den<0){den=-den;t=-t;}if(t<0 || t>den)return false;
            E3 point=a*den+(b-a)*t;return !Allowed(point,den,shared) && Inside(point,den,face);
        }
        static bool Intersects(Owner owner,int[] first,int[] second)
        {
            for(int axis=0;axis<3;axis++) {
                Func<D3,double> coord=p=>axis==0?p.x:axis==1?p.y:p.z;
                double amin=double.PositiveInfinity,amax=double.NegativeInfinity,bmin=double.PositiveInfinity,bmax=double.NegativeInfinity;
                foreach(int i in first){amin=Math.Min(amin,coord(owner.points[i]));amax=Math.Max(amax,coord(owner.points[i]));}
                foreach(int i in second){bmin=Math.Min(bmin,coord(owner.points[i]));bmax=Math.Max(bmax,coord(owner.points[i]));}
                if(amax<bmin || bmax<amin)return false;
            }
            if(owner.exact==null)owner.exact=ExactPoints(owner.raw);
            E3[] a={owner.exact[first[0]],owner.exact[first[1]],owner.exact[first[2]]},b={owner.exact[second[0]],owner.exact[second[1]],owner.exact[second[2]]};
            var shared=new List<E3>();var common=new HashSet<int>();foreach(int x in first)foreach(int y in second)if(owner.weld[x]==owner.weld[y] && common.Add(owner.weld[x]))shared.Add(owner.exact[x]);
            E3 normal=E3.Cross(a[1]-a[0],a[2]-a[0]);bool coplanar=true;foreach(var p in b)coplanar&=E3.Dot(p-a[0],normal).IsZero;
            if(shared.Count==3)return true;
            if(shared.Count==2 && !coplanar)return false;
            for(int i=0;i<3;i++)if(ExactPierces(a[i],a[(i+1)%3],b,shared) || ExactPierces(b[i],b[(i+1)%3],a,shared))return true;return false;
        }
        static Owner Analyze(RoadVolumeMesh.SupportCell cell)
        {
            var owner=new Owner {points=new D3[8],raw=cell.corners,weld=new int[8]};D3 origin=new D3(cell.corners[0]),center=new D3();
            for(int i=0;i<8;i++) {
                owner.points[i]=new D3(cell.corners[i])-origin;center+=owner.points[i]*(1.0/8);owner.weld[i]=i;
                for(int j=0;j<i;j++)if((owner.points[i]-owner.points[j]).Length==0){owner.weld[i]=owner.weld[j];break;}
            }
            var edges=new Dictionary<(int,int),(int count,int direction)>();
            for(int at=0;at<cell.triangles.Length;at+=3) {
                int[] face={cell.triangles[at],cell.triangles[at+1],cell.triangles[at+2]};D3 a=owner.points[face[0]],b=owner.points[face[1]],c=owner.points[face[2]],normal=D3.Cross(b-a,c-a);
                if(normal.Length<1e-12)continue;owner.faces.Add(face);
                owner.volume+=D3.Dot(a,D3.Cross(b,c))/6;owner.margin=Math.Min(owner.margin,-D3.Dot(center-a,normal.Unit));
                for(int i=0;i<3;i++) {
                    int x=owner.weld[face[i]],y=owner.weld[face[(i+1)%3]],direction=x<y?1:-1;var key=x<y?(x,y):(y,x);
                    edges.TryGetValue(key,out var previous);edges[key]=(previous.count+1,previous.direction+direction);
                }
            }
            var used=new HashSet<int>();foreach(var face in owner.faces)foreach(int i in face)used.Add(owner.weld[i]);
            if(used.Count-edges.Count+owner.faces.Count!=2) {owner.valid=false;owner.invalidReason="non-spherical-topology";}
            foreach(var edge in edges.Values)if(edge.count!=2 || edge.direction!=0) {owner.valid=false;owner.invalidReason="edge-incidence";}
            if(owner.volume<=1e-12) {owner.valid=false;owner.invalidReason="nonpositive-volume";}
            for(int i=0;i<owner.faces.Count;i++)for(int j=i+1;j<owner.faces.Count;j++)if(Intersects(owner,owner.faces[i],owner.faces[j])) {owner.valid=false;owner.invalidReason="intersection-"+i+"-"+j;}
            owner.kernel=owner.valid && owner.margin>Epsilon;return owner;
        }
        readonly struct Weight
        {
            public readonly BigInteger a,b,c,den;
            public Weight(BigInteger a,BigInteger b,BigInteger c,BigInteger den) {this.a=a;this.b=b;this.c=c;this.den=den;}
            public BigInteger At(int i)=>i==0?a:i==1?b:c;
        }
        static List<Weight> Split(E3[] uv,bool first)
        {
            var result=new List<Weight>();BigInteger unit=uv[0].z;
            for(int i=0;i<3;i++) {
                int before=(i+2)%3;BigInteger x=(unit-uv[before].x-uv[before].y)*(first?1:-1),y=(unit-uv[i].x-uv[i].y)*(first?1:-1);
                if((x>=0)!=(y>=0)) {
                    BigInteger den=x-y,t=x;if(den<0){den=-den;t=-t;}
                    var w=new BigInteger[3];w[before]=den-t;w[i]=t;result.Add(new Weight(w[0],w[1],w[2],den));
                }
                if(y>=0)result.Add(new Weight(i==0?1:0,i==1?1:0,i==2?1:0,1));
            }return result;
        }
        static E3 Weighted(E3[] points,Weight w)=>points[0]*w.a+points[1]*w.b+points[2]*w.c;
        static (BigInteger num,BigInteger den) ParamArea(List<Weight> polygon,E3[] uv)
        {
            BigInteger num=0,den=1;
            for(int i=0;i<polygon.Count;i++) {
                var a=polygon[i];var b=polygon[(i+1)%polygon.Count];E3 p=Weighted(uv,a),q=Weighted(uv,b);
                BigInteger next=p.x*q.y-p.y*q.x,nextDen=a.den*b.den;
                num=num*nextDen+next*den;den*=nextDen;
                BigInteger gcd=BigInteger.GreatestCommonDivisor(BigInteger.Abs(num),den);if(!gcd.IsZero){num/=gcd;den/=gcd;}
            }return (BigInteger.Abs(num),den);
        }
        static bool SquareAtLeast(double value,BigInteger numerator,BigInteger denominator)
        {
            long bits=BitConverter.DoubleToInt64Bits(value);int exp=(int)((bits>>52)&2047);if(exp==2047)return true;
            BigInteger mantissa=(bits&0xfffffffffffffL)+(exp==0?0:1L<<52);int power=exp==0?-1074:exp-1075;
            BigInteger squared=mantissa*mantissa;
            return power>=0?(squared<<(power*2))*denominator>=numerator:squared*denominator>=(numerator<<(-power*2));
        }
        static double NormUpper(BigInteger numerator,BigInteger denominator,int scale)
        {
            if(numerator.IsZero)return 0;if(scale<0)denominator<<=-scale*2;else numerator<<=scale*2;
            int nShift=Math.Max(0,numerator.ToByteArray().Length*8-500),dShift=Math.Max(0,denominator.ToByteArray().Length*8-500);
            double guess=Math.Sqrt((double)(numerator>>nShift)/(double)(denominator>>dShift)*Math.Pow(2,nShift-dShift));
            if(double.IsNaN(guess))throw new ArithmeticException("invalid delta norm");
            while(!SquareAtLeast(guess,numerator,denominator))guess=BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(guess)+1);
            return guess;
        }
        static bool Kernel(E3[] points,E3 apex,Owner owner)
        {
            foreach(var face in owner.faces)if(E3.Dot(apex-points[face[0]],E3.Cross(points[face[1]]-points[face[0]],points[face[2]]-points[face[0]]))>0)return false;return true;
        }
        // Exact rational subdivision is a proof construction only. Renderer/native
        // base triangles remain unchanged. Each reference subtriangle lies on a
        // canonical owner face; convex interpolation bounds the complete tetrahedron.
        static bool DeltaCertificate(RoadVolumeMesh.SupportTriangle triangle,Owner owner,out double delta,out bool adjusted,out bool fallback,out string rejection)
        {
            delta=0;adjusted=fallback=false;rejection="owner-kernel";if(!owner.kernel)return false;
            var source=new[]{triangle.uvA,triangle.uvB,triangle.uvC};var param=new Vector3[3];
            for(int i=0;i<3;i++) {
                float u=Mathf.Clamp01(source[i].x),v=Mathf.Clamp01(source[i].y);adjusted|=u!=source[i].x || v!=source[i].y;param[i]=new Vector3(u,v,1);
            }
            E3[] uv=ExactPoints(param);BigInteger area=BigInteger.Abs((uv[1].x-uv[0].x)*(uv[2].y-uv[0].y)-(uv[1].y-uv[0].y)*(uv[2].x-uv[0].x));if(area.IsZero){rejection="degenerate-reference-UV";return false;}
            var raw=new Vector3[12];Array.Copy(triangle.owner.corners,raw,8);raw[8]=triangle.a;raw[9]=triangle.b;raw[10]=triangle.c;
            raw[11]=triangle.SelectApex(out fallback);
            E3[] points=ExactPoints(raw,out int scale);
            if(!Kernel(points,points[11],owner)){rejection="runtime-apex-outside-kernel apex="+raw[11].ToString("G9");return false;}
            E3 actualNormal=E3.Cross(points[9]-points[8],points[10]-points[8]);BigInteger inward=E3.Dot(points[11]-points[8],actualNormal);if(inward>=0){rejection="apex-not-inward sign="+inward.Sign+" apex="+raw[11].ToString("G9");return false;}
            var actual=new[]{points[8],points[9],points[10]};var sides=triangle.owner.sides[triangle.side];BigInteger sumNum=0,sumDen=1,unit=uv[0].z;
            for(int part=0;part<2;part++) {
                var polygon=Split(uv,part==0);var covered=ParamArea(polygon,uv);
                sumNum=sumNum*covered.den+covered.num*sumDen;sumDen*=covered.den;if(covered.num.IsZero)continue;
                int at=12+triangle.side*6+part*3;var face=triangle.owner.triangles;
                E3 faceNormal=E3.Cross(points[face[at+1]]-points[face[at]],points[face[at+2]]-points[face[at]]);BigInteger orientation=E3.Dot(actualNormal,faceNormal);if(orientation<=0){rejection="face-orientation part="+part+" sign="+orientation.Sign+" apex="+raw[11].ToString("G9")+" inwardSign="+inward.Sign;return false;}
                foreach(var w in polygon) {
                    E3 t=Weighted(uv,w);BigInteger total=unit*w.den;
                    E3 reference=part==0?points[sides[0]]*(total-t.x-t.y)+points[sides[1]]*t.x+points[sides[2]]*t.y:
                        points[sides[2]]*(total-t.x)+points[sides[1]]*(total-t.y)+points[sides[3]]*(t.x+t.y-total);
                    E3 difference=Weighted(actual,w)*unit-reference;
                    delta=Math.Max(delta,NormUpper(E3.Dot(difference,difference),total*total,scale));
                }
            }
            if(sumNum!=area*sumDen)throw new ArithmeticException("canonical subdivision lost parameter area");
            rejection=delta<=.002?"none":"delta-budget";return delta<=.002;
        }
        static string One(Procedural.Definition definition,string label)
        {
            var root=new GameObject("support cell certification");var owners=new Dictionary<int,Owner>();
            int bases=0,matched=0,certified=0,invalid=0,rejected=0,deltaCertified=0,deltaRejected=0,paramAdjusted=0,apexFallback=0;bool boundMode=Environment.GetEnvironmentVariable("STAR_RACING_ROAD_CELL_BOUND_DIAGNOSTIC")=="1";double maxDelta=0;double minMargin=double.PositiveInfinity,maxDeviation=0,maxUncovered=0;
            var witnesses=new StringBuilder();var witnessCounts=new Dictionary<string,int>();var watch=System.Diagnostics.Stopwatch.StartNew();
            try {
                ProceduralTrackMesh.Build(root.transform,new TrackRoute(definition),null,null,baseTriangle=> {
                    bases++;var cell=baseTriangle.owner;
                    if(!owners.TryGetValue(cell.id,out var owner)) {
                        owner=Analyze(cell);owners[cell.id]=owner;if(!owner.valid)invalid++;else if(!owner.kernel)rejected++;
                        minMargin=Math.Min(minMargin,owner.margin);
                    }
                    D3 origin=new D3(cell.corners[0]);D3[] triangle={new D3(baseTriangle.a)-origin,new D3(baseTriangle.b)-origin,new D3(baseTriangle.c)-origin};
                    double area=D3.Cross(triangle[1]-triangle[0],triangle[2]-triangle[0]).Length*.5,covered=0,bestDeviation=double.PositiveInfinity;
                    for(int part=0;part<2;part++) {
                        int at=12+baseTriangle.side*6+part*3;D3[] face={owner.points[cell.triangles[at]],owner.points[cell.triangles[at+1]],owner.points[cell.triangles[at+2]]};
                        D3 n=D3.Cross(face[1]-face[0],face[2]-face[0]);if(n.Length<1e-12)continue;n=n.Unit;
                        double deviation=0;foreach(var p in triangle)deviation=Math.Max(deviation,Math.Abs(D3.Dot(p-face[0],n)));
                        bestDeviation=Math.Min(bestDeviation,deviation);covered+=Covered(triangle,face,true);
                    }
                    double uncovered=Math.Max(0,area-covered);bool onBoundary=uncovered<=Math.Max(1e-12,area*1e-9);
                    maxDeviation=Math.Max(maxDeviation,bestDeviation);maxUncovered=Math.Max(maxUncovered,uncovered);
                    if(onBoundary)matched++;if(onBoundary && owner.kernel)certified++;
                    if(boundMode) {if(DeltaCertificate(baseTriangle,owner,out double delta,out bool adjusted,out bool fallback,out string rejection))deltaCertified++;else {deltaRejected++;witnesses.AppendLine("DELTA_REJECT owner="+cell.id+" side="+baseTriangle.side+" reason="+rejection+" delta="+delta.ToString("G17")+" adjusted="+adjusted+" fallback="+fallback+" sourceUV="+baseTriangle.uvA.ToString("G9")+" "+baseTriangle.uvB.ToString("G9")+" "+baseTriangle.uvC.ToString("G9")+" base="+baseTriangle.a.ToString("G9")+" "+baseTriangle.b.ToString("G9")+" "+baseTriangle.c.ToString("G9"));}maxDelta=Math.Max(maxDelta,delta);if(adjusted)paramAdjusted++;if(fallback)apexFallback++;}
                    string reason=!owner.valid?"owner-invalid":!owner.kernel?"centroid-rejected":!onBoundary?"base-mismatch":"none";
                    witnessCounts.TryGetValue(reason,out int witnessCount);
                    if(reason!="none" && witnessCount<8) {witnessCounts[reason]=witnessCount+1;
                        witnesses.AppendLine("WITNESS reason="+reason+" detail="+owner.invalidReason+" owner="+cell.id+" distance="+cell.face.startDistance+".."+cell.face.endDistance+" side="+baseTriangle.side+" valid="+owner.valid+" centroidMargin="+owner.margin.ToString("G17")+" deviation="+bestDeviation.ToString("G17")+" uncoveredArea="+uncovered.ToString("G17"));
                        foreach(var p in cell.corners)witnesses.Append(p.ToString("G9")+" ");witnesses.AppendLine();witnesses.AppendLine("base="+baseTriangle.a.ToString("G9")+" "+baseTriangle.b.ToString("G9")+" "+baseTriangle.c.ToString("G9"));
                    }
                });
                string report="ROAD_CELL_REPORT label="+label+" planeTolerance="+Epsilon.ToString("G17")+" topologyPredicates=exact-float-dyadic owners="+owners.Count+" bases="+bases+" matched="+matched+" certified="+certified+" invalidOwners="+invalid+" centroidRejected="+rejected+" unsupportedBases="+(bases-matched)+" minCentroidMargin="+minMargin.ToString("G17")+" maxBaseDeviation="+maxDeviation.ToString("G17")+" maxUncoveredArea="+maxUncovered.ToString("G17")+" deltaCertified="+deltaCertified+" deltaRejected="+deltaRejected+" maxDelta="+maxDelta.ToString("G17")+" parameterAdjusted="+paramAdjusted+" apexFallback="+apexFallback+" seconds="+watch.Elapsed.TotalSeconds.ToString("F3");
                Debug.Log(report);return report+"\n"+witnesses;
            } finally {
                var meshes=new HashSet<Mesh>();foreach(var filter in root.GetComponentsInChildren<MeshFilter>())if(filter.sharedMesh!=null)meshes.Add(filter.sharedMesh);
                foreach(var collider in root.GetComponentsInChildren<MeshCollider>())if(collider.sharedMesh!=null)meshes.Add(collider.sharedMesh);
                UnityEngine.Object.DestroyImmediate(root);foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
        public static void Run()
        {
            D3[] first={new D3(0,0,0),new D3(1,0,0),new D3(0,1,0)},second={new D3(0,1,0),new D3(1,0,0),new D3(1,1,0)},spanning={new D3(0,0,0),new D3(1,0,0),new D3(1,1,0)};
            if(Math.Abs(Covered(spanning,first,true)+Covered(spanning,second,true)-.5)>1e-12)throw new Exception("cell coverage rejected planar diagonal crossing");
            var reversed=new[]{spanning[0],spanning[2],spanning[1]};if(Covered(reversed,first,true)+Covered(reversed,second,true)!=0)throw new Exception("cell coverage accepted reversed base");
            second[2]=new D3(1,1,.1);
            if(Math.Abs(Covered(spanning,first)+Covered(spanning,second)-.25)>1e-12)throw new Exception("cell coverage accepted warped off-plane base");
            var controlRaw=new[]{new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(0,1,0),new Vector3(.5f,0,0),new Vector3(.5f,-1,0),new Vector3(1.5f,-1,0),new Vector3(1,1,0)};
            var control=new Owner {raw=controlRaw,points=new D3[controlRaw.Length],weld=new int[controlRaw.Length]};for(int i=0;i<controlRaw.Length;i++){control.points[i]=new D3(controlRaw[i]);control.weld[i]=i;}
            if(!Intersects(control,new[]{0,1,2},new[]{3,4,5}))throw new Exception("exact predicate missed forbidden coplanar point contact");
            if(Intersects(control,new[]{0,1,2},new[]{2,1,6}))throw new Exception("exact predicate rejected shared diagonal");
            if(!Intersects(control,new[]{0,1,2},new[]{0,1,2}))throw new Exception("exact predicate accepted duplicate face");
            var warped=new[]{new Vector3(0,0,0),new Vector3(0,0,1),new Vector3(1,0,0),new Vector3(1,0,1)};var up=new[]{Vector3.up,new Vector3(.1f,1,0).normalized,Vector3.up,new Vector3(.1f,1,0).normalized};
            var cell=new RoadVolumeMesh.SupportCell(0,new RoadFace(0,0,0,1,0,1,0,1,false),warped,up,new[]{0,1,3,2,0},new[]{0,1,2,2,1,3},0);
            D3[] baseFace={new D3(cell.corners[2]),new D3(cell.corners[3]),new D3(cell.corners[6])};double coverage=0;
            for(int part=0;part<2;part++){int at=24+part*3;coverage+=Covered(baseFace,new[]{new D3(cell.corners[cell.triangles[at]]),new D3(cell.corners[cell.triangles[at+1]]),new D3(cell.corners[cell.triangles[at+2]])},true);}
            if(Math.Abs(coverage-D3.Cross(baseFace[1]-baseFace[0],baseFace[2]-baseFace[0]).Length*.5)>1e-12)throw new Exception("canonical reversed warped side changed diagonal");
            if(NormUpper(1,9,0)<1.0/3 || !SquareAtLeast(NormUpper(1,9,0),1,9))throw new Exception("delta norm not outward bounded");
            var report=new StringBuilder();report.AppendLine("Diagnostic only; rejected centroid does not prove empty kernel; unsupported base forbids current owner-to-base proof.");
            string fixtureFilter=Environment.GetEnvironmentVariable("STAR_RACING_ROAD_CELL_FIXTURE");int fixtures=0;if(string.IsNullOrEmpty(fixtureFilter) || fixtureFilter=="flat"){report.AppendLine(One(RoadVolumeChecks.FlatRoad(),"flat"));fixtures++;}
            foreach(string theme in new[]{"cloud-city","space-station"})foreach(string mode in new[]{"none","normal","full"})
                if(string.IsNullOrEmpty(fixtureFilter) || fixtureFilter==theme+"/"+mode){report.AppendLine(One(Procedural.Generator.Generate(77,mode,theme,true,true),theme+"/"+mode));fixtures++;}
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("STAR_RACING_ROAD_QA_DIR"),"cell-certification.txt"),report.ToString());
            Debug.Log("ROAD_CELL_DIAGNOSTIC_COMPLETE fixtures="+fixtures+" productionSupportsCreated=0 geometryChanged=false");
        }
    }
}
