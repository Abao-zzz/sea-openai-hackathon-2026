using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Alyvo.SsmsAiSqlAssistant
{
 // Lifetime belongs to the visible, connected SSMS scan workspace.
 public sealed class ConnectedGovernance:IDisposable
 {
  readonly LocalApi api=new LocalApi();readonly CancellationTokenSource stop=new CancellationTokenSource();readonly ConnectionContext context;readonly Dictionary<string,DateTimeOffset> next=new Dictionary<string,DateTimeOffset>();readonly Dictionary<int,double[]> previous=new Dictionary<int,double[]>();
  readonly Func<bool> connected;Task loop;public string Status="尚未讀取排程";
  public ConnectedGovernance(ConnectionContext context,Func<bool> connected){this.context=context;this.connected=connected??throw new ArgumentNullException(nameof(connected));}
  public void Start(){if(loop==null)loop=Loop();}
  async Task Loop()
  {
   while(!stop.IsCancellationRequested)
   {
    try{if(connected())await Tick(DateTimeOffset.UtcNow,stop.Token);else Status="等待同資料庫的已連線 SQL Editor。";}catch(OperationCanceledException){return;}catch(Exception e){Status=DbWorker.SafeError(e);Diagnostics.Write("Connected read-only schedule failed");}
    try{await Task.Delay(TimeSpan.FromMinutes(1),stop.Token);}catch(OperationCanceledException){return;}
   }
  }
  public static bool Due(JToken schedule,string fingerprint,DateTimeOffset now,DateTimeOffset? nextRun)
  {
   if(schedule?["data"]?["databaseFingerprint"]?.Value<string>()!=fingerprint||schedule["data"]["enabled"]?.Value<bool>()!=true)return false;
   var interval=schedule["data"]["intervalMinutes"]?.Value<int>();return interval>=5&&interval<=10080&&(!nextRun.HasValue||now>=nextRun);
  }
  public async Task Tick(DateTimeOffset now,CancellationToken token)
  {
   for(int offset=0;offset<10000;offset+=100)
   {
    var schedules=(JArray)await api.Send("/agent/schedules?offset="+offset+"&limit=100",null,token);
    foreach(var schedule in schedules)
    {
     var id=schedule["id"].Value<string>();if(!Due(schedule,context.Fingerprint,now,next.TryGetValue(id,out var due)?due:(DateTimeOffset?)null))continue;
     next[id]=now.AddMinutes(schedule["data"]["intervalMinutes"].Value<int>());
     if(!connected())return;await ReadSummary(schedule,token);
    }
    if(schedules.Count<100)break;
   }
  }
  async Task ReadSummary(JToken schedule,CancellationToken token)
  {
   var watch=Stopwatch.StartNew();var rows=new List<Tuple<int,double,double,double>>();await DbWorker.VerifyEnvironment(context,token);
   using(var c=context.Connect()){await c.OpenAsync(token);using(var cmd=new SqlCommand(SpScanCollector.DmvSql,c){CommandTimeout=10})using(token.Register(()=>cmd.Cancel()))using(var r=await cmd.ExecuteReaderAsync(token))while(await r.ReadAsync(token)){var count=Convert.ToDouble(r.GetValue(1));if(count>0)rows.Add(Tuple.Create(Convert.ToInt32(r.GetValue(0)),Convert.ToDouble(r.GetValue(2))/count,Convert.ToDouble(r.GetValue(3))/count,Convert.ToDouble(r.GetValue(4))/count));}}
   var selected=rows.OrderByDescending(r=>r.Item2).Take(schedule["data"]["topN"].Value<int>()).ToArray();
   foreach(var row in selected)
   {
    var current=new[]{row.Item2,row.Item3,row.Item4};if(previous.TryGetValue(row.Item1,out var before))for(int i=0;i<3;i++)if(before[i]>0&&current[i]>before[i]*1.2)await api.Send("/agent/monitor/events",new{databaseFingerprint=context.Fingerprint,kind=new[]{"io-regression","cpu-regression","duration-regression"}[i],value=current[i]/before[i]-1},token);
    previous[row.Item1]=current;
   }
   if(selected.Length==0){Status="唯讀排程完成；沒有 DMV 成本證據，不建立假工作項目。";return;}
   var job=SpBatch.Decode(await api.Send("/agent/jobs",new{databaseFingerprint=context.Fingerprint,topN=selected.Length,objectIds=selected.Select(r=>r.Item1).ToArray(),budget=new{timeSeconds=300,cpuMs=60000,logicalReads=1000000,tokens=1},maxRetries=0},token));
   var id=job["id"].Value<string>();job=SpBatch.Decode(await api.Send("/agent/jobs/"+id+"/lease",new{workerId="readonly_"+Guid.NewGuid().ToString("N"),databaseFingerprint=context.Fingerprint,leaseSeconds=60},token));var lease=job["leaseToken"].Value<string>();var command=new{leaseToken=lease,databaseFingerprint=context.Fingerprint};
   try
   {
    await api.Send("/agent/jobs/"+id+"/start",command,token);
    foreach(var row in selected)await api.Send("/agent/jobs/"+id+"/items/"+row.Item1,new{leaseToken=lease,databaseFingerprint=context.Fingerprint,sequence=1,status="skipped",elapsedMs=watch.ElapsedMilliseconds,cpuMs=0,logicalReads=0,tokens=0},token);
    await api.Send("/agent/jobs/"+id+"/complete",command,token);await api.Send("/agent/schedules/"+schedule["id"].Value<string>()+"/runs",new{jobId=id},token);
    Status="唯讀摘要已更新；"+selected.Length+" 支 SP 未送 AI、未執行驗證。";
   }
   finally{try{await api.Send("/agent/jobs/"+id+"/lease/release",command,CancellationToken.None);}catch{Diagnostics.Write("Read-only schedule lease release failed; expiry required");}}
  }
  public void Dispose(){stop.Cancel();}
 }
}
