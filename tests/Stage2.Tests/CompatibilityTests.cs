using System;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alyvo.SsmsAiSqlAssistant;
using Newtonsoft.Json;
using Xunit;

public class CompatibilityTests
{
 [Theory][InlineData(1,"É","é",true)][InlineData(1,"É","e",false)][InlineData(2,"É","e",true)][InlineData(3,"É","e",false)][InlineData(1,"カ","ｶ",true)][InlineData(2,"カ","か",true)]
 public async Task ActualSql2025CollationMatchesFingerprint(int database,string originalText,string candidateText,bool expected)
 {
  var context=new ConnectionContext{Server="localhost",Database="AlyvoStage5Demo"+database,TrustServerCertificate=true};var worker=new DbWorker();
  var a="SELECT COUNT_BIG(*) AS n,N'"+originalText+"' AS marker FROM dbo.Orders WHERE DATEDIFF(day,CONVERT(date,'2025-01-01'),OrderDate)=0;";
  var b="SELECT COUNT_BIG(*) AS n,N'"+candidateText+"' AS marker FROM dbo.Orders WHERE OrderDate>='20250101' AND OrderDate<'20250102';";
  var preflight=await worker.Preflight(context,a,b,CancellationToken.None);Assert.True(preflight.Passed,string.Join(";",preflight.Gates.Select(g=>g.Reason)));
  var run=await worker.DryRun(context,a,b,preflight,CancellationToken.None);Assert.Equal(expected,run.Passed);Assert.Null(run.CleanupWarning);
  if(!expected)Assert.Contains("結果",run.Reason);
  using(var c=context.Connect()){await c.OpenAsync();using(var cmd=new SqlCommand("SELECT CONVERT(nvarchar(128),SERVERPROPERTY('Edition')),CONVERT(nvarchar(30),SERVERPROPERTY('ProductVersion')),compatibility_level,collation_name FROM sys.databases WHERE database_id=DB_ID();",c))using(var r=await cmd.ExecuteReaderAsync()){Assert.True(await r.ReadAsync());File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"compatibility-"+database+"-"+ConnectionContext.Hash(originalText+candidateText).Substring(0,8)+".json"),JsonConvert.SerializeObject(new{edition=r.GetString(0),version=r.GetString(1),compatibility=r.GetByte(2),collation=r.GetString(3),expected,actual=run.Passed,snapshot=run.Snapshot,originalReads=run.Original?.Select(x=>x.Reads),candidateReads=run.Candidate?.Select(x=>x.Reads)},Formatting.Indented));}}
 }
 [Theory][InlineData(1)][InlineData(2)][InlineData(3)]
 public async Task DemoHasFiveRealCandidatesTwentyMapNodesAndStandalone(int database)
 {
  var context=new ConnectionContext{Server="localhost",Database="AlyvoStage5Demo"+database,TrustServerCertificate=true};var result=await new SpScanCollector().Load(context,CancellationToken.None);
  Assert.Equal(28,result.Rows.Length);Assert.Equal(5,result.Rows.Count(x=>x.Eligible));Assert.Equal(20,result.Rows.Count(x=>x.Module.Name.StartsWith("Map")));Assert.Contains(result.Rows,x=>x.Module.Name=="Map20");
 }
}
