namespace Widgets.Core.Tests;

public class IntervalVotesTests
{
    private static readonly TimeSpan Fallback = TimeSpan.FromSeconds(60);

    [Fact]
    public void NoVotes_UsesTheFallback() => Assert.Equal(Fallback, new IntervalVotes().Effective(Fallback));

    [Fact]
    public void TwoVotes_TheSmallerWins()
    {
        var v = new IntervalVotes();
        v.Set("a", TimeSpan.FromMinutes(5));
        v.Set("b", TimeSpan.FromMinutes(2));
        Assert.Equal(TimeSpan.FromMinutes(2), v.Effective(Fallback));
    }

    [Fact]
    public void RemovingTheSmallerVote_LeavesTheLarger()
    {
        var v = new IntervalVotes();
        v.Set("a", TimeSpan.FromMinutes(5));
        v.Set("b", TimeSpan.FromMinutes(2));
        v.Remove("b");
        Assert.Equal(TimeSpan.FromMinutes(5), v.Effective(Fallback));
    }

    [Fact]
    public void SettingTheSameIdAgain_ReplacesItsVote()
    {
        var v = new IntervalVotes();
        v.Set("a", TimeSpan.FromMinutes(2));
        v.Set("a", TimeSpan.FromMinutes(8));
        Assert.Equal(TimeSpan.FromMinutes(8), v.Effective(Fallback));
    }

    [Fact]
    public void RemovingAnUnknownId_ChangesNothing()
    {
        var v = new IntervalVotes();
        v.Set("a", TimeSpan.FromMinutes(3));
        v.Remove("zzz");
        Assert.Equal(TimeSpan.FromMinutes(3), v.Effective(Fallback));
    }

    [Fact]
    public void RemovingTheLastVote_GoesBackToTheFallback()
    {
        var v = new IntervalVotes();
        v.Set("a", TimeSpan.FromMinutes(3));
        v.Remove("a");
        Assert.Equal(Fallback, v.Effective(Fallback));
    }

    [Fact]
    public async Task ManyThreadsVotingAtOnce_DoNotCorruptTheResult()
    {
        var v = new IntervalVotes();
        await Task.WhenAll(Enumerable.Range(1, 50).Select(i => Task.Run(() => v.Set($"i{i}", TimeSpan.FromMinutes(i)))));
        Assert.Equal(TimeSpan.FromMinutes(1), v.Effective(Fallback));
    }
}
