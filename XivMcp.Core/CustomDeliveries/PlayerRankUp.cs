using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace XivMcp.CustomDeliveries;

/// <summary>
/// The delivery that ranks a client up, left to the player so they can watch the client's story (player_ranks_up): XIV MCP makes the
/// item and takes the character to the client, then waits without touching the conversation.
/// </summary>
public static class PlayerRankUp
{
    /// <summary>How long XIV MCP waits for the player's hand-in (longer while they are in a conversation they started).</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromMinutes(30);

    /// <summary>How long XIV MCP waits at most, also while the player is in a conversation.</summary>
    public static readonly TimeSpan HardLimit = TimeSpan.FromHours(2);

    /// <summary>How long the client's story may hold the character after the rank changed.</summary>
    public static readonly TimeSpan StoryLimit = TimeSpan.FromMinutes(10);

    /// <summary>How long the game may hold the character after the rank changed with no window and no cutscene left.</summary>
    public static readonly TimeSpan HeldWithoutWindowLimit = TimeSpan.FromMinutes(1);

    public static string Ask(string item, string client) =>
        $"Hand in {item} to {client} yourself to watch the rank-up story: it is in your bags. XIV MCP waits up to " +
        $"{Limit.TotalMinutes:0} minutes and does not touch the conversation.";

    /// <param name="inBags">The item is still in the bags.</param>
    /// <param name="inConversation">The player is still in a conversation they started, which XIV MCP leaves alone.</param>
    public static string TimedOut(string item, string client, bool inBags, bool inConversation) =>
        $"{client} did not rank up within {(inConversation ? HardLimit.TotalHours + " hours" : Limit.TotalMinutes + " minutes")}" +
        (inBags
            ? $", and {item} is still in your bags. Hand it in to {client} yourself and then skip this step, or retry it: XIV MCP takes you " +
              $"back to {client} and asks again."
            : $", and {item} is not in your bags any more. Retry this step to have it made again and be asked again, or skip it.") +
        (inConversation ? " You are still in a conversation, which XIV MCP leaves alone." : "");

    public static string WentInShort(string item, string client) =>
        $"You handed in {item}, but {client} did not reach the next rank: a delivery below the top collectability tier gives less " +
        "satisfaction. The delivery counts.";

    public static string StillInStory(string client) =>
        $"{client}'s delivery went in, but the game still holds you in the conversation after {StoryLimit.TotalMinutes:0} minutes. The " +
        "delivery is done: finish the conversation yourself, then skip this step to go on.";

    public static string HeldWithoutWindow(string client) =>
        $"{client}'s delivery went in, but the game still holds the character with no window left. The delivery is done: " +
        "recover_game_state gets the character free, then skip this step to go on.";

    public static string AlreadyRankedUp(string client, int rank) =>
        $"{client} reached rank {rank} in an earlier run of this step: the rank-up was handed in, nothing more to deliver here.";
}

public enum HandIn { Wait, RankedUp, WentInShort, TimedOut, TimedOutInConversation }

/// <summary>
/// The wait for the player's hand-in, one look per poll. A conversation counts as the player's only when it started after they were
/// asked (they were seen out of one first): the client's menu Satisfier left open, or any other one held from before, does not keep
/// the wait going, and recovery may close it. The player's own one is left alone (<see cref="PlayersConversation"/>).
/// </summary>
public sealed class HandInWait(DateTime asked, int deliveriesLeft)
{
    private bool freeSinceAsked;
    private int wentInPolls;

    /// <summary>The player is in a conversation they started after the ask: nothing may touch it.</summary>
    public bool PlayersConversation { get; private set; }

    /// <param name="rankChanged">The client's rank is not the one it had when the delivery started.</param>
    /// <param name="deliveriesLeftNow">The client's deliveries left this week now.</param>
    public HandIn Poll(DateTime now, bool inConversation, bool rankChanged, int deliveriesLeftNow)
    {
        if (!inConversation) freeSinceAsked = true;
        PlayersConversation = inConversation && freeSinceAsked;
        if (rankChanged) return HandIn.RankedUp;
        // The game counts the delivery and changes the rank together: a second look makes sure the rank did not follow.
        wentInPolls = deliveriesLeftNow < deliveriesLeft ? wentInPolls + 1 : 0;
        if (wentInPolls >= 2) return HandIn.WentInShort;
        var waited = now - asked;
        if (waited > PlayerRankUp.HardLimit) return PlayersConversation ? HandIn.TimedOutInConversation : HandIn.TimedOut;
        return waited > PlayerRankUp.Limit && !PlayersConversation ? HandIn.TimedOut : HandIn.Wait;
    }
}

public enum StoryEnd { Wait, Over, TooLong, HeldWithoutWindow }

/// <summary>
/// The wait for the player to be out of the client's conversation after their hand-in, one look per poll: over after two looks in a
/// row out of it (a story can let go for a moment between two parts); stuck when it lasts past <see cref="PlayerRankUp.StoryLimit"/>,
/// or when the game holds the character with no window or cutscene for <see cref="PlayerRankUp.HeldWithoutWindowLimit"/>.
/// </summary>
public sealed class StoryWait(DateTime since)
{
    private int outPolls;
    private DateTime? heldSince;

    public StoryEnd Poll(DateTime now, bool inConversation, bool heldWithoutWindow)
    {
        if (!inConversation)
        {
            heldSince = null;
            return ++outPolls >= 2 ? StoryEnd.Over : StoryEnd.Wait;
        }
        outPolls = 0;
        heldSince = heldWithoutWindow ? heldSince ?? now : null;
        if (heldSince is { } held && now - held > PlayerRankUp.HeldWithoutWindowLimit) return StoryEnd.HeldWithoutWindow;
        return now - since > PlayerRankUp.StoryLimit ? StoryEnd.TooLong : StoryEnd.Wait;
    }
}

/// <summary>
/// What a deliver_custom_delivery step with player_ranks_up remembers between its runs (a resume, a retry): the client's rank when
/// it first left a rank-up to the player. A run that finds the client past it knows the player ranked it up, and must not start on
/// the next rank's requests.
/// </summary>
public static class RankUpMemory
{
    public static JsonObject Remember(string client, int rank) => new() { ["client"] = client, ["rank"] = rank };

    public static bool Remembers(JsonObject? memory, string client) => memory?["client"]?.GetValue<string>() == client;

    public static bool RankedUpSince(JsonObject? memory, string client, int rankNow) =>
        Remembers(memory, client) && memory!["rank"]?.GetValue<int>() is { } rank && rankNow > rank;
}

/// <summary>
/// How one call of deliver_custom_delivery turns in. Without player_ranks_up, or when no delivery reaches the next rank, as always.
/// With it, <see cref="RankUpAt"/> is the delivery that ranks the client up, and the ones before it (<see cref="ToTurnIn"/>) go in.
/// </summary>
/// <param name="ViaQuestionable">Questionable turns in, with every item already in the bags (Satisfier's own turn-in fails for some clients).</param>
/// <param name="SkipSatisfier">Nothing goes in before the player's delivery and the character is already at the client: Satisfier stays out.</param>
public sealed record RankUpTurnIn(int? RankUpAt, int ToTurnIn, bool ViaQuestionable, bool SkipSatisfier)
{
    /// <param name="planned">What the request still takes, within the scrip cap.</param>
    /// <param name="allowances">The week's deliveries left for all clients: a rank-up past them cannot be handed in this week.</param>
    /// <param name="inBags">The request's items in the bags.</param>
    /// <param name="usable">Of those, the ones Satisfier counts (from the collectability it counts from).</param>
    /// <param name="atClient">The client's delivery window or a hand-over window is open, or the character stands next to the client.</param>
    public static RankUpTurnIn Plan(bool playerRanksUp, bool isGather, int satisfaction, int needed, int perDelivery, int planned, int allowances,
                                    int inBags, int usable, bool atClient)
    {
        var rankUpAt = playerRanksUp ? RankProgress.RankUpAt(satisfaction, needed, perDelivery, Math.Min(planned, allowances)) : null;
        var toTurnIn = rankUpAt is { } k ? k - 1 : planned;
        // Questionable turns in every item it has for the request, the rank-up one too (its turn-in goes on until the client takes no
        // more or the bags are empty); only Satisfier can be made to stop one short.
        var viaQuestionable = rankUpAt is null && TurnInWatch.TurnInWithQuestionable(isGather, inBags, planned);
        // Satisfier's first frame can turn in with the count it started with: when the delivery window is already open, it delivers
        // before XIV MCP lowers that count.
        var skip = rankUpAt == 1 && usable >= 1 && atClient;
        return new(rankUpAt, toTurnIn, viaQuestionable, skip);
    }

    public bool LeftToPlayer => RankUpAt is not null;

    /// <summary>
    /// Whether the turn-in is over before Satisfier is: the planned deliveries are in, or the client ranked up. With a rank-up left to
    /// the player Satisfier runs to its end instead: with its count lowered it closes the client's windows itself after the deliveries
    /// before the player's (or takes the character to the client when there are none).
    /// </summary>
    public bool Done(int delivered, bool rankedUp) => rankedUp || (!LeftToPlayer && delivered >= ToTurnIn);

    /// <summary>
    /// Whether turning in may count as stalled. Not while Satisfier only takes the character to the client for the player's delivery:
    /// it shows "Turning in" during the walk, which can take longer than the stall limit.
    /// </summary>
    public bool WatchStall => !(LeftToPlayer && ToTurnIn == 0);
}

/// <summary>
/// The steps do_custom_deliveries makes of its plan with player_ranks_up: after a rank-up the client has new requests, so the job
/// stops at the first run that ranks its client up and plans again there.
/// </summary>
public static class RankUpJob
{
    /// <summary>How many of the plan's runs get a delivery step, and whether a step that plans again follows them.</summary>
    public static (int Runs, bool PlanAgain) Cut(IReadOnlyList<int?> rankUps, bool playerRanksUp)
    {
        if (playerRanksUp)
            for (var i = 0; i < rankUps.Count; i++)
                if (rankUps[i] is not null) return (i + 1, true);
        return (rankUps.Count, false);
    }

    /// <summary>The arguments of the step that plans again: the player still hands in the rank-ups, for the same kind and clients.</summary>
    public static JsonObject PlanAgainArgs(string? kind, IReadOnlyCollection<string>? clients)
    {
        var args = new JsonObject { ["player_ranks_up"] = true };
        if (kind is not null) args["kind"] = kind;
        if (clients is { Count: > 0 }) args["clients"] = new JsonArray([.. clients.Select(n => (JsonNode?)n)]);
        return args;
    }

    /// <summary>What a delivery step's note adds when its run ranks the client up.</summary>
    public const string RankUpNote = ", the rank-up one by you";
}
