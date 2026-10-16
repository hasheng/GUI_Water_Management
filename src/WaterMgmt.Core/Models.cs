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
    public string SiteName { get; set; } = "New Site";
    public string? ImagePath { get; set; }
    public Permit Permit { get; set; } = new();
    public List<SiteFeature> Features { get; set; } = new();
    public List<FlowLink> Links { get; set; } = new();
    public List<TakingRecord> Takings { get; set; } = new();
}
