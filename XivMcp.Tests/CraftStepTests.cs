using XivMcp.Quests;

namespace XivMcp.Tests;

public class CraftStepTests
{
    [Theory]
    [InlineData(1, 0, 1, "Any")]
    [InlineData(1, 1, 0, null)]
    [InlineData(3, 1, 2, "Any")]
    [InlineData(1, 0, 1, "HQ")]
    [InlineData(1, 1, 0, "NQ")]
    public void A_craft_step_whose_items_are_in_the_bags_is_done(int wanted, int nq, int hq, string? quality) =>
        Assert.True(CraftStep.IsDone("Craft", wanted, nq, hq, quality));

    [Fact]
    public void A_purchase_step_whose_item_is_in_the_bags_is_done()
    {
        Assert.True(CraftStep.IsDone("PurchaseItem", 1, 13, 0, null));
        Assert.False(CraftStep.IsDone("PurchaseItem", 4, 1, 0, null));
    }

    [Theory]
    [InlineData("Craft", 1, 0, 0, "Any")]
    [InlineData("Craft", 3, 1, 1, "Any")]
    [InlineData("Craft", 1, 1, 0, "HQ")]  // only NQ owned, HQ asked
    [InlineData("Craft", 1, 0, 1, "NQ")]  // only HQ owned, NQ asked
    [InlineData("Craft", null, 5, 5, "Any")]
    [InlineData("Interact", 1, 1, 1, "Any")]
    public void Otherwise_Questionable_crafts(string type, int? wanted, int nq, int hq, string? quality) =>
        Assert.False(CraftStep.IsDone(type, wanted, nq, hq, quality));

    [Fact]
    public void A_step_is_skipped_only_when_its_items_were_owned_at_the_first_look()
    {
        var steps = new OwnedAtStart();
        Assert.True(steps.Skip("3904/255/0", doneNow: true));
        Assert.True(steps.Skip("3904/255/0", doneNow: true));

        // Chief Concerns (3900), sequence 5, 2026-11-25: Questionable crafted the item itself; it showed up as the step ended.
        Assert.False(steps.Skip("3900/5/0", doneNow: false));
        Assert.False(steps.Skip("3900/5/0", doneNow: true));
    }

    [Fact]
    public void Items_owned_at_the_first_look_but_gone_later_are_not_skipped()
    {
        var steps = new OwnedAtStart();
        Assert.True(steps.Skip("3906/2/0", doneNow: true));
        Assert.False(steps.Skip("3906/2/0", doneNow: false));
    }

    [Fact]
    public void A_step_reached_again_after_another_is_looked_at_afresh()
    {
        var steps = new OwnedAtStart();
        Assert.False(steps.Skip("3900/5/0", doneNow: false));
        Assert.True(steps.Skip("3900/6/0", doneNow: true));
        Assert.True(steps.Skip("3900/5/0", doneNow: true));
    }
}
