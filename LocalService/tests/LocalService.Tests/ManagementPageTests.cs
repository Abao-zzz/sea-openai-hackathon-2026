using System.Text;
using System.Security.Cryptography;
using LocalService;
using Xunit;

namespace LocalService.Tests;
public class ManagementPageTests
{
 [Theory][InlineData("/history/ui")][InlineData("/usage/ui")][InlineData("/agent/governance/ui")]
 public async Task ScriptsUseExactCspHashAndSameOriginApis(string path)
 {
  await using var host=new Host();using var client=host.CreateClient();var response=await client.GetAsync(path);response.EnsureSuccessStatusCode();var html=await response.Content.ReadAsStringAsync();var script=html.Split("<script>")[1].Split("</script>")[0];var hash=Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)));Assert.Contains("'sha256-"+hash+"'",response.Headers.GetValues("Content-Security-Policy").Single());Assert.DoesNotContain("innerHTML",script);Assert.Contains("textContent",script);Assert.Contains("deleteAll",html);Assert.Contains("自訂",html);Assert.Contains("cache-write",html);Assert.DoesNotContain("script-src 'unsafe-inline'",response.Headers.GetValues("Content-Security-Policy").Single());
 }
 [Fact] public void ModelPricingEscapesMarkup()
 {
  var html=ManagementPages.Render("usage",new Dictionary<string,decimal[]>{{"</pre><script>alert(1)</script>",[1,2,3,4]}});Assert.Equal(1,html.Split("<script>").Length-1);Assert.Contains("未設定模型不估價",html);
 }
}
