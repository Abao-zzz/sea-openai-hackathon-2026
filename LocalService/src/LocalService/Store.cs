using Microsoft.Data.Sqlite;
using System.Text.Json;
namespace LocalService;
public sealed class Store
{
    private readonly string connectionString;
    private readonly object sync=new();
    private readonly TimeProvider time;
    public Store(Settings settings,TimeProvider time)
    {
        this.time=time; Directory.CreateDirectory(settings.DataDir);
        connectionString=new SqliteConnectionStringBuilder {DataSource=Path.Combine(settings.DataDir,"assistant-history.db"),DefaultTimeout=10,Pooling=false}.ToString();
        using var c=Open(); using var tx=c.BeginTransaction();
        using var versionCommand=c.CreateCommand();versionCommand.Transaction=tx;versionCommand.CommandText="PRAGMA user_version";var version=Convert.ToInt32(versionCommand.ExecuteScalar());if(version>2)throw new InvalidOperationException("SQLite schema 版本較新，拒絕降版。");
        Execute(c,tx,"CREATE TABLE IF NOT EXISTS records(kind TEXT NOT NULL,id TEXT NOT NULL,created TEXT NOT NULL,data TEXT NOT NULL,PRIMARY KEY(kind,id)); CREATE INDEX IF NOT EXISTS ix_records_created ON records(kind,created); CREATE TABLE IF NOT EXISTS state(name TEXT PRIMARY KEY,value TEXT NOT NULL); ");
        if(version<2)
        {
            Execute(c,tx,"UPDATE records SET data=json_set(data,'$.usage.cachedInputTokens',COALESCE(json_extract(data,'$.usage.cachedInputTokens'),0),'$.usage.cacheWriteTokens',COALESCE(json_extract(data,'$.usage.cacheWriteTokens'),0),'$.usage.estimateKind','upper-bound') WHERE kind='usage' AND json_extract(data,'$.usage.estimateKind') IS NULL; PRAGMA user_version=2;");
        }
        tx.Commit(); Prune(settings.RetentionDays);
    }
    private SqliteConnection Open() { var c=new SqliteConnection(connectionString);c.Open();using var cmd=c.CreateCommand();cmd.CommandText="PRAGMA journal_mode=WAL; PRAGMA busy_timeout=10000; PRAGMA secure_delete=ON;";cmd.ExecuteNonQuery();return c; }
    private static void Execute(SqliteConnection c,SqliteTransaction? tx,string sql,params (string,object?)[] args) {using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;foreach(var (k,v) in args)cmd.Parameters.AddWithValue(k,v??DBNull.Value);cmd.ExecuteNonQuery();}
    private static List<T> Read<T>(SqliteConnection c,SqliteTransaction? tx,string kind)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="SELECT data FROM records WHERE kind=$k ORDER BY created,id";cmd.Parameters.AddWithValue("$k",kind);using var r=cmd.ExecuteReader();var rows=new List<T>();while(r.Read()) rows.Add(JsonSerializer.Deserialize<T>(r.GetString(0),Contract.Json)!);return rows;
    }
    public List<T> List<T>(string kind) { lock(sync) {using var c=Open();return Read<T>(c,null,kind);} }
    public T Get<T>(string kind,string id) {lock(sync){using var c=Open();return Fetch<T>(c,null,kind,id);}}
    private static T Fetch<T>(SqliteConnection c,SqliteTransaction? tx,string kind,string id)
    {
        using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="SELECT data FROM records WHERE kind=$k AND id=$i";cmd.Parameters.AddWithValue("$k",kind);cmd.Parameters.AddWithValue("$i",id);var data=cmd.ExecuteScalar() as string ?? throw new ApiError(404,"not_found","找不到指定紀錄。");return JsonSerializer.Deserialize<T>(data,Contract.Json)!;
    }
    private static void Put<T>(SqliteConnection c,SqliteTransaction? tx,string kind,string id,DateTimeOffset created,T data)=>Execute(c,tx,"INSERT INTO records(kind,id,created,data) VALUES($k,$i,$c,$d) ON CONFLICT(kind,id) DO UPDATE SET data=excluded.data",("$k",kind),("$i",id),("$c",created.ToString("O")),("$d",JsonSerializer.Serialize(data,Contract.Json)));
    public T Add<T>(string kind,string id,T data) {lock(sync){using var c=Open();Put(c,null,kind,id,time.GetUtcNow(),data);return data;}}
    public T Update<T>(string kind,string id,Func<T,T> transform)
    {
        lock(sync) { using var c=Open();using var tx=c.BeginTransaction();var value=transform(Fetch<T>(c,tx,kind,id));Put(c,tx,kind,id,time.GetUtcNow(),value);tx.Commit();return value; }
    }
    public int Delete(string kind,string? id=null)
    {
        lock(sync){using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="DELETE FROM records WHERE kind=$k AND ($i IS NULL OR id=$i)";cmd.Parameters.AddWithValue("$k",kind);cmd.Parameters.AddWithValue("$i",(object?)id??DBNull.Value);return cmd.ExecuteNonQuery();}
    }
    private static string State(SqliteConnection c,SqliteTransaction? tx,string name) {using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="SELECT value FROM state WHERE name=$n";cmd.Parameters.AddWithValue("$n",name);return cmd.ExecuteScalar() as string ?? new string('0',64);}
    private static void SetState(SqliteConnection c,SqliteTransaction tx,string name,string value)=>Execute(c,tx,"INSERT INTO state VALUES($n,$v) ON CONFLICT(name) DO UPDATE SET value=excluded.value",("$n",name),("$v",value));
    public AuditRecord AppendAudit(AuditInput input)
    {
        Contract.Fingerprint(input.DatabaseFingerprint);Contract.Fingerprint(input.BeforeHash);Contract.Fingerprint(input.AfterHash);Contract.Choice(input.Action,"apply","restore","reject");
        lock(sync){using var c=Open();using var tx=c.BeginTransaction();Verify(c,tx);var previous=State(c,tx,"audit-head");var row=new AuditRecord(Contract.Id(),time.GetUtcNow(),input,previous,"");row=row with {Hash=AuditHash(row)};Put(c,tx,"audit",row.Id,row.CreatedAt,row);SetState(c,tx,"audit-head",row.Hash);tx.Commit();return row;}
    }
    public static string AuditHash(AuditRecord row)=>Contract.Hash(JsonSerializer.Serialize(row with {Hash=""},Contract.Json));
    private static void Verify(SqliteConnection c,SqliteTransaction? tx)
    {
        var rows=Read<AuditRecord>(c,tx,"audit");var current=State(c,tx,"audit-anchor");
        // Hash linkage defines order, including entries sharing the same UTC timestamp.
        var remaining=new List<AuditRecord>(rows);
        while(remaining.Count>0) { var matches=remaining.Where(x=>x.PreviousHash==current).ToArray();if(matches.Length!=1 || AuditHash(matches[0])!=matches[0].Hash)throw new ApiError(409,"audit_tampered","稽核鏈驗證失敗。");current=matches[0].Hash;remaining.Remove(matches[0]); }
        if(current!=State(c,tx,"audit-head"))throw new ApiError(409,"audit_tampered","稽核鏈尾端驗證失敗。");
    }
    public AuditVerificationResponse VerifyAudit(){lock(sync){using var c=Open();Verify(c,null);return new(true,"SHA-256",false);}}
    public void Prune(int days)
    {
        lock(sync){using var c=Open();using var tx=c.BeginTransaction();var cutoff=time.GetUtcNow().AddDays(-days);var rows=Read<AuditRecord>(c,tx,"audit");Verify(c,tx);var anchor=State(c,tx,"audit-anchor");while(true){var r=rows.SingleOrDefault(x=>x.PreviousHash==anchor);if(r is null || r.CreatedAt>=cutoff)break;anchor=r.Hash;Execute(c,tx,"DELETE FROM records WHERE kind='audit' AND id=$i",("$i",r.Id));}SetState(c,tx,"audit-anchor",anchor);Execute(c,tx,"DELETE FROM records WHERE kind<>'audit' AND created<$c",("$c",cutoff.ToString("O")));tx.Commit();}
    }
}
public sealed class RetentionWorker(Store store,Settings settings):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {using var timer=new PeriodicTimer(TimeSpan.FromHours(6));try{while(await timer.WaitForNextTickAsync(stoppingToken))store.Prune(settings.RetentionDays);}catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){} }
}

