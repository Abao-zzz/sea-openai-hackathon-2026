using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;
namespace Alyvo.SsmsAiSqlAssistant
{
 [Export(typeof(IWpfTextViewCreationListener))][ContentType("text")][TextViewRole(PredefinedTextViewRoles.Document)]
 public sealed class SelectionListener:IWpfTextViewCreationListener
 {
  [Export(typeof(AdornmentLayerDefinition))][Name("Alyvo.SqlAssistant")][Order(After=PredefinedAdornmentLayers.Caret)]public AdornmentLayerDefinition Layer;
  [Import]public IEditorOperationsFactoryService Operations;
  [Import]public Microsoft.VisualStudio.Editor.IVsEditorAdaptersFactoryService Adapters;
  public void TextViewCreated(IWpfTextView view){try{if(view.Properties.ContainsProperty(typeof(EditorSession)))return;Diagnostics.ListenerCount++;var session=new EditorSession(view,Operations.GetEditorOperations(view),Adapters.GetViewAdapter(view));view.Properties.AddProperty(typeof(EditorSession),session);Diagnostics.Write("MEF IWpfTextViewCreationListener created");AssistantPackage.EnsureLoaded();}catch(Exception e){Diagnostics.Error(e);}}
 }
 public sealed class EditorSession
 {
  public readonly IWpfTextView View;private readonly IAdornmentLayer layer;private readonly IEditorOperations operations;private readonly DispatcherTimer timer;private readonly Microsoft.VisualStudio.TextManager.Interop.IVsTextView adapter;private readonly EditorCommands commands;
  private readonly Border card;private readonly TextBlock status;private readonly Button analyze,details;private readonly ProgressBar progress;private readonly StackPanel preview;
  private string hidden="";private bool editing;private ConnectionContext context;private int appliedVersion,appliedStart;private string appliedSql,restoreSql,appliedContext;
  public AnalysisState Analysis;public string Message="";private readonly DbWorker worker=new DbWorker();private readonly LocalApi api=new LocalApi();
  public string Sql=>View.IsClosed?"":View.Selection.StreamSelectionSpan.SnapshotSpan.GetText();
  public string SelectionKey=>View.TextSnapshot.Version.VersionNumber+":"+View.Selection.StreamSelectionSpan.SnapshotSpan.Start.Position+":"+Sql.Length+":"+ConnectionContext.Hash(Sql);
  public EditorSession(IWpfTextView view,IEditorOperations editorOperations,Microsoft.VisualStudio.TextManager.Interop.IVsTextView viewAdapter)
  {
   View=view;operations=editorOperations;adapter=viewAdapter??throw new InvalidOperationException("找不到 SQL Editor 命令鏈。");commands=new EditorCommands(this);Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(adapter.AddCommandFilter(commands,out commands.Next));layer=view.GetAdornmentLayer("Alyvo.SqlAssistant");status=Ui.Text("",12,"#4B5563");status.Margin=new Thickness(0,6,0,0);
   var panel=new StackPanel();var row=new DockPanel{LastChildFill=false};var label=Ui.Text("AI SQL",10,"#2563EB");label.FontWeight=FontWeights.SemiBold;label.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(label,Dock.Left);row.Children.Add(label);
   var close=Ui.Button("X",()=>{hidden=SelectionKey;Render();});DockPanel.SetDock(close,Dock.Right);row.Children.Add(close);
   details=Ui.Button("詳細",()=>{SessionHub.Current=this;AssistantPackage.Instance?.ShowDetails();});DockPanel.SetDock(details,Dock.Right);row.Children.Add(details);
   analyze=Ui.Button("一鍵優化",()=>StartAnalysis(),true);analyze.Background=Ui.Brush("#1F2937");DockPanel.SetDock(analyze,Dock.Right);row.Children.Add(analyze);
   panel.Children.Add(row);panel.Children.Add(status);progress=new ProgressBar{IsIndeterminate=true,Height=3,Margin=new Thickness(0,8,0,0),Visibility=Visibility.Collapsed};panel.Children.Add(progress);
   preview=new StackPanel{Visibility=Visibility.Collapsed};panel.Children.Add(preview);
   card=new Border{Width=368,MinHeight=72,Background=Brushes.White,BorderBrush=Ui.Brush("#D1D5DB"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(4),Padding=new Thickness(12),Child=panel,UseLayoutRounding=true,SnapsToDevicePixels=true};
   SessionHub.Editors.Add(this);Diagnostics.ActiveViews++;view.Selection.SelectionChanged+=SelectionChanged;view.LayoutChanged+=LayoutChanged;view.Closed+=Closed;view.GotAggregateFocus+=Focused;view.TextBuffer.Changed+=BufferChanged;view.VisualElement.PreviewKeyDown+=KeyDown;
   timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};timer.Tick+=ContextTick;timer.Start();Render();
  }
  private void Focused(object s,EventArgs e){SessionHub.Current=this;RefreshConnection();SessionHub.Refresh();}
  private void ContextTick(object s,EventArgs e){if(SessionHub.Current==this)RefreshConnection();}
  private void RefreshConnection(){if(View.IsClosed||!View.HasAggregateFocus)return;try{var current=ConnectionContext.Capture();if(context!=null&&context.Fingerprint!=current.Fingerprint)Invalidate("資料庫連線已變更，請重新分析。");context=current;}catch{/* No active SQL connection: analyze/apply will explicitly reject. */}}
  private void Invalidate(string reason){if(Analysis!=null){Analysis.Error=reason;Analysis.Preview=false;Analysis.Cancellation?.Cancel();}Message=reason;Render();SessionHub.Refresh();}
  private void BufferChanged(object s,TextContentChangedEventArgs e){if(!editing)Invalidate("編輯器內容已變更，請重新分析。");}
  private void SelectionChanged(object s,EventArgs e)
  {
   Diagnostics.SelectionEvents++;SessionHub.Current=this;Diagnostics.Write("Selection changed (SQL omitted)");RefreshConnection();if(!editing&&Analysis!=null&&!View.Selection.IsEmpty&&!Matches(Analysis,false))Invalidate("選取範圍已變更，請重新分析。");Render();SessionHub.Refresh();
  }
  private void LayoutChanged(object s,TextViewLayoutChangedEventArgs e)=>Render();
  private void Closed(object s,EventArgs e)
  {
   Analysis?.Cancellation?.Cancel();adapter.RemoveCommandFilter(commands);timer.Stop();timer.Tick-=ContextTick;View.Selection.SelectionChanged-=SelectionChanged;View.LayoutChanged-=LayoutChanged;View.Closed-=Closed;View.GotAggregateFocus-=Focused;View.TextBuffer.Changed-=BufferChanged;View.VisualElement.PreviewKeyDown-=KeyDown;layer.RemoveAllAdornments();SessionHub.Editors.Remove(this);Diagnostics.ActiveViews--;if(SessionHub.Current==this)SessionHub.Current=null;Diagnostics.Write("Editor closed; subscriptions removed");SessionHub.Refresh();
  }
  public bool Matches(AnalysisState a,bool checkConnection=true)
  {
   if(View.IsClosed||View.Selection.IsEmpty)return false;var span=View.Selection.StreamSelectionSpan.SnapshotSpan;
   if(!a.MatchesSelection(span.Snapshot.Version.VersionNumber,span.Start.Position,span.Length,span.GetText(),a.ConnectionFingerprint))return false;
   if(checkConnection){try{return ConnectionContext.Capture().Fingerprint==a.ConnectionFingerprint;}catch{return false;}}return true;
  }
  public async void StartAnalysis()
  {
   if(Analysis?.Busy==true)return;AnalysisState a=null;
   try
   {
    if(string.IsNullOrWhiteSpace(Sql))throw new InvalidOperationException("請先反白 SQL。");context=ConnectionContext.Capture();var span=View.Selection.StreamSelectionSpan.SnapshotSpan;
    a=new AnalysisState{Original=Sql,OriginalHash=ConnectionContext.Hash(Sql),Start=span.Start.Position,Length=span.Length,Version=span.Snapshot.Version.VersionNumber,Connection=context,ConnectionFingerprint=context.Fingerprint,Busy=true,Status="分析中...",Cancellation=new CancellationTokenSource()};Analysis=a;hidden="";Message="";Render();SessionHub.Current=this;AssistantPackage.Instance?.ShowDetails();SessionHub.Refresh();
    a.Payload=await Task.Run(()=>worker.BuildPayload(a.Original,a.Connection,a.Cancellation.Token));a.Response=await api.Analyze(a.Payload,a.Cancellation.Token);AnalysisHistory.Record(a,"analysis");
    if(a.Response.Candidates.Length>0){a.Status="安全預檢中...";SessionHub.Refresh();a.Preflight=await Task.Run(()=>worker.Preflight(a.Connection,a.Original,a.Candidate.Sql,a.Cancellation.Token));}
    a.Status=a.Response.AiProvider=="local-rules"?"local-rules：未設定 OpenAI，不產生 AI 候選。":a.NoFurtherSuggestions?"目前沒有進一步優化建議。":a.Response.Candidates.Length==0?"SQL 無需改寫，正在顯示索引建議。":a.Preflight?.Passed==true?"六關安全預檢通過，可建立 Snapshot。":"安全預檢未通過，不可套用。";
    AnalysisHistory.Record(a,"preflight");if(!Matches(a))a.Error="編輯器或連線已變更，請重新分析。";
   }
   catch(Exception e){if(a!=null){a.Error=DbWorker.SafeError(e);a.Status=e is OperationCanceledException?"分析已取消":"分析失敗";}else Message=DbWorker.SafeError(e);Diagnostics.Error(e);}
   finally{if(a!=null)a.Busy=false;Render();SessionHub.Refresh();}
  }
  public async void SelectCandidate(int index)
  {
   var a=Analysis;if(a==null||a.Busy||index==a.CandidateIndex)return;
   try{if(!Matches(a))throw new InvalidOperationException("選取範圍已變更，請重新分析。");a.CandidateIndex=index;a.Preflight=null;a.DryRun=null;a.Preview=false;a.Busy=true;a.Status="安全預檢中...";SessionHub.Refresh();a.Preflight=await Task.Run(()=>worker.Preflight(a.Connection,a.Original,a.Candidate.Sql,a.Cancellation.Token));a.Status=a.Preflight.Passed?"六關安全預檢通過，可建立 Snapshot。":"安全預檢未通過，不可套用。";}
   catch(Exception e){a.Error=DbWorker.SafeError(e);}finally{a.Busy=false;Render();SessionHub.Refresh();}
  }
  public async void StartDryRun()
  {
   var a=Analysis;if(a==null||a.Busy)return;
   try{if(!Matches(a)||a.Preflight?.Passed!=true)throw new InvalidOperationException("預檢未通過或選取已變更，不能 Dry Run。");a.Cancellation?.Dispose();a.Cancellation=new CancellationTokenSource();a.Busy=true;a.Status="建立 Snapshot...";SessionHub.Refresh();Render();var updates=new Progress<string>(text=>{a.Status=text;SessionHub.Refresh();Render();});a.DryRun=await Task.Run(()=>worker.DryRun(a.Connection,a.Original,a.Candidate.Sql,a.Preflight,a.Cancellation.Token,updates));a.Status=a.DryRun.Status;AnalysisHistory.Record(a,"snapshot");if(!Matches(a))a.Error="Dry Run 期間編輯器或連線已變更，請重新分析。";a.Preview=a.DryRun.Passed&&string.IsNullOrEmpty(a.Error);}
   catch(Exception e){a.Error=DbWorker.SafeError(e);a.Status="Dry Run 失敗";}finally{a.Busy=false;Render();SessionHub.Refresh();}
  }
  public void Cancel(){if(Analysis?.Busy==true){Analysis.Status="取消中...";Analysis.Cancellation.Cancel();Render();SessionHub.Refresh();}}
  public void Apply()
  {
   var a=Analysis;try{if(a?.CanApply!=true||!Matches(a))throw new InvalidOperationException("套用已拒絕：selection、buffer version、原文或資料庫已變更。");editing=true;appliedStart=a.Start;restoreSql=a.Original;appliedSql=a.Candidate.Sql;appliedContext=a.ConnectionFingerprint;if(!operations.InsertText(appliedSql))throw new InvalidOperationException("編輯器拒絕替換。");appliedVersion=View.TextSnapshot.Version.VersionNumber;a.Applied=true;a.Preview=false;a.Status="已套用至編輯器；未執行 SQL。可按復原或使用 Ctrl+Z。";AnalysisHistory.Record(a,"apply","applied");Diagnostics.Write("Editor-only apply completed");}
   catch(Exception e){Message=DbWorker.SafeError(e);if(a!=null)a.Error=Message;}finally{editing=false;Render();SessionHub.Refresh();}
  }
  public bool CanRestore=>appliedSql!=null&&!View.IsClosed&&View.TextSnapshot.Version.VersionNumber==appliedVersion;
  public void Restore()
  {
   try{if(!CanRestore||ConnectionContext.Capture().Fingerprint!=appliedContext||View.TextSnapshot.GetText(appliedStart,appliedSql.Length)!=appliedSql)throw new InvalidOperationException("編輯器或資料庫已變更，拒絕覆蓋；請使用編輯器復原歷程。");editing=true;View.Selection.Select(new SnapshotSpan(View.TextSnapshot,appliedStart,appliedSql.Length),false);if(!operations.InsertText(restoreSql))throw new InvalidOperationException("編輯器拒絕復原。");AnalysisHistory.Record(Analysis,"restore","restored");appliedSql=null;Analysis=null;Message="已復原原始選取文字；未執行 SQL。";Diagnostics.Write("Editor-only restore completed");}
   catch(Exception e){Message=DbWorker.SafeError(e);}finally{editing=false;Render();SessionHub.Refresh();}
  }
  private void KeyDown(object sender,KeyEventArgs e){if(Analysis?.Preview==true){if(e.Key==Key.Tab){e.Handled=true;Apply();}else if(e.Key==Key.Escape){e.Handled=true;Analysis.Preview=false;Render();SessionHub.Refresh();}}}
  public void Render()
  {
   if(View.IsClosed)return;try
   {
    layer.RemoveAllAdornments();var sql=Sql;if(!SessionHub.Enabled||View.Selection.IsEmpty||string.IsNullOrWhiteSpace(sql)||SelectionKey==hidden)return;
    var a=Analysis;bool same=a!=null&&Matches(a,false);status.Text=same?(string.IsNullOrEmpty(a.Error)?a.Status:a.Error):"未送 AI；已選取 "+sql.Split('\n').Length+" 行。";
    analyze.Content=a?.Busy==true?"分析中...":"一鍵優化";analyze.IsHitTestVisible=a?.Busy!=true;analyze.Focusable=a?.Busy!=true;progress.Visibility=a?.Busy==true?Visibility.Visible:Visibility.Collapsed;details.Visibility=a==null?Visibility.Collapsed:Visibility.Visible;
    preview.Children.Clear();preview.Visibility=a?.Preview==true&&a.CanApply&&same?Visibility.Visible:Visibility.Collapsed;if(preview.Visibility==Visibility.Visible){preview.Children.Add(Ui.Text("已通過 Snapshot · Tab 套用 / Esc 關閉",12,"#047857"));preview.Children.Add(Ui.Code(a.Candidate.Sql,180));preview.Children.Add(Ui.Button("套用",Apply,true));}
    card.Width=Math.Min(368,Math.Max(180,View.ViewportWidth-28));card.Measure(new Size(card.Width,double.PositiveInfinity));var height=card.DesiredSize.Height;var span=View.Selection.StreamSelectionSpan.SnapshotSpan;var geometry=View.TextViewLines.GetMarkerGeometry(span);double top=geometry?.Bounds.Top??View.ViewportTop,bottom=geometry?.Bounds.Bottom??top;double y=top-height-10;if(y<View.ViewportTop+14)y=bottom+10;y=Math.Max(View.ViewportTop+14,Math.Min(y,View.ViewportBottom-height-14));double x=Math.Max(View.ViewportLeft+14,View.ViewportRight-card.Width-14);Canvas.SetLeft(card,x-View.ViewportLeft);Canvas.SetTop(card,y-View.ViewportTop);layer.AddAdornment(AdornmentPositioningBehavior.ViewportRelative,null,this,card,null);
   }catch(Exception e){Diagnostics.Error(e);}
  }
  public void SaveVersion()
  {
   try{var sql=string.IsNullOrWhiteSpace(Sql)?View.TextSnapshot.GetText():Sql;var fragment=new TSql170Parser(true).Parse(new StringReader(sql),out var errors);var script=fragment as TSqlScript;if(errors.Count>0||script?.Batches.Count!=1||script.Batches[0].Statements.Count!=1||!(script.Batches[0].Statements[0] is ProcedureStatementBody))throw new InvalidOperationException("請選取完整 CREATE / ALTER PROCEDURE 定義，再建立目前 SP 版本。");var directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Alyvo","SsmsAiSqlAssistant","sp-versions");Directory.CreateDirectory(directory);var path=Path.Combine(directory,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+ConnectionContext.Hash(sql).Substring(0,12)+".sql");File.WriteAllText(path,sql);Message="目前 SP 版本已儲存至 "+path;AssistantPackage.Instance?.ShowDetails();SessionHub.Refresh();}
   catch(Exception e){Message=DbWorker.SafeError(e);AssistantPackage.Instance?.ShowDetails();SessionHub.Refresh();}
  }
 }
}




