using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Alyvo.SsmsAiSqlAssistant
{
 public sealed class ConnectionContext
 {
  public string Server,Database; internal SqlCredential Credential;internal bool Integrated=true; public bool Encrypt=true,TrustServerCertificate=false;
  public string Fingerprint=>Hash(Server.ToUpperInvariant()+"|"+Database.ToUpperInvariant());
  public static string Hash(string s){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-","").ToLowerInvariant();}
  public SqlConnection Connect(string database=null){var b=new SqlConnectionStringBuilder{DataSource=Server,InitialCatalog=database??Database,IntegratedSecurity=Integrated,ApplicationName="Alyvo SSMS AI Stage2",ConnectTimeout=10,Encrypt=Encrypt,TrustServerCertificate=TrustServerCertificate,Pooling=false};var c=new SqlConnection(b.ConnectionString);if(!Integrated)c.Credential=Credential;return c;}
  public static ConnectionContext Capture()
  {
   Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
   var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.Editors.ScriptFactory",false)).FirstOrDefault(t=>t!=null);
   if(type==null)throw new InvalidOperationException("找不到 SSMS SQL Editor 連線服務。");
   var factory=type.GetProperty("Instance").GetValue(null);var active=type.GetProperty("CurrentlyActiveWndConnectionInfo").GetValue(factory);
   if(active==null || (bool)active.GetType().GetProperty("IsMultipleConnection").GetValue(active))throw new InvalidOperationException("請選取單一已連線的 SQL Editor。");
   var info=active.GetType().GetProperty("UIConnectionInfo").GetValue(active);if(info==null)throw new InvalidOperationException("目前 SQL Editor 尚未連線。");
   object Get(string name)=>info.GetType().GetProperty(name)?.GetValue(info);
   var options=Get("AdvancedOptions") as NameValueCollection;var db=options?["DATABASE"];
   if(string.IsNullOrWhiteSpace(db))throw new InvalidOperationException("無法確認目前資料庫；請在 SQL Editor 選擇資料庫後重試。");
   var result=new ConnectionContext{Server=(string)Get("ServerName"),Database=db};result.TrustServerCertificate=string.Equals(options?["TRUST_SERVER_CERTIFICATE"],"true",StringComparison.OrdinalIgnoreCase)||string.Equals(options?["TrustServerCertificate"],"true",StringComparison.OrdinalIgnoreCase);var auth=Convert.ToInt32(Get("AuthenticationType"),CultureInfo.InvariantCulture);
   if(auth==1){var password=Get("InMemoryPassword") as SecureString;if(password==null)throw new InvalidOperationException("SQL 認證憑證無法安全取得。");var copy=password.Copy();copy.MakeReadOnly();result.Credential=new SqlCredential((string)Get("UserName"),copy);result.Integrated=false;}
   else if(auth!=0)throw new InvalidOperationException("此版本僅支援 Windows 或 SQL Server 認證，未知認證方式已拒絕。");
   return result;
  }
 }
 public sealed class SqlSafety:TSqlFragmentVisitor
 {
  public readonly List<string> Errors=new List<string>();public readonly List<string> Objects=new List<string>();public bool Ordered;
  public static SqlSafety Parse(string sql)
  {
   var result=new SqlSafety();if(string.IsNullOrWhiteSpace(sql)||sql.Length>64000){result.Errors.Add("選取 SQL 必須為 1–64000 字元。");return result;}
   var fragment=new TSql170Parser(true).Parse(new StringReader(sql),out var errors);foreach(var error in errors)result.Errors.Add($"語法錯誤（行 {error.Line}、欄 {error.Column}）。");
   if(fragment is TSqlScript script && (script.Batches.Count!=1 || script.Batches[0].Statements.Count!=1 || !(script.Batches[0].Statements[0] is SelectStatement)))result.Errors.Add("只接受單一唯讀 SELECT；不執行 SP、DDL 或多批次 SQL。");
   fragment.Accept(result);return result;
  }
  public static string[] ReadonlyStatements(string sql)
  {
   if(string.IsNullOrWhiteSpace(sql)||sql.Length>64000)throw new InvalidOperationException("SQL 長度不符。");
   var fragment=new TSql170Parser(true).Parse(new StringReader(sql),out var errors) as TSqlScript;
   if(errors.Count>0||fragment==null||fragment.Batches.Count!=1||fragment.Batches[0].Statements.Count==0)throw new InvalidOperationException("必須為同一批次唯讀 SELECT。");
   var statements=fragment.Batches[0].Statements.Select(x=>sql.Substring(x.StartOffset,x.FragmentLength)).ToArray();
   if(statements.Any(x=>Parse(x).Errors.Any()))throw new InvalidOperationException("包含不可驗證陳述式。");return statements;
  }
  public override void Visit(TSqlFragment node)
  {
   if(node is TSqlStatement && !(node is SelectStatement))Errors.Add("包含非 SELECT 陳述式。");
   if(node is SelectStatement s && s.Into!=null)Errors.Add("不允許 SELECT INTO。");
   if(node is OpenRowsetTableReference || node is OpenQueryTableReference || node is AdHocTableReference || node is SchemaObjectFunctionTableReference || node is SelectSetVariable)Errors.Add("不允許外部資料來源、table function 或變數指派。");
   if(node is NextValueForExpression)Errors.Add("不允許 NEXT VALUE FOR 修改 sequence。");
   if(node is TableHint)Errors.Add("包含鎖定或資料表提示，無法保證安全。");
  }
  public override void ExplicitVisit(NamedTableReference node){if(node.SchemaObject.Identifiers.Count>2)Errors.Add("不允許跨資料庫或跨伺服器引用。");Objects.Add(string.Join(".",node.SchemaObject.Identifiers.Select(i=>i.Value)));base.ExplicitVisit(node);}
  public override void ExplicitVisit(FunctionCall node){if(node.CallTarget!=null)Errors.Add("不允許使用者自訂函式。");base.ExplicitVisit(node);}
  public override void ExplicitVisit(OrderByClause node){Ordered=true;base.ExplicitVisit(node);}
  public static string Canonical(string sql){var parser=new TSql170Parser(true);var f=parser.Parse(new StringReader(sql),out var errors);if(errors.Count>0)return "";new Sql170ScriptGenerator().GenerateScript(f,out var text);return text;}
 }
 public sealed class CandidateDto
 {
  [JsonProperty("sql",Required=Required.Always)]public string Sql{get;set;}
  [JsonProperty("explanation",Required=Required.Always)]public string Explanation{get;set;}
 }
 public sealed class SuggestionDto { public string Title{get;set;} public string Sql{get;set;} public string Explanation{get;set;} }
 public sealed class AnalysisDto
 {
  [JsonProperty("suggestions")]public SuggestionDto[] Suggestions{get;set;}=new SuggestionDto[0];
  [JsonProperty("id",Required=Required.Always)]public string Id{get;set;}
  [JsonProperty("aiProvider",Required=Required.Always)]public string AiProvider{get;set;}
  [JsonProperty("summary",Required=Required.Always)]public string Summary{get;set;}
  [JsonProperty("issues",Required=Required.Always)]public string[] Issues{get;set;}
  [JsonProperty("candidates",Required=Required.Always)]public CandidateDto[] Candidates{get;set;}
  [JsonProperty("warnings",Required=Required.Always)]public string[] Warnings{get;set;}
  [JsonProperty("usage",Required=Required.Always)]public JObject Usage{get;set;}
 }
 public sealed class LocalApi
 {
  public const string Version="2026-09-08",BaseUrl="http://127.0.0.1:46217";
  private static readonly HttpClient http=new HttpClient{Timeout=Timeout.InfiniteTimeSpan};
  public async Task<JToken> Send(string path,object body,CancellationToken token)
  {
   using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token))
   using(var request=new HttpRequestMessage(body==null?HttpMethod.Get:HttpMethod.Post,BaseUrl+path))
   {
    timeout.CancelAfter(TimeSpan.FromSeconds(40));request.Headers.Add("X-Api-Version",Version);
    if(body!=null)request.Content=new StringContent(JsonConvert.SerializeObject(body),Encoding.UTF8,"application/json");
    try{using(var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token))using(var stream=await response.Content.ReadAsStreamAsync())using(var data=new MemoryStream())
    {
     var buffer=new byte[8192];int n;while((n=await stream.ReadAsync(buffer,0,buffer.Length,timeout.Token))>0){if(data.Length+n>262144)throw new InvalidOperationException("LocalService 回應超過大小限制。");data.Write(buffer,0,n);}
     var json=JToken.Parse(Encoding.UTF8.GetString(data.ToArray()));if(!response.IsSuccessStatusCode)throw new InvalidOperationException("LocalService："+(json["message"]?.Value<string>()??"服務回應失敗。"));return json;
    }}catch(HttpRequestException){throw new InvalidOperationException("無法連線至 "+BaseUrl+"/health，請先啟動 LocalService。");}
   }
  }
  public async Task Check(CancellationToken token){var caps=await Send("/api/v1/capabilities",null,token);if(caps["apiVersion"]?.Value<string>()!=Version)throw new InvalidOperationException("LocalService API 版本不相容；不會猜測回應欄位。");var health=await Send("/health",null,token);if(health["status"]?.Value<string>()!="ok")throw new InvalidOperationException("LocalService 尚未就緒。");}
  public async Task<AnalysisDto> Analyze(object payload,CancellationToken token){await Check(token);return Decode(await Send("/analyze",payload,token));}
  public static AnalysisDto Decode(JToken json){var dto=json.ToObject<AnalysisDto>();if(dto.AiProvider!="openai"&&dto.AiProvider!="local-rules")throw new InvalidOperationException("不支援的 AI 提供者。");if(dto.AiProvider=="local-rules"&&dto.Candidates.Length>0)throw new InvalidOperationException("本機規則不得偽造 AI 候選。");if(dto.Candidates.Length>10 || dto.Candidates.Any(c=>string.IsNullOrWhiteSpace(c.Sql)||c.Sql.Length>64000))throw new InvalidOperationException("候選回應不符合契約。");return dto;}
 }
 public sealed class Gate{public string Name,Reason;public bool Passed;public Gate(string n,bool p,string r){Name=n;Passed=p;Reason=r;}}
 public sealed class ColumnContract{public int Ordinal;public string Name,Type,Collation;public bool Nullable;public string Signature=>$"{Ordinal}|{Name}|{Type}|{Nullable}|{Collation}";}
 public sealed class PlanEvidence{public double Cost,Rows;public int Scans;public string[] Warnings;public string Xml;}
 public sealed class Preflight{public Gate[] Gates;public PlanEvidence Original,Candidate;public ColumnContract[] Columns;public bool Passed=>Gates?.Length==6&&Gates.All(g=>g.Passed);}
 public sealed class RunMetrics{public ActualPlanRecord SavedPlan;public string ResultContract;public string Digest,Plan;public int Rows,Columns;public long Reads,CpuMs,ElapsedMs;public string[] Warnings;public string Shape;}
 public sealed class DryRunResult
 {

  public bool ExactQueryStoreBaseline;public string Snapshot,Status,Reason,CleanupWarning,QueryStore;public RunMetrics[] Original,Candidate;public bool Passed;
  public static long Median(IEnumerable<long> values)=>values.OrderBy(v=>v).ElementAt(values.Count()/2);
 }
 public sealed class AnalysisState
 {
  public string Original,OriginalHash,ConnectionFingerprint;public int Start,Length,Version;public ConnectionContext Connection;public object Payload;public AnalysisDto Response;public Preflight Preflight;public DryRunResult DryRun;public int CandidateIndex;public string Status="";public bool Busy,Preview,Applied;public string Error="";public CancellationTokenSource Cancellation;
  public bool MatchesSelection(int version,int start,int length,string text,string fingerprint)=>Version==version&&Start==start&&Length==length&&OriginalHash==ConnectionContext.Hash(text)&&ConnectionFingerprint==fingerprint;
  public CandidateDto Candidate=>Response?.Candidates.ElementAtOrDefault(CandidateIndex);
  public bool NoFurtherSuggestions=>Response?.AiProvider=="openai"&&Response.Candidates?.Length==0&&(Response.Suggestions?.Length??0)==0&&string.IsNullOrEmpty(Error)&&Preflight?.Passed!=false&&DryRun?.Passed!=false;
  public bool CanApply=>Candidate!=null&&!Busy&&!Applied&&string.IsNullOrEmpty(Error)&&Preflight?.Passed==true&&DryRun?.Passed==true;
 }
}



