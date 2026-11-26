using System;
using System.Numerics;
using XivMcp.Maps;

namespace XivMcp.Tests;

public class ApproachTests
{
    private static readonly DateTime T0 = new(2026, 11, 2, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Getting_closer_is_progress()
    {
        var a = new Approach(TimeSpan.FromSeconds(3));
        for (var s = 0; s <= 10; s++)
            Assert.False(a.Stalled(20 - s * 1.5f, T0.AddSeconds(s)));
    }

    [Fact]
    public void Running_into_a_wall_stalls_after_the_window()
    {
        var a = new Approach(TimeSpan.FromSeconds(3));
        Assert.False(a.Stalled(19.1f, T0));
        Assert.False(a.Stalled(19.0f, T0.AddSeconds(1)));
        Assert.False(a.Stalled(19.2f, T0.AddSeconds(2)));
        Assert.True(a.Stalled(18.9f, T0.AddSeconds(3.5)));
    }

    [Fact]
    public void Sliding_along_a_wall_without_getting_closer_stalls()
    {
        var a = new Approach(TimeSpan.FromSeconds(3));
        var stalled = false;
        for (var s = 0; s <= 8; s++)
            stalled = a.Stalled(19f + (s % 2 == 0 ? 0.2f : -0.2f), T0.AddSeconds(s * 0.5));
        Assert.True(stalled);
    }

    [Fact]
    public void A_short_pause_on_the_way_is_not_a_stall()
    {
        var a = new Approach(TimeSpan.FromSeconds(3));
        Assert.False(a.Stalled(30, T0));
        Assert.False(a.Stalled(30, T0.AddSeconds(2)));
        Assert.False(a.Stalled(25, T0.AddSeconds(2.5)));
        Assert.False(a.Stalled(25, T0.AddSeconds(4)));
    }

    [Fact]
    public void A_path_round_a_wall_is_measured_along_its_waypoints()
    {
        // Camp Dragonhead (2026-11): from the steps to the House Fortemps guard, down and round a wall; straight across is 10 yalms.
        var from = new Vector3(0, 0, 0);
        Vector3[] waypoints = [new(0, 0, -10), new(10, 0, -10), new(10, 0, 0)];
        Assert.Equal(30f, Approach.PathLength(from, waypoints), 3);
        Assert.Equal(0f, Approach.PathLength(from, []));
    }

    [Fact]
    public void Walking_away_from_the_target_along_a_shrinking_path_is_progress()
    {
        var a = new Approach(TimeSpan.FromSeconds(3));
        Vector3[] waypoints = [new(0, 0, -10), new(10, 0, -10), new(10, 0, 0)];
        var target = new Vector3(10, 0, 0);
        for (var s = 0; s <= 8; s++)
        {
            var at = new Vector3(0, 0, -s); // walking down the first leg: the straight line to the target grows
            Assert.True(Vector3.Distance(at, target) >= 10f);
            Assert.False(a.Stalled(Approach.PathLength(at, waypoints), T0.AddSeconds(s)));
        }
    }
}
