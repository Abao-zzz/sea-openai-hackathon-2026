using System;
using System.IO;
using Alyvo.SsmsAiSqlAssistant;
using Xunit;
public class BaselineVersionTests
{
 static VersionRepository Repo()=>new VersionRepository(Path.Combine(Path.GetTempPath(),"AlyvoBaseline",Guid.NewGuid().ToString("N")));
 static ConnectionContext Context()=>new ConnectionContext{Server="localhost",Database="demo"};
 static MapModule Module()=>new MapModule{Id=1,Schema="dbo",Name="p",Type="P",Definition="CREATE PROC dbo.p AS SELECT 1 AS n;"};
 [Fact] public void BaselineIsCreatedOnceAndNeverOverwritesObservedHistory(){var r=Repo();var c=Context();var m=Module();var first=r.EnsureBaseline(c,m);Assert.Equal(1,first.Number);Assert.Equal("基準快照",first.State);m.Definition="CREATE PROC dbo.p AS SELECT 2 AS n;";Assert.Equal(first.Sql,r.EnsureBaseline(c,m).Sql);Assert.Single(r.Load(c,m).Versions);Assert.Empty(r.Load(c,m).Audit);Assert.Equal(2,r.Create(c,m,m.Definition,"next").Number);}
 [Fact] public void ExistingVersionsAreNotRenumbered(){var r=Repo();var c=Context();var m=Module();var first=r.Create(c,m,m.Definition,"existing");Assert.Equal(first.Kind,r.EnsureBaseline(c,m).Kind);Assert.Single(r.Load(c,m).Versions);}
 [Fact] public void UnreadableDefinitionsAndViewsDoNotCreateFakeVersion(){var r=Repo();var c=Context();var m=Module();m.Definition=null;Assert.Throws<InvalidOperationException>(()=>r.EnsureBaseline(c,m));Assert.Empty(r.Load(c,m).Versions);m=Module();m.Type="V";Assert.Throws<InvalidOperationException>(()=>r.EnsureBaseline(c,m));}
 [Fact] public void BaselineCanBeRestoredAsNewVersion(){var r=Repo();var c=Context();var m=Module();var initial=r.EnsureBaseline(c,m);var next=r.Create(c,m,"ALTER PROC dbo.p AS SELECT 2 AS n;","change");var restore=r.CreateRestore(c,m,initial.Number,next.Sql,"restore baseline");Assert.Equal(3,restore.Number);Assert.Equal(initial.Hash,restore.Hash);Assert.Equal(next.Hash,restore.BaseHash);Assert.Equal(3,r.Load(c,m).Versions.Count);}
}
