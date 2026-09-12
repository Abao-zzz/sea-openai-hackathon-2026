using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

namespace Alyvo.SsmsAiSqlAssistant
{
 public sealed class SpReview
 {
  public string CandidateSql,OriginalHash,Error="",Status="準備中",SentSelect,Explanation="";
  public AnalysisDto Analysis;public WorkloadCase[] Cases=new WorkloadCase[0];public readonly List<Preflight> Preflights=new List<Preflight>();public readonly List<DryRunResult> Runs=new List<DryRunResult>();public int? Version;
  public bool SuggestionsOnly=>Analysis?.Candidates?.Length==0&&(Analysis.Suggestions?.Length??0)>0&&string.IsNullOrEmpty(Error);
  public bool PreflightPassed=>Cases.Length>0 && Preflights.Count==Cases.Length && Preflights.All(p=>p.Passed)&&string.IsNullOrEmpty(Error);
  public bool Passed=>PreflightPassed && Runs.Count==Cases.Length && Runs.All(r=>r.Passed);
  public long Tokens=>Analysis==null?0:(Analysis.Usage["inputTokens"]?.Value<long>()??0)+(Analysis.Usage["outputTokens"]?.Value<long>()??0);
  public long Reads=>Runs.SelectMany(r=>(r.Original??new RunMetrics[0]).Concat(r.Candidate??new RunMetrics[0])).Sum(r=>r.Reads);
  public long CpuMs=>Runs.SelectMany(r=>(r.Original??new RunMetrics[0]).Concat(r.Candidate??new RunMetrics[0])).Sum(r=>r.CpuMs);
 }
 public sealed class SpReviewWorker
 {
  readonly DbWorker db=new DbWorker();readonly LocalApi api=new LocalApi();
  public const string PlanSql=@"SELECT TOP (4) CONVERT(nvarchar(max),qp.query_plan),d.cached_time FROM sys.dm_exec_procedure_stats d CROSS APPLY sys.dm_exec_query_plan(d.plan_handle) qp WHERE d.database_id=DB_ID() AND d.object_id=@id AND DATALENGTH(CONVERT(nvarchar(max),qp.query_plan))<=8388608 ORDER BY d.last_execution_time DESC;";
  public const string QueryStorePlanSql=@"SELECT TOP (4) p.query_plan,p.last_compile_start_time FROM sys.query_store_query q JOIN sys.query_store_plan p ON p.query_id=q.query_id WHERE q.object_id=@id AND DATALENGTH(p.query_plan)<=8388608 ORDER BY p.last_compile_start_time DESC;";
  public static WorkloadCase[] ExtractCases(SpBody body,IEnumerable<Tuple<string,string,DateTimeOffset>> plans,DateTimeOffset observed)
  {
   var cases=new List<WorkloadCase>();XNamespace ns="http://schemas.microsoft.com/sqlserver/2004/07/showplan";
   foreach(var plan in plans)
   {
    XDocument doc;using(var reader=XmlReader.Create(new StringReader(plan.Item1),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=8388608}))doc=XDocument.Load(reader);
    foreach(var list in doc.Descendants(ns+"ParameterList"))foreach(var kind in new[]{"ParameterRuntimeValue","ParameterCompiledValue"})
    {
     var item=new WorkloadCase{Source=plan.Item2+" / "+kind,ObservedAt=plan.Item3};bool valid=true;
     foreach(var parameter in body.Parameters)
     {var values=list.Elements(ns+"ColumnReference").Where(x=>string.Equals((string)x.Attribute("Column"),parameter.Name,StringComparison.OrdinalIgnoreCase)).Select(x=>(string)x.Attribute(kind)).Where(x=>x!=null).Distinct().ToArray();if(values.Length!=1){valid=false;break;}try{item.Values[parameter.Name]=SpBody.Literal(values[0]);}catch{valid=false;break;}}
     if(valid && body.Parameters.Length>0 && !cases.Any(c=>c.Values.OrderBy(x=>x.Key).SequenceEqual(item.Values.OrderBy(x=>x.Key))))cases.Add(item);
     if(cases.Count==4)return cases.ToArray();
    }
   }
   if(body.Parameters.All(p=>p.DefaultSql!=null))
   {var item=new WorkloadCase{Source=body.Parameters.Length==0?"無參數 / definition":"default / definition",ObservedAt=observed};foreach(var p in body.Parameters)item.Values[p.Name]=p.DefaultSql;if(!cases.Any(c=>c.Values.OrderBy(x=>x.Key).SequenceEqual(item.Values.OrderBy(x=>x.Key))))cases.Add(item);}
   return cases.Take(4).ToArray();
  }
  async Task<WorkloadCase[]> Evidence(ConnectionContext context,SpScanRow row,CancellationToken token)
  {
   var plans=new List<Tuple<string,string,DateTimeOffset>>();string failure="";
   foreach(var query in new[]{Tuple.Create(PlanSql,"plan cache"),Tuple.Create(QueryStorePlanSql,"Query Store")})
   {
    try{using(var c=context.Connect()){await c.OpenAsync(token);using(var cmd=new SqlCommand(query.Item1,c){CommandTimeout=10}){cmd.Parameters.AddWithValue("@id",row.Module.Id);using(token.Register(()=>cmd.Cancel()))using(var r=await cmd.ExecuteReaderAsync(token))while(await r.ReadAsync(token))if(!r.IsDBNull(0)){var stamp=r.GetValue(1);plans.Add(Tuple.Create(r.GetString(0),query.Item2,stamp is DateTimeOffset dto?dto:new DateTimeOffset(DateTime.SpecifyKind(Convert.ToDateTime(stamp),DateTimeKind.Local))));}}}}catch(SqlException e){failure+=" "+query.Item2+" evidence 不可用（"+e.Number+"）。";}
    if(plans.Count>=4)break;
   }
   var cases=ExtractCases(row.Body,plans,DateTimeOffset.UtcNow);if(cases.Length==0)throw new InvalidOperationException("不安全參數：找不到完整、同來源與同觀測時間的 compiled/runtime/default 參數組；不猜測值。"+failure);return cases;
  }
  public static async Task AssertUnchanged(ConnectionContext context,SpScanRow row,string hash,CancellationToken token)
  {
   using(var c=context.Connect()){await c.OpenAsync(token);using(var cmd=new SqlCommand("SELECT definition FROM sys.sql_modules WHERE object_id=@id AND OBJECT_SCHEMA_NAME(object_id)=@schema AND OBJECT_NAME(object_id)=@name;",c)){cmd.Parameters.AddWithValue("@id",row.Module.Id);cmd.Parameters.AddWithValue("@schema",row.Module.Schema);cmd.Parameters.AddWithValue("@name",row.Module.Name);var definition=await cmd.ExecuteScalarAsync(token) as string;if(definition==null||ProcedureSource.Parse(definition,context.Database).Hash!=hash)throw new InvalidOperationException("來源 SP 已變更；請重新掃描，不可沿用舊證據。");}}
  }
  public async Task<SpReview> Prepare(ConnectionContext context,SpScanRow row,CancellationToken token,IProgress<string> progress,string jobId=null,string lease=null)
  {
   if(!row.Eligible)throw new InvalidOperationException(row.Reason);var review=new SpReview{OriginalHash=ProcedureSource.Parse(row.Module.Definition,context.Database).Hash};row.Review=review;
   try
   {
    progress?.Report("第 1/4：只載入本支 SP 的 plan / parameter evidence");await AssertUnchanged(context,row,review.OriginalHash,token);review.Cases=await Evidence(context,row,token);
    progress?.Report("第 2/4：未送 OpenAI；讀取 schema 與 estimated plan");var concrete=row.Body.Bind(row.Body.SelectSql,review.Cases[0]);var payload=JObject.FromObject(await db.BuildPayload(concrete,context,token));payload["selectedSql"]=row.Body.SelectSql;review.SentSelect=row.Body.SelectSql;
    progress?.Report("第 3/4：OpenAI 候選準備；不執行 SP");await api.Check(token);
    review.Analysis=LocalApi.Decode(await api.Send("/agent/stored-procedures/prepare",new{context=payload,trigger=jobId==null?"single-user":"batch-user",jobId,leaseToken=lease,objectId=(int?)row.Module.Id},token));
    if(review.Analysis.Candidates.Length==0){review.Status=review.SuggestionsOnly?"有索引／統計資訊優化建議":"目前沒有 SQL 改寫候選";review.Explanation=review.Analysis.Summary;AnalysisHistory.RecordSp(context,row,"analysis");return review;}
    var candidate=review.Analysis.Candidates[0];review.Explanation=candidate.Explanation;review.CandidateSql=row.Body.Candidate(candidate.Sql,context.Database);
    var prefilter=await api.Send("/agent/candidate-prefilter",new{candidate=new{sql=candidate.Sql,explanation=candidate.Explanation}},token);if(prefilter["eligible"]?.Value<bool>()!=true)throw new InvalidOperationException("候選 prefilter 拒絕。"+prefilter["reasons"]);
    for(int i=0;i<review.Cases.Length;i++){token.ThrowIfCancellationRequested();progress?.Report($"第 4/4：安全預檢 workload {i+1}/{review.Cases.Length}");var item=review.Cases[i];review.Preflights.Add(await db.Preflight(context,row.Body.Bind(row.Body.SelectSql,item),row.Body.Bind(candidate.Sql,item),token));}
    review.Status=review.PreflightPassed?"六關預檢通過；等待 SP Snapshot Dry Run":"候選已退回";
   }
   catch(OperationCanceledException){review.Error="使用者取消或預算到期。";review.Status="已取消";throw;}
   catch(Exception e){review.Error=DbWorker.SafeError(e);review.Status="候選已退回";}
   AnalysisHistory.RecordSp(context,row,"preflight");
   if(review.Analysis!=null)try{await api.Send("/agent/memory",new{databaseFingerprint=context.Fingerprint,sqlHash=ConnectionContext.Hash(row.Body.SelectSql),outcome=review.PreflightPassed?"passed":"rejected"},token);}catch{Diagnostics.Write("Decision memory persistence failed");}
   return review;
  }
  public async Task Verify(ConnectionContext context,SpScanRow row,CancellationToken token,IProgress<string> progress,Action<RunMetrics> measured=null)
  {
   var review=row.Review;if(review?.PreflightPassed!=true)throw new InvalidOperationException("所有 workload 六關預檢通過前不能建立 Snapshot。");await AssertUnchanged(context,row,review.OriginalHash,token);review.Runs.Clear();
   var candidate=SpBody.Parse(review.CandidateSql);for(int i=0;i<review.Cases.Length;i++)
   {
    token.ThrowIfCancellationRequested();progress?.Report($"SP Snapshot workload {i+1}/{review.Cases.Length}");var item=review.Cases[i];var run=await db.DryRun(context,row.Body.Bind(row.Body.SelectSql,item),candidate.Bind(candidate.SelectSql,item),review.Preflights[i],token,progress,measured);review.Runs.Add(run);
    if(run.Passed && DryRunResult.Median(run.Candidate.Select(x=>x.Reads))>DryRunResult.Median(run.Original.Select(x=>x.Reads))*0.95){run.Passed=false;run.Reason="實際 median reads 未降低 5%。";}
    if(!run.Passed){review.Status="候選已退回";AnalysisHistory.RecordSp(context,row,"snapshot");return;}
   }
   review.Status=review.Passed?"所有 workload Snapshot 通過；可接受並建立版本":"候選已退回";AnalysisHistory.RecordSp(context,row,"snapshot");
  }
 }
}
