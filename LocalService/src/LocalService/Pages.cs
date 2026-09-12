using System.Net;
using System.Text;
namespace LocalService;
public static class Pages
{
    public static string Csv(string[] headers,IEnumerable<string[]> rows)
    {
        static string Cell(string x)=>"\""+((x.Length>0 && "=+-@\t\r\n".Contains(x[0]))?"'":"")+x.Replace("\"","\"\"")+"\"";
        return "\uFEFF"+string.Join(",",headers.Select(Cell))+"\r\n"+string.Join("\r\n",rows.Select(r=>string.Join(",",r.Select(Cell))));
    }
    public static string Page(string title,string[] headings,IEnumerable<string[]> rows,string? export=null)
    {
        static string H(string x)=>WebUtility.HtmlEncode(x);
        var html=new StringBuilder("<!doctype html><html lang=\"zh-TW\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>").Append(H(title)).Append("</title><style>");
        html.Append("*{box-sizing:border-box}body{margin:0;background:#F6F7F9;color:#111827;font:13px system-ui}header{background:white;border-bottom:1px solid #DDE1E6;padding:12px 16px 10px;display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap}h1{font-size:20px;margin:0 0 5px}p{color:#4B5563;margin:4px 0}nav{display:flex;gap:8px;flex-wrap:wrap}a{color:#1D4ED8}nav a,.primary{display:inline-block;padding:6px 12px;min-height:32px;border:1px solid #DDE1E6;border-radius:4px;text-decoration:none}.primary{background:#1D4ED8;color:white;font-weight:600}a:hover{background:#EFF6FF;color:#1D4ED8}a:active{background:#DDE1E6}a:focus-visible{outline:2px solid #1D4ED8;outline-offset:2px}main{max-width:1120px;margin:16px auto;padding:0 16px 22px;min-width:0}.card{background:white;border:1px solid #DDE1E6;border-radius:4px;padding:12px;margin-bottom:10px;min-width:0}.table{overflow-x:auto;max-width:100%}table{border-collapse:collapse;min-width:100%}th,td{text-align:left;padding:9px;border-bottom:1px solid #DDE1E6;white-space:nowrap}th{background:#F3F4F6}tr:nth-child(even){background:#FAFBFC}tr:hover{background:#EFF6FF}.empty{color:#6B7280}code{font-family:monospace}");
        html.Append("</style><header><div><h1>").Append(H(title)).Append("</h1><p>SSMS AI SQL 效能助手 · 本機服務</p></div><nav><a href='/'>首頁</a><a href='/usage/ui'>用量中心</a><a href='/history/ui'>歷史紀錄</a><a href='/jobs/ui'>批次工作</a><a href='/agent/governance/ui'>治理與稽核</a></nav></header><main><section class='card'><p>目前紀錄 · 時間以本機時區顯示。</p><a class='primary' href=''>重新整理</a>");
        if(export is not null)html.Append(" <a href='").Append(H(export)).Append("'>匯出 CSV</a>");
        html.Append("</section><section class='card'><div class='table'><table><thead><tr>");foreach(var h in headings)html.Append("<th>").Append(H(h=="UTC 時間"?"本機時間":h)).Append("</th>");html.Append("</tr></thead><tbody>");var count=0;
        foreach(var row in rows){count++;html.Append("<tr>");foreach(var cell in row){var shown=cell;if(cell.Length>20 && cell[4]=='-' && cell[10]=='T' && DateTimeOffset.TryParse(cell,out var date))shown=date.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");html.Append("<td>").Append(H(shown)).Append("</td>");}html.Append("</tr>");}
        if(count==0)html.Append("<tr><td class='empty' colspan='").Append(headings.Length).Append("'>目前沒有紀錄</td></tr>");
        return html.Append("</tbody></table></div></section></main></html>").ToString();
    }
}

