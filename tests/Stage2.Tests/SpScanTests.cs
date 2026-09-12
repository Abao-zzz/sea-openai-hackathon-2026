using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Alyvo.SsmsAiSqlAssistant;
using Newtonsoft.Json.Linq;
using Xunit;

public class SpScanTests
{
 [Fact]public void JobDatesAcceptParsedJsonDatesAndIdentifyResumableJobs()
 {
  var future=DateTimeOffset.UtcNow.AddMinutes(2);var job=JObject.Parse("{\"status\":\"running\",\"createdAt\":\"2026-09-12T04:00:00Z\",\"leaseExpires\":\""+future.ToString("O")+"\"}");
  Assert.Equal(4,SpBatch.Timestamp(job["createdAt"]).Value.UtcDateTime.Hour);Assert.Equal("執行中",SpBatch.State(job));job["leaseExpires"]=JValue.CreateNull();Assert.Equal("可續跑",SpBatch.State(job));job["leaseExpires"]=new JValue(DateTime.UtcNow.AddMinutes(-1));Assert.Equal("可續跑",SpBatch.State(job));Assert.Equal(future,SpBatch.Timestamp(new JValue(future)));Assert.ThrowsAny<Exception>(()=>SpBatch.Timestamp(new JValue("invalid")));
 }
 static ConnectionContext Context()=>new ConnectionContext{Server="localhost",Database="AlyvoStage5SpScanAcceptance",TrustServerCertificate=true};
 [Theory]
 [InlineData("CREATE PROC dbo.p AS UPDATE dbo.t SET a=1;")]
 [InlineData("CREATE PROC dbo.p AS EXEC dbo.q;")]
 [InlineData("CREATE PROC dbo.p @x int OUTPUT AS SELECT @x AS x;")]
 [InlineData("CREATE PROC dbo.p @t dbo.TableType READONLY AS SELECT * FROM @t;")]
 [InlineData("CREATE PROC dbo.p AS SELECT 1 AS n; SELECT 2 AS n;")]
 [InlineData("CREATE PROC dbo.p AS SELECT NEXT VALUE FOR dbo.seq AS n;")]
 [InlineData("CREATE PROC dbo.p AS SELECT 1 AS n INTO dbo.newtable;")]
 public void UnsafeBodiesAreSkipped(string sql)=>Assert.ThrowsAny<Exception>(()=>SpBody.Parse(sql));
 [Theory][InlineData("1; DROP TABLE dbo.t;--")][InlineData("GETDATE()")][InlineData("1 FROM dbo.t")][InlineData("@other")][InlineData("1 UNION SELECT 2")]
 public void ParameterFragmentsCannotInjectSql(string sql)=>Assert.ThrowsAny<Exception>(()=>SpBody.Literal(sql));
 [Fact]public void BindUsesAstVariablesOnlyAndFullCandidatePreservesProcedure()
 {
  var body=SpBody.Parse("CREATE PROC dbo.p @Id int=7 AS BEGIN SET NOCOUNT ON; SELECT @Id AS n, '@Id' AS literal; END;");
  var item=new WorkloadCase();item.Values["@Id"]="(8)";var bound=body.Bind(body.SelectSql,item);Assert.Contains("CONVERT(int, (8))",bound);Assert.Contains("'@Id'",bound);var candidate=body.Candidate("SELECT @Id + 0 AS n, '@Id' AS literal;","AlyvoSpScanAcceptance");Assert.Contains("CREATE OR ALTER PROCEDURE dbo.p",candidate);Assert.Contains("@Id int=7",candidate);Assert.Contains("SET NOCOUNT ON",candidate);Assert.ThrowsAny<Exception>(()=>body.Bind("SELECT @unknown AS n;",item));
 }
 [Fact]public void EvidenceNeverMixesParameterListsAndLimitsCases()
 {
  var body=SpBody.Parse("CREATE PROC dbo.p @a int,@b int AS SELECT @a+@b AS n;");var time=DateTimeOffset.UtcNow;
  var incomplete="<ShowPlanXML xmlns='http://schemas.microsoft.com/sqlserver/2004/07/showplan'><ParameterList><ColumnReference Column='@a' ParameterCompiledValue='(1)'/></ParameterList><ParameterList><ColumnReference Column='@b' ParameterCompiledValue='(2)'/></ParameterList></ShowPlanXML>";
  Assert.Empty(SpReviewWorker.ExtractCases(body,new[]{Tuple.Create(incomplete,"cache",time)},time));
  var plans=Enumerable.Range(1,8).Select(n=>Tuple.Create("<ShowPlanXML xmlns='http://schemas.microsoft.com/sqlserver/2004/07/showplan'><ParameterList><ColumnReference Column='@a' ParameterCompiledValue='("+n+")'/><ColumnReference Column='@b' ParameterCompiledValue='(2)'/></ParameterList></ShowPlanXML>","cache",time)).ToArray();var cases=SpReviewWorker.ExtractCases(body,plans,time);Assert.Equal(4,cases.Length);Assert.All(cases,c=>{Assert.Equal(time,c.ObservedAt);Assert.Equal(2,c.Values.Count);});
 }
 [Fact]public void AllWorkloadsMustPassBeforeAcceptance()
 {
  var review=new SpReview{Cases=new[]{new WorkloadCase(),new WorkloadCase()}};var gates=Enumerable.Range(0,6).Select(i=>new Gate(i.ToString(),true,"test")).ToArray();review.Preflights.Add(new Preflight{Gates=gates});Assert.False(review.PreflightPassed);review.Preflights.Add(new Preflight{Gates=gates});Assert.True(review.PreflightPassed);review.Runs.Add(new DryRunResult{Passed=true});review.Runs.Add(new DryRunResult{Passed=false});Assert.False(review.Passed);review.Runs[1].Passed=true;Assert.True(review.Passed);review.Error="drift";Assert.False(review.Passed);
 }
 [Fact]public async Task RealInitialScanHasEightRowsTwoCandidatesAndNoAiOrExec()
 {
  var context=Context();var api=new LocalApi();var before=await api.Send("/usage",null,CancellationToken.None);async Task<string> Counts(){using(var c=context.Connect()){await c.OpenAsync();using(var cmd=c.CreateCommand()){cmd.CommandText="SELECT object_id,SUM(execution_count) FROM sys.dm_exec_procedure_stats WHERE database_id=DB_ID() GROUP BY object_id ORDER BY object_id;";using(var reader=await cmd.ExecuteReaderAsync()){var rows=new List<string>();while(await reader.ReadAsync())rows.Add(reader.GetValue(0)+":"+reader.GetValue(1));return string.Join(",",rows);}}}}
  var counts=await Counts();var scan=await new SpScanCollector().Load(context,CancellationToken.None);Assert.Equal(8,scan.Rows.Length);Assert.Equal(2,scan.Rows.Count(r=>r.Eligible));Assert.Equal(6,scan.Rows.Count(r=>!r.Eligible));Assert.All(scan.Rows.Where(r=>!r.Eligible),r=>Assert.NotEmpty(r.Reason));Assert.Contains(scan.Rows,r=>r.Reason.Contains("encrypted"));Assert.Contains(scan.Rows,r=>r.Reason.Contains("不安全參數"));Assert.Contains(scan.Rows,r=>r.Reason.Contains("沒有成本"));Assert.Equal(counts,await Counts());Assert.True(JToken.DeepEquals(before,await api.Send("/usage",null,CancellationToken.None)));File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"scan-evidence.json"),JObject.FromObject(new{scan.ObservedAt,total=scan.Rows.Length,candidates=scan.Rows.Count(x=>x.Eligible),skipped=scan.Rows.Count(x=>!x.Eligible),rows=scan.Rows.Select(x=>new{x.Module.Id,x.Module.FullName,x.Reason,x.Source,x.Reads}),noExec=true,noAi=true}).ToString());
 }
 [Fact]public async Task RealSpBodyPreflightAndMultipleWorkloadsSnapshot()
 {
  var context=Context();var row=(await new SpScanCollector().Load(context,CancellationToken.None)).Rows.Single(r=>r.Module.Name=="CostlyDailyOrders");
  var candidate="SELECT CustomerId,SUM(Amount) AS TotalAmount FROM dbo.ScanOrders WHERE OrderDate>=@Day AND OrderDate<DATEADD(day,1,@Day) GROUP BY CustomerId;";
  var review=new SpReview{CandidateSql=row.Body.Candidate(candidate,context.Database),OriginalHash=ProcedureSource.Parse(row.Module.Definition,context.Database).Hash,Cases=new[]{new WorkloadCase{Source="test-case-explicit",Values=new Dictionary<string,string>{{"@Day","'2025-01-01'"}}},new WorkloadCase{Source="test-case-explicit",Values=new Dictionary<string,string>{{"@Day","'2025-01-02'"}}}}};row.Review=review;
  foreach(var item in review.Cases)review.Preflights.Add(await new DbWorker().Preflight(context,row.Body.Bind(row.Body.SelectSql,item),row.Body.Bind(candidate,item),CancellationToken.None));Assert.True(review.PreflightPassed,string.Join(";",review.Preflights.SelectMany(p=>p.Gates).Where(g=>!g.Passed).Select(g=>g.Reason)));
  await new SpReviewWorker().Verify(context,row,CancellationToken.None,null);File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"sp-workload-evidence.json"),Newtonsoft.Json.JsonConvert.SerializeObject(review,Newtonsoft.Json.Formatting.Indented));Assert.True(review.Passed,string.Join(";",review.Runs.Select(r=>r.Reason)));await SpReviewWorker.AssertUnchanged(context,row,review.OriginalHash,CancellationToken.None);
 }
 [Fact]public void UnknownJobStateFailsClosed()=>Assert.ThrowsAny<Exception>(()=>SpBatch.Decode(JObject.Parse("{\"id\":\"a\",\"databaseFingerprint\":\"a\",\"items\":[],\"budget\":{},\"status\":\"future-success\"}")));
 [Fact]public async Task RealSnapshotBudgetStopsNextSampleAndCleansUp()
 {
  var context=Context();var db=new DbWorker();const string original="SELECT CustomerId,SUM(Amount) AS TotalAmount FROM dbo.ScanOrders WHERE DATEDIFF(day,'2025-01-01',OrderDate)=0 GROUP BY CustomerId;";const string candidate="SELECT CustomerId,SUM(Amount) AS TotalAmount FROM dbo.ScanOrders WHERE OrderDate>='2025-01-01' AND OrderDate<'2025-01-02' GROUP BY CustomerId;";
  var preflight=await db.Preflight(context,original,candidate,CancellationToken.None);Assert.True(preflight.Passed);int samples=0;
  var run=await db.DryRun(context,original,candidate,preflight,CancellationToken.None,null,sample=>{samples++;Assert.True(sample.Reads>0);throw new InvalidOperationException("test IO budget exhausted");});
  Assert.Equal(1,samples);Assert.False(run.Passed);Assert.Contains("budget exhausted",run.Reason);Assert.True(string.IsNullOrEmpty(run.CleanupWarning));
  using(var c=context.Connect("master")){await c.OpenAsync();using(var cmd=c.CreateCommand()){cmd.CommandText="SELECT COUNT(*) FROM sys.databases WHERE name=@name;";cmd.Parameters.AddWithValue("@name",run.Snapshot);Assert.Equal(0,Convert.ToInt32(await cmd.ExecuteScalarAsync()));}}
 }
 [Fact]public async Task RealJobLeaseCheckpointCancelRetryBudgetAndResume()
 {
  var api=new LocalApi();var c=Context();var token=CancellationToken.None;async Task<JToken> Send(string path,object body=null)=>await api.Send(path,body,token);
  var j=await Send("/agent/jobs",new{databaseFingerprint=c.Fingerprint,topN=5,objectIds=new[]{1,2,3,4,5},budget=new{timeSeconds=600,cpuMs=1000,logicalReads=1000,tokens=1000},maxRetries=1});var path="/agent/jobs/"+j["id"];j=await Send(path+"/lease",new{workerId="stage4_test",databaseFingerprint=c.Fingerprint,leaseSeconds=5});var lease=j["leaseToken"].Value<string>();var command=new{leaseToken=lease,databaseFingerprint=c.Fingerprint};await Send(path+"/start",command);
  await Send(path+"/items/1",new{leaseToken=lease,databaseFingerprint=c.Fingerprint,sequence=1,status="failed",elapsedMs=1,cpuMs=0,logicalReads=0,tokens=0});await Send(path+"/lease/release",command);j=await Send(path+"/retry",new{});Assert.Equal("pending",j["items"][0]["status"].Value<string>());j=await Send(path+"/lease",new{workerId="stage4_resume",databaseFingerprint=c.Fingerprint,leaseSeconds=5});Assert.Equal(1,j["items"][0]["attempts"].Value<int>());lease=j["leaseToken"].Value<string>();await Send(path+"/start",new{leaseToken=lease,databaseFingerprint=c.Fingerprint});
  j=await Send(path+"/items/1",new{leaseToken=lease,databaseFingerprint=c.Fingerprint,sequence=2,status="failed",elapsedMs=2,cpuMs=1001,logicalReads=0,tokens=0});Assert.Equal("budget-exhausted",j["status"].Value<string>());await Send(path+"/lease/release",new{leaseToken=lease,databaseFingerprint=c.Fingerprint});await Assert.ThrowsAnyAsync<Exception>(()=>Send(path+"/retry",new{}));var replay=await Send(path+"/demo-replay",new{});Assert.True(replay["demo"].Value<bool>());Assert.Equal(5,replay["items"].Count());await Send("/agent/jobs/"+replay["id"]+"/cancel",new{});Assert.True((await Send("/agent/jobs/"+replay["id"]+"/cancel-requested"))["cancelRequested"].Value<bool>());
 }
}
