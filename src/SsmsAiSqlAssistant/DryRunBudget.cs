using System;
using System.Globalization;

namespace Alyvo.SsmsAiSqlAssistant
{
 public sealed class DryRunBudget
 {
  public int TotalSeconds=300,QuerySeconds=90,MaxRows=250000,MaxBytes=64*1024*1024,MaxPlanBytes=2*1024*1024;
  public long MaxReads=1000000,MaxCpuMs=60000;
  private long reads,cpu,rows,bytes;
  public static DryRunBudget Load(Func<string,string> environment=null)
  {
   environment=environment??Environment.GetEnvironmentVariable;
   int Read(string name,int fallback,int min,int max)
   {
    var value=environment("ALYVO_DRY_RUN_"+name);
    if(string.IsNullOrWhiteSpace(value))return fallback;
    if(!int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var parsed)||parsed<min||parsed>max)
     throw new InvalidOperationException("ALYVO_DRY_RUN_"+name+" 必須介於 "+min+" 與 "+max+"。");
    return parsed;
   }
   var b=new DryRunBudget{TotalSeconds=Read("TOTAL_SECONDS",300,1,300),QuerySeconds=Read("QUERY_SECONDS",90,1,90),MaxRows=Read("MAX_ROWS",250000,1,250000),MaxBytes=Read("MAX_BYTES",64*1024*1024,1,64*1024*1024),MaxReads=Read("MAX_READS",1000000,1,1000000),MaxCpuMs=Read("MAX_CPU_MS",60000,1,60000),MaxPlanBytes=Read("MAX_PLAN_BYTES",2*1024*1024,1,2*1024*1024)};
   b.QuerySeconds=Math.Min(b.QuerySeconds,b.TotalSeconds);return b;
  }
  public void AddResult(long rowCount,long byteCount)
  {
   checked{rows+=rowCount;bytes+=byteCount;}
   if(rows>MaxRows)throw new InvalidOperationException("超過整批結果列數預算。");
   if(bytes>MaxBytes)throw new InvalidOperationException("超過整批結果位元組預算。");
  }
  public void AddSample(RunMetrics sample)
  {
   if(sample.Reads<0||sample.CpuMs<0)throw new InvalidOperationException("實測用量不可用。");
   checked{reads+=sample.Reads;cpu+=sample.CpuMs;}
   if(reads>MaxReads)throw new InvalidOperationException("超過整批 logical reads 預算。");
   if(cpu>MaxCpuMs)throw new InvalidOperationException("超過整批 CPU 預算。");
  }
 }
}
