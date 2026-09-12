using Xunit;
using LocalService;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
namespace LocalService.Tests;
public sealed class EdgeCaseTests
{
    [Fact]public void Directory_as_key_file_does_not_stop_service(){var s=Settings.Load(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["OPENAI_API_KEY_FILE"]=Path.GetTempPath()}).Build());Assert.Null(s.Key);Assert.Equal("configured-file-unreadable",s.KeySource);}
    [Fact]public void Missing_cached_token_field_is_upper_bound(){using var doc=JsonDocument.Parse("{\"usage\":{\"input_tokens\":100,\"input_tokens_details\":{},\"output_tokens\":1}}");var usage=new OpenAi(new Settings{Model="unknown"},new HttpClient()).ParseUsage(doc.RootElement);Assert.Equal("upper-bound",usage.EstimateKind);Assert.Null(usage.EstimatedUsd);}
    [Fact]public async Task Chat_cannot_return_candidates(){await using var m=await MockOpenAi.Start();var ai=new OpenAi(new Settings{Key="test-chat-key",Model="model",Endpoint=m.Url},new HttpClient());Assert.Equal("openai_schema",(await Assert.ThrowsAsync<ApiError>(()=>ai.Analyze(Fixtures.Analyze(),default,"risks"))).Code);}
    [Fact]public async Task Response_secret_not_returned(){await using var m=await MockOpenAi.Start(body:Fixtures.Response("{\"summary\":\"secret-key-echo\",\"issues\":[],\"candidates\":[],\"warnings\":[]}"));var ai=new OpenAi(new Settings{Key="secret-key-echo",Model="model",Endpoint=m.Url},new HttpClient());Assert.Equal("sensitive_output",(await Assert.ThrowsAsync<ApiError>(()=>ai.Analyze(Fixtures.Analyze(),default))).Code);}
}
