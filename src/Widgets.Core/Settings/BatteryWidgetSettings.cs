using System.Text.Json;
using Widgets.Core.Appearance;

namespace Widgets.Core.Settings;

/// <summary>
/// What a placed copy of the battery widget stores as its settings JSON: which devices show and in what order, and how the
/// tiles look. The theme and the material behind the widget come from the host, so only the parts of
/// <see cref="AppearanceSettings"/> that describe the tiles themselves are used (layout, icons, palette, size, spacing, colours, names).
/// </summary>
public sealed class BatteryWidgetSettings
{
    /// <summary>Longest custom device name kept; a longer one would not fit a taskbar tile.</summary>
    public const int MaxLabelLength = 24;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never };

    /// <summary>Device metric ids in the order the user wants them. Ids that are not listed follow, in provider order.</summary>
    public List<string> Order { get; set; } = [];

    /// <summary>Device metric ids the user turned off.</summary>
    public List<string> Hidden { get; set; } = [];

    public AppearanceSettings Appearance { get; set; } = new();

    /// <summary>A device at or below this level (percent) is flagged as low.</summary>
    public int LowThreshold { get; set; } = 20;

    /// <summary>A name the user gave a device, by metric id, shown instead of the device's own name.</summary>
    public Dictionary<string, string> Labels { get; set; } = [];

    /// <summary>A colour (#RRGGBB) the user gave a device, by metric id, used instead of the battery palette.</summary>
    public Dictionary<string, string> Colours { get; set; } = [];

    /// <summary>Show a Windows notification once when a device reaches the low level (<see cref="LowThreshold"/>).</summary>
    public bool LowBatteryAlerts { get; set; } = true;

    /// <summary>
    /// Read a DualSense that is connected by Bluetooth. Off by default: it needs a request that leaves the pad in its
    /// extended report mode until it reconnects. A pad on a cable is always read.
    /// </summary>
    public bool ReadDualSenseBluetooth { get; set; }

    /// <summary>How often the batteries are read, in minutes (1 to 10).</summary>
    public int RefreshMinutes { get; set; } = 1;

    /// <summary>A device that has been disconnected this many minutes leaves the taskbar and flyout (0 = never). It comes back when it reports again.</summary>
    public int HideAfterMinutes { get; set; } = 30;

    /// <summary><see cref="HideAfterMinutes"/> as a span, or null for never.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public TimeSpan? HideAfter => HideAfterMinutes > 0 ? TimeSpan.FromMinutes(HideAfterMinutes) : null;

    public BatteryWidgetSettings Clone() => new()
    {
        Order = [.. Order ?? []], Hidden = [.. Hidden ?? []], Appearance = (Appearance ?? new AppearanceSettings()).Clone(), LowThreshold = LowThreshold,
        Labels = new(Labels ?? [], StringComparer.Ordinal), Colours = new(Colours ?? [], StringComparer.Ordinal), RefreshMinutes = RefreshMinutes,
        HideAfterMinutes = HideAfterMinutes, LowBatteryAlerts = LowBatteryAlerts, ReadDualSenseBluetooth = ReadDualSenseBluetooth,
    };

    /// <summary>The user's name for a device, or <paramref name="fallback"/> (the device's own name) when there is none.</summary>
    public string LabelFor(string id, string fallback) => Labels.TryGetValue(id, out var label) ? label : fallback;

    /// <summary>The user's colour for a device, or null when it follows the palette.</summary>
    public Rgb? ColourFor(string id) => Colours.TryGetValue(id, out var hex) && Hex.TryParse(hex, out var c) ? c : null;

    /// <summary>The order and visibility as the general layout settings, so <see cref="TileLayout"/> can be reused.</summary>
    public WidgetSettings AsLayout() => new() { Order = [.. Order], Hidden = [.. Hidden] };

    /// <summary>This settings object with the order and hidden lists taken from <paramref name="layout"/>.</summary>
    public BatteryWidgetSettings WithLayout(WidgetSettings layout)
    {
        var next = Clone();
        next.Order = [.. layout.Order];
        next.Hidden = [.. layout.Hidden];
        return next;
    }

    /// <summary>Reads settings leniently: an empty, missing or damaged string means defaults, unknown fields are ignored, and every value is forced into range.</summary>
    public static BatteryWidgetSettings FromJson(string? json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json)) return new BatteryWidgetSettings();
            var s = JsonSerializer.Deserialize<BatteryWidgetSettings>(json, Json);
            return s is null ? new BatteryWidgetSettings() : Sanitize(s);
        }
        catch (JsonException)
        {
            return new BatteryWidgetSettings();
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static BatteryWidgetSettings Sanitize(BatteryWidgetSettings s)
    {
        var next = s.Clone();
        next.Order = [.. (s.Order ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal)];
        next.Hidden = [.. (s.Hidden ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal)];
        next.Appearance = AppearanceSettings.Sanitize(s.Appearance ?? new AppearanceSettings());
        next.LowThreshold = Math.Clamp(s.LowThreshold, 5, 50);
        next.RefreshMinutes = Math.Clamp(s.RefreshMinutes, 1, 10);
        next.HideAfterMinutes = Math.Clamp(s.HideAfterMinutes, 0, 1440);

        next.Labels = [];
        foreach (var (id, label) in s.Labels ?? [])
        {
            var trimmed = (label ?? "").Trim();
            if (string.IsNullOrWhiteSpace(id) || trimmed.Length == 0) continue;
            if (trimmed.Length > MaxLabelLength)
            {
                // Never cut between the two halves of a surrogate pair (an emoji): that leaves a box.
                var keep = char.IsHighSurrogate(trimmed[MaxLabelLength - 1]) ? MaxLabelLength - 1 : MaxLabelLength;
                trimmed = trimmed[..keep];
            }
            next.Labels[id] = trimmed;
        }

        next.Colours = [];
        foreach (var (id, hex) in s.Colours ?? [])
            if (!string.IsNullOrWhiteSpace(id) && Hex.TryParse(hex, out var colour)) next.Colours[id] = colour.ToHex();
        return next;
    }
}
