using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SqlServer.TransactSql.ScriptDom;
namespace Alyvo.SsmsAiSqlAssistant
{
 public sealed class MapModule
 {
  public int Id; public string Schema,Name,Type,Definition,Purpose="尚未提供",Owner="尚未提供",Revision="尚未提供"; public DateTime Modified; public bool Encrypted;
  public string FullName=>DbWorker.Quote(Schema)+"."+DbWorker.Quote(Name);
  public bool IsProcedure=>Type=="P"||Type=="PC";
 }
 public sealed class MapEdge
 {
  public int Caller;public int? Callee;public string Server,Database,Schema,Entity,Evidence="",Condition="",Reason="";public int Line;
  public string Label=>string.Join(".",new[]{Server,Database,Schema,Entity}.Where(x=>!string.IsNullOrEmpty(x)));
 }
 public sealed class MapGraph
 {
  public readonly Dictionary<int,MapModule> Modules=new Dictionary<int,MapModule>();public readonly List<MapEdge> Edges=new List<MapEdge>();public DateTimeOffset ObservedAt=DateTimeOffset.UtcNow;
  public IEnumerable<MapEdge> Down(int id)=>Edges.Where(e=>e.Caller==id);
  public string DownstreamCallLines(int id)=>string.Join("；",Down(id).Where(e=>e.Callee.HasValue&&Modules.ContainsKey(e.Callee.Value)&&Modules[e.Callee.Value].IsProcedure).OrderBy(e=>e.Line).Select(e=>(e.Line>0?"第 "+e.Line+" 行":"行號未知")+" → "+Modules[e.Callee.Value].FullName).Distinct());
  public int Up(int id)=>Edges.Where(e=>e.Callee==id).Select(e=>e.Caller).Distinct().Count();
  public int ProcedureCount=>Modules.Values.Count(m=>m.IsProcedure);
  public int IndependentCount=>Modules.Values.Count(m=>m.IsProcedure&&Up(m.Id)==0&&!Down(m.Id).Any());
  public int[] Roots()
  {
   var roots=new List<int>();var seen=new HashSet<int>();
   void Cover(int first){var pending=new Stack<int>();pending.Push(first);while(pending.Count>0){var id=pending.Pop();if(!seen.Add(id))continue;foreach(var edge in Down(id))if(edge.Callee.HasValue&&Modules.ContainsKey(edge.Callee.Value))pending.Push(edge.Callee.Value);}}
   foreach(var m in Modules.Values.OrderBy(m=>m.FullName,StringComparer.Ordinal))if(Up(m.Id)==0){roots.Add(m.Id);Cover(m.Id);}
   foreach(var m in Modules.Values.OrderBy(m=>m.FullName,StringComparer.Ordinal))if(!seen.Contains(m.Id)){roots.Add(m.Id);Cover(m.Id);}return roots.ToArray();
  }
  public MapModule[] Search(string text){text=(text??"").Trim();return Modules.Values.Where(m=>text.Length==0||string.Join(" ",new[]{m.FullName,m.Purpose,m.Owner}.Concat(Down(m.Id).Select(e=>e.Condition+" "+e.Reason+" "+e.Evidence))).IndexOf(text,StringComparison.OrdinalIgnoreCase)>=0).OrderBy(m=>m.FullName).ToArray();}
 }
 public sealed class CallEvidence:TSqlFragmentVisitor
 {
  readonly MapGraph graph;readonly MapModule module;string condition="";
  CallEvidence(MapGraph g,MapModule m){graph=g;module=m;}
  static string Fragment(TSqlFragment f)=>f==null?"":string.Concat(f.ScriptTokenStream.Skip(f.FirstTokenIndex).Take(f.LastTokenIndex-f.FirstTokenIndex+1).Select(t=>t.Text));
  public static void Enrich(MapGraph graph,MapModule module)
  {
   if(module.Definition==null)return;var parsed=new TSql170Parser(true).Parse(new StringReader(module.Definition),out var errors);
   if(errors.Count>0){graph.Edges.Add(new MapEdge{Caller=module.Id,Reason="定義無法完整剖析",Evidence=string.Join(";",errors.Select(e=>"line "+e.Line))});return;}
   var comments=string.Join("\n",parsed.ScriptTokenStream.Where(t=>t.TokenType==TSqlTokenType.SingleLineComment||t.TokenType==TSqlTokenType.MultilineComment).Select(t=>t.Text));
   string Field(string pattern){var m=Regex.Match(comments,@"(?im)(?:^|[\r\n])\s*(?:--|/\*|\*)?\s*(?:"+pattern+@")\s*[:：]\s*([^\r\n*]+)");return m.Success?m.Groups[1].Value.Trim():"尚未提供";}
   module.Purpose=Field("用途|目的|Purpose");module.Owner=Field("負責單位|負責人|Owner|Team");module.Revision=Field("版本|Revision|Version");parsed.Accept(new CallEvidence(graph,module));
  }
  public override void ExplicitVisit(IfStatement node){var old=condition;var current=Fragment(node.Predicate);condition=string.IsNullOrEmpty(old)?current:"("+old+") AND ("+current+")";node.ThenStatement?.Accept(this);condition=string.IsNullOrEmpty(old)?"NOT ("+current+")":"("+old+") AND NOT ("+current+")";node.ElseStatement?.Accept(this);condition=old;}
  void Annotate(SchemaObjectName name,TSqlFragment node,bool required)
  {
   var text=Fragment(node);bool Equal(string a,string b)=>string.Equals(a??"",b??"",StringComparison.OrdinalIgnoreCase);
   var matches=name==null?new MapEdge[0]:graph.Down(module.Id).Where(e=>Equal(e.Entity,name.BaseIdentifier?.Value)&&Equal(e.Schema,name.SchemaIdentifier?.Value)&&Equal(e.Database,name.DatabaseIdentifier?.Value)&&Equal(e.Server,name.ServerIdentifier?.Value)).ToArray();
   if(matches.Length>0){var existing=matches[0];if(existing.Line>0){existing=new MapEdge{Caller=existing.Caller,Callee=existing.Callee,Server=existing.Server,Database=existing.Database,Schema=existing.Schema,Entity=existing.Entity,Reason=existing.Reason};graph.Edges.Add(existing);}existing.Line=node.StartLine;existing.Evidence=text;existing.Condition=condition;}
   else if(required)graph.Edges.Add(new MapEdge{Caller=module.Id,Server=name?.ServerIdentifier?.Value,Database=name?.DatabaseIdentifier?.Value,Schema=name?.SchemaIdentifier?.Value,Entity=name?.BaseIdentifier?.Value,Line=node.StartLine,Evidence=text,Condition=condition,Reason=name==null?"dynamic SQL / variable EXEC，無法靜態解析":"catalog 未回傳可解析的 object id"});
  }
  public override void ExplicitVisit(ExecuteStatement node){var executable=node.ExecuteSpecification.ExecutableEntity as ExecutableProcedureReference;if(node.ExecuteSpecification.LinkedServer!=null||executable?.AdHocDataSource!=null){graph.Edges.Add(new MapEdge{Caller=module.Id,Server=node.ExecuteSpecification.LinkedServer?.Value,Line=node.StartLine,Evidence=Fragment(node),Condition=condition,Reason="跨 server / ad-hoc EXEC，不推測目標"});return;}Annotate(executable?.ProcedureReference?.ProcedureReference?.Name,node,true);base.ExplicitVisit(node);}
  public override void ExplicitVisit(NamedTableReference node){Annotate(node.SchemaObject,node,false);base.ExplicitVisit(node);}
 }
 public sealed class MapCollector
 {
  public const string CatalogSql=@"SELECT o.object_id,s.name,o.name,o.type,m.definition,o.modify_date,CONVERT(bit,OBJECTPROPERTYEX(o.object_id,'IsEncrypted')) FROM (SELECT object_id,schema_id,name,type,modify_date,is_ms_shipped FROM sys.procedures UNION ALL SELECT object_id,schema_id,name,type,modify_date,is_ms_shipped FROM sys.views) o JOIN sys.schemas s ON s.schema_id=o.schema_id LEFT JOIN sys.sql_modules m ON m.object_id=o.object_id WHERE o.is_ms_shipped=0 ORDER BY s.name,o.name;
SELECT d.referencing_id,d.referenced_id,d.referenced_server_name,d.referenced_database_name,d.referenced_schema_name,d.referenced_entity_name,d.is_caller_dependent,ro.type FROM sys.sql_expression_dependencies d LEFT JOIN sys.objects ro ON ro.object_id=d.referenced_id WHERE d.referencing_id IN (SELECT object_id FROM sys.procedures WHERE is_ms_shipped=0 UNION ALL SELECT object_id FROM sys.views WHERE is_ms_shipped=0);";
  public async Task<MapGraph> Load(ConnectionContext context,CancellationToken token)
  {
   await DbWorker.VerifyEnvironment(context,token);var graph=new MapGraph();using(var connection=context.Connect()){await connection.OpenAsync(token);
    // Without database VIEW DEFINITION, a seemingly complete catalog can silently hide modules.
    using(var permission=new SqlCommand("SELECT HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','VIEW DEFINITION');",connection))if(Convert.ToInt32(await permission.ExecuteScalarAsync(token))!=1)throw new InvalidOperationException("需要資料庫 VIEW DEFINITION 權限，否則無法保證所有 SP 都存在於地圖。");
    using(var cmd=new SqlCommand(CatalogSql,connection){CommandTimeout=30})using(token.Register(()=>cmd.Cancel()))using(var r=await cmd.ExecuteReaderAsync(token))
    {
     while(await r.ReadAsync(token)){var m=new MapModule{Id=r.GetInt32(0),Schema=r.GetString(1),Name=r.GetString(2),Type=r.GetString(3).Trim(),Definition=r.IsDBNull(4)?null:r.GetString(4),Modified=r.GetDateTime(5),Encrypted=!r.IsDBNull(6)&&r.GetBoolean(6)};graph.Modules.Add(m.Id,m);}
     await r.NextResultAsync(token);while(await r.ReadAsync(token))
     {
      string S(int i)=>r.IsDBNull(i)?null:r.GetString(i);int? target=r.IsDBNull(1)?(int?)null:r.GetInt32(1);bool external=!r.IsDBNull(2)||!r.IsDBNull(3);if(!external&&target.HasValue&&!graph.Modules.ContainsKey(target.Value))continue;
      graph.Edges.Add(new MapEdge{Caller=r.GetInt32(0),Callee=!external&&target.HasValue&&graph.Modules.ContainsKey(target.Value)?target:null,Server=S(2),Database=S(3),Schema=S(4),Entity=S(5),Reason=external?"跨 database/server，不推測目標":target==null?"catalog 無 object id / caller dependent":"",Evidence="sys.sql_expression_dependencies"});
     }
    }
   }
   foreach(var module in graph.Modules.Values){token.ThrowIfCancellationRequested();CallEvidence.Enrich(graph,module);}return graph;
  }
 }
}


