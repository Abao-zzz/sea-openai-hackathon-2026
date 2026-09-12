using System.Linq;
using Alyvo.SsmsAiSqlAssistant;
using Xunit;
public class SqlComparisonTests
{
 [Fact] public void IdenticalLinesAndCrLfHaveNoChanges(){Assert.All(SqlComparison.Compare("a\r\nb","a\nb"),x=>Assert.False(x.Changed));}
 [Fact] public void InsertionKeepsFollowingLinesAligned(){var rows=SqlComparison.Compare("a\nb","a\nnew\nb");Assert.Single(rows.Where(x=>x.Changed));Assert.Equal("2  b",rows.Last().OldText);Assert.Equal("3  b",rows.Last().NewText);}
 [Fact] public void ReplacementShowsBothOldAndNew(){var rows=SqlComparison.Compare("SELECT old\nFROM t","SELECT new\nFROM t");Assert.Equal(2,rows.Count(x=>x.Changed));Assert.Contains(rows,x=>x.OldText.Contains("SELECT old")&&x.NewText=="");Assert.Contains(rows,x=>x.NewText.Contains("SELECT new")&&x.OldText=="");Assert.False(rows.Last().Changed);}
 [Fact] public void WhitespaceChangesRemainVisible(){Assert.Contains(SqlComparison.Compare(" SELECT 1","SELECT 1"),x=>x.Changed);}
 [Fact] public void LargeDefinitionsPreserveEveryLine(){var rows=SqlComparison.Compare(string.Join("\n",Enumerable.Repeat("old",1500)),string.Join("\n",Enumerable.Repeat("new",1500)));Assert.Equal(1500,rows.Count(x=>x.OldText!=""));Assert.Equal(1500,rows.Count(x=>x.NewText!=""));}
}
