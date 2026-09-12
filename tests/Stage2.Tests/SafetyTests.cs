using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alyvo.SsmsAiSqlAssistant;
using Newtonsoft.Json.Linq;
using Xunit;
public class SafetyTests
{
 [Theory]
 [InlineData("SELECT 1 AS n",true)]
 [InlineData("SELECT x FROM dbo.t",true)]
 [InlineData("DELETE dbo.t",false)]
 [InlineData("SELECT * INTO dbo.x FROM dbo.t",false)]
 [InlineData("EXEC dbo.p",false)]
 [InlineData("SELECT 1;SELECT 2",false)]
 [InlineData("SELECT * FROM other.dbo.t",false)]
 [InlineData("SELECT * FROM dbo.t WITH(UPDLOCK)",false)]
 [InlineData("SELECT dbo.f(1)",false)]
 [InlineData("SELECT * FROM OPENQUERY(x,'select 1')",false)]
 [InlineData("SELECT @x=1",false)]
 [InlineData("SELECT NEXT VALUE FOR dbo.s AS n",false)]
 [InlineData("",false)]
 public void ParserEnforcesReadonly(string sql,bool allowed)=>Assert.Equal(allowed,!SqlSafety.Parse(sql).Errors.Any());
 [Fact]public void FormattingIsNotChange()=>Assert.Equal(SqlSafety.Canonical("select 1 as n"),SqlSafety.Canonical("SELECT 1 AS n;"));
 [Fact]public void MissingApiFieldsRejected()=>Assert.ThrowsAny<Exception>(()=>LocalApi.Decode(JObject.Parse("{\"aiProvider\":\"openai\"}")));
 [Fact]public void NoFakeLocalCandidates()=>Assert.ThrowsAny<Exception>(()=>LocalApi.Decode(JObject.Parse("{\"id\":\"x\",\"aiProvider\":\"local-rules\",\"summary\":\"s\",\"issues\":[],\"candidates\":[{\"sql\":\"select 1 as n\",\"explanation\":\"s\"}],\"warnings\":[],\"usage\":{}}")));
 [Fact]public void AllSixGatesRequired(){Assert.False(new Preflight{Gates=new[]{new Gate("x",true,"")}}.Passed);Assert.False(new AnalysisState().CanApply);}
 [Fact]public async Task SnapshotCleanupRejectsOtherNames()=>await Assert.ThrowsAsync<InvalidOperationException>(()=>DbWorker.Cleanup(new ConnectionContext(),"master",1));
 static RunMetrics R(long reads)=>new RunMetrics{Digest="same",Rows=2,Columns=1,Reads=reads,CpuMs=1,ElapsedMs=1,Warnings=Array.Empty<string>(),Shape="seek",Plan="present"};
 static DryRunResult Evidence()=>new DryRunResult{Original=new[]{R(100),R(100),R(100)},Candidate=new[]{R(10),R(10),R(10)}};
 [Fact]public void CompleteEvidencePasses(){var r=Evidence();DbWorker.Evaluate(r);Assert.True(r.Passed);}
 [Theory][InlineData("result")][InlineData("reads")][InlineData("warning")][InlineData("unstable")]
 public void RegressionBlocksApply(string reason){var r=Evidence();foreach(var c in r.Candidate){if(reason=="result")c.Digest="different";if(reason=="reads")c.Reads=100;if(reason=="warning")c.Warnings=new[]{"Spill"};if(reason=="cpu")c.CpuMs=50;if(reason=="elapsed")c.ElapsedMs=100;}if(reason=="unstable")r.Candidate[0].Shape="scan";DbWorker.Evaluate(r);Assert.False(r.Passed);}
 [Fact]public void CpuAndElapsedAreDisplayOnly(){var r=Evidence();foreach(var c in r.Candidate){c.CpuMs=500;c.ElapsedMs=10000;}DbWorker.Evaluate(r);Assert.True(r.Passed);}
 [Fact]public void StaleSelectionAndDatabaseRejected(){var a=new AnalysisState{Version=7,Start=3,Length=3,OriginalHash=ConnectionContext.Hash("sql"),ConnectionFingerprint="db1"};Assert.True(a.MatchesSelection(7,3,3,"sql","db1"));Assert.False(a.MatchesSelection(8,3,3,"sql","db1"));Assert.False(a.MatchesSelection(7,4,3,"sql","db1"));Assert.False(a.MatchesSelection(7,3,2,"sql","db1"));Assert.False(a.MatchesSelection(7,3,3,"SQL","db1"));Assert.False(a.MatchesSelection(7,3,3,"sql","db2"));}
 sealed class Canceller:IProgress<string>{readonly CancellationTokenSource source;public Canceller(CancellationTokenSource s){source=s;}public void Report(string text){if(text.StartsWith("Dry Run "))source.Cancel();}}
 [Fact]public async Task SnapshotCancellationCleansUp(){var c=new ConnectionContext{Server="localhost",Database="AlyvoSsmsAiDemo",TrustServerCertificate=true};var worker=new DbWorker();var p=await worker.Preflight(c,"SELECT COUNT_BIG(*) AS n FROM dbo.AI_Test_OrdersForOptimization WHERE YEAR(CreatedAt)=2025;","SELECT COUNT_BIG(*) AS n FROM dbo.AI_Test_OrdersForOptimization WHERE CreatedAt>='20250101' AND CreatedAt<'20260101';",CancellationToken.None);using(var cancel=new CancellationTokenSource()){var result=await worker.DryRun(c,"SELECT 1 AS n","SELECT 2 AS n",p,cancel.Token,new Canceller(cancel));Assert.Equal("Dry Run 已取消",result.Status);Assert.False(result.Passed);Assert.Null(result.CleanupWarning);using(var connection=c.Connect("master")){await connection.OpenAsync();using(var cmd=connection.CreateCommand()){cmd.CommandText="SELECT COUNT(*) FROM sys.databases WHERE name=@n";cmd.Parameters.AddWithValue("@n",result.Snapshot);Assert.Equal(0,Convert.ToInt32(await cmd.ExecuteScalarAsync()));}}}} [Fact]public async Task RealSql2025Snapshot()
 {
  var context=new ConnectionContext{Server="localhost",Database="AlyvoSsmsAiDemo",TrustServerCertificate=true};
  var worker=new DbWorker();var original="SELECT COUNT_BIG(*) AS n FROM dbo.AI_Test_OrdersForOptimization WHERE YEAR(CreatedAt)=2025;";
  var candidate="SELECT COUNT_BIG(*) AS n FROM dbo.AI_Test_OrdersForOptimization WHERE CreatedAt >= '20250101' AND CreatedAt < '20260101';";
  Assert.True(await DbWorker.VerifyEnvironment(context,CancellationToken.None)>0);
  var payload=await worker.BuildPayload(original,context,CancellationToken.None);Assert.NotNull(payload);
  var p=await worker.Preflight(context,original,candidate,CancellationToken.None);
  Assert.True(p.Passed,string.Join(";",p.Gates.Select(g=>g.Name+":"+g.Reason)));
  var result=await worker.DryRun(context,original,candidate,p,CancellationToken.None);
  System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"snapshot-evidence.json"),Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));
  Assert.True(result.Passed,result.Reason+result.CleanupWarning);
  using(var c=context.Connect("master")){await c.OpenAsync();using(var cmd=c.CreateCommand()){cmd.CommandText="SELECT COUNT(*) FROM sys.databases WHERE name=@n";cmd.Parameters.AddWithValue("@n",result.Snapshot);Assert.Equal(0,Convert.ToInt32(await cmd.ExecuteScalarAsync()));}}
 }
}



