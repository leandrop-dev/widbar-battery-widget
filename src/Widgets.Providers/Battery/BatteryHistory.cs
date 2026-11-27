using System.Text.Json;
using Widgets.Core;

namespace Widgets.Providers.Battery;

/// <summary>
/// Remembers each device's battery readings for a week, for the flyout's sparkline and time-left estimate. It records a
/// reading only when the level or the charging state changed, or when a quarter of an hour has passed, so a steady device
/// does not grow the file. A missing or damaged file means no history. Safe to call from different threads.
/// </summary>
public sealed class BatteryHistory
{
    public static readonly TimeSpan Keep = TimeSpan.FromDays(7);

    /// <summary>A flat stretch still gets a point this often, so the line has no gaps.</summary>
    public static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(15);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly object _gate = new();
    private readonly string _path;
    private readonly Dictionary<string, List<HistorySample>> _byId = new(StringComparer.Ordinal);
    private bool _dirty;

    public BatteryHistory(string path, TimeProvider? time = null)
    {
        _path = path;
        Load((time ?? TimeProvider.System).GetUtcNow());
    }

    /// <summary>Adds the reading if it is worth keeping. True when it was added; call <see cref="Save"/> once after a batch.</summary>
    public bool Record(string id, int percent, bool charging, DateTimeOffset now)
    {
        if (percent is < 0 or > 100) return false;
        lock (_gate)
        {
            if (!_byId.TryGetValue(id, out var list)) _byId[id] = list = [];
            Prune(list, now);

            if (list.Count > 0)
            {
                var last = list[^1];
                if (now < last.At) return false; // the clock moved back: keep the order
                if (last.Percent == percent && last.Charging == charging && now - last.At < Heartbeat) return false;
            }
            list.Add(new HistorySample(now, percent, charging));
            _dirty = true;
            return true;
        }
    }

    /// <summary>A copy of a device's readings, oldest first. Empty for a device never seen.</summary>
    public IReadOnlyList<HistorySample> Samples(string id)
    {
        lock (_gate) return _byId.TryGetValue(id, out var list) ? [.. list] : [];
    }

    /// <summary>Writes through a temporary file when something was added. False if it could not be saved; history is then kept in memory only.</summary>
    public bool Save()
    {
        string text;
        lock (_gate)
        {
            if (!_dirty) return true;
            text = JsonSerializer.Serialize(_byId.Where(p => p.Value.Count > 0).ToDictionary(p => p.Key, p => p.Value), Json);
            _dirty = false;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, text);
            File.Move(temp, _path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (_gate) _dirty = true; // try again at the next change
            return false;
        }
    }

    private static void Prune(List<HistorySample> list, DateTimeOffset now) =>
        list.RemoveAll(s => now - s.At > Keep);

    private void Load(DateTimeOffset now)
    {
        try
        {
            if (!File.Exists(_path)) return;
            var stored = JsonSerializer.Deserialize<Dictionary<string, List<HistorySample>>>(File.ReadAllText(_path), Json) ?? [];
            foreach (var (id, list) in stored)
            {
                if (string.IsNullOrWhiteSpace(id) || list is null) continue;
                var clean = list
                    .Where(s => s is not null && s.Percent is >= 0 and <= 100 && now - s.At <= Keep && s.At <= now)
                    .OrderBy(s => s.At)
                    .ToList();
                if (clean.Count > 0) _byId[id] = clean;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _byId.Clear();
        }
    }
}
