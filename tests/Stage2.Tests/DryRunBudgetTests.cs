using System;
using Alyvo.SsmsAiSqlAssistant;
using Xunit;

public class DryRunBudgetTests
{
 [Fact] public void SpecificationDefaultsAndQueryCannotExceedTotal()
 {
  var b=DryRunBudget.Load(_=>null);Assert.Equal(300,b.TotalSeconds);Assert.Equal(90,b.QuerySeconds);Assert.Equal(250000,b.MaxRows);Assert.Equal(67108864,b.MaxBytes);Assert.Equal(1000000,b.MaxReads);Assert.Equal(60000,b.MaxCpuMs);Assert.Equal(2097152,b.MaxPlanBytes);
  Assert.Equal(2,DryRunBudget.Load(k=>k=="ALYVO_DRY_RUN_TOTAL_SECONDS"?"2":null).QuerySeconds);
 }
 [Theory][InlineData("0")][InlineData("-1")][InlineData("301")][InlineData("wrong")]
 public void InvalidEnvironmentFailsClosed(string value)=>Assert.Throws<InvalidOperationException>(()=>DryRunBudget.Load(k=>k=="ALYVO_DRY_RUN_TOTAL_SECONDS"?value:null));
 [Fact] public void LimitsApplyAcrossSamples()
 {
  var b=new DryRunBudget{MaxRows=3,MaxBytes=10,MaxReads=5,MaxCpuMs=5};b.AddResult(2,5);Assert.Throws<InvalidOperationException>(()=>b.AddResult(2,5));
  b.AddSample(new RunMetrics{Reads=3,CpuMs=2});Assert.Throws<InvalidOperationException>(()=>b.AddSample(new RunMetrics{Reads=3,CpuMs=2}));
  Assert.Throws<InvalidOperationException>(()=>new DryRunBudget().AddSample(new RunMetrics{Reads=-1}));
 }
}
