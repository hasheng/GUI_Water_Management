using WaterMgmt.Core;
using Xunit;

public class CoreTests
{
    static SiteProject Site()
    {
        var p = new SiteProject();
        p.Permits[0].Expires = new DateTime(2026, 12, 31);
        p.Permits[0].Sources.Add(new PermitSource { Name = "Pit 1 Pump", MaxLitresPerDay = 1000, MaxLitresPerMinute = 100, MaxDaysPerYear = 2 });
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
        // bad date + unknown source + implied rate 200>100 + day total 2000>1000
        Assert.Equal(4, r.Warnings.Count);
        var ex = ComplianceChecker.Check(p);
        Assert.Contains(ex, e => e.Rule == "Max litres/day" && e.Actual == 2000);      // 2 m3 > 1000 L
        Assert.Contains(ex, e => e.Rule == "Max litres/min" && e.Actual == 200);       // 2000 L / 10 min
        Assert.Contains(ex, e => e.Rule == "Max days/year");                           // 3 days > 2
    }

    [Fact]
    public void ExpiredPermit_And_UnknownSource_Flagged()
    {
        var p = Site();
        p.Takings.Add(new TakingRecord { Date = new DateTime(2027, 1, 5), SourceId = p.Permits[0].Sources[0].Id, Litres = 10 });
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

    [Fact]
    public void Import_HoursColumn_ConvertedToMinutes()
    {
        var p = Site();
        var csv = "Date,Pump,Volume (L),Hours\n2026-05-01,Pit 1 Pump,2000,2\n";
        var rows = SpreadsheetImporter.ReadCsv(csv);
        var m = SpreadsheetImporter.GuessMapping(rows[0]);
        Assert.True(m.MinutesAreHours);
        var r = SpreadsheetImporter.Apply(p, rows, 0, m);
        Assert.Equal(1, r.Imported);
        Assert.Equal(120, p.Takings[0].PumpingMinutes);            // 2 h = 120 min
        // observed rate = 2000 L / 120 min = 16.7 L/min -> under the 100 limit
        Assert.DoesNotContain(ComplianceChecker.Check(p), e => e.Rule == "Max litres/min");
    }

    [Fact]
    public void Import_FrenchAndMixedDecimalFormats_Parse()
    {
        // no permit limits on this source, so limit sanity warnings stay out of the way
        var p = new SiteProject();
        p.Permits[0].Sources.Add(new PermitSource { Name = "Pit 1 Pump" });
        var csv = "Date,Pump,Volume (L)\n" +
                  "2026-05-01,Pit 1 Pump,\"1 234,5\"\n" +          // fr-CA nbsp thousands + decimal comma
                  "2026-05-02,Pit 1 Pump,\"1,234.56\"\n" +         // en-US thousands + decimal point
                  "2026-05-03,Pit 1 Pump,\"0,5\"\n" +              // bare decimal comma (was 10x error before)
                  "2026-05-04,Pit 1 Pump,\"1.234,56\"\n";          // European mixed (fr Excel export)
        var rows = SpreadsheetImporter.ReadCsv(csv);
        var r = SpreadsheetImporter.Apply(p, rows, 0, SpreadsheetImporter.GuessMapping(rows[0]));
        Assert.Equal(4, r.Imported);
        Assert.Empty(r.Warnings);
        Assert.Equal(1234.5, p.Takings[0].Litres, 5);
        Assert.Equal(1234.56, p.Takings[1].Litres, 5);
        Assert.Equal(0.5, p.Takings[2].Litres, 5);
        Assert.Equal(1234.56, p.Takings[3].Litres, 5);
    }

    [Fact]
    public void Import_FrenchDecimalHours_ConvertedToMinutes()
    {
        var p = Site();
        var csv = "Date,Pump,Volume (L),Heures\n2026-05-01,Pit 1 Pump,\"2 000\",\"1,5\"\n";
        var rows = SpreadsheetImporter.ReadCsv(csv);
        var m = SpreadsheetImporter.GuessMapping(rows[0]);
        Assert.True(m.MinutesAreHours);
        SpreadsheetImporter.Apply(p, rows, 0, m);
        Assert.Equal(90, p.Takings[0].PumpingMinutes);             // 1,5 h = 90 min
        Assert.Equal(2000, p.Takings[0].Litres);
    }

    [Fact]
    public void Import_SanityChecks_WarnOnImplausibleData()
    {
        var p = Site();                                    // max 100 L/min, 1000 L/day
        var csv = "Date,Pump,Volume (L),Minutes\n" +
                  "2026-05-01,Pit 1 Pump,6000,30\n" +      // implied 200 L/min > 100
                  "2026-05-02,Pit 1 Pump,500,1500\n" +     // runtime > 24 h: unit slip?
                  "2026-05-03,Pit 1 Pump,400,30\n" +
                  "2026-05-03,Pit 1 Pump,400,30\n";        // day total 800 ok; with 05-01 6000 -> day total warning
        var rows = SpreadsheetImporter.ReadCsv(csv);
        var r = SpreadsheetImporter.Apply(p, rows, 0, SpreadsheetImporter.GuessMapping(rows[0]));
        Assert.Equal(4, r.Imported);
        Assert.Contains(r.Warnings, w => w.Contains("implied rate 200 L/min"));
        Assert.Contains(r.Warnings, w => w.Contains("exceeds 24 h"));
        Assert.Contains(r.Warnings, w => w.Contains("2026-05-01 'Pit 1 Pump': day total 6,000 L exceeds"));
    }

    [Fact]
    public void Import_ManuallyMappedHoursColumn_StillConverted()
    {
        var p = Site();
        var csv = "Date,Pump,Volume (L),Run hrs\n2026-05-01,Pit 1 Pump,2000,2\n";
        var rows = SpreadsheetImporter.ReadCsv(csv);
        // Mapped by hand in the UI dialog: MinutesAreHours was never set by GuessMapping,
        // so Apply must detect it from the header text itself.
        var m = new ImportMapping { DateCol = 0, SourceCol = 1, VolumeCol = 2, MinutesCol = 3 };
        var r = SpreadsheetImporter.Apply(p, rows, 0, m);
        Assert.Equal(1, r.Imported);
        Assert.Equal(120, p.Takings[0].PumpingMinutes);            // 2 h = 120 min, not 2 min
        Assert.Contains(r.Warnings, w => w.Contains("converted"));
    }

    [Fact]
    public void Load_FileWithoutSchemaVersion_MigratesToV1()
    {
        var p = Site();
        p.Features.Add(new SiteFeature { Name = "Pit 1", Kind = FeatureKind.Pit });
        var f = Path.GetTempFileName();
        ProjectStorage.Save(p, f);
        Assert.Contains("SchemaVersion", File.ReadAllText(f));     // stamped on save
        // simulate a legacy v0 file: rename the property so it deserializes as absent
        File.WriteAllText(f, File.ReadAllText(f).Replace("SchemaVersion", "OldSchemaVersion"));
        var q = ProjectStorage.Load(f);
        Assert.Equal(SiteProject.CurrentSchemaVersion, q.SchemaVersion);   // v0 file migrated
        Assert.Equal("Pit 1", q.Features[0].Name);                 // data intact
        // round trip: the migrated project saves as the current version
        ProjectStorage.Save(q, f);
        Assert.Equal(SiteProject.CurrentSchemaVersion, ProjectStorage.Load(f).SchemaVersion);
    }
}

public class MassBalanceTests
{
    [Fact]
    public void Balances_Storage_Pump_Checks()
    {
        var p = new SiteProject();
        var src = new PermitSource { Name = "P1", MaxLitresPerMinute = 400 }; p.Permits[0].Sources.Add(src);
        var pit = new SiteFeature { Name = "Pit", Kind = FeatureKind.Pit, OtherInflowLpm = 300 };
        var pump = new SiteFeature { Name = "Pump", Kind = FeatureKind.Pump, RatedLpm = 600, PermitSourceId = src.Id };
        var pond = new SiteFeature { Name = "Pond", Kind = FeatureKind.StormPond };
        p.Features.AddRange(new[] { pit, pump, pond });
        p.Links.Add(new FlowLink { FromId = pit.Id, ToId = pump.Id, FlowLpm = 500 });
        p.Links.Add(new FlowLink { FromId = pump.Id, ToId = pond.Id, FlowLpm = 500 });
        var r = MassBalance.Compute(p).ToDictionary(n => n.Feature.Name);
        Assert.Equal(BalanceStatus.StorageChange, r["Pit"].Status);   // 300 in, 500 out -> draining
        Assert.Equal(-200, r["Pit"].Net);
        Assert.Equal(BalanceStatus.OverPermit, r["Pump"].Status);     // 500 > 400
        Assert.Equal(BalanceStatus.StorageChange, r["Pond"].Status);
        p.Links[1].FlowLpm = 450; p.Permits[0].Sources[0].MaxLitresPerMinute = null;
        Assert.Equal(BalanceStatus.Imbalance, MassBalance.Compute(p).First(n => n.Feature.Name == "Pump").Status);
        p.Links[1].FlowLpm = null;
        Assert.Equal(BalanceStatus.Incomplete, MassBalance.Compute(p).First(n => n.Feature.Name == "Pump").Status);
    }
}

public class AnalyticsTests
{
    static SiteProject SiteWithTakings()
    {
        var p = new SiteProject();
        var src = new PermitSource { Name = "Well A" }; p.Permits[0].Sources.Add(src);
        p.Takings.AddRange(new[]
        {
            new TakingRecord { Date = new DateTime(2026, 1, 1), SourceId = src.Id, Litres = 500, PumpingMinutes = 50 },
            new TakingRecord { Date = new DateTime(2026, 1, 15), SourceId = src.Id, Litres = 700, PumpingMinutes = 60 },
            new TakingRecord { Date = new DateTime(2026, 2, 2), SourceId = src.Id, Litres = 300 },
            new TakingRecord { Date = new DateTime(2025, 6, 1), SourceId = src.Id, Litres = 9999 } // other year
        });
        return p;
    }

    [Fact]
    public void Monthly_SumsPerMonth_FiltersYear()
    {
        var p = SiteWithTakings();
        var m = Analytics.Monthly(p, 2026).Single();
        Assert.Equal(1200, m.Volume[0], 5);            // Jan: 500 + 700
        Assert.Equal(300, m.Volume[1], 5);             // Feb
        Assert.Equal(0, m.Volume[5], 5);
        Assert.Equal(1500, m.Total, 5);                // 2025 record excluded
        Assert.Equal(3, m.DaysTaken);
        Assert.Equal(700.0 / 60, m.MaxRate[0], 5);     // Jan max: 700/60 = 11.67 L/min beats 500/50
        Assert.Equal(0, m.MaxRate[1], 5);              // Feb has no runtime data
    }

    [Fact]
    public void MissingDays_CountsGaps_ButNotLoggedZeroes()
    {
        var p = SiteWithTakings();
        var src = p.Permits[0].Sources[0];
        // Jan 1, Jan 15 and Feb 2 recorded -> all days between with no record are gaps:
        // Jan 2-14 (13) + Jan 16-31 (16) + Feb 1 (1) = 30
        Assert.Equal(30, Analytics.Monthly(p, 2026).Single().MissingDays);
        // a logged zero-volume day is a real record, not a gap
        p.Takings.Add(new TakingRecord { Date = new DateTime(2026, 1, 8), SourceId = src.Id, Litres = 0 });
        Assert.Equal(29, Analytics.Monthly(p, 2026).Single().MissingDays);
        // single recorded day -> no gap can be inferred
        var q = new SiteProject(); q.Permits[0].Sources.Add(new PermitSource { Name = "W" });
        q.Takings.Add(new TakingRecord { Date = new DateTime(2026, 3, 1), SourceId = q.Permits[0].Sources[0].Id, Litres = 1 });
        Assert.Equal(0, Analytics.Monthly(q, 2026).Single().MissingDays);
    }

    [Fact]
    public void HtmlReport_IncludesMonthlyMatrix_AndGapWarning()
    {
        var p = SiteWithTakings();
        var html = ReportBuilder.Html(p, 2026);
        Assert.Contains("Monthly volumes", html);
        Assert.Contains("Gap days", html);
        Assert.Contains("log is incomplete", html);    // gap note shows when gaps exist
    }
}

public class MecpReportTests
{
    static SiteProject SiteForMecp()
    {
        var p = new SiteProject { SiteName = "Test Quarry" };
        p.Permits[0].Number = "1234-ABCQP7";
        p.Permits[0].Expires = new DateTime(2027, 12, 31);
        var big = new PermitSource { Name = "Pit Pump", MaxLitresPerMinute = 40, MaxLitresPerDay = 60_000, MaxDaysPerYear = 200 };
        var small = new PermitSource { Name = "Wash Water", MaxLitresPerDay = 10_000 };
        p.Permits[0].Sources.AddRange(new[] { big, small });
        p.Takings.Add(new TakingRecord { Date = new DateTime(2026, 3, 10), SourceId = big.Id, Litres = 12_000, PumpingMinutes = 300 });
        p.Takings.Add(new TakingRecord { Date = new DateTime(2026, 3, 11), SourceId = big.Id, Litres = 18_000, PumpingMinutes = 450 });
        p.Takings.Add(new TakingRecord { Date = new DateTime(2026, 4, 2), SourceId = small.Id, Litres = 8_000 });
        return p;
    }

    [Fact]
    public void MecpHtml_MonthlyVolumesInM3_WithLimitColumns()
    {
        var html = ReportBuilder.MecpHtml(SiteForMecp(), 2026);
        Assert.Contains("Annual Water Taking Report", html);
        Assert.Contains("PTTW 1234-ABCQP7", html);
        Assert.Contains("30.0", html);                 // March: 30,000 L -> 30.0 m3
        Assert.Contains("Annual report", html);
        Assert.Contains("March 31", html);             // filing deadline note
    }

    [Fact]
    public void MecpHtml_Flags50kSources()
    {
        var html = ReportBuilder.MecpHtml(SiteForMecp(), 2026);
        // Pit Pump: 60,000 L/day limit -> annual reporting; Wash Water 10,000 -> not
        Assert.Contains("YES", html);
        // only the flagged row is highlighted
        Assert.Contains("class='flag'", html);
    }

    [Fact]
    public void MecpCsv_RowPerSource_WithCorrectTotals()
    {
        var csv = ReportBuilder.MecpCsv(SiteForMecp(), 2026);
        var lines = csv.TrimEnd().Split('\n');
        Assert.Equal(3, lines.Length);                 // header + 2 sources
        // permit, source, 12 months, total, days, maxrate, 3 limits, flag = 21 columns
        Assert.Equal(21, lines[1].Split(',').Length);
        var pit = lines[1].Split(',');
        Assert.Equal("30.0", pit[4]);                  // Mar = col 2 (Permit,Source) + 2 = index 4
        Assert.StartsWith("\"1234-ABCQP7\",\"Pit Pump\"", lines[1]);
        Assert.Contains("YES", lines[1]);
        Assert.Contains("no", lines[2]);
    }
}

public class MultiPermitTests
{
    [Fact]
    public void Load_V1SinglePermitFile_MigratesToPermitsList()
    {
        const string v1 = """
        {
          "SchemaVersion": 1,
          "SiteName": "Legacy Pit",
          "Permit": {
            "Number": "88-1234",
            "Holder": "Old Co",
            "Expires": "2027-12-31T00:00:00",
            "Sources": [ { "Name": "Well 1", "MaxLitresPerMinute": 500 } ]
          },
          "Features": [],
          "Links": [],
          "Takings": []
        }
        """;
        var f = Path.GetTempFileName();
        File.WriteAllText(f, v1);
        var q = ProjectStorage.Load(f);
        Assert.Equal(2, q.SchemaVersion);                       // stamped to current
        Assert.Equal("88-1234", q.Permits.Single().Number);     // moved into the list
        Assert.Equal("Old Co", q.Permits[0].Holder);
        Assert.Equal("Well 1", q.Permits[0].Sources[0].Name);   // data intact
        ProjectStorage.Save(q, f);
        var again = ProjectStorage.Load(f);
        Assert.Equal("88-1234", again.Permits[0].Number);       // round trips as v2
    }

    [Fact]
    public void Sources_AreCheckedAgainstTheirOwnPermit()
    {
        var a = new Permit { Number = "A", Expires = new DateTime(2026, 6, 30) };
        a.Sources.Add(new PermitSource { Name = "Pit A" });
        var b = new Permit { Number = "B", Expires = new DateTime(2027, 6, 30) };
        b.Sources.Add(new PermitSource { Name = "Well B", MaxLitresPerMinute = 100 });
        var p = new SiteProject { Permits = new() { a, b } };
        p.Takings.Add(new TakingRecord { Date = new DateTime(2026, 7, 1), SourceId = a.Sources[0].Id, Litres = 100 }); // after A expiry
        p.Takings.Add(new TakingRecord { Date = new DateTime(2026, 7, 1), SourceId = b.Sources[0].Id, Litres = 100 }); // fine under B
        var ex = ComplianceChecker.Check(p);
        Assert.Contains(ex, e => e.Rule.StartsWith("Taking after permit expiry") && e.Source == "Pit A");
        Assert.DoesNotContain(ex, e => e.Source == "Well B");
        Assert.Equal(2, ComplianceChecker.Summarize(p).Count);      // sources of both permits
    }

    [Fact]
    public void Import_UnknownSource_GoesToPrimaryPermit()
    {
        var p = new SiteProject();
        p.Permits[0].Sources.Add(new PermitSource { Name = "Known" });
        var csv = "Date,Source,Volume (L)\n2026-05-01,Brand New,100\n";
        var rows = SpreadsheetImporter.ReadCsv(csv);
        var m = SpreadsheetImporter.GuessMapping(rows[0]);
        m.CreateMissingSources = true;
        var r = SpreadsheetImporter.Apply(p, rows, 0, m);
        Assert.Equal(1, r.Imported);
        Assert.Contains("Brand New", r.CreatedSources);
        Assert.Contains(p.Permits[0].Sources, s => s.Name == "Brand New");
    }
}

public class PermitYearTests
{
    static PermitSource OneDaySource() => new() { Name = "Pit Pump", MaxDaysPerYear = 1 };

    [Fact]
    public void MaxDays_UsesPermitYearWindow_NotCalendarYear()
    {
        var pt = new Permit { Number = "P", PermitYearStartMonth = 4 };   // Apr 1 - Mar 31
        pt.Sources.Add(OneDaySource());
        var p = new SiteProject { Permits = new() { pt } };
        var id = pt.Sources[0].Id;
        // one taking at the tail of the 2025-26 permit year, one at the start of 2026-27:
        // two days in calendar 2026 (old behaviour would flag), but one per permit year
        p.Takings.Add(new TakingRecord { Date = new DateTime(2026, 3, 30), SourceId = id, Litres = 100 });
        p.Takings.Add(new TakingRecord { Date = new DateTime(2026, 4, 2), SourceId = id, Litres = 100 });
        Assert.DoesNotContain(ComplianceChecker.Check(p, 2026), e => e.Rule == "Max days/year");
        // two takings inside the SAME permit year do exceed
        p.Takings.Add(new TakingRecord { Date = new DateTime(2026, 9, 10), SourceId = id, Litres = 100 });
        var ex = ComplianceChecker.Check(p, 2026).Where(e => e.Rule == "Max days/year").ToList();
        Assert.Single(ex);
        Assert.Equal(new DateTime(2027, 3, 31), ex[0].Date);              // dated at window end
    }

    [Fact]
    public void DefaultJanuary_BehavesLikeCalendarYear()
    {
        var pt = new Permit { Number = "P" };                             // PermitYearStartMonth defaults to 1
        pt.Sources.Add(OneDaySource());
        var p = new SiteProject { Permits = new() { pt } };
        var id = pt.Sources[0].Id;
        p.Takings.Add(new TakingRecord { Date = new DateTime(2026, 3, 30), SourceId = id, Litres = 100 });
        p.Takings.Add(new TakingRecord { Date = new DateTime(2026, 4, 2), SourceId = id, Litres = 100 });
        var ex = ComplianceChecker.Check(p, 2026).Where(e => e.Rule == "Max days/year").ToList();
        Assert.Single(ex);
        Assert.Equal(new DateTime(2026, 12, 31), ex[0].Date);
    }
}
