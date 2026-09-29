using TransitGames.Core.Location;
using static TransitGames.Tracking.Tests.TestData;

namespace TransitGames.Tracking.Tests;

public class TrackPointFilterTests
{
    private static readonly TrackingOptions Options = new()
    {
        Interval = TimeSpan.FromSeconds(5),
        MinDistanceMeters = 10,
        MaxAccuracyMeters = 50,
        SegmentGap = TimeSpan.FromMinutes(2),
    };

    [Fact]
    public void FirstFix_IsAccepted()
    {
        var filter = new TrackPointFilter(Options);

        Assert.Equal(TrackPointDecision.Accept, filter.Evaluate(Sample(0, 0)));
    }

    [Fact]
    public void InaccurateFix_IsRejected()
    {
        var filter = new TrackPointFilter(Options);

        Assert.Equal(TrackPointDecision.Reject, filter.Evaluate(Sample(0, 0, accuracy: 80)));
        Assert.Equal(TrackPointDecision.Accept, filter.Evaluate(Sample(0, 5, accuracy: null)));
    }

    [Fact]
    public void FixesFasterThanInterval_AreThinned()
    {
        var filter = new TrackPointFilter(Options);
        filter.Evaluate(Sample(0, 0));

        Assert.Equal(TrackPointDecision.Reject, filter.Evaluate(Sample(20, 1)));
        Assert.Equal(TrackPointDecision.Reject, filter.Evaluate(Sample(40, 2)));
        Assert.Equal(TrackPointDecision.Accept, filter.Evaluate(Sample(100, 4.6))); // within the 10 % tolerance
    }

    [Fact]
    public void StandingStill_KeepsOnePointPerMinute()
    {
        var filter = new TrackPointFilter(Options);
        filter.Evaluate(Sample(0, 0));

        Assert.Equal(TrackPointDecision.Reject, filter.Evaluate(Sample(3, 10)));
        Assert.Equal(TrackPointDecision.Reject, filter.Evaluate(Sample(5, 50)));
        Assert.Equal(TrackPointDecision.Accept, filter.Evaluate(Sample(4, 60)));
    }

    [Fact]
    public void OutOfOrderFix_IsRejected()
    {
        var filter = new TrackPointFilter(Options);
        filter.Evaluate(Sample(0, 30));

        Assert.Equal(TrackPointDecision.Reject, filter.Evaluate(Sample(500, 20)));
        Assert.Equal(TrackPointDecision.Reject, filter.Evaluate(Sample(500, 30)));
    }

    [Fact]
    public void NoUsableFixForLongerThanGap_StartsNewSegment()
    {
        var filter = new TrackPointFilter(Options);
        filter.Evaluate(Sample(0, 0));

        // Underground: only inaccurate fixes for three minutes.
        Assert.Equal(TrackPointDecision.Reject, filter.Evaluate(Sample(900, 60, accuracy: 300)));
        Assert.Equal(TrackPointDecision.AcceptNewSegment, filter.Evaluate(Sample(2000, 180)));
        Assert.Equal(TrackPointDecision.Accept, filter.Evaluate(Sample(2050, 185)));
    }

    [Fact]
    public void StationaryWithGoodSignal_IsNotAGap()
    {
        var filter = new TrackPointFilter(Options);
        filter.Evaluate(Sample(0, 0));
        for (var s = 5; s < 170; s += 5)
        {
            filter.Evaluate(Sample(1, s));
        }

        Assert.Equal(TrackPointDecision.Accept, filter.Evaluate(Sample(100, 175)));
    }
}
