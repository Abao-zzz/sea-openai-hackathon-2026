using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Alyvo.SsmsAiSqlAssistant;
using Xunit;
public class IndexInlineTests
{
 [Fact] public void SpSuggestionsAreSeparateFromRewriteValidation()
 {
  var review=new SpReview{Analysis=new AnalysisDto{Candidates=Array.Empty<CandidateDto>(),Suggestions=new[]{new SuggestionDto{Sql="CREATE INDEX ix ON dbo.t(id)"}}}};
  Assert.True(review.SuggestionsOnly);Assert.False(review.PreflightPassed);Assert.False(review.Passed);
  review.Error="connection failed";Assert.False(review.SuggestionsOnly);
  review.Error="";review.Analysis.Candidates=new[]{new CandidateDto{Sql="SELECT id FROM dbo.t"}};Assert.False(review.SuggestionsOnly);
 }
 [Fact] public void NoSuggestionSummaryDoesNotMaskErrorsOrLocalFallback()
 {
  var state=new AnalysisState{Response=new AnalysisDto{AiProvider="openai",Candidates=Array.Empty<CandidateDto>()}};
  Assert.True(state.NoFurtherSuggestions);
  state.Error="failed";Assert.False(state.NoFurtherSuggestions);
  state.Error="";state.Response.AiProvider="local-rules";Assert.False(state.NoFurtherSuggestions);
  state.Response.AiProvider="openai";state.Response.Suggestions=new[]{new SuggestionDto{Sql="CREATE INDEX ix ON dbo.t(id)"}};Assert.False(state.NoFurtherSuggestions);
 }
 [Fact] public void BestVersionRequiresCompletedConsistentMeasurements()
 {
  RunMetrics[] Samples(long reads,string digest="same")=>Enumerable.Range(0,3).Select(_=>new RunMetrics{Reads=reads,Digest=digest,ResultContract="columns",Rows=1,Shape="seek",Warnings=Array.Empty<string>()}).ToArray();
  var result=new IndexTrialResult{RolledBack=true,Before=Samples(100),After=Samples(100)};
  Assert.True(IndexTrialControl.IsCurrentVersionBest(result));
  result.After=Samples(99);Assert.False(IndexTrialControl.IsCurrentVersionBest(result));
  result.After=Samples(100,"different");Assert.False(IndexTrialControl.IsCurrentVersionBest(result));
  result.After=Samples(100);result.RolledBack=false;Assert.False(IndexTrialControl.IsCurrentVersionBest(result));
 }
 [Fact] public void InlineViewIsReusedAndNoManualStartOrApplyInitially()
 {
  Exception failure=null;var thread=new Thread(()=>{try{
   var suggestion=new SuggestionDto{Sql="CREATE INDEX ix ON dbo.t(id);",Title="index"};
   var context=new ConnectionContext{Server="localhost",Database="demo"};
   var first=(StackPanel)Ui.Suggestion(suggestion,context,"SELECT id FROM dbo.t;");
   var parent=new StackPanel();parent.Children.Add(first);
   var second=Ui.Suggestion(suggestion,context,"SELECT id FROM dbo.t;");
   Assert.Same(first,second);Assert.Empty(parent.Children);
   var inline=Assert.Single(first.Children.OfType<IndexTrialControl>());
   var panel=(StackPanel)inline.Content;
   var buttons=panel.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().ToArray();
   Assert.Equal(Visibility.Collapsed,buttons[0].Visibility);
   Assert.Equal(Visibility.Collapsed,buttons[1].Visibility);
   Assert.False(buttons[1].IsEnabled);
   var unsupported=(StackPanel)Ui.Suggestion(new SuggestionDto{Sql="UPDATE STATISTICS dbo.t;"},context,"SELECT id FROM dbo.t;");
   Assert.Empty(unsupported.Children.OfType<IndexTrialControl>());
  }catch(Exception e){failure=e;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(failure!=null)throw failure;
 }
}
