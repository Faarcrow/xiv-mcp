using System;

namespace XivMcp.Crafting;

/// <summary>What one check of a running craft decides.</summary>
public enum CraftRunCheck
{
    /// <summary>Keep waiting.</summary>
    Wait,

    /// <summary>The crafting plugin is done: it worked and is idle again, or it never started.</summary>
    Ended,

    /// <summary>Busy without any count change for the stall time: stop it.</summary>
    Stalled,

    /// <summary>Its solver worked on one craft for too long: stop it.</summary>
    SolverHung,

    /// <summary>The run took longer than its time limit.</summary>
    TimedOut,

    /// <summary>A direct call has to answer now, before the AI app gives up on it; the crafting goes on.</summary>
    Answer,
}

/// <summary>
/// Follows a crafting plugin's run (a list, or one item crafted a number of times) from its state, checked every few seconds,
/// and from the bag counts in <see cref="CraftListProgress"/>. The run has ended once the plugin was seen working and is idle
/// again, or when it never started within <see cref="StartGrace"/> (a run known to have started: at the first idle check). It is
/// stuck when the plugin stays busy without any count changing for the stall time, or when its solver works on one craft for
/// <see cref="SolverLimit"/>.
/// </summary>
/// <param name="timeout">Longest run; null: no limit.</param>
/// <param name="answerBy">For a direct call (not a job step): when to answer although the crafting goes on.</param>
/// <param name="startedRunning">The plugin is known to be busy from the start (Artisan's "craft X" queues its work inside the call),
/// so an idle plugin at the first check means it gave up at once, not that it has yet to start.</param>
public sealed class CraftRunWatch(CraftListProgress progress, DateTime started, TimeSpan stallAfter, TimeSpan? timeout = null, DateTime? answerBy = null,
                                  bool startedRunning = false)
{
    /// <summary>How long the plugin may take to start before an idle plugin counts as done (it never started).</summary>
    public static readonly TimeSpan StartGrace = TimeSpan.FromSeconds(30);

    /// <summary>How long the Raphael solver may work on one craft before the run counts as hung.</summary>
    public static readonly TimeSpan SolverLimit = TimeSpan.FromMinutes(3);

    /// <summary>
    /// How long a direct call follows the crafting, from the call's arrival, before it answers: AI apps stop waiting for a call after
    /// about 60 seconds, and an answer after that goes nowhere (the crafting goes on either way). With at most
    /// <see cref="StanceExitWait"/> for the crafting log after it, the call is answered about 50 seconds after it came in.
    /// </summary>
    public static readonly TimeSpan DirectCallWait = TimeSpan.FromSeconds(40);

    /// <summary>How long to wait after the end for the crafting log to close, so the next step finds the character free.</summary>
    public static readonly TimeSpan StanceExitWait = TimeSpan.FromSeconds(10);

    private bool sawRunning = startedRunning;

    /// <summary>A direct call's deadline has come: check the plugin now (the check then answers) instead of on the next regular check.</summary>
    public bool AnswerDue(DateTime now) => answerBy is { } by && now >= by;

    /// <summary>
    /// Until when to wait for the crafting log to close after the run ended at <paramref name="now"/>: <see cref="StanceExitWait"/>,
    /// for a direct call no longer than that past its deadline.
    /// </summary>
    public DateTime LogWaitEnds(DateTime now) => (answerBy is { } by && by < now ? by : now) + StanceExitWait;

    /// <param name="running">The plugin is crafting or running a list.</param>
    /// <param name="paused">Its list is paused (not stuck).</param>
    /// <param name="solving">Its solver is working out a craft.</param>
    public CraftRunCheck Check(DateTime now, bool running, bool paused, bool solving)
    {
        if (running) sawRunning = true;
        else if (sawRunning || now - started > StartGrace) return CraftRunCheck.Ended;
        // A paused list or a solver still working is not stuck, unless the solver works far too long without a craft.
        if (paused) progress.Touch(now);
        progress.SolverBusy(solving, now);
        if (running && progress.SolverHung(now, SolverLimit)) return CraftRunCheck.SolverHung;
        if (running && progress.Stalled(now, stallAfter)) return CraftRunCheck.Stalled;
        if (timeout is { } limit && now - started > limit) return CraftRunCheck.TimedOut;
        if (answerBy is { } by && now >= by) return CraftRunCheck.Answer;
        return CraftRunCheck.Wait;
    }
}
