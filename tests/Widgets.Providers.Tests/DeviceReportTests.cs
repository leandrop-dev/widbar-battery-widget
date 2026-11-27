using Widgets.Providers.Battery;
using Widgets.Providers.Battery.Hid;

namespace Widgets.Providers.Tests;

public class DeviceReportTests
{
    // Paths and serials are what must never reach the report; the markers make that visible in the assertions.
    private const string SecretPath = @"\\?\hid#vid_1038&pid_22a1&mi_04#SERIAL-1234-SECRET#{guid}";

    private static HidInfo Hid(int vid, int pid, string product, int usagePage = 0x01, string path = SecretPath) =>
        new(path, vid, pid, usagePage, 5, 64, 64, product);

    [Fact]
    public void InterfacesOfOneDeviceBecomeOneEntryWithAllItsUsagePages()
    {
        var devices = DeviceReport.Group([
            Hid(0x1038, 0x22A1, "SteelSeries Arctis Nova 7", usagePage: 0xFFC0),
            Hid(0x1038, 0x22A1, "SteelSeries Arctis Nova 7", usagePage: 0x000C),
            Hid(0x1038, 0x22A1, "SteelSeries Arctis Nova 7", usagePage: 0xFFC0),
        ]);
        var d = Assert.Single(devices);
        Assert.Equal("SteelSeries Arctis Nova 7", d.Product);
        Assert.Equal([0x000C, 0xFFC0], d.UsagePages);
    }

    [Fact]
    public void EntriesWithNoProductNameAreLeftOutAndTheRestAreInIdOrder()
    {
        var devices = DeviceReport.Group([
            Hid(0x054C, 0x0CE6, "DualSense Wireless Controller"),
            Hid(0x0000, 0x0001, ""),
            Hid(0x0000, 0x0002, "   "),
            Hid(0x046D, 0xC52B, "USB Receiver"),
        ]);
        Assert.Equal([(0x046D, 0xC52B), (0x054C, 0x0CE6)], devices.Select(d => (d.Vid, d.Pid)));
    }

    [Fact]
    public void ADeviceTheWidgetReadsIsMarkedSupportedAndAnotherIsNot()
    {
        var devices = DeviceReport.Group([
            Hid(0x1038, 0x22A1, "SteelSeries Arctis Nova 7"),   // a Nova 7
            Hid(0x1038, 0x1830, "SteelSeries Rival 3 Wireless"),
            Hid(0x054C, 0x0CE6, "DualSense Wireless Controller"),
            Hid(0x054C, 0x0DF2, "DualSense Edge Wireless Controller"),
            Hid(0x1038, 0x1234, "SteelSeries Something New"),   // SteelSeries, but not one it knows
            Hid(0x046D, 0x22A1, "Not SteelSeries"),             // same product id, other vendor
        ]);
        Assert.Equal(
            ["Not SteelSeries:False", "DualSense Wireless Controller:True", "DualSense Edge Wireless Controller:True",
                "SteelSeries Something New:False", "SteelSeries Rival 3 Wireless:True", "SteelSeries Arctis Nova 7:True"],
            devices.Select(d => $"{d.Product}:{d.Supported}"));
    }

    [Fact]
    public void TheTextHasTheVersionsTheIdsAndTheSupportedMarkerButNoPathOrSerial()
    {
        var text = DeviceReport.Text(DeviceReport.Group([
            Hid(0x1038, 0x22A1, "SteelSeries Arctis Nova 7", usagePage: 0xFFC0),
            Hid(0x046D, 0xC52B, "USB Receiver"),
        ]), "0.5.0", "10.0.26300.0");

        Assert.Contains("Widget: 0.5.0", text);
        Assert.Contains("Windows: 10.0.26300.0", text);
        Assert.Contains("046D:C52B  USB Receiver  [0001]", text);
        Assert.Contains("1038:22A1  SteelSeries Arctis Nova 7  [FFC0]  supported", text);
        Assert.DoesNotContain("SERIAL", text);
        Assert.DoesNotContain(@"\\?\", text);
        Assert.DoesNotContain("hid#", text);
    }

    [Fact]
    public void WithNoDevicesTheTextSaysSo() =>
        Assert.Contains("(none found)", DeviceReport.Text([], "0.5.0", "10.0"));

    [Fact]
    public void TheIssueAddressPointsAtTheFormAndCarriesTheEncodedReport()
    {
        var uri = DeviceReport.IssueUri("Widget: 0.5.0\nHID devices: 046D:C52B  USB Receiver & more");
        Assert.StartsWith(DeviceReport.IssueBase + "?template=device-request.yml", uri.AbsoluteUri);
        Assert.Contains("title=Device%20request%3A%20", uri.AbsoluteUri);
        Assert.Contains("devices=Widget%3A%200.5.0%0AHID%20devices%3A%20046D%3AC52B%20%20USB%20Receiver%20%26%20more", uri.AbsoluteUri);
    }

    [Fact]
    public void ATooLongReportIsCutLineByLineToFitAndSaysHowToGetTheRest()
    {
        var lines = Enumerable.Range(0, 400).Select(i => $"{i:X4}:{i:X4}  Some Quite Long Device Name Number {i}  [0001, FF00]");
        var report = "Widget: 0.5.0\n" + string.Join('\n', lines);

        var uri = DeviceReport.IssueUri(report);
        Assert.True(uri.AbsoluteUri.Length <= DeviceReport.MaxUrlLength, $"{uri.AbsoluteUri.Length} characters");

        var devices = Uri.UnescapeDataString(uri.AbsoluteUri.Split("&devices=")[1]);
        Assert.StartsWith("Widget: 0.5.0\n0000:0000", devices);          // the start is kept
        Assert.Contains("more lines: use Copy report", devices);
        Assert.DoesNotContain("0190:0190", devices);                      // the end is what was cut
    }

    [Fact]
    public void AShortReportIsNotCut()
    {
        var uri = DeviceReport.IssueUri("Widget: 0.5.0\nWindows: 10.0\nHID devices:\n046D:C52B  USB Receiver  [0001]");
        Assert.DoesNotContain("more%20lines", uri.AbsoluteUri);
        Assert.Contains("USB%20Receiver", uri.AbsoluteUri);
    }

    [Fact]
    public void TheFakeSystemsHaveNoListingAndTheRealOneNeverThrows()
    {
        Assert.Empty(((IHidSystem)new NoListingHid()).EnumerateAll());
        _ = new WindowsHid().EnumerateAll(); // whatever this PC has; it only has to return
    }

    private sealed class NoListingHid : IHidSystem
    {
        public IReadOnlyList<HidInfo> Enumerate(int vendorId) => [];
        public IHidHandle Open(HidInfo info) => throw new InvalidOperationException();
    }
}
