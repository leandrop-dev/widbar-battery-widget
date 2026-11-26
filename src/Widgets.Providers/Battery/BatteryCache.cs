using System.Text.Json;

namespace Widgets.Providers.Battery;

/// <summary>A device's last known battery level, remembered between runs.</summary>
public sealed record CachedBattery(string Id, string Name, DeviceKind Kind, int Percent, DateTimeOffset At);

/// <summary>
/// Remembers each device's last battery level on disk, so a device that is asleep or switched off when the app
/// starts still shows its last value (dimmed) instead of disappearing. A missing or damaged file means an empty cache.
/// </summary>
public sealed class BatteryCache
{
    /// <summary>Devices not heard from for this long are forgotten, so a device that was sold or lost does not linger.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;

    public BatteryCache(string path) => _path = path;

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinTaskbarWidgets", "battery-cache.json");

    public IReadOnlyList<CachedBattery> Load(DateTimeOffset now)
    {
        try
        {
            if (!File.Exists(_path)) return [];
            var items = JsonSerializer.Deserialize<List<CachedBattery>>(File.ReadAllText(_path), Json) ?? [];
            return items
                .Where(i => i is not null && !string.IsNullOrWhiteSpace(i.Id) && !string.IsNullOrWhiteSpace(i.Name)
                            && i.Percent is >= 0 and <= 100 && now - i.At <= MaxAge)
                .GroupBy(i => i.Id, StringComparer.Ordinal).Select(g => g.First())
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    /// <summary>Writes through a temporary file. False if it could not be saved; the app carries on without a cache.</summary>
    public bool Save(IEnumerable<CachedBattery> items)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(items.ToList(), Json));
            File.Move(temp, _path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
