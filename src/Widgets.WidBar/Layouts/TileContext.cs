using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Widgets.Core;
using Widgets.Core.Appearance;
using Widgets.Core.Settings;

namespace Widgets.WidBar.Layouts;

/// <summary>Everything a layout needs to draw the taskbar preview: the settings, the taskbar's theme and the room it has.</summary>
/// <param name="SlotHeight">How tall the preview's slot is: the taskbar's height, or about 26 with small taskbar buttons.</param>
/// <param name="WindowsAccent">The Windows accent colour, or null when it could not be read.</param>
internal sealed record TileContext(BatteryWidgetSettings Settings, bool Dark, double SlotHeight, Rgb? WindowsAccent)
{
    public Ink Ink => new(Dark);
    public AppearanceSettings Look => Settings.Appearance;

    /// <summary>The "Value size" setting as a factor: 0.9, 1.0, 1.15 or 1.3.</summary>
    public double Scale => Look.Scale / 100.0;

    /// <summary>The user's Windows accent colour. Read on demand, so a change in Windows settings shows on the next redraw.</summary>
    public static Rgb? ReadWindowsAccent()
    {
        try
        {
            var c = new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            return new Rgb(c.R, c.G, c.B);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>One way of drawing the shown devices on the taskbar. The layouts are those of WidBar's System Metrics widget.</summary>
internal interface ITileLayout
{
    FrameworkElement Build(IReadOnlyList<Metric> shown, TileContext context);
}
