using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
namespace StarRacingPrototype.Distribution {
 public static class UpdateAttemptJournal {
  public static string Digest(string envelope){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Convert.FromBase64String((string)JObject.Parse(envelope)["payloadBase64"]))).Replace("-","");}
  public static bool AlreadyAttempted(string journal,string envelope,string fromVersion,string targetVersion){
   if(!File.Exists(journal))return false;
   try{var data=JObject.Parse(File.ReadAllText(journal));return (string)data["descriptorDigest"]==Digest(envelope)&&(string)data["targetVersion"]==targetVersion;}
   catch{return true;} // damaged journal must not cause an automatic elevation loop
  }
  public static void Begin(string journal,string envelope,string fromVersion,string targetVersion){
   Save(journal,new JObject{["attemptId"]=Guid.NewGuid().ToString("N"),["descriptorDigest"]=Digest(envelope),["fromVersion"]=fromVersion,["targetVersion"]=targetVersion}.ToString());
  }
  public static void Save(string file,string value){
   Directory.CreateDirectory(Path.GetDirectoryName(file));var temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){var bytes=Encoding.UTF8.GetBytes(value);stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
    if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);
   }finally{if(File.Exists(temp))File.Delete(temp);}
  }
 }
}
