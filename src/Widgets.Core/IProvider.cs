namespace Widgets.Core;

/// <summary>
/// A source of metrics. Same contract as the reference project's vendor readers:
/// never throws (log and return what is available), respects cancellation, and finishes within a bounded time.
/// </summary>
public interface IProvider
{
    string Id { get; }

    /// <summary>How often the scheduler should call <see cref="PollAsync"/>.</summary>
    TimeSpan Interval { get; }

    Task<IReadOnlyList<Metric>> PollAsync(CancellationToken cancellationToken);
}
