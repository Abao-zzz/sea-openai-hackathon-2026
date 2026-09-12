using System.Net.Http.Json;
using Xunit;
using LocalService;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;

namespace LocalService.Tests;
public sealed class MockOpenAi:IAsyncDisposable
{
    private readonly WebApplication app;
    public Uri Url {get;}
    public string Body="";
    public int Calls;
    private MockOpenAi(WebApplication app,Uri url){this.app=app;Url=url;}
    public static async Task<MockOpenAi> Start(int status=200,string? body=null,int delayMs=0)
    {
        var b=WebApplication.CreateBuilder();b.Logging.ClearProviders();b.WebHost.ConfigureKestrel(o=>o.Listen(IPAddress.Loopback,0));var app=b.Build();MockOpenAi? mock=null;
        app.MapPost("/responses",async c=>{mock!.Calls++;mock.Body=await new StreamReader(c.Request.Body).ReadToEndAsync(c.RequestAborted);if(delayMs>0)await Task.Delay(delayMs,c.RequestAborted);c.Response.StatusCode=status;c.Response.ContentType="application/json";await c.Response.WriteAsync(body??Fixtures.Response(),c.RequestAborted);});
        await app.StartAsync();var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();mock=new(app,new Uri(address+"/responses"));return mock;
    }
    public async ValueTask DisposeAsync(){await app.StopAsync();await app.DisposeAsync();}
}
public sealed class OpenAiTests
{
    private static OpenAi Client(MockOpenAi m,int timeout=3)=>new(new Settings{Key="unit-test-only",Model="unit-test-model",Endpoint=m.Url,TimeoutSeconds=timeout},new HttpClient(new SocketsHttpHandler{AllowAutoRedirect=false}){Timeout=System.Threading.Timeout.InfiniteTimeSpan});
    [Fact]public async Task Success_real_http_metadata_privacy_usage(){await using var m=await MockOpenAi.Start();var(answer,usage)=await Client(m).Analyze(Fixtures.Analyze(),default);Assert.Single(answer.Candidates);Assert.Equal(100,usage.InputTokens);Assert.Equal(20,usage.CachedInputTokens);Assert.Null(usage.EstimatedUsd);Assert.Equal("unknown-model",usage.EstimateKind);Assert.DoesNotContain("dbo.unreferenced",m.Body);Assert.DoesNotContain(Fixtures.F,m.Body);Assert.DoesNotContain("unit-test-only",m.Body);Assert.Contains("\"store\":false",m.Body);}
    [Theory][InlineData(401,"openai_401")][InlineData(403,"openai_403")][InlineData(429,"openai_429")][InlineData(500,"openai_500")][InlineData(503,"openai_503")]
    public async Task Http_errors_are_sanitized(int status,string code){await using var m=await MockOpenAi.Start(status,"provider-secret-key");var e=await Assert.ThrowsAsync<ApiError>(()=>Client(m).Analyze(Fixtures.Analyze(),default));Assert.Equal(code,e.Code);Assert.DoesNotContain("provider-secret",e.Message);}
    [Theory][InlineData("not json","openai_json")][InlineData("{}","openai_schema")][InlineData("{\"output\":[]}","openai_empty")][InlineData("{\"status\":\"incomplete\"}","openai_incomplete")][InlineData("{\"output\":[{\"content\":[{\"type\":\"refusal\",\"refusal\":\"no\"}]}]}","openai_refusal")]
    public async Task Malformed_envelope(string body,string code){await using var m=await MockOpenAi.Start(body:body);var e=await Assert.ThrowsAsync<ApiError>(()=>Client(m).Analyze(Fixtures.Analyze(),default));Assert.Equal(code,e.Code);}
    [Theory][InlineData("{}","openai_schema")][InlineData("not json","openai_json")][InlineData("{\"summary\":\"a\",\"issues\":[],\"candidates\":[{\"sql\":\"DELETE dbo.t\",\"explanation\":\"bad\"}],\"warnings\":[]}","unsafe_candidate")]
    public async Task Malformed_or_unsafe_output(string text,string code){await using var m=await MockOpenAi.Start(body:Fixtures.Response(text));var e=await Assert.ThrowsAsync<ApiError>(()=>Client(m).Analyze(Fixtures.Analyze(),default));Assert.Equal(code,e.Code);}
    [Fact]public async Task Markdown_fence(){await using var m=await MockOpenAi.Start(body:Fixtures.Response("```json\n{\"summary\":\"a\",\"issues\":[],\"candidates\":[],\"warnings\":[]}\n```"));var(a,_)=await Client(m).Analyze(Fixtures.Analyze(),default);Assert.Equal("a",a.Summary);}
    [Fact]public async Task Timeout(){await using var m=await MockOpenAi.Start(delayMs:2000);Assert.Equal("openai_timeout",(await Assert.ThrowsAsync<ApiError>(()=>Client(m,1).Analyze(Fixtures.Analyze(),default))).Code);}
    [Fact]public async Task Cancellation(){await using var m=await MockOpenAi.Start(delayMs:2000);using var cts=new CancellationTokenSource(100);await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Client(m).Analyze(Fixtures.Analyze(),cts.Token));}
    [Fact]public async Task Oversize(){await using var m=await MockOpenAi.Start(body:new string('x',Settings.MaxBody+1));Assert.Equal("openai_oversized",(await Assert.ThrowsAsync<ApiError>(()=>Client(m).Analyze(Fixtures.Analyze(),default))).Code);}
    [Fact]public async Task Oversize_text(){await using var m=await MockOpenAi.Start(body:Fixtures.Response(new string('x',64001)));Assert.Equal("openai_oversized",(await Assert.ThrowsAsync<ApiError>(()=>Client(m).Analyze(Fixtures.Analyze(),default))).Code);}
    [Fact]public async Task No_key_never_calls_provider(){await using var m=await MockOpenAi.Start();using var h=new Host();h.Options=new Settings{DataDir=h.Dir,Endpoint=m.Url};using var c=h.Client();(await c.PostAsJsonAsync("/analyze",Fixtures.Analyze())).EnsureSuccessStatusCode();await c.GetAsync("/health");await c.GetAsync("/agent/stored-procedures/query");Assert.Equal(0,m.Calls);}
    [Fact]public async Task Json_mode_input_explicitly_requests_json(){await using var m=await MockOpenAi.Start();await Client(m).Analyze(Fixtures.Analyze(),default);using var request=JsonDocument.Parse(m.Body);Assert.Contains("JSON",request.RootElement.GetProperty("input").GetString());}
    [Fact]public void Cached_pricing_and_legacy_upper_bound(){var s=new Settings{Model="priced-test",Prices=new(){["priced-test"]=[1,0.1m,2,4]}};var ai=new OpenAi(s,new HttpClient());using var d=JsonDocument.Parse("{\"usage\":{\"input_tokens\":100,\"output_tokens\":10}}");var u=ai.ParseUsage(d.RootElement);Assert.Equal("upper-bound",u.EstimateKind);Assert.Equal(0.00014m,u.EstimatedUsd);}
}
