namespace Widgets.Providers.Battery;

/// <summary>
/// Whether any reader knows a USB vendor and product id. One place for the "request support" report to ask, so a new
/// reader is added to the list here and nowhere else.
/// </summary>
public static class SupportedDevices
{
    private static readonly Func<int, int, bool>[] Readers =
    [
        SteelSeriesReader.Supports,
        DualSenseReader.Supports,
    ];

    public static bool Supports(int vendorId, int productId) => Readers.Any(supports => supports(vendorId, productId));
}
