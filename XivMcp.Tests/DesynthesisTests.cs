using System;
using XivMcp.Crafting;

namespace XivMcp.Tests;

public class DesynthesisTests
{
    private static readonly int[] Stacks = [99, 17, 1];

    [Fact]
    public void Without_a_quantity_every_stack_goes_smallest_first()
    {
        Assert.Equal([new DesynthStep(2, 1, false), new DesynthStep(1, 17, true), new DesynthStep(0, 99, true)],
            Desynthesis.Plan(Stacks, quantity: null, keep: 0));
    }

    [Fact]
    public void A_single_item_is_no_bulk_run()
    {
        Assert.Equal([new DesynthStep(0, 1, false)], Desynthesis.Plan([1], quantity: null, keep: 0));
    }

    [Fact]
    public void A_stack_used_only_partly_goes_item_by_item()
    {
        Assert.Equal([new DesynthStep(2, 1, false), new DesynthStep(1, 9, false)], Desynthesis.Plan(Stacks, quantity: 10, keep: 0));
        Assert.Equal([new DesynthStep(2, 1, false), new DesynthStep(1, 17, true), new DesynthStep(0, 89, false)],
            Desynthesis.Plan(Stacks, quantity: null, keep: 10));
    }

    [Fact]
    public void A_quantity_ending_on_a_whole_stack_takes_it_in_bulk()
    {
        Assert.Equal([new DesynthStep(2, 1, false), new DesynthStep(1, 17, true)], Desynthesis.Plan(Stacks, quantity: 18, keep: 0));
    }

    [Fact]
    public void Equal_stacks_go_in_bag_order()
    {
        Assert.Equal([new DesynthStep(0, 5, true), new DesynthStep(1, 2, false)], Desynthesis.Plan([5, 5], quantity: null, keep: 3));
    }

    [Fact]
    public void Every_plan_stays_within_its_limits()
    {
        int[][] layouts = [[99, 17, 1], [5, 5], [1], [3, 999, 3, 1]];
        int?[] quantities = [null, 1, 2, 17, 18, 100, 5000];
        int[] keeps = [0, 1, 3, 50, 1000];
        foreach (var stacks in layouts)
        foreach (var quantity in quantities)
        foreach (var keep in keeps)
        {
            var steps = Desynthesis.Plan(stacks, quantity, keep);
            Assert.Equal(Math.Max(0, Math.Min(quantity ?? int.MaxValue, stacks.Sum() - keep)), steps.Sum(s => s.Count));
            Assert.All(steps, s => Assert.InRange(s.Count, 1, stacks[s.Stack]));
            Assert.All(steps, s => Assert.Equal(s.Count == stacks[s.Stack] && s.Count > 1, s.Bulk));
            Assert.Equal(steps.Count, steps.Select(s => s.Stack).Distinct().Count());
            // Only the last run may use a stack partly.
            Assert.All(steps.SkipLast(1), s => Assert.Equal(stacks[s.Stack], s.Count));
        }
    }

    [Fact]
    public void Keeping_everything_plans_nothing()
    {
        Assert.Empty(Desynthesis.Plan(Stacks, quantity: null, keep: 117));
        Assert.Empty(Desynthesis.Plan(Stacks, quantity: 5, keep: 500));
        Assert.Empty(Desynthesis.Plan([], quantity: null, keep: 0));
    }

    [Fact]
    public void Nonsense_counts_are_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Desynthesis.Plan(Stacks, quantity: 0, keep: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Desynthesis.Plan(Stacks, quantity: null, keep: -1));
    }

    private static readonly DesynthItem Fish = new(Desynthesizable: true, Unique: false, Untradable: false, Collectable: false, Melded: false, InGearset: false, Glamoured: false);

    [Fact]
    public void A_plain_item_may_go()
    {
        Assert.Null(Desynthesis.Refusal(Fish));
    }

    [Fact]
    public void Items_the_game_would_warn_about_are_left_alone()
    {
        Assert.NotNull(Desynthesis.Refusal(Fish with { Desynthesizable = false }));
        Assert.NotNull(Desynthesis.Refusal(Fish with { Untradable = true }));
        Assert.NotNull(Desynthesis.Refusal(Fish with { Unique = true }));
        Assert.NotNull(Desynthesis.Refusal(Fish with { Collectable = true }));
        Assert.NotNull(Desynthesis.Refusal(Fish with { Melded = true }));
        Assert.NotNull(Desynthesis.Refusal(Fish with { InGearset = true }));
    }

    [Fact]
    public void Glamoured_gear_is_left_alone_like_melded_gear()
    {
        Assert.Equal("It is glamoured; desynthesize it by hand if you mean to.", Desynthesis.Refusal(Fish with { Glamoured = true }));
    }

    [Fact]
    public void One_item_is_asked_by_name()
    {
        var approval = Desynthesis.Approval([new DesynthCount("Cobalt Ingot", 5, 0)]);
        Assert.Equal("Desynthesize 5 × Cobalt Ingot?", approval.Title);
        Assert.Equal(["5 × Cobalt Ingot"], approval.Details);
    }

    [Fact]
    public void Several_items_are_asked_by_their_total()
    {
        var approval = Desynthesis.Approval([new DesynthCount("Cobalt Ingot", 5, 0), new DesynthCount("Rarefied Lumber", 3, 1)]);
        Assert.Equal("Desynthesize 8 items?", approval.Title);
        Assert.Equal(["5 × Cobalt Ingot", "3 × Rarefied Lumber (1 of them HQ)"], approval.Details);
    }

    [Fact]
    public void Items_all_HQ_say_so_once()
    {
        Assert.Equal(["4 × Mythrite Ingot (HQ)"], Desynthesis.Approval([new DesynthCount("Mythrite Ingot", 4, 4)]).Details);
    }

    [Fact]
    public void An_item_listed_twice_is_named()
    {
        Assert.Equal("Cobalt Ingot is listed more than once; list each item once.",
            Desynthesis.ListedTwice([(5057u, "Cobalt Ingot"), (5058u, "Mythrite Ingot"), (5057u, "Cobalt Ingot")]));
        Assert.Null(Desynthesis.ListedTwice([(5057u, "Cobalt Ingot"), (5058u, "Mythrite Ingot")]));
    }
}
