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

/// <summary>A throwaway folder, deleted on dispose. <see cref="File_"/> is a file inside a sub folder that does not exist yet.</summary>
internal sealed class TempPath : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "bc-" + Guid.NewGuid().ToString("N"));
    public string File_ => Path.Combine(Dir, "sub", "battery-cache.json");
    public void Dispose() { try { Directory.Delete(Dir, true); } catch (IOException) { } }
}
