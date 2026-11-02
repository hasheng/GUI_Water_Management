using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace WaterMgmt.Core;

public static class ProjectStorage
{
    static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Save(SiteProject p, string path)
    {
        p.SchemaVersion = SiteProject.CurrentSchemaVersion;   // always stamp the current format
        File.WriteAllText(path, JsonSerializer.Serialize(p, Opts));
    }

    public static SiteProject Load(string path)
    {
        var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject();

        if (node["SchemaVersion"]?.GetValue<int>() is { } ver && ver > SiteProject.CurrentSchemaVersion)
            throw new InvalidDataException($"This site file was saved by a newer version of the app (format {ver}; this build reads up to {SiteProject.CurrentSchemaVersion}). Update the app before opening it, otherwise saving would lose data.");

        // v1 -> v2 (structural part): the single "Permit" object becomes the only
        // entry of the "Permits" list. Must happen at the JSON level — a typed
        // SiteProject has no Permits property to receive the old data.
        if (!node.ContainsKey("Permits"))
        {
            var single = node["Permit"]?.DeepClone();
            node.Remove("Permit");
            node["Permits"] = single is null ? new JsonArray() : new JsonArray(single);
        }

        var p = node.Deserialize<SiteProject>(Opts) ?? new SiteProject();
        Migrate(p);
        if (p.Permits.Count == 0) p.Permits.Add(new Permit());   // keep PrimaryPermit valid
        return p;
    }

    /// <summary>
    /// Forward-migrates a loaded project to SiteProject.CurrentSchemaVersion.
    /// Legacy files written before SchemaVersion existed deserialize with
    /// SchemaVersion == null. When bumping the format, add a new arm per version
    /// step, oldest first (structural JSON rewrites live in Load).
    /// </summary>
    static SiteProject Migrate(SiteProject p)
    {
        if (p.SchemaVersion is null)
        {
            // v0 -> v1: SchemaVersion introduced; no data shape changed, just stamp it.
            p.SchemaVersion = 1;
        }
        if (p.SchemaVersion < 2)
        {
            // v1 -> v2: single Permit moved into Permits at the JSON level (see Load).
            p.SchemaVersion = 2;
        }
        // Future example:
        // if (p.SchemaVersion < 3) { /* upgrade v2 data to v3 */ p.SchemaVersion = 3; }
        return p;
    }
}
