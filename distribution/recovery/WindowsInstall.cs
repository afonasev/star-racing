using System.Diagnostics;
using Newtonsoft.Json;
using Velopack;
using Velopack.NuGet;
namespace StarRacingPrototype.Distribution;

public static class GameInstallGate {
 public const string Name="Global\\StarRacingDesktop-Running";
 public static IDisposable Acquire(){
  if(!OperatingSystem.IsWindows())return new Empty();
  return new Owned();
 }
 // A named mutex is thread-affine; native updater awaits must not transfer ownership.
 sealed class Owned:IDisposable {
  readonly ManualResetEventSlim release=new(false);readonly Thread holder;
  public Owned(){
   var ready=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
   holder=new Thread(()=>{
    try{using var mutex=new Mutex(true,Name,out bool created);
     if(!created)throw new IOException("Закройте Star Racing и повторите установку.");
     ready.SetResult(true);try{release.Wait();}finally{mutex.ReleaseMutex();}
    }catch(Exception error){ready.TrySetException(error);}
   }){IsBackground=true};holder.Start();
   try{ready.Task.GetAwaiter().GetResult();}catch{holder.Join();release.Dispose();throw;}
  }
  public void Dispose(){release.Set();holder.Join();release.Dispose();}
 }
 sealed class Empty:IDisposable {public void Dispose(){}}
}
public sealed class InstallJournal {
 public int schema=1;public string target="",stage="",backup="",token="",state="applying";
 public Dictionary<string,bool> old=new();
}
public static class WindowsInstall {
 public static readonly string[] Nodes={"current","packages","Star Racing.exe","Update.exe",".portable","Star Racing.ico","Uninstall-Star-Racing.exe","installer.complete","installer.receipt"};
 static bool Exists(string path)=>File.Exists(path)||Directory.Exists(path);
 static void NoLinks(string path){
  if(!Exists(path))return;
  if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Ссылки и junction в файлах установки не поддерживаются: "+path);
  if(Directory.Exists(path))foreach(string child in Directory.EnumerateFileSystemEntries(path))NoLinks(child);
 }
 static void Move(string from,string to){if(Directory.Exists(from))Directory.Move(from,to);else File.Move(from,to);}
 static void Remove(string path){NoLinks(path);if(Directory.Exists(path))Directory.Delete(path,true);else if(File.Exists(path))File.Delete(path);}
 static string JournalPath(string target)=>target+".sr-install.json";
 public static void ValidToken(string token){if(!System.Text.RegularExpressions.Regex.IsMatch(token,@"\A[A-Za-z0-9-]{1,80}\z"))throw new IOException("Некорректный идентификатор установки");}
 static string Canonical(string path)=>Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
 static void Save(InstallJournal j)=>RecoveryTransaction.Atomic(JournalPath(j.target),JsonConvert.SerializeObject(j));
 static InstallJournal Read(string target){
  NoLinks(JournalPath(target));string raw=File.ReadAllText(JournalPath(target));var j=ReleaseAuthentication.Parse(raw).ToObject<InstallJournal>()??throw new IOException("Повреждён журнал установки");ValidToken(j.token);
  if(j.schema!=1||j.target!=target||j.stage!=target+".sr-stage-"+j.token||j.backup!=target+".sr-backup-"+j.token||!j.old.Keys.OrderBy(x=>x).SequenceEqual(Nodes.OrderBy(x=>x))||j.state!="applying"&&j.state!="committed")throw new IOException("Некорректный журнал установки");
  return j;
 }
 static PackageManifest Manifest(string target){
  string path=Path.Combine(target,"current","sq.version");
  if(!PackageManifest.TryParseFromFile(path,out var m)||m.Id!="tech.afonasev.star-racing.win-x64"||m.Channel!="win-x64"||m.Version==null)throw new IOException("Не удалось определить установленную версию. Файлы сохранены; восстановите исходный sq.version или удалите приложение штатным способом перед новой установкой.");
  return m;
 }
 public static string Inspect(string target,string version){
  target=Canonical(target);NoLinks(target);
  if(File.Exists(JournalPath(target))){Read(target);return "P";}
  var wanted=SemanticVersion.Parse(version);if(wanted.ToNormalizedString()!=version)throw new IOException("Некорректная версия пакета");
  if(!Directory.Exists(target)||!Directory.EnumerateFileSystemEntries(target).Any())return "N";
  string marker=Path.Combine(target,"installer.complete");
  if(!File.Exists(marker)||File.ReadAllText(marker)!="StarRacingDesktop-v1")throw new IOException("Папка не является распознанной установкой Star Racing. Выберите пустую папку.");
  var current=Manifest(target);
  if(current.Version.ToNormalizedString().Contains('-')!=version.Contains('-'))throw new IOException("Тестовый и стабильный каналы нельзя устанавливать поверх друг друга.");
  if(wanted<current.Version)throw new IOException("Установка старой версии поверх новой запрещена. Используйте штатное удаление с сохранением пользовательских данных.");
  return (wanted==current.Version?"R":"U")+current.Version.ToNormalizedString();
 }
 static void Stopped(string target){
  if(!OperatingSystem.IsWindows())return;
  foreach(var p in Process.GetProcessesByName("Star Racing"))using(p){
   try{var exe=p.MainModule?.FileName;if(exe!=null&&Canonical(exe).StartsWith(target+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Закройте Star Racing и повторите установку.");}
   catch(InvalidOperationException){} // process exited during inspection
  }
 }
 static void RollbackCore(InstallJournal j){
  Stopped(j.target);NoLinks(j.target);Directory.CreateDirectory(j.target);NoLinks(j.backup);NoLinks(j.stage);
  foreach(string name in Nodes.Reverse()){
   string target=Path.Combine(j.target,name),old=Path.Combine(j.backup,name),staged=Path.Combine(j.stage,name);
   if(Exists(old)){Remove(target);Move(old,target);}
   else if(!j.old[name]&&Exists(target)&&!Exists(staged))Remove(target);
   else if(j.old[name]&&!Exists(target))throw new IOException("Не найден резервный файл: "+name+". Сохраните журнал и повторите восстановление.");
  }
  Remove(j.stage);Remove(j.backup);File.Delete(JournalPath(j.target));
 }
 public static string Prepare(string stage,string target,string version,string token,Action<string> fault=null){
  target=Canonical(target);stage=Canonical(stage);ValidToken(token);
  if(stage!=target+".sr-stage-"+token)throw new IOException("Временная папка должна принадлежать этой установке и быть рядом с ней.");
  Directory.CreateDirectory(Path.GetDirectoryName(target)!);
  using var gate=GameInstallGate.Acquire();using var locked=new FileStream(target+".sr-install.lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  if(File.Exists(JournalPath(target))){var pending=Read(target);if(pending.state=="committed"){Remove(pending.backup);Remove(pending.stage);File.Delete(JournalPath(target));}else RollbackCore(pending);}
  string prior=Inspect(target,version);NoLinks(stage);var next=Manifest(stage);
  if(next.Version!=SemanticVersion.Parse(version))throw new IOException("Версия файлов не соответствует установщику");
  foreach(string required in Nodes.Where(x=>x!="packages"&&x!="installer.receipt"))if(!Exists(Path.Combine(stage,required)))throw new IOException("Не хватает файла поставки: "+required);
  if(File.ReadAllText(Path.Combine(stage,"installer.complete"))!="StarRacingDesktop-v1")throw new IOException("Некорректный маркер поставки");
  Stopped(target);Directory.CreateDirectory(target);
  var journal=new InstallJournal{target=target,stage=stage,backup=target+".sr-backup-"+token,token=token,old=Nodes.ToDictionary(n=>n,n=>Exists(Path.Combine(target,n)))};
  if(Exists(journal.backup))throw new IOException("Резервная папка уже существует; сохраните её и проверьте предыдущую установку.");
  Directory.CreateDirectory(journal.backup);Save(journal);
  try{
   foreach(string name in Nodes){Stopped(target);string destination=Path.Combine(target,name),backup=Path.Combine(journal.backup,name),source=Path.Combine(stage,name);
    if(Exists(destination)){Move(destination,backup);fault?.Invoke("backup:"+name);}
    if(Exists(source)){Move(source,destination);fault?.Invoke("install:"+name);}
   }
   if(Manifest(target).Version!=next.Version)throw new IOException("Установка не подтверждена");
  }catch{RollbackCore(journal);throw;}
  return prior;
 }
 public static void Commit(string target,string token){
  target=Canonical(target);using var gate=GameInstallGate.Acquire();using var locked=new FileStream(target+".sr-install.lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  var journal=Read(target);if(journal.token!=token)throw new IOException("Подтверждается другая установка");
  journal.state="committed";Save(journal);RecoveryTransaction.Atomic(Path.Combine(target,"installer.receipt"),token);
  try{Remove(journal.backup);Remove(journal.stage);File.Delete(JournalPath(target));}catch(Exception error) when(error is IOException||error is UnauthorizedAccessException){/* durable committed journal permits cleanup on the next install */}
 }
 public static void RemoveInstalled(string target){
  target=Canonical(target);using var gate=GameInstallGate.Acquire();using var locked=new FileStream(target+".sr-install.lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  if(File.Exists(JournalPath(target)))throw new IOException("Сначала завершите восстановление: закройте игру и повторите установку.");
  NoLinks(target);string marker=Path.Combine(target,"installer.complete");
  if(!File.Exists(marker)||File.ReadAllText(marker)!="StarRacingDesktop-v1")throw new IOException("Не распознана установка; файлы сохранены.");
  Stopped(target);foreach(string node in Nodes)Remove(Path.Combine(target,node));
 }
 public static void Rollback(string target,string token){
  target=Canonical(target);using var gate=GameInstallGate.Acquire();using var locked=new FileStream(target+".sr-install.lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  var journal=Read(target);if(journal.token!=token||journal.state!="applying")throw new IOException("Нет подходящей незавершённой установки");RollbackCore(journal);
 }
}
