namespace WaterMgmt.Core;

public record Exceedance(DateTime Date, string Source, string Rule, double Actual, double Limit);

public record SourceSummary(string Source, double TotalLitres, int DaysTaking, double MaxDayLitres,
    double? MaxLpm, double? LimitLpd, double? LimitLpm, int? LimitDays);

public static class ComplianceChecker
{
    public static List<Exceedance> Check(SiteProject p, int? year = null)
    {
        var res = new List<Exceedance>();
        var recs = p.Takings.Where(t => year == null || t.Date.Year == year).ToList();
        foreach (var src in p.Permit.Sources)
        {
            var daily = recs.Where(r => r.SourceId == src.Id)
                .GroupBy(r => r.Date.Date)
                .Select(g => (Date: g.Key, Litres: g.Sum(x => x.Litres),
                              Rate: g.Max(x => ObservedLpm(x))))
                .OrderBy(d => d.Date).ToList();

            foreach (var d in daily)
            {
                if (src.MaxLitresPerDay is { } lpd && d.Litres > lpd)
                    res.Add(new(d.Date, src.Name, "Max litres/day", d.Litres, lpd));
                if (src.MaxLitresPerMinute is { } lpm && d.Rate is { } r && r > lpm)
                    res.Add(new(d.Date, src.Name, "Max litres/min", r, lpm));
                if (p.Permit.Expires is { } exp && d.Litres > 0 && d.Date > exp.Date)
                    res.Add(new(d.Date, src.Name, "Taking after permit expiry", d.Litres, 0));
            }
            if (src.MaxDaysPerYear is { } maxDays)
                foreach (var y in daily.Where(d => d.Litres > 0).GroupBy(d => d.Date.Year))
                    if (y.Count() > maxDays)
                        res.Add(new(new DateTime(y.Key, 12, 31), src.Name, "Max days/year", y.Count(), maxDays));
        }
        // takings recorded against an unknown source
        var known = p.Permit.Sources.Select(s => s.Id).ToHashSet();
        foreach (var r in recs.Where(r => !known.Contains(r.SourceId)))
            res.Add(new(r.Date, "(unpermitted source)", "Taking not under any permit source", r.Litres, 0));
        return res.OrderBy(e => e.Date).ToList();
    }

    static double? ObservedLpm(TakingRecord r) =>
        r.PeakLpm ?? (r.PumpingMinutes is > 0 ? r.Litres / r.PumpingMinutes : null);

    public static List<SourceSummary> Summarize(SiteProject p, int? year = null)
    {
        var recs = p.Takings.Where(t => year == null || t.Date.Year == year).ToList();
        return p.Permit.Sources.Select(s =>
        {
            var r = recs.Where(x => x.SourceId == s.Id).ToList();
            var days = r.GroupBy(x => x.Date.Date).Select(g => g.Sum(x => x.Litres)).ToList();
            return new SourceSummary(s.Name, r.Sum(x => x.Litres), days.Count(d => d > 0),
                days.DefaultIfEmpty(0).Max(), r.Select(ObservedLpm).Where(v => v != null).Max(),
                s.MaxLitresPerDay, s.MaxLitresPerMinute, s.MaxDaysPerYear);
        }).ToList();
    }
}
