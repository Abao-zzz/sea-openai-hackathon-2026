using Microsoft.SqlServer.TransactSql.ScriptDom;
using System.Text.RegularExpressions;
namespace LocalService;
public sealed record SqlInspection(bool ReadOnly,string[] Errors,string[] Objects,string[] Issues);
public static class SqlRules
{
    public static bool ValidSuggestion(string sql)
    {
        if(string.IsNullOrWhiteSpace(sql))return false;
        var script=new TSql170Parser(true).Parse(new StringReader(sql),out var errors) as TSqlScript;
        if(errors.Count>0||script is null||script.Batches.Count!=1)return false;
        var statements=script.Batches.SelectMany(b=>b.Statements).ToArray();
        return statements.Length==1&&(statements[0] is CreateIndexStatement||statements[0] is UpdateStatisticsStatement);
    }
    public static SqlInspection Inspect(string sql)
    {
        var parser=new TSql170Parser(true);var fragment=parser.Parse(new StringReader(sql),out var errors);
        var visitor=new Inspector();fragment.Accept(visitor);
        var issues=new List<string>();if(visitor.Star)issues.Add("SELECT * 使輸出契約依賴資料表欄位，請明確列出欄位。");if(visitor.Scans)issues.Add("前置萬用字元可能使索引搜尋失效。");
        var readOnly=errors.Count==0 && !visitor.Unsafe && visitor.Selects>0;
        return new(readOnly,errors.Select(x=>$"SQL 語法錯誤：行 {x.Line}，欄 {x.Column}。").ToArray(),visitor.Objects.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),issues.ToArray());
    }
    private sealed class Inspector:TSqlFragmentVisitor
    {
        public bool Unsafe,Star,Scans;public int Selects;public readonly List<string> Objects=[];
        public override void Visit(TSqlFragment node)
        {
            if(node is TSqlStatement && node is not (SelectStatement or CreateProcedureStatement or AlterProcedureStatement or CreateOrAlterProcedureStatement or BeginEndBlockStatement)) Unsafe=true;
            if(node is SelectStatement)Selects++;
            // SELECT INTO and external rowsets are side effects / data exfiltration risks.
            if(node is SelectStatement q && q.Into is not null)Unsafe=true;
            if(node is OpenRowsetTableReference or OpenQueryTableReference or AdHocTableReference)Unsafe=true;
            if(node is SelectSetVariable)Unsafe=true;
        }
        public override void ExplicitVisit(SelectStarExpression node){Star=true;base.ExplicitVisit(node);}
        public override void ExplicitVisit(NamedTableReference node){Objects.Add(string.Join(".",node.SchemaObject.Identifiers.Select(x=>x.Value)));if(node.SchemaObject.Identifiers.Count>2)Unsafe=true;base.ExplicitVisit(node);}
        public override void ExplicitVisit(LikePredicate node){if(node.SecondExpression is StringLiteral s && s.Value.StartsWith('%'))Scans=true;base.ExplicitVisit(node);}
    }
    public static ValidationResponse Validate(Candidate c)
    {
        Contract.Check(c);var s=Inspect(c.Sql);var e=s.Errors.ToList();if(!s.ReadOnly)e.Add("候選不符合唯讀語法限制或包含不支援的陳述式。");return new(e.Count==0,false,"contract-only",e.ToArray());
    }
    public static PrefilterResponse Prefilter(ValidateRequest r){var v=Validate(r.Candidate);return new(v.Valid,false,v.Errors,v.VerificationMode);}
    public static RankResponse Rank(RankRequest request)
    {
        Contract.Check(request);
        if(request.Procedures.Select(x=>x.ObjectId).Distinct().Count()!=request.Procedures.Length)throw new ApiError(400,"duplicate_object","物件識別碼不得重複。");
        var rows=request.Procedures.Select(p=>
        {
            var reasons=new List<string>();if(p.Encrypted)reasons.Add("encrypted");if(string.IsNullOrWhiteSpace(p.Definition))reasons.Add("definition-missing");else if(!Inspect(p.Definition).ReadOnly)reasons.Add("side-effect-or-unsupported");if(p.SideEffect)reasons.Add("side-effect");
            return new RankedProcedure(p.ObjectId,p.Name,p.CpuMs+p.DurationMs+p.LogicalReads,reasons.Count==0,reasons.ToArray());
        }).OrderByDescending(x=>x.Score).ThenBy(x=>x.ObjectId).ToArray();
        return new(rows.Where(x=>x.Eligible).Take(request.TopN).ToArray(),rows.Where(x=>!x.Eligible).ToArray());
    }
    public static ObjectMetadata[] Metadata(AnalyzeRequest r,SqlInspection inspection)
    {
        Contract.Check(r);Contract.Fingerprint(r.ServerFingerprint);Contract.Fingerprint(r.DatabaseFingerprint);
        return r.Metadata.Where(x=>inspection.Objects.Any(n=>string.Equals(n,x.Name,StringComparison.OrdinalIgnoreCase))).ToArray();
    }
    public const string MetadataQuery="""
        -- Worker supplies @objectName (nvarchar(776)); current database only.
        SELECT s.name AS schema_name,o.name AS object_name,c.name AS column_name,
               t.name AS type_name,c.max_length,c.precision,c.scale,c.is_nullable
        FROM sys.objects AS o JOIN sys.schemas AS s ON s.schema_id=o.schema_id
        JOIN sys.columns AS c ON c.object_id=o.object_id JOIN sys.types AS t ON t.user_type_id=c.user_type_id
        WHERE o.object_id=OBJECT_ID(@objectName) ORDER BY c.column_id;
        """;
    public const string Evidence="""
        -- Read-only evidence; does not execute a procedure or candidate SQL.
        -- Worker binds @objectId int. Never upload actual plan XML or result rows.
        SELECT execution_count,total_worker_time,total_logical_reads,total_elapsed_time,
               cached_time,last_execution_time
        FROM sys.dm_exec_procedure_stats WHERE database_id=DB_ID() AND object_id=@objectId;
        """;
    public const string Procedures="""
        SELECT p.object_id,s.name AS schema_name,p.name,m.definition,
               CONVERT(bit,OBJECTPROPERTYEX(p.object_id,'IsEncrypted')) AS encrypted,
               COALESCE(SUM(d.execution_count),0) AS execution_count,
               COALESCE(SUM(d.total_worker_time),0)/1000.0 AS cpu_ms,
               COALESCE(SUM(d.total_logical_reads),0) AS logical_reads,
               COALESCE(SUM(d.total_elapsed_time),0)/1000.0 AS duration_ms
        FROM sys.procedures p JOIN sys.schemas s ON p.schema_id=s.schema_id
        LEFT JOIN sys.sql_modules m ON p.object_id=m.object_id
        LEFT JOIN sys.dm_exec_procedure_stats d ON d.object_id=p.object_id AND d.database_id=DB_ID()
        WHERE p.is_ms_shipped=0
        GROUP BY p.object_id,s.name,p.name,m.definition;
        """;
    public const string QueryStore="""
        -- Worker MUST set SqlCommand.CommandTimeout=3; on timeout retain DMV results.
        -- No Query Store enablement or database setting changes.
        SELECT q.object_id,SUM(rs.count_executions) AS execution_count,
               SUM(rs.avg_cpu_time*rs.count_executions)/1000.0 AS cpu_ms,
               SUM(rs.avg_logical_io_reads*rs.count_executions) AS logical_reads,
               SUM(rs.avg_duration*rs.count_executions)/1000.0 AS duration_ms
        FROM sys.query_store_query q JOIN sys.query_store_plan p ON p.query_id=q.query_id
        JOIN sys.query_store_runtime_stats rs ON rs.plan_id=p.plan_id
        WHERE q.object_id>0 GROUP BY q.object_id;
        """;
    public const string Parameters="""
        -- Parameter types only; no result values or actual execution plan.
        SELECT p.parameter_id,p.name,t.name AS type_name,p.max_length,p.precision,p.scale,p.is_output
        FROM sys.parameters p JOIN sys.types t ON p.user_type_id=t.user_type_id
        WHERE p.object_id=@objectId ORDER BY p.parameter_id;
        """;
}


