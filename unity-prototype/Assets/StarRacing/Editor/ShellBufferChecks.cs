using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEngine;
namespace StarRacingPrototype {
 public static class ShellBufferChecks {
  // Cancel in RoadVolumeMesh itself after reserve and partial core processing,
  // before shell upload. This complements the earlier top-chunk cancellation.
  public static void RunCancellation(){
   const int count=16384;var top=new List<Vector3>(count*4);var faces=new List<RoadFace>(count*2);var indices=new List<int>(count*6);
   for(int q=0;q<count;q++){
    int at=top.Count;top.Add(new Vector3(q,0,0));top.Add(new Vector3(q+1,0,0));top.Add(new Vector3(q,0,-1));top.Add(new Vector3(q+1,0,-1));
    var face=new RoadFace{geometryId=1,startDistance=q,endDistance=q+1,leftStart=0,leftEnd=0,rightStart=1,rightEnd=1};faces.Add(face);faces.Add(face);
    foreach(int i in new[]{0,1,2,2,1,3})indices.Add(at+i);
   }
   var root=new GameObject("Shell reserve cancellation");var old=RoadVolumeMesh.FrameObserved;int frames=0;bool partial=false;double first=0,maximum=0;
   RoadVolumeMesh.FrameObserved=(_,__,___)=>frames++;
   try{
    Func<RoadFace,float,TrackFrame> evaluate=(_,distance)=>new TrackFrame{distance=distance,position=new Vector3(distance,0,0),normal=Vector3.up,right=Vector3.forward,tangent=Vector3.right,halfWidth=1};
    var steps=(IEnumerator)typeof(RoadVolumeMesh).GetMethod("BuildCore",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{root.transform,evaluate,top,faces,indices,null,null,null,false,false});
    using(var work=new LoadingWork(steps)){
     int calls=0;while(true){var clock=Stopwatch.StartNew();bool more=work.MoveNext();clock.Stop();double elapsed=clock.Elapsed.TotalSeconds;if(calls++==0)first=elapsed;maximum=Math.Max(maximum,elapsed);if(!more)break;if(frames>2){partial=true;break;}}
     if(!partial||frames>=count*2||root.transform.childCount!=0||work.Succeeded)throw new Exception("Shell partial cancellation boundary missing");
     work.Dispose();int before=frames;if(work.MoveNext()||frames!=before||root.transform.childCount!=0)throw new Exception("Shell work continued after disposal");
    }
    string output=Environment.GetEnvironmentVariable("STAR_RACING_WORLD_QA_DIR");Directory.CreateDirectory(output);
    File.WriteAllText(Path.Combine(output,"shell-cancellation.json"),"{\"partialFrames\":"+frames+",\"firstMoveSeconds\":"+first.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+",\"maxMoveSeconds\":"+maximum.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+",\"children\":0,\"lateWork\":false}");
    UnityEngine.Debug.Log("SHELL_BUFFER_CANCELLATION_OK frames="+frames);
   }finally{RoadVolumeMesh.FrameObserved=old;UnityEngine.Object.DestroyImmediate(root);}
  }
  public static void RunAffected(){RunCancellation();OwnerPreparationChecks.RunDataContracts();UnityEngine.Debug.Log("SHELL_BUFFER_AFFECTED_OK");}
 }
}
