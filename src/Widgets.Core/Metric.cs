namespace Widgets.Core;

public enum MetricKind
{
    Battery,
    Temperature,
    Percent,
    Tokens,
    Cost,
}

/// <summary>One value shown as a tile on the taskbar widget.</summary>
/// <param name="Id">Stable identifier, unique across providers (for example "system:ram").</param>
/// <param name="Label">Short display label.</param>
/// <param name="Kind">What the value measures.</param>
/// <param name="Value">Current value, or null when no reading is available.</param>
/// <param name="Unit">Display unit, for example "%" or "°C".</param>
/// <param name="Source">Id of the provider that produced it.</param>
/// <param name="Updated">When the reading was taken, when known.</param>
/// <param name="Stale">True when <paramref name="Value"/> is the last known reading, not a live one.</param>
/// <param name="Approximate">True when the value is an estimate (shown with a leading "~").</param>
/// <param name="Charging">True when a battery is charging right now (shown with a bolt).</param>
/// <param name="Text">Short text shown instead of the value (for example "USB" for a wired pad with no battery).</param>
/// <param name="Icon">Which icon to draw (see <see cref="MetricIcons"/>), or null for none.</param>
/// <param name="OfflineSince">For a device that is not connected right now: when it was last connected. Null while it is connected.</param>
/// <param name="Superseded">True when another device of the same kind is connected and this one steps aside (see <see cref="DevicePresence"/>).</param>
public sealed record Metric(
    string Id,
    string Label,
    MetricKind Kind,
    double? Value,
    string Unit,
    string Source,
    DateTimeOffset? Updated = null,
    bool Stale = false,
    bool Approximate = false,
    bool Charging = false,
    string? Text = null,
    string? Icon = null,
    DateTimeOffset? OfflineSince = null,
    bool Superseded = false);

/// <summary>Names of the icons a tile can carry. The app draws each one in the chosen icon style.</summary>
public static class MetricIcons
{
    public const string Cpu = "cpu", Gpu = "gpu", Ram = "ram", Thermometer = "thermo";
    public const string Headset = "headset", Mouse = "mouse", Keyboard = "keyboard", Gamepad = "gamepad", Generic = "generic";
}
