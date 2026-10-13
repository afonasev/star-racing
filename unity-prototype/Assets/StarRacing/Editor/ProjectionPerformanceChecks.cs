using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
namespace StarRacingPrototype {
    // Opt-in CPU microbenchmark; no Player or native physics timing claims.
    public static class ProjectionPerformanceChecks {
        public static void Run() {
            string output=Environment.GetEnvironmentVariable("STAR_RACING_PROJECTION_QA_DIR");
            if(string.IsNullOrEmpty(output))throw new ArgumentException("STAR_RACING_PROJECTION_QA_DIR required");
            Directory.CreateDirectory(output);
            TrackProjectionParityChecks.Run();
            using(var samples=new BinaryWriter(File.Create(Path.Combine(output,"frames.bin"))))
            using(var timing=new StreamWriter(Path.Combine(output,"timing.csv"))) {
                timing.WriteLine("theme,repeat,queries,milliseconds,checksum");
                foreach(string theme in new[]{"cloud-city","space-station"}) {
                    var route=new TrackRoute(Procedural.Generator.Generate(2273265884,"normal",theme,true,true));
                    var positions=new Vector3[256];var progress=new float[256];var random=new System.Random(77);
                    for(int i=0;i<positions.Length;i++) {
                        progress[i]=i==0?0:i==1?route.Length:(float)random.NextDouble()*route.Length;
                        var basis=route.Evaluate(progress[i]);
                        positions[i]=basis.position+basis.right*((float)random.NextDouble()*60-30)+basis.normal*((float)random.NextDouble()*80-40);
                        // Both projection and direct evaluation include complete frame identity.
                        Write(samples,route.Project(positions[i],progress[i],30));Write(samples,route.Evaluate(progress[i]));
                    }
                    float checksum=0;
                    for(int i=0;i<8192;i++)checksum+=route.Project(positions[i%256],progress[i%256],30).distance;
                    for(int repeat=0;repeat<5;repeat++) {
                        var watch=Stopwatch.StartNew();checksum=0;
                        for(int i=0;i<65536;i++)checksum+=route.Project(positions[i%256],progress[i%256],30).distance;
                        watch.Stop();timing.WriteLine(theme+","+repeat+",65536,"+watch.Elapsed.TotalMilliseconds.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+checksum.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
            }
            UnityEngine.Debug.Log("PROJECTION_PERFORMANCE_OK exactParityCases=768 capturedFrames=1024 measuredQueries=655360");
        }
        static void Write(BinaryWriter w,TrackFrame f){w.Write(f.distance);Write(w,f.position);Write(w,f.tangent);Write(w,f.normal);Write(w,f.right);w.Write(f.halfWidth);w.Write(f.gap);w.Write(f.segmentId);}
        static void Write(BinaryWriter w,Vector3 v){w.Write(v.x);w.Write(v.y);w.Write(v.z);}
    }
}
