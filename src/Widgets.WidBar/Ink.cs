using Microsoft.UI.Xaml.Media;
using Widgets.Core.Appearance;

namespace Widgets.WidBar;

/// <summary>
/// Text and accent colours for the taskbar's theme. They are fixed colours chosen by the theme flag, not application
/// resources: the preview follows the taskbar theme, which the user can set apart from the apps theme.
/// </summary>
internal readonly record struct Ink(bool Dark)
{
    public static Brush Solid(Rgb c, double opacity = 1) =>
        new SolidColorBrush(Windows.UI.Color.FromArgb((byte)Math.Round(opacity * 255), c.R, c.G, c.B));

    /// <summary>The colour of the taskbar behind the widget, used to keep battery colours readable.</summary>
    public Rgb Bar => Dark ? new Rgb(32, 32, 32) : new Rgb(243, 243, 243);

    /// <summary>The colour of plain text, as a colour, for the code that decides colours without drawing.</summary>
    public Rgb NeutralRgb => Dark ? new Rgb(255, 255, 255) : new Rgb(20, 20, 20);

    public Brush Primary => Solid(NeutralRgb);
    public Brush Secondary => Solid(Dark ? new Rgb(200, 200, 200) : new Rgb(74, 74, 74));
    public Brush Success => Solid(Dark ? new Rgb(108, 203, 95) : new Rgb(15, 123, 15));
    /// <summary>White on a dark taskbar, black on a light one, for hairlines and tracks drawn at low opacity.</summary>
    public static Rgb Neutral(bool dark) => dark ? new Rgb(255, 255, 255) : new Rgb(0, 0, 0);

    public Brush Track => Solid(Neutral(Dark), 0.14);
}
