using System;
using System.ComponentModel.Design;
using System.Windows;
using Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer;
namespace Alyvo.SsmsAiSqlAssistant
{
 public sealed class ScanEntry:ToolsMenuItemBase,IMenuItem
 {
  public ScanEntry(){Text="AI 掃描高成本 SP";MultiSelect=false;}
  public new string Name=>"Alyvo.HighCostSpScan";
  public new Guid CommandGuid=>new Guid("21fa7c14-5aa9-4a63-87ef-64a3eaabebd3");public new int ItemId=>0x301;
  public new EventHandler MenuHandler=>(s,e)=>{try{AssistantPackage.Instance.ShowScan(MapIntegration.Capture(Parent));}catch(Exception ex){MessageBox.Show(ex.Message,"SP 效能儀表板");}};
  public override void UpdateMenuCommandStatus(MenuCommand command){command.Enabled=command.Visible=Parent!=null;}
  protected override void Invoke(){MenuHandler(this,EventArgs.Empty);}
  public override object Clone()=>new ScanEntry{Parent=Parent};
 }
}
