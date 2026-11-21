using System;
using XivMcp.Maps;

namespace XivMcp.Tests;

public class WorldVisitTests
{
    private static WorldVisitAction Next(bool loggedIn = true, bool onTarget = false, bool busy = false, bool loading = false,
                                         double sinceCommand = 30, int attempts = 1) =>
        WorldVisit.Next(new WorldVisitState(loggedIn, onTarget, busy, loading, TimeSpan.FromSeconds(sinceCommand), attempts));

    [Fact] public void On_the_world_and_idle_is_arrived() => Assert.Equal(WorldVisitAction.Arrived, Next(onTarget: true));
    [Fact] public void On_the_world_while_the_plugin_still_works_waits() => Assert.Equal(WorldVisitAction.Wait, Next(onTarget: true, busy: true));
    [Fact] public void A_busy_plugin_is_left_alone() => Assert.Equal(WorldVisitAction.Wait, Next(busy: true, sinceCommand: 300));
    [Fact] public void Loading_or_in_the_lobby_waits() => Assert.Equal(WorldVisitAction.Wait, Next(loading: true, sinceCommand: 300));
    [Fact] public void Logged_out_during_a_data_center_trip_waits() => Assert.Equal(WorldVisitAction.Wait, Next(loggedIn: false, sinceCommand: 300));
    [Fact] public void Right_after_the_command_waits_for_the_plugin_to_start() => Assert.Equal(WorldVisitAction.Wait, Next(sinceCommand: 5));
    [Fact] public void An_idle_plugin_off_target_is_retried() => Assert.Equal(WorldVisitAction.Retry, Next(sinceCommand: 25));
    [Fact] public void After_the_last_retry_it_gives_up() => Assert.Equal(WorldVisitAction.GiveUp, Next(sinceCommand: 25, attempts: WorldVisit.MaxAttempts));
}
