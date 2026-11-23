using System;
using System.Collections.Generic;
using System.Linq;

namespace XivMcp.CustomDeliveries;

/// <summary>Satisfier's "Job choice" setting (CraftJobType in its config), which picks both the crafter and the gatherer.</summary>
public enum SatisfierJobChoice { Specific, Current, LowestXP, HighestXP }

/// <summary>Satisfier's job settings, with its defaults: Specific, Carpenter (8) and Miner (16).</summary>
public sealed record SatisfierJobSettings(SatisfierJobChoice Choice = SatisfierJobChoice.Specific, uint CraftJob = 8, uint GatherJob = 16);

/// <summary>A class or job and the character's level in it (0: not unlocked).</summary>
public readonly record struct JobLevel(uint Job, int Level);

/// <summary>The crafter and gatherer Satisfier would use now, and the character's Fisher level: the same for every client.</summary>
public sealed record JobPick(JobLevel Crafter, JobLevel Gatherer, int FisherLevel);

/// <summary>
/// How far below a request's level a job can still make it. Neither rule is in the game data, so both are one change away if the game
/// disagrees.
/// </summary>
public static class LevelMargins
{
    /// <summary>
    /// A crafter can start recipes up to this many levels above their own: "Generally, you can craft recipes up to five levels above
    /// your own" (HGG's crafting guide), and Artisan skips a list recipe only when the job's level is below the recipe's minus 5.
    /// Checked in game for 1 level only: a level 71 Armorer crafted Custom Weaving Components (recipe level 72) with Artisan at
    /// collectability 291, past Ehll Tou's top tier (273).
    /// </summary>
    public const int Craft = 5;

    /// <summary>
    /// A gatherer finds gathering points up to this many levels above their own: "a level 21 Botanist can locate level 25 and lower
    /// spots" (consolegameswiki, Gathering), and "lv 26+ to gather from lv 30 nodes" (a Square Enix forum post). Gather n Chill, which
    /// Satisfier gathers with, checks no level. Checked in game only 5 levels above (hidden: in Labyrinthos a level 80 Botanist could
    /// not target any of the level 85 logging points) and 2 below (visible: a level 87 Miner could target a level 85 quarrying point);
    /// 1 to 4 levels above is untested. Whether an item above the gatherer's level can be gathered at a point it finds is not known
    /// either, so XIV MCP does not refuse for the item's level.
    /// </summary>
    public const int Gather = 4;
}

/// <summary>
/// The job that makes a request (crafts, gathers or fishes it), the character's level in it, the request's level (the recipe's; the
/// lowest gathering point's that has the item, or the item's own if higher; the fish's or its fishing spot's, whichever is higher)
/// and the lowest job level that can make it at all (<see cref="LevelMargins"/>). Only that is judged: how the items turn out (their
/// collectability) depends on gear as much as on level, so XIV MCP does not guess it from the level. A request that is not
/// <see cref="Enforced"/> (fishing: no margin is known, and XIV MCP fishes itself) is only shown, never judged.
/// </summary>
public sealed record DeliveryJob(uint Job, int JobLevel, int Level, int MinLevel, bool Enforced = true)
{
    public static DeliveryJob Craft(uint job, int jobLevel, int recipeLevel) => new(job, jobLevel, recipeLevel, recipeLevel - LevelMargins.Craft);

    /// <summary>
    /// A request's level and lowest workable level for one gathering point that has the item: the point's level less the margin; the
    /// item's own level only raises the request's level (see <see cref="LevelMargins.Gather"/>).
    /// </summary>
    public static (int Level, int MinLevel) GatherLevels(int pointLevel, int itemLevel) =>
        (Math.Max(pointLevel, itemLevel), pointLevel - LevelMargins.Gather);

    /// <summary>Fishing levels are only shown: no margin is known, and XIV MCP fishes itself and never refuses for them.</summary>
    public static DeliveryJob Fish(int fisherLevel, int level) => new(18, fisherLevel, level, level, Enforced: false);

    /// <summary>The job can make the request at all.</summary>
    public bool CanMake => !Enforced || JobLevel >= MinLevel;

    /// <summary>Whether the job's level stops a delivery of <paramref name="toDeliver"/>: only when items are still to be made.</summary>
    public bool Blocks(int inBags, int toDeliver) => !CanMake && inBags < toDeliver;

    /// <summary>
    /// Whether the job can deliver the request: it can make it, or the items still to deliver are all in the bags. Null when the
    /// request is not judged (fishing).
    /// </summary>
    public bool? LevelOk(int inBags, int toDeliver) => Enforced ? CanMake || (toDeliver > 0 && inBags >= toDeliver) : null;

    /// <summary>Whether a delivery that makes <paramref name="toMake"/> items with this job (<see cref="SatisfierMakes"/>) is refused: the job cannot make them.</summary>
    public bool Refuses(int toMake) => toMake > 0 && Enforced && !CanMake;
}

/// <summary>
/// How many items a delivery makes, as Satisfier counts them: its tasks ignore the scrip cap and the week's deliveries left, and make
/// what the request still takes (RemainingTurnins) less the items in the bags they count. AutoCraft counts items from collectability
/// 1, AutoGather from the request's lowest tier. A gathering delivery that Questionable turns in, with every item in the bags, makes
/// none; fishing is XIV MCP's own (never judged).
/// </summary>
public static class SatisfierMakes
{
    /// <param name="stillTakes">What the request still takes (Satisfier's RemainingTurnins).</param>
    /// <param name="usable">The request's items in the bags Satisfier counts.</param>
    /// <param name="viaQuestionable">Questionable turns in instead of Satisfier (see <see cref="TurnInWatch.TurnInWithQuestionable"/>).</param>
    public static int Items(DeliveryKind kind, int stillTakes, int usable, bool viaQuestionable) => kind switch
    {
        DeliveryKind.Craft => Math.Max(0, stillTakes - usable),
        DeliveryKind.Gather when !viaQuestionable => Math.Max(0, stillTakes - usable),
        _ => 0,
    };
}

/// <summary>What XIV MCP says about a job's level against a request's. Shown to the player, so no contractions.</summary>
public static class DeliveryLevelText
{
    /// <summary>Why the job cannot make the request (crafting or gathering).</summary>
    public static string CannotMake(DeliveryKind kind, string item, string client, string jobName, DeliveryJob job) => kind == DeliveryKind.Craft
        ? $"Satisfier would craft {item} for {client} as {jobName}, which is level {job.JobLevel}, but the recipe is level {job.Level}, " +
          $"and a crafter can start recipes only up to {LevelMargins.Craft} levels above their own."
        : $"Satisfier would gather {item} for {client} as {jobName}, which is level {job.JobLevel}, but the lowest gathering points that " +
          $"have it are level {job.MinLevel + LevelMargins.Gather}, and a gatherer finds points only up to {LevelMargins.Gather} levels above their own.";

}

/// <summary>
/// The crafter and gatherer Satisfier uses for a delivery, as it picks them (vsatisfy CraftTurnin.GetCrafter, GatherData.GetGatherer).
/// Lowest and highest XP compare the jobs' levels, not their experience, and look neither at the recipe's level nor at the gathering
/// point's.
/// </summary>
public static class SatisfierJobs
{
    /// <param name="crafters">The crafters (ClassJobCategory 33) in the ClassJob sheet's order, with the character's levels.</param>
    public static uint Crafter(SatisfierJobSettings settings, IReadOnlyList<JobLevel> crafters) => settings.Choice switch
    {
        // Satisfier's GetCurrentCrafterJob keeps the selected crafter unless the current job is 0 (its condition is inverted), so
        // "Current" crafts with the selected crafter. Checked against Satisfier 0.0.0.50.
        SatisfierJobChoice.Current => settings.CraftJob,
        SatisfierJobChoice.LowestXP => Lowest(crafters) ?? settings.CraftJob,
        SatisfierJobChoice.HighestXP => Highest(crafters) ?? settings.CraftJob,
        _ => settings.CraftJob,
    };

    /// <param name="gatherers">Miner and Botanist in the ClassJob sheet's order, with the character's levels.</param>
    /// <param name="currentJob">The character's job now: "Current" gathers with it when it is Miner or Botanist.</param>
    public static uint Gatherer(SatisfierJobSettings settings, IReadOnlyList<JobLevel> gatherers, uint currentJob) => settings.Choice switch
    {
        SatisfierJobChoice.Current => currentJob is 16 or 17 ? currentJob : settings.GatherJob,
        SatisfierJobChoice.LowestXP => Lowest(gatherers) ?? settings.GatherJob,
        SatisfierJobChoice.HighestXP => Highest(gatherers) ?? settings.GatherJob,
        _ => settings.GatherJob,
    };

    /// <summary>The setting by its name in Satisfier ("LowestXP"), or null for a name this version of XIV MCP does not know.</summary>
    public static SatisfierJobChoice? Choice(string? value) =>
        Enum.TryParse<SatisfierJobChoice>(value, true, out var c) && Enum.IsDefined(c) && !int.TryParse(value, out _) ? c : null;

    // Satisfier orders the unlocked jobs (level 1 and up) with a stable sort, so ties go to the first in the sheet.
    private static uint? Lowest(IEnumerable<JobLevel> jobs) => jobs.Where(j => j.Level >= 1).OrderBy(j => j.Level).Select(j => (uint?)j.Job).FirstOrDefault();

    private static uint? Highest(IEnumerable<JobLevel> jobs) => jobs.Where(j => j.Level >= 1).OrderByDescending(j => j.Level).Select(j => (uint?)j.Job).FirstOrDefault();
}
