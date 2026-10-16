using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StarRacingPrototype {
 public sealed class LabParameter {
  public readonly string Key,Group,Name,Description,Unit;public readonly decimal Step;
  public double Min=>ReleaseBalance.AllowedRange(Key).min;public double Max=>ReleaseBalance.AllowedRange(Key).max;
  public LabParameter(string key,string group,string name,string description,string unit,decimal step){Key=key;Group=group;Name=name;Description=description;Unit=unit;Step=step;}
  public string Format(decimal value)=>value.ToString("0.################",CultureInfo.InvariantCulture);
  public bool TryValue(string text,out decimal value){return decimal.TryParse(text.Replace(',','.'),NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out value)&&value>=(decimal)Min&&value<=(decimal)Max&&value%Step==0;}
 }
 public static class LabParameters {
  public static readonly string[] Groups={"Движение","Руление и дрифт","Нитро","AI"};
  public static readonly LabParameter[] All={
   new LabParameter("baseSpeedKmh",Groups[0],"Скорость без нитро","Предельная скорость обычного движения","км/ч",1),
   new LabParameter("acceleration",Groups[0],"Ускорение","Набор скорости при газе","м/с²",1),
   new LabParameter("brakeDeceleration",Groups[0],"Торможение","Снижение скорости при тормозе","м/с²",1),
   new LabParameter("highSpeedSteeringRetention",Groups[1],"Руль на скорости","Доля рулевого воздействия на высокой скорости","доля",.01m),
   new LabParameter("steeringResponseSeconds",Groups[1],"Отклик руления","Время реакции руля; меньше — быстрее","с",.01m),
   new LabParameter("yawResponseSeconds",Groups[1],"Отклик поворота","Время реакции автомобиля на поворот","с",.01m),
   new LabParameter("regularGrip",Groups[1],"Обычное сцепление","Удержание направления без заноса","коэф.",.01m),
   new LabParameter("driftGrip",Groups[1],"Сцепление в заносе","Удержание направления при дрифте","коэф.",.01m),
   new LabParameter("driftEntryYawImpulse",Groups[1],"Вход в занос","Дополнительный поворот при входе в дрифт","коэф.",.01m),
   new LabParameter("counterSteerStrength",Groups[1],"Контрруление","Сила стабилизации встречным рулением","коэф.",.01m),
   new LabParameter("gripRecoverySeconds",Groups[1],"Возврат сцепления","Время восстановления после заноса","с",.01m),
   new LabParameter("brakeYawInfluence",Groups[1],"Поворот на тормозе","Влияние торможения на поворот","коэф.",.01m),
   new LabParameter("throttleDriftInfluence",Groups[1],"Газ в заносе","Влияние газа на поворот в дрифте","коэф.",.01m),
   new LabParameter("slipDrag",Groups[1],"Потери в заносе","Снижение скорости при боковом скольжении","коэф.",.01m),
   new LabParameter("nitroMaxSpeedKmh",Groups[2],"Скорость с нитро","Максимальная скорость при усилении","км/ч",1),
   new LabParameter("nitroAcceleration",Groups[2],"Ускорение с нитро","Набор скорости при усилении","м/с²",1),
   new LabParameter("nitroCapacity",Groups[2],"Запас нитро","Максимальный заряд","ед.",1),
   new LabParameter("nitroDrainPerSecond",Groups[2],"Расход нитро","Расход заряда при усилении","ед./с",.1m),
   new LabParameter("nitroRecoveryPerSecond",Groups[2],"Восстановление нитро","Набор заряда без усиления","ед./с",.1m),
   new LabParameter("nitroRecoveryBehindMultiplier",Groups[2],"Заряд при отставании","Множитель восстановления позади соперника","×",.1m),
   new LabParameter("aiOvertakeAggression",Groups[3],"Агрессивность обгона","Решение AI об обгоне; возможности машины не меняются","доля",.01m),
   new LabParameter("aiNitroReserve",Groups[3],"Резерв нитро AI","Заряд, который AI старается сохранить","ед.",1),
   new LabParameter("aiNitroStraightDistance",Groups[3],"Прямая для нитро AI","Дистанция прямого участка для использования нитро","м",1)
  };
  public static IEnumerable<LabParameter> Search(string query)=>All.Where(p=>(p.Group+" "+p.Name+" "+p.Description+" "+p.Key).IndexOf(query??"",StringComparison.OrdinalIgnoreCase)>=0);
  internal static JObject Read(string json){using(var reader=new JsonTextReader(new StringReader(json))){reader.FloatParseHandling=FloatParseHandling.Decimal;var value=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});if(reader.Read())throw new FormatException("Лишние данные после JSON");return value;}}
  public static string Resolve(string raw,string defaults){
   var root=Read(raw);if(root.Properties().Count()!=2||(int?)root["version"]!=ReleaseBalance.Version||!(root["values"] is JObject values))throw new FormatException("Несовместимая схема баланса");
   bool changed=false;
   if(defaults!=null){var fallback=(JObject)Read(defaults)["values"];foreach(var pair in fallback)if(!values.ContainsKey(pair.Key)){values.Add(pair.Key,pair.Value.DeepClone());changed=true;}}
   string resolved=changed?root.ToString(Formatting.Indented):raw;ReleaseBalance.Parse(resolved);
   foreach(var p in All){var token=values[p.Key];if(token==null||!(token.Type==JTokenType.Float||token.Type==JTokenType.Integer)||!p.TryValue(token.Value<decimal>().ToString(CultureInfo.InvariantCulture),out _))throw new FormatException(p.Name+": значение не соответствует диапазону или шагу");}
   return resolved;
  }
 }
 public sealed class LabDraft {
  readonly Dictionary<string,decimal> baseline=new Dictionary<string,decimal>();
  public readonly string BaseJson;public readonly Dictionary<string,string> Inputs=new Dictionary<string,string>();
  public LabDraft(string json){BaseJson=json;var values=LabParameters.Read(json)["values"];foreach(var p in LabParameters.All){baseline[p.Key]=values[p.Key].Value<decimal>();Inputs[p.Key]=p.Format(baseline[p.Key]);}}
  public IEnumerable<LabParameter> Changes=>LabParameters.All.Where(p=>!p.TryValue(Inputs[p.Key],out var n)||n!=baseline[p.Key]);
  public bool Dirty=>Changes.Any();
  public string OldValue(LabParameter p)=>p.Format(baseline[p.Key]);
  public string Build(){var root=LabParameters.Read(BaseJson);foreach(var p in LabParameters.All){if(!p.TryValue(Inputs[p.Key],out var v))throw new FormatException(p.Name+": допустимо "+p.Min+"–"+p.Max+", шаг "+p.Step);root["values"][p.Key]=v;}return LabParameters.Resolve(root.ToString(Formatting.Indented),null);}
 }
 [Serializable] public sealed class LabRevision {public int number;public string createdUtc;}
 [Serializable] public sealed class LabProfile {public string id,name;public List<LabRevision> versions=new List<LabRevision>();}
 [Serializable] public sealed class LabIndex {public int schema=1,generation;public string defaultId;public List<LabProfile> profiles=new List<LabProfile>();public Dictionary<string,List<int>> releases=new Dictionary<string,List<int>>();}
 [Serializable] public sealed class LabBundle {public LabIndex index;public Dictionary<string,string> versions=new Dictionary<string,string>();}
 // Immutable files are published before an atomic index replacement. A failed publish
 // may leave an orphan version, which remains reserved and is never overwritten.
 public sealed class LabFileStore {
  public readonly string Root;public string Token {get;private set;}="";
  public LabFileStore(string root){Root=root;}
  public static string VersionKey(string id,int number){if(!Guid.TryParseExact(id,"D",out var guid)||guid.ToString("D")!=id||number<1)throw new FormatException("Некорректный ID или номер версии");return id+"/"+number+".json";}
  public LabIndex Read(){var path=Path.Combine(Root,"index.json");if(!File.Exists(path)){Token="";return new LabIndex();}var raw=File.ReadAllText(path);var value=JsonConvert.DeserializeObject<LabIndex>(raw);ValidateIndex(value);Token=Hash(raw);return value;}
  public static void ValidateIndex(LabIndex value){
   if(value==null||value.schema!=1||value.generation<0||value.profiles==null||value.releases==null||value.profiles.Any(p=>p==null)||value.profiles.Select(p=>p.id).Distinct().Count()!=value.profiles.Count)throw new FormatException("Повреждён индекс профилей");
   foreach(var p in value.profiles){VersionKey(p.id,1);if(string.IsNullOrWhiteSpace(p.name)||p.name.Length>40||p.versions==null||p.versions.Count==0||p.versions.Any(v=>v==null)||p.versions.Select(v=>v.number).Distinct().Count()!=p.versions.Count||p.versions.Any(v=>v.number<1))throw new FormatException("Повреждён профиль");}
   foreach(var mark in value.releases){var p=value.profiles.SingleOrDefault(x=>x.id==mark.Key);if(p==null||mark.Value==null||mark.Value.Distinct().Count()!=mark.Value.Count||mark.Value.Any(n=>!p.versions.Any(v=>v.number==n)))throw new FormatException("Повреждены релизные отметки");}
   if(value.defaultId!=null&&!value.profiles.Any(p=>p.id==value.defaultId))throw new FormatException("Не найден основной Default");
  }
  public string ReadVersion(string id,int n)=>File.ReadAllText(Path.Combine(Root,VersionKey(id,n)));
  public int NextNumber(LabProfile p){int max=p.versions.Count==0?0:p.versions.Max(v=>v.number);string dir=Path.Combine(Root,p.id);if(Directory.Exists(dir))foreach(var path in Directory.GetFiles(dir,"*.json"))if(int.TryParse(Path.GetFileNameWithoutExtension(path),out int n))max=Math.Max(max,n);return checked(max+1);}
  public void Write(string expected,Action<LabIndex> mutation,Dictionary<string,string> files=null){
   Directory.CreateDirectory(Root);using(var lease=new FileStream(Path.Combine(Root,"writer.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
    var index=Read();if(Token!=expected)throw new IOException("Профили изменены другим процессом. Откройте лабораторию заново.");mutation(index);ValidateIndex(index);
    if(files!=null)foreach(var pair in files){var parts=pair.Key.Split('/');if(parts.Length!=2||!int.TryParse(Path.GetFileNameWithoutExtension(parts[1]),out int n)||VersionKey(parts[0],n)!=pair.Key)throw new FormatException("Некорректный путь версии");string path=Path.Combine(Root,pair.Key);Directory.CreateDirectory(Path.GetDirectoryName(path));using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var writer=new StreamWriter(stream,new UTF8Encoding(false))){writer.Write(pair.Value);writer.Flush();stream.Flush(true);}}
    index.generation++;Atomic(Path.Combine(Root,"index.json"),JsonConvert.SerializeObject(index,Formatting.Indented));Read();
   }
  }
  static string Hash(string text){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","");}
  public static void Atomic(string path,string text){string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var writer=new StreamWriter(stream,new UTF8Encoding(false))){writer.Write(text);writer.Flush();stream.Flush(true);}if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
 }
 public sealed class LabSnapshot {
  public readonly string ProfileId,ProfileName,Json;public readonly int Version;public string Label=>ProfileName+" · версия "+Version;
  public LabSnapshot(string id,string name,int version,string json){ProfileId=id;ProfileName=name;Version=version;Json=json;}
 }
 public sealed class LabLibrary {
  readonly LabBundle packaged;readonly LabFileStore store;readonly bool author;LabIndex local;string token;
  public static string Describe(Exception error){
   if(error is JsonException)return "Файл профиля повреждён. Исходные данные сохранены. Выберите другую версию или релизный Default.";
   if(error is FileNotFoundException||error is DirectoryNotFoundException)return "Файл выбранной версии не найден. Выберите другую версию или релизный Default.";
   bool russian=error.Message.Any(c=>c>='А'&&c<='я');
   if(error is UnauthorizedAccessException||error is IOException&&!russian)return "Не удалось прочитать или сохранить файлы. Проверьте права доступа и свободное место. Исходные данные не заменены.";
   if(error is FormatException&&!russian)return "Конфигурация несовместима с этой версией игры. Исходные данные сохранены. Выберите другую версию или релизный Default.";
   return russian?error.Message:"Действие не выполнено. Перечитайте список профилей или вернитесь к релизному Default.";
  }
  public string Warning {get;private set;}="";public bool Author=>author;public string Destination=>store.Root;
  public LabSnapshot Active {get;private set;}public LabSnapshot Selected {get;private set;}public LabDraft Draft {get;private set;}
  public LabLibrary(LabBundle bundle,LabFileStore store,bool author){
   // Clone trusted package input; no caller can mutate the protected catalog afterward.
   packaged=JsonConvert.DeserializeObject<LabBundle>(JsonConvert.SerializeObject(bundle));LabFileStore.ValidateIndex(packaged.index);this.store=store;this.author=author;
   Reload();try{Reset();}catch(Exception e){Warning=Describe(e);}
  }
  public string CatalogError {get{try{ReleaseDefault();return "";}catch(Exception e){return Describe(e);}}}
  public void Reload(){try{local=store.Read();token=store.Token;if(author&&local.defaultId==null)throw new FormatException("Не найден основной Default автора");if(!author&&local.profiles.Any(p=>packaged.index.profiles.Any(b=>b.id==p.id)))throw new FormatException("Свои профили конфликтуют с защищённым пакетом");Warning="";}catch(Exception e) when(e is IOException||e is UnauthorizedAccessException||e is FormatException||e is JsonException){local=null;Warning="Не удалось прочитать профили: "+Describe(e);}}
  LabIndex Source=>author&&local!=null?local:packaged.index;
  public IEnumerable<LabProfile> Profiles=>(author?Source.profiles:packaged.index.profiles.Concat(local?.profiles??new List<LabProfile>())).Select(p=>JsonConvert.DeserializeObject<LabProfile>(JsonConvert.SerializeObject(p)));
  public bool Editable(string id)=>local!=null&&(author||!packaged.index.profiles.Any(p=>p.id==id));
  public bool CanDelete(string id)=>Editable(id)&&id!=Source.defaultId;
  public bool Marked(string id,int n)=>Source.releases.TryGetValue(id,out var versions)&&versions.Contains(n);
  string Raw(string id,int n){string key=LabFileStore.VersionKey(id,n);if((!author||local==null)&&packaged.versions.TryGetValue(key,out var raw))return raw;return store.ReadVersion(id,n);}
  public LabSnapshot ReleaseDefault(){
   string id=Source.defaultId;if(id==null||!Source.releases.TryGetValue(id,out var marks)||marks.Count==0)throw new FormatException("У Default нет релизной версии. Отметьте корректную версию в режиме автора.");
   var p=Profiles.Single(p=>p.id==id);int n=marks.Max();string raw=Raw(id,n);return new LabSnapshot(id,p.name,n,LabParameters.Resolve(raw,null));
  }
  public LabSnapshot Resolve(string id,int n){var p=Profiles.Single(x=>x.id==id);if(!p.versions.Any(v=>v.number==n))throw new FormatException("Нет выбранной версии");string defaults=null;try{defaults=ReleaseDefault().Json;}catch(FormatException){}return new LabSnapshot(id,p.name,n,LabParameters.Resolve(Raw(id,n),defaults));}
  public void Select(string id,int n){var next=Resolve(id,n);Selected=Active=next;Draft=new LabDraft(next.Json);}
  public void Reset(){var next=ReleaseDefault();Select(next.ProfileId,next.Version);}
  public void Discard(){if(Selected!=null)Draft=new LabDraft(Selected.Json);}
  public LabSnapshot Capture(){if(Active==null)throw new FormatException("Нет активной сохранённой конфигурации");return new LabSnapshot(Active.ProfileId,Active.ProfileName,Active.Version,Active.Json);}
  void Change(Action<LabIndex> mutation,Dictionary<string,string> files=null){if(local==null)throw new IOException("Исправьте файлы профилей перед записью; прежние данные сохранены.");store.Write(token,mutation,files);local=store.Read();token=store.Token;}
  static string Name(string value){value=(value??"").Trim();if(value.Length<1||value.Length>40||value.Any(char.IsControl))throw new FormatException("Имя: 1–40 символов без управляющих знаков");return value;}
  public void Copy(string id,int n,string name){var source=Resolve(id,n);string newId=Guid.NewGuid().ToString("D");name=Name(name);var profile=new LabProfile{id=newId,name=name,versions=new List<LabRevision>{new LabRevision{number=1,createdUtc=DateTime.UtcNow.ToString("O")}}};Change(index=>index.profiles.Add(profile),new Dictionary<string,string>{{LabFileStore.VersionKey(newId,1),source.Json}});Select(newId,1);}
  public void Save(){
   if(Selected==null||!Editable(Selected.ProfileId))throw new InvalidOperationException("Профиль поставки защищён. Создайте копию.");if(!Draft.Dirty)throw new InvalidOperationException("Нет изменений для новой версии");
   string json=Draft.Build();var p=local.profiles.Single(x=>x.id==Selected.ProfileId);int number=store.NextNumber(p);string id=p.id;
   Change(index=>index.profiles.Single(x=>x.id==id).versions.Add(new LabRevision{number=number,createdUtc=DateTime.UtcNow.ToString("O")}),new Dictionary<string,string>{{LabFileStore.VersionKey(id,number),json}});Select(id,number);
  }
  public void Rename(string id,string name){if(!Editable(id))throw new InvalidOperationException("Профиль защищён");name=Name(name);Change(index=>index.profiles.Single(p=>p.id==id).name=name);if(Selected?.ProfileId==id){Selected=new LabSnapshot(id,name,Selected.Version,Selected.Json);if(Active?.ProfileId==id)Active=new LabSnapshot(id,name,Active.Version,Active.Json);}}
  public void Delete(string id){if(!CanDelete(id))throw new InvalidOperationException("Основной Default и профили поставки не удаляются");var fallback=Active?.ProfileId==id?ReleaseDefault():null;Change(index=>{index.profiles.RemoveAll(p=>p.id==id);index.releases.Remove(id);});if(fallback!=null)Select(fallback.ProfileId,fallback.Version);else if(Selected?.ProfileId==id)Reset();try{string folder=Path.Combine(store.Root,id);if(Directory.Exists(folder))Directory.Delete(folder,true);}catch(Exception e) when(e is IOException||e is UnauthorizedAccessException){Warning="Профиль удалён из каталога, но не все файлы удалены: "+e.Message;}}
  public void Mark(string id,int number,bool enabled){if(!author||local==null)throw new InvalidOperationException("Релизные отметки доступны только автору");Resolve(id,number);Change(index=>{if(!index.releases.TryGetValue(id,out var list))index.releases[id]=list=new List<int>();if(enabled&&!list.Contains(number))list.Add(number);if(!enabled)list.Remove(number);});}
  public LabBundle Export(){if(!author||local==null)throw new IOException("Невозможно собрать каталог: исправьте исходные файлы автора.");var baseline=ReleaseDefault();var result=new LabBundle{index=new LabIndex{defaultId=baseline.ProfileId}};foreach(var p in Source.profiles){if(!Source.releases.TryGetValue(p.id,out var marks)||marks.Count==0)continue;var copy=new LabProfile{id=p.id,name=p.name,versions=p.versions.Where(v=>marks.Contains(v.number)).Select(v=>new LabRevision{number=v.number,createdUtc=v.createdUtc}).ToList()};result.index.profiles.Add(copy);result.index.releases[p.id]=marks.ToList();foreach(var n in marks)result.versions[LabFileStore.VersionKey(p.id,n)]=Resolve(p.id,n).Json;}return result;}
 }
}
