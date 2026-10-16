using Newtonsoft.Json;
using StarRacingPrototype.Distribution;
static class RecoveryChecks {
 public static void Run(Action<bool,string> require){
  string root=Path.Combine(Path.GetTempPath(),"recovery-journal-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  string path=Path.Combine(root,"transaction.json");
  try{
   var r=new RecoveryRecord{appId="tech.afonasev.star-racing.win-x64",channel="win-x64",track="production",previousVersion="0.3.5",targetVersion="0.3.6",root=root,packages=root,helper=Path.Combine(root,"Update.exe"),executable=Path.Combine(root,"game.exe"),token=Guid.NewGuid().ToString("N"),gamePid=123,gameStarted=456};
   foreach(var state in new[]{"prepared","applying","pending-health","healthy","recovery-required"}){r.state=state;RecoveryTransaction.Save(path,r);require(RecoveryTransaction.Read(path).state==state,"durable state "+state);}
   r.state="pending-health";RecoveryTransaction.Save(path,r);
   require(!RecoveryTransaction.HasHealth(path,r),"matching version journal is not health");
   foreach(var bad in new[]{("0.3.5",r.token,123,456L),("0.3.6","foreign",123,456L),("0.3.6",r.token,124,456L),("0.3.6",r.token,123,TimeSpan.TicksPerSecond+457L)}){
    bool rejected=false;try{RecoveryTransaction.Acknowledge(path,r,bad.Item1,bad.Item2,bad.Item3,bad.Item4);}catch(InvalidDataException){rejected=true;}require(rejected,"reject foreign health identity");
   }
   RecoveryTransaction.Acknowledge(path,r,"0.3.6",r.token,123,456);require(RecoveryTransaction.HasHealth(path,r),"exact health ack accepted");
   File.WriteAllText(path+".health","corrupt");require(!RecoveryTransaction.HasHealth(path,r),"corrupt ack never healthy");
   r.arguments=new[]{new string('x',140000)};RecoveryTransaction.Save(path,r);require(RecoveryTransaction.Read(path).arguments[0].Length==140000,"journal limit covers two bounded signed descriptors");
   File.WriteAllText(path,"{\"state\":\"healthy\",\"state\":\"prepared\"}");bool duplicate=false;try{RecoveryTransaction.Read(path);}catch(JsonException){duplicate=true;}require(duplicate,"duplicate journal state rejected");
   r.state="unknown";bool invalid=false;try{RecoveryTransaction.Save(path,r);}catch(InvalidDataException){invalid=true;}require(invalid,"invalid transition cannot be saved");
   require(!Directory.GetFiles(root,"*.tmp").Any(),"atomic journal leaves no temporary files");
  }finally{Directory.Delete(root,true);}
 }
}
