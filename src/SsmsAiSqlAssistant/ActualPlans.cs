using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json;

namespace Alyvo.SsmsAiSqlAssistant
{
 public sealed class ActualPlanRecord
 {
  public string Hash,Path,Summary;public bool Oversize;public DateTimeOffset CreatedUtc;
 }
 public static class ActualPlans
 {
  private static readonly object Sync=new object();
  public static string Root=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Alyvo","SsmsAiSqlAssistant","actual-plans");
  public static ActualPlanRecord Save(string databaseFingerprint,string queryFingerprint,string xml,int maxBytes=2097152)
  {
   if(!System.Text.RegularExpressions.Regex.IsMatch(databaseFingerprint??"","^[A-Fa-f0-9]{64}$")||!System.Text.RegularExpressions.Regex.IsMatch(queryFingerprint??"","^[A-Fa-f0-9]{64}$"))throw new InvalidOperationException("Plan fingerprint 無效。");
   if(string.IsNullOrWhiteSpace(xml))throw new InvalidOperationException("缺少 actual plan。");
   var bytes=Encoding.UTF8.GetBytes(xml);var record=new ActualPlanRecord{Hash=ConnectionContext.Hash(xml),Oversize=bytes.Length>maxBytes,CreatedUtc=DateTimeOffset.UtcNow};
   record.Summary=record.Oversize?"Actual plan 超過大小上限；僅保存 hash 與 oversize 狀態。":Describe(xml);
   lock(Sync)
   {
    var directory=System.IO.Path.Combine(Root,databaseFingerprint);Directory.CreateDirectory(directory);
    var prefix=queryFingerprint+"_";var stem=prefix+DateTime.UtcNow.ToString("yyyyMMddHHmmssfffffff")+"_"+Guid.NewGuid().ToString("N");
    if(!record.Oversize){record.Path=System.IO.Path.Combine(directory,stem+".sqlplan");File.WriteAllBytes(record.Path,bytes);}
    File.WriteAllText(System.IO.Path.Combine(directory,stem+".json"),JsonConvert.SerializeObject(record),Encoding.UTF8);
    foreach(var file in new DirectoryInfo(directory).GetFiles(prefix+"*.json").OrderByDescending(f=>f.Name,StringComparer.Ordinal).Skip(20))
    {var plan=System.IO.Path.ChangeExtension(file.FullName,".sqlplan");if(File.Exists(plan))File.Delete(plan);file.Delete();}
   }
   return record;
  }
  public static string Describe(string xml)
  {
   XDocument doc;using(var r=XmlReader.Create(new StringReader(xml),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=2097152}))doc=XDocument.Load(r);
   XNamespace ns="http://schemas.microsoft.com/sqlserver/2004/07/showplan";
   var ops=doc.Descendants(ns+"RelOp").Select(x=>(string)x.Attribute("PhysicalOp")??"").ToArray();
   var warnings=doc.Descendants(ns+"Warnings").Select(x=>string.Join(" ",x.Attributes().Select(a=>a.Name.LocalName+"="+a.Value).Concat(x.Elements().Select(e=>e.Name.LocalName))));
   var memory=doc.Descendants(ns+"MemoryGrantInfo").Select(x=>string.Join(" ",x.Attributes().Select(a=>a.Name.LocalName+"="+a.Value)));
   var parameters=doc.Descendants(ns+"ParameterList").SelectMany(x=>x.Elements()).Select(x=>string.Join(" ",x.Attributes().Where(a=>new[]{"Column","ParameterDataType","ParameterCompiledValue","ParameterRuntimeValue"}.Contains(a.Name.LocalName)).Select(a=>a.Name.LocalName+"="+a.Value)));
   return "Seek："+ops.Count(x=>x.Contains("Seek"))+"；Scan："+ops.Count(x=>x.Contains("Scan"))+"\nWarnings："+string.Join("；",warnings)+"\nMemory grant："+string.Join("；",memory)+"\nCompiled / runtime parameters：\n"+string.Join("\n",parameters);
  }
 }
}
