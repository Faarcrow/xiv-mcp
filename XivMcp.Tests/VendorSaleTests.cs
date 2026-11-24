using System;
using XivMcp.Shops;

namespace XivMcp.Tests;

public class VendorSaleTests
{
    private static readonly int[] Stacks = [999, 999, 352, 40];

    [Fact]
    public void Without_a_quantity_every_stack_is_sold()
    {
        Assert.Equal([3, 2, 0, 1], VendorSale.Pick(Stacks, quantity: null, keep: 0));
    }

    [Fact]
    public void Keeping_some_leaves_whole_stacks_behind()
    {
        // Small stacks go first, so the large ones stay.
        Assert.Equal([3, 2], VendorSale.Pick(Stacks, quantity: null, keep: 1000));
        Assert.Empty(VendorSale.Pick(Stacks, quantity: null, keep: 5000));
    }

    [Fact]
    public void A_quantity_is_never_exceeded()
    {
        Assert.Equal([3, 2], VendorSale.Pick(Stacks, quantity: 500, keep: 0));
        Assert.Equal([3], VendorSale.Pick(Stacks, quantity: 45, keep: 0));
        Assert.Empty(VendorSale.Pick(Stacks, quantity: 10, keep: 0));
    }

    [Fact]
    public void Nonsense_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VendorSale.Pick(Stacks, quantity: 0, keep: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => VendorSale.Pick(Stacks, quantity: null, keep: -1));
    }

    // Addon rows 3407 ("Sell <item> for <gil> gil?") and 399, as their fixed pieces.
    private static readonly string[] SellPrompt = ["Sell ", " for ", " gil?"];
    private static readonly string[] UntradableWarning = ["This item is unique and untradable. Are you certain you wish to sell it?"];

    [Fact]
    public void The_sell_question_is_answered_yes()
    {
        Assert.True(VendorSale.Confirms("Sell Crimson Hat for 236 gil?", SellPrompt, UntradableWarning, sellUntradable: false));
    }

    [Fact]
    public void The_untradable_warning_is_answered_yes_only_when_allowed()
    {
        const string warning = "This item is unique and untradable.\nAre you certain you wish to sell it?";
        Assert.False(VendorSale.Confirms(warning, SellPrompt, UntradableWarning, sellUntradable: false));
        Assert.True(VendorSale.Confirms(warning, SellPrompt, UntradableWarning, sellUntradable: true));
        Assert.True(VendorSale.IsUntradableWarning(warning, UntradableWarning));
    }

    private static readonly SlotContent Planned = new(ItemId: 5057, Hq: false, Quantity: 40);

    [Fact]
    public void A_slot_that_still_holds_the_planned_stack_is_sold()
    {
        Assert.Null(VendorSale.Changed(Planned, new SlotContent(5057, false, 40)));
    }

    [Fact]
    public void A_slot_the_player_changed_meanwhile_is_left_alone()
    {
        // The approval prompt can stay open for minutes: the player may sort, move, use or split the stack meanwhile.
        Assert.Equal("the slot is empty now", VendorSale.Changed(Planned, null));
        Assert.Equal("the slot is empty now", VendorSale.Changed(Planned, new SlotContent(0, false, 0)));
        Assert.Equal("another item is in the slot now", VendorSale.Changed(Planned, new SlotContent(5058, false, 40)));
        Assert.Equal("the slot holds the high-quality one now", VendorSale.Changed(Planned, new SlotContent(5057, true, 40)));
        Assert.Equal("the slot holds the normal-quality one now", VendorSale.Changed(Planned with { Hq = true }, new SlotContent(5057, false, 40)));
        Assert.Equal("the slot holds 12 now, not 40", VendorSale.Changed(Planned, new SlotContent(5057, false, 12)));
        Assert.Equal("the slot holds 99 now, not 40", VendorSale.Changed(Planned, new SlotContent(5057, false, 99)));
    }

    [Fact]
    public void The_untradable_warning_is_confirmed_only_for_an_approved_item_still_in_its_slot()
    {
        var approved = new HashSet<uint> { 5057 };
        Assert.True(VendorSale.MayConfirmUntradable(Planned, Planned, approved));
        Assert.False(VendorSale.MayConfirmUntradable(Planned, Planned, new HashSet<uint>()));
        // Another untradable item moved into the slot: its warning was never approved.
        Assert.False(VendorSale.MayConfirmUntradable(Planned, new SlotContent(6000, false, 40), approved));
        Assert.False(VendorSale.MayConfirmUntradable(Planned, null, approved));
    }

    [Fact]
    public void Other_questions_are_answered_no()
    {
        const string materia = "This item has materia attached. Are you certain you wish to sell it?";
        Assert.False(VendorSale.Confirms(materia, SellPrompt, UntradableWarning, sellUntradable: true));
        Assert.False(VendorSale.IsUntradableWarning(materia, UntradableWarning));
    }

    [Fact]
    public void Items_vendors_do_not_buy_are_refused_first()
    {
        Assert.Equal("Vendors do not buy this item.", VendorSale.Refusal(vendorsBuy: false, held: 0, inGearset: true));
    }

    [Fact]
    public void Having_none_says_where_it_looked()
    {
        // Before, an item only in the saddlebag (or nowhere) came back as "sold 0, left 0" with no word on why.
        Assert.Equal("None in the bags or the armoury chest.", VendorSale.Refusal(vendorsBuy: true, held: 0, inGearset: false));
    }

    [Fact]
    public void Gear_set_pieces_are_never_sold()
    {
        Assert.Equal("A gear set uses it, so it is not sold.", VendorSale.Refusal(vendorsBuy: true, held: 1, inGearset: true));
        Assert.Null(VendorSale.Refusal(vendorsBuy: true, held: 1, inGearset: false));
    }
}
