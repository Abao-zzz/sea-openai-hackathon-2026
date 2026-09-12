using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Alyvo.SsmsAiSqlAssistant
{
 // Ordered results hash their framed rows; unordered results add SHA-256 row
 // hashes modulo 2^256, retaining multiplicity without retaining result rows.
 public sealed class ResultFingerprint:IDisposable
 {
  private readonly bool ordered;
  private readonly SHA256 sequence=SHA256.Create();
  private readonly byte[] sum=new byte[32];
  private long count;
  private bool finished;
  public ResultFingerprint(bool ordered){this.ordered=ordered;}
  public void Add(byte[] row)
  {
   if(finished)throw new InvalidOperationException("Fingerprint 已結束。");
   byte[] digest;using(var sha=SHA256.Create())digest=sha.ComputeHash(row);
   if(ordered)sequence.TransformBlock(digest,0,digest.Length,digest,0);
   else{int carry=0;for(int i=0;i<sum.Length;i++){carry+=sum[i]+digest[i];sum[i]=(byte)carry;carry>>=8;}}
   checked{count++;}
  }
  public string Finish()
  {
   if(finished)throw new InvalidOperationException("Fingerprint 已結束。");finished=true;
   if(!ordered)sequence.TransformBlock(sum,0,sum.Length,sum,0);
   var tail=BitConverter.GetBytes(count);sequence.TransformFinalBlock(tail,0,tail.Length);
   return BitConverter.ToString(sequence.Hash).Replace("-","");
  }
  public static byte[] Encode(object[] values,string[] types,CompareInfo[] collations=null,CompareOptions[] options=null)
  {
   if(values.Length!=types.Length)throw new ArgumentException("欄位契約數量不同。");
   using(var data=new MemoryStream())using(var writer=new BinaryWriter(data,Encoding.UTF8,true))
   {
    writer.Write(values.Length);
    for(int i=0;i<values.Length;i++)
    {
     writer.Write(types[i]);var v=values[i];writer.Write(v!=null&&v!=DBNull.Value);if(v==null||v==DBNull.Value)continue;
     if(v is System.Data.SqlTypes.SqlDecimal number){writer.Write(number.IsPositive);writer.Write(number.Scale);foreach(var part in number.Data)writer.Write(part);}
     else if(v is byte[] bytes){writer.Write(bytes.Length);writer.Write(bytes);}
     else if(v is double d)writer.Write(Float(d,12));
     else if(v is float f)writer.Write(Float(f,6));
     else if(v is DateTime dt)writer.Write(dt.ToString("O",CultureInfo.InvariantCulture));
     else if(v is DateTimeOffset dto)writer.Write(dto.ToString("O",CultureInfo.InvariantCulture));
     else if(v is string s&&collations!=null&&collations[i]!=null){var key=collations[i].GetSortKey(s,options?[i]??CompareOptions.None).KeyData;writer.Write(key.Length);writer.Write(key);}
     else writer.Write(Convert.ToString(v,CultureInfo.InvariantCulture));
    }
    writer.Flush();return data.ToArray();
   }
  }
  private static string Float(double value,int digits)
  {
   if(double.IsNaN(value))return "NaN";if(double.IsPositiveInfinity(value))return "+Infinity";if(double.IsNegativeInfinity(value))return "-Infinity";if(value==0)return "0";
   return value.ToString("G"+digits,CultureInfo.InvariantCulture);
  }
  public void Dispose()=>sequence.Dispose();
 }
}
