using System;
using System.Reflection;
using UnityEngine;
namespace StarRacingPrototype {
 public static class TrackProjectionParityChecks {
  static TrackFrame Reference(TrackRoute route,Vector3 position,float previousDistance,float window) {
            var Samples=route.Samples;bool IsClosed=route.IsClosed;float Length=route.Length;var Definition=route.Definition;
            var derivatives=typeof(TrackRoute).GetField("derivatives",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(route);
            float Wrap(float value)=>route.Wrap(value);
            float WrappedDelta(float a,float b)=>route.WrappedDelta(a,b);
            TrackFrame Evaluate(float value)=>route.Evaluate(value);
            var curve=typeof(TrackRoute).GetMethod("Curve",BindingFlags.Instance|BindingFlags.NonPublic);
            void Curve(int index,float t,out Vector3 point,out Vector3 first,out Vector3 second) {
                object[] values={index,t,null,null,null};curve.Invoke(route,values);point=(Vector3)values[2];first=(Vector3)values[3];second=(Vector3)values[4];
            }
            Procedural.Span[] PavedAt(float value)=>route.PavedAt(value);

            float previous = Wrap(previousDistance);
            float bestScore = float.PositiveInfinity;
            float bestDistance = previous;
            int bestSegment = 0;
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
                if (score < bestScore) { bestScore = score; bestDistance = candidate; bestSegment = i; }
            }
            if (derivatives != null) {
                bestScore = float.PositiveInfinity;
                for (int i = Mathf.Max(0, bestSegment - 1); i <= Mathf.Min(Samples.Length - 2, bestSegment + 1); i++) {
                    var a = Samples[i]; var b = Samples[i + 1];
                    float span = b.distance - a.distance;
                    float t = Mathf.Clamp01(Vector3.Dot(position - a.position, b.position - a.position) /
                        (b.position - a.position).sqrMagnitude);
                    for (int iteration = 0; iteration < 5; iteration++) {
                        Curve(i, t, out var point, out var first, out var second);
                        float denominator = first.sqrMagnitude + Vector3.Dot(point - position, second);
                        if (Mathf.Abs(denominator) < .00001f) break;
                        t = Mathf.Clamp01(t - Vector3.Dot(point - position, first) / denominator);
                    }
                    float candidate = a.distance + span * t;
                    if (Mathf.Abs(WrappedDelta(candidate, previous)) > window) continue;
                    Curve(i, t, out var nearest, out _, out _);
                    float score = (position - nearest).sqrMagnitude;
                    if (score < bestScore) { bestScore = score; bestDistance = candidate; }
                }
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
  public static void Run() {
   int cases=0;var random=new System.Random(77);
   foreach(string theme in new[]{"flat","cloud-city","space-station"}) {
    var route=new TrackRoute(theme=="flat"?RoadVolumeChecks.FlatRoad():Procedural.Generator.Generate(77,"full",theme,true,true));
    for(int i=0;i<256;i++) {
     float progress=i<4?new[]{0f,30f,route.Length-.001f,route.Length}[i]:(float)random.NextDouble()*route.Length;
     var basis=route.Evaluate(progress);var position=basis.position+basis.right*((float)random.NextDouble()*40-20)+basis.normal*((float)random.NextDouble()*12-6)+basis.tangent*((float)random.NextDouble()*80-40);
     float window=new[]{0f,.001f,20f,30f,-1f,route.Length*2,float.NaN,float.PositiveInfinity}[i%8];
     var expected=Reference(route,position,progress,window);var actual=route.Project(position,progress,window);
     if(!actual.Equals(expected))throw new Exception("Projection changed exhaustive result theme="+theme+" progress="+progress+" window="+window+" expected="+expected.distance+" actual="+actual.distance);
     cases++;
    }
   }
   Debug.Log("TRACK_PROJECTION_EXACT_PARITY_OK cases="+cases+" exhaustiveBaseline=true tolerances=none");
  }
 }
}
