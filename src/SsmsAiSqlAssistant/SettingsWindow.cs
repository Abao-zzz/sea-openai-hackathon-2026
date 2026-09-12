using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace Alyvo.SsmsAiSqlAssistant
{
 public sealed class SettingsWindow:Window
 {
  public SettingsWindow()
  {
   Title="AI 管理與設定";Width=690;Height=650;MinWidth=520;MinHeight=450;UseLayoutRounding=true;SnapsToDevicePixels=true;Background=Ui.Brush("#F6F7F9");
   var body=new StackPanel{Margin=new Thickness(16)};Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
   body.Children.Add(Ui.Text("AI 管理與設定",20));var links=new WrapPanel();foreach(var item in new[]{new[]{"歷史紀錄","/history/ui"},new[]{"用量中心","/usage/ui"},new[]{"治理與變更稽核","/agent/governance/ui"},new[]{"批次紀錄","/jobs/ui"}})links.Children.Add(Ui.Button(item[0],()=>Ui.Open(LocalApi.BaseUrl+item[1])));links.Children.Add(Ui.Button("分析與套用歷史",()=>new HistoryWindow().ShowDialog()));body.Children.Add(Ui.Card(links));
   var service=new StackPanel();var status=Ui.Text("LocalService："+LocalApi.BaseUrl);service.Children.Add(status);service.Children.Add(Ui.AsyncButton("檢查服務與 API",async()=>{status.Text="檢查中…";try{var api=new LocalApi();await api.Check(CancellationToken.None);var health=await api.Send("/health",null,CancellationToken.None);status.Text="服務正常\n"+"模型："+health["model"]+"\nKey 來源："+health["apiKeySource"]+"\nAPI："+health["apiVersion"];}catch(Exception e){status.Text=DbWorker.SafeError(e);}}));body.Children.Add(Ui.Card(service));
   var keyPanel=new StackPanel();keyPanel.Children.Add(Ui.Text("OpenAI key",16));keyPanel.Children.Add(Ui.Text("只儲存在目前 Windows 使用者範圍。設定後需重新啟動 LocalService；不會顯示或寫入診斷紀錄。"));var key=new PasswordBox{MinHeight=30,MaxLength=512,Margin=new Thickness(0,6,0,6)};keyPanel.Children.Add(key);var keyStatus=Ui.Text("");var buttons=new WrapPanel();
   void Save(bool user)
   {
    try{var value=key.Password.Trim();if(value.Length<10||value.Contains("\r")||value.Contains("\n"))throw new InvalidOperationException("請輸入有效 key。");if(user)Environment.SetEnvironmentVariable("OPENAI_API_KEY",value,EnvironmentVariableTarget.User);else SaveLocalKey(value);key.Clear();keyStatus.Text="已儲存。請重新啟動 LocalService；User 環境 key 優先於 local file。";}catch{keyStatus.Text="設定失敗，請檢查輸入與檔案權限。";}
   }
   buttons.Children.Add(Ui.Button("設定 local key",()=>Save(false)));buttons.Children.Add(Ui.Button("設定 user key",()=>Save(true)));buttons.Children.Add(Ui.Button("清除 key",()=>{if(MessageBox.Show(this,"清除目前使用者的 local 與 user OpenAI key？執行中的服務須重啟才會更新。","清除 key",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;try{Environment.SetEnvironmentVariable("OPENAI_API_KEY",null,EnvironmentVariableTarget.User);if(File.Exists(KeyPath))File.Delete(KeyPath);key.Clear();keyStatus.Text="已清除儲存設定；請重新啟動服務。";}catch{keyStatus.Text="清除失敗，請檢查權限。";}}));keyPanel.Children.Add(buttons);keyPanel.Children.Add(keyStatus);body.Children.Add(Ui.Card(keyPanel));
   var diagnostics=new StackPanel();diagnostics.Children.Add(Ui.Text("診斷（不含 SQL / key）",16));diagnostics.Children.Add(Ui.Text("Package loaded："+Diagnostics.PackageLoaded+"\nMEF listener："+Diagnostics.ListenerCount+"\nActive views："+Diagnostics.ActiveViews+"\nSelection events："+Diagnostics.SelectionEvents+"\nLast adornment error："+Diagnostics.LastError));body.Children.Add(Ui.Card(diagnostics));
  }
  private static string KeyPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Alyvo","SsmsAiSqlAssistant","openai.key");
  private static void SaveLocalKey(string key)
  {
   var path=KeyPath;Directory.CreateDirectory(Path.GetDirectoryName(path));var sid=WindowsIdentity.GetCurrent().User;var security=new FileSecurity();security.SetAccessRuleProtection(true,false);security.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,AccessControlType.Allow));
   // Apply the ACL when the file is created, before writing credential bytes.
   if(File.Exists(path))File.SetAccessControl(path,security);
   using(var stream=new FileStream(path,FileMode.Create,FileSystemRights.Write,FileShare.None,4096,FileOptions.None,security))using(var writer=new StreamWriter(stream))writer.Write(key);
  }
 }
}
