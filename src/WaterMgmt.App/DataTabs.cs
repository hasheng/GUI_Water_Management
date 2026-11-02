using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using WaterMgmt.Core;

namespace WaterMgmt.App;

public partial class MainWindow
{
    record TakingRow(TakingRecord Rec, string Source)
    {
        public string Date => Rec.Date.ToString("yyyy-MM-dd");
        public string Litres => Rec.Litres.ToString("N0");
        public string Minutes => Rec.PumpingMinutes?.ToString("0.#") ?? "";
    }

    static DataGridTextColumn Col(string header, string path, bool ro = false, double? width = null)
    {
        var c = new DataGridTextColumn { Header = header, Binding = new Binding(path), IsReadOnly = ro };
        if (width != null) c.Width = new DataGridLength(width.Value);
        return c;
    }

    // ---------- Features ----------
    readonly DataGrid featGrid = new() { AutoGenerateColumns = false, CanUserSortColumns = true };
    readonly TextBlock featSummary = new() { Margin = new Thickness(8), FontSize = 14 };

    Control BuildFeaturesTab()
    {
        featGrid.Columns.Add(Col("Name", "Name"));
        featGrid.Columns.Add(Col("Type", "Kind", true));
        featGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "Dewatered", Binding = new Binding("Dewatered") });
        featGrid.Columns.Add(Col("Rated L/min", "RatedLpm"));
        featGrid.Columns.Add(Col("Notes", "Notes", false, 300));
        var dock = new DockPanel();
        DockPanel.SetDock(featSummary, Dock.Top); dock.Children.Add(featSummary); dock.Children.Add(featGrid);
        return dock;
    }

    void RefreshFeatures()
    {
        featGrid.ItemsSource = null; featGrid.ItemsSource = project.Features;
        int C(FeatureKind k) => project.Features.Count(f => f.Kind == k);
        var cap = project.Features.Where(f => f.Kind == FeatureKind.Pump).Sum(f => f.RatedLpm ?? 0);
        featSummary.Text = $"{C(FeatureKind.Pit)} pits ({project.Features.Count(f => f.Dewatered)} dewatered) · {C(FeatureKind.StormPond)} storm ponds · " +
            $"{C(FeatureKind.Sump)} sumps · {C(FeatureKind.Well)} wells · {C(FeatureKind.WashStation)} wash stations · {C(FeatureKind.Pump)} pumps ({cap:N0} L/min rated) · {project.Links.Count} pipes";
    }

    // ---------- Permit & takings ----------
    readonly TextBox siteName = new() { Width = 220 }, permitNo = new() { Width = 160 }, holder = new() { Width = 220 }, yearStart = new() { Width = 40, Watermark = "1-12" };
    readonly DatePicker issued = new(), expires = new();
    readonly ComboBox permitBox = new() { MinWidth = 160 };
    readonly DataGrid srcGrid = new() { AutoGenerateColumns = false, Height = 180 };
    readonly DataGrid takeGrid = new() { AutoGenerateColumns = false, IsReadOnly = true };
    readonly ComboBox takeSrc = new() { MinWidth = 180 };
    readonly DatePicker takeDate = new();
    readonly TextBox takeL = new() { Width = 100, Watermark = "Litres" }, takeMin = new() { Width = 90, Watermark = "Minutes" };
    bool loadingPermit;
    List<PermitSource> allSources = new();          // combined across permits; index maps takeSrc

    Permit SelectedPermit => permitBox.SelectedItem as Permit ?? project.PrimaryPermit;

    Control BuildPermitTab()
    {
        siteName.LostFocus += (_, _) => SavePermitFields(); permitNo.LostFocus += (_, _) => SavePermitFields(); holder.LostFocus += (_, _) => SavePermitFields(); yearStart.LostFocus += (_, _) => SavePermitFields();
        issued.SelectedDateChanged += (_, _) => SavePermitFields(); expires.SelectedDateChanged += (_, _) => SavePermitFields();
        permitBox.SelectionChanged += (_, _) => RefreshPermit();
        srcGrid.Columns.Add(Col("Source (pump / well / pit)", "Name", false, 220));
        srcGrid.Columns.Add(Col("Max L/min", "MaxLitresPerMinute"));
        srcGrid.Columns.Add(Col("Max L/day", "MaxLitresPerDay"));
        srcGrid.Columns.Add(Col("Max days/year", "MaxDaysPerYear"));
        takeGrid.Columns.Add(Col("Date", "Date")); takeGrid.Columns.Add(Col("Source", "Source", false, 220));
        takeGrid.Columns.Add(Col("Litres", "Litres")); takeGrid.Columns.Add(Col("Minutes", "Minutes"));
        var p = new StackPanel { Margin = new Thickness(10), Spacing = 4 };
        p.Children.Add(Row(Lbl("Site"), siteName, Lbl("PTTW #"), permitNo, Lbl("Holder"), holder));
        p.Children.Add(Row(Lbl("Permit"), permitBox, Btn("Add permit", AddPermit), Btn("Remove this permit", RemovePermit)));
        p.Children.Add(Row(Lbl("Issued"), issued, Lbl("Expires"), expires, Lbl("Permit yr starts (month)"), yearStart));
        p.Children.Add(new TextBlock { Text = "Permitted sources and limits for the selected PTTW", FontWeight = FontWeight.Bold, Margin = new Thickness(0, 8, 0, 0) });
        p.Children.Add(srcGrid);
        p.Children.Add(Row(Btn("Add source", () => { SelectedPermit.Sources.Add(new PermitSource { Name = "New source" }); RefreshPermit(); }),
            Btn("Remove selected source", () => { if (srcGrid.SelectedItem is PermitSource s) { SelectedPermit.Sources.Remove(s); RefreshPermit(); } })));
        p.Children.Add(new TextBlock { Text = "Daily takings", FontWeight = FontWeight.Bold, Margin = new Thickness(0, 8, 0, 0) });
        p.Children.Add(Row(takeDate, takeSrc, takeL, takeMin, Btn("Add taking", AddTaking),
            Btn("Delete selected", () => { if (takeGrid.SelectedItem is TakingRow r) { project.Takings.Remove(r.Rec); RefreshPermit(); } })));
        var dock = new DockPanel();
        DockPanel.SetDock(p, Dock.Top); dock.Children.Add(p); dock.Children.Add(takeGrid);
        return new ScrollViewer { Content = dock };
    }

    void SavePermitFields()
    {
        if (loadingPermit) return;
        project.SiteName = siteName.Text ?? "";
        var pt = SelectedPermit;
        pt.Number = permitNo.Text ?? ""; pt.Holder = holder.Text ?? "";
        pt.Issued = issued.SelectedDate?.DateTime; pt.Expires = expires.SelectedDate?.DateTime;
        pt.PermitYearStartMonth = int.TryParse(yearStart.Text, out var m) && m is >= 1 and <= 12 ? m : 1;
    }

    void AddPermit()
    {
        project.Permits.Add(new Permit { Number = $"New permit {project.Permits.Count + 1}" });
        permitBox.SelectedItem = project.Permits[^1];
        RefreshPermit();
    }

    void RemovePermit()
    {
        if (project.Permits.Count > 1 && permitBox.SelectedItem is Permit del)
            project.Permits.Remove(del);
        else
            SetStatus("A site needs at least one permit.");
        RefreshPermit();
    }

    void AddTaking()
    {
        var i = takeSrc.SelectedIndex;
        if (i < 0 || i >= allSources.Count || takeDate.SelectedDate == null || !double.TryParse(takeL.Text, out var l)) { SetStatus("Pick a date, source, and litres."); return; }
        project.Takings.Add(new TakingRecord
        {
            Date = takeDate.SelectedDate.Value.DateTime, SourceId = allSources[i].Id, Litres = l,
            PumpingMinutes = double.TryParse(takeMin.Text, out var m) ? m : null
        });
        RefreshPermit();
    }

    void RefreshPermit()
    {
        loadingPermit = true;
        var prev = permitBox.SelectedItem as Permit ?? project.PrimaryPermit;
        permitBox.ItemsSource = project.Permits.ToList();
        permitBox.SelectedItem = project.Permits.Contains(prev) ? prev : project.PrimaryPermit;
        var pt = SelectedPermit;
        siteName.Text = project.SiteName; permitNo.Text = pt.Number; holder.Text = pt.Holder;
        issued.SelectedDate = pt.Issued; expires.SelectedDate = pt.Expires;
        yearStart.Text = pt.PermitYearStartMonth.ToString();
        loadingPermit = false;
        allSources = project.Permits.SelectMany(x => x.Sources).ToList();
        srcGrid.ItemsSource = null; srcGrid.ItemsSource = pt.Sources;
        takeSrc.ItemsSource = allSources.Select(s => s.Name).ToList();
        takeGrid.ItemsSource = project.Takings.OrderByDescending(t => t.Date).Take(1000)
            .Select(t => new TakingRow(t, allSources.FirstOrDefault(s => s.Id == t.SourceId)?.Name ?? "(unknown)")).ToList();
    }

    // ---------- Import ----------
    List<string[]> importRows = new();
    readonly TextBox importPreview = new() { IsReadOnly = true, FontFamily = new FontFamily("Consolas,monospace"), Height = 260, AcceptsReturn = true };
    readonly TextBox importLog = new() { IsReadOnly = true, Height = 150, AcceptsReturn = true };
    readonly TextBox headerRowBox = new() { Width = 60, Text = "1" };
    readonly ComboBox mDate = new() { MinWidth = 140 }, mSrc = new() { MinWidth = 140 }, mVol = new() { MinWidth = 140 }, mMin = new() { MinWidth = 140 };
    readonly ComboBox mUnit = new() { ItemsSource = Enum.GetValues<VolumeUnit>(), SelectedIndex = 0 };
    readonly CheckBox mCreate = new() { Content = "Create unknown sources as new permit sources" };
    readonly Button undoImportBtn = new() { Content = "Undo last import", IsEnabled = false, Padding = new Thickness(8, 4) };
    int importTakingsMark = -1;
    List<PermitSource> beforeImportSources = new();

    Control BuildImportTab()
    {
        headerRowBox.LostFocus += (_, _) => RemapImport(false);
        var p = new StackPanel { Margin = new Thickness(10), Spacing = 4 };
        p.Children.Add(new TextBlock { Text = "Load the client's Excel/CSV, confirm which row holds the headers, and map the columns.", TextWrapping = TextWrapping.Wrap });
        p.Children.Add(Row(Btn("Open spreadsheet…", async () => await OpenImportFile()), Lbl("Header row #"), headerRowBox));
        p.Children.Add(importPreview);
        p.Children.Add(Row(Lbl("Date"), mDate, Lbl("Source"), mSrc, Lbl("Volume"), mVol, Lbl("Minutes (optional)"), mMin, Lbl("Unit"), mUnit));
        p.Children.Add(mCreate);
        p.Children.Add(Row(Btn("Import takings", RunImport), undoImportBtn));
        p.Children.Add(importLog);
        return new ScrollViewer { Content = p };
    }

    async Task OpenImportFile()
    {
        var path = await PickOpen("Spreadsheet", "*.xlsx", "*.xlsm", "*.csv");
        if (path == null) return;
        try
        {
            importRows = SpreadsheetImporter.ReadRows(path);
            headerRowBox.Text = (SpreadsheetImporter.GuessHeaderRow(importRows) + 1).ToString();
            RemapImport(true); SetStatus($"Read {importRows.Count} rows from {Path.GetFileName(path)}");
        }
        catch (Exception ex) { importLog.Text = "Could not read file: " + ex.Message; }
    }

    int HeaderIdx => int.TryParse(headerRowBox.Text, out var h) ? Math.Clamp(h - 1, 0, Math.Max(0, importRows.Count - 1)) : 0;

    void RemapImport(bool guess)
    {
        if (importRows.Count == 0) return;
        importPreview.Text = string.Join("\n", importRows.Take(15).Select((r, i) => $"{i + 1,3}: {string.Join(" | ", r)}"));
        var header = importRows[HeaderIdx];
        var names = new List<string> { "(none)" }; names.AddRange(header.Select((h, i) => $"{i + 1}: {h}"));
        foreach (var c in new[] { mDate, mSrc, mVol, mMin }) c.ItemsSource = names;
        var g = SpreadsheetImporter.GuessMapping(header);
        mDate.SelectedIndex = g.DateCol + 1; mSrc.SelectedIndex = g.SourceCol + 1;
        mVol.SelectedIndex = g.VolumeCol + 1; mMin.SelectedIndex = g.MinutesCol + 1; mUnit.SelectedItem = g.Unit;
    }

    void RunImport()
    {
        if (importRows.Count == 0) { importLog.Text = "Open a file first."; return; }
        var m = new ImportMapping
        {
            DateCol = mDate.SelectedIndex - 1, SourceCol = mSrc.SelectedIndex - 1, VolumeCol = mVol.SelectedIndex - 1,
            MinutesCol = mMin.SelectedIndex - 1, Unit = (VolumeUnit)(mUnit.SelectedItem ?? VolumeUnit.Litres),
            CreateMissingSources = mCreate.IsChecked == true
        };
        // snapshot for session-scoped undo (imports only ever append)
        importTakingsMark = project.Takings.Count;
        beforeImportSources = project.Permits.SelectMany(x => x.Sources).ToList();
        var r = SpreadsheetImporter.Apply(project, importRows, HeaderIdx, m);
        importLog.Text = $"Imported {r.Imported} rows.\n" +
            (r.CreatedSources.Count > 0 ? "Created sources: " + string.Join(", ", r.CreatedSources.Distinct()) + "\n" : "") +
            string.Join("\n", r.Warnings.Take(200));
        SetStatus($"Imported {r.Imported} takings; {r.Warnings.Count} warnings.");
        undoImportBtn.IsEnabled = r.Imported > 0;
    }

    void UndoImport()
    {
        if (importTakingsMark < 0) return;
        if (project.Takings.Count > importTakingsMark)
            project.Takings.RemoveRange(importTakingsMark, project.Takings.Count - importTakingsMark);
        foreach (var pt in project.Permits)   // drop sources the import created
            pt.Sources.RemoveAll(s => !beforeImportSources.Contains(s));
        importTakingsMark = -1;
        undoImportBtn.IsEnabled = false;
        importLog.Text = "Last import undone.";
        SetStatus("Import undone.");
        RefreshPermit();
    }

    // ---------- Compliance & reports ----------
    readonly DataGrid sumGrid = new() { AutoGenerateColumns = false, IsReadOnly = true, Height = 200 };
    readonly DataGrid excGrid = new() { AutoGenerateColumns = false, IsReadOnly = true };
    readonly ComboBox yearBox = new() { MinWidth = 100 };
    readonly TextBlock excHeader = new() { FontWeight = FontWeight.Bold, Margin = new Thickness(0, 8) };
    readonly StackPanel gaugePanel = new() { Margin = new Thickness(0, 6) };
    bool loadingYears;

    Control BuildReportTab()
    {
        sumGrid.Columns.Add(Col("Source", "Source", false, 200)); sumGrid.Columns.Add(Col("Total L", "TotalLitres"));
        sumGrid.Columns.Add(Col("Days", "DaysTaking")); sumGrid.Columns.Add(Col("Max day L", "MaxDayLitres"));
        sumGrid.Columns.Add(Col("Limit L/day", "LimitLpd")); sumGrid.Columns.Add(Col("Max L/min", "MaxLpm"));
        sumGrid.Columns.Add(Col("Limit L/min", "LimitLpm")); sumGrid.Columns.Add(Col("Limit days", "LimitDays"));
        excGrid.Columns.Add(Col("Date", "Date")); excGrid.Columns.Add(Col("Source", "Source", false, 200));
        excGrid.Columns.Add(Col("Rule", "Rule", false, 260)); excGrid.Columns.Add(Col("Actual", "Actual")); excGrid.Columns.Add(Col("Limit", "Limit"));
        yearBox.SelectionChanged += (_, _) => { if (!loadingYears) RefreshReport(); };
        var p = new StackPanel { Margin = new Thickness(10), Spacing = 4 };
        p.Children.Add(Row(Lbl("Year"), yearBox, Btn("Export HTML report…", () => Export(true)), Btn("Export CSV summary…", () => Export(false)),
                           Btn("Export MECP annual (HTML)…", () => ExportMecp(true)), Btn("Export MECP annual (CSV)…", () => ExportMecp(false))));
        p.Children.Add(sumGrid); p.Children.Add(gaugePanel); p.Children.Add(excHeader);
        var dock = new DockPanel();
        DockPanel.SetDock(p, Dock.Top); dock.Children.Add(p); dock.Children.Add(excGrid);
        return dock;
    }

    int? SelectedYear => yearBox.SelectedItem is int y ? y : null;

    void RefreshReport()
    {
        loadingYears = true;
        var prev = SelectedYear;
        var years = project.Takings.Select(t => t.Date.Year).Distinct().OrderDescending().ToList();
        yearBox.ItemsSource = new object[] { "All" }.Concat(years.Cast<object>()).ToList();
        yearBox.SelectedItem = prev != null && years.Contains(prev.Value) ? prev : "All";
        loadingYears = false;
        sumGrid.ItemsSource = ComplianceChecker.Summarize(project, SelectedYear);
        var exc = ComplianceChecker.Check(project, SelectedYear);
        excHeader.Text = exc.Count == 0 ? "No exceedances recorded." : $"{exc.Count} exceedance(s)";
        excHeader.Foreground = exc.Count == 0 ? Brushes.DarkGreen : Brushes.Firebrick;
        excGrid.ItemsSource = exc;
        RefreshGauges();
    }

    /// <summary>Per-source usage bars: worst day vs the L/day limit and peak observed
    /// rate vs the L/min limit, green below 80%, amber approaching, red over.</summary>
    void RefreshGauges()
    {
        gaugePanel.Children.Clear();
        foreach (var s in ComplianceChecker.Summarize(project, SelectedYear))
        {
            Gauge(s.Source, "worst day", s.MaxDayLitres, s.LimitLpd, "L/day");
            if (s.MaxLpm != null) Gauge(s.Source, "peak rate", s.MaxLpm!.Value, s.LimitLpm, "L/min");
        }

        void Gauge(string src, string what, double actual, double? limit, string unit)
        {
            if (limit is not { } lim || lim <= 0) return;
            var pct = Math.Clamp(actual / lim * 100, 0, 100);
            var col = actual >= lim ? Brushes.Firebrick : pct >= 80 ? Brushes.DarkGoldenrod : Brushes.DarkGreen;
            gaugePanel.Children.Add(new StackPanel
            {
                Spacing = 1,
                Children =
                {
                    new TextBlock { Text = $"{src} — {what}: {actual:N0} / {lim:N0} {unit} ({actual / lim * 100:F0}% of limit)", Foreground = col, FontSize = 12 },
                    new ProgressBar { Minimum = 0, Maximum = 100, Value = pct, Height = 10 }
                }
            });
        }
    }

    async void Export(bool html)
    {
        var ext = html ? "html" : "csv";
        var path = await PickSave("Export report", $"{project.SiteName} water report", ext);
        if (path == null) return;
        File.WriteAllText(path, html ? ReportBuilder.Html(project, SelectedYear) : ReportBuilder.Csv(project, SelectedYear));
        SetStatus("Exported " + path);
    }

    async void ExportMecp(bool html)
    {
        var ext = html ? "html" : "csv";
        var path = await PickSave("Export MECP annual report", $"{project.SiteName} annual water taking {SelectedYear?.ToString() ?? DateTime.Today.Year.ToString()}", ext);
        if (path == null) return;
        File.WriteAllText(path, html ? ReportBuilder.MecpHtml(project, SelectedYear) : ReportBuilder.MecpCsv(project, SelectedYear));
        SetStatus("Exported " + path);
    }

    // ---------- Mass balance ----------
    readonly DataGrid balGrid = new() { AutoGenerateColumns = false, IsReadOnly = true };
    readonly TextBox tolBox = new() { Width = 60, Text = "5" };
    readonly TextBlock balHeader = new() { FontWeight = FontWeight.Bold, Margin = new Thickness(0, 6) };
    double BalanceTolerance => double.TryParse(tolBox.Text, out var t) ? Math.Max(0, t) : 5;

    record BalRow(NodeBalance B)
    {
        public string Name => B.Feature.Name; public string Type => B.Feature.Kind.ToString();
        public string In => B.In.ToString("N0"); public string OtherIn => B.OtherIn.ToString("N0");
        public string Out => B.Out.ToString("N0"); public string OtherLoss => B.OtherLoss.ToString("N0");
        public string Net => B.Net.ToString("+#,0;-#,0;0"); public string Status => B.Status.ToString(); public string Note => B.Note;
    }

    Control BuildBalanceTab()
    {
        foreach (var (h, path, w) in new[] { ("Object", "Name", 180.0), ("Type", "Type", 100), ("Pipe in L/min", "In", 100), ("Other in", "OtherIn", 80),
                     ("Pipe out L/min", "Out", 110), ("Other loss", "OtherLoss", 90), ("Net (storage Δ)", "Net", 110), ("Status", "Status", 120), ("Note", "Note", 380) })
            balGrid.Columns.Add(Col(h, path, true, w));
        tolBox.LostFocus += (_, _) => { RefreshBalance(); RefreshMap(); };
        var p = new StackPanel { Margin = new Thickness(10), Spacing = 4 };
        p.Children.Add(new TextBlock { Text = "Steady-state balance from pipe flows entered on the Site Map (L/min). Pits, ponds and sumps may fill or drain; pumps, wash stations and other objects must balance. Problem objects get a red outline on the map.", TextWrapping = TextWrapping.Wrap });
        p.Children.Add(Row(Lbl("Tolerance %"), tolBox, Btn("Recalculate", () => { RefreshBalance(); RefreshMap(); })));
        p.Children.Add(balHeader);
        var dock = new DockPanel();
        DockPanel.SetDock(p, Dock.Top); dock.Children.Add(p); dock.Children.Add(balGrid);
        return dock;
    }

    void RefreshBalance()
    {
        var rows = MassBalance.Compute(project, BalanceTolerance);
        balGrid.ItemsSource = rows.Select(r => new BalRow(r)).ToList();
        var bad = rows.Count(r => MassBalance.IsProblem(r.Status)); var inc = rows.Count(r => r.Status == BalanceStatus.Incomplete);
        balHeader.Text = $"{bad} problem(s), {inc} incomplete";
        balHeader.Foreground = bad == 0 ? Brushes.DarkGreen : Brushes.Firebrick;
    }
}
