using System;
using System.Collections.Generic;
using System.Linq;

namespace XivMcp.Jobs;

/// <summary>
/// Steps a running job step adds to its own job (do_custom_deliveries planning again after a rank-up): they go right after it, and
/// the job's other steps stay.
/// </summary>
public static class StepInsert
{
    /// <summary>The id of a step the running step adds: the running step's id in front, so it never clashes with the job's others.</summary>
    public static string Id(string running, string added) => $"{running}-{added}";

    /// <summary>
    /// The job's steps once the running step added <paramref name="added"/>: right after it, in place of the unfinished steps an
    /// earlier run of the same step added (a retry, a resume), which <see cref="Id"/> marks. Every other step stays where it was.
    /// </summary>
    public static List<T> Insert<T>(IReadOnlyList<T> steps, T running, IReadOnlyList<T> added, Func<T, string> id, Func<T, bool> unfinished)
        where T : class
    {
        var mine = id(running) + "-";
        var kept = steps.Where(s => ReferenceEquals(s, running) || !(id(s).StartsWith(mine, StringComparison.Ordinal) && unfinished(s))).ToList();
        var at = kept.FindIndex(s => ReferenceEquals(s, running));
        if (at < 0) throw new ArgumentException("The running step is not one of the steps.", nameof(running));
        kept.InsertRange(at + 1, added);
        return kept;
    }
}

/// <summary>When the job runner gets the character free after a step (recovery: clicking through dialogue, closing windows).</summary>
public static class StepRecovery
{
    /// <summary>
    /// After a step that failed or was stopped, unless it only waited, the plugin is shutting down, or the step left the player in a
    /// conversation they want to see (a client's rank-up story).
    /// </summary>
    public static bool ShouldRecover(bool failedOrStopped, bool passive, bool shuttingDown, bool playerInConversation) =>
        failedOrStopped && !passive && !shuttingDown && !playerInConversation;
}
