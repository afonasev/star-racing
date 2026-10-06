using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace StarRacingPrototype {
 public sealed class ReleaseBalance {
  public const int Version = 8;
  readonly Dictionary<string,float> values;
  public string Hash {get;}
  public float this[string key] => values[key];
  public int Count => values.Count;
  ReleaseBalance(Dictionary<string,float> values,string json) {
   this.values=values;
   using(var hash=SHA256.Create()) Hash=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-","").ToLowerInvariant();
  }
  static readonly Dictionary<string,(double min,double max)> ranges=new Dictionary<string,(double,double)> {
   {"baseSpeedKmh",(100d,400d)},
   {"nitroMaxSpeedKmh",(100d,500d)},
   {"acceleration",(1d,100d)},
   {"highSpeedSteeringRetention",(0.1d,0.7d)},
   {"steeringResponseSeconds",(0.05d,0.4d)},
   {"yawResponseSeconds",(0.05d,0.5d)},
   {"regularGrip",(1.5d,8d)},
   {"driftGrip",(0.2d,2d)},
   {"driftEntryYawImpulse",(0d,1.5d)},
   {"counterSteerStrength",(0.5d,2.5d)},
   {"gripRecoverySeconds",(0.05d,0.8d)},
   {"brakeDeceleration",(15d,60d)},
   {"brakeYawInfluence",(0d,2d)},
   {"throttleDriftInfluence",(0d,1.5d)},
   {"slipDrag",(0d,0.6d)},
   {"nitroAcceleration",(1d,150d)},
   {"nitroCapacity",(1d,1000d)},
   {"nitroDrainPerSecond",(0.1d,500d)},
   {"nitroRecoveryPerSecond",(0d,500d)},
   {"nitroRecoveryBehindMultiplier",(1d,10d)},
   {"aiOvertakeAggression",(0d,1d)},
   {"aiNitroReserve",(0d,1000d)},
   {"aiNitroStraightDistance",(20d,200d)},
   {"racerNitroRecoveryMultiplier",(1d,5d)},
   {"aceNitroRecoveryMultiplier",(1d,5d)},
   {"racerAccelerationMultiplier",(1d,3d)},
   {"aceAccelerationMultiplier",(1d,3d)},
   {"contactSeparationPerStep",(0.01d,2d)},
   {"contactLateralDamping",(0d,1d)},
   {"contactLongitudinalDamping",(0d,1d)},
   {"trackHairpinMinGapSegments",(1d,20d)},
   {"trackMinimumLongCurves",(0d,20d)},
   {"trackCurvatureAlternation",(0d,1d)},
   {"trackMinimumDriftCurves",(0d,20d)},
   {"trackMinimumTightHairpins",(0d,20d)},
   {"trackDriftCurveMinLength",(40d,600d)},
   {"trackDriftCurveMaxLength",(40d,600d)},
   {"trackDriftCurveMinTurnDegrees",(10d,179d)},
   {"trackDriftCurveMaxTurnDegrees",(10d,179d)},
   {"trackTightHairpinMinLength",(40d,300d)},
   {"trackTightHairpinMaxLength",(40d,300d)},
   {"trackTightHairpinMinTurnDegrees",(90d,179d)},
   {"trackDemandingTurnMinGap",(40d,1200d)},
   {"trackDemandingTurnMaxGap",(100d,2000d)},
   {"trackPatternMinCount",(1d,4d)},
   {"trackPatternMaxCount",(2d,6d)},
   {"trackSpiralTurns",(2d,3d)},
   {"trackChicaneIntensity",(0.4d,2d)},
   {"trackWideSectionChance",(0d,1d)},
   {"trackWideSectionWidthMultiplier",(1.2d,2.5d)},
  };
  public static ReleaseBalance Parse(string json) {
   if(string.IsNullOrWhiteSpace(json))throw new FormatException("Release balance file is missing or empty");
   var root=new Reader(json).Read();
   if(root.Count!=2 || !root.TryGetValue("version",out var version) || !(version is double v) || v!=Version || !root.TryGetValue("values",out var payload) || !(payload is Dictionary<string,object> fields))
    throw new FormatException("Expected balance version 8 and values only");
   if(fields.Count!=ranges.Count)throw new FormatException("Balance values have missing or unknown keys");
   var result=new Dictionary<string,float>();
   foreach(var pair in ranges) {
    if(!fields.TryGetValue(pair.Key,out var raw) || !(raw is double number) || double.IsNaN(number) || double.IsInfinity(number) || number<pair.Value.min || number>pair.Value.max)
     throw new FormatException("Invalid balance value: "+pair.Key);
    result.Add(pair.Key,(float)number);
   }
   foreach(var pair in new[]{("nitroMaxSpeedKmh","baseSpeedKmh"),("trackDriftCurveMaxLength","trackDriftCurveMinLength"),("trackDriftCurveMaxTurnDegrees","trackDriftCurveMinTurnDegrees"),("trackTightHairpinMaxLength","trackTightHairpinMinLength"),("trackDemandingTurnMaxGap","trackDemandingTurnMinGap"),("trackPatternMaxCount","trackPatternMinCount")})
    if(result[pair.Item1]<result[pair.Item2])throw new FormatException(pair.Item1+" must be >= "+pair.Item2);
   return new ReleaseBalance(result,json);
  }
  // Restricted JSON grammar: objects and finite numbers only, exactly what v8 permits.
  // Duplicate keys, strings as numbers, trailing data/commas and nested excess are errors.
  sealed class Reader {
   readonly string text; int at;
   static readonly Regex number=new Regex(@"\G-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?",RegexOptions.CultureInvariant);
   public Reader(string text){this.text=text;}
   void White(){while(at<text.Length && (text[at]==' '||text[at]=='\r'||text[at]=='\n'||text[at]=='\t'))at++;}
   void Take(char c){White();if(at>=text.Length||text[at++]!=c)throw new FormatException("Invalid balance JSON at "+at);}
   public Dictionary<string,object> Read(){var o=Object(0);White();if(at!=text.Length)throw new FormatException("Trailing balance JSON");return o;}
   Dictionary<string,object> Object(int depth){
    if(depth>1)throw new FormatException("Unexpected nested balance object");
    Take('{');var result=new Dictionary<string,object>();White();
    if(at<text.Length && text[at]=='}'){at++;return result;}
    while(true){
     Take('"');int start=at;
     while(at<text.Length && (char.IsLetterOrDigit(text[at])||text[at]=='_'))at++;
     string key=text.Substring(start,at-start);Take('"');Take(':');White();
     object value;
     if(at<text.Length && text[at]=='{')value=Object(depth+1);
     else {var match=number.Match(text,at);if(!match.Success)throw new FormatException("Expected numeric balance value: "+key);at+=match.Length;if(!double.TryParse(match.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out double parsed))throw new FormatException("Invalid numeric balance value: "+key);value=parsed;}
     if(result.ContainsKey(key))throw new FormatException("Duplicate balance key: "+key);result.Add(key,value);
     White();if(at<text.Length && text[at]=='}'){at++;return result;}Take(',');
    }
   }
  }
 }
}
