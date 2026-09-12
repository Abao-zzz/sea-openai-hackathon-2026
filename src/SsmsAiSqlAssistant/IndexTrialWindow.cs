using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
namespace Alyvo.SsmsAiSqlAssistant
{
 public sealed class IndexTrialControl:UserControl
 {
  public event Action CurrentVersionBest;
  public static bool IsCurrentVersionBest(IndexTrialResult value)=>value!=null&&value.RolledBack&&!value.Applied&&!value.Eligible&&IndexTrial.Evaluate(value.Before,value.After)=="Logical reads 未降低至少 5%。"&&DryRunResult.Median(value.Before.Select(x=>x.Reads))<=DryRunResult.Median(value.After.Select(x=>x.Reads));
  readonly ConnectionContext context;readonly string query;readonly string ddl;readonly StackPanel body=new StackPanel();readonly TextBlock status=Ui.Text("");readonly Button test,apply,stop;CancellationTokenSource cancellation;IndexTrialResult result;bool busy,started;
  public IndexTrialControl(ConnectionContext connection,string sql,string indexSql="")
  {
   context=connection;query=sql;ddl=indexSql;Background=Ui.Brush("#F6F7F9");var panel=new StackPanel{Margin=new Thickness(4)};
   panel.Children.Add(Ui.Text("索引效能比較",16));panel.Children.Add(status);panel.Children.Add(body);
   var buttons=new WrapPanel();test=Ui.AsyncButton("測試索引並 rollback",()=>Run(false),true);apply=Ui.AsyncButton("確認建立索引",()=>Run(true),true);apply.IsEnabled=false;apply.Visibility=Visibility.Collapsed;stop=Ui.Button("取消",()=>cancellation?.Cancel());stop.IsEnabled=false;test.Visibility=Visibility.Collapsed;buttons.Children.Add(test);buttons.Children.Add(apply);buttons.Children.Add(stop);panel.Children.Add(buttons);
   var details=new StackPanel();details.Children.Add(Ui.Text(context.Server+" / "+context.Database,13));details.Children.Add(Ui.Text("直接在目前資料庫 transaction 內測量、建立索引、重測後 rollback。測試會鎖住目標資料表；等待鎖最多 5 秒，整批預算 300 秒。",12,"#92400E"));details.Children.Add(Ui.Code(query,150));panel.Children.Add(Ui.Expand("測試查詢與執行環境",details));Content=panel;status.Text="等待自動測試索引…";
   Loaded+=async(s,e)=>{if(started)return;started=true;await Run(false);};

  }
  async Task Run(bool commit)
  {
   if(busy)return;if(commit&&MessageBox.Show("正式建立索引到 "+context.Server+" / "+context.Database+"？\n此動作會提交，不再 rollback。\n\n"+ddl,"確認建立索引",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;busy=true;test.IsEnabled=apply.IsEnabled=false;stop.IsEnabled=true;cancellation=new CancellationTokenSource();
   try{if(commit){await IndexTrial.Apply(context,result,cancellation.Token);status.Text="索引已正式建立並提交。";}else{result=null;body.Children.Clear();result=await IndexTrial.Test(context,query,ddl,cancellation.Token,new Progress<string>(s=>status.Text=s));status.Text=result.Eligible?"結果一致，讀取量改善達標。測試已 rollback，可決定是否正式建立索引。":result.Reason;}ShowResult();}
   catch(Exception e){status.Text=(result?.Applied==true?"索引已提交，但後續紀錄失敗，請勿重複建立：":"未完成：")+DbWorker.SafeError(e);}
   finally{busy=false;cancellation.Dispose();cancellation=null;test.IsEnabled=true;test.Content="重試索引測試";test.Visibility=result==null?Visibility.Visible:Visibility.Collapsed;stop.IsEnabled=false;apply.IsEnabled=result?.Eligible==true&&!result.Applied;apply.Visibility=apply.IsEnabled?Visibility.Visible:Visibility.Collapsed;}
  }
  void ShowResult()
  {
   body.Children.Clear();if(result==null)return;if(IsCurrentVersionBest(result)){status.Text="目前已是本次測試中的最優版本";foreach(UIElement child in ((StackPanel)Content).Children)child.Visibility=ReferenceEquals(child,status)?Visibility.Visible:Visibility.Collapsed;CurrentVersionBest?.Invoke();return;}body.Children.Add(Ui.Text(result.Applied?"已正式提交":result.RolledBack?"測試索引已 rollback，資料庫未保留測試索引":"Rollback 尚未確認",15));if(result.Before==null||result.After==null)return;
   long Median(RunMetrics[] samples,Func<RunMetrics,long> field)=>DryRunResult.Median(samples.Select(field));var rows=new[]{new{指標="Logical reads",之前=Median(result.Before,x=>x.Reads),之後=Median(result.After,x=>x.Reads)},new{指標="CPU (ms)",之前=Median(result.Before,x=>x.CpuMs),之後=Median(result.After,x=>x.CpuMs)},new{指標="Elapsed (ms)",之前=Median(result.Before,x=>x.ElapsedMs),之後=Median(result.After,x=>x.ElapsedMs)}};body.Children.Add(new DataGrid{ItemsSource=rows.Select(x=>new{x.指標,x.之前,x.之後,改善=IndexBenchmark.Improvement(x.之前,x.之後)}).ToArray(),AutoGenerateColumns=true,IsReadOnly=true,CanUserAddRows=false,MinHeight=125});body.Children.Add(Ui.Text("各三次取中位數。耗時／CPU 含編譯與測量開銷；建立索引會影響快取，因此差異不全代表索引收益。未驗證寫入成本與所有參數分布。",12,"#92400E"));
   foreach(var phase in new[]{Tuple.Create("之前",result.Before),Tuple.Create("之後",result.After)}){var detail=new StackPanel();foreach(var sample in phase.Item2){detail.Children.Add(Ui.Text($"reads {sample.Reads} · CPU {sample.CpuMs} ms · elapsed {sample.ElapsedMs} ms · rows {sample.Rows}\n{sample.Shape}",11));if(sample.SavedPlan!=null&&!sample.SavedPlan.Oversize)detail.Children.Add(Ui.Button("開啟 actual plan",()=>Ui.Open(sample.SavedPlan.Path)));}body.Children.Add(Ui.Expand(phase.Item1+"逐次數據與計畫",detail));}
   if(result.Receipt!=null)body.Children.Add(Ui.Button("開啟本機測試／建立紀錄",()=>Ui.Open(result.Receipt)));
  }
 }
}
