using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace LocalService;
public static class Contract
{
    public const string Version = "2026-09-08";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false) }, MaxDepth = 32 };
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Id() => Guid.NewGuid().ToString("N");
    public static void Check(object? value)
    {
        if(value is null) throw new ApiError(400,"invalid_request","缺少必要欄位。");
        var errors = new List<ValidationResult>();
        if(!Validator.TryValidateObject(value,new ValidationContext(value),errors,true)) throw new ApiError(400,"invalid_request","欄位格式或必要欄位不正確。");
        foreach(var p in value.GetType().GetProperties())
        {
            var x = p.GetValue(value);
            if(x is System.Collections.IEnumerable a && x is not string) { foreach(var i in a) if(i is not null && !i.GetType().IsPrimitive && i is not string) Check(i); }
            else if(x is not null && x.GetType().Namespace == "LocalService" && !x.GetType().IsEnum) Check(x);
        }
    }
    public static void Fingerprint(string s) { if(!System.Text.RegularExpressions.Regex.IsMatch(s,"^[a-f0-9]{64}$")) throw new ApiError(400,"invalid_fingerprint","必須使用 SHA-256 指紋。"); }
    public static void Choice(string value,params string[] choices) { if(!choices.Contains(value,StringComparer.Ordinal)) throw new ApiError(400,"unsupported","不支援此狀態或模式。"); }
}
public sealed class ApiError(int status,string code,string message) : Exception(message) { public int Status { get; }=status; public string Code { get; }=code; }
public sealed record ErrorResponse(string Code,string Message,string RequestId);
public sealed record ColumnMetadata([property:Required,MaxLength(128)] string Name,[property:Required,MaxLength(128)] string SqlType,bool Nullable);
public sealed record ObjectMetadata([property:Required,MaxLength(260)] string Name,[property:Required,MaxLength(200)] ColumnMetadata[] Columns);
public sealed record PlanSummary([property:Range(0,double.MaxValue)] double EstimatedCost,[property:Range(0,double.MaxValue)] double EstimatedRows,[property:Range(0,10000)] int ScanCount);
public sealed record AnalyzeRequest([property:Required,MaxLength(64000)] string SelectedSql,[property:Required,MaxLength(64)] ObjectMetadata[] Metadata,[property:Required] PlanSummary EstimatedPlan,[property:Required] string ServerFingerprint,[property:Required] string DatabaseFingerprint);
public sealed record ChatRequest([property:Required] AnalyzeRequest Context,[property:Required] string Topic);
public sealed record Candidate([property:Required,MaxLength(64000)] string Sql,[property:Required,MaxLength(8000)] string Explanation);
public sealed record AiAnswer([property:Required,MaxLength(8000)] string Summary,[property:Required,MaxLength(100)] string[] Issues,[property:Required,MaxLength(10)] Candidate[] Candidates,[property:Required,MaxLength(100)] string[] Warnings);
public sealed record Usage(long InputTokens,long CachedInputTokens,long CacheWriteTokens,long OutputTokens,string Model,decimal? EstimatedUsd,string EstimateKind);
public sealed record AnalysisResponse(string Id,string AiProvider,string Summary,string[] Issues,Candidate[] Candidates,string[] Warnings,Usage Usage);
public sealed record ValidateRequest([property:Required] Candidate Candidate);
public sealed record ValidationResponse(bool Valid,bool CanApply,string VerificationMode,string[] Errors);
public sealed record QueryResponse(string Sql,int TimeoutSeconds,string ExecutionLocation);
public sealed record ProcedureInput(int ObjectId,[property:Required,MaxLength(260)] string Name,string? Definition,bool Encrypted,bool SideEffect,[property:Range(0,double.MaxValue)] double CpuMs,[property:Range(0,double.MaxValue)] double LogicalReads,[property:Range(0,double.MaxValue)] double DurationMs);
public sealed record RankRequest([property:Required,MaxLength(1000)] ProcedureInput[] Procedures,[property:Range(1,100)] int TopN);
public sealed record RankedProcedure(int ObjectId,string Name,double Score,bool Eligible,string[] Reasons);
public sealed record RankResponse(RankedProcedure[] Candidates,RankedProcedure[] Skipped);
public sealed record PrepareRequest([property:Required] AnalyzeRequest Context,[property:Required] string Trigger,string? JobId=null,string? LeaseToken=null,int? ObjectId=null);
public sealed record Budget([property:Range(1,86400)] long TimeSeconds,[property:Range(1,86400000)] long CpuMs,[property:Range(1,long.MaxValue)] long LogicalReads,[property:Range(1,10000000)] long Tokens);
public sealed record JobCreate([property:Required] string DatabaseFingerprint,[property:Range(1,100)] int TopN,[property:Required,MinLength(1),MaxLength(100)] int[] ObjectIds,[property:Required] Budget Budget,[property:Range(0,10)] int MaxRetries);
public sealed record LeaseRequest([property:Required,MaxLength(64),RegularExpression("^[a-zA-Z0-9_-]+$")] string WorkerId,[property:Required] string DatabaseFingerprint,[property:Range(5,300)] int LeaseSeconds);
public sealed record JobCommand([property:Required] string LeaseToken,[property:Required] string DatabaseFingerprint);
public sealed record CheckpointRequest([property:Required] string LeaseToken,[property:Required] string DatabaseFingerprint,[property:Range(1,int.MaxValue)] int Sequence,[property:Required] string Status,[property:Range(0,long.MaxValue)] long ElapsedMs,[property:Range(0,long.MaxValue)] long CpuMs,[property:Range(0,long.MaxValue)] long LogicalReads,[property:Range(0,long.MaxValue)] long Tokens);
public sealed record JobItem(int ObjectId,string Status,int Attempts,int Sequence,long ElapsedMs,long CpuMs,long LogicalReads,long Tokens,long ReservedTokens=0,string? ReservationLeaseToken=null);
public sealed record JobRecord(string Id,DateTimeOffset CreatedAt,string DatabaseFingerprint,int TopN,Budget Budget,int MaxRetries,string Status,bool CancelRequested,string? WorkerId,string? LeaseToken,DateTimeOffset? LeaseExpires,DateTimeOffset? StartedAt,JobItem[] Items,bool Demo);
public sealed record HistoryRecord(string Id,DateTimeOffset CreatedAt,string ServerFingerprint,string DatabaseFingerprint,string SqlHash,string Provider,string Status,string VerificationStatus);
public sealed record UsageRecord(string Id,DateTimeOffset CreatedAt,Usage Usage);
public sealed record StatusRequest([property:Required] string Status);
public sealed record VerificationRequest([property:Required] string Id,[property:Required] string Status,[property:Required] string Mode);
public sealed record AuditInput([property:Required] string DatabaseFingerprint,[property:Range(1,int.MaxValue)] int ObjectId,[property:Required] string BeforeHash,[property:Required] string AfterHash,[property:Required] string Action);
public sealed record AuditRecord(string Id,DateTimeOffset CreatedAt,AuditInput Change,string PreviousHash,string Hash);
public sealed record MemoryInput([property:Required] string DatabaseFingerprint,[property:Required] string SqlHash,[property:Required] string Outcome);
public sealed record ScheduleInput([property:Required] string DatabaseFingerprint,[property:Range(1,100)] int TopN,[property:Range(5,10080)] int IntervalMinutes,bool Enabled);
public sealed record EventInput([property:Required] string DatabaseFingerprint,[property:Required] string Kind,[property:Range(0,double.MaxValue)] double Value);
public sealed record GovernanceRecord<T>(string Id,DateTimeOffset CreatedAt,T Data,string Status);
public sealed record ScheduleRun([property:Required] string JobId);
public sealed record HealthResponse(string Status,string AiProvider,bool ApiKeyConfigured,string ApiKeySource,string Model,string ResponsesEndpoint,string HistoryStorage,string ApiVersion,string ServiceMode,string Authentication,int RequestRateLimitPerMinute,string TenantIsolation,string Language);
public sealed record PrefilterResponse(bool Eligible,bool CanApply,string[] Reasons,string VerificationMode);
public sealed record AuditVerificationResponse(bool Valid,string Algorithm,bool ExternallyImmutable);
