namespace WaterMgmt.Core;

public record Exceedance(DateTime Date, string Source, string Rule, double Actual, double Limit);

public record SourceSummary(string Source, double TotalLitres, int DaysTaking, double MaxDayLitres,
    double? MaxLpm, double? LimitLpd, double? LimitLpm, int? LimitDays);

/// <summary>An upcoming (or passed) permit deadline, surfaced before it becomes an exceedance.</summary>
public record PermitNotice(string Severity, int Days, string Message);

public static class ComplianceChecker
{
    public static List<Exceedance> Check(SiteProject p, int? year = null)
    {
        var res = new List<Exceedance>();
        var recs = p.Takings.Where(t => year == null || t.Date.Year == year).ToList();
        // each source is checked against the limits of the permit that owns it
        var owner = SourceOwner(p);
        foreach (var (src, ownPt) in owner)
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
                if (ownPt.Expires is { } exp && d.Litres > 0 && d.Date > exp.Date)
                    res.Add(new(d.Date, src.Name, "Taking after permit expiry", d.Litres, 0));
            }
            if (src.MaxDaysPerYear is { } maxDays)
            {
                // Permit years can straddle calendar years, so count over ALL of the source's
                // records and keep a permit year if it overlaps the requested calendar year.
                var allDays = p.Takings.Where(r => r.SourceId == src.Id)
                    .GroupBy(r => r.Date.Date).Where(g => g.Sum(x => x.Litres) > 0).Select(g => g.Key);
                foreach (var y in allDays.GroupBy(d => PermitYearEnd(d, ownPt.PermitYearStartMonth)))
                {
                    var end = y.Key; var start = end.AddYears(-1).AddDays(1);
                    if (year != null && (year < start.Year || year > end.Year)) continue;
                    if (y.Count() > maxDays)
                        res.Add(new(end, src.Name, "Max days/year", y.Count(), maxDays));
                }
            }
        }
        // takings recorded against an unknown source
        var known = owner.Keys.Select(s => s.Id).ToHashSet();
        foreach (var r in recs.Where(r => !known.Contains(r.SourceId)))
            res.Add(new(r.Date, "(unpermitted source)", "Taking not under any permit source", r.Litres, 0));
        return res.OrderBy(e => e.Date).ToList();
    }

    /// <summary>Peak rate actually observed for a record: measured peak if supplied,
    /// otherwise volume / pumping minutes. Null when no runtime data exists.</summary>
    public static double? ObservedLpm(TakingRecord r) =>
        r.PeakLpm ?? (r.PumpingMinutes is > 0 ? r.Litres / r.PumpingMinutes : null);

    /// <summary>Last day of the permit-year window containing d, where a permit year
    /// is the 12 months starting in startMonth. January (the default) is the plain
    /// calendar year. Used to date a Max days/year exceedance at the window's end.</summary>
    public static DateTime PermitYearEnd(DateTime d, int startMonth)
    {
        if (startMonth <= 1) return new DateTime(d.Year, 12, 31);
        var start = new DateTime(d.Month >= startMonth ? d.Year : d.Year - 1, startMonth, 1);
        return start.AddYears(1).AddDays(-1);
    }

    /// <summary>Map of every permitted source to the permit it is taken under.</summary>
    internal static Dictionary<PermitSource, Permit> SourceOwner(SiteProject p)
    {
        var owner = new Dictionary<PermitSource, Permit>();
        foreach (var pt in p.Permits)
            foreach (var s in pt.Sources)
                owner[s] = pt;
        return owner;
    }

    /// <summary>
    /// Deadline warnings for every permit on the site: expired, critical (30 days or
    /// less), warning (90 days or less). Renewals take months, so this is surfaced
    /// well before any taking-after-expiry exceedance can happen.
    /// </summary>
    public static List<PermitNotice> GetPermitNotices(SiteProject p, DateTime? asOf = null)
    {
        var res = new List<PermitNotice>();
        var today = (asOf ?? DateTime.Today).Date;
        foreach (var pt in p.Permits)
        {
            if (pt.Expires is not { } exp) continue;
            var days = (int)(exp.Date - today).TotalDays;
            if (days < 0)
                res.Add(new PermitNotice("expired", days,
                    $"Permit {pt.Number} EXPIRED {-days:N0} days ago ({exp.Date:yyyy-MM-dd})."));
            else if (days <= 30)
                res.Add(new PermitNotice("critical", days,
                    $"Permit {pt.Number} expires in {days} days ({exp.Date:yyyy-MM-dd})."));
            else if (days <= 90)
                res.Add(new PermitNotice("warning", days,
                    $"Permit {pt.Number} expires in {days} days ({exp.Date:yyyy-MM-dd})."));
        }
        return res;
    }

    public static List<SourceSummary> Summarize(SiteProject p, int? year = null)
    {
        var recs = p.Takings.Where(t => year == null || t.Date.Year == year).ToList();
        return p.Permits.SelectMany(pt => pt.Sources).Select(s =>
        {
            var r = recs.Where(x => x.SourceId == s.Id).ToList();
            var days = r.GroupBy(x => x.Date.Date).Select(g => g.Sum(x => x.Litres)).ToList();
            return new SourceSummary(s.Name, r.Sum(x => x.Litres), days.Count(d => d > 0),
                days.DefaultIfEmpty(0).Max(), r.Select(ObservedLpm).Where(v => v != null).Max(),
                s.MaxLitresPerDay, s.MaxLitresPerMinute, s.MaxDaysPerYear);
        }).ToList();
    }
}
