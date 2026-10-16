using WaterMgmt.Core;
using Xunit;

public class CoreTests
{
    static SiteProject Site()
    {
        var p = new SiteProject();
        p.Permit.Expires = new DateTime(2026, 12, 31);
        p.Permit.Sources.Add(new PermitSource { Name = "Pit 1 Pump", MaxLitresPerDay = 1000, MaxLitresPerMinute = 100, MaxDaysPerYear = 2 });
        return p;
    }

    [Fact]
    public void Import_CsvWithMessyHeaderAndUnits_FlagsExceedances()
    {
        var p = Site();
        var csv = "Quarry daily log\n\nDate,Pump,Volume (m3),Run minutes\n2026-05-01,pit 1 pump,0.5,10\n2026-05-02,Pit 1 Pump,2,10\n2026-05-03,Pit 1 Pump,0.1,5\nbad,Pit 1 Pump,1,1\n2026-05-04,Nowhere,1,1\n";
        var rows = SpreadsheetImporter.ReadCsv(csv);
        int h = SpreadsheetImporter.GuessHeaderRow(rows);
        Assert.Equal(1, h);
        var m = SpreadsheetImporter.GuessMapping(rows[h]);
        Assert.Equal(VolumeUnit.CubicMetres, m.Unit);
        var r = SpreadsheetImporter.Apply(p, rows, h, m);
        Assert.Equal(3, r.Imported);
        Assert.Equal(2, r.Warnings.Count);
        var ex = ComplianceChecker.Check(p);
        Assert.Contains(ex, e => e.Rule == "Max litres/day" && e.Actual == 2000);      // 2 m3 > 1000 L
        Assert.Contains(ex, e => e.Rule == "Max litres/min" && e.Actual == 200);       // 2000 L / 10 min
        Assert.Contains(ex, e => e.Rule == "Max days/year");                           // 3 days > 2
    }

    [Fact]
    public void ExpiredPermit_And_UnknownSource_Flagged()
    {
        var p = Site();
        p.Takings.Add(new TakingRecord { Date = new DateTime(2027, 1, 5), SourceId = p.Permit.Sources[0].Id, Litres = 10 });
        p.Takings.Add(new TakingRecord { Date = new DateTime(2026, 1, 5), SourceId = Guid.NewGuid(), Litres = 10 });
        var ex = ComplianceChecker.Check(p);
        Assert.Contains(ex, e => e.Rule.StartsWith("Taking after permit expiry"));
        Assert.Contains(ex, e => e.Rule.StartsWith("Taking not under"));
    }

    [Fact]
    public void SaveLoad_RoundTrips_And_ReportRenders()
    {
        var p = Site();
        p.Features.Add(new SiteFeature { Name = "Pit 1", Kind = FeatureKind.Pit, Dewatered = true });
        var f = Path.GetTempFileName();
        ProjectStorage.Save(p, f);
        var q = ProjectStorage.Load(f);
        Assert.Equal(FeatureKind.Pit, q.Features[0].Kind);
        Assert.Contains("Pit 1 Pump", ReportBuilder.Html(q, null));
    }
}
