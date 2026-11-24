using XivMcp.Jobs;

namespace XivMcp.Tests;

public class StepInsertTests
{
    private sealed record Step(string Id, bool Done = false);

    private static List<string> Insert(IReadOnlyList<Step> steps, string running, params string[] added) =>
        StepInsert.Insert(steps, steps.Single(s => s.Id == running), [.. added.Select(a => new Step(StepInsert.Id(running, a)))], s => s.Id, s => !s.Done)
            .Select(s => s.Id).ToList();

    [Fact]
    public void Added_steps_go_right_after_the_running_step_and_the_rest_stays()
    {
        // A player's job: deliveries, then the summoning bell. Planning again must not lose the bell.
        Assert.Equal(["plan", "plan-d0", "plan-r1", "bell"], Insert([new("plan"), new("bell")], "plan", "d0", "r1"));
    }

    [Fact]
    public void A_step_run_again_replaces_what_it_added_before()
    {
        var steps = new List<Step> { new("d0", true), new("r0"), new("r0-d0"), new("r0-r1"), new("bell") };
        Assert.Equal(["d0", "r0", "r0-d1", "bell"], Insert(steps, "r0", "d1"));
    }

    [Fact]
    public void Finished_steps_it_added_before_stay()
    {
        var steps = new List<Step> { new("r0-d0", true), new("r0"), new("bell") };
        Assert.Equal(["r0-d0", "r0", "bell"], Insert(steps, "r0"));
    }

    [Fact]
    public void Steps_of_another_step_are_left_alone()
    {
        // r10's steps start with "r1" too, but not with "r1-".
        var steps = new List<Step> { new("r1"), new("r1-d0"), new("r10-d0"), new("bell") };
        Assert.Equal(["r1", "r1-d1", "r10-d0", "bell"], Insert(steps, "r1", "d1"));
    }

    [Theory]
    [InlineData(true, false, false, false, true)]
    [InlineData(false, false, false, false, false)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, false, true, false, false)]
    [InlineData(true, false, false, true, false)]
    public void Recovery_runs_after_a_failed_or_stopped_step_unless_the_player_is_in_a_conversation(bool failed, bool passive, bool shutdown, bool player, bool recover) =>
        Assert.Equal(recover, StepRecovery.ShouldRecover(failed, passive, shutdown, player));
}
