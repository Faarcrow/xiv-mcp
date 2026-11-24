using XivMcp.Crafting;

namespace XivMcp.Tests;

public class CraftRunWatchTests
{
    private static readonly DateTime T0 = new(2026, 11, 22, 18, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Stall = TimeSpan.FromMinutes(5);

    // Bronze Ingot (item 10) from Copper Ore (item 1), three crafts.
    private static readonly ListRecipe Ingot = new(100, "Bronze Ingot", 10, 3, 1, []);

    private static Dictionary<uint, int> Counts(int ore, int ingot) => new() { [1] = ore, [10] = ingot };

    private static (CraftListProgress Progress, CraftRunWatch Watch) Run(TimeSpan? timeout = null, DateTime? answerBy = null, TimeSpan? stall = null,
                                                                         bool startedRunning = false)
    {
        var progress = new CraftListProgress([Ingot], Counts(9, 0), T0);
        return (progress, new CraftRunWatch(progress, T0, stall ?? Stall, timeout, answerBy, startedRunning));
    }

    [Fact]
    public void The_run_ends_once_the_plugin_was_seen_working_and_is_idle_again()
    {
        var (progress, watch) = Run();
        Assert.Equal(CraftRunCheck.Wait, watch.Check(T0.AddSeconds(5), running: true, paused: false, solving: false));
        progress.Observe(Counts(6, 1), T0.AddSeconds(30));
        Assert.Equal(CraftRunCheck.Wait, watch.Check(T0.AddSeconds(30), running: true, paused: false, solving: false));
        Assert.Equal(CraftRunCheck.Ended, watch.Check(T0.AddSeconds(35), running: false, paused: false, solving: false));
    }

    [Fact]
    public void A_plugin_that_never_starts_counts_as_ended_only_after_the_start_grace()
    {
        // The plugin can take a moment to pick the recipe up; idle before that is not the end yet.
        var (_, watch) = Run();
        Assert.Equal(CraftRunCheck.Wait, watch.Check(T0.AddSeconds(5), running: false, paused: false, solving: false));
        Assert.Equal(CraftRunCheck.Wait, watch.Check(T0 + CraftRunWatch.StartGrace, running: false, paused: false, solving: false));
        Assert.Equal(CraftRunCheck.Ended, watch.Check(T0 + CraftRunWatch.StartGrace + TimeSpan.FromSeconds(5), running: false, paused: false, solving: false));
    }

    [Fact]
    public void A_run_known_to_have_started_ends_as_soon_as_the_plugin_is_idle()
    {
        // Artisan's "craft X" queues its work inside the call that starts it, so it is busy from the start. Idle at the first check
        // means it gave up at once (no materials, no gearset for the job): the start grace would only add half a minute.
        var (_, watch) = Run(startedRunning: true);
        Assert.Equal(CraftRunCheck.Ended, watch.Check(T0.AddSeconds(5), running: false, paused: false, solving: false));
    }

    [Fact]
    public void A_busy_plugin_with_no_count_change_for_the_stall_time_is_stalled()
    {
        var (progress, watch) = Run();
        progress.Observe(Counts(6, 1), T0.AddMinutes(1));
        Assert.Equal(CraftRunCheck.Wait, watch.Check(T0.AddMinutes(5), running: true, paused: false, solving: false));
        Assert.Equal(CraftRunCheck.Stalled, watch.Check(T0.AddMinutes(6), running: true, paused: false, solving: false));
    }

    [Fact]
    public void A_zero_stall_time_never_stalls()
    {
        var (_, watch) = Run(stall: TimeSpan.Zero);
        Assert.Equal(CraftRunCheck.Wait, watch.Check(T0.AddHours(3), running: true, paused: false, solving: false));
    }

    [Fact]
    public void A_paused_list_is_not_stuck()
    {
        var (_, watch) = Run();
        for (var minute = 1; minute <= 20; minute++)
            Assert.Equal(CraftRunCheck.Wait, watch.Check(T0.AddMinutes(minute), running: true, paused: true, solving: false));
    }

    [Fact]
    public void A_solver_that_works_on_one_craft_for_too_long_is_hung()
    {
        var (_, watch) = Run();
        Assert.Equal(CraftRunCheck.Wait, watch.Check(T0.AddMinutes(1), running: true, paused: false, solving: true));
        Assert.Equal(CraftRunCheck.Wait, watch.Check(T0.AddMinutes(3), running: true, paused: false, solving: true));
        Assert.Equal(CraftRunCheck.SolverHung, watch.Check(T0.AddMinutes(1) + CraftRunWatch.SolverLimit, running: true, paused: false, solving: true));
    }

    [Fact]
    public void A_run_past_its_time_limit_times_out_and_without_one_never_does()
    {
        var (progress, watch) = Run(timeout: TimeSpan.FromMinutes(10));
        var (endless, noLimit) = Run();
        for (var minute = 1; minute <= 10; minute++) // something changes every minute: never a stall
        {
            progress.Observe(Counts(minute % 2, 0), T0.AddMinutes(minute));
            endless.Observe(Counts(minute % 2, 0), T0.AddMinutes(minute));
        }
        Assert.Equal(CraftRunCheck.Wait, watch.Check(T0.AddMinutes(10), running: true, paused: false, solving: false));
        Assert.Equal(CraftRunCheck.TimedOut, watch.Check(T0.AddMinutes(10.5), running: true, paused: false, solving: false));
        Assert.Equal(CraftRunCheck.Wait, noLimit.Check(T0.AddMinutes(10.5), running: true, paused: false, solving: false));
    }

    [Fact]
    public void A_direct_call_answers_at_its_deadline_while_the_crafting_goes_on()
    {
        var by = T0 + CraftRunWatch.DirectCallWait;
        var (_, watch) = Run(answerBy: by);
        Assert.Equal(CraftRunCheck.Wait, watch.Check(by.AddSeconds(-5), running: true, paused: false, solving: false));
        Assert.Equal(CraftRunCheck.Answer, watch.Check(by, running: true, paused: false, solving: false));
        // Done in time: the call reports the end like a job step.
        var (_, quick) = Run(answerBy: by);
        Assert.Equal(CraftRunCheck.Wait, quick.Check(T0.AddSeconds(5), running: true, paused: false, solving: false));
        Assert.Equal(CraftRunCheck.Ended, quick.Check(by, running: false, paused: false, solving: false));
    }

    [Fact]
    public void A_deadline_that_passed_during_the_approval_answers_at_the_first_check()
    {
        // The deadline counts from the call's arrival: a slow approval can use it up before the crafting even started.
        var (_, watch) = Run(answerBy: T0.AddSeconds(-3), startedRunning: true);
        Assert.True(watch.AnswerDue(T0.AddSeconds(1)));
        Assert.Equal(CraftRunCheck.Answer, watch.Check(T0.AddSeconds(1), running: true, paused: false, solving: false));
    }

    [Fact]
    public void A_direct_call_answers_within_about_50_seconds_of_its_arrival()
    {
        // After about 60 seconds the AI app stops waiting, and the answer would go nowhere. The deadline is checked every second, and
        // a run that ends at the deadline waits for the crafting log to close no longer than StanceExitWait past it.
        var by = T0 + CraftRunWatch.DirectCallWait;
        var (_, watch) = Run(answerBy: by);
        Assert.False(watch.AnswerDue(by.AddSeconds(-1)));
        Assert.True(watch.AnswerDue(by));
        Assert.Equal(T0.AddSeconds(20) + CraftRunWatch.StanceExitWait, watch.LogWaitEnds(T0.AddSeconds(20)));
        Assert.Equal(by + CraftRunWatch.StanceExitWait, watch.LogWaitEnds(by.AddSeconds(1)));
        Assert.True(watch.LogWaitEnds(by.AddSeconds(1)) - T0 <= TimeSpan.FromSeconds(50));
        // A job step has no deadline.
        var (_, step) = Run();
        Assert.False(step.AnswerDue(T0.AddHours(5)));
        Assert.Equal(T0.AddHours(5) + CraftRunWatch.StanceExitWait, step.LogWaitEnds(T0.AddHours(5)));
    }
}
