using XivMcp.Crafting;

namespace XivMcp.Tests;

public class CraftItemStepTests
{
    // Iron Ingot: a blacksmith (job 10) and an armorer (job 11) recipe.
    private static readonly List<(uint RecipeId, uint JobId)> IronIngot = [(43, 10), (600, 11)];

    [Fact]
    public void The_current_job_s_recipe_is_preferred_and_otherwise_the_first()
    {
        Assert.Equal(600u, CraftItemStep.PickRecipe(IronIngot, currentJob: 11, remembered: null));
        Assert.Equal(43u, CraftItemStep.PickRecipe(IronIngot, currentJob: 9, remembered: null));
    }

    [Fact]
    public void A_step_run_again_keeps_the_recipe_it_used_although_the_job_changed()
    {
        // The first run crafted as a blacksmith; Artisan switched gearsets meanwhile, so the character is an armorer now. Picking the
        // armorer's recipe would not match the step's memory, and the step would start over.
        Assert.Equal(43u, CraftItemStep.PickRecipe(IronIngot, currentJob: 11, remembered: 43));
        // A remembered recipe for another item does not count.
        Assert.Equal(600u, CraftItemStep.PickRecipe(IronIngot, currentJob: 11, remembered: 1008));
    }

    [Fact]
    public void Only_a_craft_item_memory_names_a_recipe()
    {
        Assert.Equal(1008u, CraftItemStep.RememberedRecipe(CraftItemStep.Memory(1008, 3, new() { [1008] = 2 }), amount: 3));
        Assert.Null(CraftItemStep.RememberedRecipe(new CraftListMemory(7, [new(1008, 3)], [new(1008, 3)], []), amount: 3));
        Assert.Null(CraftItemStep.RememberedRecipe(null, amount: 3));
    }

    [Fact]
    public void A_memory_for_another_amount_names_no_recipe()
    {
        // A step that inherits the memory of a failed craft_item step with another amount starts over (see Resume), so the earlier
        // run's recipe must not win over the current job's either.
        var memory = CraftItemStep.Memory(43, 3, new() { [43] = 2 });
        Assert.Null(CraftItemStep.RememberedRecipe(memory, amount: 5));
        Assert.Empty(CraftItemStep.Resume(memory, 43, 5).Plan.Done);
    }

    [Fact]
    public void A_step_run_again_asks_only_for_the_crafts_still_missing()
    {
        var memory = CraftItemStep.Memory(1008, 3, new() { [1008] = 2 });
        var (plan, crafts) = CraftItemStep.Resume(memory, 1008, 3);
        Assert.Equal(1, crafts);
        Assert.Equal(2, plan.Done[1008]);
        Assert.Equal("Continued an earlier run: Artisan was asked only for the 1 craft(s) still missing.", CraftItemStep.Note(plan, crafts));
    }

    [Fact]
    public void A_first_run_or_another_amount_asks_for_every_craft()
    {
        var (first, all) = CraftItemStep.Resume(null, 1008, 3);
        Assert.Equal(3, all);
        Assert.Null(CraftItemStep.Note(first, all));
        // Also an amount that equals the crafts a retry of the earlier step asked for: that is a new batch.
        var memory = CraftItemStep.Memory(1008, 3, new() { [1008] = 2 });
        var (other, one) = CraftItemStep.Resume(memory, 1008, 1);
        Assert.Equal(1, one);
        Assert.Empty(other.Done);
        Assert.Null(CraftItemStep.Note(other, one));
    }

    [Fact]
    public void Everything_made_by_earlier_runs_needs_no_crafting()
    {
        var (plan, crafts) = CraftItemStep.Resume(CraftItemStep.Memory(1008, 3, new() { [1008] = 3 }), 1008, 3);
        Assert.True(plan.AllDone);
        Assert.Equal("Every craft was already made by an earlier run of this step.", CraftItemStep.Note(plan, crafts));
    }
}
