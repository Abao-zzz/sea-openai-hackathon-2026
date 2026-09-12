using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json.Linq;

namespace Alyvo.SsmsAiSqlAssistant
{
 [Guid("8c746be2-3d88-469d-a498-370348589c7f")]
 public sealed class SpScanWindow:ToolWindowPane
 {
  public SpScanWindow(){Caption="AI SQL 資料庫掃描";Content=new SpScanControl();}
  protected override void Dispose(bool disposing){if(disposing)(Content as SpScanControl)?.Dispose();base.Dispose(disposing);}
 }
 public sealed class SpScanControl:UserControl,IDisposable
 {
  readonly StackPanel page=new StackPanel{Margin=new Thickness(16,16,16,22),MaxWidth=1120};readonly TextBlock title=Ui.Text("SP 效能儀表板",20),subtitle=Ui.Text("依目前 DMV 與可快速取得的近期成本排序；選一支後才載入該 SP 的 plan 證據。",12,"#4B5563");readonly ScrollViewer scroll;
  readonly SpBatch batch=new SpBatch();readonly SpReviewWorker worker=new SpReviewWorker();readonly VersionRepository versions=new VersionRepository();readonly MapControl map=new MapControl();readonly SpBudget budget=new SpBudget();
  ConnectedGovernance governance;ConnectionContext context;SpScanResult scan;SpScanRow reviewRow;CancellationTokenSource single;bool disposed,busy,rendering;int tab,pageNumber,top=5;string query="",error="";DateTimeOffset started;TextBlock live;
  public SpScanControl()
  {
   UseLayoutRounding=true;SnapsToDevicePixels=true;Background=Ui.Brush("#F6F7F9");FontFamily=new FontFamily("Segoe UI");FontSize=12;title.FontWeight=FontWeights.SemiBold;
   var header=new DockPanel();var actions=new WrapPanel();actions.Children.Add(Action("用量中心",()=>Ui.Open(LocalApi.BaseUrl+"/usage/ui")));actions.Children.Add(Async("重新掃描",Reload));DockPanel.SetDock(actions,Dock.Right);header.Children.Add(actions);var labels=Stack(title,subtitle);header.Children.Add(labels);var shell=new DockPanel();var head=new Border{Child=header,Background=Brushes.White,BorderBrush=Ui.Brush("#DDE1E6"),BorderThickness=new Thickness(0,0,0,1),Padding=new Thickness(16,12,16,10)};DockPanel.SetDock(head,Dock.Top);shell.Children.Add(head);scroll=new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};shell.Children.Add(scroll);Content=shell;batch.Changed+=BatchChanged;Unloaded+=(s,e)=>governance?.Dispose();Loaded+=(s,e)=>{if(context!=null){governance?.Dispose();governance=new ConnectedGovernance(context,ScheduleConnected);governance.Start();}};
  }
  bool ScheduleConnected(){if(disposed||!IsVisible||context==null)return false;try{return ConnectionContext.Capture().Fingerprint==context.Fingerprint;}catch{return false;}}
  static StackPanel Stack(params UIElement[] children){var panel=new StackPanel();foreach(var child in children)panel.Children.Add(child);return panel;}
  Button Action(string label,System.Action action,bool primary=false)=>Ui.Button(label,()=>{try{action();}catch(Exception e){error=DbWorker.SafeError(e);Render();}},primary);
  Button Async(string label,Func<Task> action,bool primary=false)=>Ui.AsyncButton(label,async()=>{try{await action();}catch(Exception e){error=DbWorker.SafeError(e);Render();}},primary);
  static Border Badge(string text,string color="#1D4ED8")=>new Border{Child=Ui.Text(text,11,color),Background=Ui.Brush("#EFF6FF"),BorderBrush=Ui.Brush("#DDE1E6"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(4),Padding=new Thickness(6,2,6,2),Margin=new Thickness(0,3,6,6)};
  void Card(string label,params UIElement[] children){var box=Stack();if(label!=null){var heading=Ui.Text(label,16);heading.FontWeight=FontWeights.SemiBold;box.Children.Add(heading);}foreach(var child in children)box.Children.Add(child);page.Children.Add(Ui.Card(box));}
  public async Task Load(ConnectionContext connection){if(busy||batch.Busy)throw new InvalidOperationException("請先取消目前作業。");governance?.Dispose();context=connection;await Reload();if(scan!=null){governance=new ConnectedGovernance(context,ScheduleConnected);governance.Start();}}
  async Task Reload()
  {
   if(context==null)return;if(busy||batch.Busy)throw new InvalidOperationException("作業進行中；請先取消，不能清除活動中的證據。");busy=true;single?.Dispose();single=new CancellationTokenSource();scan=null;reviewRow=null;query="";pageNumber=tab=0;error="";subtitle.Text="唯讀選靶，不送 OpenAI、不執行 Stored Procedure。";Render();
   try{scan=await new SpScanCollector().Load(context,single.Token);map.LoadSnapshot(context,scan.Graph);}
   catch(Exception e){error=DbWorker.SafeError(e);}finally{busy=false;subtitle.Text="依目前 DMV 與可快速取得的近期成本排序；選一支後才載入該 SP 的 plan 證據。";Render();}
  }
  void BatchChanged(){if(disposed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new System.Action(BatchChanged));return;}if(live!=null)live.Text=BatchText();if(!batch.Busy)Render();}
  string BatchText()
  {
   if(batch.Job==null)return batch.Progress;var items=batch.Job["items"].ToArray();return $"{items.Count(x=>new[]{"completed","failed","skipped"}.Contains(x["status"].Value<string>()))}/{items.Length} · {batch.Progress}\nElapsed {items.Sum(x=>x["elapsedMs"].Value<long>())/1000.0:N1}s · CPU {items.Sum(x=>x["cpuMs"].Value<long>()):N0}ms · IO {items.Sum(x=>x["logicalReads"].Value<long>()):N0} · tokens {items.Sum(x=>x["tokens"].Value<long>()):N0} · retry 最多1次";
  }
  bool Match(SpScanRow row)=>query.Length==0||row.SearchText.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0;
  public void Render()
  {
   if(disposed)return;var offset=scroll.VerticalOffset;rendering=true;
   try
   {
    // Detach the reusable map before replacing its parent tabs.
    if(map.Parent is ContentControl parent)parent.Content=null;page.Children.Clear();title.Text=reviewRow==null?"SP 效能儀表板":"SP 候選審查";
    if(error.Length>0){var fail=Ui.Card(Ui.Text(error,13,"#B91C1C"));fail.BorderBrush=Ui.Brush("#B91C1C");page.Children.Add(fail);}
    if(scan==null){Card(context?.Database??"尚未選取資料庫",Ui.Text(busy?"正在唯讀載入 catalog、DMV、Query Store summary…":"從 Object Explorer database node 右鍵開啟。"));if(busy)page.Children.Add(Action("取消",()=>single.Cancel()));return;}
    if(reviewRow!=null){Review();return;}
    Summary();Search();var tabs=new TabControl{Background=Ui.Brush("#F6F7F9"),BorderBrush=Ui.Brush("#DDE1E6")};
    var candidates=scan.Rows.Where(r=>r.Eligible&&Match(r)).ToArray();var skipped=scan.Rows.Where(r=>!r.Eligible&&Match(r)).ToArray();
    tabs.Items.Add(new TabItem{Header="優先候選 ("+scan.Rows.Count(r=>r.Eligible)+")",Content=Rows(candidates,false)});tabs.Items.Add(new TabItem{Header="已跳過 ("+scan.Rows.Count(r=>!r.Eligible)+")",Content=Rows(skipped,true)});
    map.Height=Math.Max(650,ActualHeight-200);tabs.Items.Add(new TabItem{Header="SP 呼叫地圖 ("+scan.Graph.ProcedureCount+")",Content=map});tabs.SelectedIndex=tab;tabs.SelectionChanged+=(s,e)=>{if(!rendering&&e.Source==tabs){tab=tabs.SelectedIndex;pageNumber=0;}};page.Children.Add(tabs);
    page.Children.Add(Ui.Expand("更多資料 / Debug",Ui.Text("初掃只讀 catalog、DMV 與最多3秒 Query Store summary；不解析完整 plan、不呼叫 OpenAI。\n"+scan.Notice)));
   }
   finally{rendering=false;scroll.ScrollToVerticalOffset(offset);}
  }
  void Summary()
  {
   var box=Stack();var name=Ui.Text(context.Database,16);name.FontWeight=FontWeights.SemiBold;box.Children.Add(name);box.Children.Add(Ui.Text("清單不解析 plan XML；重型 plan cache／Query Store 證據在點選單支 SP 後載入。",12,"#4B5563"));box.Children.Add(new WrapPanel{Children={Badge("Stored Procedure"),Badge("優先 Top N","#4B5563")}});
   var toolbar=new WrapPanel{Margin=new Thickness(0,10,0,8)};toolbar.Children.Add(Ui.Text($"批次驗證 Top {top}\n不自動套用 · {budget.TimeSeconds:N0}秒 · CPU {budget.CpuMs:N0}ms · IO {budget.LogicalReads:N0} · token {budget.Tokens:N0} · 重試1次",12,"#4B5563"));
   if(batch.Busy)toolbar.Children.Add(Async("取消整批",batch.Cancel,true));else{var begin=Async("批次驗證 Top "+Math.Min(top,scan.Rows.Count(r=>r.Eligible)),()=>StartBatch(),true);begin.IsEnabled=!busy&&scan.Rows.Any(r=>r.Eligible);toolbar.Children.Add(begin);toolbar.Children.Add(Action("預算設定",BudgetDialog));}
   if(busy)toolbar.Children.Add(Action("取消單支預檢",()=>single?.Cancel()));toolbar.Children.Add(Async("批次紀錄",History));box.Children.Add(toolbar);live=Ui.Text(BatchText(),12,"#1D4ED8");if(batch.Busy)box.Children.Add(new ProgressBar{Height=3,IsIndeterminate=true,Margin=new Thickness(0,4,0,4)});box.Children.Add(live);
   var counts=new UniformGrid{Columns=3,Background=Ui.Brush("#F8FAFC")};foreach(var pair in new[]{Tuple.Create("掃描 SP",scan.Rows.Length,"#6B7280"),Tuple.Create("優先候選",scan.Rows.Count(r=>r.Eligible),"#1D4ED8"),Tuple.Create("跳過",scan.Rows.Count(r=>!r.Eligible),"#92400E")})counts.Children.Add(new Border{Padding=new Thickness(10),BorderBrush=Ui.Brush("#DDE1E6"),BorderThickness=new Thickness(0,0,1,0),Child=Stack(Ui.Text(pair.Item2.ToString(),20,pair.Item3),Ui.Text(pair.Item1))});box.Children.Add(counts);page.Children.Add(Ui.Card(box));
  }
  void Search()
  {
   var field=new TextBox{Text=query,Height=32,VerticalContentAlignment=VerticalAlignment.Center,MinWidth=120,ToolTip="搜尋 SP 名稱、用途或負責人"};field.TextChanged+=(s,e)=>query=field.Text.Trim();
   void Apply(){pageNumber=0;if(!scan.Rows.Any(r=>r.Eligible&&Match(r))&&scan.Rows.Any(r=>!r.Eligible&&Match(r)))tab=1;else if(scan.Rows.Any(r=>r.Eligible&&Match(r))&&!scan.Rows.Any(r=>!r.Eligible&&Match(r)))tab=0;map.LoadSnapshot(context,scan.Graph,query);Render();}
   field.KeyDown+=(s,e)=>{if(e.Key==System.Windows.Input.Key.Enter)Apply();};var row=new DockPanel();var button=Action("搜尋",Apply,true);DockPanel.SetDock(button,Dock.Right);row.Children.Add(button);var count=Ui.Text($"清單 {scan.Rows.Count(Match)} · 地圖 {scan.Graph.ProcedureCount} 支");DockPanel.SetDock(count,Dock.Right);row.Children.Add(count);var overlay=new Grid();overlay.Children.Add(field);var hint=Ui.Text("搜尋 SP 名稱、用途或負責人",12,"#6B7280");hint.IsHitTestVisible=false;hint.Margin=new Thickness(8,6,0,0);hint.Visibility=query.Length==0?Visibility.Visible:Visibility.Collapsed;field.TextChanged+=(s,e)=>hint.Visibility=field.Text.Length==0?Visibility.Visible:Visibility.Collapsed;overlay.Children.Add(hint);row.Children.Add(overlay);page.Children.Add(Ui.Card(row));
  }
  UIElement Rows(SpScanRow[] rows,bool skipped)
  {
   var panel=new StackPanel{Margin=new Thickness(12)};panel.Children.Add(Ui.Text(skipped?"只列出無法進入本批預檢的項目與具體原因。":"依歷史總 logical reads 排序；只處理本批最值得看的項目。",12,"#4B5563"));
   panel.Children.Add(new Border{Background=Ui.Brush("#F3F4F6"),Padding=new Thickness(10),Child=Ui.Text(skipped?"Stored Procedure                                      跳過原因":"#   Stored Procedure                                      狀態／操作")});
   if(rows.Length==0)panel.Children.Add(Ui.Text("沒有符合搜尋條件的項目。",14));int max=Math.Max(1,(rows.Length+19)/20);int current=Math.Min(pageNumber,max-1);
   foreach(var row in rows.Skip(current*20).Take(20))
   {
    var details=Stack();var actions=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};var identity=Ui.Text((skipped?"":row.Rank+"  ")+row.Module.FullName,14);identity.FontWeight=FontWeights.SemiBold;details.Children.Add(identity);
    if(skipped)details.Children.Add(Ui.Text(row.Reason,12,"#92400E"));else
    {
     details.Children.Add(Ui.Text($"Reads {row.Reads:N0} · {row.Executions:N0} 次 · 平均 {(row.Executions>0?row.Reads/row.Executions:0):N0} · {row.Source}",12,"#4B5563"));details.Children.Add(new ProgressBar{Minimum=0,Maximum=Math.Max(1,scan.Rows.Max(x=>x.Reads)),Value=row.Reads,Height=2,Margin=new Thickness(0,6,0,6)});
     details.Children.Add(Ui.Text(row.Stage,12,"#1D4ED8"));if(busy&&single!=null&&row.Review!=null&&row.Stage.Contains("/"))details.Children.Add(new ProgressBar{Height=3,IsIndeterminate=true});
     var go=row.Review==null?Async("候選預檢",()=>Single(row),true):Action("查看結果",()=>{reviewRow=row;Render();scroll.ScrollToTop();},true);go.ToolTip="按下後才載入此 objectId 的 plan / parameter evidence，並可能呼叫 OpenAI。";go.IsEnabled=!busy&&!batch.Busy;actions.Children.Add(go);
     if(row.Review!=null && row.Review.Passed!=true && row.Attempts<2){var retry=Async("重試",()=>Single(row));retry.IsEnabled=!busy&&!batch.Busy;actions.Children.Add(retry);}
    }
    details.Children.Add(actions);details.Children.Add(Ui.Expand("查看參數與依據",Ui.Text($"用途：{row.Module.Purpose}\n負責單位：{row.Module.Owner}\n最後執行：{row.LastExecution?.ToString("yyyy-MM-dd HH:mm:ss")??"無"}\nCPU {row.CpuMs:N0} ms · elapsed {row.ElapsedMs:N0} ms\n參數：{(row.Body==null?"不支援／未確認":string.Join(", ",row.Body.Parameters.Select(p=>p.Name+" "+p.Type)))}\n定義：{(row.Module.Definition==null?"缺失":"完整")} · encrypted={row.Module.Encrypted}\n{row.Reason}")));
    var card=Ui.Card(details);card.Background=Ui.Brush(row.Review!=null&&busy?"#EFF6FF":"#FFFFFF");panel.Children.Add(card);
   }
   var navigation=new WrapPanel();if(current>0)navigation.Children.Add(Action("上一頁",()=>{pageNumber=current-1;Render();}));navigation.Children.Add(Ui.Text($"第 {current+1}/{max} 頁 · 每頁20筆"));if(current+1<max)navigation.Children.Add(Action("下一頁",()=>{pageNumber=current+1;Render();}));panel.Children.Add(navigation);return panel;
  }
  async Task Single(SpScanRow row)
  {
   if(busy||batch.Busy)return;if(row.Attempts>=2)throw new InvalidOperationException("已達本批單支重試上限；重新掃描才重置。");busy=true;row.Attempts++;error="";single?.Dispose();single=new CancellationTokenSource();started=DateTimeOffset.UtcNow;row.Stage="第 1/4：載入本支 evidence";Render();
   try{await worker.Prepare(context,row,single.Token,new Progress<string>(s=>{row.Stage=s;subtitle.Text=row.Module.FullName+"："+s;Render();}));row.Stage=row.Review.Status;reviewRow=row;}
   catch(OperationCanceledException){row.Stage="已取消";reviewRow=row;}finally{busy=false;Render();scroll.ScrollToTop();}
  }
  async Task DryRun(SpScanRow row)
  {
   if(busy||batch.Busy)return;busy=true;single?.Dispose();single=new CancellationTokenSource();Render();try{await worker.Verify(context,row,single.Token,new Progress<string>(s=>{row.Stage=s;subtitle.Text=s;Render();}));}catch(OperationCanceledException){row.Review.Error="已取消";row.Review.Status="候選已退回";}finally{busy=false;Render();}
  }
  void Review()
  {
   var row=reviewRow;var r=row.Review;if(r==null)return;subtitle.Text=row.Module.FullName+"；來源查詢模式，不在來源資料庫執行候選。";page.Children.Add(Action("返回待審清單",()=>{reviewRow=null;subtitle.Text="依目前 DMV 與可快速取得的近期成本排序；選一支後才載入該 SP 的 plan 證據。";Render();}));
   var color=r.Passed?"#047857":r.PreflightPassed?"#1D4ED8":"#B91C1C";var summary=Stack(Ui.Text(busy?row.Stage:r.Status,18,color),Ui.Text(r.Error,12,"#B91C1C"));var grid=new UniformGrid{Columns=2};foreach(var pair in new[]{new[]{"分析來源",r.Analysis?.AiProvider??"未送 OpenAI"},new[]{"候選",r.Analysis==null?"0 組":r.Analysis.Candidates.Length+" 組"},new[]{"安全預檢",r.PreflightPassed?"通過":"退回／未完成"},new[]{"資料庫動作","未自動套用"},new[]{"Snapshot Dry Run",r.Passed?"所有案例通過":r.PreflightPassed?"可執行":"不可執行"},new[]{"版本",r.Version.HasValue?"V"+r.Version:"尚未建立"}})grid.Children.Add(new Border{Padding=new Thickness(8),Background=Ui.Brush("#F8FAFC"),Child=Stack(Ui.Text(pair[0],11,"#6B7280"),Ui.Text(pair[1],13,color))});summary.Children.Add(grid);var callout=Ui.Card(summary);callout.BorderBrush=Ui.Brush(color);callout.BorderThickness=new Thickness(4,1,1,1);page.Children.Add(callout);
   if(busy){page.Children.Add(new ProgressBar{Height=3,IsIndeterminate=true});page.Children.Add(Action("取消",()=>single?.Cancel()));}
   if(r.CandidateSql!=null)Card("候選 SP",Ui.Text("完整內容供審查與複製；不執行、不寫回資料庫。"),Ui.Code(r.CandidateSql,300),Action("複製 SQL",()=>Clipboard.SetText(r.CandidateSql)));
   Card("來源查詢模式",Ui.Text("只分析分離的唯讀 SELECT；參數、SP 身分與其餘定義保留。完整候選必須通過所有 workload Snapshot 驗證後才能建立版本。"),Ui.Expand("本次實際送出的 SELECT",Ui.Code(r.SentSelect,220)));
   foreach(var i in Enumerable.Range(0,r.Cases.Length))
   {
    var item=r.Cases[i];Card("Workload "+(i+1),Ui.Text(item.Source+" · "+item.ObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),Ui.Code(string.Join("\n",item.Values.Select(v=>v.Key+" = "+v.Value)),130));
    if(i<r.Preflights.Count){var p=r.Preflights[i];var checks=Stack();foreach(var gate in p.Gates)checks.Children.Add(Ui.Text((gate.Passed?"通過：":"未過：")+gate.Name+" · "+gate.Reason,12,gate.Passed?"#4B5563":"#B91C1C"));Card("安全預檢",Ui.Text(p.Gates.Count(g=>g.Passed)+"/6 項通過",14,p.Passed?"#047857":"#B91C1C"),Ui.Text("這是編譯與欄位檢查，尚不代表資料結果一致。"),Ui.Expand("查看安全預檢項目",checks,true),Ui.Expand("原版 estimated plan",Ui.Code(p.Original?.Xml,160)),Ui.Expand("候選 estimated plan",Ui.Code(p.Candidate?.Xml,160)));}
    if(i<r.Runs.Count){var run=r.Runs[i];var values=Stack(Ui.Text(run.Reason,13,run.Passed?"#047857":"#B91C1C"),Ui.Text(run.CleanupWarning));if(run.Original?.Length==3&&run.Candidate?.Length==3){values.Children.Add(Ui.Text($"結果一致：{run.Original.Concat(run.Candidate).Select(x=>x.Digest).Distinct().Count()==1}\nMedian reads {DryRunResult.Median(run.Original.Select(x=>x.Reads))} → {DryRunResult.Median(run.Candidate.Select(x=>x.Reads))}\nCPU {DryRunResult.Median(run.Original.Select(x=>x.CpuMs))} → {DryRunResult.Median(run.Candidate.Select(x=>x.CpuMs))} ms"));values.Children.Add(Ui.Expand("原版 actual plan",Ui.Code(run.Original[0].Plan,200)));values.Children.Add(Ui.Expand("候選 actual plan",Ui.Code(run.Candidate[0].Plan,200)));}Card("SP Snapshot Dry Run",values);}
   }
   Card("優化說明",Ui.Text(r.Analysis?.Summary),Ui.Text(r.Explanation),Ui.Text(string.Join("\n",r.Analysis?.Warnings??new string[0]),12,"#92400E"));
   if(r.PreflightPassed&&!r.Passed&&!busy)page.Children.Add(Async("SP Snapshot Dry Run",()=>DryRun(row),true));
   if(r.Passed&&!busy){if(!r.Version.HasValue)page.Children.Add(Async("接受並建立版本",()=>Accept(row),true));else{if(versions.Load(context,row.Module).Versions.Single(v=>v.Number==r.Version.Value).State!="已套用")page.Children.Add(Async("套用到資料庫",()=>Apply(row),true));page.Children.Add(Action("匯出",()=>MessageBox.Show(versions.Export(context,row.Module),"版本匯出")));}}
  }
  async Task Accept(SpScanRow row)
  {
   var r=row.Review;if(!r.Passed)throw new InvalidOperationException("驗證未通過。");await SpReviewWorker.AssertUnchanged(context,row,r.OriginalHash,CancellationToken.None);var reason=MapControl.AskReason("AI 優化："+row.Module.FullName);if(reason==null)return;r.Version=versions.Create(context,row.Module,r.CandidateSql,reason,"AI").Number;MapControl.NotifyVersionChanged();r.Status="V"+r.Version+" 未套用";Render();
  }
  async Task Apply(SpScanRow row)
  {
   if(!row.Review.Passed||!row.Review.Version.HasValue)throw new InvalidOperationException("必須先通過驗證並建立版本。");var folder=versions.Prepare(context,row.Module,row.Review.Version.Value);if(MessageBox.Show("套用 "+row.Module.FullName+" 到 "+context.Server+" / "+context.Database+"？\n已建立 up/down migration："+folder,"確認套用 SP 版本",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;await versions.Apply(context,row.Module,row.Review.Version.Value,false,CancellationToken.None);AnalysisHistory.RecordSp(context,row,"apply","applied");row.Review.Status="V"+row.Review.Version+" 已套用";MapControl.NotifyVersionChanged();Render();
  }
  async Task StartBatch(string id=null,string action=null){if(busy||batch.Busy)return;reviewRow=null;var task=batch.Run(scan,top,budget,id,action);Render();await task;Render();}
  void BudgetDialog()
  {
   var panel=Stack();panel.Margin=new Thickness(20);var labels=new[]{"Top N (1–100)","整批秒數","CPU ms","Logical reads","Tokens"};var values=new[]{top.ToString(),budget.TimeSeconds.ToString(),budget.CpuMs.ToString(),budget.LogicalReads.ToString(),budget.Tokens.ToString()};var fields=values.Select(x=>new TextBox{Text=x,MinWidth=260,Margin=new Thickness(0,0,0,8)}).ToArray();for(int i=0;i<fields.Length;i++){panel.Children.Add(Ui.Text(labels[i]));panel.Children.Add(fields[i]);}var window=new Window{Title="批次預算",Content=panel,Width=370,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterScreen};panel.Children.Add(Ui.Button("取消",()=>window.DialogResult=false));panel.Children.Add(Ui.Button("儲存",()=>{try{var n=int.Parse(fields[0].Text);var b=new SpBudget{TimeSeconds=long.Parse(fields[1].Text),CpuMs=long.Parse(fields[2].Text),LogicalReads=long.Parse(fields[3].Text),Tokens=long.Parse(fields[4].Text)};b.Validate();if(n<1||n>100)throw new Exception();top=n;budget.TimeSeconds=b.TimeSeconds;budget.CpuMs=b.CpuMs;budget.LogicalReads=b.LogicalReads;budget.Tokens=b.Tokens;window.DialogResult=true;}catch{MessageBox.Show("請輸入有效預算。");}},true));window.ShowDialog();Render();
  }
  async Task History()
  {
   if(context==null)return;var jobs=await batch.List(context,CancellationToken.None);var panel=Stack();panel.Margin=new Thickness(16);panel.Children.Add(Ui.Text("批次紀錄 · 相同資料庫",18));if(jobs.Count==0)panel.Children.Add(Ui.Text("尚無批次紀錄。"));var window=new Window{Title="批次紀錄",Width=820,Height=650,Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},WindowStartupLocation=WindowStartupLocation.CenterScreen};
   foreach(var job in jobs.Reverse())
   {var id=job["id"].Value<string>();var row=Stack(Ui.Text(id+" · "+SpBatch.State(job),14),Ui.Text(SpBatch.Timestamp(job["createdAt"]).Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),Ui.Code(string.Join("\n",job["items"].Select(i=>$"objectId {i["objectId"]} · {i["status"]} · attempts {i["attempts"]} · CPU {i["cpuMs"]} · IO {i["logicalReads"]} · tokens {i["tokens"]}")),140));var buttons=new WrapPanel();
    void Run(string action){window.Close();ThreadHelper.JoinableTaskFactory.RunAsync(()=>StartBatch(id,action));}
    if(SpBatch.State(job)=="可續跑")buttons.Children.Add(Action("同 DB 續跑",()=>Run(null)));if(job["items"].Any(i=>i["status"].Value<string>()=="failed"&&i["attempts"].Value<int>()<=1)&&job["status"].Value<string>()!="budget-exhausted")buttons.Children.Add(Action("重試失敗項目",()=>Run("retry")));buttons.Children.Add(Action("重新執行",()=>Run("replay")));buttons.Children.Add(Action("Demo replay（重新實測）",()=>Run("demo-replay")));row.Children.Add(buttons);panel.Children.Add(Ui.Card(row));}
   window.ShowDialog();
  }
  public void Dispose(){governance?.Dispose();disposed=true;single?.Cancel();batch.Changed-=BatchChanged;batch.Dispose();map.Dispose();}
 }
}
