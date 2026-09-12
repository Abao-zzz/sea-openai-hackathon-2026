using System;
using System.IO;
using System.Linq;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Alyvo.SsmsAiSqlAssistant
{
 public static class Diagnostics
 {
  public static int ListenerCount,ActiveViews,SelectionEvents; public static bool PackageLoaded; public static string LastError="";
  public static readonly string DirectoryPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Alyvo","SsmsAiSqlAssistant","diagnostics");
  public static void Write(string activity)
  {
   try{System.IO.Directory.CreateDirectory(DirectoryPath);File.WriteAllText(Path.Combine(DirectoryPath,"mef-status.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new {utc=DateTimeOffset.UtcNow,packageLoaded=PackageLoaded,listenerCount=ListenerCount,activeViews=ActiveViews,selectionEvents=SelectionEvents,activity,lastError=LastError},Newtonsoft.Json.Formatting.Indented));ActivityLog.LogInformation("Alyvo.SsmsAiSqlAssistant",activity);}catch{ /* Diagnostics must never crash the editor. */ }
  }
  public static void Error(Exception e){LastError=e.GetType().Name;Write("操作失敗："+LastError);}
 }
 public static class SessionHub
 {
  public static bool Enabled=true;public static EditorSession Current;public static readonly List<EditorSession> Editors=new List<EditorSession>();
  public static event Action Changed;public static void Refresh(){Changed?.Invoke();}
 }
 [PackageRegistration(UseManagedResourcesOnly=true,AllowsBackgroundLoading=true)]
 [ProvideMenuResource("Menus.ctmenu",1)]
 [ProvideToolWindow(typeof(DetailsWindow),Style=VsDockStyle.Tabbed,Window="DocumentWell",Orientation=ToolWindowOrientation.Right,DockedWidth=500)]
 [ProvideToolWindow(typeof(MapWindow),Style=VsDockStyle.MDI)]
 [ProvideToolWindow(typeof(SpScanWindow),Style=VsDockStyle.MDI)]
 [ProvideAutoLoad(UIContextGuids80.NoSolution,PackageAutoLoadFlags.BackgroundLoad)]
 [Guid("7b7ca13c-4dd4-4eb9-a26f-021b0b5650bd")]
 public sealed class AssistantPackage:AsyncPackage
 {
  private MapIntegration mapIntegration;private SpScanControl scanControl; public static AssistantPackage Instance;private static readonly Guid Commands=new Guid("716bb7ef-65f4-4f5e-a23b-b6cb09ff8c6a");
  protected override async Task InitializeAsync(CancellationToken cancellationToken,IProgress<ServiceProgressData> progress)
  {
   await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);Instance=this;
   var service=(OleMenuCommandService)await GetServiceAsync(typeof(IMenuCommandService));
   foreach(var id in new[]{0x100,0x103}){var command=new OleMenuCommand((s,e)=>Toggle(),new CommandID(Commands,id));command.BeforeQueryStatus+=(s,e)=>command.Checked=SessionHub.Enabled;service.AddCommand(command);}
   service.AddCommand(new MenuCommand((s,e)=>ShowDetails(),new CommandID(Commands,0x101)));
   service.AddCommand(new MenuCommand((s,e)=>JoinableTaskFactory.RunAsync(MapIntegration.SaveEditorVersion),new CommandID(Commands,0x102)));
   service.AddCommand(new MenuCommand((s,e)=>new SettingsWindow().ShowDialog(),new CommandID(Commands,0x104)));
   mapIntegration=new MapIntegration(service);Diagnostics.PackageLoaded=true;Diagnostics.Write("Package load completed");
  }
  public void ShowMap(ConnectionContext context){ThreadHelper.ThrowIfNotOnUIThread();var pane=FindToolWindow(typeof(MapWindow),0,true);ErrorHandler.ThrowOnFailure(((IVsWindowFrame)pane.Frame).Show());JoinableTaskFactory.RunAsync(async()=>{try{await ((MapControl)pane.Content).Load(context);}catch(Exception e){MessageBox.Show(e.Message,"SP 呼叫地圖");}});}
  public void ShowScan(ConnectionContext context){ThreadHelper.ThrowIfNotOnUIThread();var pane=FindToolWindow(typeof(SpScanWindow),0,true);scanControl=(SpScanControl)pane.Content;ErrorHandler.ThrowOnFailure(((IVsWindowFrame)pane.Frame).Show());JoinableTaskFactory.RunAsync(async()=>{try{await scanControl.Load(context);}catch(Exception e){MessageBox.Show(e.Message,"SP 效能儀表板");}});}
  protected override void Dispose(bool disposing){if(disposing){mapIntegration?.Dispose();scanControl?.Dispose();}base.Dispose(disposing);}
  private void Toggle(){ThreadHelper.ThrowIfNotOnUIThread();SessionHub.Enabled=!SessionHub.Enabled;foreach(var editor in SessionHub.Editors.ToArray())editor.Render();var bar=(IVsStatusbar)GetService(typeof(SVsStatusbar));bar?.SetText(SessionHub.Enabled?"AI SQL 浮動模式已開啟：反白 SQL 會顯示一鍵優化卡；按卡片才會分析，不會自動送 AI。":"AI SQL 浮動模式已關閉：反白 SQL 不再跳出浮動卡，也不會自動分析。");}
  public void ShowDetails(){ThreadHelper.ThrowIfNotOnUIThread();var pane=FindToolWindow(typeof(DetailsWindow),0,true);ErrorHandler.ThrowOnFailure(((IVsWindowFrame)pane.Frame).Show());((DetailsControl)pane.Content).Refresh();}
  public static void EnsureLoaded(){ThreadHelper.JoinableTaskFactory.RunAsync(async()=>{await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();try{var shell=(IVsShell)Package.GetGlobalService(typeof(SVsShell));var id=new Guid("7b7ca13c-4dd4-4eb9-a26f-021b0b5650bd");shell.LoadPackage(ref id,out _);}catch(Exception e){Diagnostics.Error(e);}});}
 }
 [Guid("18428c52-526b-422a-ab87-d16b0aa71a03")]
 public sealed class DetailsWindow:ToolWindowPane
 {
  public DetailsWindow():base(null){Caption="AI SQL 詳細解析";Content=new DetailsControl();}
 }
}



