using System.Text;
using Widgets.Providers.Battery.Hid;

namespace Widgets.Providers.Battery;

/// <summary>One kind of device Windows reports, with the interfaces it exposes folded into one line.</summary>
public sealed record ReportedDevice(int Vid, int Pid, string Product, IReadOnlyList<int> UsagePages, bool Supported);

/// <summary>
/// The report that goes into a "request support for my device" issue: which HID devices Windows sees, by vendor and
/// product id, and which of them the widget already reads. It holds ids and product names only, never a device path, a
/// serial number or a Bluetooth address. Pure, so the wording and the size limit are tested without any UI.
/// </summary>
public static class DeviceReport
{
    public const string IssueBase = "https://github.com/leandrop-dev/widbar-battery-widget/issues/new";

    /// <summary>Browsers and GitHub refuse very long addresses, so the prefilled text is cut to fit under this.</summary>
    public const int MaxUrlLength = 6000;

    /// <summary>
    /// One entry per vendor and product id, in id order. Interfaces without a product name are left out: they are internal
    /// parts such as a root hub or a touch panel, and tell a maintainer nothing.
    /// </summary>
    public static IReadOnlyList<ReportedDevice> Group(IEnumerable<HidInfo> infos) =>
    [
        .. infos
            .Where(i => i.Product.Trim().Length > 0)
            .GroupBy(i => (i.Vid, i.Pid))
            .OrderBy(g => g.Key.Vid).ThenBy(g => g.Key.Pid)
            .Select(g => new ReportedDevice(
                g.Key.Vid, g.Key.Pid,
                g.Select(i => i.Product.Trim()).First(),
                [.. g.Select(i => i.UsagePage).Distinct().Order()],
                SteelSeriesReader.Supports(g.Key.Vid, g.Key.Pid) || DualSenseReader.Supports(g.Key.Vid, g.Key.Pid))),
    ];

    /// <summary>The plain-text report the issue form and the clipboard get.</summary>
    public static string Text(IReadOnlyList<ReportedDevice> devices, string widgetVersion, string windowsVersion)
    {
        var text = new StringBuilder();
        text.AppendLine($"Widget: {widgetVersion}");
        text.AppendLine($"Windows: {windowsVersion}");
        text.AppendLine("HID devices Windows reports (vendor:product, name, usage pages):");
        if (devices.Count == 0) text.AppendLine("(none found)");
        foreach (var d in devices) text.AppendLine(Line(d));
        return text.ToString().TrimEnd();
    }

    /// <summary>The address of the issue form with the report filled in. A report too long for an address is cut line by line.</summary>
    public static Uri IssueUri(string report)
    {
        const string Form = IssueBase + "?template=device-request.yml&title=Device%20request%3A%20";
        var lines = report.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        for (var cut = 0; cut <= lines.Count; cut++)
        {
            var body = cut == 0
                ? report
                : string.Join('\n', lines.Take(lines.Count - cut)) + $"\n(and {cut} more lines: use Copy report and paste it here)";
            var url = $"{Form}&devices={Uri.EscapeDataString(body)}";
            if (url.Length <= MaxUrlLength) return new Uri(url);
        }
        return new Uri(Form);
    }

    private static string Line(ReportedDevice d) =>
        $"{d.Vid:X4}:{d.Pid:X4}  {d.Product}  [{string.Join(", ", d.UsagePages.Select(p => p.ToString("X4")))}]{(d.Supported ? "  supported" : "")}";
}
