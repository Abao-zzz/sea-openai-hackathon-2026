using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
namespace Alyvo.SsmsAiSqlAssistant
{
 public sealed class SqlChangeLine
 {
  public string OldText {get;set;} public string NewText {get;set;}
  public string OldColor {get;set;} public string NewColor {get;set;}
  public bool Changed {get;set;}
 }
 public static class SqlComparison
 {
  public static List<SqlChangeLine> Compare(string original,string candidate)
  {
   var a=(original??"").Replace("\r\n","\n").Split('\n');var b=(candidate??"").Replace("\r\n","\n").Split('\n');var result=new List<SqlChangeLine>();int i=0,j=0;
   int[,] lcs=null;if((long)(a.Length+1)*(b.Length+1)<=2000000){lcs=new int[a.Length+1,b.Length+1];for(int x=a.Length-1;x>=0;x--)for(int y=b.Length-1;y>=0;y--)lcs[x,y]=a[x]==b[y]?1+lcs[x+1,y+1]:Math.Max(lcs[x+1,y],lcs[x,y+1]);}
   while(i<a.Length||j<b.Length)
   {
    if(i<a.Length&&j<b.Length&&a[i]==b[j]){result.Add(new SqlChangeLine{OldText=$"{i+1}  {a[i]}",NewText=$"{j+1}  {b[j]}",OldColor="#FFFFFF",NewColor="#FFFFFF"});i++;j++;}
    else if(i<a.Length&&(j==b.Length||lcs==null||lcs[i+1,j]>=lcs[i,j+1])){result.Add(new SqlChangeLine{OldText=$"− {i+1}  {a[i++]}",NewText="",OldColor="#FEE2E2",NewColor="#F8FAFC",Changed=true});}
    else {result.Add(new SqlChangeLine{OldText="",NewText=$"+ {j+1}  {b[j++]}",OldColor="#F8FAFC",NewColor="#DCFCE7",Changed=true});}
   }
   return result;
  }
  public static UIElement View(string original,string candidate,bool applied=false)
  {
   var lines=Compare(original,candidate);var panel=new StackPanel();panel.Children.Add(Ui.Text((applied?"左：修改前舊版 SP　右：本次已套用版本":"左：掃描時舊版 SP　右：AI 新版候選（尚未套用）")+"\n紅色 − 為刪除，綠色 + 為新增；修改以刪除舊行及新增新行呈現。",12,"#334155"));
   var table=new DataGrid{ItemsSource=lines,AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,CanUserSortColumns=false,CanUserReorderColumns=false,EnableRowVirtualization=true,Height=360,HeadersVisibility=DataGridHeadersVisibility.Column,Background=Brushes.White,Foreground=Ui.Brush("#111827"),GridLinesVisibility=DataGridGridLinesVisibility.Vertical,RowHeaderWidth=0};
   foreach(var side in new[]{"Old","New"}){var label=new FrameworkElementFactory(typeof(TextBlock));label.SetBinding(TextBlock.TextProperty,new Binding(side+"Text"));label.SetBinding(TextBlock.BackgroundProperty,new Binding(side+"Color"));label.SetValue(TextBlock.ForegroundProperty,Ui.Brush("#111827"));label.SetValue(TextBlock.FontFamilyProperty,new FontFamily("Consolas"));label.SetValue(TextBlock.FontSizeProperty,12d);label.SetValue(TextBlock.PaddingProperty,new Thickness(5));label.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);table.Columns.Add(new DataGridTemplateColumn{Header=side=="Old"?"舊版 SP（行號）":applied?"已套用版本（行號）":"新版候選（行號）",Width=new DataGridLength(1,DataGridLengthUnitType.Star),CellTemplate=new DataTemplate{VisualTree=label}});}
   var onlyChanges=new CheckBox{Content="只看差異",Margin=new Thickness(0,6,0,8),Foreground=Ui.Brush("#111827")};onlyChanges.Checked+=(s,e)=>table.ItemsSource=lines.Where(x=>x.Changed).ToArray();onlyChanges.Unchecked+=(s,e)=>table.ItemsSource=lines;panel.Children.Add(onlyChanges);if(!lines.Any(x=>x.Changed))panel.Children.Add(Ui.Text("新版與舊版內容相同。"));panel.Children.Add(table);
   var actions=new WrapPanel();actions.Children.Add(Ui.Button("複製舊版",()=>Clipboard.SetText(original??"")));actions.Children.Add(Ui.Button("複製新版",()=>Clipboard.SetText(candidate??"")));panel.Children.Add(actions);return panel;
  }
 }
}