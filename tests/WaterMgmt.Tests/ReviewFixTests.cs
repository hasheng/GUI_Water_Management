using ClosedXML.Excel;
using WaterMgmt.Core;
using Xunit;

public class ReviewFixTests
{
    static SiteProject Site(int? maxDays = null, int startMonth = 1)
    {
        var p = new SiteProject();
        p.Permits[0].PermitYearStartMonth = startMonth;
        p.Permits[0].Sources.Add(new PermitSource { Name = "P", MaxDaysPerYear = maxDays });
        return p;
    }

    [Fact]
    public void ThreeDigitDecimalComma_NeverSilentlyThousandsFold()
    {
        // 0,125 can only be a decimal; no warning needed
        var p = Site();
        var rows = SpreadsheetImporter.ReadCsv("Date,Pump,Volume (m3)\n2026-05-01,P,\"0,125\"\n");
        var r = SpreadsheetImporter.Apply(p, rows, 0, SpreadsheetImporter.GuessMapping(rows[0]));
        Assert.Equal(125, p.Takings[0].Litres, 5);   // 0.125 m3 -> 125 L
        Assert.Empty(r.Warnings);

        // 1,234 alone is genuinely ambiguous: imported as 1234 but flagged
        p = Site();
        rows = SpreadsheetImporter.ReadCsv("Date,Pump,Volume (L)\n2026-05-01,P,\"1,234\"\n");
        r = SpreadsheetImporter.Apply(p, rows, 0, SpreadsheetImporter.GuessMapping(rows[0]));
        Assert.Contains(r.Warnings, w => w.Contains("ambiguous"));

        // ...but when the same column shows decimal commas elsewhere, 1,234 is read as 1.234
        p = Site();
        rows = SpreadsheetImporter.ReadCsv("Date,Pump,Volume (m3)\n2026-05-01,P,\"1,234\"\n2026-05-02,P,\"2,5\"\n");
        r = SpreadsheetImporter.Apply(p, rows, 0, SpreadsheetImporter.GuessMapping(rows[0]));
        Assert.Equal(1234, p.Takings[0].Litres, 5);  // 1.234 m3 = 1234 L
        Assert.Equal(2500, p.Takings[1].Litres, 5);
        Assert.DoesNotContain(r.Warnings, w => w.Contains("ambiguous"));
    }

    [Fact]
    public void Xlsx_NumbersAreReadAsValuesNotDisplayText()
    {
        var f = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet();
            ws.Cell(1, 1).Value = "Vol"; ws.Cell(2, 1).Value = 0.4; ws.Cell(2, 1).Style.NumberFormat.Format = "0";
            ws.Cell(3, 1).Value = 2.5; ws.Cell(3, 1).Style.NumberFormat.Format = "#,##0";
            wb.SaveAs(f);
        }
        var rows = SpreadsheetImporter.ReadRows(f);
        Assert.Equal("0.4", rows[1][0]);
        Assert.Equal("2.5", rows[2][0]);
    }

    [Fact]
    public void NewerFileFormat_IsRefusedNotDowngraded()
    {
        var f = Path.GetTempFileName();
        File.WriteAllText(f, "{\"SchemaVersion\":99,\"SiteName\":\"S\"}");
        Assert.Throws<InvalidDataException>(() => ProjectStorage.Load(f));
    }

    [Fact]
    public void ImportResult_TracksExactRowsForUndo()
    {
        var p = Site();
        var manual = new TakingRecord { Date = new DateTime(2026, 1, 1), SourceId = p.Permits[0].Sources[0].Id, Litres = 5 };
        p.Takings.Add(manual);
        var rows = SpreadsheetImporter.ReadCsv("Date,Pump,Volume (L)\n2026-05-01,P,10\n2026-05-02,New,20\n");
        var m = SpreadsheetImporter.GuessMapping(rows[0]); m.CreateMissingSources = true;
        var r = SpreadsheetImporter.Apply(p, rows, 0, m);
        p.Takings.Insert(0, new TakingRecord { Date = new DateTime(2026, 2, 2), SourceId = p.Permits[0].Sources[0].Id, Litres = 7 }); // user edit after import
        Assert.Equal(2, r.AddedTakings.Count);
        Assert.Single(r.AddedSources);
        var added = r.AddedTakings.ToHashSet();
        p.Takings.RemoveAll(added.Contains);
        Assert.Equal(2, p.Takings.Count);            // manual + post-import edit both survive
        Assert.Contains(manual, p.Takings);
    }

    [Fact]
    public void MaxDaysPerYear_PermitYearSpanningCalendarYears_IsCaughtWithYearFilter()
    {
        var p = Site(maxDays: 3, startMonth: 4);     // permit year Apr–Mar
        var id = p.Permits[0].Sources[0].Id;
        foreach (var d in new[] { "2026-12-01", "2026-12-02", "2027-02-01", "2027-02-02" })
            p.Takings.Add(new TakingRecord { Date = DateTime.Parse(d), SourceId = id, Litres = 1 });
        Assert.Single(ComplianceChecker.Check(p), e => e.Rule == "Max days/year");
        Assert.Single(ComplianceChecker.Check(p, 2026), e => e.Rule == "Max days/year");
        Assert.Single(ComplianceChecker.Check(p, 2027), e => e.Rule == "Max days/year");
        Assert.Empty(ComplianceChecker.Check(p, 2028));
    }
}
