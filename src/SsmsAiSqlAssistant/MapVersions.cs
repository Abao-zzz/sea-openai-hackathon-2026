using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Newtonsoft.Json;
namespace Alyvo.SsmsAiSqlAssistant
{
 public sealed class ProcedureSource
 {
  public string Schema,Name,Sql,Canonical,Hash;
  public static ProcedureSource Parse(string sql,string database)
  {
   var script=new TSql170Parser(true).Parse(new StringReader(sql),out var errors) as TSqlScript;
   if(errors.Count>0||script==null)throw new InvalidOperationException("SP 定義語法錯誤。");var statements=script.Batches.SelectMany(b=>b.Statements).ToArray();
   var body=statements.OfType<ProcedureStatementBody>().SingleOrDefault();
   if(body==null||statements.Any(s=>!(s is ProcedureStatementBody)&&!(s is UseStatement)))throw new InvalidOperationException("只能建立單一完整 CREATE / ALTER PROCEDURE 版本；不可夾帶其他批次。");
   if(statements.OfType<UseStatement>().Any(u=>!string.Equals(u.DatabaseName.Value,database,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("USE database 與目前連線不一致。");
   var name=body.ProcedureReference.Name;if(name.Identifiers.Count!=2)throw new InvalidOperationException("SP 必須明確指定 schema.name。");
   if(body.Options.Any(o=>o.OptionKind==ProcedureOptionKind.Encryption))throw new InvalidOperationException("不支援加密 SP 的版本套用。");
   int start=0;foreach(var token in script.ScriptTokenStream.Take(body.FirstTokenIndex))if(token.TokenType==TSqlTokenType.Go)start=token.Offset+token.Text.Length;
   // Only the CREATE/ALTER prefix is rewritten. Comments and procedure body are preserved.
   var trailingGo=script.ScriptTokenStream.Skip(body.LastTokenIndex+1).FirstOrDefault(t=>t.TokenType==TSqlTokenType.Go);var end=trailingGo?.Offset??sql.Length;
   var module=sql.Substring(start,end-start).Trim();var offset=body.StartOffset-start;while(offset>0&&start<sql.Length&&char.IsWhiteSpace(sql[start])){start++;offset--;}
   var prefix=module.Substring(0,Math.Max(0,offset));var rest=module.Substring(Math.Max(0,offset));
   rest=Regex.Replace(rest,@"\A(?:CREATE\s+(?:OR\s+ALTER\s+)?|ALTER\s+)PROC(?:EDURE)?\b","CREATE OR ALTER PROCEDURE",RegexOptions.IgnoreCase);
   var migration=prefix+rest;var parsed=new TSql170Parser(true).Parse(new StringReader(migration),out var migrationErrors);if(migrationErrors.Count>0)throw new InvalidOperationException("無法產生安全 migration。");
   new Sql170ScriptGenerator().GenerateScript(parsed,out var canonical);var comments=string.Join("\n",parsed.ScriptTokenStream.Where(t=>t.TokenType==TSqlTokenType.SingleLineComment||t.TokenType==TSqlTokenType.MultilineComment).Select(t=>t.Text.Trim()));canonical=canonical.Trim()+"\n"+comments;
   return new ProcedureSource{Schema=name.SchemaIdentifier.Value,Name=name.BaseIdentifier.Value,Sql=migration,Canonical=canonical,Hash=ConnectionContext.Hash(canonical)};
  }
 }
 public sealed class SpVersion
 {
  public int Number;public int? RestoredFromVersion;public string Sql,Hash,Author,Reason,Diff,BaseHash,State="未套用",Kind="人工",DownSql,UpSql;public DateTimeOffset CreatedAt=DateTimeOffset.UtcNow;
 }
 public sealed class VersionFile
 {
  public string ServerFingerprint,DatabaseFingerprint,Schema,Name;public int NextNumber=1;public List<SpVersion> Versions=new List<SpVersion>();public List<VersionAudit> Audit=new List<VersionAudit>();
 }
 public sealed class VersionAudit{public DateTimeOffset Time=DateTimeOffset.UtcNow;public string Operation,BeforeHash,AfterHash,Author,PreviousHash,Hash;public int Number;}
 public sealed class VersionRepository
 {
  public readonly string Root;public VersionRepository(string root=null){Root=root??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Alyvo","SsmsAiSqlAssistant","map-versions");Directory.CreateDirectory(Root);}
  string Key(ConnectionContext c,MapModule m)=>ConnectionContext.Hash(c.Fingerprint+"|"+m.Schema+"|"+m.Name);
  string PathFor(ConnectionContext c,MapModule m)=>Path.Combine(Root,Key(c,m)+".json");
  public VersionFile Load(ConnectionContext c,MapModule m)
  {
   var path=PathFor(c,m);if(!File.Exists(path))return new VersionFile{ServerFingerprint=ConnectionContext.Hash(c.Server.ToUpperInvariant()),DatabaseFingerprint=c.Fingerprint,Schema=m.Schema,Name=m.Name};
   var result=JsonConvert.DeserializeObject<VersionFile>(File.ReadAllText(path));if(result==null||result.DatabaseFingerprint!=c.Fingerprint||result.Schema!=m.Schema||result.Name!=m.Name)throw new InvalidOperationException("版本檔身分不符，拒絕操作。");return result;
  }
  void Save(ConnectionContext c,MapModule m,VersionFile file){var path=PathFor(c,m);var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temp,JsonConvert.SerializeObject(file,Formatting.Indented),new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
  T Locked<T>(ConnectionContext c,MapModule m,Func<T> action){using(var mutex=new Mutex(false,"Local\\AlyvoMapVersion_"+Key(c,m))){if(!mutex.WaitOne(TimeSpan.FromSeconds(15)))throw new InvalidOperationException("版本資料正在更新，請稍後再試。");try{return action();}finally{mutex.ReleaseMutex();}}}
  public SpVersion EnsureBaseline(ConnectionContext c,MapModule m)
  {
   if(!m.IsProcedure)throw new InvalidOperationException("只有 SP 建立基準版本。");
   return Locked(c,m,()=>{var file=Load(c,m);if(file.Versions.Count>0)return file.Versions.OrderBy(v=>v.Number).First();
    if(string.IsNullOrWhiteSpace(m.Definition))throw new InvalidOperationException("無法建立基準版本：SP 定義未回傳，可能加密或缺少權限。");
    var source=ProcedureSource.Parse(m.Definition,c.Database);if(source.Schema!=m.Schema||source.Name!=m.Name)throw new InvalidOperationException("SP 定義身分不符。");
    var version=new SpVersion{Number=file.NextNumber++,Kind="基準",State="基準快照",Sql=m.Definition,Hash=source.Hash,BaseHash=source.Hash,Author=Environment.UserDomainName+"\\"+Environment.UserName,Reason="首次載入時保存資料庫現有定義；未執行或修改 SP。",Diff=Diff(m.Definition,m.Definition),UpSql=source.Sql,DownSql=source.Sql};file.Versions.Add(version);Save(c,m,file);return version;});
  }
  public SpVersion Create(ConnectionContext c,MapModule m,string sql,string reason,string kind="人工")
  {
   if(!m.IsProcedure||m.Definition==null)throw new InvalidOperationException("SQL Server 沒有回傳這支 SP 的完整定義。");if(string.IsNullOrWhiteSpace(reason))throw new InvalidOperationException("請填寫本次修改原因。");
   var source=ProcedureSource.Parse(sql,c.Database);if(source.Schema!=m.Schema||source.Name!=m.Name)throw new InvalidOperationException("editor 的 SP 與選取物件不符。");var baseline=ProcedureSource.Parse(m.Definition,c.Database);
   return Locked(c,m,()=>{var file=Load(c,m);var duplicate=file.Versions.FirstOrDefault(v=>v.Hash==source.Hash);if(duplicate!=null)return duplicate;var previous=file.Versions.LastOrDefault()?.Sql??m.Definition;var version=new SpVersion{Kind=kind,Number=file.NextNumber++,Sql=sql,Hash=source.Hash,BaseHash=baseline.Hash,Author=Environment.UserDomainName+"\\"+Environment.UserName,Reason=reason.Trim(),Diff=Diff(previous,sql),UpSql=source.Sql,DownSql=baseline.Sql};file.Versions.Add(version);Save(c,m,file);return version;});
  }
  public SpVersion CreateRestore(ConnectionContext c,MapModule m,int targetNumber,string currentDefinition,string reason)
  {
   if(!m.IsProcedure||string.IsNullOrWhiteSpace(reason))throw new InvalidOperationException("請選取 SP 並填寫還原原因。");
   var baseline=ProcedureSource.Parse(currentDefinition,c.Database);
   if(baseline.Schema!=m.Schema||baseline.Name!=m.Name)throw new InvalidOperationException("目前資料庫 SP 身分不符。");
   return Locked(c,m,()=>{var file=Load(c,m);var target=file.Versions.Single(v=>v.Number==targetNumber);var source=ProcedureSource.Parse(target.Sql,c.Database);if(source.Schema!=m.Schema||source.Name!=m.Name)throw new InvalidOperationException("還原版本的 SP 身分不符。");
    var version=new SpVersion{Number=file.NextNumber++,RestoredFromVersion=targetNumber,Kind="還原",Sql=target.Sql,Hash=source.Hash,BaseHash=baseline.Hash,Author=Environment.UserDomainName+"\\"+Environment.UserName,Reason=reason.Trim(),Diff=Diff(currentDefinition,target.Sql),UpSql=source.Sql,DownSql=baseline.Sql};file.Versions.Add(version);Save(c,m,file);return version;});
  }
  public static string Diff(string before,string after)
  {
   var a=(before??"").Replace("\r\n","\n").Split('\n');var b=(after??"").Replace("\r\n","\n").Split('\n');int prefix=0;while(prefix<Math.Min(a.Length,b.Length)&&a[prefix]==b[prefix])prefix++;int suffix=0;while(suffix<Math.Min(a.Length,b.Length)-prefix&&a[a.Length-1-suffix]==b[b.Length-1-suffix])suffix++;
   return "--- previous.sql\n+++ candidate.sql\n@@ -"+(prefix+1)+","+(a.Length-prefix-suffix)+" +"+(prefix+1)+","+(b.Length-prefix-suffix)+" @@\n"+string.Join("\n",a.Skip(prefix).Take(a.Length-prefix-suffix).Select(x=>"-"+x).Concat(b.Skip(prefix).Take(b.Length-prefix-suffix).Select(x=>"+"+x)));
  }
  public string Export(ConnectionContext c,MapModule m)
  {
   return Locked(c,m,()=>{var file=Load(c,m);var directory=Path.Combine(Root,"exports",Key(c,m),DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"history.json"),JsonConvert.SerializeObject(file,Formatting.Indented),new UTF8Encoding(false));foreach(var v in file.Versions)File.WriteAllText(Path.Combine(directory,"V"+v.Number+".sql"),v.Sql,new UTF8Encoding(false));return directory;});
  }
  public string Prepare(ConnectionContext c,MapModule m,int number){var version=Load(c,m).Versions.Single(v=>v.Number==number);var directory=Path.Combine(Root,"migrations",Key(c,m),"V"+number);Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"up.sql"),version.UpSql,new UTF8Encoding(false));File.WriteAllText(Path.Combine(directory,"down.sql"),version.DownSql,new UTF8Encoding(false));return directory;}
  public async Task Apply(ConnectionContext c,MapModule m,int number,bool restore,CancellationToken token)
  {
   var file=Load(c,m);var version=file.Versions.Single(v=>v.Number==number);var desired=ProcedureSource.Parse(restore?version.DownSql:version.UpSql,c.Database);var expected=restore?version.Hash:version.BaseHash;
   if(desired.Schema!=m.Schema||desired.Name!=m.Name)throw new InvalidOperationException("migration 目標不符。");
   Prepare(c,m,number);
   await DbWorker.VerifyEnvironment(c,token);
   using(var connection=c.Connect()){await connection.OpenAsync(token);using(var tx=connection.BeginTransaction(System.Data.IsolationLevel.Serializable))
   {
    try{
     using(var gate=new SqlCommand("DECLARE @r int;EXEC @r=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;IF @r<0 THROW 51000,'Version lock unavailable',1;",connection,tx)){gate.Parameters.AddWithValue("@resource","AlyvoMap:"+m.Id);await gate.ExecuteNonQueryAsync(token);}
     async Task<string> Read(){using(var cmd=new SqlCommand("SELECT sm.definition FROM sys.sql_modules sm JOIN sys.objects o ON o.object_id=sm.object_id JOIN sys.schemas s ON s.schema_id=o.schema_id WHERE o.object_id=@id AND s.name=@schema AND o.name=@name AND o.type='P';",connection,tx)){cmd.Parameters.AddWithValue("@id",m.Id);cmd.Parameters.AddWithValue("@schema",m.Schema);cmd.Parameters.AddWithValue("@name",m.Name);return await cmd.ExecuteScalarAsync(token) as string;}}
     var before=await Read();if(before==null||ProcedureSource.Parse(before,c.Database).Hash!=expected)throw new InvalidOperationException("definition drift：資料庫定義已變更，已 rollback，請重新載入地圖。");
     using(var cmd=new SqlCommand(desired.Sql,connection,tx){CommandTimeout=30})using(token.Register(()=>cmd.Cancel()))await cmd.ExecuteNonQueryAsync(token);
     var actual=await Read();if(actual==null||ProcedureSource.Parse(actual,c.Database).Hash!=desired.Hash)throw new InvalidOperationException("回讀 hash 不一致，已 rollback。");
     // Persist a pending audit before commit; interruption is visible and never reported as success.
     Update(c,m,number,restore?"還原待提交":"套用待提交",restore?"restore-pending":"apply-pending",expected,desired.Hash);
     tx.Commit();Update(c,m,number,restore?"已還原":"已套用",restore?"restore":"apply",expected,desired.Hash);
    }catch{try{tx.Rollback();}catch{/* Commit may have completed; pending audit must be reconciled by reloading DB. */}throw;}
   }}
   await ChangeAudit.Publish(c,m,expected,desired.Hash,restore);
  }
  void Update(ConnectionContext c,MapModule m,int number,string state,string operation,string before,string after)
  {Locked(c,m,()=>{var f=Load(c,m);var v=f.Versions.Single(x=>x.Number==number);v.State=state;var audit=new VersionAudit{Number=number,Operation=operation,BeforeHash=before,AfterHash=after,Author=Environment.UserDomainName+"\\"+Environment.UserName,PreviousHash=f.Audit.LastOrDefault()?.Hash??""};audit.Hash=ConnectionContext.Hash(JsonConvert.SerializeObject(audit));f.Audit.Add(audit);Save(c,m,f);return true;});}
 }
}

