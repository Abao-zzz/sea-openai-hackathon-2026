using System;
using Alyvo.SsmsAiSqlAssistant;
using Xunit;
public class EditorLoadingTests
{
 [Fact] public void LoadedEditorDoesNotReloadPackage(){Assert.Equal(typeof(string),MapIntegration.ResolveEditorType(()=>typeof(string),()=>throw new Exception("Unexpected load")));}
 [Fact] public void ColdEditorLoadsPackageBeforeResolvingAgain(){bool loaded=false;int searches=0;Assert.Equal(typeof(string),MapIntegration.ResolveEditorType(()=>{searches++;return loaded?typeof(string):null;},()=>loaded=true));Assert.Equal(2,searches);}
 [Fact] public void FailedPackageLoadIsReported(){Assert.Throws<InvalidOperationException>(()=>MapIntegration.ResolveEditorType(()=>null,()=>throw new InvalidOperationException("Load failed")));Assert.Throws<InvalidOperationException>(()=>MapIntegration.ResolveEditorType(()=>null,()=>{}));}
}
