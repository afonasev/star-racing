using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
namespace StarRacingPrototype {
 // The same nested iterator pipeline is drained synchronously by tools and once
 // per rendered frame by the live loading flow. Dispose unwinds all staging finally blocks.
 public sealed class LoadingWork : IEnumerator,IDisposable {
  readonly Stack<IEnumerator> stack=new Stack<IEnumerator>();bool disposed;
  public bool IsPending=>!disposed&&stack.Count>0;
  public Exception Error {get;private set;}
  public string Status {get;private set;}="Готовим гонку";
  public bool Succeeded {get;private set;}
  public object Current=>null;
  public LoadingWork(IEnumerator work){stack.Push(work);}
  public bool MoveNext(){
   if(disposed)return false;
   try {
    while(stack.Count>0){var step=stack.Peek();if(!step.MoveNext()){stack.Pop();(step as IDisposable)?.Dispose();continue;}
     if(step.Current is IEnumerator nested){stack.Push(nested);continue;}
     if(step.Current is string text)Status=text;return true;
    }
    Succeeded=true;return false;
   }catch(Exception error){Error=error;Dispose();return false;}
  }
  public void Dispose(){
   if(disposed)return;disposed=true;
   var errors=new List<Exception>();if(Error!=null)errors.Add(Error);
   while(stack.Count>0){try{(stack.Pop() as IDisposable)?.Dispose();}catch(Exception error){errors.Add(error);}}
   if(errors.Count>0){Error=errors.Count==1?errors[0]:new AggregateException(errors);Succeeded=false;}
  }
  public void Reset()=>throw new NotSupportedException();
  internal static Task<T> Background<T>(Func<T> action){var task=Task.Run(action);_=task.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);return task;}
  internal static void Drain(IEnumerator steps){using(var work=new LoadingWork(steps)){while(work.MoveNext()){}if(work.Error!=null)throw work.Error;}}
  public static bool SimulationFrozen=>freezes>0;static int freezes;
  [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetFreezes(){freezes=0;}
  internal sealed class Freeze : IDisposable {
   float restore=UnityEngine.Time.timeScale;bool disposed;
   public Freeze(){freezes++;UnityEngine.Time.timeScale=0;}
   public void Keep(float value){restore=value;}
   public void Dispose(){if(disposed)return;disposed=true;freezes--;UnityEngine.Time.timeScale=freezes>0?0:restore;}
  }
  internal sealed class Budget {
   long started=Stopwatch.GetTimestamp();
   public bool Due=>(Stopwatch.GetTimestamp()-started)/(double)Stopwatch.Frequency>=.004;
   public void Restart(){started=Stopwatch.GetTimestamp();}
  }
 }
}
