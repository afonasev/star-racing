using System.Threading;
using UnityEngine;
namespace StarRacingPrototype {
 public sealed class NativeTempoFilter : MonoBehaviour {
  NativeTempoPlayback playback;
  public void Bind(NativeTempoPlayback value)=>Volatile.Write(ref playback,value);
  void OnAudioFilterRead(float[] data,int channels){var current=Volatile.Read(ref playback);if(current!=null)current.Render(data,channels);}
 }
}
