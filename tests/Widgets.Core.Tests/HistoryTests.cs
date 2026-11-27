using Widgets.Core;

namespace Widgets.Core.Tests;

public class DischargeEstimateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static HistorySample At(double hoursAgo, int percent, bool charging = false) =>
        new(Now.AddHours(-hoursAgo), percent, charging);

    /// <summary>One reading an hour, falling one point an hour, newest 'now'.</summary>
    private static List<HistorySample> Steady(int hours, int endPercent, int pointsPerHour = 1) =>
        [.. Enumerable.Range(0, hours + 1).Select(i => At(hours - i, endPercent + (hours - i) * pointsPerHour))];

    [Fact]
    public void ASteadyDrainGivesTheTimeLeftAtThatRate()
    {
        // 30% now, losing 2 points an hour: 15 hours left.
        var left = DischargeEstimate.TimeLeft(Steady(8, 30, pointsPerHour: 2), Now)!.Value;
        Assert.InRange(left.TotalHours, 14.5, 15.5);
        Assert.Equal("about 15 h", DischargeEstimate.Describe(left));
    }

    [Fact]
    public void AWobbleAtTheEndDoesNotSwingTheAnswer()
    {
        // A wobble at the end (one reading a point off) does not swing the answer.
        var samples = Steady(10, 40);
        samples[^1] = At(0, 39);
        var left = DischargeEstimate.TimeLeft(samples, Now)!.Value;
        Assert.InRange(left.TotalHours, 35, 42);
    }

    [Fact]
    public void ChargingAtTheEndOrNoFreshReadingGivesNothing()
    {
        Assert.Null(DischargeEstimate.TimeLeft([.. Steady(6, 50).SkipLast(1), At(0, 49, charging: true)], Now));

        var old = Steady(6, 50).Select(s => s with { At = s.At.AddHours(-3) }).ToList(); // newest reading is 3 h old
        Assert.Null(DischargeEstimate.TimeLeft(old, Now));
    }

    [Fact]
    public void OnlyTheLatestUnbrokenDischargeCounts()
    {
        // Drained from 90 to 60, charged back up, then drained slowly from 80 to 70 over ten hours.
        List<HistorySample> samples =
        [
            At(30, 90), At(25, 75), At(20, 60),
            At(18, 62, charging: true), At(16, 80, charging: true),
            .. Enumerable.Range(0, 11).Select(i => At(10 - i, 70 + (10 - i))),
        ];
        var left = DischargeEstimate.TimeLeft(samples, Now)!.Value;
        Assert.InRange(left.TotalHours, 65, 75); // 70% at one point an hour, not the fast early drain
    }

    [Fact]
    public void ABatterySwapEndsTheRunEvenWithoutAChargingFlag()
    {
        // The mouse went 30, 29, 28, then someone fitted new batteries: 100, 99, 98 ...
        List<HistorySample> samples = [At(12, 30), At(11, 29), At(10, 28), .. Enumerable.Range(0, 9).Select(i => At(8 - i, 92 + (8 - i)))];
        var left = DischargeEstimate.TimeLeft(samples, Now)!.Value;
        Assert.InRange(left.TotalHours, 90, 100);
    }

    [Fact]
    public void NotEnoughSpanLevelsOrDropGivesNothing()
    {
        Assert.Null(DischargeEstimate.TimeLeft([At(0.5, 80), At(0.25, 79), At(0.1, 78), At(0, 77), At(0, 76)], Now)); // under an hour
        Assert.Null(DischargeEstimate.TimeLeft([At(3, 80), At(2, 79), At(1, 78), At(0, 77)], Now));                  // only 4 levels
        Assert.Null(DischargeEstimate.TimeLeft([At(3, 80), At(2, 80), At(1, 79), At(0.5, 78), At(0, 77), At(0, 77)], Now)); // dropped 3, not 4
        Assert.Null(DischargeEstimate.TimeLeft([At(0, 50)], Now));
        Assert.Null(DischargeEstimate.TimeLeft([], Now));
    }

    [Fact]
    public void XInputsFourCoarseStepsNeverGiveAnEstimate()
    {
        List<HistorySample> samples = [At(40, 100), At(30, 65), At(20, 30), At(1, 5), At(0, 5)];
        Assert.Null(DischargeEstimate.TimeLeft(samples, Now));
    }

    [Fact]
    public void AFlatLineGivesNothingAndAnAbsurdForecastIsNotShown()
    {
        Assert.Null(DischargeEstimate.TimeLeft([.. Enumerable.Range(0, 10).Select(i => At(10 - i, 50))], Now));

        // Five points over 100 hours from 100%: about 2000 hours left, far beyond a forecast.
        List<HistorySample> slow = [At(100, 100), At(80, 99), At(60, 98), At(40, 97), At(20, 96), At(0, 95)];
        Assert.Null(DischargeEstimate.TimeLeft(slow, Now));
    }

    [Fact]
    public void ReadingsGivenOutOfOrderAreSortedFirst()
    {
        var shuffled = Steady(8, 30, pointsPerHour: 2).OrderBy(_ => Guid.NewGuid()).ToList();
        Assert.InRange(DischargeEstimate.TimeLeft(shuffled, Now)!.Value.TotalHours, 14.5, 15.5);
    }

    [Theory]
    [InlineData(0.2, "under 1 h")]
    [InlineData(1, "about 1 h")]
    [InlineData(5.6, "about 6 h")]
    [InlineData(47, "about 47 h")]
    [InlineData(48, "about 2 d")]
    [InlineData(100, "about 4 d")]
    public void DescribeUsesHoursThenDays(double hours, string expected) =>
        Assert.Equal(expected, DischargeEstimate.Describe(TimeSpan.FromHours(hours)));
}

public class SparklineTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    private static HistorySample At(double hoursAgo, int percent, bool charging = false) =>
        new(Now.AddHours(-hoursAgo), percent, charging);

    [Fact]
    public void NoReadingsMeansNoLine()
    {
        var segments = Sparkline.Segments([], Now, Day);
        Assert.Empty(segments);
        Assert.False(Sparkline.HasLine(segments));
    }

    [Fact]
    public void ReadingsBecomeAPositionInTheWindowAndALevelFromTopToBottom()
    {
        var segment = Assert.Single(Sparkline.Segments([At(12, 100), At(6, 50), At(0, 0)], Now, Day));
        Assert.False(segment.Charging);
        Assert.Equal(0.5, segment.Points[0].X, 6); // 12 h ago is the middle of a 24 h window
        Assert.Equal(0.0, segment.Points[0].Y, 6); // full is the top
        Assert.Equal(0.75, segment.Points[1].X, 6);
        Assert.Equal(0.5, segment.Points[1].Y, 6);
        Assert.Equal(1.0, segment.Points[2].X, 6);
        Assert.Equal(1.0, segment.Points[2].Y, 6); // empty is the bottom
    }

    [Fact]
    public void TheReadingBeforeTheWindowStartsTheLineAtTheLeftEdge()
    {
        var segment = Assert.Single(Sparkline.Segments([At(30, 80), At(10, 70), At(0, 60)], Now, Day));
        Assert.Equal(0.0, segment.Points[0].X);
        Assert.Equal(0.2, segment.Points[0].Y, 6); // carried at 80%
        Assert.Equal(3, segment.Points.Count);
    }

    [Fact]
    public void AConnectedDevicesLineReachesTheRightEdgeButASleepingOneStopsShort()
    {
        var connected = Assert.Single(Sparkline.Segments([At(5, 60), At(0.2, 58)], Now, Day));
        Assert.Equal(1.0, connected.Points[^1].X);
        Assert.Equal(0.42, connected.Points[^1].Y, 6);

        var asleep = Assert.Single(Sparkline.Segments([At(10, 60), At(6, 58)], Now, Day));
        Assert.True(asleep.Points[^1].X < 1);
        Assert.Equal(2, asleep.Points.Count);
    }

    [Fact]
    public void ChargingStretchesBecomeTheirOwnSegmentsSharingTheBoundaryPoint()
    {
        var segments = Sparkline.Segments(
            [At(10, 50), At(8, 45), At(6, 45, charging: true), At(4, 70, charging: true), At(2, 69), At(0, 68)], Now, Day);

        Assert.Equal([false, true, false], segments.Select(s => s.Charging));
        // Discharging 10 h to 6 h ago, charging from 6 h ago to the first reading after it, then discharging to now.
        Assert.Equal(3, segments[0].Points.Count);
        Assert.Equal(segments[0].Points[^1], segments[1].Points[0]);
        Assert.Equal(segments[1].Points[^1], segments[2].Points[0]);
    }

    [Fact]
    public void ASingleReadingIsNotALineYet()
    {
        var segments = Sparkline.Segments([At(40, 50)], Now, Day); // old, outside the window and not carried
        Assert.False(Sparkline.HasLine(segments));
        Assert.False(Sparkline.HasLine(Sparkline.Segments([At(3, 50)], Now, Day)) ); // one point, too old to carry to now
    }

    [Fact]
    public void FutureReadingsAreIgnoredAndLevelsAreClamped()
    {
        var segments = Sparkline.Segments([At(2, 120), At(-3, 50)], Now, Day);
        var segment = Assert.Single(segments);
        Assert.All(segment.Points, p => Assert.InRange(p.Y, 0, 1));
        Assert.All(segment.Points, p => Assert.InRange(p.X, 0, 1));
    }
}
