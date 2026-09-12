using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Alyvo.SsmsAiSqlAssistant
{
 public sealed class AnalysisHistoryEntry
 {
  public string Id,DatabaseFingerprint,SqlFingerprint,Stage,Verification,ApplyStatus,Summary,Model;
  public DateTimeOffset Time;public long InputTokens,CachedTokens,CacheWriteTokens,OutputTokens;
 }
 public sealed class AnalysisHistory
 {
  readonly string path;static readonly object Sync=new object();
  public AnalysisHistory(string path=null){this.path=path??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Alyvo","SsmsAiSqlAssistant","analysis-history.db");}
  SqliteConnection Open()
  {
   Directory.CreateDirectory(Path.GetDirectoryName(path));var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString());c.Open();using(var cmd=c.CreateCommand()){cmd.CommandText="PRAGMA secure_delete=ON; PRAGMA busy_timeout=5000; CREATE TABLE IF NOT EXISTS analysis_history(id TEXT PRIMARY KEY,data TEXT NOT NULL);";cmd.ExecuteNonQuery();}return c;
  }
  public void Save(AnalysisHistoryEntry entry)
  {
   if(!System.Text.RegularExpressions.Regex.IsMatch(entry.DatabaseFingerprint??"","^[a-f0-9]{64}$")||!System.Text.RegularExpressions.Regex.IsMatch(entry.SqlFingerprint??"","^[a-f0-9]{64}$"))throw new InvalidOperationException("歷史指紋無效。");
   if(!new[]{"analysis","preflight","snapshot","apply","restore"}.Contains(entry.Stage)||!new[]{"not-run","passed","failed"}.Contains(entry.Verification)||!new[]{"not-applied","applied","restored"}.Contains(entry.ApplyStatus))throw new InvalidOperationException("歷史狀態無效。");
   if(!System.Text.RegularExpressions.Regex.IsMatch(entry.Id??"","^[a-f0-9]{32}$"))throw new InvalidOperationException("歷史 ID 無效。");
   if(!System.Text.RegularExpressions.Regex.IsMatch(entry.Model??"","^[a-zA-Z0-9._:/-]{0,128}$"))entry.Model="unknown-model";
   // Summary is generated from enum states, never copied from SQL or AI prose.
   entry.Summary=entry.Stage+" / "+entry.Verification+" / "+entry.ApplyStatus;
   lock(Sync)using(var c=Open())using(var cmd=c.CreateCommand()){cmd.CommandText="INSERT INTO analysis_history(id,data) VALUES($id,$data) ON CONFLICT(id) DO UPDATE SET data=excluded.data";cmd.Parameters.AddWithValue("$id",entry.Id);cmd.Parameters.AddWithValue("$data",JsonConvert.SerializeObject(entry));cmd.ExecuteNonQuery();}
  }
  public List<AnalysisHistoryEntry> List()
  {
   lock(Sync)using(var c=Open())using(var cmd=c.CreateCommand()){cmd.CommandText="SELECT data FROM analysis_history";using(var r=cmd.ExecuteReader()){var entries=new List<AnalysisHistoryEntry>();while(r.Read())entries.Add(JsonConvert.DeserializeObject<AnalysisHistoryEntry>(r.GetString(0)));return entries.OrderByDescending(x=>x.Time).ToList();}}
  }
  public void Delete(string id=null){lock(Sync)using(var c=Open())using(var cmd=c.CreateCommand()){cmd.CommandText=id==null?"DELETE FROM analysis_history":"DELETE FROM analysis_history WHERE id=$id";if(id!=null)cmd.Parameters.AddWithValue("$id",id);cmd.ExecuteNonQuery();}}
  public static void Record(AnalysisState state,string stage,string apply="not-applied")
  {
   if(state?.Response==null)return;
   try{var usage=JObject.FromObject(state.Response.Usage);long Value(string key)=>usage[key]?.Value<long>()??0;
    new AnalysisHistory().Save(new AnalysisHistoryEntry{Id=state.Response.Id,Time=DateTimeOffset.UtcNow,DatabaseFingerprint=state.ConnectionFingerprint,SqlFingerprint=state.OriginalHash,Stage=stage,Verification=state.DryRun==null?"not-run":state.DryRun.Passed?"passed":"failed",ApplyStatus=apply,Model=usage["model"]?.Value<string>()??"",InputTokens=Value("inputTokens"),CachedTokens=Value("cachedInputTokens"),CacheWriteTokens=Value("cacheWriteTokens"),OutputTokens=Value("outputTokens")});
   }catch{Diagnostics.Write("Minimal analysis history persistence failed");}
  }
  public static string Csv(IEnumerable<AnalysisHistoryEntry> entries)
  {
   string Cell(string text)=>"\""+((text.Length>0&&"=+-@\t\r\n".Contains(text[0]))?"'":"")+text.Replace("\"","\"\"")+"\"";
   return "識別碼,本機時間,資料庫指紋,SQL指紋,階段,驗證,套用,摘要,模型,input,cached,cache-write,output\r\n"+string.Join("\r\n",entries.Select(x=>string.Join(",",new[]{x.Id,x.Time.ToLocalTime().ToString("O"),x.DatabaseFingerprint,x.SqlFingerprint,x.Stage,x.Verification,x.ApplyStatus,x.Summary,x.Model,x.InputTokens.ToString(CultureInfo.InvariantCulture),x.CachedTokens.ToString(CultureInfo.InvariantCulture),x.CacheWriteTokens.ToString(CultureInfo.InvariantCulture),x.OutputTokens.ToString(CultureInfo.InvariantCulture)}.Select(v=>Cell(v??"")))));
  }
  public static void RecordSp(ConnectionContext context,SpScanRow row,string stage,string apply="not-applied")
  {
   var review=row.Review;if(review?.Analysis==null)return;Record(new AnalysisState{Response=review.Analysis,ConnectionFingerprint=context.Fingerprint,OriginalHash=ConnectionContext.Hash(row.Body.SelectSql),DryRun=review.Runs.Count==0?null:new DryRunResult{Passed=review.Passed}},stage,apply);
  }
 }
}
