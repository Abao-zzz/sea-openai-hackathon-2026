using System;
using System.IO;
using System.Linq;
using Alyvo.SsmsAiSqlAssistant;
using Xunit;
public class RestoreVersionTests
{
 [Fact] public void RestoreCreatesNewVersionWithSelectedContentAndCurrentRollback()
 {
  var repo=new VersionRepository(Path.Combine(Path.GetTempPath(),"AlyvoRestore",Guid.NewGuid().ToString("N")));var c=new ConnectionContext{Server="localhost",Database="demo"};var m=new MapModule{Id=1,Schema="dbo",Name="p",Type="P",Definition="CREATE PROC dbo.p AS SELECT 0 AS n;"};
  var v1=repo.Create(c,m,"CREATE PROC dbo.p AS SELECT 1 AS n;","first");var v2=repo.Create(c,m,"CREATE PROC dbo.p AS SELECT 2 AS n;","second");
  var v3=repo.CreateRestore(c,m,v1.Number,v2.Sql,"restore first");Assert.Equal(3,v3.Number);Assert.Equal(v1.Hash,v3.Hash);Assert.Equal(v2.Hash,v3.BaseHash);Assert.Equal(v1.Number,v3.RestoredFromVersion);Assert.Equal("未套用",v3.State);Assert.Equal("還原",v3.Kind);Assert.Equal(v2.Hash,ProcedureSource.Parse(v3.DownSql,c.Database).Hash);
  var v4=repo.CreateRestore(c,m,v1.Number,v2.Sql,"restore again");Assert.Equal(4,v4.Number);var history=repo.Load(c,m);Assert.Equal(4,history.Versions.Count);Assert.Equal(v1.Sql,history.Versions[0].Sql);Assert.Equal(v2.Sql,history.Versions[1].Sql);Assert.Empty(history.Audit);
 }
 [Fact] public void WrongIdentityAndMissingReasonDoNotCreateVersion()
 {
  var repo=new VersionRepository(Path.Combine(Path.GetTempPath(),"AlyvoRestore",Guid.NewGuid().ToString("N")));var c=new ConnectionContext{Server="localhost",Database="demo"};var m=new MapModule{Id=1,Schema="dbo",Name="p",Type="P",Definition="CREATE PROC dbo.p AS SELECT 1;"};repo.Create(c,m,m.Definition,"first");
  Assert.Throws<InvalidOperationException>(()=>repo.CreateRestore(c,m,1,"CREATE PROC dbo.other AS SELECT 1;","wrong"));Assert.Throws<InvalidOperationException>(()=>repo.CreateRestore(c,m,1,m.Definition," "));Assert.Single(repo.Load(c,m).Versions);
 }
}
