using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
namespace Alyvo.SsmsAiSqlAssistant
{
 public static class TableSchema
 {
  public static string TypeName(string name,int length,int precision,int scale)
  {
   switch(name.ToLowerInvariant()){
    case "nvarchar":case "nchar":return name+"("+(length<0?"max":(length/2).ToString(CultureInfo.InvariantCulture))+")";
    case "varchar":case "char":case "varbinary":case "binary":return name+"("+(length<0?"max":length.ToString(CultureInfo.InvariantCulture))+")";
    case "decimal":case "numeric":return name+"("+precision+","+scale+")";
    case "datetime2":case "datetimeoffset":case "time":return name+"("+scale+")";
    default:return name;
   }
  }
  public static async Task<object> Read(SqlConnection connection,string name,CancellationToken token)
  {
   const string sql=@"IF ISNULL(HAS_PERMS_BY_NAME(@name,'OBJECT','VIEW DEFINITION'),0)<>1 THROW 51001,'VIEW DEFINITION required for complete schema and indexes',1;
SELECT c.name,t.name,c.is_nullable,c.max_length,c.precision,c.scale,t.is_user_defined,SCHEMA_NAME(t.schema_id)
FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id WHERE c.object_id=OBJECT_ID(@name) ORDER BY c.column_id;
SELECT i.index_id,i.name,i.type_desc,i.is_unique,i.is_primary_key,i.is_disabled,i.filter_definition,c.name,ic.key_ordinal,ic.is_descending_key,ic.is_included_column
FROM sys.indexes i LEFT JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id LEFT JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE i.object_id=OBJECT_ID(@name) AND i.index_id>0 AND i.is_hypothetical=0 ORDER BY i.index_id,ic.is_included_column,ic.key_ordinal,ic.index_column_id;";
   using(var cmd=new SqlCommand(sql,connection){CommandTimeout=10}){cmd.Parameters.AddWithValue("@name",name);using(token.Register(()=>cmd.Cancel()))using(var r=await cmd.ExecuteReaderAsync(token)){
    var columns=new List<object>();while(await r.ReadAsync(token)){var type=r.GetBoolean(6)?DbWorker.Quote(r.GetString(7))+"."+DbWorker.Quote(r.GetString(1)):TypeName(r.GetString(1),r.GetInt16(3),r.GetByte(4),r.GetByte(5));columns.Add(new{name=r.GetString(0),sqlType=type,nullable=r.GetBoolean(2)});if(columns.Count>200)throw new InvalidOperationException("引用資料表超過 200 欄，無法完整傳送 schema。");}
    if(columns.Count==0)throw new InvalidOperationException("引用物件 metadata 不完整，已停止分析。");await r.NextResultAsync(token);var indexes=new JArray();JObject index=null;int previous=-1;
    while(await r.ReadAsync(token)){var id=r.GetInt32(0);if(id!=previous){previous=id;index=new JObject{{"name",r.GetString(1)},{"type",r.GetString(2)},{"unique",r.GetBoolean(3)},{"primaryKey",r.GetBoolean(4)},{"disabled",r.GetBoolean(5)},{"filter",r.IsDBNull(6)?null:r.GetString(6)},{"keyColumns",new JArray()},{"includedColumns",new JArray()}};indexes.Add(index);if(indexes.Count>64)throw new InvalidOperationException("引用資料表超過 64 個索引，無法完整傳送索引資訊。");}
     if(!r.IsDBNull(7)){if(r.GetBoolean(10))((JArray)index["includedColumns"]).Add(r.GetString(7));else ((JArray)index["keyColumns"]).Add(new JObject{{"name",r.GetString(7)},{"ordinal",r.GetByte(8)},{"descending",r.GetBoolean(9)}});}
    }
    return new{name,columns=columns.ToArray(),indexes};
   }}
  }
 }
}
