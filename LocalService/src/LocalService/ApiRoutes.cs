using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace LocalService;
public sealed class ApiRoutes(WebApplication app)
{
    private readonly JsonObject paths=[];
    private readonly JsonObject schemas=[];
    private readonly NullabilityInfoContext nullability=new();
    public object Document()=>new {openapi="3.1.0",info=new {title="SSMS AI SQL LocalService",version=Contract.Version,description="Loopback-only by default. Non-loopback requires HTTPS, opt-in, X-Service-Key, X-Tenant-Id, X-Device-Id, X-Api-Version and UUID X-Request-Id. JSON errors are fail-closed. List endpoints use offset/limit."},servers=new[]{new {url="http://127.0.0.1:46217"}},paths,components=new {schemas,securitySchemes=new {serviceKey=new {type="apiKey",name="X-Service-Key",@in="header"}}}};
    public void Get<T>(string path,Func<HttpContext,T> action){Describe(path,"get",null,typeof(T));app.MapGet(path,(HttpContext c)=>Bound(action(c)));}
    public void Post<T,R>(string path,Func<HttpContext,T,R> action)=>PostAsync<T,R>(path,(c,v)=>Task.FromResult(action(c,v)));
    public void PostAsync<T,R>(string path,Func<HttpContext,T,Task<R>> action)
    {
        Describe(path,"post",typeof(T),typeof(R));app.MapPost(path,async (HttpContext c)=>
        {
            if(!c.Request.HasJsonContentType())throw new ApiError(415,"json_required","必須使用 application/json。");
            using var body=await JsonDocument.ParseAsync(c.Request.Body,new JsonDocumentOptions {MaxDepth=32},c.RequestAborted);
            ValidatePresence(body.RootElement,typeof(T));var input=body.RootElement.Deserialize<T>(Contract.Json);Contract.Check(input);return Bound(await action(c,input!));
        });
    }
    public void Delete<T>(string path,Func<HttpContext,T> action){Describe(path,"delete",null,typeof(T));app.MapDelete(path,(HttpContext c)=>Bound(action(c)));}
    public void File(string path,Func<HttpContext,string> action,string contentType){Describe(path,"get",null,typeof(string),contentType);app.MapGet(path,(HttpContext c)=>Results.Text(action(c),contentType,System.Text.Encoding.UTF8));}
    private static IResult Bound<T>(T value)
    {
        var bytes=JsonSerializer.SerializeToUtf8Bytes(value,Contract.Json);
        if(bytes.Length>Settings.MaxBody)throw new ApiError(413,"response_too_large","回應超過大小限制，請縮小查詢範圍。");
        return Results.Bytes(bytes,"application/json; charset=utf-8");
    }
    private void ValidatePresence(JsonElement json,Type type)
    {
        if(type.IsArray){if(json.ValueKind!=JsonValueKind.Array)throw new JsonException();foreach(var item in json.EnumerateArray())ValidatePresence(item,type.GetElementType()!);return;}
        if(type.Namespace!="LocalService")return;
        if(json.ValueKind!=JsonValueKind.Object)throw new JsonException();
        foreach(var p in type.GetProperties()) {var name=JsonNamingPolicy.CamelCase.ConvertName(p.Name);if(!json.TryGetProperty(name,out var v)){if(nullability.Create(p).ReadState!=NullabilityState.Nullable)throw new ApiError(400,"missing_required","缺少必要欄位。");}else if(v.ValueKind==JsonValueKind.Null && nullability.Create(p).ReadState!=NullabilityState.Nullable)throw new ApiError(400,"missing_required","必要欄位不得為空值。");else if(v.ValueKind!=JsonValueKind.Null)ValidatePresence(v,p.PropertyType);}
    }
    private void Describe(string path,string method,Type? request,Type response,string content="application/json")
    {
        var operation=new JsonObject { ["operationId"]=method+"_"+System.Text.RegularExpressions.Regex.Replace(path,"[^a-zA-Z0-9]","_"),["responses"]=new JsonObject{["200"]=new JsonObject{["description"]="成功",["content"]=new JsonObject{[content]=new JsonObject{["schema"]=Schema(response)}}}}};
        var responses=(JsonObject)operation["responses"]!;
        foreach(var status in new[]{400,403,404,409,413,415,422,429,499,500,502,504})responses[status.ToString()]=new JsonObject { ["description"]="錯誤；不得視為成功",["content"]=new JsonObject{["application/json"]=new JsonObject{["schema"]=Schema(typeof(ErrorResponse))}}};
        if(request is not null)operation["requestBody"]=new JsonObject { ["required"]=true,["content"]=new JsonObject{["application/json"]=new JsonObject{["schema"]=Schema(request)}}};
        var parameters=new JsonArray(); if(method=="get" && (response.IsArray || (response.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(response)))) foreach(var key in new[]{"offset","limit"})parameters.Add(new JsonObject{["name"]=key,["in"]="query",["required"]=false,["schema"]=new JsonObject{["type"]="integer",["minimum"]=key=="offset"?0:1,["maximum"]=key=="offset"?int.MaxValue:200,["default"]=key=="offset"?0:100}});foreach(System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(path,"{([^}]+)}"))parameters.Add(new JsonObject{["name"]=m.Groups[1].Value,["in"]="path",["required"]=true,["schema"]=new JsonObject{["type"]="string"}});
        if(parameters.Count>0)operation["parameters"]=parameters;
        if(paths[path] is null)paths[path]=new JsonObject();paths[path]![method]=operation;
    }
    private static string[] Values(Type type,string name)
    {
        if(name=="Status" && type==typeof(JobRecord))return ["pending","running","completed","cancelled","budget-exhausted"];
        if(name=="Status" && type==typeof(JobItem))return ["pending","running","completed","failed","skipped"];
        if(name=="Status" && type==typeof(CheckpointRequest))return ["running","completed","failed","skipped"];
        if(name=="Status" && type==typeof(VerificationRequest))return ["passed","failed","unknown"];
        if(name=="Status" && type==typeof(HistoryRecord))return ["analyzed","reviewed","rejected","archived"];
        if(name=="VerificationMode" || (name=="Mode" && type==typeof(VerificationRequest)))return ["contract-only"];
        if(name=="Trigger")return ["single-user","batch-user"];
        if(name=="Topic")return ["explanation","risks","indexes"];
        if(name=="Action")return ["apply","restore","reject"];
        if(name=="Outcome")return ["passed","failed","rejected"];
        if(name=="Kind" && type==typeof(EventInput))return ["cpu-regression","io-regression","duration-regression","worker-disconnected"];
        if(name=="AiProvider" || name=="Provider")return ["openai","local-rules"];
        if(name=="ApiKeySource")return ["OPENAI_API_KEY","configured-file","not-configured","configured-file-unreadable"];
        if(name=="EstimateKind")return ["upper-bound","unknown-model","estimated","not-applicable"];
        return [];
    }
    private JsonObject Schema(Type type)
    {
        var underlying=Nullable.GetUnderlyingType(type);if(underlying is not null)return new JsonObject{["anyOf"]=new JsonArray(Schema(underlying),new JsonObject{["type"]="null"})};
        if(type==typeof(string))return new(){["type"]="string"};
        if(type==typeof(bool))return new(){["type"]="boolean"};
        if(type==typeof(DateTimeOffset) || type==typeof(DateTime))return new(){["type"]="string",["format"]="date-time"};
        if(type==typeof(int) || type==typeof(long))return new(){["type"]="integer",["format"]=type==typeof(int)?"int32":"int64"};
        if(type==typeof(double) || type==typeof(decimal))return new(){["type"]="number"};
        if(type.IsArray)return new(){["type"]="array",["items"]=Schema(type.GetElementType()!)};
        if(type.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(type))return new(){["type"]="array",["items"]=Schema(type.GenericTypeArguments[0])};
        var name=type.Name.Contains('<')?"Response"+Contract.Hash(type.ToString())[..12]:type.Name.Split('`')[0]+(type.IsGenericType?string.Join("",type.GenericTypeArguments.Select(x=>x.Name)):"");
        if(!schemas.ContainsKey(name))
        {
            schemas[name]=new JsonObject();var properties=new JsonObject();var required=new JsonArray();
            foreach(var p in type.GetProperties()){var prop=JsonNamingPolicy.CamelCase.ConvertName(p.Name);var schema=Schema(p.PropertyType);if(nullability.Create(p).ReadState==NullabilityState.Nullable && !p.PropertyType.IsValueType)schema=new JsonObject{["anyOf"]=new JsonArray(schema,new JsonObject{["type"]="null"})};else required.Add(prop);if(p.GetCustomAttribute<MaxLengthAttribute>() is {} max)schema[p.PropertyType==typeof(string)?"maxLength":"maxItems"]=max.Length;if(p.GetCustomAttribute<MinLengthAttribute>() is {} min)schema[p.PropertyType==typeof(string)?"minLength":"minItems"]=min.Length;if(p.GetCustomAttribute<RangeAttribute>() is {} range){schema["minimum"]=Convert.ToDouble(range.Minimum);schema["maximum"]=Convert.ToDouble(range.Maximum);}if(p.GetCustomAttribute<RegularExpressionAttribute>() is {} re)schema["pattern"]=re.Pattern;var values=Values(type,p.Name);if(values.Length>0)schema["enum"]=new JsonArray(values.Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray());properties[prop]=schema;}
            schemas[name]=new JsonObject{["type"]="object",["properties"]=properties,["required"]=required,["additionalProperties"]=true};
        }
        return new(){["$ref"]="#/components/schemas/"+name};
    }
}
