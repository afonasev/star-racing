using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using UnityEngine;

namespace StarRacingPrototype {
    public static class RosterFixtureEquivalenceChecks {
        public static string Fingerprint(TrackRoute route) {
            // TrackFrame is not [Serializable]; explicitly include every stored field.
            using(var bytes = new MemoryStream()) using(var w = new BinaryWriter(bytes)) {
                w.Write(JsonUtility.ToJson(route.Definition));
                w.Write(route.Length);w.Write(route.StartDistance);w.Write(route.FinishDistance);
                w.Write(route.LoopStart);w.Write(route.LoopEnd);w.Write(route.JumpStart);w.Write(route.JumpEnd);w.Write(route.IsClosed);
                w.Write(route.Samples.Length);
                foreach(var f in route.Samples) {
                    w.Write(f.distance);w.Write(f.halfWidth);w.Write(f.segmentId);w.Write(f.gap);
                    Write(w,f.position);Write(w,f.tangent);Write(w,f.normal);Write(w,f.right);
                }
                w.Flush();using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes.ToArray())).Replace("-","");
            }
        }
        static void Write(BinaryWriter w,Vector3 v){w.Write(v.x);w.Write(v.y);w.Write(v.z);}
        static string Observations(TrackRoute route,int count) {
            var result=new StringBuilder();var roster=new RaceRoster(count,2,77);
            foreach(var e in roster.Entrants) {
                result.Append(e.Id).Append(':').Append(e.HumanSeat).Append(':').Append(e.Profile).Append(':').Append(e.Seed).Append(':').Append(e.GridSlot).Append(':');
                result.Append(RaceRoster.GridDistance(route,e.GridSlot).ToString("R",System.Globalization.CultureInfo.InvariantCulture)).Append(':');
                result.Append(RaceRoster.GridLateral(route,e.GridSlot).ToString("R",System.Globalization.CultureInfo.InvariantCulture)).Append(';');
            }
            return result.ToString();
        }
        public static void Run() {
            int cases=0;var watch=System.Diagnostics.Stopwatch.StartNew();
            Debug.Log("ROSTER_EQUIVALENCE_BEGIN utc="+DateTime.UtcNow.ToString("O"));
            foreach(string theme in new[]{"cloud-city","space-station"}) {
                var reused=new TrackRoute(Procedural.Generator.Generate(77,"normal",theme,false,true));
                string initial=Fingerprint(reused);
                foreach(int count in new[]{2,8,64}) {
                    var fresh=new TrackRoute(Procedural.Generator.Generate(77,"normal",theme,false,true));
                    if(Fingerprint(fresh)!=initial)throw new Exception("Fresh route differs: "+theme);
                    if(Observations(fresh,count)!=Observations(reused,count))throw new Exception("Roster observation differs: "+theme+count);
                    if(Fingerprint(reused)!=initial||Fingerprint(fresh)!=initial)throw new Exception("Roster mutated geometry: "+theme+count);
                    cases++;
                }
                Debug.Log("ROSTER_FIXTURE_FINGERPRINT theme="+theme+" hash="+initial);
            }
            if(cases!=6)throw new Exception("Missing equivalence case");
            Debug.Log("ROSTER_EQUIVALENCE_SECONDS "+watch.Elapsed.TotalSeconds.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
            Debug.Log("ROSTER_FIXTURE_EQUIVALENCE_OK cases="+cases+" token="+(Environment.GetEnvironmentVariable("STAR_RACING_QA_TOKEN")??"manual"));
        }
    }
}
