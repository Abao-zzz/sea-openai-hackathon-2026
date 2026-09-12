using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace Alyvo.SsmsAiSqlAssistant
{
 public sealed class HistoryWindow:Window
 {
  readonly AnalysisHistory store=new AnalysisHistory();readonly StackPanel rows=new StackPanel();readonly TextBox search=new TextBox{Width=180,MinHeight=30};readonly ComboBox stage=new ComboBox{Width=105,ItemsSource=new[]{"全部","analysis","preflight","snapshot","apply","restore"},SelectedIndex=0},status=new ComboBox{Width=115,ItemsSource=new[]{"全部","not-applied","applied","restored","passed","failed"},SelectedIndex=0};readonly DatePicker from=new DatePicker(),to=new DatePicker();List<AnalysisHistoryEntry> visible=new List<AnalysisHistoryEntry>();
  public HistoryWindow()
  {
   Title="分析與套用歷史";Width=1024;Height=720;UseLayoutRounding=true;SnapsToDevicePixels=true;Background=Ui.Brush("#F6F7F9");var body=new DockPanel{Margin=new Thickness(16)};Content=body;
   var header=new StackPanel();header.Children.Add(Ui.Text("分析與套用歷史",20));header.Children.Add(Ui.Text("僅保存 fingerprint、狀態摘要與用量；不保存 SQL、結果、key、connection 或 plan XML。"));var filters=new WrapPanel();foreach(var control in new UIElement[]{Ui.Text("搜尋"),search,Ui.Text("階段"),stage,Ui.Text("狀態"),status,Ui.Text("從"),from,Ui.Text("至"),to})filters.Children.Add(control);header.Children.Add(filters);var actions=new WrapPanel();actions.Children.Add(Ui.Button("重新整理",Refresh));actions.Children.Add(Ui.Button("CSV",Export));actions.Children.Add(Ui.Button("刪除全部",()=>{if(MessageBox.Show(this,"刪除全部分析與套用歷史？此操作無法復原。","刪除歷史",MessageBoxButton.YesNo)==MessageBoxResult.Yes){store.Delete();Refresh();}}));header.Children.Add(actions);DockPanel.SetDock(header,Dock.Top);body.Children.Add(header);body.Children.Add(new ScrollViewer{Content=rows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});search.TextChanged+=(s,e)=>Refresh();stage.SelectionChanged+=(s,e)=>Refresh();status.SelectionChanged+=(s,e)=>Refresh();from.SelectedDateChanged+=(s,e)=>Refresh();to.SelectedDateChanged+=(s,e)=>Refresh();Refresh();
  }
  void Refresh()
  {
   try{visible=store.List().Where(x=>(stage.SelectedIndex==0||x.Stage==(string)stage.SelectedItem)&&(status.SelectedIndex==0||x.ApplyStatus==(string)status.SelectedItem||x.Verification==(string)status.SelectedItem)&&(!from.SelectedDate.HasValue||x.Time.LocalDateTime.Date>=from.SelectedDate.Value.Date)&&(!to.SelectedDate.HasValue||x.Time.LocalDateTime.Date<=to.SelectedDate.Value.Date)&&string.Join(" ",new[]{x.Id,x.DatabaseFingerprint,x.SqlFingerprint,x.Summary,x.Model}).IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)>=0).ToList();rows.Children.Clear();rows.Children.Add(Ui.Text("符合條件："+visible.Count+" 筆"));foreach(var entry in visible){var card=new StackPanel();card.Children.Add(Ui.Text(entry.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")+" · "+entry.Summary,14));card.Children.Add(Ui.Text("模型："+entry.Model+" · input "+entry.InputTokens+" · output "+entry.OutputTokens));card.Children.Add(Ui.Expand("詳細",Ui.Text("ID："+entry.Id+"\nDatabase："+entry.DatabaseFingerprint+"\nSQL fingerprint："+entry.SqlFingerprint+"\nCached："+entry.CachedTokens+" · cache-write："+entry.CacheWriteTokens)));card.Children.Add(Ui.Button("刪除此筆",()=>{if(MessageBox.Show(this,"刪除此筆歷史？","刪除歷史",MessageBoxButton.YesNo)==MessageBoxResult.Yes){store.Delete(entry.Id);Refresh();}}));rows.Children.Add(Ui.Card(card));}}
   catch(Exception e){rows.Children.Clear();rows.Children.Add(Ui.Text("歷史讀取失敗："+DbWorker.SafeError(e),12,"#B91C1C"));}
  }
  void Export(){var dialog=new Microsoft.Win32.SaveFileDialog{FileName="analysis-history.csv",Filter="CSV|*.csv"};if(dialog.ShowDialog(this)==true)try{File.WriteAllText(dialog.FileName,AnalysisHistory.Csv(visible),new UTF8Encoding(true));}catch(Exception e){MessageBox.Show(this,DbWorker.SafeError(e),"匯出失敗");}}
 }
}
