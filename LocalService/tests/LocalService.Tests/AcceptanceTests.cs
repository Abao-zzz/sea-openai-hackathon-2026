using Xunit;
using LocalService;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
namespace LocalService.Tests;
public sealed class AcceptanceTests
{
    [Fact]public void Migration_v1_cache_details_and_idempotency()
    {
        using var h=new Host();Directory.CreateDirectory(h.Dir);using(var c=new SqliteConnection("Pooling=False;Data Source="+Path.Combine(h.Dir,"assistant-history.db")))
        {
            c.Open();using var cmd=c.CreateCommand();cmd.CommandText="CREATE TABLE records(kind TEXT,id TEXT,created TEXT,data TEXT,PRIMARY KEY(kind,id)); PRAGMA user_version=1; INSERT INTO records VALUES('usage','legacy',$date,$data);";cmd.Parameters.AddWithValue("$date",h.Clock.Now.ToString("O"));cmd.Parameters.AddWithValue("$data",JsonSerializer.Serialize(new{id="legacy",createdAt=h.Clock.Now,usage=new{inputTokens=100,outputTokens=10,model="legacy",estimatedUsd=0.001}},Contract.Json));cmd.ExecuteNonQuery();
        }
        var s=new Store(new Settings{DataDir=h.Dir},h.Clock);var u=Assert.Single(s.List<UsageRecord>("usage"));Assert.Equal("upper-bound",u.Usage.EstimateKind);Assert.Equal(0,u.Usage.CachedInputTokens);Assert.Single(new Store(new Settings{DataDir=h.Dir},h.Clock).List<UsageRecord>("usage"));
    }
    [Fact]public void Future_schema_refused(){using var h=new Host();Directory.CreateDirectory(h.Dir);using(var c=new SqliteConnection("Pooling=False;Data Source="+Path.Combine(h.Dir,"assistant-history.db"))){c.Open();using var cmd=c.CreateCommand();cmd.CommandText="PRAGMA user_version=99";cmd.ExecuteNonQuery();}Assert.Throws<InvalidOperationException>(()=>new Store(new Settings{DataDir=h.Dir},h.Clock));}
    [Fact]public async Task All_job_http_routes()
    {
        using var h=new Host();using var c=h.Client();
        async Task<JobRecord> Post(string path,object r){var response=await c.PostAsJsonAsync(path,r);Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());return (await response.Content.ReadFromJsonAsync<JobRecord>())!;}
        var j=await Post("/agent/jobs",Fixtures.Job() with{TopN=1,ObjectIds=[10]});var root="/agent/jobs/"+j.Id;
        Assert.Equal(HttpStatusCode.OK,(await c.GetAsync("/agent/jobs")).StatusCode);Assert.Equal(HttpStatusCode.OK,(await c.GetAsync(root)).StatusCode);Assert.Contains(j.Id,await c.GetStringAsync("/agent/jobs/resumable/"+Fixtures.F));
        j=await Post(root+"/lease",new LeaseRequest("worker",Fixtures.F,30));var cmd=new JobCommand(j.LeaseToken!,Fixtures.F);await Post(root+"/lease/renew",cmd);await Post(root+"/start",cmd);await Post(root+"/items/10",new CheckpointRequest(cmd.LeaseToken,Fixtures.F,1,"failed",1,1,1,1));await Post(root+"/lease/release",cmd);await Post(root+"/retry",new{});j=await Post(root+"/lease",new LeaseRequest("worker",Fixtures.F,30));cmd=new(j.LeaseToken!,Fixtures.F);await Post(root+"/start",cmd);await Post(root+"/items/10",new CheckpointRequest(cmd.LeaseToken,Fixtures.F,2,"completed",2,2,2,2));await Post(root+"/complete",cmd);await Post(root+"/demo-replay",new{});var replay=await Post(root+"/replay",new{});await Post("/agent/jobs/"+replay.Id+"/cancel",new{});Assert.Contains("true",await c.GetStringAsync("/agent/jobs/"+replay.Id+"/cancel-requested"));
    }
    [Fact]public async Task Remaining_routes_have_real_responses()
    {
        using var h=new Host();using var c=h.Client();
        foreach(var path in new[]{"/metadata-query","/evidence-script","/agent/stored-procedures/query","/agent/stored-procedures/query-store-summary-query","/agent/stored-procedures/parameter-evidence-query","/history/stats","/history/export.csv","/usage/export.csv","/usage","/agent/memory","/agent/schedules","/agent/monitor/events","/sp/change-audit","/sp/change-audit/export.csv","/"}){var r=await c.GetAsync(path);Assert.True(r.IsSuccessStatusCode,path);}
        var q=await c.GetFromJsonAsync<QueryResponse>("/agent/stored-procedures/query-store-summary-query");Assert.Equal(3,q!.TimeoutSeconds);
        (await c.PostAsJsonAsync("/chat",new ChatRequest(Fixtures.Analyze(),"risks"))).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/agent/stored-procedures/prepare",new PrepareRequest(Fixtures.Analyze(),"single-user"))).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/agent/stored-procedures/rank",new RankRequest([new(1,"dbo.p","CREATE PROC dbo.p AS SELECT 1",false,false,1,1,1)],1))).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/agent/candidate-prefilter",new ValidateRequest(new("SELECT 1","說明")))).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/validate",new ValidateRequest(new("SELECT 1","說明")))).EnsureSuccessStatusCode();
        (await c.DeleteAsync("/history")).EnsureSuccessStatusCode();Assert.Equal("[]",await c.GetStringAsync("/history"));
    }
    [Fact]public async Task Key_configured_read_endpoints_do_not_call_OpenAI()
    {
        await using var m=await MockOpenAi.Start();using var h=new Host();h.Options=new Settings{DataDir=h.Dir,Key="test-only",Model="test-model",Endpoint=m.Url};using var c=h.Client();
        foreach(var p in new[]{"/health","/metadata-query","/agent/stored-procedures/query","/history","/usage","/agent/jobs","/agent/memory"})(await c.GetAsync(p)).EnsureSuccessStatusCode();
        (await c.PostAsJsonAsync("/agent/candidate-prefilter",new ValidateRequest(new("SELECT 1","說明")))).EnsureSuccessStatusCode();Assert.Equal(0,m.Calls);
        (await c.PostAsJsonAsync("/analyze",Fixtures.Analyze())).EnsureSuccessStatusCode();Assert.Equal(1,m.Calls);var usage=await c.GetStringAsync("/usage/export.csv");Assert.Contains("100",usage);Assert.DoesNotContain("SELECT",usage);Assert.DoesNotContain("test-only",usage);Assert.DoesNotContain("orders",usage);
    }
}
