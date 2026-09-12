using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alyvo.SsmsAiSqlAssistant;
using Newtonsoft.Json;
using Xunit;
public class IndexTrialTests
{
 [Theory][InlineData("DROP TABLE dbo.t;")][InlineData("CREATE INDEX ix ON dbo.other(id);")][InlineData("CREATE UNIQUE INDEX ix ON dbo.t(id);")][InlineData("CREATE INDEX ix ON dbo.t(id) WITH (DROP_EXISTING=ON);")][InlineData("CREATE INDEX ix ON dbo.t(id); DELETE dbo.t;")]
 public void UnsafeDdlRejected(string ddl)=>Assert.Throws<InvalidOperationException>(()=>IndexTrial.Parse("SELECT id FROM dbo.t;",ddl));
 [Fact] public void NonclusteredCoveringIndexAccepted(){Assert.Equal("ix",IndexTrial.Parse("SELECT id FROM dbo.t;","CREATE NONCLUSTERED INDEX ix ON dbo.t(id) INCLUDE(amount);").Name.Value);}
 [Fact] public void ComparisonRequiresResultsAndImprovement(){RunMetrics[] Samples(long reads,string digest)=>Enumerable.Range(0,3).Select(_=>new RunMetrics{Reads=reads,Digest=digest,ResultContract="columns",Rows=3,Shape="seek",Warnings=Array.Empty<string>()}).ToArray();Assert.Equal("",IndexTrial.Evaluate(Samples(100,"a"),Samples(10,"a")));Assert.NotEqual("",IndexTrial.Evaluate(Samples(100,"a"),Samples(100,"a")));Assert.NotEqual("",IndexTrial.Evaluate(Samples(100,"a"),Samples(10,"b")));}
 sealed class Callback: IProgress<string>{readonly Action<string> callback;public Callback(Action<string> action){callback=action;}public void Report(string value)=>callback(value);}
 [Fact] public async Task RealTrialRollbackCancellationAndExplicitCommit()
 {
  var context=new ConnectionContext{Server="localhost",Database="AlyvoStage5Demo1",TrustServerCertificate=true};var table="IndexTrial_"+Guid.NewGuid().ToString("N");var qualified="dbo.["+table+"]";
  async Task Execute(string sql){using(var c=context.Connect()){await c.OpenAsync();using(var cmd=c.CreateCommand()){cmd.CommandText=sql;cmd.CommandTimeout=30;await cmd.ExecuteNonQueryAsync();}}}
  async Task<int> Count(){using(var c=context.Connect()){await c.OpenAsync();using(var cmd=c.CreateCommand()){cmd.CommandText="SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(@table) AND name='IX_trial'";cmd.Parameters.AddWithValue("@table","dbo."+table);return (int)await cmd.ExecuteScalarAsync();}}}
  await Execute("CREATE TABLE "+qualified+" (Id int NOT NULL PRIMARY KEY, CustomerId int NOT NULL, Amount decimal(18,2) NOT NULL, Padding char(200) NOT NULL); INSERT "+qualified+" SELECT TOP (5000) ROW_NUMBER() OVER(ORDER BY (SELECT NULL)),ROW_NUMBER() OVER(ORDER BY (SELECT NULL))%1000,10,'x' FROM sys.all_objects a CROSS JOIN sys.all_objects b;");
  try{var query="SELECT Id,Amount FROM "+qualified+" WHERE CustomerId=123;";var ddl="CREATE NONCLUSTERED INDEX IX_trial ON "+qualified+"(CustomerId) INCLUDE(Amount);";
   using(var cancellation=new CancellationTokenSource()){await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>IndexTrial.Test(context,query,ddl,cancellation.Token,new Callback(s=>{if(s=="建立索引後 1/3")cancellation.Cancel();})));Assert.Equal(0,await Count());}
   var result=await IndexTrial.Test(context,query,ddl,CancellationToken.None);Assert.True(result.RolledBack);Assert.True(result.Eligible,result.Reason);Assert.Equal(0,await Count());Assert.Equal(3,result.Before.Length);Assert.Equal(3,result.After.Length);
   var tampered=JsonConvert.DeserializeObject<IndexTrialResult>(JsonConvert.SerializeObject(result));tampered.Ddl=ddl.Replace("CustomerId)","Id)");await Assert.ThrowsAsync<InvalidOperationException>(()=>IndexTrial.Apply(context,tampered,CancellationToken.None));Assert.Equal(0,await Count());
   await IndexTrial.Apply(context,result,CancellationToken.None);Assert.True(result.Applied);Assert.False(result.Eligible);Assert.Equal(1,await Count());await Assert.ThrowsAsync<InvalidOperationException>(()=>IndexTrial.Apply(context,result,CancellationToken.None));
  }finally{await Execute("DROP TABLE "+qualified+";");}
 }
}
