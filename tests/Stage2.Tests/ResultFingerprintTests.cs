using System;
using System.Globalization;
using Alyvo.SsmsAiSqlAssistant;
using Xunit;

public class ResultFingerprintTests
{
 static string Hash(bool ordered,params string[] rows){using(var h=new ResultFingerprint(ordered)){foreach(var r in rows)h.Add(ResultFingerprint.Encode(new object[]{r},new[]{"nvarchar"}));return h.Finish();}}
 [Fact] public void UnorderedPreservesMultiplicityWithoutPreservingOrder(){Assert.Equal(Hash(false,"a","b","a"),Hash(false,"b","a","a"));Assert.NotEqual(Hash(false,"a","b"),Hash(false,"a","b","a"));Assert.NotEqual(Hash(true,"a","b"),Hash(true,"b","a"));}
 [Fact] public void NullEmptyAndTextNullDiffer(){Assert.NotEqual(Hash(false,(string)null),Hash(false,""));Assert.NotEqual(Hash(false,(string)null),Hash(false,"null"));}
 static byte[] Float(object value,string type)=>ResultFingerprint.Encode(new[]{value},new[]{type});
 [Fact] public void FloatingPointCanonicalization(){Assert.Equal(Float(0d,"float"),Float(-0d,"float"));Assert.Equal(Float(1.2345678901231d,"float"),Float(1.2345678901232d,"float"));Assert.Equal(Float(1.234561f,"real"),Float(1.234562f,"real"));Assert.NotEqual(Float(double.NaN,"float"),Float(double.PositiveInfinity,"float"));Assert.NotEqual(Float(double.NegativeInfinity,"float"),Float(double.PositiveInfinity,"float"));}
 [Fact] public void FramingPreventsConcatenationAmbiguity(){Assert.NotEqual(ResultFingerprint.Encode(new object[]{"ab","c"},new[]{"varchar","varchar"}),ResultFingerprint.Encode(new object[]{"a","bc"},new[]{"varchar","varchar"}));}
 [Fact] public void ExplicitCollationControlsCaseAndAccent(){var c=new[]{CultureInfo.GetCultureInfo("en-US").CompareInfo};byte[] E(string s,CompareOptions o)=>ResultFingerprint.Encode(new object[]{s},new[]{"nvarchar"},c,new[]{o});Assert.Equal(E("É",CompareOptions.IgnoreCase|CompareOptions.IgnoreNonSpace),E("e",CompareOptions.IgnoreCase|CompareOptions.IgnoreNonSpace));Assert.NotEqual(E("É",CompareOptions.None),E("e",CompareOptions.None));}
}
