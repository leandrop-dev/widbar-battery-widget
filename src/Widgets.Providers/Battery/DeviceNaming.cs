using System.Text;
using System.Text.RegularExpressions;

namespace Widgets.Providers.Battery;

/// <summary>
/// Cleaning of names a device reports about itself, and a guess of its kind from the name when the device does not say.
/// Ported from taskbar_battery_widget/naming.py of Gabrielfch/taskbar-battery-widget (MIT, see THIRD_PARTY_NOTICES.md),
/// keeping its keyword lists for the kinds this widget has.
/// </summary>
internal static class DeviceNaming
{
    /// <summary>A device's own name is untrusted: printable characters only, no markup characters, at most 48 long.</summary>
    public static string Sanitize(string? name, int maxLength = 48)
    {
        var kept = new StringBuilder();
        foreach (var ch in name ?? "")
            if (!char.IsControl(ch) && (ch == ' ' || !char.IsWhiteSpace(ch)) && ch is not ('<' or '>' or '&'))
                kept.Append(ch);
        var collapsed = Regex.Replace(kept.ToString(), @"\s{2,}", " ").Trim();
        return (collapsed.Length > maxLength ? collapsed[..maxLength] : collapsed).TrimEnd();
    }

    // Kind, substring keywords, whole-word keywords. The order is the priority.
    private static readonly (DeviceKind Kind, string[] Substrings, string[] Words)[] Keywords =
    [
        (DeviceKind.Gamepad, ["controller", "gamepad", "xbox", "dualsense", "dualshock", "joy-con", "joycon", "mando", "gamesir", "8bitdo"], []),
        (DeviceKind.Keyboard, ["keyboard", "teclado"], ["keys", "k380", "k780"]),
        (DeviceKind.Mouse, ["mouse", "ratón", "raton", "trackball", "mx master", "mx anywhere", "trackpad"], []),
        (DeviceKind.Headset, ["headset", "headphone", "earbud", "earphone", "airpods", "auricular", "casco", "wh-1000", "wf-1000", "kraken", "barracuda", "arctis"], ["buds"]),
    ];

    /// <summary>The kind a name suggests, or <see cref="DeviceKind.Other"/>.</summary>
    public static DeviceKind KindFromName(string? name)
    {
        var n = (name ?? "").ToLowerInvariant();
        foreach (var (kind, substrings, words) in Keywords)
        {
            if (substrings.Any(n.Contains)) return kind;
            if (words.Any(w => Regex.IsMatch(n, $"(?<![a-z0-9]){Regex.Escape(w)}(?![a-z0-9])"))) return kind;
        }
        return DeviceKind.Other;
    }
}
