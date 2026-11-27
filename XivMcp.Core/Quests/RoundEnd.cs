using System;
using System.Collections.Generic;
using System.Linq;

namespace XivMcp.Quests;

/// <summary>
/// The end of a round of quests Questionable does for XIV MCP. Once the given quests are done Questionable goes on with quests of its
/// own choosing (the journal, the main scenario), often with a teleport, faster than XIV MCP can stop it. Its "stop after the current
/// quest" flag (the flag button in its quest window) stops it in the same frame the quest completes instead, before it picks another.
/// </summary>
public static class RoundEnd
{
    /// <summary>
    /// The round's quests Questionable still has to do: not finished, and not one it has no path for (it never does those, so they
    /// would keep the round from ever reaching its last quest).
    /// </summary>
    public static IReadOnlyList<QuestInfo> Left(IEnumerable<QuestInfo> round, Func<QuestInfo, bool> finished, IReadOnlySet<uint> noPath) =>
        [.. round.Where(q => !noPath.Contains(q.Id) && !finished(q))];

    /// <summary>
    /// Whether to set Questionable's stop-after-this-quest flag now: one quest of the round is left and Questionable is on it. A daily
    /// only once it is accepted: Questionable checks the flag when its quest is complete and not accepted, and the game counts a daily
    /// done on an earlier day as complete, so Questionable would stop before accepting it.
    /// </summary>
    /// <param name="unfinished">The round's quests left (<see cref="Left"/>).</param>
    /// <param name="current">Questionable's current quest (its id, <see cref="QuestInfo.QuestionableId"/>).</param>
    /// <param name="lastAccepted">The one quest left is in the journal.</param>
    public static bool Arm(IReadOnlyList<QuestInfo> unfinished, string? current, bool lastAccepted) =>
        unfinished.Count == 1 && unfinished[0].QuestionableId == current && (!unfinished[0].Repeatable || lastAccepted);
}
