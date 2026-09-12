using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Newtonsoft.Json;
namespace Alyvo.SsmsAiSqlAssistant
{
 public sealed class IndexTrialResult
 {
  public string DatabaseFingerprint,Sql,Ddl,DefinitionHash,Table,IndexName,Inventory,Reason,Receipt;
  public RunMetrics[] Before,After;public bool RolledBack,Eligible,Applied;public DateTimeOffset MeasuredUtc;
 }
 public static class IndexTrial
 {
  static readonly SemaphoreSlim Gate=new SemaphoreSlim(1,1);
  public static CreateIndexStatement Parse(string query,string ddl)
  {
   var read=SqlSafety.Parse(query);if(read.Errors.Any())throw new InvalidOperationException("測量 SQL 必須通過唯讀 SELECT 檢查。");
   var script=new TSql170Parser(true).Parse(new StringReader(ddl??""),out var errors) as TSqlScript;
   var statements=script?.Batches.SelectMany(b=>b.Statements).ToArray();var index=statements?.Length==1?statements[0] as CreateIndexStatement:null;
   if(errors.Count>0||script.Batches.Count!=1||index==null||index.Unique||index.Clustered==true||index.IndexOptions.Count>0||index.OnFileGroupOrPartitionScheme!=null||index.FileStreamOn!=null||index.OnName.Identifiers.Count!=2)throw new InvalidOperationException("自動測試只支援單一非唯一 NONCLUSTERED CREATE INDEX，不支援其他批次、索引選項或檔案配置。");
   var name=index.OnName.SchemaIdentifier.Value+"."+index.OnName.BaseIdentifier.Value;if(!read.Objects.Contains(name,StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException("索引目標必須是測量 SQL 明確引用的同資料庫資料表。");return index;
  }
  static string Target(CreateIndexStatement index)=>DbWorker.Quote(index.OnName.SchemaIdentifier.Value)+"."+DbWorker.Quote(index.OnName.BaseIdentifier.Value);
  static async Task Execute(SqlConnection c,SqlTransaction tx,string sql,CancellationToken token,int seconds=30)
  {using(var command=new SqlCommand(sql,c,tx){CommandTimeout=seconds})using(token.Register(()=>command.Cancel()))await command.ExecuteNonQueryAsync(token);}
  static async Task<string> Inventory(SqlConnection c,SqlTransaction tx,string query,CancellationToken token)
  {
   var parts=new List<string>();using(var env=new SqlCommand("SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ProductVersion'))+'|'+CONVERT(nvarchar(20),database_id)+'|'+CONVERT(nvarchar(30),create_date,126)+'|'+CONVERT(nvarchar(10),compatibility_level)+'|'+collation_name FROM sys.databases WHERE database_id=DB_ID();",c,tx))parts.Add(Convert.ToString(await env.ExecuteScalarAsync(token)));
   foreach(var table in SqlSafety.Parse(query).Objects.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x,StringComparer.Ordinal))using(var cmd=new SqlCommand(@"SELECT (SELECT object_id,name,type,modify_date FROM sys.objects WHERE object_id=OBJECT_ID(@name) FOR XML RAW),
(SELECT column_id,name,user_type_id,max_length,precision,scale,is_nullable,is_computed,collation_name FROM sys.columns WHERE object_id=OBJECT_ID(@name) ORDER BY column_id FOR XML RAW),
(SELECT index_id,name,type,is_unique,is_primary_key,is_disabled,filter_definition FROM sys.indexes WHERE object_id=OBJECT_ID(@name) ORDER BY index_id FOR XML RAW),
(SELECT index_id,index_column_id,column_id,key_ordinal,is_descending_key,is_included_column FROM sys.index_columns WHERE object_id=OBJECT_ID(@name) ORDER BY index_id,index_column_id FOR XML RAW);",c,tx){CommandTimeout=10}){cmd.Parameters.AddWithValue("@name",table);using(var r=await cmd.ExecuteReaderAsync(token)){await r.ReadAsync(token);parts.Add(table);for(int i=0;i<4;i++)parts.Add(r.GetString(i));}}
   return ConnectionContext.Hash(string.Join("\n",parts));
  }
  static async Task LockTarget(SqlConnection c,SqlTransaction tx,CreateIndexStatement index,CancellationToken token)
  {
   using(var check=new SqlCommand("IF OBJECT_ID(@table,'U') IS NULL THROW 51000,'Index target must be a table',1; IF EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(@table) AND name=@index) THROW 51000,'Index already exists; do not create twice',1;",c,tx)){check.Parameters.AddWithValue("@table",Target(index));check.Parameters.AddWithValue("@index",index.Name.Value);await check.ExecuteNonQueryAsync(token);}
   await Execute(c,tx,"SELECT TOP (1) 1 FROM "+Target(index)+" WITH (TABLOCKX,HOLDLOCK);",token,10);
  }
  public static string Evaluate(RunMetrics[] before,RunMetrics[] after)
  {
   if(before?.Length!=3||after?.Length!=3)return "缺少三輪完整測量。";
   var all=before.Concat(after).ToArray();if(all.Any(x=>x==null||x.Reads<0||string.IsNullOrEmpty(x.Digest)||string.IsNullOrEmpty(x.ResultContract)))return "測量證據不完整。";
   if(all.Select(x=>x.Digest+"|"+x.ResultContract+"|"+x.Rows).Distinct().Count()!=1)return "欄位或結果不一致，不可建立索引。";
   if(after.Any(x=>x.Warnings==null||x.Warnings.Length>0)||after.Select(x=>x.Shape).Distinct().Count()!=1)return "新增索引後計畫不穩定或含有警告。";
   if(after.Min(x=>x.Reads)>0&&after.Max(x=>x.Reads)>after.Min(x=>x.Reads)*1.2)return "新增索引後 logical reads 不穩定。";
   var old=DryRunResult.Median(before.Select(x=>x.Reads));var newer=DryRunResult.Median(after.Select(x=>x.Reads));return old>0&&newer<=old*0.95?"":"Logical reads 未降低至少 5%。";
  }
  public static async Task<IndexTrialResult> Test(ConnectionContext context,string query,string ddl,CancellationToken cancel,IProgress<string> progress=null)
  {
   var index=Parse(query,ddl);await new DbWorker().BuildPayload(query,context,cancel);var limits=DryRunBudget.Load();progress?.Report("等待其他索引作業完成…");await Gate.WaitAsync(cancel);
   var result=new IndexTrialResult{DatabaseFingerprint=context.Fingerprint,Sql=query,Ddl=ddl,DefinitionHash=ConnectionContext.Hash(query+"|"+ddl),Table=Target(index),IndexName=index.Name.Value};
   try{using(var budget=CancellationTokenSource.CreateLinkedTokenSource(cancel)){budget.CancelAfter(TimeSpan.FromSeconds(limits.TotalSeconds));var token=budget.Token;using(var c=context.Connect()){await c.OpenAsync(token);await Execute(c,null,"SET XACT_ABORT ON; SET LOCK_TIMEOUT 5000;",token);using(var tx=c.BeginTransaction(System.Data.IsolationLevel.Serializable))
   {
    try{progress?.Report("取得資料表鎖，開始 transaction 測試…");await LockTarget(c,tx,index,token);result.Inventory=await Inventory(c,tx,query,token);
     async Task<RunMetrics[]> Samples(string phase){var samples=new List<RunMetrics>();for(int i=0;i<3;i++){progress?.Report(phase+" "+(i+1)+"/3");var sample=await DbWorker.MeasureOnConnection(c,tx,query,limits,token);limits.AddSample(sample);sample.SavedPlan=ActualPlans.Save(context.Fingerprint,ConnectionContext.Hash(query),sample.Plan,limits.MaxPlanBytes);samples.Add(sample);}return samples.ToArray();}
     result.Before=await Samples("建立索引前");await Execute(c,tx,"SET STATISTICS XML OFF; SET STATISTICS IO OFF; SET STATISTICS TIME OFF;",token);progress?.Report("transaction 內建立索引…");await Execute(c,tx,ddl,token,limits.QuerySeconds);result.After=await Samples("建立索引後");result.Reason=Evaluate(result.Before,result.After);
    }finally{progress?.Report("Rollback 測試索引並釋放鎖…");tx.Rollback();result.RolledBack=true;}
   }
   await Execute(c,null,"SET STATISTICS XML OFF; SET STATISTICS IO OFF; SET STATISTICS TIME OFF;",token);
   if(await Inventory(c,null,query,token)!=result.Inventory)throw new InvalidOperationException("Rollback 後 schema／索引狀態與測試前不同，不能正式建立。");result.MeasuredUtc=DateTimeOffset.UtcNow;result.Eligible=string.IsNullOrEmpty(result.Reason);result.Reason=result.Eligible?"結果一致、logical reads 至少降低 5%；測試索引已 rollback，可確認正式建立。":result.Reason;result.Receipt=Save(result,"trial");return result;
   }}}finally{Gate.Release();}
  }
  static string Save(IndexTrialResult result,string operation)
  {
   var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Alyvo","SsmsAiSqlAssistant","index-trials");Directory.CreateDirectory(root);var path=Path.Combine(root,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+"-"+operation+".json");File.WriteAllText(path,JsonConvert.SerializeObject(result,Formatting.Indented));return path;
  }
  public static async Task Apply(ConnectionContext context,IndexTrialResult result,CancellationToken cancel)
  {
   if(result==null||result.DefinitionHash!=ConnectionContext.Hash(result.Sql+"|"+result.Ddl)||!result.Eligible||!result.RolledBack||result.Applied||result.DatabaseFingerprint!=context.Fingerprint||DateTimeOffset.UtcNow-result.MeasuredUtc>TimeSpan.FromMinutes(30)||Evaluate(result.Before,result.After)!="")throw new InvalidOperationException("證據不符、已建立或測試超過 30 分鐘，請重新測試。");
   var index=Parse(result.Sql,result.Ddl);if(Target(index)!=result.Table||index.Name.Value!=result.IndexName)throw new InvalidOperationException("索引目標已變更。");await DbWorker.VerifyEnvironment(context,cancel);if(!await Gate.WaitAsync(0,cancel))throw new InvalidOperationException("已有索引作業進行中。");
   try{using(var budget=CancellationTokenSource.CreateLinkedTokenSource(cancel)){budget.CancelAfter(TimeSpan.FromSeconds(90));var token=budget.Token;using(var c=context.Connect()){await c.OpenAsync(token);await Execute(c,null,"SET XACT_ABORT ON; SET LOCK_TIMEOUT 5000;",token);using(var tx=c.BeginTransaction(System.Data.IsolationLevel.Serializable)){try{await LockTarget(c,tx,index,token);if(await Inventory(c,tx,result.Sql,token)!=result.Inventory)throw new InvalidOperationException("資料表結構或索引已變更，請重新測試。");await Execute(c,tx,result.Ddl,token,90);tx.Commit();result.Applied=true;result.Eligible=false;}catch{try{tx.Rollback();}catch{}throw;}}}result.Receipt=Save(result,"apply");}}finally{Gate.Release();}
  }
 }
}
