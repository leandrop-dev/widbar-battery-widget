using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace Widgets.Providers.Battery;

/// <summary>A device's Battery Service as read from the Bluetooth stack.</summary>
public sealed record GattBattery(string DeviceId, string Name, bool Connected, int? Percent);

/// <summary>
/// Battery of paired Bluetooth LE devices that expose the standard Battery Service (GATT 0x180F, level
/// characteristic 0x2A19). The Windows Bluetooth stack does the talking; nothing is written to the device.
/// </summary>
public sealed class BluetoothLeBatteryReader : IBatteryReader
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<GattBattery>>> _source;

    public BluetoothLeBatteryReader() : this(ReadFromWindowsAsync) { }

    /// <summary>For tests: replaces the Windows Bluetooth stack.</summary>
    public BluetoothLeBatteryReader(Func<CancellationToken, Task<IReadOnlyList<GattBattery>>> source) => _source = source;

    public async Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct)
    {
        var result = new List<BatteryReading>();
        foreach (var b in await _source(ct).ConfigureAwait(false))
        {
            var name = string.IsNullOrWhiteSpace(b.Name) ? "Bluetooth device" : b.Name.Trim();
            // A device that is not connected cannot answer: report it as off rather than as an old value.
            var percent = b.Connected && b.Percent is >= 0 and <= 100 ? b.Percent : null;
            result.Add(new BatteryReading($"ble:{b.DeviceId}", name, DeviceKind.Other, percent, false, b.Connected));
        }
        return result;
    }

    private static async Task<IReadOnlyList<GattBattery>> ReadFromWindowsAsync(CancellationToken ct)
    {
        var found = new List<GattBattery>();
        try
        {
            var selector = GattDeviceService.GetDeviceSelectorFromUuid(GattServiceUuids.Battery);
            var services = await DeviceInformation.FindAllAsync(selector).AsTask(ct).ConfigureAwait(false);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var svcInfo in services)
            {
                ct.ThrowIfCancellationRequested();
                using var service = await GattDeviceService.FromIdAsync(svcInfo.Id).AsTask(ct).ConfigureAwait(false);
                if (service is null || !seen.Add(service.DeviceId)) continue;

                using var device = await BluetoothLEDevice.FromIdAsync(service.DeviceId).AsTask(ct).ConfigureAwait(false);
                if (device is null) continue;

                var connected = device.ConnectionStatus == BluetoothConnectionStatus.Connected;
                int? percent = null;
                if (connected)
                {
                    var chars = await service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel,
                        BluetoothCacheMode.Uncached).AsTask(ct).ConfigureAwait(false);
                    if (chars.Status == GattCommunicationStatus.Success && chars.Characteristics.Count > 0)
                    {
                        var value = await chars.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(ct).ConfigureAwait(false);
                        if (value.Status == GattCommunicationStatus.Success && value.Value.Length >= 1)
                            percent = DataReader.FromBuffer(value.Value).ReadByte();
                    }
                }
                found.Add(new GattBattery(service.DeviceId, device.Name, connected, percent));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Bluetooth off, no adapter, or access denied: there is simply nothing to show.
        }
        return found;
    }
}
