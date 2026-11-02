using System.Text.Json.Serialization;

namespace WaterMgmt.Core;

public enum FeatureKind { Pit, StormPond, Sump, Well, WashStation, Pump, Outfall, Other }

public class SiteFeature
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public FeatureKind Kind { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>True for pits/sumps that are actively dewatered.</summary>
    public bool Dewatered { get; set; }
    /// <summary>Rated capacity for pumps (L/min).</summary>
    public double? RatedLpm { get; set; }
    /// <summary>Unmeasured inflow to this node, e.g. seepage, runoff, precipitation (L/min).</summary>
    public double? OtherInflowLpm { get; set; }
    /// <summary>Unmeasured loss at this node, e.g. evaporation, truck loading, wash consumption (L/min).</summary>
    public double? OtherLossLpm { get; set; }
    /// <summary>Permit source this feature takes water under (pumps, wells, pits).</summary>
    public Guid? PermitSourceId { get; set; }
    public string Notes { get; set; } = "";
}

/// <summary>A pipe/channel with direction From -> To.</summary>
public class FlowLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FromId { get; set; }
    public Guid ToId { get; set; }
    public double? FlowLpm { get; set; }
    public string Label { get; set; } = "";
}

/// <summary>One authorised taking point under a Permit To Take Water.</summary>
public class PermitSource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public double? MaxLitresPerMinute { get; set; }
    public double? MaxLitresPerDay { get; set; }
    public int? MaxDaysPerYear { get; set; }
}

public class Permit
{
    public string Number { get; set; } = "";
    public string Holder { get; set; } = "";
    public DateTime? Issued { get; set; }
    public DateTime? Expires { get; set; }
    /// <summary>Month (1-12) each permit year starts. Max-days-per-year limits count
    /// over this rolling 12-month window, not necessarily the calendar year;
    /// 1 = January (the default).</summary>
    public int PermitYearStartMonth { get; set; } = 1;
    public List<PermitSource> Sources { get; set; } = new();
}

public class TakingRecord
{
    public DateTime Date { get; set; }
    public Guid SourceId { get; set; }
    public double Litres { get; set; }
    /// <summary>Minutes pumped that day, if known; used to derive the observed rate.</summary>
    public double? PumpingMinutes { get; set; }
    /// <summary>Directly measured peak rate, if the client supplied one.</summary>
    public double? PeakLpm { get; set; }
}

public class SiteProject
{
    /// <summary>Current .wsite file format version. Bump when the serialized shape
    /// changes and add a matching arm in ProjectStorage.Migrate.</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>File format version of this project. Null in memory and for legacy
    /// files saved before versioning existed (the JSON property is simply absent);
    /// ProjectStorage stamps it on Save and migrates older files on Load.</summary>
    public int? SchemaVersion { get; set; }
    public string SiteName { get; set; } = "New Site";
    public string? ImagePath { get; set; }

    /// <summary>All permits covering the site — groundwater and surface water PTTWs
    /// are normally separate, so a site routinely carries two or more.</summary>
    public List<Permit> Permits { get; set; } = new() { new() };

    /// <summary>First permit; the one the permit tab edits by default and the one
    /// new sources are created under. Never null.</summary>
    [JsonIgnore]
    public Permit PrimaryPermit
    {
        get { if (Permits.Count == 0) Permits.Add(new Permit()); return Permits[0]; }
    }

    public List<SiteFeature> Features { get; set; } = new();
    public List<FlowLink> Links { get; set; } = new();
    public List<TakingRecord> Takings { get; set; } = new();
}
