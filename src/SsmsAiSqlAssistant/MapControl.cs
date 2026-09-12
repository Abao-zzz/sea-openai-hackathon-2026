using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell;
namespace Alyvo.SsmsAiSqlAssistant
{
 [Guid("6e4227cb-8842-41bf-a443-cd95239bf1fe")]
 public sealed class MapWindow:ToolWindowPane
 {
  public MapWindow():base(null){Caption="SP 呼叫地圖";Content=new MapControl();}
  protected override void Dispose(bool disposing){if(disposing)(Content as MapControl)?.Dispose();base.Dispose(disposing);}
 }
 public sealed class MapTreeNode
 {
  public int? Id;public MapEdge Edge;public int[] Path;public bool Cycle;
 }
 public sealed class MapControl:UserControl,IDisposable
 {
  static event Action VersionChanged;
  readonly StackPanel page=new StackPanel(),crumbs=new StackPanel{Orientation=Orientation.Horizontal},inspector=new StackPanel();
  readonly TextBlock summary=Ui.Text("尚未載入地圖",13),count=Ui.Text("",12),status=Ui.Text("",12);
  readonly TextBox search=new TextBox{MinWidth=180,Height=30,VerticalContentAlignment=VerticalAlignment.Center,ToolTip="搜尋 SP 名稱、用途或負責人"};
  readonly DispatcherTimer bringTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime+80)};
  readonly TreeView tree=new TreeView{BorderThickness=new Thickness(0),Background=Brushes.White,MinHeight=320};
  readonly Grid columns=new Grid();readonly Border treeCard,inspectorCard;readonly ScrollViewer scroll;
  readonly VersionRepository versions=new VersionRepository();readonly List<int> navigation=new List<int>();CancellationTokenSource cancellation;ConnectionContext context;MapGraph graph;MapModule selected;bool disposed,busy;int expansion=2;
  public MapControl()
  {
   UseLayoutRounding=true;SnapsToDevicePixels=true;Background=Ui.Brush("#F6F7F9");FontFamily=new FontFamily("Segoe UI");FontSize=13;page.Margin=new Thickness(16,16,16,22);page.MaxWidth=1120;page.HorizontalAlignment=HorizontalAlignment.Stretch;
   var header=new DockPanel();var actions=new StackPanel{Orientation=Orientation.Horizontal};actions.Children.Add(Button("用量中心",()=>Ui.Open(LocalApi.BaseUrl+"/usage/ui")));actions.Children.Add(AsyncButton("重新載入地圖",Reload));DockPanel.SetDock(actions,Dock.Right);header.Children.Add(actions);header.Children.Add(Ui.Text("SP 呼叫地圖",20));var headerPanel=new StackPanel();headerPanel.Children.Add(header);
   headerPanel.Children.Add(Ui.Text("只讀 SP 結構與靜態呼叫關係；不讀歷史效能、不送 OpenAI。",12,"#6B7280"));summary.Margin=new Thickness(0,14,0,12);page.Children.Add(Card(summary));
   var filter=new DockPanel();var searchButton=Button("搜尋",RenderTree);DockPanel.SetDock(searchButton,Dock.Right);filter.Children.Add(searchButton);DockPanel.SetDock(count,Dock.Right);count.Margin=new Thickness(10,8,10,0);filter.Children.Add(count);var searchBox=new Grid();searchBox.Children.Add(search);var hint=Ui.Text("搜尋 SP 名稱、用途或負責人",12,"#6B7280");hint.IsHitTestVisible=false;hint.Margin=new Thickness(8,6,0,0);searchBox.Children.Add(hint);search.TextChanged+=(s,e)=>hint.Visibility=search.Text.Length==0?Visibility.Visible:Visibility.Collapsed;filter.Children.Add(searchBox);page.Children.Add(filter);search.KeyDown+=SearchKey;
   page.Children.Add(status);crumbs.Margin=new Thickness(0,12,0,8);page.Children.Add(crumbs);
   var left=new StackPanel();left.Children.Add(Ui.Text("SP / View 呼叫樹",17));var tools=new WrapPanel();tools.Children.Add(Button("展開 2 層",()=>Expand(2)));tools.Children.Add(Button("展開 4 層",()=>Expand(4)));tools.Children.Add(Button("全部收合",()=>Expand(0)));left.Children.Add(tools);left.Children.Add(tree);
   treeCard=Card(left);inspectorCard=Card(inspector);columns.Children.Add(treeCard);columns.Children.Add(inspectorCard);page.Children.Add(columns);scroll=new ScrollViewer{Content=page,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};var shell=new DockPanel();var head=new Border{Child=headerPanel,Background=Brushes.White,Padding=new Thickness(16,12,16,10),BorderBrush=Ui.Brush("#DDE1E6"),BorderThickness=new Thickness(0,0,0,1)};DockPanel.SetDock(head,Dock.Top);shell.Children.Add(head);shell.Children.Add(scroll);Content=shell;
   tree.Resources[SystemColors.HighlightBrushKey]=Ui.Brush("#DBEAFE");tree.Resources[SystemColors.HighlightTextBrushKey]=Ui.Brush("#111827");tree.Resources[SystemColors.InactiveSelectionHighlightBrushKey]=Ui.Brush("#EFF6FF");bringTimer.Tick+=BringInspector;tree.SelectedItemChanged+=Selected;tree.PreviewMouseDoubleClick+=DoubleClicked;tree.AddHandler(TreeViewItem.ExpandedEvent,new RoutedEventHandler(Expanded));SizeChanged+=Resized;VersionChanged+=RefreshVersion;LayoutColumns();
  }
  static Border Card(UIElement child)=>new Border{Child=child,Background=Brushes.White,BorderBrush=Ui.Brush("#D1D5DB"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(4),Padding=new Thickness(14),Margin=new Thickness(0,0,10,10)};
  static Button Button(string text,Action action)=>Ui.Button(text,()=>{try{action();}catch(Exception e){MessageBox.Show(e.Message,"SP 呼叫地圖");}});
  static Button AsyncButton(string text,Func<Task> action)=>Ui.AsyncButton(text,async()=>{try{await action();}catch(Exception e){MessageBox.Show(e.Message,"SP 呼叫地圖");}});
  readonly Dictionary<int,string> baselineErrors=new Dictionary<int,string>();
  void EnsureBaselines(){baselineErrors.Clear();foreach(var m in graph.Modules.Values.Where(x=>x.IsProcedure)){try{versions.EnsureBaseline(context,m);}catch(Exception e){baselineErrors[m.Id]=e.Message;}}}
  public async Task Load(ConnectionContext connection){context=connection;navigation.Clear();await Reload();}
  public void LoadSnapshot(ConnectionContext connection,MapGraph snapshot,string filter=""){context=connection;graph=snapshot;EnsureBaselines();navigation.Clear();search.Text=filter??"";status.Text="觀察時間 "+snapshot.ObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");RenderTree();RenderInspector();}
  async Task Reload()
  {
   if(context==null||busy)return;busy=true;cancellation?.Cancel();cancellation?.Dispose();cancellation=new CancellationTokenSource();status.Text="正在唯讀載入 catalog…";
   try{var fresh=await new MapCollector().Load(context,cancellation.Token);if(disposed)return;graph=fresh;EnsureBaselines();navigation.RemoveAll(id=>!graph.Modules.ContainsKey(id));selected=selected!=null&&graph.Modules.TryGetValue(selected.Id,out var m)?m:null;status.Text="觀察時間 "+graph.ObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");RenderTree();RenderInspector();}
   catch(Exception e){if(!disposed)status.Text="載入失敗："+e.Message;throw;}finally{busy=false;}
  }
  void SearchKey(object sender,KeyEventArgs e){if(e.Key==Key.Enter){e.Handled=true;RenderTree();}}
  void Resized(object sender,SizeChangedEventArgs e)=>LayoutColumns();
  void LayoutColumns(){columns.ColumnDefinitions.Clear();columns.RowDefinitions.Clear();bool wide=ActualWidth>=900;if(wide){columns.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});columns.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(380)});}else{columns.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});columns.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});}Grid.SetColumn(inspectorCard,wide?1:0);Grid.SetRow(inspectorCard,wide?0:1);tree.MaxHeight=wide?Math.Max(340,ActualHeight-250):420;}
  void RenderTree()
  {
   if(graph==null)return;tree.Items.Clear();crumbs.Children.Clear();crumbs.Children.Add(Button("主頁",()=>{navigation.Clear();RenderTree();}));for(int i=0;i<navigation.Count;i++){var index=i;crumbs.Children.Add(Ui.Text(" › ",14));if(i==navigation.Count-1){var label=Ui.Text(graph.Modules[navigation[i]].FullName,13);label.FontWeight=FontWeights.SemiBold;crumbs.Children.Add(label);}else crumbs.Children.Add(Button(graph.Modules[navigation[i]].FullName,()=>{navigation.RemoveRange(index+1,navigation.Count-index-1);RenderTree();}));}
   summary.Text=context.Database+"　SP "+graph.ProcedureCount+" 支 · "+graph.Edges.Count+" 條關係 · "+graph.Roots().Length+" 個入口　獨立 SP "+graph.IndependentCount+" 支";
   var matches=graph.Search(search.Text);var ids=string.IsNullOrWhiteSpace(search.Text)?(navigation.Count==0?graph.Roots():new[]{navigation.Last()}):matches.Select(m=>m.Id).ToArray();count.Text="清單 "+matches.Length+" · 地圖 "+graph.ProcedureCount+" 支";
   foreach(var id in ids)tree.Items.Add(Item(id,null,new int[0]));if(ids.Length==0)tree.Items.Add(new TreeViewItem{Header="沒有符合搜尋條件的 SP / View。",IsEnabled=false});Expand(expansion);
  }
  TreeViewItem Item(int? id,MapEdge edge,int[] path)
  {
   bool cycle=id.HasValue&&path.Contains(id.Value);var data=new MapTreeNode{Id=id,Edge=edge,Path=path,Cycle=cycle};var label=id.HasValue?graph.Modules[id.Value].FullName:"未解析 · "+edge.Label;label+=id.HasValue?(graph.Modules[id.Value].IsProcedure?"  SP":"  View"):"";
   if(cycle)label+=" · 循環";else if(id.HasValue&&graph.Up(id.Value)>1)label+=" · 共用";if(!string.IsNullOrEmpty(edge?.Condition))label+=" · 條件："+edge.Condition;
   var nodeHeader=new WrapPanel();nodeHeader.Children.Add(Ui.Text(label,13));if(id.HasValue){var calls=graph.DownstreamCallLines(id.Value);if(calls.Length>0){var lineLabel=Ui.Text(calls,12,"#475569");lineLabel.Margin=new Thickness(12,3,0,3);lineLabel.ToolTip="呼叫行號以此 SP 掃描時的完整定義為準，不含 Query Editor 額外加入的 USE / GO。";nodeHeader.Children.Add(lineLabel);}}
   var item=new TreeViewItem{Header=nodeHeader,Tag=data,Padding=new Thickness(4,7,4,7),HorizontalContentAlignment=HorizontalAlignment.Stretch};item.ToolTip=edge==null?label:edge.Reason+"\nline "+edge.Line+" "+edge.Evidence;
   if(id.HasValue&&!cycle&&graph.Down(id.Value).Any())item.Items.Add(new TreeViewItem{Header="展開讀取本機關係",Tag="deferred"});return item;
  }
  void Populate(TreeViewItem item){var data=item.Tag as MapTreeNode;if(data?.Id==null||data.Cycle||item.Items.Count!=1||!Equals((item.Items[0] as TreeViewItem)?.Tag,"deferred"))return;item.Items.Clear();foreach(var edge in graph.Down(data.Id.Value))item.Items.Add(Item(edge.Callee,edge,data.Path.Concat(new[]{data.Id.Value}).ToArray()));}
  void Expanded(object sender,RoutedEventArgs e){if(e.OriginalSource is TreeViewItem item)Populate(item);}
  void Expand(int depth){expansion=depth;void Walk(TreeViewItem item,int level){item.IsExpanded=level<depth;if(item.IsExpanded){Populate(item);foreach(TreeViewItem child in item.Items)Walk(child,level+1);}}foreach(TreeViewItem item in tree.Items)Walk(item,1);}
  void Selected(object sender,RoutedPropertyChangedEventArgs<object> e){var node=(e.NewValue as TreeViewItem)?.Tag as MapTreeNode;if(node?.Id!=null){selected=graph.Modules[node.Id.Value];RenderInspector();if(ActualWidth<900){bringTimer.Stop();bringTimer.Start();}}else if(node?.Edge!=null){inspector.Children.Clear();inspector.Children.Add(Ui.Text("未解析 · "+node.Edge.Label,17));inspector.Children.Add(Ui.Text(node.Edge.Reason));inspector.Children.Add(Ui.Code("line "+node.Edge.Line+"\n"+node.Edge.Evidence,200));}}
  public static MapTreeNode ClickedNode(DependencyObject source){while(source!=null){if(source is TreeViewItem item)return item.Tag as MapTreeNode;source=source is Visual?VisualTreeHelper.GetParent(source):LogicalTreeHelper.GetParent(source);}return null;}
  void BringInspector(object sender,EventArgs e){bringTimer.Stop();if(!disposed&&ActualWidth<900)inspectorCard.BringIntoView();}
  void DoubleClicked(object sender,MouseButtonEventArgs e){bringTimer.Stop();var node=ClickedNode(e.OriginalSource as DependencyObject);if(node?.Id==null)return;e.Handled=true;var module=graph.Modules[node.Id.Value];if(module.IsProcedure){try{MapIntegration.OpenQuery(context,module);}catch(Exception error){MessageBox.Show(error.Message,"開啟 SP");}}}
  void Scope(){if(selected==null)return;if(navigation.Count==0||navigation.Last()!=selected.Id)navigation.Add(selected.Id);search.Clear();RenderTree();}
  void RenderInspector()
  {
   inspector.Children.Clear();if(selected==null){inspector.Children.Add(Ui.Text("選取節點以查看 SP / View 資訊",14));return;}var module=selected;inspector.Children.Add(Ui.Text(module.FullName,18));inspector.Children.Add(Ui.Text((module.IsProcedure?"SP":"View")+"　上游 "+graph.Up(module.Id)+" · 下游 "+graph.Down(module.Id).Count(),12,"#2563EB"));inspector.Children.Add(Ui.Text("最後變更 "+module.Modified.ToString("yyyy-MM-dd HH:mm:ss")+"\n定義："+(module.Definition==null?(module.Encrypted?"加密":"未回傳"):"完整"),12));
   var actions=new WrapPanel();if(graph.Down(module.Id).Any())actions.Children.Add(Button("查看下游",Scope));if(module.IsProcedure&&module.Definition!=null){actions.Children.Add(Button("開 Query Editor",()=>MapIntegration.OpenQuery(context,module)));actions.Children.Add(Button("建立版本",()=>{var reason=AskReason();if(reason==null)return;versions.Create(context,module,module.Definition,reason);NotifyVersionChanged();}));actions.Children.Add(Button("匯出",()=>MessageBox.Show(versions.Export(context,module),"已匯出 SQL + history.json")));}inspector.Children.Add(actions);
   var tabs=new TabControl{Margin=new Thickness(0,12,0,0)};var basic=new StackPanel{Margin=new Thickness(8)};basic.Children.Add(Ui.Text("用途\n"+module.Purpose+"\n\n負責單位\n"+module.Owner+"\n\n資料庫註解來源\nsys.sql_modules definition\nDeclared revision: "+module.Revision));foreach(var edge in graph.Down(module.Id))basic.Children.Add(Ui.Text("\n→ "+(edge.Callee.HasValue?graph.Modules[edge.Callee.Value].FullName:"未解析 · "+edge.Label)+"\n"+edge.Condition+"\n"+edge.Reason+"\nline "+edge.Line+" · "+edge.Evidence,12));tabs.Items.Add(new TabItem{Header="基本資料",Content=basic});
   var sqlPanel=new StackPanel();sqlPanel.Children.Add(Ui.Code(module.Definition??"SQL Server 沒有回傳這支 SP 的完整定義。",380));if(module.Definition!=null)sqlPanel.Children.Add(Button("複製完整 SQL",()=>Clipboard.SetText(module.Definition)));tabs.Items.Add(new TabItem{Header="完整 SQL",Content=sqlPanel});
   var history=new StackPanel{Margin=new Thickness(8)};history.Children.Add(Ui.Text("選取版本以查看內容；還原會新增版本，保留既有歷史。",12));
   if(module.IsProcedure)
   {
    var records=versions.Load(context,module).Versions.OrderByDescending(v=>v.Number).ToArray();
    if(records.Length==0)history.Children.Add(Ui.Text(baselineErrors.TryGetValue(module.Id,out var baselineError)?baselineError:"尚未建立版本。",12,"#92400E"));
    else
    {
     var picker=new ComboBox{ItemsSource=records.Select(v=>"V"+v.Number+" · "+v.Kind+" · "+v.State).ToArray(),Margin=new Thickness(0,8,0,10),MinHeight=30};var detail=new StackPanel();
     void ShowVersion(){detail.Children.Clear();if(picker.SelectedIndex<0)return;var v=records[picker.SelectedIndex];detail.Children.Add(Ui.Text("V"+v.Number+" · "+v.Kind+" · "+v.State,15));detail.Children.Add(Ui.Text(v.Author+"\n"+v.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")+"\n原因："+v.Reason+(v.RestoredFromVersion.HasValue?"\n還原來源：V"+v.RestoredFromVersion:""),11));detail.Children.Add(Ui.Code(v.Sql,300));detail.Children.Add(Ui.Expand("此版差異",Ui.Code(v.Diff,200),true));var buttons=new WrapPanel();buttons.Children.Add(Button("查看此版完整 SQL",()=>ShowSql("V"+v.Number,v.Sql)));if(v.State=="未套用")buttons.Children.Add(AsyncButton("套用到資料庫",()=>Apply(module,v,false)));buttons.Children.Add(AsyncButton("還原此版並建立新版本",()=>RestoreVersion(module,v)));detail.Children.Add(buttons);}
     picker.SelectionChanged+=(s,e)=>ShowVersion();history.Children.Add(picker);history.Children.Add(detail);picker.SelectedIndex=0;
    }
   }
   else history.Children.Add(Ui.Text("View 不提供 SP 套用操作。"));tabs.Items.Add(new TabItem{Header="版本紀錄",Content=history});inspector.Children.Add(tabs);
  }
  async Task RestoreVersion(MapModule module,SpVersion target)
  {
   if(busy)return;var reason=AskReason("還原至 V"+target.Number);if(reason==null)return;SpVersion created;busy=true;
   try{var current=await MapIntegration.ReadCurrentDefinition(context,module);created=versions.CreateRestore(context,module,target.Number,current,reason);}finally{busy=false;}
   NotifyVersionChanged();await Apply(module,created,false);
  }
  async Task Apply(MapModule module,SpVersion version,bool restore){if(busy)return;var operation=restore?"還原":"套用";var migrations=versions.Prepare(context,module,version.Number);if(MessageBox.Show(operation+" V"+version.Number+" 到 "+context.Server+" / "+context.Database+" / "+module.FullName+"？\n將以 transaction 執行 CREATE OR ALTER，並回讀 hash。\nup/down migration："+migrations,operation+"到資料庫",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;busy=true;try{await versions.Apply(context,module,version.Number,restore,CancellationToken.None);}finally{busy=false;}await Reload();}
  static void ShowSql(string title,string sql){var window=new Window{Title=title,Width=760,Height=560,Content=Ui.Code(sql,500),WindowStartupLocation=WindowStartupLocation.CenterScreen};window.ShowDialog();}
  public static string AskReason(string initial=""){var reason=new TextBox{Text=initial,MinHeight=70,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap};var panel=new StackPanel{Margin=new Thickness(20)};panel.Children.Add(Ui.Text("本次修改原因",15));panel.Children.Add(reason);var window=new Window{Title="建立目前 SP 版本",Content=panel,Width=440,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterScreen};var row=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};row.Children.Add(Ui.Button("取消",()=>window.DialogResult=false));row.Children.Add(Ui.Button("建立版本",()=>{if(!string.IsNullOrWhiteSpace(reason.Text))window.DialogResult=true;else reason.Focus();},true));panel.Children.Add(row);return window.ShowDialog()==true?reason.Text:null;}
  public static void NotifyVersionChanged()=>VersionChanged?.Invoke();
  void RefreshVersion(){Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>{if(!disposed){RenderTree();RenderInspector();}}));}
  public void Dispose(){disposed=true;bringTimer.Stop();bringTimer.Tick-=BringInspector;cancellation?.Cancel();cancellation?.Dispose();VersionChanged-=RefreshVersion;SizeChanged-=Resized;search.KeyDown-=SearchKey;tree.SelectedItemChanged-=Selected;tree.PreviewMouseDoubleClick-=DoubleClicked;tree.RemoveHandler(TreeViewItem.ExpandedEvent,new RoutedEventHandler(Expanded));}
 }
}




