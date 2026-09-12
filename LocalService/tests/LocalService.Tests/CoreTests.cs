using Xunit;
using LocalService;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;

namespace LocalService.Tests;
public sealed class TestClock:TimeProvider {public DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now;}
public sealed class LoopbackFilter:IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)=>app=>{app.Use((c,n)=>{c.Connection.RemoteIpAddress=IPAddress.Loopback;return n(c);});next(app);};
}
public sealed class Host:WebApplicationFactory<Program>
{
    public readonly string Dir=Path.Combine(Path.GetTempPath(),"localservice-test-"+Guid.NewGuid().ToString("N"));
    public readonly TestClock Clock=new();
    public Settings? Options;
    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        b.ConfigureServices(s=>{s.AddSingleton(Options??new Settings{DataDir=Dir});s.AddSingleton<TimeProvider>(Clock);s.AddSingleton<IStartupFilter,LoopbackFilter>();});
    }
    public HttpClient Client()=>CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("http://localhost"),AllowAutoRedirect=false});
    protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing && Directory.Exists(Dir))Directory.Delete(Dir,true);}
}
public static class Fixtures
{
    public static readonly string F=Contract.Hash("test database"),S=Contract.Hash("test server");
    public static AnalyzeRequest Analyze()=>new("SELECT * FROM dbo.orders",[new("dbo.orders",[new("id","int",false)]),new("dbo.unreferenced",[new("private","varchar",true)])],new(1,10,1),S,F);
    public static JobCreate Job()=>new(F,2,[10,20],new(100,1000,10000,1000),1);
    public static string Response(string output="{\"summary\":\"分析\",\"issues\":[],\"candidates\":[{\"sql\":\"SELECT id FROM dbo.orders\",\"explanation\":\"明確欄位\"}],\"warnings\":[]}")=>JsonSerializer.Serialize(new{status="completed",output=new[]{new{type="message",content=new[]{new{type="output_text",text=output}}}},usage=new{input_tokens=100,input_tokens_details=new{cached_tokens=20},output_tokens=10}});
}
public sealed class SqlTests
{
    [Theory][InlineData("SELECT 1",true)][InlineData("SELECT * FROM dbo.t",true)][InlineData("SELECT 1 INTO dbo.x",false)][InlineData("DELETE FROM dbo.t",false)][InlineData("EXEC dbo.x",false)][InlineData("UPDATE dbo.t SET x=1",false)][InlineData("CREATE PROCEDURE dbo.p AS SELECT 1",true)][InlineData("CREATE PROCEDURE dbo.p AS DELETE dbo.t",false)][InlineData("SELECT * FROM OPENQUERY(server,'SELECT 1')",false)][InlineData("SELECT * FROM other.dbo.t",false)][InlineData("SELECT (",false)]
    public void Read_only_fail_closed(string sql,bool expected)=>Assert.Equal(expected,SqlRules.Inspect(sql).ReadOnly);
    [Fact] public void No_apply_even_for_valid_contract(){var r=SqlRules.Validate(new("SELECT 1","説明"));Assert.True(r.Valid);Assert.False(r.CanApply);Assert.Equal("contract-only",r.VerificationMode);}
    [Fact] public void Rank_exclusions_and_order(){var r=SqlRules.Rank(new([new(1,"a","CREATE PROC dbo.a AS SELECT 1",false,false,100,10,2),new(2,"b",null,false,false,1000,100,100),new(3,"c","DELETE dbo.x",false,false,999,9,9),new(4,"d","SELECT 1",true,false,999,9,9)],1));Assert.Single(r.Candidates);Assert.Equal(1,r.Candidates[0].ObjectId);Assert.Equal(3,r.Skipped.Length);}
    [Fact] public void Only_referenced_metadata(){var r=Fixtures.Analyze();Assert.Single(SqlRules.Metadata(r,SqlRules.Inspect(r.SelectedSql)));}
    [Theory][InlineData(SqlRules.MetadataQuery)][InlineData(SqlRules.Evidence)][InlineData(SqlRules.Procedures)][InlineData(SqlRules.QueryStore)][InlineData(SqlRules.Parameters)]public void Generated_queries_are_readonly(string sql)=>Assert.True(SqlRules.Inspect(sql).ReadOnly);
}
public sealed class KeyTests
{
    private static Settings Load(Dictionary<string,string?> values)=>Settings.Load(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    [Fact]public void Priority_and_missing_file(){var s=Load(new(){["OPENAI_API_KEY"]="test-only-key",["OPENAI_API_KEY_FILE"]="Z:/missing-test-key"});Assert.Equal("OPENAI_API_KEY",s.KeySource);s=Load(new(){["OPENAI_API_KEY_FILE"]="Z:/missing-test-key"});Assert.Null(s.Key);Assert.Equal("configured-file-unreadable",s.KeySource);}
    [Fact]public void Key_file_and_privacy(){var file=Path.GetTempFileName();try{File.WriteAllText(file,"unit-test-secret");var s=Load(new(){["OPENAI_API_KEY_FILE"]=file});Assert.Equal("configured-file",s.KeySource);Assert.DoesNotContain("unit-test-secret",JsonSerializer.Serialize(s.Health()));}finally{File.Delete(file);}}
    [Fact]public void Unreadable_file(){var file=Path.GetTempFileName();try{using var locked=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None);var s=Load(new(){["OPENAI_API_KEY_FILE"]=file});Assert.Null(s.Key);Assert.Equal("configured-file-unreadable",s.KeySource);}finally{File.Delete(file);}}
}
public sealed class ApiTests
{
    [Fact]public async Task Health_local_and_capabilities(){using var h=new Host();using var c=h.Client();var r=await c.GetFromJsonAsync<JsonElement>("/health");Assert.Equal("local-rules",r.GetProperty("aiProvider").GetString());Assert.False(r.GetProperty("apiKeyConfigured").GetBoolean());var cap=await c.GetFromJsonAsync<JsonElement>("/api/v1/capabilities");Assert.Equal(170,cap.GetProperty("target").GetProperty("compatibilityLevel").GetInt32());}
    [Fact]public async Task Analyze_history_status_verification_delete(){using var h=new Host();using var c=h.Client();var result=await c.PostAsJsonAsync("/analyze",Fixtures.Analyze());result.EnsureSuccessStatusCode();var r=await result.Content.ReadFromJsonAsync<AnalysisResponse>();Assert.NotNull(r);Assert.Empty(r.Candidates);var history=await c.GetStringAsync("/history");Assert.DoesNotContain("SELECT",history);Assert.DoesNotContain("orders",history);Assert.Contains(r.Id,history);(await c.PostAsJsonAsync($"/history/{r.Id}/status",new StatusRequest("reviewed"))).EnsureSuccessStatusCode();(await c.PostAsJsonAsync("/history/verification",new VerificationRequest(r.Id,"passed","contract-only"))).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.OK,(await c.DeleteAsync($"/history/{r.Id}")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await c.GetAsync($"/history/{r.Id}")).StatusCode);}
    [Theory][InlineData("{}")][InlineData("{\"selectedSql\":\"SELECT 1\"}")][InlineData("null")][InlineData("not-json")]
    public async Task Required_fields_fail_closed(string body){using var h=new Host();using var c=h.Client();var r=await c.PostAsync("/analyze",new StringContent(body,Encoding.UTF8,"application/json"));Assert.Equal(HttpStatusCode.BadRequest,r.StatusCode);var error=await r.Content.ReadFromJsonAsync<ErrorResponse>();Assert.NotNull(error);Assert.NotEmpty(error.RequestId);}
    [Fact]public async Task Optional_unknown_fields_ignored(){using var h=new Host();using var c=h.Client();var json=JsonSerializer.Serialize(Fixtures.Analyze(),Contract.Json);json=json[..^1]+",\"futureField\":true,\"connectionString\":\"not-forwarded\"}";var r=await c.PostAsync("/analyze",new StringContent(json,Encoding.UTF8,"application/json"));r.EnsureSuccessStatusCode();Assert.DoesNotContain("not-forwarded",await c.GetStringAsync("/history"));}
    [Fact]public async Task Error_envelope_and_unknown_status(){using var h=new Host();using var c=h.Client();Assert.Equal(HttpStatusCode.NotFound,(await c.GetAsync("/missing")).StatusCode);var r=await c.PostAsJsonAsync("/agent/monitor/events/none/status",new StatusRequest("magic-success"));Assert.Equal(HttpStatusCode.BadRequest,r.StatusCode);Assert.Contains("unsupported",await r.Content.ReadAsStringAsync());}
    [Theory][InlineData("/history/ui")][InlineData("/usage/ui")][InlineData("/jobs/ui")][InlineData("/agent/governance/ui")]
    public async Task Pages_shared_style_real_empty_state(string path){using var h=new Host();using var c=h.Client();var html=await c.GetStringAsync(path);Assert.Contains("lang=\"zh-TW\"",html);Assert.Contains("目前沒有紀錄",html);Assert.Contains("overflow-x:auto",html);Assert.Contains("#1D4ED8",html);}
    [Fact]public async Task Body_cors_host_version_limits(){using var h=new Host();using var c=h.Client();using var req=new HttpRequestMessage(HttpMethod.Get,"/health");req.Headers.Add("Origin","https://evil.example");Assert.Equal(HttpStatusCode.Forbidden,(await c.SendAsync(req)).StatusCode);using var v=new HttpRequestMessage(HttpMethod.Get,"/health");v.Headers.Add("X-Api-Version","old");Assert.Equal(HttpStatusCode.Conflict,(await c.SendAsync(v)).StatusCode);using var host=new HttpRequestMessage(HttpMethod.Get,"/health");host.Headers.Host="evil.example";Assert.Equal(HttpStatusCode.Forbidden,(await c.SendAsync(host)).StatusCode);var big=await c.PostAsync("/analyze",new StringContent(new string('x',Settings.MaxBody+1),Encoding.UTF8,"application/json"));Assert.Equal(HttpStatusCode.RequestEntityTooLarge,big.StatusCode);}
    [Fact]public async Task Contract_inventory(){using var h=new Host();using var c=h.Client();var doc=await c.GetFromJsonAsync<JsonElement>("/openapi.json");Assert.Equal("3.1.0",doc.GetProperty("openapi").GetString());var paths=doc.GetProperty("paths");foreach(var path in new[]{"/health","/analyze","/chat","/validate","/agent/jobs/{id}/lease/renew","/agent/jobs/{id}/demo-replay","/agent/jobs/resumable/{databaseFingerprint}","/sp/change-audit/verify","/agent/schedules/{id}/runs"})Assert.True(paths.TryGetProperty(path,out _),path);Assert.True(paths.EnumerateObject().Count()>=45);}
    [Fact]public async Task Governance_and_audit_real_crud(){using var h=new Host();using var c=h.Client();(await c.PostAsJsonAsync("/agent/memory",new MemoryInput(Fixtures.F,Fixtures.S,"passed"))).EnsureSuccessStatusCode();var sr=await c.PostAsJsonAsync("/agent/schedules",new ScheduleInput(Fixtures.F,2,60,true));var schedule=await sr.Content.ReadFromJsonAsync<GovernanceRecord<ScheduleInput>>();var jr=await c.PostAsJsonAsync("/agent/jobs",Fixtures.Job());var job=await jr.Content.ReadFromJsonAsync<JobRecord>();(await c.PostAsJsonAsync($"/agent/schedules/{schedule!.Id}/runs",new ScheduleRun(job!.Id))).EnsureSuccessStatusCode();var er=await c.PostAsJsonAsync("/agent/monitor/events",new EventInput(Fixtures.F,"cpu-regression",2));var ev=await er.Content.ReadFromJsonAsync<GovernanceRecord<EventInput>>();(await c.PostAsJsonAsync($"/agent/monitor/events/{ev!.Id}/status",new StatusRequest("resolved"))).EnsureSuccessStatusCode();(await c.PostAsJsonAsync("/sp/change-audit",new AuditInput(Fixtures.F,1,Fixtures.F,Fixtures.S,"apply"))).EnsureSuccessStatusCode();Assert.Contains("true",await c.GetStringAsync("/sp/change-audit/verify"));}
}
