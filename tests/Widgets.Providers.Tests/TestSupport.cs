using Widgets.Providers.Battery;

namespace Widgets.Providers.Tests;

/// <summary>A clock the test moves by hand.</summary>
internal sealed class FakeTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>A reader whose answer the test supplies.</summary>
internal sealed class Reader(Func<IReadOnlyList<BatteryReading>> read) : IBatteryReader
{
    public Task<IReadOnlyList<BatteryReading>> ReadAsync(CancellationToken ct) => Task.FromResult(read());
}

/// <summary>
/// A HID interface whose replies the test scripts, and which remembers everything sent to it. A reader that sends a
/// request outside its fixed table shows up in <see cref="Written"/>, <see cref="SetFeatures"/> or <see cref="FeatureRequests"/>.
/// </summary>
internal sealed class RecordingHandle(
    Func<byte[]?>? read = null,
    Func<byte, byte[]?>? onGetFeature = null,
    Func<byte[], bool>? onSetFeature = null,
    Func<byte[], bool>? onWrite = null) : Widgets.Providers.Battery.Hid.IHidHandle
{
    public List<byte[]> Written { get; } = [];
    public List<byte[]> SetFeatures { get; } = [];
    public List<byte> FeatureRequests { get; } = [];
    public int Drains { get; private set; }

    public Task<byte[]?> ReadAsync(CancellationToken ct) => Task.FromResult(read?.Invoke());

    public Task<byte[]?> GetFeatureAsync(byte reportId, CancellationToken ct)
    {
        FeatureRequests.Add(reportId);
        return Task.FromResult(onGetFeature?.Invoke(reportId));
    }

    public Task<bool> SetFeatureAsync(byte[] report, CancellationToken ct)
    {
        SetFeatures.Add(report);
        return Task.FromResult(onSetFeature?.Invoke(report) ?? false);
    }

    public Task<bool> WriteAsync(byte[] report, CancellationToken ct)
    {
        Written.Add(report);
        return Task.FromResult(onWrite?.Invoke(report) ?? false);
    }

    public Task DrainAsync() { Drains++; return Task.CompletedTask; }
    public void Dispose() { }
}

/// <summary>A HID system with a fixed set of interfaces, each backed by a handle, remembering which were opened.</summary>
internal sealed class RecordingHidSystem(
    IReadOnlyList<Widgets.Providers.Battery.Hid.HidInfo> infos,
    params (string Path, Widgets.Providers.Battery.Hid.IHidHandle Handle)[] handles) : Widgets.Providers.Battery.Hid.IHidSystem
{
    public List<string> Opened { get; } = [];

    public IReadOnlyList<Widgets.Providers.Battery.Hid.HidInfo> Enumerate(int vendorId) => [.. infos.Where(i => i.Vid == vendorId)];
    public IReadOnlyList<Widgets.Providers.Battery.Hid.HidInfo> EnumerateAll() => infos;

    public Widgets.Providers.Battery.Hid.IHidHandle Open(Widgets.Providers.Battery.Hid.HidInfo info)
    {
        Opened.Add(info.Path);
        return handles.First(h => h.Path == info.Path).Handle;
    }
}

/// <summary>A throwaway folder, deleted on dispose. <see cref="File_"/> is a file inside a sub folder that does not exist yet.</summary>
internal sealed class TempPath : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "bc-" + Guid.NewGuid().ToString("N"));
    public string File_ => Path.Combine(Dir, "sub", "battery-cache.json");
    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }
}
