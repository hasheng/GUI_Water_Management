using System.Net;
using System.Text;

namespace WaterMgmt.Core;

public static class ReportBuilder
{
    public static string Html(SiteProject p, int? year)
    {
        var sum = ComplianceChecker.Summarize(p, year);
        var exc = ComplianceChecker.Check(p, year);
        string E(object? o) => WebUtility.HtmlEncode(o?.ToString() ?? "");
        string N(double? v) => v?.ToString("N0") ?? "—";
        var sb = new StringBuilder();
        sb.Append("<html><head><meta charset='utf-8'><style>body{font-family:Segoe UI,Arial;margin:30px}" +
            "table{border-collapse:collapse}td,th{border:1px solid #999;padding:4px 8px;text-align:right}" +
            "th{background:#e8eef4}td:first-child,th:first-child{text-align:left}.bad{color:#b00020;font-weight:bold}</style></head><body>");
        sb.Append($"<h1>Water Taking Summary – {E(p.SiteName)}</h1>");
        sb.Append($"<p>PTTW {E(p.Permit.Number)} · Holder: {E(p.Permit.Holder)} · Expires: {E(p.Permit.Expires?.ToString("yyyy-MM-dd"))} · Period: {(year?.ToString() ?? "all")}</p>");
        sb.Append("<h2>Takings by source</h2><table><tr><th>Source</th><th>Total (L)</th><th>Days</th><th>Max day (L)</th><th>Limit L/day</th><th>Max L/min</th><th>Limit L/min</th><th>Limit days/yr</th></tr>");
        foreach (var s in sum)
            sb.Append($"<tr><td>{E(s.Source)}</td><td>{N(s.TotalLitres)}</td><td>{s.DaysTaking}</td><td>{N(s.MaxDayLitres)}</td><td>{N(s.LimitLpd)}</td><td>{N(s.MaxLpm)}</td><td>{N(s.LimitLpm)}</td><td>{s.LimitDays?.ToString() ?? "—"}</td></tr>");
        sb.Append("</table><h2>Exceedances</h2>");
        if (exc.Count == 0) sb.Append("<p>None recorded.</p>");
        else
        {
            sb.Append("<table><tr><th>Date</th><th>Source</th><th>Rule</th><th>Actual</th><th>Limit</th></tr>");
            foreach (var x in exc)
                sb.Append($"<tr class='bad'><td>{x.Date:yyyy-MM-dd}</td><td>{E(x.Source)}</td><td>{E(x.Rule)}</td><td>{x.Actual:N0}</td><td>{x.Limit:N0}</td></tr>");
            sb.Append("</table>");
        }
        sb.Append("<h2>Water balance (L/min)</h2><table><tr><th>Object</th><th>In</th><th>Out</th><th>Net</th><th>Status</th><th>Note</th></tr>");
        foreach (var b in MassBalance.Compute(p).Where(b => b.Status != BalanceStatus.Boundary))
            sb.Append($"<tr{(MassBalance.IsProblem(b.Status) ? " class='bad'" : "")}><td>{E(b.Feature.Name)}</td><td>{b.In + b.OtherIn:N0}</td><td>{b.Out + b.OtherLoss:N0}</td><td>{b.Net:N0}</td><td>{b.Status}</td><td>{E(b.Note)}</td></tr>");
        sb.Append("</table>");
        sb.Append("<h2>Site features</h2><ul>");
        foreach (var f in p.Features.OrderBy(f => f.Kind))
            sb.Append($"<li>{f.Kind}: {E(f.Name)}{(f.Dewatered ? " (dewatered)" : "")}</li>");
        sb.Append("</ul></body></html>");
        return sb.ToString();
    }

    public static string Csv(SiteProject p, int? year)
    {
        var sb = new StringBuilder("Source,TotalLitres,DaysTaking,MaxDayLitres,LimitLpd,MaxLpm,LimitLpm,LimitDaysPerYear\n");
        foreach (var s in ComplianceChecker.Summarize(p, year))
            sb.AppendLine($"\"{s.Source}\",{s.TotalLitres:F0},{s.DaysTaking},{s.MaxDayLitres:F0},{s.LimitLpd},{s.MaxLpm:F1},{s.LimitLpm},{s.LimitDays}");
        return sb.ToString();
    }
}
