using System.Text.Json;
using System.Text.Json.Serialization;

namespace WaterMgmt.Core;

public static class ProjectStorage
{
    static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Save(SiteProject p, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(p, Opts));

    public static SiteProject Load(string path) =>
        JsonSerializer.Deserialize<SiteProject>(File.ReadAllText(path), Opts) ?? new SiteProject();
}
