using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alyvo.SsmsAiSqlAssistant;
using Newtonsoft.Json.Linq;
using Xunit;
public class TableSchemaTests
{
 [Theory][InlineData("nvarchar",80,0,0,"nvarchar(40)")][InlineData("varchar",-1,0,0,"varchar(max)")][InlineData("decimal",9,18,2,"decimal(18,2)")][InlineData("datetime2",8,0,3,"datetime2(3)")]
 public void FullColumnType(string name,int length,int precision,int scale,string expected)=>Assert.Equal(expected,TableSchema.TypeName(name,length,precision,scale));
 [Fact] public async Task RealReferencedTableIncludesPrimaryKeyAndCoveringIndex()
 {
  var c=new ConnectionContext{Server="localhost",Database="AlyvoStage5Demo1",TrustServerCertificate=true};var payload=JObject.FromObject(await new DbWorker().BuildPayload("SELECT CustomerId, Amount FROM dbo.Orders WHERE OrderDate >= '20250101';",c,CancellationToken.None));var table=payload["metadata"].Single();Assert.Contains(table["columns"],x=>x["name"].ToString()=="Amount"&&x["sqlType"].ToString()=="decimal(18,2)");Assert.Contains(table["indexes"],x=>x["primaryKey"].Value<bool>());Assert.Contains(table["indexes"],x=>x["keyColumns"].Any(k=>k["name"].ToString()=="OrderDate")&&x["includedColumns"].Any(k=>k.ToString()=="Amount"));
 }
}
