using System.Globalization;
using System.Text.Json.Serialization;
using Widgets.Core.Appearance;

namespace Widgets.Core.Settings;

/// <summary>How each tile is framed. Transparent and Compact draw no background and use thin dividers instead.</summary>
public enum PillStyle { Classic, Soft, Transparent, Compact }

/// <summary>The icon drawn before each value. Battery draws a level gauge for devices (other tiles use Outline).</summary>
public enum IconStyle { Outline, Filled, Simple, Battery, None }

public enum BatteryPalette { Classic, ColourBlind, Pastel, HighContrast, Mono, Custom }

/// <summary>Auto follows the Windows taskbar theme.</summary>
public enum ThemeChoice { Auto, Light, Dark }

/// <summary>Acrylic is the standard frosted-glass bar. Adjustable is the same glass with a strength the user sets.</summary>
public enum BackgroundStyle { Acrylic, Adjustable }

/// <summary>How the taskbar preview is laid out: the layouts of WidBar's System Metrics widget, minus its history graph.</summary>
public enum PreviewLayout { VerticalMeters, FillCapsules, IconMatrix, MinimalText, RadialGauges }

/// <summary>The space between two tiles of the preview.</summary>
public enum TileSpacing { Compact, Standard, Relaxed }

/// <summary>Where the tiles sit when the preview is wider than they need.</summary>
public enum ContentAlignment { Left, Center, Right }

/// <summary>How the widget looks. Part of <see cref="WidgetSettings"/>; invalid values fall back to the defaults.</summary>
public sealed class AppearanceSettings
{
    public static readonly int[] Scales = [90, 100, 115, 130];

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PillStyle Pill { get; set; } = PillStyle.Transparent;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public IconStyle Icons { get; set; } = IconStyle.Outline;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BatteryPalette Palette { get; set; } = BatteryPalette.Classic;

    /// <summary>Colours for the Custom palette, as #RRGGBB.</summary>
    public string CustomHigh { get; set; } = "#6EDC78";
    public string CustomMedium { get; set; } = "#F5C842";
    public string CustomLow { get; set; } = "#FF9999";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ThemeChoice Theme { get; set; } = ThemeChoice.Auto;

    /// <summary>Percent: one of <see cref="Scales"/>.</summary>
    public int Scale { get; set; } = 100;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BackgroundStyle Background { get; set; } = BackgroundStyle.Acrylic;

    /// <summary>For the Adjustable background: 0 (clearest glass) to 100 (most opaque).</summary>
    public int BackgroundOpacity { get; set; } = 55;

    /// <summary>The name of each tile beside its value.</summary>
    public bool ShowNames { get; set; } = true;

    // Lenient: a file saved by an older build with a layout that no longer exists keeps all its other settings.
    [JsonConverter(typeof(PreviewLayoutConverter))]
    public PreviewLayout Layout { get; set; } = PreviewLayout.VerticalMeters;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TileSpacing Spacing { get; set; } = TileSpacing.Standard;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ContentAlignment Alignment { get; set; } = ContentAlignment.Center;

    /// <summary>Pixels added to (or taken from) the preview's automatic width: -120 to 400, 0 keeps it automatic.</summary>
    public int WidthAdjustment { get; set; }

    public bool ShowPercentSymbol { get; set; } = true;
    public bool ShowSeparators { get; set; } = true;
    public bool ColouredLabelsAndIcons { get; set; } = true;
    public bool ColouredValues { get; set; }

    /// <summary>The bolt next to a device that is charging.</summary>
    public bool ShowChargingBolt { get; set; } = true;

    /// <summary>One Windows accent colour for every device, instead of the battery palette.</summary>
    public bool UseAccentColour { get; set; }

    public AppearanceSettings Clone() => (AppearanceSettings)MemberwiseClone();

    /// <summary>A copy with every value forced into its valid range, so a hand-edited file cannot break the widget.</summary>
    public static AppearanceSettings Sanitize(AppearanceSettings a)
    {
        var s = a.Clone();
        if (!Enum.IsDefined(s.Pill)) s.Pill = PillStyle.Transparent;
        if (!Enum.IsDefined(s.Icons)) s.Icons = IconStyle.Outline;
        if (!Enum.IsDefined(s.Palette)) s.Palette = BatteryPalette.Classic;
        if (!Enum.IsDefined(s.Theme)) s.Theme = ThemeChoice.Auto;
        if (!Enum.IsDefined(s.Background)) s.Background = BackgroundStyle.Acrylic;
        if (!Enum.IsDefined(s.Layout)) s.Layout = PreviewLayout.VerticalMeters;
        if (!Enum.IsDefined(s.Spacing)) s.Spacing = TileSpacing.Standard;
        if (!Enum.IsDefined(s.Alignment)) s.Alignment = ContentAlignment.Center;
        s.WidthAdjustment = Math.Clamp(s.WidthAdjustment, -120, 400);
        s.Scale = Scales.Contains(s.Scale) ? s.Scale : Scales.OrderBy(x => Math.Abs(x - s.Scale)).First();
        s.BackgroundOpacity = Math.Clamp(s.BackgroundOpacity, 0, 100);
        s.CustomHigh = Hex.Normalise(s.CustomHigh, "#6EDC78");
        s.CustomMedium = Hex.Normalise(s.CustomMedium, "#F5C842");
        s.CustomLow = Hex.Normalise(s.CustomLow, "#FF9999");
        return s;
    }
}

/// <summary>#RRGGBB parsing.</summary>
public static class Hex
{
    public static bool TryParse(string? text, out Rgb color)
    {
        color = default;
        if (text is not { Length: 7 } || text[0] != '#') return false;
        if (!int.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var n)) return false;
        color = new Rgb((byte)(n >> 16), (byte)(n >> 8), (byte)n);
        return true;
    }

    /// <summary>The colour in upper-case #RRGGBB form, or <paramref name="fallback"/> when the text is not one.</summary>
    public static string Normalise(string? text, string fallback) => TryParse(text, out var c) ? c.ToHex() : fallback;
}
