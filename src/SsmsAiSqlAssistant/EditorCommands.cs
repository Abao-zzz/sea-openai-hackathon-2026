using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
namespace Alyvo.SsmsAiSqlAssistant
{
 // SSMS routes Tab through the native editor command chain before WPF KeyDown.
 public sealed class EditorCommands:IOleCommandTarget
 {
  private readonly EditorSession session;public IOleCommandTarget Next;
  public EditorCommands(EditorSession value){session=value;}
  private bool Handles(Guid group,uint id)=>group==VSConstants.VSStd2K && session.Analysis?.Preview==true && (id==(uint)VSConstants.VSStd2KCmdID.TAB||id==(uint)VSConstants.VSStd2KCmdID.CANCEL);
  public int QueryStatus(ref Guid group,uint count,OLECMD[] commands,IntPtr text)
  {var hr=Next?.QueryStatus(ref group,count,commands,text)??(int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;for(int i=0;i<count;i++)if(Handles(group,commands[i].cmdID)){commands[i].cmdf=(uint)(OLECMDF.OLECMDF_SUPPORTED|OLECMDF.OLECMDF_ENABLED);hr=VSConstants.S_OK;}return hr;}
  public int Exec(ref Guid group,uint id,uint options,IntPtr input,IntPtr output)
  {
   Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();if(Handles(group,id)){if(id==(uint)VSConstants.VSStd2KCmdID.TAB)session.Apply();else{session.Analysis.Preview=false;session.Render();SessionHub.Refresh();}return VSConstants.S_OK;}
   return Next?.Exec(ref group,id,options,input,output)??(int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
  }
 }
}

