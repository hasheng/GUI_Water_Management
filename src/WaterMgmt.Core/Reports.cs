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
            "th{background:#e8eef4}td:first-child,th:first-child{text-align:left}.bad{color:#b00020;font-weight:bold}.warn{color:#8a6d00;font-weight:bold}</style></head><body>");
        sb.Append($"<h1>Water Taking Summary – {E(p.SiteName)}</h1>");
        sb.Append("<p>" + string.Join(" · ", p.Permits.Select(pt => $"PTTW {E(pt.Number)} · Holder: {E(pt.Holder)} · Expires: {E(pt.Expires?.ToString("yyyy-MM-dd"))}")) + $" · Period: {(year?.ToString() ?? "all")}</p>");
        foreach (var n in ComplianceChecker.GetPermitNotices(p))
        {
            var color = n.Severity is "expired" or "critical" ? "#b00020" : "#8a6d00";
            sb.Append($"<p style='margin:8px 0;padding:6px 10px;font-weight:bold;color:{color};background:#fff8e1;border:1px solid #e0c868'>{E(n.Message)}</p>");
        }
        sb.Append("<h2>Takings by source</h2><table><tr><th>Source</th><th>Total (L)</th><th>Days</th><th>Max day (L)</th><th>Limit L/day</th><th>Max L/min</th><th>Limit L/min</th><th>Limit days/yr</th></tr>");
        foreach (var s in sum)
            sb.Append($"<tr><td>{E(s.Source)}</td><td>{N(s.TotalLitres)}</td><td>{s.DaysTaking}</td><td>{N(s.MaxDayLitres)}</td><td>{N(s.LimitLpd)}</td><td>{N(s.MaxLpm)}</td><td>{N(s.LimitLpm)}</td><td>{s.LimitDays?.ToString() ?? "—"}</td></tr>");
        sb.Append("</table>");
        var yr = year ?? p.Takings.Select(t => t.Date.Year).DefaultIfEmpty(DateTime.Today.Year).Max();
        var mon = Analytics.Monthly(p, yr);
        string[] monNames = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        sb.Append($"<h2>Monthly volumes \u2013 {yr} (L)</h2><table><tr><th>Source</th>");
        foreach (var mn in monNames) sb.Append($"<th>{mn}</th>");
        sb.Append("<th>Total</th><th>Days</th><th>Gap days</th></tr>");
        foreach (var m in mon)
        {
            sb.Append($"<tr><td>{E(m.Source)}</td>");
            foreach (var v in m.Volume) sb.Append($"<td>{N(v)}</td>");
            sb.Append($"<td>{N(m.Total)}</td><td>{m.DaysTaken}</td><td{(m.MissingDays > 0 ? " class='warn'" : "")}>{m.MissingDays}</td></tr>");
        }
        sb.Append("</table>");
        if (mon.Any(m => m.MissingDays > 0))
            sb.Append("<p class='warn'>Gap days = calendar days with no record at all between the first and last logged taking. Confirm with the client whether pumping stopped or the log is incomplete before filing.</p>");
        sb.Append("<h2>Exceedances</h2>");
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

    /// <summary>True when a source's permitted maximum allows 50,000 L/day or more —
    /// the threshold at which water taking becomes subject to Ontario's annual
    /// reporting regime.</summary>
    public static bool IsAnnualReportingSource(PermitSource s) =>
        (s.MaxLitresPerDay is { } lpd && lpd >= 50_000)
        || (s.MaxLitresPerDay == null && s.MaxLitresPerMinute is { } lpm && lpm * 60 * 24 >= 50_000);

    /// <summary>
    /// Annual water taking report in the MECP submission style: one table per permit
    /// with monthly takings (m³), annual total, days taken, observed peak rate and the
    /// permit limits side by side. Sources subject to annual reporting (max
    /// 50,000 L/day or more) are flagged. Rendered from live project data — compare
    /// against the current ministry form before filing; reports are due March 31
    /// following the reported year.
    /// </summary>
    public static string MecpHtml(SiteProject p, int? year = null)
    {
        var yr = year ?? p.Takings.Select(t => t.Date.Year).DefaultIfEmpty(DateTime.Today.Year).Max();
        string E(object? o) => WebUtility.HtmlEncode(o?.ToString() ?? "");
        string N(double? v) => v?.ToString("N1") ?? "—";
        var sb = new StringBuilder();
        sb.Append("<html><head><meta charset='utf-8'><style>body{font-family:Segoe UI,Arial;margin:30px}" +
            "table{border-collapse:collapse}td,th{border:1px solid #999;padding:4px 8px;text-align:right}" +
            "th{background:#e8eef4}td:first-child,th:first-child{text-align:left}" +
            ".bad{color:#b00020;font-weight:bold}.warn{color:#8a6d00;font-weight:bold}" +
            ".flag{background:#fff3cd;font-weight:bold}</style></head><body>");
        sb.Append($"<h1>Annual Water Taking Report – {E(p.SiteName)} ({yr})</h1>");
        foreach (var pt in p.Permits)
        {
            sb.Append($"<h2>PTTW {E(pt.Number)}</h2>");
            sb.Append($"<p>Holder: {E(pt.Holder)} · Issued: {E(pt.Issued?.ToString("yyyy-MM-dd"))} · Expires: {E(pt.Expires?.ToString("yyyy-MM-dd"))}</p>");
            foreach (var n in ComplianceChecker.GetPermitNotices(p))
            {
                var color = n.Severity is "expired" or "critical" ? "#b00020" : "#8a6d00";
                sb.Append($"<p style='margin:8px 0;padding:6px 10px;font-weight:bold;color:{color};background:#fff8e1;border:1px solid #e0c868'>{E(n.Message)}</p>");
            }
            var byId = pt.Sources.ToDictionary(s => s.Id);
            sb.Append("<table><tr><th>Source</th>");
            foreach (var mn in MonNames) sb.Append($"<th>{mn} (m³)</th>");
            sb.Append("<th>Total (m³)</th><th>Days</th><th>Max L/min</th><th>Permit L/min</th><th>Permit L/day</th><th>Permit days/yr</th><th>Annual report</th></tr>");
            foreach (var m in Analytics.Monthly(p, yr).Where(m => byId.ContainsKey(m.SourceId)))
            {
                var src = byId[m.SourceId];
                var flag = IsAnnualReportingSource(src);
                sb.Append($"<tr{(flag ? " class='flag'" : "")}><td>{E(m.Source)}</td>");
                foreach (var v in m.Volume) sb.Append($"<td>{N(v / 1000.0)}</td>");
                sb.Append($"<td>{N(m.Total / 1000.0)}</td><td>{m.DaysTaken}</td><td>{N(m.MaxRate.Max())}</td>");
                sb.Append($"<td>{N(src.MaxLitresPerMinute)}</td><td>{N(src.MaxLitresPerDay)}</td><td>{src.MaxDaysPerYear?.ToString() ?? "—"}</td>");
                sb.Append($"<td>{(flag ? "YES" : "no")}</td></tr>");
            }
            sb.Append("</table>");
        }
        sb.Append("<p style='margin-top:16px'>Volumes in cubic metres (1 m³ = 1,000 L). Rows flagged " +
            "<b>Annual report: YES</b> take under a permitted maximum of 50,000 L/day or more and " +
            "are subject to Ontario's annual water taking reporting. Reports for a calendar year " +
            "are due March 31 of the following year. Verify against the current ministry form before filing.</p>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    static string[] MonNames { get; } = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

    /// <summary>Same data as MecpHtml, one CSV row per source, volumes in m³.</summary>
    public static string MecpCsv(SiteProject p, int? year = null)
    {
        var yr = year ?? p.Takings.Select(t => t.Date.Year).DefaultIfEmpty(DateTime.Today.Year).Max();
        var sb = new StringBuilder("Permit,Source," + string.Join(",", MonNames.Select(m => m + "M3")) +
            ",TotalM3,DaysTaken,MaxLpm,PermitMaxLpm,PermitMaxLpd,PermitDaysPerYear,AnnualReporting\n");
        foreach (var pt in p.Permits)
        {
            var byId = pt.Sources.ToDictionary(s => s.Id);
            foreach (var m in Analytics.Monthly(p, yr).Where(m => byId.ContainsKey(m.SourceId)))
            {
                var src = byId[m.SourceId];
                sb.Append($"\"{pt.Number}\",\"{m.Source}\",");
                sb.Append(string.Join(",", m.Volume.Select(v => (v / 1000.0).ToString("F1"))));
                sb.Append($",{(m.Total / 1000.0):F1},{m.DaysTaken},{m.MaxRate.Max():F1},");
                sb.Append($"{src.MaxLitresPerMinute},{src.MaxLitresPerDay},{src.MaxDaysPerYear},{(IsAnnualReportingSource(src) ? "YES" : "no")}\n");
            }
        }
        return sb.ToString();
    }
}
