using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Alyvo.SsmsAiSqlAssistant
{
 public sealed class SpParameter
 {
  public string Name,Type,DefaultSql;
 }
 public sealed class SpBody
 {
  public string Definition,SelectSql; public int SelectStart,SelectLength; public SpParameter[] Parameters;
  public static SpBody Parse(string definition)
  {
   if(string.IsNullOrWhiteSpace(definition))throw new InvalidOperationException("definition missing：沒有完整定義。");
   var f=new TSql170Parser(true).Parse(new StringReader(definition),out var errors);
   if(errors.Count!=0 || !(f is TSqlScript script) || script.Batches.Count!=1 || script.Batches[0].Statements.Count!=1 || !(script.Batches[0].Statements[0] is ProcedureStatementBody p))throw new InvalidOperationException("side effect / unsupported：無法解析單一 SP 定義。");
   if(p.Options.Count!=0)throw new InvalidOperationException("side effect / unsupported：SP 選項無法安全驗證。");
   var statements=new List<TSqlStatement>();
   void Flatten(IEnumerable<TSqlStatement> items){foreach(var s in items){if(s is BeginEndBlockStatement b)Flatten(b.StatementList.Statements);else statements.Add(s);}}
   Flatten(p.StatementList.Statements);
   var selects=statements.OfType<SelectStatement>().ToArray();
   if(selects.Length!=1 || statements.Any(s=>!(s is SelectStatement)&&!Regex.IsMatch(definition.Substring(s.StartOffset,s.FragmentLength),@"^SET\s+NOCOUNT\s+(ON|OFF)\s*;?\s*$",RegexOptions.IgnoreCase)))throw new InvalidOperationException("side effect / unsupported：只支援單一唯讀 SELECT body；含 EXEC、DML、動態 SQL 或控制流程者不自動處理。");
   var select=selects[0];var sql=definition.Substring(select.StartOffset,select.FragmentLength);
   var safety=SqlSafety.Parse(sql);if(safety.Errors.Any())throw new InvalidOperationException("side effect / unsupported："+string.Join("；",safety.Errors.Distinct()));
   var parameters=new List<SpParameter>();
   foreach(var param in p.Parameters)
   {
    var type=definition.Substring(param.DataType.StartOffset,param.DataType.FragmentLength);
    if(param.Modifier!=ParameterModifier.None || !SafeType(type))throw new InvalidOperationException("不安全參數：不支援 OUTPUT、READONLY、table / user-defined 或此參數型別。");
    string value=null;if(param.Value!=null)value=Literal(definition.Substring(param.Value.StartOffset,param.Value.FragmentLength));
    parameters.Add(new SpParameter{Name=param.VariableName.Value,Type=type,DefaultSql=value});
   }
   var names=parameters.Select(x=>x.Name).ToArray();var variables=new Variables();select.Accept(variables);
   if(variables.Items.Any(v=>!names.Contains(v.Name,StringComparer.OrdinalIgnoreCase)))throw new InvalidOperationException("不安全參數：body 引用未宣告變數。");
   return new SpBody{Definition=definition,SelectSql=sql,SelectStart=select.StartOffset,SelectLength=select.FragmentLength,Parameters=parameters.ToArray()};
  }
  public static bool SafeType(string type)=>Regex.IsMatch(type,@"^(bit|tinyint|smallint|int|bigint|money|smallmoney|real|float(?:\(\d+\))?|decimal\(\d+,\s*\d+\)|numeric\(\d+,\s*\d+\)|date|datetime|smalldatetime|datetime2(?:\(\d+\))?|datetimeoffset(?:\(\d+\))?|time(?:\(\d+\))?|uniqueidentifier|(?:n?varchar|n?char|varbinary|binary)\((?:\d+|max)\))$",RegexOptions.IgnoreCase);
  public static string Literal(string value)
  {
   if(value==null || value.Length>4096)throw new InvalidOperationException("不安全參數：值缺失或超過限制。");
   var fragment=new TSql170Parser(true).Parse(new StringReader("SELECT "+value+" AS p;"),out var errors);
   if(errors.Count!=0 || !(fragment is TSqlScript script) || script.Batches.Count!=1 || script.Batches[0].Statements.Count!=1 || !(script.Batches[0].Statements[0] is SelectStatement s) || !(s.QueryExpression is QuerySpecification q) || q.SelectElements.Count!=1 || !(q.SelectElements[0] is SelectScalarExpression e))throw new InvalidOperationException("不安全參數：必須為 scalar literal。");
   ScalarExpression expression=e.Expression;while(expression is ParenthesisExpression par)expression=par.Expression;
   if(expression is UnaryExpression unary && (unary.UnaryExpressionType==UnaryExpressionType.Negative || unary.UnaryExpressionType==UnaryExpressionType.Positive))expression=unary.Expression;
   if(!(expression is Literal))throw new InvalidOperationException("不安全參數：不允許運算式、函式或 SQL 片段。");
   if(q.FromClause!=null || q.WhereClause!=null || q.GroupByClause!=null || q.HavingClause!=null || s.Into!=null)throw new InvalidOperationException("不安全參數 SQL。");
   new Sql170ScriptGenerator().GenerateScript(e.Expression,out var normalized);return normalized;
  }
  sealed class Variables:TSqlFragmentVisitor{public readonly List<VariableReference> Items=new List<VariableReference>();public override void ExplicitVisit(VariableReference node){Items.Add(node);}}
  public string Bind(string sql,WorkloadCase item)
  {
   var parsed=new TSql170Parser(true).Parse(new StringReader(sql),out var errors);if(errors.Count!=0 || SqlSafety.Parse(sql).Errors.Any())throw new InvalidOperationException("候選必須為單一唯讀 SELECT。");
   var visitor=new Variables();parsed.Accept(visitor);var result=sql;
   foreach(var variable in visitor.Items.OrderByDescending(x=>x.StartOffset))
   {
    var parameter=Parameters.SingleOrDefault(x=>string.Equals(x.Name,variable.Name,StringComparison.OrdinalIgnoreCase));
    if(parameter==null || !item.Values.TryGetValue(parameter.Name,out var value))throw new InvalidOperationException("缺少同來源完整 workload 參數。");
    result=result.Remove(variable.StartOffset,variable.FragmentLength).Insert(variable.StartOffset,"CONVERT("+parameter.Type+", "+Literal(value)+")");
   }
   return result;
  }
  public string Candidate(string sql,string database)
  {
   if(SqlSafety.Parse(sql).Errors.Any())throw new InvalidOperationException("AI 候選 body 非唯讀 SELECT。");
   var full=Definition.Remove(SelectStart,SelectLength).Insert(SelectStart,sql.TrimEnd().TrimEnd(';')+";");
   var canonical=ProcedureSource.Parse(full,database).Sql;var body=Parse(canonical);
   if(!body.Parameters.Select(x=>x.Name+"|"+x.Type+"|"+x.DefaultSql).SequenceEqual(Parameters.Select(x=>x.Name+"|"+x.Type+"|"+x.DefaultSql)))throw new InvalidOperationException("候選參數契約改變。");
   return canonical;
  }
 }
 public sealed class WorkloadCase
 {
  public string Source;public DateTimeOffset ObservedAt;public Dictionary<string,string> Values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
 }
 public sealed class SpScanRow
 {
  public MapModule Module;public SpBody Body;public string Reason="",Source="無 evidence",Stage="待審";public DateTime? LastExecution;
  public long Executions;public double Reads,CpuMs,ElapsedMs;public int Rank,Attempts;public SpReview Review;
  public bool Eligible=>string.IsNullOrEmpty(Reason);
  public string SearchText=>Module.Schema+" "+Module.Name+" "+Module.Purpose+" "+Module.Owner+" "+Reason+" "+Module.Definition;
 }
 public sealed class SpScanResult
 {
  public ConnectionContext Context;public MapGraph Graph;public SpScanRow[] Rows;public DateTimeOffset ObservedAt=DateTimeOffset.UtcNow;public string Notice="";
 }
 public sealed class SpScanCollector
 {
  public const string DmvSql=@"SELECT object_id,SUM(execution_count),SUM(CONVERT(float,total_logical_reads)),SUM(CONVERT(float,total_worker_time))/1000,SUM(CONVERT(float,total_elapsed_time))/1000,MAX(last_execution_time) FROM sys.dm_exec_procedure_stats WHERE database_id=DB_ID() GROUP BY object_id;";
  public const string SummarySql=@"SELECT q.object_id,SUM(rs.count_executions),SUM(rs.avg_logical_io_reads*rs.count_executions),SUM(rs.avg_cpu_time*rs.count_executions)/1000,SUM(rs.avg_duration*rs.count_executions)/1000,MAX(rs.last_execution_time) FROM sys.query_store_query q JOIN sys.query_store_plan p ON p.query_id=q.query_id JOIN sys.query_store_runtime_stats rs ON rs.plan_id=p.plan_id WHERE q.object_id>0 AND rs.last_execution_time>=DATEADD(day,-7,SYSUTCDATETIME()) GROUP BY q.object_id;";
  public async Task<SpScanResult> Load(ConnectionContext context,CancellationToken token)
  {
   var graph=await new MapCollector().Load(context,token);var result=new SpScanResult{Context=context,Graph=graph};
   result.Rows=graph.Modules.Values.Where(x=>x.IsProcedure).Select(m=>new SpScanRow{Module=m}).ToArray();
   foreach(var row in result.Rows){token.ThrowIfCancellationRequested();try{if(row.Module.Encrypted)throw new InvalidOperationException("encrypted：SP 定義已加密。");row.Body=SpBody.Parse(row.Module.Definition);}catch(Exception e){row.Reason=e.Message;}}
   bool dmv=false,qs=false;
   try{await Costs(result,DmvSql,"DMV",15,token);dmv=true;}catch(SqlException e){result.Notice="DMV 權限不足或不可用（"+e.Number+"）；嘗試 Query Store。";}
   try{await Costs(result,SummarySql,"Query Store",3,token);qs=true;}catch(SqlException e){result.Notice+=" Query Store summary 不可用或 3 秒逾時（"+e.Number+"）；保留 DMV。";}
   foreach(var row in result.Rows)if(row.Eligible && row.Executions<=0)row.Reason=!dmv&&!qs?"權限不足／成本 evidence 不可用：DMV 與 Query Store 皆無法讀取。":"沒有成本 evidence：沒有可用歷史執行統計，不自動進入候選。";
   result.Rows=result.Rows.OrderByDescending(x=>x.Reads).ThenBy(x=>x.Module.FullName).ToArray();int rank=0;foreach(var row in result.Rows.Where(x=>x.Eligible))row.Rank=++rank;return result;
  }
  static async Task Costs(SpScanResult result,string sql,string source,int timeout,CancellationToken token)
  {
   using(var c=result.Context.Connect()){await c.OpenAsync(token);using(var cmd=new SqlCommand(sql,c){CommandTimeout=timeout})using(token.Register(()=>cmd.Cancel()))using(var r=await cmd.ExecuteReaderAsync(token))while(await r.ReadAsync(token))
   {var row=result.Rows.SingleOrDefault(x=>x.Module.Id==Convert.ToInt32(r.GetValue(0)));if(row==null||r.IsDBNull(1))continue;var executions=Convert.ToInt64(r.GetValue(1));if(executions<=0 || (source=="Query Store"&&row.Executions>0))continue;row.Executions=executions;row.Reads=Convert.ToDouble(r.GetValue(2));row.CpuMs=Convert.ToDouble(r.GetValue(3));row.ElapsedMs=Convert.ToDouble(r.GetValue(4));row.Source=source;row.LastExecution=r.IsDBNull(5)?(DateTime?)null:(r.GetValue(5) is DateTimeOffset dto?dto.LocalDateTime:Convert.ToDateTime(r.GetValue(5)));}}
  }
 }
}
