using System.Collections.Generic;
using System.Linq;

namespace XivMcp.Crafting;

/// <summary>
/// craft_item (one item crafted a number of times) and what its job step keeps between runs (resume after a pause, retry). It has
/// no crafting list, so its memory is a <see cref="CraftListMemory"/> without one (<see cref="CraftListMemory.NoList"/>): its recipe
/// with the amount asked for, as both the original and the written list.
/// </summary>
public static class CraftItemStep
{
    /// <summary>
    /// The recipe an earlier run of the step crafted, or null when the memory is not a craft_item one or was for another amount: then
    /// the step starts over (see <see cref="Resume"/>), and the recipe is picked as for a first run.
    /// </summary>
    public static uint? RememberedRecipe(CraftListMemory? memory, int amount) =>
        memory is { ListId: CraftListMemory.NoList, Original: [var r] } && r.Crafts == amount ? r.RecipeId : null;

    /// <summary>
    /// Of the recipes that make the item (some items have one per job, e.g. ingots for blacksmiths and armorers): the one an
    /// earlier run of the step used, so a job change in between does not start the step over; else the current job's; else the first.
    /// </summary>
    public static uint PickRecipe(IReadOnlyList<(uint RecipeId, uint JobId)> candidates, uint currentJob, uint? remembered)
    {
        if (remembered is { } r && candidates.Any(c => c.RecipeId == r)) return r;
        return candidates.FirstOrDefault(c => c.JobId == currentJob) is { RecipeId: not 0 } mine ? mine.RecipeId : candidates[0].RecipeId;
    }

    /// <summary>What an earlier run of the step made, and how many crafts to ask the crafting plugin for: the ones still missing.</summary>
    public static (CraftListResumePlan Plan, int Crafts) Resume(CraftListMemory? memory, uint recipeId, int amount)
    {
        var plan = CraftListResume.Plan(memory, CraftListMemory.NoList, [new RecipeCount(recipeId, amount)]);
        return (plan, plan.Shrink is [var left] ? left.Crafts : amount);
    }

    public static CraftListMemory Memory(uint recipeId, int amount, Dictionary<uint, int> done) =>
        new(CraftListMemory.NoList, [new RecipeCount(recipeId, amount)], [new RecipeCount(recipeId, amount)], done);

    /// <summary>The result's note on earlier runs, or null for a run that crafts everything itself.</summary>
    public static string? Note(CraftListResumePlan plan, int crafts) =>
        plan.AllDone ? "Every craft was already made by an earlier run of this step."
        : crafts < plan.Original[0].Crafts ? $"Continued an earlier run: Artisan was asked only for the {crafts} craft(s) still missing."
        : null;
}
