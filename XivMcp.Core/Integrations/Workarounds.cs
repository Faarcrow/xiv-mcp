using System;
using System.Collections.Generic;
using System.Linq;

namespace XivMcp.Integrations;

/// <summary>
/// Something an integration does on behalf of the plugin it drives, where that plugin falls short (a step it hangs on, a prompt it
/// leaves open, a job it doesn't switch to): <paramref name="Does"/> says what XIV MCP does, <paramref name="Instead"/> what happens
/// when the player turns the plugin's workarounds off. One sentence each, for the player. Generated into the docs, so keep the format.
/// </summary>
public sealed record Workaround(string Id, string Does, string Instead);

/// <summary>A workaround a run used, and how often: what a job's result lists.</summary>
public sealed record WorkaroundUse(string Id, string Does, int Count);

/// <summary>
/// The workarounds one run of an integration's job used, and how often. <paramref name="enabled"/> is read on every check, so turning
/// the plugin's workarounds off in the middle of a run stops them from then on.
/// </summary>
public sealed class WorkaroundLog(Func<bool> enabled)
{
    private readonly List<WorkaroundUse> used = [];

    /// <summary>Whether the player allows the plugin's workarounds now.</summary>
    public bool Enabled => enabled();

    /// <summary>Counts a workaround that just did its part.</summary>
    public void Used(Workaround workaround)
    {
        var i = used.FindIndex(u => u.Id == workaround.Id);
        if (i < 0) used.Add(new WorkaroundUse(workaround.Id, workaround.Does, 1));
        else used[i] = used[i] with { Count = used[i].Count + 1 };
    }

    /// <summary>The workarounds used, in the order they were first used; null when none was.</summary>
    public IReadOnlyList<WorkaroundUse>? Report() => used.Count == 0 ? null : used.ToList();
}

/// <summary>What XIV MCP does where Questionable falls short while it does quests (do_quests and the jobs that run it).</summary>
public static class QuestionableWorkarounds
{
    public static readonly Workaround PickJob = new("pick_job",
        "Before a quest is accepted, switches to a gearset of a job that may do it: your current job when it may, else the highest level one, and Miner or Botanist for gathering quests.",
        "Questionable accepts the quest on the job its own settings pick.");

    public static readonly Workaround BackToAcceptedJob = new("back_to_accepted_job",
        "Before Questionable goes on with an accepted quest, switches back to the job it was accepted on, which a craft step can leave.",
        "Questionable does this itself when its setting to switch to the job a quest was accepted with is on.");

    public static readonly Workaround CloseBlockingWindow = new("close_blocking_window",
        "Closes a window that holds your character, such as a shop or the retainer list at a bell, before starting Questionable, which can't teleport while it is open.",
        "Questionable starts with the window open.");

    public static readonly Workaround AcceptTogether = new("accept_together",
        "Has Questionable accept several quests that are ready at once (often the day's allied society quests from one NPC) before doing any, as its Accept all quests does.",
        "Questionable accepts and does them one after another.");

    public static readonly Workaround ConfirmHqTurnIn = new("confirm_hq_turn_in",
        "Answers Yes when a quest conversation asks whether to hand in a high-quality item.",
        "Questionable is left to answer it.");

    public static readonly Workaround SkipMeldedStep = new("skip_melded_step",
        "Presses Questionable's Skip at a step that waits for you to meld materia on an item, when that item already has materia.",
        "Questionable waits at the step until you press Skip, and the job stops once it makes no progress.");

    public static readonly Workaround SkipOwnedItemsStep = new("skip_owned_items_step",
        "Presses Questionable's Skip at a craft or purchase step whose items are already in your bags, and stops the Artisan list it started for them.",
        "Questionable crafts or buys the items again.");

    public static IReadOnlyList<Workaround> All { get; } =
        [PickJob, BackToAcceptedJob, CloseBlockingWindow, AcceptTogether, ConfirmHqTurnIn, SkipMeldedStep, SkipOwnedItemsStep];
}
