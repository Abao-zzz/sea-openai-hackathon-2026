using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
namespace Alyvo.SsmsAiSqlAssistant
{
 public sealed class DbWorker
 {
  private static readonly SemaphoreSlim SnapshotGate=new SemaphoreSlim(1,1);
  private static readonly XNamespace PlanNs="http://schemas.microsoft.com/sqlserver/2004/07/showplan";
  public static string Quote(string identifier)=>"["+identifier.Replace("]","]]")+"]";
  private static async Task Open(SqlConnection c,CancellationToken t)=>await c.OpenAsync(t);
  private static async Task<int> Execute(SqlConnection c,string sql,CancellationToken t,int timeout=15)
  {using(var cmd=new SqlCommand(sql,c){CommandTimeout=timeout})using(t.Register(()=>cmd.Cancel()))return await cmd.ExecuteNonQueryAsync(t);}
  public static async Task<int> VerifyEnvironment(ConnectionContext context,CancellationToken token)
  {
   using(var c=context.Connect()){await Open(c,token);using(var cmd=new SqlCommand("SELECT CONVERT(int,SERVERPROPERTY('ProductMajorVersion')),compatibility_level,database_id,source_database_id FROM sys.databases WHERE database_id=DB_ID();",c))using(var reader=await cmd.ExecuteReaderAsync(token))
    {if(!await reader.ReadAsync(token)||reader.GetInt32(0)!=17||reader.GetByte(1)!=170||!reader.IsDBNull(3))throw new InvalidOperationException("來源必須為 SQL Server 2025（17.x）、compatibility 170 的非 Snapshot 資料庫。");return reader.GetInt32(2);}}
  }
  public async Task<object> BuildPayload(string sql,ConnectionContext context,CancellationToken token)
  {
   var parsed=SqlSafety.Parse(sql);if(parsed.Errors.Any())throw new InvalidOperationException(string.Join("\n",parsed.Errors.Distinct()));await VerifyEnvironment(context,token);
   var metadata=new List<object>();using(var c=context.Connect()){await Open(c,token);foreach(var name in parsed.Objects.Distinct(StringComparer.OrdinalIgnoreCase))
   {
    await CheckDependencies(c,name,token);
    using(var cmd=new SqlCommand("SELECT c.name,t.name,c.is_nullable FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id WHERE c.object_id=OBJECT_ID(@name) ORDER BY c.column_id;",c)){cmd.Parameters.AddWithValue("@name",name);var columns=new List<object>();using(var r=await cmd.ExecuteReaderAsync(token))while(await r.ReadAsync(token))columns.Add(new{name=r.GetString(0),sqlType=r.GetString(1),nullable=r.GetBoolean(2)});if(columns.Count==0)throw new InvalidOperationException("引用物件 metadata 不完整，已停止分析。");metadata.Add(new{name,columns=columns.ToArray()});}
   }}var plan=await Estimated(context,sql,token);
   return new{selectedSql=sql,metadata=metadata.ToArray(),estimatedPlan=new{estimatedCost=plan.Cost,estimatedRows=plan.Rows,scanCount=plan.Scans},serverFingerprint=ConnectionContext.Hash(context.Server.ToUpperInvariant()),databaseFingerprint=context.Fingerprint};
  }
  private static async Task CheckDependencies(SqlConnection c,string name,CancellationToken token)
  {
   const string query=@"DECLARE @root int=OBJECT_ID(@name);
IF @root IS NULL THROW 51001,'Referenced object metadata unavailable',1;
;WITH refs AS (
 SELECT o.object_id,o.type,0 AS depth FROM sys.objects o WHERE o.object_id=@root
 UNION ALL SELECT o.object_id,o.type,r.depth+1 FROM refs r JOIN sys.sql_expression_dependencies d ON d.referencing_id=r.object_id JOIN sys.objects o ON o.object_id=d.referenced_id WHERE r.depth<32)
SELECT COUNT(*) FROM refs r WHERE r.depth=32 OR r.type NOT IN ('U','V') OR (r.type='V' AND NOT EXISTS(SELECT 1 FROM sys.sql_modules m WHERE m.object_id=r.object_id AND m.definition IS NOT NULL)) OR EXISTS(SELECT 1 FROM sys.sql_expression_dependencies d WHERE d.referencing_id=r.object_id AND (d.referenced_database_name IS NOT NULL OR d.referenced_server_name IS NOT NULL OR d.referenced_id IS NULL)) OPTION(MAXRECURSION 32);";
   using(var cmd=new SqlCommand(query,c){CommandTimeout=10}){cmd.Parameters.AddWithValue("@name",name);if(Convert.ToInt32(await cmd.ExecuteScalarAsync(token),CultureInfo.InvariantCulture)>0)throw new InvalidOperationException("引用包含無法驗證的 View、函式或跨資料庫相依性。");}
  }
  public static PlanEvidence ParsePlan(string xml)
  {
   XDocument doc;using(var reader=XmlReader.Create(new StringReader(xml),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=8*1024*1024}))doc=XDocument.Load(reader);
   var statements=doc.Descendants(PlanNs+"StmtSimple").ToArray();if(statements.Length==0||statements.Any(x=>(string)x.Attribute("StatementType")!="SELECT"))throw new InvalidOperationException("執行計畫不是可驗證 SELECT。");
   double Number(XAttribute a)=>a==null?0:double.Parse(a.Value,CultureInfo.InvariantCulture);
   var cost=statements.Sum(x=>Number(x.Attribute("StatementSubTreeCost")));if(double.IsNaN(cost)||double.IsInfinity(cost))throw new InvalidOperationException("執行計畫成本無效。");
   var warnings=doc.Descendants(PlanNs+"Warnings").SelectMany(x=>x.Elements().Select(e=>e.Name.LocalName).Concat(x.Attributes().Select(a=>a.Name.LocalName))).Distinct().ToArray();
   return new PlanEvidence{Xml=xml,Cost=cost,Rows=statements.Sum(x=>Number(x.Attribute("StatementEstRows"))),Scans=doc.Descendants(PlanNs+"RelOp").Count(x=>(x.Attribute("PhysicalOp")?.Value??"").Contains("Scan")),Warnings=warnings};
  }
  public static async Task<PlanEvidence> Estimated(ConnectionContext context,string sql,CancellationToken token)
  {
   if(SqlSafety.Parse(sql).Errors.Any())throw new InvalidOperationException("唯讀安全檢查未通過。");
   using(var c=context.Connect()){await Open(c,token);await Execute(c,"SET SHOWPLAN_XML ON;",token);try{using(var cmd=new SqlCommand(sql,c){CommandTimeout=15})using(token.Register(()=>cmd.Cancel())){var xml=await cmd.ExecuteScalarAsync(token) as string;if(string.IsNullOrWhiteSpace(xml))throw new InvalidOperationException("無法取得 estimated plan。");return ParsePlan(xml);}}finally{await Execute(c,"SET SHOWPLAN_XML OFF;",CancellationToken.None);}}
  }
  public static async Task<ColumnContract[]> Columns(ConnectionContext context,string sql,CancellationToken token)
  {
   using(var c=context.Connect()){await Open(c,token);using(var cmd=new SqlCommand("SELECT column_ordinal,name,system_type_name,is_nullable,collation_name,error_number FROM sys.dm_exec_describe_first_result_set(@sql,NULL,0) WHERE is_hidden=0 OR error_number IS NOT NULL ORDER BY column_ordinal;",c){CommandTimeout=15})
   {cmd.Parameters.Add("@sql",SqlDbType.NVarChar,-1).Value=sql;var cols=new List<ColumnContract>();using(var r=await cmd.ExecuteReaderAsync(token))while(await r.ReadAsync(token)){if(!r.IsDBNull(5)||r.IsDBNull(1)||r.IsDBNull(2)||r.IsDBNull(3))throw new InvalidOperationException("無法驗證輸出欄位契約；請為輸出欄位命名。");cols.Add(new ColumnContract{Ordinal=r.GetInt32(0),Name=r.GetString(1),Type=r.GetString(2),Nullable=r.GetBoolean(3),Collation=r.IsDBNull(4)?"":r.GetString(4)});}if(cols.Count==0)throw new InvalidOperationException("沒有可驗證的輸出欄位。");return cols.ToArray();}}
  }
  public async Task<Preflight> Preflight(ConnectionContext context,string original,string candidate,CancellationToken token)
  {
   try
   {
    var originals=SqlSafety.ReadonlyStatements(original);var candidates=SqlSafety.ReadonlyStatements(candidate);
    if(originals.Length!=candidates.Length)return new Preflight{Gates=new[]{new Gate("結果集契約",false,"結果集數量不同。")}};
    if(originals.Length>1)
    {
     var parts=new List<Preflight>();for(int i=0;i<originals.Length;i++)parts.Add(await Preflight(context,originals[i],candidates[i],token));
     return new Preflight{Gates=Enumerable.Range(0,6).Select(i=>new Gate(parts[0].Gates[i].Name,parts.All(p=>p.Gates.Length==6&&p.Gates[i].Passed),string.Join("；",parts.Select((p,n)=>"結果集 "+(n+1)+"："+p.Gates[i].Reason)))).ToArray(),Columns=parts.SelectMany(p=>p.Columns??Array.Empty<ColumnContract>()).ToArray(),Original=new PlanEvidence{Cost=parts.Sum(p=>p.Original?.Cost??0)},Candidate=new PlanEvidence{Cost=parts.Sum(p=>p.Candidate?.Cost??0)}};
    }
   }
   catch(InvalidOperationException){ /* Report ordinary safety gates below. */ }
   var gates=new List<Gate>();var safe=SqlSafety.Parse(original);var other=SqlSafety.Parse(candidate);bool readOnly=!safe.Errors.Any()&&!other.Errors.Any();gates.Add(new Gate("唯讀",readOnly,readOnly?"原版與候選皆為單一唯讀 SELECT。":string.Join("；",safe.Errors.Concat(other.Errors).Distinct())));
   bool different=SqlSafety.Canonical(original)!=SqlSafety.Canonical(candidate);gates.Add(new Gate("實質差異",different,different?"候選包含實質 SQL 變更。":"候選只改變格式或完全相同。"));var result=new Preflight();
   try
   {
    if(!readOnly)throw new InvalidOperationException("唯讀安全檢查未通過。");
    using(var c=context.Connect()){await Open(c,token);foreach(var n in other.Objects.Distinct())await CheckDependencies(c,n,token);}
    result.Original=await Estimated(context,original,token);result.Candidate=await Estimated(context,candidate,token);gates.Add(new Gate("編譯",true,"原版與候選 estimated plan 編譯成功；尚未執行 SQL。"));
    var columns=await Columns(context,original,token);var cc=await Columns(context,candidate,token);result.Columns=columns;bool match=columns.Select(c=>c.Signature).SequenceEqual(cc.Select(c=>c.Signature));gates.Add(new Gate("欄位契約",match,match?"欄位名稱、型別、NULL 與 collation 契約一致。":"輸出欄位契約不同，拒絕套用。"));
    bool clean=result.Candidate.Warnings.Length==0;gates.Add(new Gate("Plan warning",clean,clean?"候選沒有 plan warning。":"候選包含警告："+string.Join("、",result.Candidate.Warnings)));
    var improvement=result.Original.Cost>0?1-result.Candidate.Cost/result.Original.Cost:0;gates.Add(new Gate("Estimated cost",improvement>=0.05,$"預估成本 {result.Original.Cost:G6} → {result.Candidate.Cost:G6}；降低 {improvement:P1}，門檻 5%。"));
   }
   catch(OperationCanceledException){throw;}catch(Exception e){while(gates.Count<6)gates.Add(new Gate(new[]{"唯讀","實質差異","編譯","欄位契約","Plan warning","Estimated cost"}[gates.Count],false,gates.Count==2?SafeError(e):"前置驗證未通過，未執行。"));}
   result.Gates=gates.ToArray();return result;
  }
  public static string SafeError(Exception e)=>e is SqlException?"SQL Server 無法完成驗證（錯誤代碼 "+((SqlException)e).Number+"），請檢查權限、SQL 或連線。":e is OperationCanceledException?"操作已取消。":e.Message;
  private sealed class BaselineEvidence{public bool Exact;public string Summary;}
  private async Task<BaselineEvidence> QueryStore(ConnectionContext context,string sql,Preflight preflight,CancellationToken token)
  {
   var none=new BaselineEvidence{Summary="沒有精確 Query Store baseline；原版與候選各三次交錯取樣。"};
   if(sql.Contains("@")||SqlSafety.ReadonlyStatements(sql).Length!=1||string.IsNullOrEmpty(preflight.Original?.Xml))return none;
   var hash=(string)XDocument.Parse(preflight.Original.Xml).Descendants(PlanNs+"StmtSimple").Single().Attribute("QueryPlanHash");if(string.IsNullOrEmpty(hash))return none;
   try
   {
    using(var c=context.Connect()){await Open(c,token);using(var cmd=new SqlCommand(@"SELECT COUNT(DISTINCT q.query_id),COUNT(DISTINCT p.plan_id),SUM(rs.count_executions),SUM(rs.avg_logical_io_reads*rs.count_executions)/NULLIF(SUM(rs.count_executions),0),MIN(CONVERT(varchar(18),p.query_plan_hash,1)),MAX(CONVERT(varchar(18),p.query_plan_hash,1))
FROM sys.query_store_query_text t JOIN sys.query_store_query q ON q.query_text_id=t.query_text_id JOIN sys.query_store_plan p ON p.query_id=q.query_id JOIN sys.query_store_runtime_stats rs ON rs.plan_id=p.plan_id
WHERE CONVERT(varbinary(max),t.query_sql_text)=CONVERT(varbinary(max),@sql) AND q.object_id=0 AND q.query_parameterization_type=0 AND rs.execution_type=0 AND rs.count_executions>0;",c){CommandTimeout=3})
    {cmd.Parameters.Add("@sql",SqlDbType.NVarChar,-1).Value=sql.Trim().TrimEnd(';').TrimEnd();using(var r=await cmd.ExecuteReaderAsync(token)){if(!await r.ReadAsync(token)||r.GetInt32(0)!=1||r.GetInt32(1)!=1||r.IsDBNull(2)||r.IsDBNull(3)||r.IsDBNull(4)||r.IsDBNull(5)||!string.Equals(r.GetString(4),hash,StringComparison.OrdinalIgnoreCase)||!string.Equals(r.GetString(5),hash,StringComparison.OrdinalIgnoreCase))return none;var reads=Convert.ToDouble(r.GetValue(3));if(reads<=0||double.IsNaN(reads)||double.IsInfinity(reads))return none;return new BaselineEvidence{Exact=true,Summary="精確 SQL bytes / 單一 query 與 plan hash 相符；歷史執行 "+r.GetValue(2)+" 次，平均 reads "+reads.ToString("G6",CultureInfo.InvariantCulture)+"。原版一次、候選三次 Snapshot 實測。"};}}}
   }
   catch(OperationCanceledException){throw;}catch{return none;}
  }
  public async Task<DryRunResult> DryRun(ConnectionContext context,string original,string candidate,Preflight preflight,CancellationToken token,IProgress<string> progress=null,Action<RunMetrics> measured=null)
  {
   if(preflight?.Passed!=true)throw new InvalidOperationException("六關預檢尚未通過。");
   var limits=DryRunBudget.Load();
   var sourceId=await VerifyEnvironment(context,token);if(sourceId<=4)throw new InvalidOperationException("禁止驗證 system database。");
   if(!await SnapshotGate.WaitAsync(0,token))throw new InvalidOperationException("此 process 已有 Snapshot 批次執行中。");
   var name="AlyvoVerify_"+Regex.Replace(context.Database,"[^a-zA-Z0-9_]","_").Substring(0,Math.Min(context.Database.Length,48))+"_"+DateTime.UtcNow.ToString("yyyyMMddHHmmss",CultureInfo.InvariantCulture)+"_"+Guid.NewGuid().ToString("N");var result=new DryRunResult{Snapshot=name,Status="建立 Snapshot..."};
   using(var budget=CancellationTokenSource.CreateLinkedTokenSource(token))
   {
    budget.CancelAfter(TimeSpan.FromSeconds(limits.TotalSeconds));var t=budget.Token;
    try
    {
     progress?.Report("建立 Snapshot...");await CheckSnapshotEnvironment(context,t);var qs=await QueryStore(context,original,preflight,t);result.QueryStore=qs.Summary;result.ExactQueryStoreBaseline=qs.Exact;var files=new List<string>();
     using(var source=context.Connect()){await Open(source,t);using(var cmd=new SqlCommand("SELECT name,physical_name,type FROM sys.database_files WHERE type<>1;",source))using(var r=await cmd.ExecuteReaderAsync(t))while(await r.ReadAsync(t)){if(r.GetByte(2)!=0)throw new InvalidOperationException("此資料庫含不支援的 Snapshot 檔案類型。");var path=Path.Combine(Path.GetDirectoryName(r.GetString(1)),name+"_"+files.Count+".ss");files.Add("(NAME=N'"+r.GetString(0).Replace("'","''")+"',FILENAME=N'"+path.Replace("'","''")+"')");}}
     if(files.Count==0)throw new InvalidOperationException("找不到來源 data file。");
     using(var master=context.Connect("master")){await Open(master,t);await Execute(master,"CREATE DATABASE "+Quote(name)+" ON "+string.Join(",",files)+" AS SNAPSHOT OF "+Quote(context.Database)+";",t,30);}
     var baseline=new List<RunMetrics>();var proposed=new List<RunMetrics>();var saved=new HashSet<string>();
     async Task<RunMetrics> Sample(string sql){var sample=await Measure(context,name,sourceId,sql,limits,t);limits.AddSample(sample);if(saved.Add(sql))sample.SavedPlan=ActualPlans.Save(context.Fingerprint,ConnectionContext.Hash(sql),sample.Plan,limits.MaxPlanBytes);measured?.Invoke(sample);return sample;}
     for(int i=0;i<3;i++){progress?.Report($"Dry Run {i+1}/3：在唯讀 Snapshot 比對結果與實際計畫...");if(qs.Exact){if(i==0)baseline.Add(await Sample(original));proposed.Add(await Sample(candidate));}else if(i%2==0){baseline.Add(await Sample(original));proposed.Add(await Sample(candidate));}else{proposed.Add(await Sample(candidate));baseline.Add(await Sample(original));}}
     result.Original=baseline.ToArray();result.Candidate=proposed.ToArray();Evaluate(result);result.Status="Dry Run 完成";
    }
    catch(OperationCanceledException){result.Status=token.IsCancellationRequested?"Dry Run 已取消":"Dry Run 失敗";result.Reason=token.IsCancellationRequested?"使用者取消。":"超過 "+limits.TotalSeconds+" 秒整批預算。";result.Passed=false;}
    catch(Exception e){result.Status="Dry Run 失敗";result.Reason=SafeError(e);result.Passed=false;}
    finally{try{await Cleanup(context,name,sourceId);}catch{result.CleanupWarning="Snapshot 清理失敗，請檢查 "+name+"；完成清理前不可套用。";result.Passed=false;}finally{SnapshotGate.Release();}}
   }
   return result;
  }
  private static async Task CheckSnapshotEnvironment(ConnectionContext context,CancellationToken token)
  {
   using(var c=context.Connect())
   {
    await Open(c,token);
    using(var cmd=new SqlCommand("SELECT CONVERT(int,SERVERPROPERTY('EngineEdition')),CONVERT(nvarchar(128),SERVERPROPERTY('Edition')),collation_name,is_query_store_on,HAS_PERMS_BY_NAME(NULL,NULL,'CREATE ANY DATABASE'),HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','VIEW DATABASE STATE') FROM sys.databases WHERE database_id=DB_ID();",c))
    using(var r=await cmd.ExecuteReaderAsync(token))
    {
     if(!await r.ReadAsync(token)||r.IsDBNull(0)||!new[]{2,3}.Contains(r.GetInt32(0))||r.IsDBNull(1)||r.IsDBNull(2)||r.IsDBNull(3)||r.IsDBNull(4)||r.GetInt32(4)!=1||r.IsDBNull(5)||r.GetInt32(5)!=1)throw new InvalidOperationException("Snapshot edition / collation / Query Store 狀態或必要權限檢查未通過。");
    }
    using(var cmd=new SqlCommand("SELECT COUNT(*) FROM sys.database_files f OUTER APPLY sys.dm_os_volume_stats(DB_ID(),f.file_id) v WHERE f.type=0 AND (v.supports_sparse_files IS NULL OR v.supports_sparse_files<>1);",c){CommandTimeout=10})
    {if(Convert.ToInt32(await cmd.ExecuteScalarAsync(token))!=0)throw new InvalidOperationException("資料檔磁碟不支援 sparse file，或無法查證。");}
    using(var cmd=new SqlCommand("SELECT name FROM sys.databases WHERE name LIKE N'AlyvoVerify[_]%' OR name LIKE N'AlyvoAiDryRun[_]%';",c))
    using(var r=await cmd.ExecuteReaderAsync(token))
    {var names=new List<string>();while(await r.ReadAsync(token))names.Add(r.GetString(0));if(names.Count>0)throw new InvalidOperationException("伺服器有殘留 Snapshot，請人工確認："+string.Join("、",names));}
   }
  }
  public static void Evaluate(DryRunResult result)
  {
   var a=result.Original;var b=result.Candidate;var reasons=new List<string>();if(a==null||b==null||a.Length!=(result.ExactQueryStoreBaseline?1:3)||b.Length!=3){result.Passed=false;result.Reason="缺少三輪完整證據。";return;}
   bool equal=a.Concat(b).All(r=>r.Digest==a[0].Digest&&r.Rows==a[0].Rows&&r.Columns==a[0].Columns&&r.ResultContract==a[0].ResultContract);if(!equal)reasons.Add("結果或重複執行結果不一致。");
   if(b.Select(r=>r.Shape).Distinct().Count()!=1 || (b.Min(r=>r.Reads)>0 && b.Max(r=>r.Reads)>b.Min(r=>r.Reads)*1.2))reasons.Add("候選計畫或 logical reads 不穩定。");
   if(DryRunResult.Median(b.Select(r=>r.Reads))>=DryRunResult.Median(a.Select(r=>r.Reads)))reasons.Add("median logical reads 沒有降低。");
   if(a.Concat(b).Any(r=>r.Reads<0))reasons.Add("logical reads 不可用。");
   if(b.Any(r=>r.Warnings==null||r.Warnings.Length>0))reasons.Add("actual plan 警告或 regression。");
   result.Passed=reasons.Count==0;result.Reason=result.Passed?"完整取樣結果一致、候選穩定、median reads 降低且 actual plan 無 regression。":string.Join("\n",reasons);
  }
  private static async Task AssertSnapshot(SqlConnection c,int sourceId,CancellationToken token)
  {using(var cmd=new SqlCommand("SELECT database_id,source_database_id,is_read_only FROM sys.databases WHERE database_id=DB_ID();",c))using(var r=await cmd.ExecuteReaderAsync(token))if(!await r.ReadAsync(token)||r.GetInt32(0)==sourceId||r.IsDBNull(1)||r.GetInt32(1)!=sourceId||!r.GetBoolean(2))throw new InvalidOperationException("目的地不是經確認的唯讀 Snapshot；拒絕執行候選。");}
  public static async Task Cleanup(ConnectionContext context,string name,int sourceId)
  {
   if(!Regex.IsMatch(name,"^AlyvoVerify_[a-zA-Z0-9_]{1,48}_[0-9]{14}_[a-f0-9]{32}$"))throw new InvalidOperationException("Snapshot 名稱不符。");
   using(var c=context.Connect("master")){await Open(c,CancellationToken.None);using(var cmd=new SqlCommand("SELECT source_database_id FROM sys.databases WHERE name=@name;",c)){cmd.Parameters.AddWithValue("@name",name);var id=await cmd.ExecuteScalarAsync();if(id==null)return;if(id==DBNull.Value||Convert.ToInt32(id)!=sourceId)throw new InvalidOperationException("拒絕移除非本次 Snapshot。");}await Execute(c,"DROP DATABASE "+Quote(name)+";",CancellationToken.None,15);}
  }
  private static async Task<RunMetrics> Measure(ConnectionContext context,string snapshot,int sourceId,string sql,DryRunBudget limits,CancellationToken token)
  {
   var statements=SqlSafety.ReadonlyStatements(sql);var result=new RunMetrics();var messages=new StringBuilder();var fingerprints=new List<string>();var plans=new List<XElement>();var contracts=new List<string>();var compareInfos=new List<CompareInfo[]>();var compareOptions=new List<CompareOptions[]>();
   using(var c=context.Connect(snapshot))
   {
    await Open(c,token);await AssertSnapshot(c,sourceId,token);
    foreach(var statement in statements)
    {
     using(var metadata=new SqlCommand("SELECT collation_name,CONVERT(int,COLLATIONPROPERTY(collation_name,'LCID')),CONVERT(int,COLLATIONPROPERTY(collation_name,'ComparisonStyle')) FROM sys.dm_exec_describe_first_result_set(@sql,NULL,0) WHERE is_hidden=0 ORDER BY column_ordinal;",c){CommandTimeout=10})
     {
      metadata.Parameters.Add("@sql",SqlDbType.NVarChar,-1).Value=statement;var infos=new List<CompareInfo>();var options=new List<CompareOptions>();
      using(var reader=await metadata.ExecuteReaderAsync(token))while(await reader.ReadAsync(token))
      {
       if(reader.IsDBNull(0)){infos.Add(null);options.Add(CompareOptions.None);continue;}
       var collation=reader.GetString(0);contracts.Add("collation="+collation);
       if(collation.Contains("_BIN")){infos.Add(null);options.Add(CompareOptions.Ordinal);continue;}
       if(reader.IsDBNull(1)||reader.IsDBNull(2))throw new InvalidOperationException("Collation 比較規則不可用。");
       var style=reader.GetInt32(2);var option=CompareOptions.None;if((style&1)!=0)option|=CompareOptions.IgnoreCase;if((style&2)!=0)option|=CompareOptions.IgnoreNonSpace;if((style&65536)!=0)option|=CompareOptions.IgnoreKanaType;if((style&131072)!=0)option|=CompareOptions.IgnoreWidth;
       infos.Add(CultureInfo.GetCultureInfo(reader.GetInt32(1)).CompareInfo);options.Add(option);
      }
      compareInfos.Add(infos.ToArray());compareOptions.Add(options.ToArray());
     }
    }
    await Execute(c,"SET LANGUAGE us_english; SET NOCOUNT ON; SET STATISTICS IO ON; SET STATISTICS TIME ON; SET STATISTICS XML ON;",token);
    c.InfoMessage+=(s,e)=>messages.AppendLine(e.Message);
    using(var cmd=new SqlCommand(sql,c){CommandTimeout=limits.QuerySeconds})using(token.Register(()=>cmd.Cancel()))using(var r=await cmd.ExecuteReaderAsync(token))
    {
     int sets=0;do
     {
      if(r.FieldCount==0)continue;
      if(r.FieldCount==1&&r.GetName(0)=="Microsoft SQL Server 2005 XML Showplan"){if(await r.ReadAsync(token)){var xml=r.GetString(0);using(var reader=XmlReader.Create(new StringReader(xml),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=8*1024*1024}))plans.Add(XElement.Load(reader));}continue;}
      sets++;if(sets>statements.Length)throw new InvalidOperationException("結果集數量不同。");result.Columns+=r.FieldCount;
      var schema=r.GetSchemaTable();foreach(DataRow column in schema.Rows)contracts.Add(string.Join("|",new[]{"ColumnOrdinal","ColumnName","DataTypeName","AllowDBNull","ColumnSize","NumericPrecision","NumericScale"}.Select(k=>schema.Columns.Contains(k)?Convert.ToString(column[k],CultureInfo.InvariantCulture):"")));contracts.Add("END_SET");
      using(var fingerprint=new ResultFingerprint(SqlSafety.Parse(statements[sets-1]).Ordered)){
      while(await r.ReadAsync(token))
      {
       result.Rows++;
       var values=new object[r.FieldCount];for(int column=0;column<r.FieldCount;column++){try{values[column]=r.GetValue(column);}catch(OverflowException){values[column]=r.GetSqlDecimal(column);}}var types=Enumerable.Range(0,r.FieldCount).Select(r.GetDataTypeName).ToArray();
       if(compareInfos[sets-1].Length!=r.FieldCount)throw new InvalidOperationException("Collation 欄位契約不同。");var encoded=ResultFingerprint.Encode(values,types,compareInfos[sets-1],compareOptions[sets-1]);limits.AddResult(1,encoded.Length);fingerprint.Add(encoded);
      }
      fingerprints.Add(fingerprint.Finish());}
     }while(await r.NextResultAsync(token));if(sets!=statements.Length)throw new InvalidOperationException("未取得完整結果集。");
    }
   }
   if(plans.Count>0){var merged=new XElement(plans[0]);foreach(var additional in plans.Skip(1))foreach(var batch in additional.Descendants(PlanNs+"Batch"))merged.Element(PlanNs+"BatchSequence").Add(new XElement(batch));result.Plan=merged.ToString(SaveOptions.DisableFormatting);}result.ResultContract=ConnectionContext.Hash(string.Join("\n",contracts));
   if(plans.Count==0)throw new InvalidOperationException("缺少 actual plan，不能套用。");
   var evidence=ParsePlan(result.Plan);result.Warnings=evidence.Warnings;XDocument plan=XDocument.Parse(result.Plan);result.Shape=string.Join("|",plan.Descendants(PlanNs+"RelOp").Select(x=>(string)x.Attribute("PhysicalOp")));
   foreach(Match m in Regex.Matches(messages.ToString(),@"logical reads\s+(\d+)",RegexOptions.IgnoreCase))result.Reads+=long.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture);
   foreach(Match m in Regex.Matches(messages.ToString(),@"CPU time\s*=\s*(\d+)\s*ms,\s*elapsed time\s*=\s*(\d+)\s*ms",RegexOptions.IgnoreCase)){result.CpuMs+=long.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture);result.ElapsedMs+=long.Parse(m.Groups[2].Value,CultureInfo.InvariantCulture);}
   if(!Regex.IsMatch(messages.ToString(),@"logical reads\s+\d+",RegexOptions.IgnoreCase))throw new InvalidOperationException("logical reads 不可用。");
   if(!messages.ToString().Contains("CPU time"))throw new InvalidOperationException("沒有完整 CPU/elapsed 統計。");
   result.Digest=ConnectionContext.Hash(string.Join("|",fingerprints));return result;
  }
 }
}
