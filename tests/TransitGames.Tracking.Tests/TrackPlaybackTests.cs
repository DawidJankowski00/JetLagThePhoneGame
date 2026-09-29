using TransitGames.Tracking.Playback;
using static TransitGames.Tracking.Tests.TestData;

namespace TransitGames.Tracking.Tests;

public class TrackPlaybackTests
{
    private readonly TrackPlayback _playback = new(
    [
        Point(0, 0, 0),
        Point(0, 100, 10),
        Point(1, 5000, 400),
        Point(1, 5100, 420),
    ]);

    [Fact]
    public void Bounds()
    {
        Assert.Equal(T0, _playback.Start);
        Assert.Equal(TimeSpan.FromSeconds(420), _playback.Duration);
        Assert.Equal(2, _playback.Segments.Count);
    }

    [Fact]
    public void InterpolatesWithinSegment()
    {
        Assert.Equal(North(50).Latitude, _playback.PositionAt(T0.AddSeconds(5)).Latitude, 9);
        Assert.Equal(North(5025).Latitude, _playback.PositionAt(T0.AddSeconds(405)).Latitude, 9);
        Assert.Equal(North(100), _playback.PositionAt(T0.AddSeconds(10)));
    }

    [Fact]
    public void HoldsLastFixDuringGapBetweenSegments()
    {
        Assert.Equal(North(100), _playback.PositionAt(T0.AddSeconds(200)));
    }

    [Fact]
    public void ClampsOutsideTrack()
    {
        Assert.Equal(North(0), _playback.PositionAt(T0.AddHours(-1)));
        Assert.Equal(North(5100), _playback.PositionAtFraction(2));
        Assert.Equal(T0.AddSeconds(210), _playback.TimeAtFraction(0.5));
    }

    [Fact]
    public void FractionAt_IsTheInverseOfTimeAtFraction_AndClamps()
    {
        Assert.Equal(0.5, _playback.FractionAt(T0.AddSeconds(210)), 6);
        Assert.Equal(0, _playback.FractionAt(T0.AddHours(-1)));
        Assert.Equal(1, _playback.FractionAt(T0.AddHours(1)));
    }

    [Fact]
    public void EmptyTrack_Throws() =>
        Assert.Throws<ArgumentException>(() => new TrackPlayback([]));
}
