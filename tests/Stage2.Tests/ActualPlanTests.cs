using System;
using System.IO;
using Alyvo.SsmsAiSqlAssistant;
using Xunit;

public class ActualPlanTests
{
 const string Plan="<ShowPlanXML xmlns='http://schemas.microsoft.com/sqlserver/2004/07/showplan'><RelOp PhysicalOp='Index Seek'/><MemoryGrantInfo GrantedMemory='1024'/><ParameterList><ColumnReference Column='@p' ParameterCompiledValue='1' ParameterRuntimeValue='2'/></ParameterList></ShowPlanXML>";
 [Fact] public void SummaryIncludesLocalRuntimeEvidence(){var s=ActualPlans.Describe(Plan);Assert.Contains("Seek：1",s);Assert.Contains("GrantedMemory=1024",s);Assert.Contains("ParameterRuntimeValue=2",s);}
 [Fact] public void RejectsXmlExternalEntities()=>Assert.ThrowsAny<Exception>(()=>ActualPlans.Describe("<!DOCTYPE x [<!ENTITY a SYSTEM 'file:///c:/secret'>]><x>&a;</x>"));
 [Fact] public void RetentionAndOversizeNeverPersistOversizeXml()
 {
  var old=ActualPlans.Root;var root=Path.Combine(Path.GetTempPath(),"AlyvoPlanTest_"+Guid.NewGuid().ToString("N"));ActualPlans.Root=root;
  try{var db=ConnectionContext.Hash("db");var q=ConnectionContext.Hash("q");for(int i=0;i<22;i++)ActualPlans.Save(db,q,Plan);Assert.Equal(20,Directory.GetFiles(Path.Combine(root,db),"*.json").Length);Assert.Equal(20,Directory.GetFiles(Path.Combine(root,db),"*.sqlplan").Length);var over=ActualPlans.Save(db,q,Plan,1);Assert.True(over.Oversize);Assert.Null(over.Path);Assert.Equal(20,Directory.GetFiles(Path.Combine(root,db),"*.json").Length);Assert.Equal(19,Directory.GetFiles(Path.Combine(root,db),"*.sqlplan").Length);Assert.Throws<InvalidOperationException>(()=>ActualPlans.Save("../escape",q,Plan));}
  finally{ActualPlans.Root=old;if(Directory.Exists(root))Directory.Delete(root,true);}
 }
}
