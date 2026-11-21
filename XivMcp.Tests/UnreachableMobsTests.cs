using System;
using XivMcp.Farming;

namespace XivMcp.Tests;

public class UnreachableMobsTests
{
    private static readonly DateTime T0 = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_skipped_monster_is_left_alone_for_a_while()
    {
        var skip = new UnreachableMobs();
        skip.Skip(42, T0);
        Assert.True(skip.IsSkipped(42, T0.AddMinutes(1)));
        Assert.False(skip.IsSkipped(7, T0.AddMinutes(1)));
    }

    [Fact]
    public void It_is_tried_again_later_it_may_have_wandered_somewhere_reachable()
    {
        var skip = new UnreachableMobs();
        skip.Skip(42, T0);
        Assert.False(skip.IsSkipped(42, T0 + UnreachableMobs.For + TimeSpan.FromSeconds(1)));
    }
}
