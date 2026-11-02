using ClosedXML.Excel;
using System.Globalization;

namespace WaterMgmt.Core;

public enum VolumeUnit { Litres, CubicMetres, UsGallons, ImperialGallons }

public class ImportMapping
{
    public int DateCol { get; set; } = -1;
    public int SourceCol { get; set; } = -1;
    public int VolumeCol { get; set; } = -1;
    public int MinutesCol { get; set; } = -1;
    /// <summary>True when the minutes column is actually in hours (header says "Hours").</summary>
    public bool MinutesAreHours { get; set; }
    public VolumeUnit Unit { get; set; } = VolumeUnit.Litres;
    public bool CreateMissingSources { get; set; }
}

public class ImportResult
{
    public int Imported { get; set; }
    public List<string> Warnings { get; } = new();
    public List<string> CreatedSources { get; } = new();
}

/// <summary>Reads messy client spreadsheets (xlsx/csv) into raw rows, then maps them to takings.</summary>
public static class SpreadsheetImporter
{
    public static List<string[]> ReadRows(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".xlsx" or ".xlsm" ? ReadXlsx(path) : ReadCsv(File.ReadAllText(path));
    }

    static List<string[]> ReadXlsx(string path)
    {
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheets.First();
        var range = ws.RangeUsed();
        var rows = new List<string[]>();
        if (range == null) return rows;
        foreach (var r in range.Rows())
            rows.Add(r.Cells().Select(c => c.DataType == XLDataType.DateTime
                ? c.GetDateTime().ToString("yyyy-MM-dd") : c.GetFormattedString().Trim()).ToArray());
        return rows;
    }

    public static List<string[]> ReadCsv(string text)
    {
        var rows = new List<string[]>();
        foreach (var line in text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0))
        {
            var f = new List<string>(); var sb = new System.Text.StringBuilder(); bool q = false;
            foreach (var ch in line)
            {
                if (ch == '"') q = !q;
                else if (ch == ',' && !q) { f.Add(sb.ToString().Trim()); sb.Clear(); }
                else sb.Append(ch);
            }
            f.Add(sb.ToString().Trim()); rows.Add(f.ToArray());
        }
        return rows;
    }

    /// <summary>Guess a header row index: first row with >=2 non-numeric non-empty cells.</summary>
    public static int GuessHeaderRow(List<string[]> rows)
    {
        for (int i = 0; i < Math.Min(rows.Count, 20); i++)
            if (rows[i].Count(c => c.Length > 0 && !double.TryParse(c, out _)) >= 2) return i;
        return 0;
    }

    /// <summary>Suggest column mapping from header text.</summary>
    public static ImportMapping GuessMapping(string[] header)
    {
        int Find(params string[] keys) => Array.FindIndex(header, h =>
            keys.Any(k => h.Contains(k, StringComparison.OrdinalIgnoreCase)));
        var m = new ImportMapping
        {
            DateCol = Find("date", "day"),
            SourceCol = Find("source", "pump", "well", "location", "pit", "station", "name"),
            VolumeCol = Find("volume", "litres", "liters", "m3", "m³", "gal", "total", "taken", "pumped"),
            MinutesCol = Find("minutes", "mins", "runtime", "run time", "hour", "hrs", "heure")
        };
        if (m.MinutesCol >= 0)
            m.MinutesAreHours = HeaderSaysHours(header[m.MinutesCol]);
        var vh = m.VolumeCol >= 0 ? header[m.VolumeCol].ToLowerInvariant() : "";
        m.Unit = vh.Contains("m3") || vh.Contains("m³") ? VolumeUnit.CubicMetres
               : vh.Contains("usg") || vh.Contains("us gal") ? VolumeUnit.UsGallons
               : vh.Contains("igal") || vh.Contains("imp") ? VolumeUnit.ImperialGallons
               : vh.Contains("gal") ? VolumeUnit.UsGallons : VolumeUnit.Litres;
        return m;
    }

    public static double ToLitres(double v, VolumeUnit u) => u switch
    {
        VolumeUnit.CubicMetres => v * 1000,
        VolumeUnit.UsGallons => v * 3.785411784,
        VolumeUnit.ImperialGallons => v * 4.54609,
        _ => v
    };

    /// <summary>
    /// Parses numbers written in either English or French-Canadian conventions:
    /// "0.5", "1,234.56" (en), "0,5", "1 234,5", "1.234,56" (fr-CA, incl. non-breaking
    /// space thousands separators that Excel emits in French locales).
    /// When both separators appear, the right-most one is the decimal point.
    /// A trailing comma followed by 1-2 digits is a decimal comma; otherwise a
    /// lone comma is treated as a thousands separator ("1,234" -> 1234).
    /// </summary>
    public static bool TryParseDecimal(string? s, out double v)
    {
        v = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim().Replace("\u00A0", "").Replace("\u202F", "").Replace(" ", "").Replace("'", "");
        bool hasComma = s.Contains(','), hasDot = s.Contains('.');
        var norm = s;
        if (hasComma && hasDot)
        {
            norm = s.LastIndexOf(',') > s.LastIndexOf('.')
                ? s.Replace(".", "").Replace(',', '.')   // 1.234,5  -> 1234.5
                : s.Replace(",", "");                    // 1,234.56 -> 1234.56
        }
        else if (hasComma)
        {
            var tail = s.Length - s.LastIndexOf(',') - 1;
            norm = tail is 1 or 2 ? s.Replace(',', '.') : s.Replace(",", "");
        }
        return double.TryParse(norm, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    /// <summary>Header text indicates a runtime column expressed in hours (en/fr, incl. "hrs").</summary>
    static bool HeaderSaysHours(string h) =>
        h.Contains("hour", StringComparison.OrdinalIgnoreCase)
     || h.Contains("heur", StringComparison.OrdinalIgnoreCase)
     || h.Contains("hr", StringComparison.OrdinalIgnoreCase);

    public static ImportResult Apply(SiteProject p, List<string[]> rows, int headerRow, ImportMapping m)
    {
        var res = new ImportResult();
        if (m.DateCol < 0 || m.VolumeCol < 0) { res.Warnings.Add("Date and volume columns are required."); return res; }
        // Hours detection must also work when the column was mapped by hand in the UI dialog
        // (GuessMapping never ran), so sniff the actual header row, not just the flag.
        var runtimeHeader = m.MinutesCol >= 0 && headerRow >= 0 && headerRow < rows.Count
                            && m.MinutesCol < rows[headerRow].Length ? rows[headerRow][m.MinutesCol] : "";
        var inHours = m.MinutesAreHours || HeaderSaysHours(runtimeHeader);
        if (inHours && m.MinutesCol >= 0)
            res.Warnings.Add($"Column '{runtimeHeader}' is in hours; values were converted ×60 to minutes.");
        var dayTotals = new Dictionary<(DateTime Date, Guid Src), double>();
        for (int i = headerRow + 1; i < rows.Count; i++)
        {
            var r = rows[i];
            string Cell(int c) => c >= 0 && c < r.Length ? r[c] : "";
            if (!TryDate(Cell(m.DateCol), out var date)) { if (r.Any(x => x.Length > 0)) res.Warnings.Add($"Row {i + 1}: bad date '{Cell(m.DateCol)}'"); continue; }
            if (!TryParseDecimal(Cell(m.VolumeCol), out var vol))
            { res.Warnings.Add($"Row {i + 1}: bad volume '{Cell(m.VolumeCol)}'"); continue; }

            var name = m.SourceCol >= 0 ? Cell(m.SourceCol) : "";
            var src = p.Permits.SelectMany(pt => pt.Sources).FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (src == null)
            {
                if (m.CreateMissingSources && name.Length > 0)
                {
                    src = new PermitSource { Name = name };
                    p.PrimaryPermit.Sources.Add(src); res.CreatedSources.Add(name);
                }
                else { res.Warnings.Add($"Row {i + 1}: unknown source '{name}'"); continue; }
            }
            double? mins = null;
            if (m.MinutesCol >= 0 && TryParseDecimal(Cell(m.MinutesCol), out var mm))
                mins = inHours ? mm * 60 : mm;
            var litres = ToLitres(vol, m.Unit);
            // Sanity checks: warn at import time so bad sheets get fixed before they
            // reach a report. Rows are still imported; Compliance reports the verdict.
            if (mins is { } mi && mi > 1440)
                res.Warnings.Add($"Row {i + 1}: runtime {mi:N0} min exceeds 24 h — check the runtime column units.");
            if (mins is { } m2 && m2 > 0 && src.MaxLitresPerMinute is { } lpm && litres / m2 > lpm)
                res.Warnings.Add($"Row {i + 1}: implied rate {litres / m2:N0} L/min exceeds the permit max of {lpm:N0} L/min.");
            dayTotals[(date.Date, src.Id)] = dayTotals.GetValueOrDefault((date.Date, src.Id)) + litres;
            p.Takings.Add(new TakingRecord { Date = date, SourceId = src.Id, Litres = litres, PumpingMinutes = mins });
            res.Imported++;
        }
        foreach (var ((date, srcId), total) in dayTotals)
        {
            var s = p.Permits.SelectMany(pt => pt.Sources).FirstOrDefault(x => x.Id == srcId);
            if (s?.MaxLitresPerDay is { } lpd && total > lpd)
                res.Warnings.Add($"{date:yyyy-MM-dd} '{s.Name}': day total {total:N0} L exceeds the permit max of {lpd:N0} L/day.");
        }
        return res;
    }

    static bool TryDate(string s, out DateTime d)
    {
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return true;
        if (double.TryParse(s, out var oa) && oa > 20000 && oa < 80000) { d = DateTime.FromOADate(oa); return true; }
        return false;
    }
}
