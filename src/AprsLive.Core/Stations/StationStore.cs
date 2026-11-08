using System.Text.Json;
using System.Text.Json.Serialization;
using AprsLive.Core.Settings;

namespace AprsLive.Core.Stations;

/// <summary>
/// Keeps the heard stations across restarts in <c>stations.json</c>, next to
/// <c>settings.json</c>. The station-expiry setting still applies once they
/// are loaded back.
/// </summary>
public static class StationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath => Path.Combine(
        Path.GetDirectoryName(AppSettings.DefaultPath)!, "stations.json");

    /// <summary>The saved stations, or none if the file is missing or unreadable.</summary>
    public static List<HeardStation> Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (!File.Exists(path))
                return new();
            return JsonSerializer.Deserialize<List<HeardStation>>(File.ReadAllText(path), JsonOptions) ?? new();
        }
        catch
        {
            return new();
        }
    }

    /// <summary>Writes a temporary file first, so an interrupted save never leaves a truncated list.</summary>
    public static void Save(IEnumerable<HeardStation> stations, string? path = null)
    {
        path ??= DefaultPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(stations.ToList(), JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }
}
