using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Documents;
using Newtonsoft.Json.Linq;
namespace Alyvo.SsmsAiSqlAssistant
{
 public static class Ui
 {
  public static SolidColorBrush Brush(string color)=>new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
  public static TextBlock Text(string text,int size=12,string color="#111827")=>new TextBlock{Text=text??"",FontSize=size,Foreground=Brush(color),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,3)};
  public static Button Button(string text,Action action,bool primary=false){var b=new Button{Content=text,Padding=new Thickness(10,5,10,5),MinHeight=30,Margin=new Thickness(6,0,0,4),Background=primary?Brush("#1D4ED8"):Brushes.White,Foreground=primary?Brushes.White:Brush("#111827"),BorderBrush=Brush("#DDE1E6"),BorderThickness=new Thickness(1)};b.Click+=(s,e)=>action();return b;}
  public static Button AsyncButton(string text,Func<Task> action,bool primary=false){var b=Button(text,()=>{},primary);async void Click(object sender,RoutedEventArgs e){try{await action();}catch(Exception error){Diagnostics.Error(error);}}b.Click+=Click;return b;}
  public static Button ActionButton(string text,Action action)
  {
   var b=Button(text,action,true);
   var border=new FrameworkElementFactory(typeof(Border));border.Name="Surface";
   border.SetValue(Border.BackgroundProperty,Brush("#1D4ED8"));border.SetValue(Border.BorderBrushProperty,Brush("#1E40AF"));border.SetValue(Border.BorderThicknessProperty,new Thickness(2));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(4));border.SetValue(Border.PaddingProperty,new Thickness(10,5,10,5));
   var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(ContentPresenter.ContentProperty,new TemplateBindingExtension(ContentControl.ContentProperty));content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);content.SetValue(TextElement.ForegroundProperty,Brushes.White);border.AppendChild(content);
   var template=new ControlTemplate(typeof(Button)){VisualTree=border};
   foreach(var state in new[]{new{Property=UIElement.IsMouseOverProperty,Color="#1E40AF"},new{Property=ButtonBase.IsPressedProperty,Color="#1E3A8A"}}){var trigger=new Trigger{Property=state.Property,Value=true};trigger.Setters.Add(new Setter(Border.BackgroundProperty,Brush(state.Color),"Surface"));template.Triggers.Add(trigger);}
   var focus=new Trigger{Property=UIElement.IsKeyboardFocusedProperty,Value=true};focus.Setters.Add(new Setter(Border.BorderBrushProperty,Brush("#F59E0B"),"Surface"));template.Triggers.Add(focus);
   var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(Border.BackgroundProperty,Brush("#E2E8F0"),"Surface"));disabled.Setters.Add(new Setter(Border.BorderBrushProperty,Brush("#94A3B8"),"Surface"));content.Name="Label";disabled.Setters.Add(new Setter(TextElement.ForegroundProperty,Brush("#334155"),"Label"));template.Triggers.Add(disabled);
   b.Template=template;ToolTipService.SetShowOnDisabled(b,true);return b;
  }
  public static TextBox Code(string sql,double height=160)=>new TextBox{Text=sql??"",IsReadOnly=true,FontFamily=new FontFamily("Consolas"),FontSize=12,Background=Brush("#F8FAFC"),Foreground=Brush("#111827"),BorderBrush=Brush("#DDE1E6"),BorderThickness=new Thickness(1),Padding=new Thickness(9,8,9,8),TextWrapping=TextWrapping.NoWrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=height,MinHeight=60,Margin=new Thickness(0,6,0,6)};
  public static Border Card(UIElement child)=>new Border{Background=Brushes.White,BorderBrush=Brush("#DDE1E6"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(4),Padding=new Thickness(12),Margin=new Thickness(0,0,0,10),Child=child};
  public static Expander Expand(string label,UIElement content,bool expanded=false)=>new Expander{Header=label,Content=content,IsExpanded=expanded,Margin=new Thickness(0,8,0,4),Foreground=Brush("#111827")};
  public static void Open(string path){try{Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}catch(Exception e){Diagnostics.Error(e);}}
 }
 public sealed class DetailsControl:UserControl
 {
  private readonly StackPanel body=new StackPanel{Margin=new Thickness(12,12,12,16)};private readonly ScrollViewer scroll;private bool subscribed;
  public DetailsControl()
  {
   Background=Ui.Brush("#F6F7F9");UseLayoutRounding=true;SnapsToDevicePixels=true;FontFamily=new FontFamily("Segoe UI");FontSize=12;
   var root=new DockPanel();var header=new DockPanel{LastChildFill=true};var usage=Ui.Button("用量中心",()=>Ui.Open(LocalApi.BaseUrl+"/usage/ui"));DockPanel.SetDock(usage,Dock.Right);header.Children.Add(usage);var titles=new StackPanel();var title=Ui.Text("AI SQL 優化儀表板",20);title.FontWeight=FontWeights.SemiBold;titles.Children.Add(title);titles.Children.Add(Ui.Text("候選、Dry Run 與套用狀態集中呈現。",12,"#4B5563"));header.Children.Add(titles);var head=new Border{Background=Brushes.White,BorderBrush=Ui.Brush("#DDE1E6"),BorderThickness=new Thickness(0,0,0,1),Padding=new Thickness(16,12,16,10),Child=header};DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
   scroll=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};root.Children.Add(scroll);Content=root;
   Loaded+=(s,e)=>{if(!subscribed){SessionHub.Changed+=Refresh;subscribed=true;}Refresh();};Unloaded+=(s,e)=>{if(subscribed){SessionHub.Changed-=Refresh;subscribed=false;}};
  }
  private void AddCard(string title,params UIElement[] content){var panel=new StackPanel();if(!string.IsNullOrEmpty(title)){var text=Ui.Text(title,16);text.FontWeight=FontWeights.SemiBold;panel.Children.Add(text);}foreach(var item in content)panel.Children.Add(item);body.Children.Add(Ui.Card(panel));}
  public void Refresh()
  {
   if(!Dispatcher.CheckAccess()){Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.RunAsync(async()=>{await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();Refresh();});return;}var offset=scroll.VerticalOffset;body.Children.Clear();var session=SessionHub.Current;var a=session?.Analysis;
   if(!string.IsNullOrEmpty(session?.Message))AddCard("訊息",Ui.Text(session.Message,12,"#92400E"));
   if(a==null){AddCard("開始優化",Ui.Text("請回 Query Editor 反白 SQL，開啟「AI 優化」浮動模式，再按卡片上的「一鍵優化」。反白本身不會送 AI。"));AddDiagnostics();return;}
   var failure=!string.IsNullOrEmpty(a.Error)||a.Preflight?.Passed==false||a.DryRun?.Passed==false;var accent=failure?"#B91C1C":a.DryRun?.Passed==true?"#047857":"#1D4ED8";
   var conclusion=new StackPanel();conclusion.Children.Add(Ui.Text(a.Status,17,accent));if(!string.IsNullOrEmpty(a.Error))conclusion.Children.Add(Ui.Text(a.Error,12,"#B91C1C"));conclusion.Children.Add(Ui.Text("只替換編輯器文字；候選不在來源資料庫執行。",12,"#4B5563"));
   if(a.Busy){conclusion.Children.Add(new ProgressBar{IsIndeterminate=true,Height=4,Margin=new Thickness(0,8,0,8)});conclusion.Children.Add(Ui.Button("取消",session.Cancel));}
   var apply=Ui.ActionButton(a.Applied?"已套用":"套用 SQL",session.Apply);apply.IsEnabled=a.CanApply;
   var applyReason=a.Applied?"已替換編輯器文字，可使用復原或 Ctrl+Z。":a.CanApply?"驗證通過，按「套用 SQL」替換原本反白的 SQL。":!string.IsNullOrEmpty(a.Error)?"請先排除錯誤並重新分析。":a.Busy?"驗證執行中，完成後才可套用。":a.Candidate==null?"等待 AI 產生候選 SQL。":a.Preflight?.Passed!=true?"六項安全預檢通過後，請執行 Snapshot Dry Run。":"Snapshot Dry Run 通過後才可套用。";
   apply.ToolTip=applyReason;
   var actions=new WrapPanel();var run=Ui.ActionButton(a.Busy?"處理中…":a.DryRun==null?"建立 Snapshot / Dry Run":"重新執行 Snapshot / Dry Run",session.StartDryRun);run.IsEnabled=!a.Busy&&a.Preflight?.Passed==true&&string.IsNullOrEmpty(a.Error)&&!a.Applied;run.ToolTip="六項安全預檢通過後，在 Snapshot 執行結果與效能驗證。";actions.Children.Add(run);actions.Children.Add(apply);conclusion.Children.Add(actions);
   conclusion.Children.Add(Ui.Text(applyReason,12,"#334155"));
   var c=Ui.Card(conclusion);c.BorderBrush=Ui.Brush(accent);c.BorderThickness=new Thickness(4,1,1,1);body.Children.Add(c);
   var grid=new UniformGrid{Columns=2};foreach(var pair in new[]{new[]{"分析來源",a.Response?.AiProvider??"尚未送出"},new[]{"候選",(a.Response?.Candidates.Length??0)+" 組"},new[]{"安全預檢",a.Preflight==null?"尚未完成":a.Preflight.Passed?"通過":"退回"},new[]{"Snapshot Dry Run",a.DryRun?.Status??"尚未執行"}}){var item=new StackPanel{Margin=new Thickness(5)};item.Children.Add(Ui.Text(pair[0],11,"#6B7280"));item.Children.Add(Ui.Text(pair[1],13));grid.Children.Add(item);}body.Children.Add(Ui.Card(grid));
   if(a.Response!=null)
   {
    AddCard("摘要",Ui.Text(a.Response.Summary));if(a.Response.Issues.Any())AddCard("問題與修改理由",Ui.Text(string.Join("\n",a.Response.Issues)));if(a.Response.Warnings.Any())AddCard("提醒",Ui.Text(string.Join("\n",a.Response.Warnings),12,"#92400E"));
    if(a.Response.Candidates.Length>1){var choices=new ComboBox{ItemsSource=Enumerable.Range(0,a.Response.Candidates.Length).Select(i=>"候選版本 #"+(i+1)).ToArray(),SelectedIndex=a.CandidateIndex,Margin=new Thickness(0,0,0,10),IsEnabled=!a.Busy};choices.SelectionChanged+=(s,e)=>{if(choices.SelectedIndex>=0)session.SelectCandidate(choices.SelectedIndex);};body.Children.Add(choices);}
   }
   if(a.Candidate!=null)
   {
    AddCard("候選 SQL",Ui.Text(a.Preflight?.Passed==true?"安全預檢通過，仍需 Snapshot Dry Run。":"先確認完整內容，再執行 Snapshot Dry Run。",12,"#4B5563"),Ui.Code(a.Candidate.Sql,260),Ui.Button("複製 SQL",()=>Clipboard.SetText(a.Candidate.Sql)),Ui.Expand("原因與代價",Ui.Text(a.Candidate.Explanation),true));
   }
   body.Children.Add(Ui.Expand("本次實際送出的 SELECT",Ui.Code(a.Original,220)));
   if(a.Preflight!=null)
   {
    var checks=new StackPanel();foreach(var gate in a.Preflight.Gates)checks.Children.Add(Ui.Text((gate.Passed?"通過：":"未過：")+gate.Name+" · "+gate.Reason,12,gate.Passed?"#4B5563":"#B91C1C"));
    AddCard("安全預檢",Ui.Text($"{a.Preflight.Gates.Count(g=>g.Passed)}/6 項通過",13,a.Preflight.Passed?"#047857":"#B91C1C"),Ui.Text("這是編譯、欄位契約與 estimated plan 檢查；不代表結果或效能已經驗證。",12,"#4B5563"),Ui.Expand("查看安全預檢項目",checks,true));
    if(a.Preflight.Columns!=null)body.Children.Add(Ui.Expand("輸出欄位契約",Ui.Code(string.Join("\n",a.Preflight.Columns.Select(col=>$"{col.Ordinal}. {col.Name} · {col.Type} · NULL={col.Nullable} · {col.Collation}")),160)));
   }
   var dry=new StackPanel();dry.Children.Add(Ui.Text("預設預算：每次 90 秒、整批 300 秒；250000 列 / 64 MiB；1M reads / 60 秒 CPU。",12,"#4B5563"));
   if(a.Preflight?.Passed!=true)dry.Children.Add(Ui.Text("六關安全預檢通過前不可建立 Snapshot。",12,"#92400E"));
   if(a.DryRun!=null)
   {
    var r=a.DryRun;foreach(var saved in (r.Original??Array.Empty<RunMetrics>()).Concat(r.Candidate??Array.Empty<RunMetrics>()).Where(x=>x.SavedPlan!=null).Select(x=>x.SavedPlan)){var planCard=new StackPanel();planCard.Children.Add(Ui.Text(saved.Summary));planCard.Children.Add(Ui.Text("SHA-256："+saved.Hash));if(!saved.Oversize&&!string.IsNullOrEmpty(saved.Path))planCard.Children.Add(Ui.Button("開啟本機 actual plan",()=>Ui.Open(saved.Path)));dry.Children.Add(Ui.Expand("已保存 actual plan（只在本機）",planCard));}dry.Children.Add(Ui.Text(r.Status,15,r.Passed?"#047857":"#B91C1C"));dry.Children.Add(Ui.Text(r.Reason,12,r.Passed?"#047857":"#B91C1C"));dry.Children.Add(Ui.Text("Query Store baseline："+r.QueryStore));
    if(!string.IsNullOrEmpty(r.CleanupWarning))dry.Children.Add(Ui.Text(r.CleanupWarning,12,"#B91C1C"));
    if(r.Original?.Length>=1&&r.Candidate?.Length==3)
    {
     dry.Children.Add(Ui.Text($"結果一致性：{(r.Original.Concat(r.Candidate).Select(x=>x.Digest).Distinct().Count()==1?"一致":"不一致")}\nColumns：{r.Original[0].Columns} → {r.Candidate[0].Columns}\nRows：{r.Original[0].Rows} → {r.Candidate[0].Rows}\nMedian logical reads：{DryRunResult.Median(r.Original.Select(x=>x.Reads))} → {DryRunResult.Median(r.Candidate.Select(x=>x.Reads))}\nMedian CPU：{DryRunResult.Median(r.Original.Select(x=>x.CpuMs))} → {DryRunResult.Median(r.Candidate.Select(x=>x.CpuMs))} ms\nMedian elapsed：{DryRunResult.Median(r.Original.Select(x=>x.ElapsedMs))} → {DryRunResult.Median(r.Candidate.Select(x=>x.ElapsedMs))} ms"));
     foreach(var pair in new[]{new[]{"原版 actual plan",r.Original[0].Plan},new[]{"候選 actual plan",r.Candidate[0].Plan}}){var plan=new StackPanel();plan.Children.Add(Ui.Code(pair[1].Substring(0,Math.Min(pair[1].Length,128000)),160));plan.Children.Add(Ui.Button("複製完整 actual plan",()=>Clipboard.SetText(pair[1])));dry.Children.Add(Ui.Expand(pair[0],plan));}
    }
   }
   AddCard("Snapshot Dry Run",dry);
   if(a.CanApply)AddCard("套用預覽",Ui.Text("已符合 Snapshot 結果一致、候選穩定、median reads 降低、actual plan 無 regression。",12,"#047857"),Ui.Code(a.Candidate.Sql,240),Ui.Button("套用",session.Apply,true),Ui.Button("顯示 inline preview",()=>{a.Preview=true;session.Render();}));
   if(session.CanRestore)AddCard("復原",Ui.Text("只還原本次替換的原始文字；有後續修改時會拒絕覆蓋。"),Ui.Button("復原",session.Restore));
   if(a.Response!=null)AddChat(a);
   AddDiagnostics();scroll.ScrollToVerticalOffset(offset);
  }
  private void AddChat(AnalysisState a)
  {
   var panel=new StackPanel();panel.Children.Add(Ui.Text("支援主題：說明、風險、索引。只回答目前分析上下文。",12,"#4B5563"));var input=new TextBox{MinHeight=30,Margin=new Thickness(0,6,0,6)};panel.Children.Add(input);var answer=Ui.Text("");Button send=null;send=Ui.AsyncButton("送出",async()=>
   {
    var value=input.Text.Trim();var topic=value=="說明"?"explanation":value=="風險"?"risks":value=="索引"?"indexes":null;if(topic==null){answer.Text="請輸入支援的主題：說明、風險或索引。";return;}
    send.IsHitTestVisible=false;answer.Text="回答中...";try{var json=await new LocalApi().Send("/chat",new{context=a.Payload,topic},a.Cancellation.Token);answer.Text=LocalApi.Decode(json).Summary;}catch(Exception e){answer.Text=DbWorker.SafeError(e);}finally{send.IsHitTestVisible=true;}
   },true);panel.Children.Add(send);panel.Children.Add(answer);AddCard("AI 對話",panel);
  }
  private void AddDiagnostics()
  {
   var text=Ui.Text($"Package loaded：{Diagnostics.PackageLoaded}\nMEF listener 建立：{Diagnostics.ListenerCount}\nActive views：{Diagnostics.ActiveViews}\nSelection events：{Diagnostics.SelectionEvents}\nLast adornment error：{Diagnostics.LastError}\nAPI：{LocalApi.Version}\n來源與候選 SQL 只在記憶體中處理；診斷不寫入 SQL 或 key。",11,"#4B5563");body.Children.Add(Ui.Expand("更多資料 / Debug",text));
  }
 }
}
