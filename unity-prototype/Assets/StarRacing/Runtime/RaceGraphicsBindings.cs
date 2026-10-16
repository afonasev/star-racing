using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
namespace StarRacingPrototype {
 public sealed class RaceGraphicsBindings:ScriptableObject {
  public ScriptableRendererFeature contactShadows;
  static readonly Dictionary<ScriptableRendererFeature,List<Lease>> owners=new Dictionary<ScriptableRendererFeature,List<Lease>>();
  public Lease Acquire(){if(contactShadows==null)throw new InvalidOperationException("Graphics runtime SSAO binding missing");return new Lease(contactShadows);}
  // Only mutate the live feature flag. Never persist or destroy the renderer asset.
  public sealed class Lease:IDisposable {
   ScriptableRendererFeature feature;readonly bool original;bool desired;
   internal Lease(ScriptableRendererFeature value){feature=value;if(!owners.TryGetValue(value,out var list)){list=new List<Lease>();owners.Add(value,list);}original=list.Count==0?value.isActive:list[0].original;desired=value.isActive;list.Add(this);}
   public bool Active=>feature!=null&&feature.isActive;
   public void Apply(bool enabled){if(feature==null)throw new ObjectDisposedException("Graphics feature lease");desired=enabled;feature.SetActive(enabled);}
   public void Dispose(){if(feature==null)return;var value=feature;feature=null;var list=owners[value];list.Remove(this);if(list.Count==0){value.SetActive(original);owners.Remove(value);}else value.SetActive(list[list.Count-1].desired);}
  }
 }
}
