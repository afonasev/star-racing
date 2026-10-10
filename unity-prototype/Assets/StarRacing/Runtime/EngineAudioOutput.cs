using System;
using UnityEngine;
namespace StarRacingPrototype {
 // Generate the current mixer block rather than filling a streaming clip ahead.
 [RequireComponent(typeof(AudioSource))]
 public sealed class EngineAudioOutput : MonoBehaviour {
  volatile EngineAudioDsp generator;
  volatile int renderedBlocks;
  public int RenderedBlocks => renderedBlocks;
  public void Bind(EngineAudioDsp value){generator=value;}
  void OnAudioFilterRead(float[] data,int channels){
   var current=generator;
   if(current==null){Array.Clear(data,0,data.Length);return;}
   current.RenderInterleaved(data,channels);renderedBlocks++;
  }
 }
}
