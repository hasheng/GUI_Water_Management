using WaterMgmt.Core;

// water — headless access to the quarry water management core.
//
//   water new <project.wsite>
//       Create an empty project with one permit and one source.
//   water check <project.wsite> [year]
//       Print permit notices and compliance exceedances.
//       Exit code 2 when exceedances exist (CI/cron friendly).
//   water report <project.wsite> [--year N] [--out file.html]
//       Full HTML report to stdout or a file.
//   water mecp <project.wsite> [--year N] [--csv] [--out file]
//       MECP-style annual report (CSV with --csv).
//   water import <sheet> <project.wsite> [--header N] [--create-sources] [--dry-run]
//       Import a spreadsheet using automatic column detection, then save.
//       --dry-run reports what would happen without touching the project.

return Run(args);

static int Run(string[] args)
{
    try
    {
        if (args.Length == 0) return Usage();
        return args[0].ToLowerInvariant() switch
        {
            "new" => CmdNew(args[1..]),
            "check" => CmdCheck(args[1..]),
            "report" => CmdReport(args[1..], false),
            "mecp" => CmdReport(args[1..], true),
            "import" => CmdImport(args[1..]),
            _ => Usage()
        };
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("error: " + ex.Message);
        return 1;
    }
}

static int CmdNew(string[] a)
{
    if (a.Length < 1) return Usage();
    var p = new SiteProject { SiteName = Path.GetFileNameWithoutExtension(a[0]) };
    var pt = p.PrimaryPermit;
    pt.Number = "NEW-0000";
    pt.Sources.Add(new PermitSource { Name = "Pit pump" });
    ProjectStorage.Save(p, a[0]);
    Console.WriteLine("created " + a[0]);
    return 0;
}

static int CmdCheck(string[] a)
{
    if (a.Length < 1) return Usage();
    var p = ProjectStorage.Load(a[0]);
    int? year = a.Length > 1 && int.TryParse(a[1], out var y) ? y : null;
    foreach (var n in ComplianceChecker.GetPermitNotices(p))
        Console.WriteLine($"[{n.Severity.ToUpperInvariant()}] {n.Message}");
    var ex = ComplianceChecker.Check(p, year);
    foreach (var e in ex)
        Console.WriteLine($"[{e.Date:yyyy-MM-dd}] {e.Source}: {e.Rule} — {e.Actual:N0} vs limit {e.Limit:N0}");
    Console.WriteLine(ex.Count == 0 ? "no exceedances" : $"{ex.Count} exceedance(s)");
    return ex.Count == 0 ? 0 : 2;
}

static int CmdReport(string[] a, bool mecp)
{
    if (a.Length < 1) return Usage();
    var p = ProjectStorage.Load(a[0]);
    int? year = OptValue(a, "--year") is { } s && int.TryParse(s, out var y) ? y : null;
    var outPath = OptValue(a, "--out");
    string content;
    if (mecp && a.Contains("--csv")) content = ReportBuilder.MecpCsv(p, year);
    else if (mecp) content = ReportBuilder.MecpHtml(p, year);
    else content = ReportBuilder.Html(p, year);
    if (outPath != null)
    {
        File.WriteAllText(outPath, content);
        Console.WriteLine("wrote " + outPath);
    }
    else Console.WriteLine(content);
    return 0;
}

static int CmdImport(string[] a)
{
    if (a.Length < 2) return Usage();
    string sheet = a[0], projPath = a[1];
    int header = OptValue(a, "--header") is { } h && int.TryParse(h, out var n) ? Math.Max(0, n - 1) : -1;
    var p = ProjectStorage.Load(projPath);
    var rows = SpreadsheetImporter.ReadRows(sheet);
    if (header < 0) header = SpreadsheetImporter.GuessHeaderRow(rows);
    var m = SpreadsheetImporter.GuessMapping(rows[header]);
    m.CreateMissingSources = a.Contains("--create-sources");
    var r = SpreadsheetImporter.Apply(p, rows, header, m);
    Console.WriteLine($"mapping: date={m.DateCol} source={m.SourceCol} volume={m.VolumeCol} " +
                      $"minutes={m.MinutesCol} ({(m.MinutesAreHours ? "hours" : "minutes")}) unit={m.Unit}");
    foreach (var w in r.Warnings) Console.WriteLine("warn: " + w);
    Console.WriteLine($"imported {r.Imported} rows; {r.CreatedSources.Count} source(s) created");
    if (a.Contains("--dry-run"))
    {
        Console.WriteLine("dry run — project not modified");
        return 0;
    }
    ProjectStorage.Save(p, projPath);
    Console.WriteLine("saved " + projPath);
    return 0;
}

static string? OptValue(string[] a, string name)
{
    var i = Array.IndexOf(a, name);
    return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
}

static int Usage()
{
    Console.WriteLine("""
        usage: water <command> ...

          water new <project.wsite>
          water check <project.wsite> [year]                exit 2 if exceedances
          water report <project.wsite> [--year N] [--out f.html]
          water mecp <project.wsite> [--year N] [--csv] [--out f]
          water import <sheet> <project.wsite> [--header N] [--create-sources] [--dry-run]
        """);
    return 1;
}
