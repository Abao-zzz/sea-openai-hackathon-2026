using System.Net.Http.Json;
using System.Text.Json;
using LocalService;
using Xunit;
namespace LocalService.Tests;
public class SqlSuggestionTests
{
 [Theory][InlineData("CREATE NONCLUSTERED INDEX IX_orders ON dbo.orders(id) INCLUDE(amount);",true)][InlineData("UPDATE STATISTICS dbo.orders;",true)][InlineData("DROP TABLE dbo.orders;",false)][InlineData("CREATE INDEX ix ON dbo.orders(id); DELETE dbo.orders;",false)][InlineData("SELECT 1;",false)]
 public void SuggestionsAreSeparateFromExecutableCandidates(string sql,bool valid)=>Assert.Equal(valid,SqlRules.ValidSuggestion(sql));
 [Theory][InlineData(false)][InlineData(true)]
 public async Task SuggestionsReachBothApiResponsesWithoutBecomingCandidates(bool procedure)
 {
  var suggestion=new SqlSuggestion("新增客戶索引","CREATE INDEX IX_orders ON dbo.orders(id);","先測試寫入成本。");var answer=new AiAnswer("建議索引",[],[],[],[suggestion]);await using var provider=await MockOpenAi.Start(body:Fixtures.Response(JsonSerializer.Serialize(answer,Contract.Json)));using var host=new Host();host.Options=new Settings{DataDir=host.Dir,Key="unit-test-only",Model="unit-test-model",Endpoint=provider.Url};using var client=host.Client();var response=procedure?await client.PostAsJsonAsync("/agent/stored-procedures/prepare",new PrepareRequest(Fixtures.Analyze(),"single-user")):await client.PostAsJsonAsync("/analyze",Fixtures.Analyze());response.EnsureSuccessStatusCode();var result=await response.Content.ReadFromJsonAsync<AnalysisResponse>();Assert.Empty(result!.Candidates);Assert.Equal(suggestion.Sql,Assert.Single(result.Suggestions!).Sql);
 }
}
