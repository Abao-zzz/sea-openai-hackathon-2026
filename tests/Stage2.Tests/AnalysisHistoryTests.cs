using System;
using System.IO;
using System.Linq;
using Alyvo.SsmsAiSqlAssistant;
using Xunit;

public class AnalysisHistoryTests
{
 [Fact] public void SqlitePersistsMinimalStateAndDeletesWithoutRetainingSql()
 {
  var path=Path.Combine(Path.GetTempPath(),"AlyvoHistory_"+Guid.NewGuid().ToString("N"),"history.db");var store=new AnalysisHistory(path);var id=Guid.NewGuid().ToString("N");
  try{var entry=new AnalysisHistoryEntry{Id=id,DatabaseFingerprint=ConnectionContext.Hash("db"),SqlFingerprint=ConnectionContext.Hash("sql"),Stage="analysis",Verification="not-run",ApplyStatus="not-applied",Summary="SELECT secret FROM private",Model="SELECT secret",Time=DateTimeOffset.UtcNow};store.Save(entry);var read=new AnalysisHistory(path).List().Single();Assert.Equal("unknown-model",read.Model);Assert.DoesNotContain("secret",read.Summary);entry.Stage="apply";entry.ApplyStatus="applied";entry.Verification="passed";store.Save(entry);Assert.Single(store.List());Assert.Equal("applied",store.List()[0].ApplyStatus);Assert.DoesNotContain("secret",AnalysisHistory.Csv(store.List()));store.Delete(id);Assert.Empty(store.List());store.Save(entry);store.Delete();Assert.Empty(store.List());Assert.DoesNotContain("secret",System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)));}
  finally{if(Directory.Exists(Path.GetDirectoryName(path)))Directory.Delete(Path.GetDirectoryName(path),true);}
 }
}
