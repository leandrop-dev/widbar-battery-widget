using System.Globalization;
using Widgets.Core.Settings;

namespace Widgets.Core.Appearance;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public string ToHex() => string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}");
}

public enum BatteryBand { High, Medium, Warning, Low, Unknown }

/// <summary>Colours of the battery level bands. Palette values follow the reference project (Gabrielfch/taskbar-battery-widget, MIT).</summary>
public static class BatteryColors
{
    public const double HighMin = 60, MediumMin = 30;

    private readonly record struct Pair(Rgb Dark, Rgb Light);
    private sealed record Set(Pair High, Pair Medium, Pair Warning, Pair Low, Pair None);

    private static Pair P(byte dr, byte dg, byte db, byte lr, byte lg, byte lb) => new(new Rgb(dr, dg, db), new Rgb(lr, lg, lb));
    private static readonly Pair Grey = P(182, 182, 182, 82, 82, 82);
    private static readonly Pair MonoMid = P(235, 235, 235, 40, 40, 40);

    private static readonly Dictionary<BatteryPalette, Set> Sets = new()
    {
        [BatteryPalette.Classic] = new(P(110, 220, 120, 17, 97, 40), P(245, 200, 66, 114, 74, 0), P(255, 158, 72, 139, 58, 0), P(255, 153, 153, 165, 24, 24), Grey),
        [BatteryPalette.ColourBlind] = new(P(115, 196, 240, 0, 82, 150), P(240, 228, 66, 96, 80, 0), P(245, 172, 45, 118, 62, 0), P(255, 165, 120, 145, 50, 0), Grey),
        [BatteryPalette.Pastel] = new(P(168, 230, 180, 22, 92, 52), P(255, 236, 150, 105, 78, 0), P(255, 200, 150, 130, 62, 8), P(255, 170, 185, 150, 30, 56), P(195, 195, 205, 78, 78, 88)),
        [BatteryPalette.HighContrast] = new(P(0, 255, 90, 0, 90, 0), P(255, 255, 0, 92, 62, 0), P(255, 165, 30, 130, 44, 0), P(255, 155, 155, 160, 0, 0), P(255, 255, 255, 0, 0, 0)),
        [BatteryPalette.Mono] = new(MonoMid, MonoMid, MonoMid, P(255, 153, 153, 165, 24, 24), Grey),
    };

    /// <summary>The band for a level. A level at or below <paramref name="lowAt"/> is Low, whatever the fixed bands say.</summary>
    public static BatteryBand BandFor(double? percent, double lowAt)
    {
        if (percent is not { } p || double.IsNaN(p)) return BatteryBand.Unknown;
        if (p <= lowAt) return BatteryBand.Low;
        if (p < MediumMin) return BatteryBand.Warning;
        if (p < HighMin) return BatteryBand.Medium;
        return BatteryBand.High;
    }

    /// <param name="barBackground">The colour the text sits on, used to keep a custom colour readable. Ignored by the built-in palettes, which are already readable on both themes.</param>
    public static Rgb For(AppearanceSettings a, BatteryBand band, bool dark, Rgb barBackground)
    {
        if (a.Palette == BatteryPalette.Custom) return Custom(a, band, dark, barBackground);
        var set = Sets[a.Palette];
        var pair = band switch
        {
            BatteryBand.High => set.High,
            BatteryBand.Medium => set.Medium,
            BatteryBand.Warning => set.Warning,
            BatteryBand.Low => set.Low,
            _ => set.None,
        };
        return dark ? pair.Dark : pair.Light;
    }

    private static Rgb Custom(AppearanceSettings a, BatteryBand band, bool dark, Rgb background)
    {
        var high = Hex.TryParse(a.CustomHigh, out var h) ? h : new Rgb(110, 220, 120);
        var medium = Hex.TryParse(a.CustomMedium, out var m) ? m : new Rgb(245, 200, 66);
        var low = Hex.TryParse(a.CustomLow, out var l) ? l : new Rgb(255, 153, 153);
        var picked = band switch
        {
            BatteryBand.High => high,
            BatteryBand.Medium => medium,
            BatteryBand.Warning => Mid(medium, low), // orange sits halfway between the medium and low colours
            BatteryBand.Low => low,
            _ => dark ? Grey.Dark : Grey.Light,
        };
        return Contrast.Ensure(picked, background, 4.5);
    }

    private static Rgb Mid(Rgb x, Rgb y) => new((byte)((x.R + y.R) / 2), (byte)((x.G + y.G) / 2), (byte)((x.B + y.B) / 2));
}

/// <summary>WCAG contrast between two colours.</summary>
public static class Contrast
{
    public static double Luminance(Rgb c)
    {
        static double Ch(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }

    public static double Ratio(Rgb a, Rgb b)
    {
        var la = Luminance(a); var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>The colour nudged towards white (on a dark background) or black (on a light one) until it reaches <paramref name="minRatio"/>.</summary>
    public static Rgb Ensure(Rgb color, Rgb background, double minRatio)
    {
        if (Ratio(color, background) >= minRatio) return color;
        var towards = Luminance(background) < 0.5 ? new Rgb(255, 255, 255) : new Rgb(0, 0, 0);
        for (var step = 1; step <= 20; step++)
        {
            var t = step / 20.0;
            var c = new Rgb(Lerp(color.R, towards.R, t), Lerp(color.G, towards.G, t), Lerp(color.B, towards.B, t));
            if (Ratio(c, background) >= minRatio) return c;
        }
        return towards;
    }

    private static byte Lerp(byte from, byte to, double t) => (byte)Math.Round(from + (to - from) * t);
}
