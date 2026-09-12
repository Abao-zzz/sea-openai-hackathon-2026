using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Alyvo.SsmsAiSqlAssistant
{
 public static class ChangeAudit
 {
  public static async Task Publish(ConnectionContext context,MapModule module,string before,string after,bool restore)
  {
   var payload=new{databaseFingerprint=context.Fingerprint,objectId=module.Id,beforeHash=before,afterHash=after,action=restore?"restore":"apply"};
   var directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Alyvo","SsmsAiSqlAssistant","audit-outbox");Directory.CreateDirectory(directory);var path=Path.Combine(directory,Guid.NewGuid().ToString("N")+".json");
   File.WriteAllText(path,JsonConvert.SerializeObject(new{createdUtc=DateTimeOffset.UtcNow,status="pending",payload}));
   try{var response=await new LocalApi().Send("/sp/change-audit",payload,CancellationToken.None);File.WriteAllText(path,JsonConvert.SerializeObject(new{createdUtc=DateTimeOffset.UtcNow,status="confirmed",payload,receipt=response["id"]?.ToString()}));}
   catch{throw new InvalidOperationException("SQL 變更已提交且本機 hash-chain 已記錄；LocalService 稽核同步尚未確認，待處理紀錄位於 audit-outbox。請勿重複套用。");}
  }
 }
}
