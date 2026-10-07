using System;
using UnityEngine;

namespace StarRacingPrototype {
 [Serializable]
 public sealed class LocalRaceConfig {
  public const string PreferenceKey="StarRacing.LocalRace.v1";
  public static readonly int[] ParticipantCounts={8,16,32,64};
  public static readonly string[] ThemeIds={"random","cloud-city","space-station"};
  public static readonly string[] ThemeNames={"Случайная тема","Город в облаках","Космическая станция"};
  public static readonly string[] RailIds={"normal","full","none"};
  public static readonly string[] RailNames={"Обычные","Полные","Без ограждений"};
  public int version=1,entrants=8,humans=2;
  public int[] devices={-2,-1,0,1};
  public const int MaxNameLength=10;
  public string[] names={"Игрок 1","Игрок 2","Игрок 3","Игрок 4"};
  public static string CleanName(string name){var result="";foreach(char c in name??"")if(!char.IsControl(c)&&!char.IsSurrogate(c)&&result.Length<MaxNameLength)result+=c;return result;}
  public string PlayerName(int seat)=>string.IsNullOrWhiteSpace(names[seat])?"Игрок "+(seat+1):names[seat];
  public void RandomizeSeed(){uint previous=Seed,value;do{var bytes=Guid.NewGuid().ToByteArray();value=BitConverter.ToUInt32(bytes,0);}while(value==previous);seed=value.ToString();}
  public string seed="77",theme="random",rails="normal";
  public bool jumps,handicap;
  public uint Seed=>uint.Parse(seed);
  public LocalRaceConfig Copy()=>JsonUtility.FromJson<LocalRaceConfig>(JsonUtility.ToJson(this));
  public bool Validate(out string error){
   error="";
   if(version!=1)error="Неизвестная версия настроек";
   else if(humans<0||humans>4||devices==null||devices.Length!=4||names==null||names.Length!=4)error="Выберите до 4 игроков";
   else if(Array.IndexOf(ParticipantCounts,entrants)<0)error="Выберите 8, 16, 32 или 64 участника";
   else if(!uint.TryParse(seed,out _))error="Seed: целое число от 0 до 4294967295";
   else if(Array.IndexOf(ThemeIds,theme)<0||Array.IndexOf(RailIds,rails)<0)error="Выберите тему и ограждения из списка";
   if(error.Length==0)for(int i=0;i<humans;i++){
    if(names[i]!=CleanName(names[i])){error="Слишком длинное имя игрока";break;}
    if(devices[i]<-2){error="Неизвестное устройство игрока "+(i+1);break;}
    for(int j=0;j<i;j++)if(devices[i]==devices[j])error="У каждого игрока должно быть своё устройство";
   }
   return error.Length==0;
  }
  public static LocalRaceConfig Normalize(LocalRaceConfig config){
   config=config??new LocalRaceConfig();config.version=1;
   config.humans=Mathf.Clamp(config.humans,0,4);
   if(config.devices==null||config.devices.Length!=4)config.devices=new[]{-2,-1,0,1};
   for(int i=0;i<4;i++){if(config.devices[i]<-2)config.devices[i]=i-2;
    for(int j=0;j<i;j++)if(config.devices[i]==config.devices[j]){for(int d=-2;d<4;d++){bool used=false;for(int k=0;k<i;k++)used|=config.devices[k]==d;if(!used){config.devices[i]=d;break;}}}}
   if(config.names==null||config.names.Length!=4)config.names=new[]{"Игрок 1","Игрок 2","Игрок 3","Игрок 4"};
   for(int i=0;i<4;i++)config.names[i]=CleanName(config.names[i]);
   if(Array.IndexOf(ParticipantCounts,config.entrants)<0)config.entrants=8;
   if(!uint.TryParse(config.seed,out uint value))value=77;config.seed=value.ToString();
   if(Array.IndexOf(ThemeIds,config.theme)<0)config.theme="random";
   if(Array.IndexOf(RailIds,config.rails)<0)config.rails="normal";
   return config;
  }
  public static LocalRaceConfig Load(){
   if(PlayerPrefs.HasKey(PreferenceKey)){
    try{var data=JsonUtility.FromJson<LocalRaceConfig>(PlayerPrefs.GetString(PreferenceKey));if(data!=null&&data.version==1)return Normalize(data);}catch(ArgumentException){}
   }
   return Normalize(new LocalRaceConfig {entrants=PlayerPrefs.GetInt("StarRacing.Entrants",8),
    seed=PlayerPrefs.GetString("StarRacing.TrackSeed","77"),theme=PlayerPrefs.GetString("StarRacing.TrackTheme","random"),
    rails=PlayerPrefs.GetString("StarRacing.TrackRails","normal"),jumps=PlayerPrefs.GetInt("StarRacing.TrackJumps",0)!=0,
    handicap=PlayerPrefs.GetInt("StarRacing.AiHandicap",0)!=0});
  }
  public void Save(){if(!Validate(out var error))throw new ArgumentException(error);PlayerPrefs.SetString(PreferenceKey,JsonUtility.ToJson(this));PlayerPrefs.Save();}
 }
}
