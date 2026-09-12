using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel.Design;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Sdk.Sfc;
using Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer;
using Microsoft.VisualStudio.Shell;

namespace Alyvo.SsmsAiSqlAssistant
{
 public sealed class MapEntry : ToolsMenuItemBase, IMenuItem
 {
  public MapEntry(){Text="AI 建立 SP 呼叫地圖";MultiSelect=false;}
  public new string Name=>"Alyvo.SpCallMap";
  public new Guid CommandGuid=>new Guid("21fa7c14-5aa9-4a63-87ef-64a3eaabebd3");public new int ItemId=>0x300;
  public new EventHandler MenuHandler=>(s,e)=>{try{AssistantPackage.Instance.ShowMap(MapIntegration.Capture(Parent));}catch(Exception ex){MessageBox.Show(ex.Message,"SP 呼叫地圖");}};
  public override void UpdateMenuCommandStatus(MenuCommand command){command.Enabled=command.Visible=Parent!=null;}
  protected override void Invoke(){MenuHandler(this,EventArgs.Empty);}
  public override object Clone()=>new MapEntry{Parent=Parent};
 }
 public sealed class MapIntegration:IDisposable
 {
  readonly DispatcherTimer timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(150)};
  readonly ConditionalWeakTable<object,MapEntry> attached=new ConditionalWeakTable<object,MapEntry>();
  string lastFailure=""; readonly HashSet<System.Windows.Forms.TreeView> trees=new HashSet<System.Windows.Forms.TreeView>();
  public MapIntegration(IMenuCommandService commands){var command=new MenuCommand((s,e)=>{try{var service=(IObjectExplorerService)Package.GetGlobalService(typeof(IObjectExplorerService));service.GetSelectedNodes(out var count,out var nodes);if(count!=1)throw new InvalidOperationException("請選取單一 database node。");AssistantPackage.Instance.ShowMap(Capture(nodes[0]));}catch(Exception error){MessageBox.Show(error.Message,"SP 呼叫地圖");}},new CommandID(new Guid("21fa7c14-5aa9-4a63-87ef-64a3eaabebd3"),0x300));commands.AddCommand(command);timer.Tick+=Tick;timer.Start();}
  void Selected(object sender,System.Windows.Forms.TreeViewEventArgs e)=>Attach();
  void Clicked(object sender,System.Windows.Forms.TreeNodeMouseClickEventArgs e)=>Attach();
  void Tick(object sender,EventArgs e)=>Attach();
  void Attach()
  {
   ThreadHelper.ThrowIfNotOnUIThread();
   try{
    var service=Package.GetGlobalService(typeof(IObjectExplorerService)) as IObjectExplorerService;if(service==null)return;
    service.GetSelectedNodes(out var count,out var nodes);if(count!=1||nodes==null)return;var node=nodes[0];
    var tree=(node as System.Windows.Forms.TreeNode)?.TreeView;if(tree!=null&&trees.Add(tree)){tree.AfterSelect+=Selected;tree.NodeMouseClick+=Clicked;}
    if(new Urn(node.Context).Type!="Database")return;
    var handler=node.GetService(typeof(IMenuHandler));if(handler==null||attached.TryGetValue(handler,out _))return;
    var item=new MapEntry{Parent=node};var add=handler.GetType().GetMethod("AddChild",new[]{typeof(string),typeof(object)});if(add==null)throw new InvalidOperationException("SSMS Object Explorer menu handler 不支援 AddItem。");
    add.Invoke(handler,new object[]{"MenuItem",item});add.Invoke(handler,new object[]{"MenuItem",new ScanEntry{Parent=node}});attached.Add(handler,item);Diagnostics.Write("SP map and scan database context menus attached");
   }catch(Exception e){var failure=e.GetBaseException().GetType().Name+": "+e.GetBaseException().Message;if(failure!=lastFailure){lastFailure=failure;Microsoft.VisualStudio.Shell.ActivityLog.LogError("Alyvo.SpMap",failure+"\n"+e.StackTrace);Diagnostics.Error(e);}}
  }
  public static ConnectionContext Capture(INodeInformation node)
  {
   ThreadHelper.ThrowIfNotOnUIThread();if(node==null)throw new InvalidOperationException("請選取 database node。");var urn=new Urn(node.Context);if(urn.Type!="Database")throw new InvalidOperationException("只能從 database node 建立地圖。");
   var database=urn.GetAttribute("Name","Database");var info=node.Connection as SqlConnectionInfo;if(info==null||string.IsNullOrEmpty(database))throw new InvalidOperationException("無法取得 exact Object Explorer 連線。");
   if(info.StrictEncryption)throw new InvalidOperationException("目前 SQL client 不支援 Strict Encryption；已拒絕降級連線。");
   var result=new ConnectionContext{Server=info.ServerName,Database=database,Encrypt=info.EncryptConnection,TrustServerCertificate=info.TrustServerCertificate,Integrated=info.UseIntegratedSecurity};
   if(!result.Integrated){if(info.Authentication.ToString()!="SqlPassword"&&info.Authentication.ToString()!="NotSpecified")throw new InvalidOperationException("地圖目前支援 Windows / SQL Server 認證。");var password=info.SecurePassword?.Copy();if(password==null)throw new InvalidOperationException("無法安全取得 SQL 認證。");password.MakeReadOnly();result.Credential=new SqlCredential(info.UserName,password);}return result;
  }
  public void Dispose(){timer.Stop();timer.Tick-=Tick;foreach(var tree in trees){tree.AfterSelect-=Selected;tree.NodeMouseClick-=Clicked;}trees.Clear();}
  public static string QueryText(ConnectionContext context,MapModule module,string definition=null){if(!module.IsProcedure)throw new InvalidOperationException("此物件是 View，不能當作 SP 開啟。");if((definition??module.Definition)==null)throw new InvalidOperationException("SQL Server 沒有回傳這支 SP 的完整定義。");return "USE "+DbWorker.Quote(context.Database)+";\r\nGO\r\n"+(definition??module.Definition).Replace("\r\n","\n").Replace("\r","\n").Replace("\n","\r\n");}
  public static void OpenQuery(ConnectionContext context,MapModule module,string definition=null)
  {
   if(definition==null){ThreadHelper.JoinableTaskFactory.RunAsync(async()=>{try{var current=await ReadCurrentDefinition(context,module);await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();OpenQuery(context,module,current);}catch(Exception error){await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();MessageBox.Show(error.Message,"讀取資料庫目前 SP");}});return;}
   ThreadHelper.ThrowIfNotOnUIThread();var sql=QueryText(context,module,definition);
   var type=ResolveEditorType(()=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.Editors.ScriptFactory",false)).FirstOrDefault(t=>t!=null),()=>{
    var shell=Package.GetGlobalService(typeof(Microsoft.VisualStudio.Shell.Interop.SVsShell)) as Microsoft.VisualStudio.Shell.Interop.IVsShell;
    if(shell==null)throw new InvalidOperationException("無法取得 SSMS Shell 服務。");
    // Registered by SSMS 22 Extensions/Application/SQLEditors.pkgdef.
    var packageId=new Guid("4058755A-8FBE-41C7-BC99-3DBF5C74BA62");Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(shell.LoadPackage(ref packageId,out var editorPackage));
   });
   var factory=type.GetProperty("Instance").GetValue(null);var method=type.GetMethods().Single(m=>m.Name=="CreateNewScript"&&m.GetParameters().Length==4&&m.GetParameters()[1].ParameterType.Name=="UIConnectionInfo");var info=Activator.CreateInstance(method.GetParameters()[1].ParameterType);
   void Set(string name,object value)=>info.GetType().GetProperty(name).SetValue(info,value);
   Set("ServerType",new Guid("8c91a03d-f9b4-46c0-a305-b5dcc79ff907"));Set("ServerName",context.Server);Set("AuthenticationType",context.Integrated?0:1);Set("PersistPassword",false);
   if(!context.Integrated){Set("UserName",context.Credential.UserId);Set("InMemoryPassword",context.Credential.Password.Copy());}
   var options=(NameValueCollection)info.GetType().GetProperty("AdvancedOptions").GetValue(info);options["DATABASE"]=context.Database;options["ENCRYPT_CONNECTION"]=context.Encrypt.ToString();options["TRUST_SERVER_CERTIFICATE"]=context.TrustServerCertificate.ToString();
   var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Alyvo","SsmsAiSqlAssistant","map-open-scripts");Directory.CreateDirectory(folder);var path=Path.Combine(folder,Guid.NewGuid().ToString("N")+".sql");File.WriteAllText(path,sql);
   method.Invoke(factory,new object[]{path,info,null,module.Name});Diagnostics.Write("SP map opened exact object id "+module.Id);
  }
  public static Type ResolveEditorType(Func<Type> find,Action load)
  {
   var type=find();if(type!=null)return type;load();return find()??throw new InvalidOperationException("SSMS SQL Editor 套件已載入，但找不到 ScriptFactory 服務。請檢查 SSMS 安裝。");
  }
  public static async Task<string> ReadCurrentDefinition(ConnectionContext context,MapModule module)
  {
   using(var connection=context.Connect()){await connection.OpenAsync();using(var command=new SqlCommand("SELECT sm.definition FROM sys.sql_modules sm JOIN sys.objects o ON o.object_id=sm.object_id JOIN sys.schemas s ON s.schema_id=o.schema_id WHERE o.object_id=@id AND s.name=@schema AND o.name=@name AND o.type='P';",connection){CommandTimeout=15}){command.Parameters.AddWithValue("@id",module.Id);command.Parameters.AddWithValue("@schema",module.Schema);command.Parameters.AddWithValue("@name",module.Name);var sql=await command.ExecuteScalarAsync() as string;if(sql==null)throw new InvalidOperationException("無法讀取目前 SP 定義；物件可能已移除、變更或缺少權限。請重新掃描。");return sql;}}
  }
  public static async Task SaveEditorVersion()
  {
   await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();try{var editor=SessionHub.Current;if(editor==null||editor.View.IsClosed)throw new InvalidOperationException("請先開啟完整 SP Query Editor。");var sql=editor.View.TextSnapshot.GetText();var context=ConnectionContext.Capture();var source=ProcedureSource.Parse(sql,context.Database);var reason=MapControl.AskReason();if(reason==null)return;
    var graph=await new MapCollector().Load(context,System.Threading.CancellationToken.None);var module=graph.Modules.Values.SingleOrDefault(m=>m.Schema==source.Schema&&m.Name==source.Name&&m.IsProcedure)??throw new InvalidOperationException("目前資料庫找不到這支 SP。");var version=new VersionRepository().Create(context,module,sql,reason);MapControl.NotifyVersionChanged();MessageBox.Show("V"+version.Number+" "+version.State,"建立目前 SP 版本");
   }catch(Exception e){MessageBox.Show(e.Message,"建立目前 SP 版本");}
  }
 }
}
