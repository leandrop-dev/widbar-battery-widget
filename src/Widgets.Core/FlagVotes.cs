namespace Widgets.Core;

/// <summary>
/// A yes/no choice that each placed copy of a widget makes for something they share, such as whether to read a device
/// over Bluetooth. It is on while any copy wants it. Safe to call from different threads.
/// </summary>
public sealed class FlagVotes
{
    private readonly object _gate = new();
    private readonly HashSet<string> _wanting = new(StringComparer.Ordinal);

    /// <summary>
    /// Records what <paramref name="instanceId"/> wants. False withdraws its vote. Returns true when this changed the
    /// overall answer (<see cref="Any"/>), so a caller can react to the switch turning on or off and not to every repeat.
    /// </summary>
    public bool Set(string instanceId, bool wanted)
    {
        lock (_gate)
        {
            var before = _wanting.Count > 0;
            if (wanted) _wanting.Add(instanceId);
            else _wanting.Remove(instanceId);
            return before != _wanting.Count > 0;
        }
    }

    /// <summary>Withdraws a copy's vote, for example when it is removed from the taskbar.</summary>
    public void Remove(string instanceId)
    {
        lock (_gate) _wanting.Remove(instanceId);
    }

    /// <summary>True while at least one copy wants it.</summary>
    public bool Any
    {
        get { lock (_gate) return _wanting.Count > 0; }
    }
}
