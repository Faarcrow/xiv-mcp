using System;
using System.Collections.Generic;
using System.Linq;

namespace XivMcp.Quests;

/// <summary>
/// An allied society's story quests left to the player to watch (player_ranks_up on do_quests and do_tribe_dailies): its quests that
/// are not dailies, the rank-up quests and the ones that open with them. Questionable still walks, fights and hands in items, but
/// nothing skips their talk or cutscenes, and every choice in them is the player's.
/// </summary>
public static class PlayerStory
{
    /// <summary>The windows in which the game asks the player to choose; Questionable answers them while it runs.</summary>
    public static readonly string[] ChoiceWindows = ["SelectString", "SelectIconString", "CutSceneSelectString", "SelectYesno", "PointMenu"];

    /// <summary>The window offering a quest. TextAdvance accepts it, unless the quest is one of a <see cref="Choice"/>.</summary>
    public const string OfferWindow = "JournalAccept";

    /// <summary>Whether the player watches this quest of an allied society: with player_ranks_up, every quest that is not a daily.</summary>
    public static bool Watched(QuestInfo quest, bool playerRanksUp) => playerRanksUp && !quest.Repeatable;

    /// <summary>
    /// The quests of one round: a watched quest alone, so the player's story never runs into a daily; otherwise the round's other
    /// quests without the watched ones, which come in a round of their own.
    /// </summary>
    public static IReadOnlyList<QuestInfo> Round(IReadOnlyList<QuestInfo> quests, QuestInfo first, Func<QuestInfo, bool> watched) =>
        watched(first) ? [first] : [.. quests.Where(q => !watched(q))];

    /// <summary>
    /// Whether a choice window that opens during a watched quest is the player's, so Questionable is stopped before it can answer.
    /// Not when the player is in no conversation or cutscene (a prompt of the game's own, such as Return), in a quest battle
    /// (Questionable answers there even while stopped), travelling (Lifestream, an aetheryte), at the prompt that enters a solo duty
    /// (Questionable fights it), or at a prompt Questionable answers whatever its settings (a high-quality turn-in, a ticket, a
    /// purchase).
    /// </summary>
    public static bool PlayersChoice(ChoiceSeen seen) =>
        seen.InConversation && !seen.QuestBattle && !seen.Travelling && !seen.SoloDutyStart && !seen.KnownPrompt;

    /// <summary>What the job's progress says the player is asked.</summary>
    public static string Describe(string window) => window switch
    {
        "SelectYesno" => "a Yes or No question",
        "PointMenu" => "a choice of places",
        OfferWindow => "the quest offer",
        _ => "a dialogue choice",
    };

    /// <summary>
    /// The story quests the player chooses between, <paramref name="first"/> first: those offered with it that shut it out once done,
    /// or that it shuts out (QuestLock), such as the Qitari's stelae, two quests that back two versions of their history. Only
    /// <paramref name="first"/> when it stands alone.
    /// </summary>
    /// <param name="offered">The watched quests ready to accept, not accepted.</param>
    public static IReadOnlyList<QuestInfo> Choice(QuestInfo first, IEnumerable<QuestInfo> offered) =>
        [first, .. offered.Where(q => q.Id != first.Id && !q.Repeatable && (q.Locks.Contains(first.Id) || first.Locks.Contains(q.Id)))];

    /// <summary>
    /// Which quest of a <see cref="Choice"/> Questionable goes to: the first, and after each one the player did not take, the next,
    /// round and round until the player takes one.
    /// </summary>
    /// <param name="declined">The quest whose offer the player left last, if any.</param>
    public static QuestInfo Offer(IReadOnlyList<QuestInfo> choice, uint? declined)
    {
        var last = declined is { } id ? choice.Select(q => q.Id).ToList().IndexOf(id) : -1;
        return choice[(last + 1) % choice.Count];
    }

    public const string HeldWithoutWindow =
        "The game still holds your character after the quest's conversation, with no window left. recover_game_state gets the " +
        "character free; then retry this step.";
}

/// <summary>A choice window as it is set up, before Questionable's own handler sees it (<see cref="PlayerStory.PlayersChoice"/>).</summary>
/// <param name="InConversation">The player is in a conversation or a cutscene.</param>
/// <param name="QuestBattle">The character is in a quest battle instance.</param>
/// <param name="Travelling">Lifestream is travelling, or the target is an aetheryte.</param>
/// <param name="SoloDutyStart">A Yes or No while Questionable's current step enters a solo duty.</param>
/// <param name="KnownPrompt">A Yes or No whose text is one Questionable answers whatever its settings.</param>
public sealed record ChoiceSeen(bool InConversation, bool QuestBattle, bool Travelling, bool SoloDutyStart, bool KnownPrompt);
