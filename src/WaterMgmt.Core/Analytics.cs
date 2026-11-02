namespace WaterMgmt.Core;

/// <summary>One source's monthly breakdown for a reporting year. Index 0 = January.</summary>
public record MonthlyUsage(
    Guid SourceId,
    string Source,
    double[] Volume,      // litres taken per month
    double[] MaxRate,     // max observed L/min per month (0 = unknown/no rate data)
    double Total,         // annual total litres
    int DaysTaken,        // distinct days with any record
    int MissingDays);     // calendar days with NO record between first and last record

public static class Analytics
{
    /// <summary>
    /// Monthly volume/rate matrix per source for a calendar year.
    /// MissingDays counts calendar days with no record at all between the first and
    /// last recorded day — a gap in the log is not the same as a day at zero, and
    /// unlogged stretches must be chased with the client before the annual report.
    /// </summary>
    public static List<MonthlyUsage> Monthly(SiteProject p, int year)
    {
        var res = new List<MonthlyUsage>();
        foreach (var src in p.Permits.SelectMany(pt => pt.Sources))
        {
            var recs = p.Takings.Where(t => t.SourceId == src.Id && t.Date.Year == year).ToList();
            var vol = new double[12];
            var rate = new double[12];
            foreach (var g in recs.GroupBy(t => t.Date.Month))
            {
                vol[g.Key - 1] = g.Sum(t => t.Litres);
                rate[g.Key - 1] = g.Select(ComplianceChecker.ObservedLpm)
                    .OfType<double>().DefaultIfEmpty(0).Max();
            }
            var days = recs.Select(t => t.Date.Date).Distinct().OrderBy(d => d).ToList();
            int missing = 0;
            if (days.Count > 1)
            {
                var set = days.ToHashSet();
                for (var d = days[0]; d <= days[^1]; d = d.AddDays(1))
                    if (!set.Contains(d)) missing++;
            }
            res.Add(new MonthlyUsage(src.Id, src.Name, vol, rate, vol.Sum(), days.Count, missing));
        }
        return res;
    }
}
