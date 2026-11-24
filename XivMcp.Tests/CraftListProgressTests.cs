using XivMcp.Crafting;

namespace XivMcp.Tests;

public class CraftListProgressTests
{
    private static readonly DateTime T0 = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    // Ingot (item 10, ingredient 1) → Plate (item 20, uses ingots) → Hammer (item 30, uses plates).
    private static readonly ListRecipe Ingot = new(100, "Ingot", 10, 6, 1, []);
    private static readonly ListRecipe Plate = new(200, "Plate", 20, 3, 1, []);
    private static readonly ListRecipe Hammer = new(300, "Wrapped Hammer", 30, 1, 1, ["Steel Hammer"]);

    private static Dictionary<uint, int> Counts(int ore, int ingot, int plate, int hammer) => new() { [1] = ore, [10] = ingot, [20] = plate, [30] = hammer };

    [Fact]
    public void Crafts_are_counted_from_items_added_even_when_later_crafts_use_them_up()
    {
        var p = new CraftListProgress([Ingot, Plate], Counts(12, 0, 0, 0), T0);
        p.Observe(Counts(10, 0, 0, 0), T0.AddSeconds(5));  // synthesis started: ore used
        p.Observe(Counts(10, 1, 0, 0), T0.AddSeconds(30)); // ingot done
        for (var i = 2; i <= 6; i++) p.Observe(Counts(12 - 2 * i, i, 0, 0), T0.AddSeconds(30 * i));
        p.Observe(Counts(0, 4, 0, 0), T0.AddSeconds(200)); // plate synthesis took 2 ingots
        p.Observe(Counts(0, 4, 1, 0), T0.AddSeconds(230));
        Assert.Equal(6, p.CraftsDone(Ingot.RecipeId));
        Assert.Equal(1, p.CraftsDone(Plate.RecipeId));
        Assert.Equal(Plate, p.Current);
    }

    [Fact]
    public void Multi_yield_recipes_count_crafts_not_items()
    {
        var shafts = new ListRecipe(400, "Shaft", 40, 2, 3, []);
        var p = new CraftListProgress([shafts], new Dictionary<uint, int> { [40] = 0 }, T0);
        p.Observe(new Dictionary<uint, int> { [40] = 3 }, T0.AddSeconds(30));
        Assert.Equal(1, p.CraftsDone(400));
        p.Observe(new Dictionary<uint, int> { [40] = 6 }, T0.AddSeconds(60));
        Assert.Equal(2, p.CraftsDone(400));
        Assert.Null(p.Current);
        Assert.True(p.Finished);
    }

    [Fact]
    public void Items_made_by_two_entries_fill_them_in_list_order()
    {
        var first = new ListRecipe(1, "Rivets", 50, 2, 1, []);
        var second = new ListRecipe(2, "Rivets", 50, 3, 1, []);
        var p = new CraftListProgress([first, second], new Dictionary<uint, int> { [50] = 0 }, T0);
        p.Observe(new Dictionary<uint, int> { [50] = 4 }, T0.AddSeconds(5));
        Assert.Equal(2, p.CraftsDone(1));
        Assert.Equal(2, p.CraftsDone(2));
    }

    [Fact]
    public void Earlier_progress_is_kept()
    {
        var p = new CraftListProgress([Ingot, Plate], Counts(0, 6, 0, 0), T0, new Dictionary<uint, int> { [100] = 6 });
        Assert.Equal(6, p.CraftsDone(100));
        Assert.Equal(Plate, p.Current);
        Assert.False(p.MadeProgressThisRun);
    }

    [Fact]
    public void No_count_change_for_the_stall_time_is_a_stall()
    {
        var p = new CraftListProgress([Ingot, Plate, Hammer], Counts(0, 0, 0, 0), T0);
        var stall = TimeSpan.FromMinutes(5);
        Assert.False(p.Stalled(T0.AddMinutes(4), stall));
        p.Observe(Counts(0, 1, 0, 0), T0.AddMinutes(4));
        Assert.False(p.Stalled(T0.AddMinutes(8), stall));
        p.Observe(Counts(0, 1, 0, 0), T0.AddMinutes(8)); // nothing changed
        Assert.True(p.Stalled(T0.AddMinutes(9), stall));
    }

    [Fact]
    public void Other_activity_postpones_the_stall()
    {
        var p = new CraftListProgress([Ingot], Counts(0, 0, 0, 0), T0);
        p.Touch(T0.AddMinutes(4)); // e.g. Artisan's solver is still working
        Assert.False(p.Stalled(T0.AddMinutes(8), TimeSpan.FromMinutes(5)));
        Assert.True(p.Stalled(T0.AddMinutes(9), TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void A_zero_stall_time_never_stalls()
    {
        var p = new CraftListProgress([Ingot], Counts(0, 0, 0, 0), T0);
        Assert.False(p.Stalled(T0.AddHours(5), TimeSpan.Zero));
    }

    [Fact]
    public void Stall_message_names_the_recipe_and_the_equipment_ingredient()
    {
        var p = new CraftListProgress([Ingot, Hammer], Counts(0, 0, 0, 0), T0, new Dictionary<uint, int> { [100] = 6 });
        var message = p.StallMessage("MCP: Hammer", TimeSpan.FromMinutes(5));
        Assert.Contains("Wrapped Hammer", message);
        Assert.Contains("recipe 300", message);
        Assert.Contains("Steel Hammer", message);
        Assert.Contains("equipment", message);
        Assert.Contains("by hand", message);
    }

    [Fact]
    public void Stall_message_without_equipment_lists_the_likely_causes()
    {
        var p = new CraftListProgress([Ingot], Counts(0, 0, 0, 0), T0);
        var message = p.StallMessage("L", TimeSpan.FromMinutes(5));
        Assert.Contains("Ingot", message);
        Assert.DoesNotContain("equipment as an ingredient", message);
        Assert.Contains("missing materials", message);
    }

    [Fact]
    public void Gear_note_only_for_recipes_with_equipment_ingredients()
    {
        Assert.Null(GearIngredients.Note([]));
        var note = GearIngredients.Note(["Hard Leather Grimoire"]);
        Assert.Contains("Hard Leather Grimoire", note);
        Assert.Contains("by hand", note);
        Assert.True(GearIngredients.IsGear(equipSlotCategory: 1));
        Assert.False(GearIngredients.IsGear(equipSlotCategory: 0));
    }

    [Fact]
    public void A_solver_busy_for_too_long_without_a_craft_counts_as_hung()
    {
        var p = new CraftListProgress([Ingot], Counts(2, 0, 0, 0), T0);
        var limit = TimeSpan.FromMinutes(3);
        p.SolverBusy(true, T0);
        Assert.False(p.SolverHung(T0.AddMinutes(2), limit));
        p.SolverBusy(true, T0.AddMinutes(2));
        Assert.False(p.Stalled(T0.AddMinutes(2), TimeSpan.FromMinutes(1))); // a busy solver still isn't a stall
        p.SolverBusy(true, T0.AddMinutes(3));
        Assert.True(p.SolverHung(T0.AddMinutes(3), limit));
    }

    [Fact]
    public void Crafts_or_an_idle_solver_reset_the_solver_clock()
    {
        var p = new CraftListProgress([Ingot], Counts(4, 0, 0, 0), T0);
        var limit = TimeSpan.FromMinutes(3);
        p.SolverBusy(true, T0);
        p.Observe(Counts(2, 1, 0, 0), T0.AddMinutes(2)); // a craft finished
        p.SolverBusy(true, T0.AddMinutes(4));
        Assert.False(p.SolverHung(T0.AddMinutes(4), limit));
        p.SolverBusy(false, T0.AddMinutes(5));
        p.SolverBusy(true, T0.AddMinutes(6));
        Assert.False(p.SolverHung(T0.AddMinutes(8), limit));
        Assert.False(p.SolverHung(T0.AddHours(1), TimeSpan.Zero));
    }

    [Fact]
    public void Hung_solver_message_names_the_recipe_and_is_retried_by_jobs()
    {
        var p = new CraftListProgress([Ingot], Counts(2, 0, 0, 0), T0);
        var message = p.SolverHungMessage("L", TimeSpan.FromMinutes(3));
        Assert.Contains("Ingot", message);
        Assert.True(XivMcp.Jobs.StepRetry.ShouldRetry(message, attempts: 1));
    }

    [Fact]
    public void The_run_again_note_only_appears_once_crafts_are_done()
    {
        var p = new CraftListProgress([Ingot, Plate], Counts(12, 0, 0, 0), T0);
        Assert.Equal("", p.RunAgainNote());
        p.Observe(Counts(10, 1, 0, 0), T0.AddSeconds(30));
        Assert.Contains("1 craft(s) are done", p.RunAgainNote());
        Assert.Contains("running this job step again", p.RunAgainNote());
    }

    // ---- One item crafted a number of times (craft_item): a run with a single entry.

    private static readonly ListRecipe ThreeIngots = new(100, "Bronze Ingot", 10, 3, 1, []);

    private static CraftListProgress IngotRun(int made)
    {
        var p = new CraftListProgress([ThreeIngots], new Dictionary<uint, int> { [1] = 9, [10] = 0 }, T0);
        p.Observe(new Dictionary<uint, int> { [1] = 9 - 2 * made, [10] = made }, T0.AddSeconds(30));
        return p;
    }

    [Fact]
    public void An_item_run_that_crafted_nothing_names_the_likely_causes()
    {
        var message = IngotRun(made: 0).ItemShortfall(bagsFull: false, inJob: true);
        Assert.Contains("without crafting Bronze Ingot", message);
        Assert.Contains("missing materials", message);
        Assert.Contains("wrong job or gear", message);
        Assert.DoesNotContain("again", message); // nothing to continue from
    }

    [Fact]
    public void An_item_run_cut_short_says_how_many_were_made_and_how_to_get_the_rest()
    {
        var p = IngotRun(made: 2);
        var inJob = p.ItemShortfall(bagsFull: false, inJob: true);
        Assert.Contains("2 of 3", inJob);
        Assert.Contains("running this job step again", inJob);
        // Called directly, nothing remembers the crafts: asking for the full amount again would craft it all once more.
        var direct = p.ItemShortfall(bagsFull: false, inJob: false);
        Assert.Contains("2 of 3", direct);
        Assert.Contains("amount=1", direct);
    }

    [Fact]
    public void Full_bags_are_named_as_the_cause()
    {
        var message = IngotRun(made: 1).ItemShortfall(bagsFull: true, inJob: true);
        Assert.Contains("bags are full", message);
        Assert.DoesNotContain("missing materials", message);
    }

    [Fact]
    public void Stuck_and_hung_messages_for_one_item_name_the_item_not_a_list()
    {
        var p = IngotRun(made: 2);
        var stall = p.StallMessage(listName: null, TimeSpan.FromMinutes(5));
        Assert.StartsWith("Artisan made no progress on Bronze Ingot", stall);
        Assert.DoesNotContain("''", stall);
        Assert.Contains("Bronze Ingot (recipe 100, 2 of 3 crafts done)", stall);
        Assert.Contains("missing materials", stall);
        Assert.DoesNotContain("list", stall);
        var hung = p.SolverHungMessage(listName: null, TimeSpan.FromMinutes(3));
        Assert.StartsWith("Artisan's solver hung", hung); // what jobs retry on
        Assert.Contains("Bronze Ingot (recipe 100)", hung);
        Assert.DoesNotContain("''", hung);
        Assert.DoesNotContain("list", hung);
        Assert.True(XivMcp.Jobs.StepRetry.ShouldRetry(hung, attempts: 1));
    }

    [Fact]
    public void A_finished_item_run_has_no_shortfall() =>
        Assert.Null(IngotRun(made: 3).ItemShortfall(bagsFull: true, inJob: true));

    [Fact]
    public void A_direct_call_that_answers_early_says_how_far_the_crafting_got_and_that_it_goes_on()
    {
        var note = IngotRun(made: 1).ItemStillCrafting();
        Assert.Contains("Bronze Ingot", note);
        Assert.Contains("1 of 3", note);
        Assert.Contains("job step", note);
        Assert.DoesNotMatch(@"n't", note);
    }
}
