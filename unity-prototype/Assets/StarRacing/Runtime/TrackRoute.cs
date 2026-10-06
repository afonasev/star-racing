using System;
using System.Collections.Generic;
using UnityEngine;

namespace StarRacingPrototype {
    /// <summary>Procedural track geometry, never vehicle state.</summary>
    public sealed class TrackRoute {
        public TrackFrame[] Samples;
        public Procedural.Definition Definition { get; private set; }
        Procedural.Span[][] paved;
        Procedural.SpanPair[][] roadPanels;
        public float Length;
        public float LoopStart;
        public float LoopEnd;
        public float JumpStart;
        public float JumpEnd;
        public bool IsClosed { get; }
        public float StartDistance => IsClosed ? 8f : 30f;
        public float FinishDistance { get; private set; }

        const float HalfWidth = 8f;

        public TrackRoute(Procedural.Definition definition) {
            IsClosed = false;
            Definition = definition;
            var source = definition.samples;
            roadPanels=new Procedural.SpanPair[source.Length-1][];
            for(int i=0;i<roadPanels.Length;i++)roadPanels[i]=Procedural.Layout.Pairs(definition,source[i],source[i+1]);
            paved = new Procedural.Span[source.Length][];
            for (int i=0;i<source.Length;i++) paved[i]=Procedural.Layout.Paved(definition,source[i]);
            var native = new List<TrackFrame>();
            var first = source[0];
            for (int i = 0; i < 15; i++) native.Add(FromSource(first, i * 2, first.position.Float - first.tangent.Float * (30 - i * 2)));
            foreach (var sample in source) native.Add(FromSource(sample, (float)sample.distance + 30, sample.position.Float));
            FinishDistance = (float)definition.totalLength + 30;
            var last = source[source.Length - 1];
            for (int i = 1; i <= 80; i++) native.Add(FromSource(last, FinishDistance + i * 2, last.position.Float + last.tangent.Float * (i * 2)));
            Samples = native.ToArray(); Length = FinishDistance + 160;
            var loops = Procedural.Generator.Runs(source, "loop");
            if (loops.Count > 0) { LoopStart = (float)source[loops[0].start].distance + 30; LoopEnd = LoopStart + loops[0].length * 5; }
        }

        static TrackFrame FromSource(Procedural.Sample sample, float distance, Vector3 position) {
            return new TrackFrame { distance = distance, position = position, tangent = sample.tangent.Float,
                normal = sample.normal.Float, right = -sample.right.Float, halfWidth = (float)sample.halfWidth, segmentId = sample.index };
        }

        public Procedural.Span[] PavedAt(float distance) {
            if (Definition == null) return new[] { new Procedural.Span(0, Evaluate(distance).halfWidth) };
            return paved[SourceAt(distance).index];
        }
        public Procedural.Sample SourceAt(float distance) {
            int index = Mathf.Clamp(Mathf.FloorToInt((distance - 30) / 5), 0, Definition.samples.Length - 1);
            return Definition.samples[index];
        }
        // Each panel is the same convex lateral strip used by the road mesh. A linear path
        // contained at both ends of a panel remains contained between them, including its margin.
        public bool SupportsCorridorSegment(float from,float fromLane,float to,float toLane,float margin) {
            if(to<from)return false;
            if(Definition==null)return Mathf.Max(Mathf.Abs(fromLane),Mathf.Abs(toLane))+margin<=Mathf.Min(Evaluate(from).halfWidth,Evaluate(to).halfWidth);
            float at=from;
            while(at<to-.0001f){
                int index=Mathf.FloorToInt((at-30)/5);
                float end=Mathf.Min(to,30+(index+1)*5);
                float a=Mathf.Lerp(fromLane,toLane,Mathf.InverseLerp(from,to,at)),b=Mathf.Lerp(fromLane,toLane,Mathf.InverseLerp(from,to,end));
                bool supported=false;
                if(index<0||index>=roadPanels.Length)supported=Mathf.Max(Mathf.Abs(a),Mathf.Abs(b))+margin<=Evaluate(at).halfWidth;
                else foreach(var panel in roadPanels[index]){
                    float u=(at-(30+index*5))/5,v=(end-(30+index*5))/5;
                    float leftA=Mathf.Lerp(-(float)panel.first.Right,-(float)panel.second.Right,u),rightA=Mathf.Lerp(-(float)panel.first.Left,-(float)panel.second.Left,u);
                    float leftB=Mathf.Lerp(-(float)panel.first.Right,-(float)panel.second.Right,v),rightB=Mathf.Lerp(-(float)panel.first.Left,-(float)panel.second.Left,v);
                    if(a-margin>=leftA-.002f&&a+margin<=rightA+.002f&&b-margin>=leftB-.002f&&b+margin<=rightB+.002f){supported=true;break;}
                }
                if(!supported)return false;
                at=end;
            }
            return true;
        }
        public bool RoadDeficits(float distance,float lateral,out float leftDeficit,out float rightDeficit){
            leftDeficit=rightDeficit=0;if(Definition==null)return false;
            int index=Mathf.FloorToInt((distance-30)/5);if(index<0||index>=roadPanels.Length)return false;
            float best=float.NegativeInfinity,t=(distance-(30+index*5))/5;
            foreach(var panel in roadPanels[index]){float left=Mathf.Lerp(-(float)panel.first.Right,-(float)panel.second.Right,t),right=Mathf.Lerp(-(float)panel.first.Left,-(float)panel.second.Left,t),clearance=Mathf.Min(lateral-left,right-lateral);if(clearance>=0&&clearance>best){best=clearance;leftDeficit=Mathf.Max(0,2.1f-(lateral-left));rightDeficit=Mathf.Max(0,2.1f-(right-lateral));}}
            return best>=0;
        }
        public bool SupportsInteriorReturn(float from,float fromLane,float to,float toLane,ref float leftDeficit,ref float rightDeficit){
            int index=Mathf.FloorToInt((from-30)/5);if(Definition==null||index<0||index>=roadPanels.Length||to>30+(index+1)*5+.002f)return false;
            float u=(from-(30+index*5))/5,v=(to-(30+index*5))/5;
            foreach(var panel in roadPanels[index]){
                float la=Mathf.Lerp(-(float)panel.first.Right,-(float)panel.second.Right,u),ra=Mathf.Lerp(-(float)panel.first.Left,-(float)panel.second.Left,u);
                float lb=Mathf.Lerp(-(float)panel.first.Right,-(float)panel.second.Right,v),rb=Mathf.Lerp(-(float)panel.first.Left,-(float)panel.second.Left,v);
                if(fromLane<la||fromLane>ra||toLane<lb||toLane>rb)continue;
                float aLeft=Mathf.Max(0,2.1f-(fromLane-la)),aRight=Mathf.Max(0,2.1f-(ra-fromLane)),bLeft=Mathf.Max(0,2.1f-(toLane-lb)),bRight=Mathf.Max(0,2.1f-(rb-toLane));
                if(aLeft<=leftDeficit+.002f&&aRight<=rightDeficit+.002f&&bLeft<=aLeft+.002f&&bRight<=aRight+.002f){leftDeficit=bLeft;rightDeficit=bRight;return true;}
            }
            return false;
        }
        public bool IsJumpRegion(float distance) {
            if (Definition == null) return distance >= JumpStart && distance <= JumpEnd && JumpEnd > JumpStart;
            float index = (distance - 30) / 5;
            foreach (var jump in Definition.jumps) if(index >= jump.rampStartIndex && index <= jump.landingEndIndex) return true;
            return false;
        }

        public Procedural.Jump RampAtContact(Vector3 point,float progress) {
            if(Definition==null)return null;
            var frame=Evaluate(progress);
            float contactDistance=progress+Vector3.Dot(point-frame.position,frame.tangent);
            float lateral=-Vector3.Dot(point-frame.position,frame.right);
            float height=Vector3.Dot(point-frame.position,frame.normal);
            foreach(var jump in Definition.jumps) {
                if(contactDistance>=30+jump.rampStartIndex*5 && contactDistance<=30+jump.launchIndex*5+.1f &&
                   Mathf.Abs(lateral-(float)jump.lateralCenter)<=(float)jump.lateralHalfWidth && height>.05f)return jump;
            }
            return null;
        }
        public bool CanLaunch(Procedural.Jump jump,Vector3 position,float distance,Vector3 velocity) {
            if(jump==null)return false;
            var frame=Evaluate(30+jump.launchIndex*5);
            float lateral=-Vector3.Dot(position-frame.position,frame.right);
            return Mathf.Abs(distance-frame.distance)<=6 && Mathf.Abs(lateral-(float)jump.lateralCenter)<=(float)jump.lateralHalfWidth-.5f &&
                Vector3.Dot(velocity,frame.tangent)>5 && Vector3.Dot(velocity,frame.normal)>.1f;
        }
        public bool InFlightCorridor(Procedural.Jump jump,Vector3 position,float distance) {
            if(jump==null || distance<30+jump.rampStartIndex*5 || distance>30+jump.landingEndIndex*5)return false;
            var frame=Evaluate(distance);
            float lateral=Vector3.Dot(position-frame.position,frame.right),height=Vector3.Dot(position-frame.position,frame.normal);
            return Mathf.Abs(lateral)<=frame.halfWidth+1 && height>=-.6f && height<20;
        }

        public bool ContinuousLoopAt(float distance) => !IsJumpRegion(distance) && PavedAt(distance).Length>0 && (Definition==null ? distance>=LoopStart && distance<=LoopEnd : SourceAt(distance).kind=="loop");
        public void EvaluateBasis(float distance,out Vector3 tangent,out Vector3 normal){var frame=Evaluate(distance);tangent=frame.tangent;normal=frame.normal;}
        public Vector3 EvaluateTangent(float distance)=>Evaluate(distance).tangent;
        public TrackFrame Evaluate(float distance) {
            float d = Wrap(distance);
            if (!IsClosed && d >= Length) return Samples[Samples.Length - 1];
            int lo = FindSegment(d);
            TrackFrame a = Samples[lo];
            TrackFrame b = Samples[(lo + 1) % Samples.Length];
            float end = lo == Samples.Length - 1 ? Length : b.distance;
            float t = Mathf.InverseLerp(a.distance, end, d);
            var result = MakeFrame(d, Vector3.Lerp(a.position, b.position, t),
                Vector3.Slerp(a.tangent, b.tangent, t).normalized,
                Vector3.Slerp(a.normal, b.normal, t).normalized,
                t < .5f ? a.gap : b.gap, t < .5f ? a.segmentId : b.segmentId);
            result.halfWidth = Mathf.Lerp(a.halfWidth, b.halfWidth, t);
            return result;
        }

        public TrackFrame Project(Vector3 position, float previousDistance, float window = 20f) {
            float previous = Wrap(previousDistance);
            float bestScore = float.PositiveInfinity;
            float bestDistance = previous;
            for (int i = 0; i < Samples.Length - (IsClosed ? 0 : 1); i++) {
                TrackFrame a = Samples[i];
                TrackFrame b = Samples[(i + 1) % Samples.Length];
                float end = i == Samples.Length - 1 ? Length : b.distance;
                float segmentLength = end - a.distance;
                Vector3 ab = b.position - a.position;
                float t = ab.sqrMagnitude < .0001f ? 0f : Mathf.Clamp01(Vector3.Dot(position - a.position, ab) / ab.sqrMagnitude);
                float candidate = a.distance + segmentLength * t;
                if (Mathf.Abs(WrappedDelta(candidate, previous)) > window) continue;
                float score = (position - Vector3.Lerp(a.position, b.position, t)).sqrMagnitude;
                if (score < bestScore) { bestScore = score; bestDistance = candidate; }
            }
            var frame = Evaluate(bestDistance);
            if (Definition != null) {
                var spans = PavedAt(bestDistance);
                float lateral = -Vector3.Dot(position - frame.position, frame.right);
                double best = double.PositiveInfinity;
                foreach (var span in spans) {
                    double separation = Math.Max(0, Math.Abs(lateral - span.offset) - span.halfWidth);
                    if (separation < best) { best = separation; frame = Evaluate(bestDistance); frame.position -= frame.right * (float)span.offset; frame.halfWidth = (float)span.halfWidth; }
                }
                frame.gap = spans.Length == 0;
            }
            return frame;
        }

        public float WrappedDelta(float target, float from) {
            float delta = Wrap(target) - Wrap(from);
            if (!IsClosed) return delta;
            if (delta > Length * .5f) delta -= Length;
            if (delta < -Length * .5f) delta += Length;
            return delta;
        }

        public float Wrap(float distance) => IsClosed ? Mathf.Repeat(distance, Length) : Mathf.Clamp(distance, 0, Length);

        int FindSegment(float distance) {
            int low = 0, high = Samples.Length - 1;
            while (low < high) { int mid = (low + high + 1) / 2; if (Samples[mid].distance <= distance) low = mid; else high = mid - 1; }
            return low;
        }

        static TrackFrame MakeFrame(float distance, Vector3 position, Vector3 tangent, Vector3 normal, bool gap, int segmentId) {
            tangent.Normalize(); normal = Vector3.ProjectOnPlane(normal, tangent).normalized;
            return new TrackFrame { distance = distance, position = position, tangent = tangent, normal = normal, right = Vector3.Cross(normal, tangent).normalized, halfWidth = HalfWidth, gap = gap, segmentId = segmentId };
        }
    }
}
