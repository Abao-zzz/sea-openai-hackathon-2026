using System.Net.Http.Json;
using System.Text.Json;
using LocalService;
using Xunit;
namespace LocalService.Tests;
public class IndexMetadataTests
{
 [Theory][InlineData(false)][InlineData(true)]
 public async Task BothAnalysisRoutesForwardReferencedSchemaAndIndexes(bool procedure)
 {
  await using var provider=await MockOpenAi.Start();using var host=new Host();host.Options=new Settings{DataDir=host.Dir,Key="unit-test-only",Model="unit-test-model",Endpoint=provider.Url};using var client=host.Client();
  var index=new IndexMetadata("IX_orders","NONCLUSTERED",false,false,false,"id > 0",[new("id",1,true)],["amount"]);var request=Fixtures.Analyze() with {Metadata=[new("dbo.orders",[new("id","int",false),new("amount","decimal(18,2)",true)],[index]),new("dbo.unreferenced",[new("private","int",false)],[index with{Name="private_index"}])]};
  var response=procedure?await client.PostAsJsonAsync("/agent/stored-procedures/prepare",new PrepareRequest(request,"single-user")):await client.PostAsJsonAsync("/analyze",request);response.EnsureSuccessStatusCode();
  using var envelope=JsonDocument.Parse(provider.Body);var input=envelope.RootElement.GetProperty("input").GetString()!;using var payload=JsonDocument.Parse(input[input.IndexOf('{')..]);var metadata=payload.RootElement.GetProperty("metadata");Assert.Equal(1,metadata.GetArrayLength());var sent=metadata[0];Assert.Equal("decimal(18,2)",sent.GetProperty("columns")[1].GetProperty("sqlType").GetString());var ix=sent.GetProperty("indexes")[0];Assert.Equal("IX_orders",ix.GetProperty("name").GetString());Assert.True(ix.GetProperty("keyColumns")[0].GetProperty("descending").GetBoolean());Assert.Equal("amount",ix.GetProperty("includedColumns")[0].GetString());Assert.Equal("id > 0",ix.GetProperty("filter").GetString());Assert.DoesNotContain("private_index",input);Assert.Contains("索引效益未實測",envelope.RootElement.GetProperty("instructions").GetString());
 }
 [Fact] public void OldRequestsRemainValid(){var r=Fixtures.Analyze();Contract.Check(r);Assert.Null(r.Metadata[0].Indexes);}
}
