using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alyvo.SsmsAiSqlAssistant;
using Newtonsoft.Json;
using Xunit;
public class VideoRewriteTests
{
 [Fact] public async Task ExistingDateIndexSupportsRewriteWithoutDdl()
 {
  var context=new ConnectionContext{Server="localhost",Database="AlyvoFullTest20260912",TrustServerCertificate=true};
  var original="SELECT Status, COUNT_BIG(*) AS OrderCount, SUM(Amount) AS TotalAmount FROM dbo.Orders WHERE DATEDIFF(day, CONVERT(date, '2025-01-01'), OrderDate) = 0 GROUP BY Status;";
  var candidate="SELECT Status, COUNT_BIG(*) AS OrderCount, SUM(Amount) AS TotalAmount FROM dbo.Orders WHERE OrderDate >= CONVERT(date, '2025-01-01') AND OrderDate < CONVERT(date, '2025-01-02') GROUP BY Status;";
  var worker=new DbWorker();var pre=await worker.Preflight(context,original,candidate,CancellationToken.None);
  Assert.True(pre.Passed,string.Join(";",pre.Gates.Select(x=>x.Reason)));
  var run=await worker.DryRun(context,original,candidate,pre,CancellationToken.None);
  File.WriteAllText(@"C:\Users\User\Documents\Codex\2026-09-12\new-chat\outputs\FullTest\rewrite-validation.json",JsonConvert.SerializeObject(new{run.Passed,run.Reason,run.CleanupWarning,Before=run.Original.Select(x=>new{x.Reads,x.Rows,x.Digest}),After=run.Candidate.Select(x=>new{x.Reads,x.Rows,x.Digest})},Formatting.Indented));
  Assert.True(run.Passed,run.Reason+run.CleanupWarning);
 }
}
