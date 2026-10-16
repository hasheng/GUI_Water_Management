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
            MinutesCol = Find("minutes", "mins", "runtime", "run time", "hours")
        };
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

    public static ImportResult Apply(SiteProject p, List<string[]> rows, int headerRow, ImportMapping m)
    {
        var res = new ImportResult();
        if (m.DateCol < 0 || m.VolumeCol < 0) { res.Warnings.Add("Date and volume columns are required."); return res; }
        for (int i = headerRow + 1; i < rows.Count; i++)
        {
            var r = rows[i];
            string Cell(int c) => c >= 0 && c < r.Length ? r[c] : "";
            if (!TryDate(Cell(m.DateCol), out var date)) { if (r.Any(x => x.Length > 0)) res.Warnings.Add($"Row {i + 1}: bad date '{Cell(m.DateCol)}'"); continue; }
            if (!double.TryParse(Cell(m.VolumeCol).Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var vol))
            { res.Warnings.Add($"Row {i + 1}: bad volume '{Cell(m.VolumeCol)}'"); continue; }

            var name = m.SourceCol >= 0 ? Cell(m.SourceCol) : "";
            var src = p.Permit.Sources.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (src == null)
            {
                if (m.CreateMissingSources && name.Length > 0)
                {
                    src = new PermitSource { Name = name };
                    p.Permit.Sources.Add(src); res.CreatedSources.Add(name);
                }
                else { res.Warnings.Add($"Row {i + 1}: unknown source '{name}'"); continue; }
            }
            double? mins = null;
            if (m.MinutesCol >= 0 && double.TryParse(Cell(m.MinutesCol), NumberStyles.Any, CultureInfo.InvariantCulture, out var mm)) mins = mm;
            p.Takings.Add(new TakingRecord { Date = date, SourceId = src.Id, Litres = ToLitres(vol, m.Unit), PumpingMinutes = mins });
            res.Imported++;
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
