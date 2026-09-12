using LocalService;
using System.Text.Json;

var builder=WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders(); // Never allow framework request/body/exception logging to leak SQL or credentials.
builder.WebHost.UseUrls(builder.Configuration["SSMS_AI_URLS"]??"http://127.0.0.1:46217");
builder.WebHost.ConfigureKestrel(o=>{o.Limits.MaxRequestBodySize=Settings.MaxBody;o.Limits.RequestHeadersTimeout=TimeSpan.FromSeconds(15);});
builder.Services.AddSingleton(_=>Settings.Load(builder.Configuration));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Store>();builder.Services.AddSingleton<Jobs>();builder.Services.AddSingleton<SecurityGate>();
builder.Services.AddSingleton(new HttpClient(new SocketsHttpHandler {AllowAutoRedirect=false}){Timeout=Timeout.InfiniteTimeSpan});
builder.Services.AddSingleton<OpenAi>();builder.Services.AddSingleton<Analysis>();builder.Services.AddHostedService<RetentionWorker>();
var app=builder.Build();var settings=app.Services.GetRequiredService<Settings>();var store=app.Services.GetRequiredService<Store>();var jobs=app.Services.GetRequiredService<Jobs>();var analysis=app.Services.GetRequiredService<Analysis>();var clock=app.Services.GetRequiredService<TimeProvider>();
app.Use(async (context,next)=>
{
    context.TraceIdentifier=Guid.NewGuid().ToString("D");context.Response.Headers["X-Request-Id"]=context.TraceIdentifier;context.Response.Headers["X-Api-Version"]=Contract.Version;context.Response.Headers["X-Content-Type-Options"]="nosniff";context.Response.Headers["Cache-Control"]="no-store";context.Response.Headers["Content-Security-Policy"]="default-src 'none'; style-src 'unsafe-inline'; script-src 'sha256-"+ManagementPages.ScriptHash+"'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    try
    {
        app.Services.GetRequiredService<SecurityGate>().Check(context);
        // Bound chunked requests as well as Content-Length requests before deserialization.
        if(context.Request.Method=="POST")
        {
            using var buffer=new MemoryStream();var chunk=new byte[8192];int n;
            while((n=await context.Request.Body.ReadAsync(chunk,context.RequestAborted))>0){if(buffer.Length+n>Settings.MaxBody)throw new ApiError(413,"body_too_large","要求內容超過大小限制。");buffer.Write(chunk,0,n);}
            context.Request.Body=new MemoryStream(buffer.ToArray());
            try{await next(context);}finally{await context.Request.Body.DisposeAsync();}
        }
        else await next(context);
    }
    catch(Exception e)
    {
        if(context.Response.HasStarted){context.Abort();return;}
        var error=e switch {ApiError a=>a,JsonException=>new ApiError(400,"invalid_json","JSON 格式不正確。"),BadHttpRequestException b=>new ApiError(b.StatusCode,"invalid_request","要求格式不正確。"),OperationCanceledException=>new ApiError(499,"cancelled","要求已取消。"),_=>new ApiError(500,"internal_error","服務處理失敗，未執行 SQL。")};
        context.Response.StatusCode=error.Status;await context.Response.WriteAsJsonAsync(new ErrorResponse(error.Code,error.Message,context.TraceIdentifier),Contract.Json,CancellationToken.None);
    }
});
var api=new ApiRoutes(app);
static T[] PageRows<T>(HttpContext c,IEnumerable<T> rows)
{
    int Read(string key,int fallback,int min,int max){if(!c.Request.Query.TryGetValue(key,out var raw))return fallback;if(!int.TryParse(raw,out var value)||value<min||value>max)throw new ApiError(400,"invalid_pagination","分頁參數格式不正確。");return value;}
    return rows.Skip(Read("offset",0,0,int.MaxValue)).Take(Read("limit",100,1,200)).ToArray();
}
static string Id(HttpContext c)=>c.Request.RouteValues["id"]?.ToString()??throw new ApiError(400,"missing_id","缺少識別碼。");
api.Get("/health",_=>settings.Health());
api.Get("/api/v1/capabilities",_=>new {apiVersion=Contract.Version,language="zh-TW",features=new {analysis=true,chat=true,history=true,usage=true,jobs=true,governance=true,audit=true,sqlExecution=false,spMap=false,highCostScanWorker=false},budgets=new {maxRequestBytes=Settings.MaxBody,maxResponseBytes=Settings.MaxBody,maxSelectedSqlCharacters=64000,maxTopN=100,maxLeaseSeconds=300,queryStoreTimeoutSeconds=3},supportedVerificationModes=new[]{"contract-only"},target=new {localService="net10.0",ssms="22",sqlServer="2025 (17.x)",compatibilityLevel=170}});
api.Get("/metadata-query",_=>new QueryResponse(SqlRules.MetadataQuery,30,"ssms-worker"));
api.Get("/evidence-script",_=>new QueryResponse(SqlRules.Evidence,30,"ssms-worker"));
api.PostAsync<AnalyzeRequest,AnalysisResponse>("/analyze",(c,r)=>analysis.Run(r,c.RequestAborted));
api.PostAsync<ChatRequest,AnalysisResponse>("/chat",(c,r)=>{Contract.Choice(r.Topic,"explanation","risks","indexes");return analysis.Run(r.Context,c.RequestAborted,r.Topic);});
api.Post<ValidateRequest,ValidationResponse>("/validate",(_,r)=>SqlRules.Validate(r.Candidate));
api.Get("/agent/stored-procedures/query",_=>new QueryResponse(SqlRules.Procedures,30,"ssms-worker"));
api.Get("/agent/stored-procedures/query-store-summary-query",_=>new QueryResponse(SqlRules.QueryStore,3,"ssms-worker-retain-dmv-on-timeout"));
api.Get("/agent/stored-procedures/parameter-evidence-query",_=>new QueryResponse(SqlRules.Parameters,30,"ssms-worker"));
api.Post<RankRequest,RankResponse>("/agent/stored-procedures/rank",(_,r)=>SqlRules.Rank(r));
api.PostAsync<PrepareRequest,AnalysisResponse>("/agent/stored-procedures/prepare",async(c,r)=>
{
    Contract.Choice(r.Trigger,"single-user","batch-user");
    if(r.Trigger=="single-user")return await analysis.Run(r.Context,c.RequestAborted);
    if(r.JobId is null || r.LeaseToken is null || r.ObjectId is null)throw new ApiError(400,"batch_context_required","批次 prepare 需要 jobId、leaseToken 與 objectId。");
    var inputBound=settings.Key is null?0:System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(r.Context,Contract.Json))+8192L;
    var outputLimit=jobs.ReserveAi(r.JobId,new(r.LeaseToken,r.Context.DatabaseFingerprint),r.ObjectId.Value,inputBound);
    long? consumed=null;
    try {var result=await analysis.Run(r.Context,c.RequestAborted,outputTokenLimit:outputLimit);consumed=result.Usage.InputTokens+result.Usage.OutputTokens;return result;}
    finally {jobs.SettleAi(r.JobId,r.ObjectId.Value,r.LeaseToken,consumed);}
});
api.Post<ValidateRequest,PrefilterResponse>("/agent/candidate-prefilter",(_,r)=>SqlRules.Prefilter(r));
api.Get("/history",c=>PageRows(c,store.List<HistoryRecord>("history")));
api.Get("/history/stats",_=>new {count=store.List<HistoryRecord>("history").Count,retentionDays=settings.RetentionDays});
api.Get("/history/{id}",c=>store.Get<HistoryRecord>("history",Id(c)));
api.Post<VerificationRequest,HistoryRecord>("/history/verification",(_,r)=>{Contract.Choice(r.Status,"passed","failed","unknown");Contract.Choice(r.Mode,"contract-only");return store.Update<HistoryRecord>("history",r.Id,h=>h with {VerificationStatus="contract-"+r.Status});});
api.Post<StatusRequest,HistoryRecord>("/history/{id}/status",(c,r)=>{Contract.Choice(r.Status,"analyzed","reviewed","rejected","archived");return store.Update<HistoryRecord>("history",Id(c),h=>h with {Status=r.Status});});
api.Delete("/history/{id}",c=>new {deleted=store.Delete("history",Id(c))});api.Delete("/history",_=>new {deleted=store.Delete("history")});
api.Get("/usage",c=>PageRows(c,store.List<UsageRecord>("usage").Select(x=>x with {Usage=x.Usage with {EstimateKind=x.Usage.EstimateKind??"upper-bound"}})));
string[] historyHead=["識別碼","UTC 時間","資料庫指紋","提供者","狀態","驗證狀態"];
string[][] HistoryRows()=>store.List<HistoryRecord>("history").Select(h=>new[]{h.Id,h.CreatedAt.ToString("O"),h.DatabaseFingerprint,h.Provider,h.Status,h.VerificationStatus}).ToArray();
string[] usageHead=["識別碼","UTC 時間","模型","輸入 token","快取輸入","快取寫入","輸出 token","估計 USD","估價方式"];
string[][] UsageRows()=>store.List<UsageRecord>("usage").Select(u=>new[]{u.Id,u.CreatedAt.ToString("O"),u.Usage.Model,u.Usage.InputTokens.ToString(),u.Usage.CachedInputTokens.ToString(),u.Usage.CacheWriteTokens.ToString(),u.Usage.OutputTokens.ToString(),u.Usage.EstimatedUsd?.ToString(System.Globalization.CultureInfo.InvariantCulture)??"未知",u.Usage.EstimateKind??"upper-bound"}).ToArray();
api.File("/history/export.csv",_=>Pages.Csv(historyHead,HistoryRows()),"text/csv");api.File("/usage/export.csv",_=>Pages.Csv(usageHead,UsageRows()),"text/csv");
api.Post<JobCreate,JobRecord>("/agent/jobs",(_,r)=>jobs.Create(r));api.Get("/agent/jobs",c=>PageRows(c,jobs.List()));api.Get("/agent/jobs/{id}",c=>jobs.Get(Id(c)));
api.Get("/agent/jobs/resumable/{databaseFingerprint}",c=>{var fp=c.Request.RouteValues["databaseFingerprint"]!.ToString()!;Contract.Fingerprint(fp);return PageRows(c,jobs.List().Where(j=>j.DatabaseFingerprint==fp && j.Status is "pending" or "running" && (j.LeaseExpires is null || j.LeaseExpires<=clock.GetUtcNow())));});
api.Post<LeaseRequest,JobRecord>("/agent/jobs/{id}/lease",(c,r)=>jobs.Lease(Id(c),r));
foreach(var action in new[]{"renew","release","start","complete"})
{
    var route=action is "renew" or "release"?"lease/"+action:action;
    api.Post<JobCommand,JobRecord>("/agent/jobs/{id}/"+route,(c,r)=>jobs.Command(Id(c),action,r));
}
api.Post<EmptyRequest,JobRecord>("/agent/jobs/{id}/cancel",(c,_)=>jobs.Cancel(Id(c)));
api.Post<EmptyRequest,JobRecord>("/agent/jobs/{id}/retry",(c,_)=>jobs.Retry(Id(c)));
api.Post<EmptyRequest,JobRecord>("/agent/jobs/{id}/replay",(c,_)=>jobs.Replay(Id(c),false));
api.Post<EmptyRequest,JobRecord>("/agent/jobs/{id}/demo-replay",(c,_)=>jobs.Replay(Id(c),true));
api.Post<CheckpointRequest,JobRecord>("/agent/jobs/{id}/items/{objectId}",(c,r)=>{if(!int.TryParse(c.Request.RouteValues["objectId"]?.ToString(),out var objectId))throw new ApiError(400,"invalid_object_id","物件識別碼格式錯誤。");return jobs.Checkpoint(Id(c),objectId,r);});
api.Get("/agent/jobs/{id}/cancel-requested",c=>new {cancelRequested=jobs.Get(Id(c)).CancelRequested});
api.Post<AuditInput,AuditRecord>("/sp/change-audit",(_,r)=>store.AppendAudit(r));api.Get("/sp/change-audit",c=>PageRows(c,store.List<AuditRecord>("audit")));api.Get("/sp/change-audit/verify",_=>store.VerifyAudit());
api.File("/sp/change-audit/export.csv",_=>Pages.Csv(["識別碼","UTC 時間","資料庫指紋","物件 ID","動作","先前雜湊","雜湊"],store.List<AuditRecord>("audit").Select(a=>new[]{a.Id,a.CreatedAt.ToString("O"),a.Change.DatabaseFingerprint,a.Change.ObjectId.ToString(),a.Change.Action,a.PreviousHash,a.Hash})),"text/csv");
api.Post<MemoryInput,GovernanceRecord<MemoryInput>>("/agent/memory",(_,r)=>{Contract.Fingerprint(r.DatabaseFingerprint);Contract.Fingerprint(r.SqlHash);Contract.Choice(r.Outcome,"passed","failed","rejected");var v=new GovernanceRecord<MemoryInput>(Contract.Id(),clock.GetUtcNow(),r,"active");return store.Add("memory",v.Id,v);});api.Get("/agent/memory",c=>PageRows(c,store.List<GovernanceRecord<MemoryInput>>("memory")));
api.Post<ScheduleInput,GovernanceRecord<ScheduleInput>>("/agent/schedules",(_,r)=>{Contract.Fingerprint(r.DatabaseFingerprint);var v=new GovernanceRecord<ScheduleInput>(Contract.Id(),clock.GetUtcNow(),r,"worker-required");return store.Add("schedules",v.Id,v);});api.Get("/agent/schedules",c=>PageRows(c,store.List<GovernanceRecord<ScheduleInput>>("schedules")));
api.Post<ScheduleRun,GovernanceRecord<ScheduleRun>>("/agent/schedules/{id}/runs",(c,r)=>{var schedule=store.Get<GovernanceRecord<ScheduleInput>>("schedules",Id(c));var job=jobs.Get(r.JobId);if(!schedule.Data.Enabled || schedule.Data.DatabaseFingerprint!=job.DatabaseFingerprint)throw new ApiError(409,"schedule_mismatch","排程已停用或資料庫不相符。");var v=new GovernanceRecord<ScheduleRun>(Contract.Id(),clock.GetUtcNow(),r,"reported");return store.Add("schedule-runs-"+schedule.Id,v.Id,v);});
api.Post<EventInput,GovernanceRecord<EventInput>>("/agent/monitor/events",(_,r)=>{Contract.Fingerprint(r.DatabaseFingerprint);Contract.Choice(r.Kind,"cpu-regression","io-regression","duration-regression","worker-disconnected");var v=new GovernanceRecord<EventInput>(Contract.Id(),clock.GetUtcNow(),r,"open");return store.Add("events",v.Id,v);});api.Get("/agent/monitor/events",c=>PageRows(c,store.List<GovernanceRecord<EventInput>>("events")));
api.Post<StatusRequest,GovernanceRecord<EventInput>>("/agent/monitor/events/{id}/status",(c,r)=>{Contract.Choice(r.Status,"open","acknowledged","resolved");return store.Update<GovernanceRecord<EventInput>>("events",Id(c),v=>v with {Status=r.Status});});
api.File("/history/ui",_=>ManagementPages.Render("history",settings.Prices),"text/html");
api.File("/usage/ui",_=>ManagementPages.Render("usage",settings.Prices),"text/html");
api.File("/jobs/ui",_=>Pages.Page("批次工作",["識別碼","UTC 時間","狀態","Top N","完成數","要求取消"],jobs.List().Select(j=>new[]{j.Id,j.CreatedAt.ToString("O"),j.Status,j.TopN.ToString(),j.Items.Count(x=>x.Status=="completed").ToString(),j.CancelRequested?"是":"否"})),"text/html");
api.File("/agent/governance/ui",_=>ManagementPages.Render("governance",settings.Prices),"text/html");
api.File("/",_=>Pages.Page("LocalService",["服務","狀態"],new[]{new[]{"API 版本",Contract.Version},new[]{"執行環境",".NET 10"},new[]{"AI 提供者",settings.Key is null?"本機規則":"OpenAI"}}).Replace("</main>","<section class='card'><a href='/health'>health</a> · <a href='/api/v1/capabilities'>capabilities</a> · <a href='/openapi.json'>OpenAPI 3.1</a></section></main>"),"text/html");
app.MapGet("/openapi.json",()=>Results.Json(api.Document(),Contract.Json));
app.MapFallback((HttpContext c)=>Results.Json(new ErrorResponse("not_found","找不到 API。",c.TraceIdentifier),Contract.Json,statusCode:404));
app.Lifetime.ApplicationStarted.Register(()=>Console.WriteLine("LocalService 已啟動；API "+Contract.Version+"；"+(settings.Key is null?"本機規則":"OpenAI 已設定")));
app.Run();
public partial class Program { }
namespace LocalService { public sealed record EmptyRequest; }
