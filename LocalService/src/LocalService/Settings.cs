using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
namespace LocalService;
public sealed class Settings
{
    public string DataDir { get; init; } = "";
    public string? Key { get; init; }
    public string KeySource { get; init; } = "not-configured";
    public string Model { get; init; } = "";
    public Uri Endpoint { get; init; } = new("https://api.openai.com/v1/responses");
    public int TimeoutSeconds { get; init; } = 30;
    public int RetentionDays { get; init; } = 180;
    public bool AllowRemote { get; init; }
    public string? ServiceKey { get; init; }
    public string TenantId { get; init; } = "local-user";
    public int RateLimit { get; init; } = 300;
    public Dictionary<string,decimal[]> Prices { get; init; } = [];
    public const int MaxBody = 262144;
    public static Settings Load(IConfiguration config)
    {
        string? Read(string name) => config[name];
        var dir=Read("SSMS_AI_SQL_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Alyvo","SsmsAiSqlAssistant");
        var key=Read("OPENAI_API_KEY"); var source="OPENAI_API_KEY";
        if(string.IsNullOrWhiteSpace(key))
        {
            var file=Read("OPENAI_API_KEY_FILE");
            var explicitFile=!string.IsNullOrWhiteSpace(file); if(!explicitFile)file=null;
            file ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Alyvo","SsmsAiSqlAssistant","openai.key");
            source="not-configured"; key=null;
            try { if(explicitFile || File.Exists(file)) { key=File.ReadAllText(file).Trim(); source=string.IsNullOrWhiteSpace(key)?"configured-file-unreadable":"configured-file"; } }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { source="configured-file-unreadable"; }
        }
        if(string.IsNullOrWhiteSpace(key)) key=null;
        var endpoint=new Uri(Read("OPENAI_RESPONSES_URL") ?? "https://api.openai.com/v1/responses");
        if((endpoint.Scheme!="https" && !(endpoint.Scheme=="http" && endpoint.IsLoopback)) || !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment)) throw new InvalidOperationException("Responses URL 必須使用 HTTPS（loopback 除外），不得含認證或查詢參數。");
        var prices=System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,decimal[]>>(Read("SSMS_AI_MODEL_PRICES_JSON") ?? "{}")!; if(prices.Any(x=>x.Value.Length!=4 || x.Value.Any(v=>v<0)))throw new InvalidOperationException("模型價格必須包含四個非負數。"); return new Settings { Prices=prices,DataDir=dir,Key=key,KeySource=source,Model=Read("OPENAI_MODEL") ?? "",Endpoint=endpoint,AllowRemote=Read("SSMS_AI_ALLOW_REMOTE")=="true",ServiceKey=Read("SSMS_AI_SERVICE_KEY"),TenantId=Read("SSMS_AI_TENANT_ID") ?? "local-user" };
    }
    public HealthResponse Health()=>new("ok",Key is null?"local-rules":"openai",Key is not null,KeySource,Model,Endpoint.GetLeftPart(UriPartial.Path),"sqlite",Contract.Version,AllowRemote?"remote-opt-in":"local-loopback",AllowRemote?"service-api-key":"loopback-only",RateLimit,TenantId,"zh-TW");
}
public sealed class SecurityGate(Settings settings,TimeProvider time)
{
    private readonly ConcurrentDictionary<string,(long Window,int Count)> rates=[];
    public void Check(HttpContext c)
    {
        var ip=c.Connection.RemoteIpAddress;
        var local=ip is not null && IPAddress.IsLoopback(ip);
        string device="loopback";
        if(!local)
        {
            var h=c.Request.Headers;
            if(!settings.AllowRemote || !c.Request.IsHttps || string.IsNullOrWhiteSpace(settings.ServiceKey) || !Equal(h["X-Service-Key"].ToString(),settings.ServiceKey) || h["X-Tenant-Id"]!=settings.TenantId || !System.Text.RegularExpressions.Regex.IsMatch(h["X-Device-Id"].ToString(),"^[a-zA-Z0-9_-]{1,64}$") || h["X-Api-Version"]!=Contract.Version || !Guid.TryParse(h["X-Request-Id"],out _)) throw new ApiError(403,"remote_denied","遠端連線缺少必要的安全條件。");
            device=h["X-Device-Id"].ToString();
        }
        if(local && c.Request.Host.Host!="localhost" && (!IPAddress.TryParse(c.Request.Host.Host,out var host) || !IPAddress.IsLoopback(host))) throw new ApiError(403,"invalid_host","不允許此主機名稱。");
        if(c.Request.Headers.TryGetValue("Origin",out var origin) && origin.ToString()!=$"{c.Request.Scheme}://{c.Request.Host}") throw new ApiError(403,"origin_denied","不允許跨來源存取。");
        if(c.Request.Headers.TryGetValue("X-Api-Version",out var version) && version!=Contract.Version) throw new ApiError(409,"api_version","API 版本不相容。");
        if(c.Request.ContentLength>Settings.MaxBody) throw new ApiError(413,"body_too_large","要求內容超過大小限制。");
        var window=time.GetUtcNow().ToUnixTimeSeconds()/60;
        foreach(var old in rates.Where(x=>x.Value.Window<window-1)) rates.TryRemove(old.Key,out _);
        var state=rates.AddOrUpdate(device,(_)=>(window,1),(_,v)=>v.Window==window?(window,v.Count+1):(window,1));
        if(state.Count>settings.RateLimit) throw new ApiError(429,"rate_limit","請求過於頻繁，請稍後重試。");
    }
    private static bool Equal(string a,string b)=>CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)),SHA256.HashData(Encoding.UTF8.GetBytes(b)));
}
