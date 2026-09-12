using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alyvo.SsmsAiSqlAssistant;
using Newtonsoft.Json.Linq;
using Xunit;

public class Stage5IntegrationTests
{
 [Fact] public void ScheduleNeedsExactDatabaseEnabledAndDueTime()
 {
  var time=DateTimeOffset.UtcNow;var schedule=JObject.FromObject(new{data=new{databaseFingerprint="db",enabled=true,intervalMinutes=5}});
  Assert.True(ConnectedGovernance.Due(schedule,"db",time,null));Assert.False(ConnectedGovernance.Due(schedule,"other",time,null));Assert.False(ConnectedGovernance.Due(schedule,"db",time,time.AddMinutes(1)));schedule["data"]["enabled"]=false;Assert.False(ConnectedGovernance.Due(schedule,"db",time,null));
 }
 [Fact] public async Task MultipleResultSetsUseActualSql2025Evidence()
 {
  var c=new ConnectionContext{Server="localhost",Database="AlyvoSsmsAiDemo",TrustServerCertificate=true};var w=new DbWorker();
  var a="SELECT COUNT_BIG(*) AS n FROM dbo.AI_Test_OrdersForOptimization WHERE YEAR(CreatedAt)=2025;";
  var b="SELECT COUNT_BIG(*) AS n FROM dbo.AI_Test_OrdersForOptimization WHERE CreatedAt>='20250101' AND CreatedAt<'20260101';";
  var p=await w.Preflight(c,a+a,b+b,CancellationToken.None);Assert.True(p.Passed,string.Join(";",p.Gates.Select(g=>g.Reason)));var r=await w.DryRun(c,a+a,b+b,p,CancellationToken.None);Assert.True(r.Passed,r.Reason+r.CleanupWarning);Assert.All(r.Original,x=>{Assert.Equal(2,x.Rows);Assert.Equal(2,x.Columns);Assert.NotNull(x.ResultContract);});Assert.NotNull(r.Original[0].SavedPlan);Assert.StartsWith("AlyvoVerify_",r.Snapshot);
 }
 [Fact] public void MultipleResultSafetyDoesNotPermitWrites()
 {
  Assert.Equal(2,SqlSafety.ReadonlyStatements("SELECT 1 AS a; SELECT 2 AS b;").Length);Assert.Throws<InvalidOperationException>(()=>SqlSafety.ReadonlyStatements("SELECT 1; DELETE dbo.t;"));Assert.Throws<InvalidOperationException>(()=>SqlSafety.ReadonlyStatements("SELECT 1; EXEC dbo.p;"));
 }
 [Fact] public async Task ExactQueryStoreUsesOneOriginalAndThreeCandidates()
 {
  var context=new ConnectionContext{Server="localhost",Database="AlyvoStage5Demo1",TrustServerCertificate=true};var worker=new DbWorker();
  var a="SELECT COUNT_BIG(*) AS baseline_stage5 FROM dbo.Orders WHERE DATEDIFF(day,CONVERT(date,'2025-01-03'),OrderDate)=0;";
  var b="SELECT COUNT_BIG(*) AS baseline_stage5 FROM dbo.Orders WHERE OrderDate>='20250103' AND OrderDate<'20250104';";
  using(var c=context.Connect()){await c.OpenAsync();using(var cmd=c.CreateCommand()){cmd.CommandText=a;await cmd.ExecuteScalarAsync();cmd.CommandText="EXEC sys.sp_query_store_flush_db;";await cmd.ExecuteNonQueryAsync();}}
  var preflight=await worker.Preflight(context,a,b,CancellationToken.None);Assert.True(preflight.Passed);var run=await worker.DryRun(context,a,b,preflight,CancellationToken.None);Assert.True(run.Passed,run.Reason);Assert.True(run.ExactQueryStoreBaseline,run.QueryStore);Assert.Single(run.Original);Assert.Equal(3,run.Candidate.Length);
 }
}
