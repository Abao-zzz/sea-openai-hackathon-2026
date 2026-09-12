using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Alyvo.SsmsAiSqlAssistant
{
 public sealed class SpBudget
 {
  public long TimeSeconds=1800,CpuMs=300000,LogicalReads=5000000,Tokens=30000;
  public object Payload()=>new{timeSeconds=TimeSeconds,cpuMs=CpuMs,logicalReads=LogicalReads,tokens=Tokens};
  public void Validate(){if(TimeSeconds<1||TimeSeconds>86400||CpuMs<1||CpuMs>86400000||LogicalReads<1||Tokens<1||Tokens>10000000)throw new InvalidOperationException("批次預算超出允許範圍。");}
 }
 public sealed class SpBatch:IDisposable
 {
  readonly LocalApi api=new LocalApi();readonly SpReviewWorker worker=new SpReviewWorker();readonly string workerId="ssms_"+Guid.NewGuid().ToString("N");
  CancellationTokenSource cancellation;Task heartbeat;bool closing;public JObject Job;public string Progress="";public bool Busy;public event Action Changed;public ConnectionContext Context;
  public string JobId=>Job?["id"]?.Value<string>();string Lease=>Job?["leaseToken"]?.Value<string>();
  void Signal(string text=null){if(text!=null)Progress=text;Changed?.Invoke();}
  object Command()=>new{leaseToken=Lease,databaseFingerprint=Context.Fingerprint};
  public static JObject Decode(JToken value)
  {
   var job=value as JObject;if(job==null||job["id"]?.Type!=JTokenType.String||job["databaseFingerprint"]?.Type!=JTokenType.String||!(job["items"] is JArray)||job["budget"]==null)throw new InvalidOperationException("工作回應缺少必要欄位。");
   if(!new[]{"pending","running","completed","cancelled","budget-exhausted"}.Contains(job["status"]?.Value<string>()))throw new InvalidOperationException("unsupported：未知工作狀態。");
   foreach(var item in job["items"])if(item["objectId"]?.Type!=JTokenType.Integer||!new[]{"pending","running","completed","failed","skipped"}.Contains(item["status"]?.Value<string>()))throw new InvalidOperationException("unsupported：未知工作項目。");return job;
  }
  public async Task<JArray> List(ConnectionContext context,CancellationToken token)
  {
   var all=new JArray();for(int offset=0;offset<10000;offset+=100){var page=(JArray)await api.Send("/agent/jobs?offset="+offset+"&limit=100",null,token);foreach(var job in page){Decode(job);if(job["databaseFingerprint"].Value<string>()==context.Fingerprint)all.Add(job);}if(page.Count<100)break;}return all;
  }
  public static DateTimeOffset? Timestamp(JToken value)
  {
   if(value==null||value.Type==JTokenType.Null)return null;
   var raw=(value as JValue)?.Value;
   if(raw is DateTimeOffset offset)return offset;
   if(raw is DateTime date)return new DateTimeOffset(date.Kind==DateTimeKind.Unspecified?DateTime.SpecifyKind(date,DateTimeKind.Utc):date);
   if(DateTimeOffset.TryParse(value.Value<string>(),System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind,out var parsed))return parsed;
   throw new InvalidOperationException("工作時間格式不符合契約。");
  }
  public static string State(JToken job)
  {
   var state=job["status"]?.Value<string>();var expires=Timestamp(job["leaseExpires"]);if((state=="running"||state=="pending")&&(!expires.HasValue||expires<=DateTimeOffset.UtcNow))return "可續跑";
   switch(state){case "pending":return "等待";case "running":return "執行中";case "completed":return "完成";case "cancelled":return "已取消";case "budget-exhausted":return "預算用盡";default:return "unsupported";}
  }
  public async Task Run(SpScanResult scan,int top,SpBudget budget,string existing=null,string action=null)
  {
   if(Busy)throw new InvalidOperationException("已有批次執行中。");if(top<1||top>100)throw new InvalidOperationException("Top N 必須為 1–100。");budget.Validate();Context=scan.Context;Busy=true;closing=false;cancellation=new CancellationTokenSource();var token=cancellation.Token;
   try
   {
    await api.Check(token);if(existing==null){var ids=scan.Rows.Where(r=>r.Eligible).Take(top).Select(r=>r.Module.Id).ToArray();if(ids.Length==0)throw new InvalidOperationException("沒有可驗證候選。");Job=Decode(await api.Send("/agent/jobs",new{databaseFingerprint=Context.Fingerprint,topN=top,objectIds=ids,budget=budget.Payload(),maxRetries=1},token));}
    else{Job=Decode(await api.Send("/agent/jobs/"+existing,null,token));if(Job["databaseFingerprint"].Value<string>()!=Context.Fingerprint)throw new InvalidOperationException("工作只能在相同 database 續跑。");if(action!=null){if(!new[]{"retry","replay","demo-replay"}.Contains(action))throw new InvalidOperationException("不支援的工作動作。");Job=Decode(await api.Send("/agent/jobs/"+existing+"/"+action,new{},token));}}
    Job=Decode(await api.Send("/agent/jobs/"+JobId+"/lease",new{workerId,databaseFingerprint=Context.Fingerprint,leaseSeconds=60},token));if(string.IsNullOrEmpty(Lease))throw new InvalidOperationException("沒有租約；可能已用盡預算。");
    Job=Decode(await api.Send("/agent/jobs/"+JobId+"/start",Command(),token));Save();heartbeat=Heartbeat(token);Signal("批次已開始；不自動套用");
    var watch=Stopwatch.StartNew();long limit=Job["budget"]["timeSeconds"].Value<long>();cancellation.CancelAfter(TimeSpan.FromSeconds(Math.Max(1,limit)));
    foreach(var objectId in Job["items"].Select(x=>x["objectId"].Value<int>()).ToArray())
    {
     token.ThrowIfCancellationRequested();Job=Decode(await api.Send("/agent/jobs/"+JobId,null,token));var item=Job["items"].Single(x=>x["objectId"].Value<int>()==objectId);if(new[]{"completed","failed","skipped"}.Contains(item["status"].Value<string>()))continue;
     if(Job["cancelRequested"].Value<bool>()||Job["status"].Value<string>()!="running")throw new OperationCanceledException();
     var row=scan.Rows.SingleOrDefault(r=>r.Module.Id==objectId);if(row==null||!row.Eligible){await Checkpoint(objectId,"skipped",item,0,0,0,token);continue;}
     long started=watch.ElapsedMilliseconds,measuredCpu=0,measuredReads=0;long usedCpu=Job["items"].Sum(x=>x["cpuMs"].Value<long>()),usedReads=Job["items"].Sum(x=>x["logicalReads"].Value<long>());await Checkpoint(objectId,"running",item,0,0,0,token);row.Stage="批次處理中";row.Attempts++;Signal(row.Module.FullName+"：候選預檢");
     void Measure(RunMetrics sample){measuredCpu=checked(measuredCpu+sample.CpuMs);measuredReads=checked(measuredReads+sample.Reads);Signal($"{row.Module.FullName} · 本支 elapsed {(watch.ElapsedMilliseconds-started)/1000.0:N1}s · CPU {measuredCpu} ms · IO {measuredReads}");if(usedCpu+measuredCpu>Job["budget"]["cpuMs"].Value<long>()||usedReads+measuredReads>Job["budget"]["logicalReads"].Value<long>())throw new InvalidOperationException("批次 CPU / IO 預算用盡；停止下一次取樣。");}
     try{var progress=new Progress<string>(s=>{row.Stage=s;Signal(row.Module.FullName+"："+s);});await worker.Prepare(Context,row,token,progress,JobId,Lease);if(row.Review.PreflightPassed)await worker.Verify(Context,row,token,progress,Measure);row.Stage=row.Review.Status;}
     catch(OperationCanceledException){row.Stage=closing?"可續跑":"已取消";throw;}
     catch(Exception e){row.Stage="候選已退回";if(row.Review==null)row.Review=new SpReview();row.Review.Error=DbWorker.SafeError(e);}
     Job=Decode(await api.Send("/agent/jobs/"+JobId,null,token));item=Job["items"].Single(x=>x["objectId"].Value<int>()==objectId);
     await Checkpoint(objectId,row.Review?.Passed==true?"completed":"failed",item,watch.ElapsedMilliseconds-started,measuredCpu,measuredReads,token);Save();Signal(row.Module.FullName+"："+row.Stage);
     if(Job["status"].Value<string>()=="budget-exhausted")break;
    }
    if(Job["status"].Value<string>()=="running")Job=Decode(await api.Send("/agent/jobs/"+JobId+"/complete",Command(),token));Signal("批次"+State(Job)+"；不自動套用");
   }
   catch(OperationCanceledException){Signal(closing?"可續跑：worker 已停止，不再查詢資料庫。":"已取消／預算到期；已停止後续工作。");}
   catch(Exception e){Signal("批次停止："+DbWorker.SafeError(e));}
   finally
   {
    cancellation.Cancel();try{if(heartbeat!=null)await heartbeat;}catch{}heartbeat=null;
    if(!string.IsNullOrEmpty(Lease))try{Job=Decode(await api.Send("/agent/jobs/"+JobId+"/lease/release",Command(),CancellationToken.None));}catch{}
    try{if(Job!=null)Save();}catch(Exception e){Progress+="；本機紀錄寫入失敗："+DbWorker.SafeError(e);}finally{Busy=false;Signal();}
   }
  }
  async Task Checkpoint(int id,string state,JToken old,long elapsed,long cpu,long reads,CancellationToken token)
  {
   Job=Decode(await api.Send("/agent/jobs/"+JobId+"/items/"+id,new{leaseToken=Lease,databaseFingerprint=Context.Fingerprint,sequence=old["sequence"].Value<int>()+1,status=state,elapsedMs=checked(old["elapsedMs"].Value<long>()+elapsed),cpuMs=checked(old["cpuMs"].Value<long>()+cpu),logicalReads=checked(old["logicalReads"].Value<long>()+reads),tokens=old["tokens"].Value<long>()},token));
  }
  async Task Heartbeat(CancellationToken token)
  {
   try{while(true){await Task.Delay(10000,token);var active=await api.Send("/agent/jobs/"+JobId+"/cancel-requested",null,token);if(active["cancelRequested"]?.Value<bool>()!=false){cancellation.Cancel();return;}await api.Send("/agent/jobs/"+JobId+"/lease/renew",Command(),token);Signal();}}
   catch(OperationCanceledException){}catch{cancellation.Cancel();Signal("租約失效；停止 worker，避免重複執行。");}
  }
  public async Task Cancel(){cancellation?.Cancel();if(JobId!=null)Job=Decode(await api.Send("/agent/jobs/"+JobId+"/cancel",new{},CancellationToken.None));Signal("取消整批：不會開始下一支 SP。");}
  void Save(){var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Alyvo","SsmsAiSqlAssistant","scan-batches");Directory.CreateDirectory(folder);var copy=(JObject)Job.DeepClone();copy.Remove("leaseToken");copy.Remove("workerId");foreach(var item in copy["items"])((JObject)item).Remove("reservationLeaseToken");var path=Path.Combine(folder,JobId+".json");var temp=path+".tmp";File.WriteAllText(temp,copy.ToString(Formatting.Indented));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
  public void Dispose(){closing=true;cancellation?.Cancel();Changed=null;}
 }
}
