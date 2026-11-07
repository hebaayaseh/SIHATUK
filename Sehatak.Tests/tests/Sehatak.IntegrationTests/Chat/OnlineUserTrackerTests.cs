using Sehatak.API.Hubs;
using Shouldly;
using Xunit;

namespace Sehatak.IntegrationTests.Chat;

public class OnlineUserTrackerTests
{
    [Fact]
    public void First_connection_means_online_and_last_disconnect_means_offline()
    {
        var t = new OnlineUserTracker();
        t.UserConnected("1:5").ShouldBeTrue();
        t.UserConnected("1:5").ShouldBeFalse();           // second device
        t.GetOnlineUsers().ShouldBe(new[] { "1:5" });

        t.UserDisconnected("1:5").ShouldBeFalse();        // one device left
        t.GetOnlineUsers().ShouldBe(new[] { "1:5" });
        t.UserDisconnected("1:5").ShouldBeTrue();
        t.GetOnlineUsers().ShouldBeEmpty();

        t.UserConnected("1:5").ShouldBeTrue();            // comes back
    }

    [Fact]
    public void Users_are_counted_separately()
    {
        var t = new OnlineUserTracker();
        t.UserConnected("1:5").ShouldBeTrue();
        t.UserConnected("2:5").ShouldBeTrue();
        t.UserDisconnected("1:5").ShouldBeTrue();
        t.GetOnlineUsers().ShouldBe(new[] { "2:5" });
    }

    /// <summary>A disconnect for someone who never connected writes -1 into the table; his next real connection then reads as "not first" and never shows online.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public void A_disconnect_without_a_connection_changes_nothing()
    {
        var t = new OnlineUserTracker();
        t.UserDisconnected("1:9").ShouldBeFalse();
        t.GetOnlineUsers().ShouldBeEmpty();
        t.UserConnected("1:9").ShouldBeTrue();
    }

    /// <summary>Read-then-write without a lock: two devices closing at the same moment both write count-1, so the user stays "online" forever.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public void Many_devices_closing_at_once_end_with_the_user_offline_exactly_once()
    {
        for (var round = 0; round < 30; round++)
        {
            var t = new OnlineUserTracker();
            const int devices = 200;
            for (var i = 0; i < devices; i++) t.UserConnected("1:7");

            var wentOffline = 0;
            Parallel.For(0, devices, _ => { if (t.UserDisconnected("1:7")) Interlocked.Increment(ref wentOffline); });

            wentOffline.ShouldBe(1, $"round {round}");
            t.GetOnlineUsers().ShouldBeEmpty($"round {round}");
        }
    }

    [Fact]
    public void Many_devices_opening_at_once_come_online_exactly_once()
    {
        var t = new OnlineUserTracker();
        var cameOnline = 0;
        Parallel.For(0, 200, _ => { if (t.UserConnected("1:8")) Interlocked.Increment(ref cameOnline); });
        cameOnline.ShouldBe(1);
    }
}
