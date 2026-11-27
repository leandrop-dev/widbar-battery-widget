using Widgets.Providers.Battery.Hid;

namespace Widgets.Providers.Battery.Logitech;

/// <summary>What is remembered about one receiver slot or direct device between polls, so a poll after the first asks less.</summary>
internal sealed class SlotInfo
{
    public string? Name;
    public int? Type;
    public DateTimeOffset? ProbedAt;
    public (int FeatureId, int Index)? Battery;
    public int BatteryFailures;
}

internal enum PingState { Ok, Absent, Timeout, Hidpp1 }

/// <summary>
/// HID++ requests and replies over the short and/or long collection of one interface. Every request is checked against the
/// read-only list in <see cref="Hidpp"/> before it is written. The token bounds every wait, so a scan can be abandoned from
/// outside; <see cref="Broken"/> is set after a failed read or write.
/// </summary>
internal sealed class HidppChannel : IDisposable
{
    private readonly IHidHandle? _short;
    private readonly IHidHandle? _long;
    private int _sw;

    public bool Broken { get; private set; }
    public int? LastError { get; private set; }
    public string? LastErrorKind { get; private set; }

    public HidppChannel(IHidSystem hid, HidppGroup group)
    {
        try
        {
            if (group.Short is not null) _short = hid.Open(group.Short);
            if (group.Long is not null) _long = hid.Open(group.Long);
        }
        catch
        {
            Dispose();
            throw;
        }
        if (_short is null && _long is null) throw new IOException("No HID++ collection.");
    }

    public void Dispose()
    {
        _short?.Dispose();
        _long?.Dispose();
    }

    // Rotates 1 to 15, so a late reply to an earlier request never matches the current one.
    private int NextSw() => _sw = _sw % 15 + 1;

    /// <summary>One HID++ 2.0 query: its matching reply (a success or an error), or null on a timeout, a cancelled wait or a broken channel.</summary>
    public Task<HidppReply?> RequestAsync(int deviceIndex, int featureIndex, int function, byte[] prms, TimeSpan timeout, int featureId, CancellationToken ct)
    {
        var useLong = _long is not null;
        var sw = NextSw();
        var packet = Hidpp.BuildGuardedRequest(deviceIndex, featureIndex, featureId, function, prms, sw, useLong); // throws before anything is sent
        return ExchangeAsync(useLong ? _long : _short, packet, timeout, r => Hidpp.ReplyMatches(r, deviceIndex, featureIndex, function, sw), ct);
    }

    private async Task<HidppReply?> RequestRegisterAsync(int deviceIndex, int register, TimeSpan timeout, CancellationToken ct)
    {
        var packet = Hidpp.BuildHidpp1GetRegister(deviceIndex, register); // throws before anything is sent
        return await ExchangeAsync(_short, packet, timeout, r => Hidpp.Hidpp1ReplyMatches(r, deviceIndex, register), ct).ConfigureAwait(false);
    }

    private async Task<HidppReply?> ExchangeAsync(IHidHandle? write, byte[] packet, TimeSpan timeout, Func<HidppReply, bool> matches, CancellationToken ct)
    {
        if (Broken || write is null || ct.IsCancellationRequested) return null;
        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        window.CancelAfter(timeout);
        try
        {
            foreach (var h in new[] { _short, _long })
                if (h is not null) await h.DrainAsync().ConfigureAwait(false);

            using (var sending = CancellationTokenSource.CreateLinkedTokenSource(window.Token))
            {
                sending.CancelAfter(TimeSpan.FromSeconds(1));
                if (!await write.WriteAsync(packet, sending.Token).ConfigureAwait(false)) return null;
            }

            var slice = TimeSpan.FromMilliseconds(_short is not null && _long is not null ? 20 : 100);
            while (!window.IsCancellationRequested)
            {
                foreach (var h in new[] { _long, _short })
                {
                    if (h is null) continue;
                    using var one = CancellationTokenSource.CreateLinkedTokenSource(window.Token);
                    one.CancelAfter(slice);
                    var reply = Hidpp.ParseReport(await h.ReadAsync(one.Token).ConfigureAwait(false));
                    if (reply is not null && matches(reply)) return reply;
                }
                await Task.Delay(1, window.Token).ConfigureAwait(false);
            }
            return null;
        }
        catch (OperationCanceledException) { return null; }
        catch (IOException) { Broken = true; return null; }
    }

    // ---- typed helpers

    public async Task<int?> GetFeatureIndexAsync(int deviceIndex, int featureId, TimeSpan timeout, CancellationToken ct)
    {
        var reply = await RequestAsync(deviceIndex, 0x00, 0, [(byte)(featureId >> 8), (byte)(featureId & 0xFF)], timeout, Hidpp.FRoot, ct).ConfigureAwait(false);
        return reply is null || reply.Error is not null ? null : Hidpp.DecodeGetFeature(reply.Params);
    }

    public async Task<PingState> PingAsync(int deviceIndex, TimeSpan timeout, CancellationToken ct)
    {
        var reply = await RequestAsync(deviceIndex, 0x00, 1, [0x00, 0x00, 0xA5], timeout, Hidpp.FRoot, ct).ConfigureAwait(false);
        if (reply is null) return PingState.Timeout;
        LastError = reply.Error;
        LastErrorKind = reply.ErrorKind;
        if (reply.Error is not null) return Hidpp.IsNotConnected(reply) ? PingState.Absent : PingState.Hidpp1;
        return Hidpp.DecodePing(reply.Params) is null ? PingState.Timeout : PingState.Ok;
    }

    public async Task<string?> ReadNameAsync(int deviceIndex, TimeSpan timeout, CancellationToken ct)
    {
        if (await GetFeatureIndexAsync(deviceIndex, Hidpp.FDeviceName, timeout, ct).ConfigureAwait(false) is not { } idx) return null;
        var count = await RequestAsync(deviceIndex, idx, 0, [], timeout, Hidpp.FDeviceName, ct).ConfigureAwait(false);
        if (count is null || count.Error is not null) return null;

        var length = Hidpp.DecodeNameLength(count.Params);
        var chunks = new List<byte[]>();
        var got = 0;
        while (got < length && chunks.Count < 8)
        {
            var chunk = await RequestAsync(deviceIndex, idx, 1, [(byte)got], timeout, Hidpp.FDeviceName, ct).ConfigureAwait(false);
            if (chunk is null || chunk.Error is not null) break;
            var text = chunk.Params.TakeWhile(b => b != 0).ToArray();
            if (text.Length == 0) break;
            chunks.Add(text);
            got += text.Length;
        }
        return DeviceNaming.Sanitize(Hidpp.AssembleName(chunks, length)) is { Length: > 0 } name ? name : null;
    }

    public async Task<int?> ReadTypeAsync(int deviceIndex, TimeSpan timeout, CancellationToken ct)
    {
        if (await GetFeatureIndexAsync(deviceIndex, Hidpp.FDeviceName, timeout, ct).ConfigureAwait(false) is not { } idx) return null;
        var reply = await RequestAsync(deviceIndex, idx, 2, [], timeout, Hidpp.FDeviceName, ct).ConfigureAwait(false);
        return reply is null || reply.Error is not null || reply.Params.Length == 0 ? null : reply.Params[0];
    }

    /// <summary>The battery of a HID++ 1.0 device: register 0x0D (a percentage), else 0x07 (a coarse level).</summary>
    public async Task<(int Percent, bool Charging)?> ReadBatteryHidpp1Async(int deviceIndex, TimeSpan timeout, CancellationToken ct)
    {
        foreach (var (register, decode) in new (int, Func<byte[], (int, bool)?>)[]
                 { (Hidpp.Hidpp1RegBatteryCharge, Hidpp.DecodeHidpp1Charge), (Hidpp.Hidpp1RegBatteryStatus, Hidpp.DecodeHidpp1Status) })
        {
            var reply = await RequestRegisterAsync(deviceIndex, register, timeout, ct).ConfigureAwait(false);
            if (reply is null || reply.Error is not null) continue;
            if (decode(reply.Params) is { } result) return result;
        }
        return null;
    }

    /// <summary>
    /// The battery of a HID++ 2.0 device. <paramref name="known"/> remembers which battery feature worked: its index can change
    /// (a firmware update, another device in the slot), so one cheap getFeature confirms it before it is used again.
    /// </summary>
    public async Task<(int Percent, bool Charging)?> ReadBatteryAsync(int deviceIndex, SlotInfo known, TimeSpan timeout, CancellationToken ct)
    {
        IEnumerable<int> order = Hidpp.BatteryFeatures;
        (int FeatureId, int Index)? cached = known.Battery;
        if (cached is { } c)
        {
            if (Hidpp.BatteryReaders.ContainsKey(c.FeatureId) && c.Index is >= 1 and <= Hidpp.MaxFeatureIndex)
            {
                if (await GetFeatureIndexAsync(deviceIndex, c.FeatureId, timeout, ct).ConfigureAwait(false) is { } now)
                {
                    cached = (c.FeatureId, now);
                    order = [c.FeatureId];
                }
                else
                {
                    known.Battery = null;
                    cached = null;
                }
            }
            else
            {
                known.Battery = null; // a corrupt entry
                cached = null;
            }
        }

        foreach (var featureId in order)
        {
            int idx;
            if (cached is { } hit) idx = hit.Index;
            else if (await GetFeatureIndexAsync(deviceIndex, featureId, timeout, ct).ConfigureAwait(false) is { } found) idx = found;
            else continue;

            var (function, decode) = Hidpp.BatteryReaders[featureId];
            var reply = await RequestAsync(deviceIndex, idx, function, [], timeout, featureId, ct).ConfigureAwait(false);
            if (reply is null || reply.Error is not null) continue;
            if (decode(reply.Params) is { } result)
            {
                known.Battery = (featureId, idx);
                return result;
            }
        }
        known.Battery = null;
        return null;
    }
}
