using AgentHub.Orchestration.Coordinator;
using Xunit;

namespace AgentHub.Tests;

[Trait("Category", "Phase10")]
public class ActiveSessionTrackerTests
{
    [Fact]
    public void Increment_IncreasesCount()
    {
        var tracker = new ActiveSessionTracker();
        tracker.Increment("host1");
        Assert.Equal(1, tracker.GetCount("host1"));
    }

    [Fact]
    public void Increment_MultipleTimes_Accumulates()
    {
        var tracker = new ActiveSessionTracker();
        tracker.Increment("host1");
        tracker.Increment("host1");
        tracker.Increment("host1");
        Assert.Equal(3, tracker.GetCount("host1"));
    }

    [Fact]
    public void Decrement_DecreasesCount()
    {
        var tracker = new ActiveSessionTracker();
        tracker.Increment("host1");
        tracker.Increment("host1");
        tracker.Decrement("host1");
        Assert.Equal(1, tracker.GetCount("host1"));
    }

    [Fact]
    public void Decrement_NeverGoesBelowZero()
    {
        var tracker = new ActiveSessionTracker();
        tracker.Decrement("host1");
        tracker.Decrement("host1");
        Assert.Equal(0, tracker.GetCount("host1"));
    }

    [Fact]
    public void GetCount_UnknownHost_ReturnsZero()
    {
        var tracker = new ActiveSessionTracker();
        Assert.Equal(0, tracker.GetCount("unknown-host"));
    }

    [Fact]
    public void TracksMultipleHostsIndependently()
    {
        var tracker = new ActiveSessionTracker();
        tracker.Increment("host1");
        tracker.Increment("host1");
        tracker.Increment("host2");
        Assert.Equal(2, tracker.GetCount("host1"));
        Assert.Equal(1, tracker.GetCount("host2"));
    }
}
