using Alyvo.SsmsAiSqlAssistant;
using Xunit;
public class CallLineTests
{
 [Fact] public void UsesCallerDefinitionLinesAndPreservesRepeatedCalls()
 {
  var g=new MapGraph();var caller=new MapModule{Id=1,Schema="dbo",Name="parent",Type="P",Definition="CREATE PROC dbo.parent AS\nBEGIN\n EXEC dbo.child;\n EXEC dbo.child;\nEND"};g.Modules.Add(1,caller);g.Modules.Add(2,new MapModule{Id=2,Schema="dbo",Name="child",Type="P"});g.Edges.Add(new MapEdge{Caller=1,Callee=2,Schema="dbo",Entity="child"});CallEvidence.Enrich(g,caller);Assert.Equal("第 3 行 → [dbo].[child]；第 4 行 → [dbo].[child]",g.DownstreamCallLines(1));Assert.Equal("",g.DownstreamCallLines(2));
 }
 [Fact] public void UnknownLineIsNotReportedAsZeroAndViewsAreExcluded()
 {
  var g=new MapGraph();g.Modules.Add(2,new MapModule{Id=2,Schema="dbo",Name="child",Type="P"});g.Modules.Add(3,new MapModule{Id=3,Schema="dbo",Name="view",Type="V"});g.Edges.Add(new MapEdge{Caller=1,Callee=2});g.Edges.Add(new MapEdge{Caller=1,Callee=3,Line=2});Assert.Equal("行號未知 → [dbo].[child]",g.DownstreamCallLines(1));
 }
}
