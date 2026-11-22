using XivMcp.Gear;
using static XivMcp.Fashion.GearSlots;
using static XivMcp.Gear.GearsetPieces;

namespace XivMcp.Tests;

public class GearsetPiecesTests
{
    private const uint Hq = HqOffset;

    private static Held InArmoury(uint itemId, int slot = 0) => new(itemId, Place.Armoury, "ArmoryRings", slot);

    [Fact]
    public void The_old_belt_slot_is_not_a_piece()
    {
        var matched = Match([(MainHand, 100u), (Waist, 23505u)], [InArmoury(100)]);
        Assert.Equal([MainHand], matched.Select(m => m.Slot));
        Assert.All(matched, m => Assert.NotNull(m.Held));
    }

    [Fact]
    public void Empty_slots_are_left_out()
    {
        Assert.Empty(Match([(OffHand, 0u)], []));
    }

    [Fact]
    public void A_piece_that_is_not_held_is_missing()
    {
        var matched = Match([(Head, 200u), (Body, 201u)], [InArmoury(200)]);
        Assert.NotNull(matched.Single(m => m.Slot == Head).Held);
        Assert.Null(matched.Single(m => m.Slot == Body).Held);
    }

    [Fact]
    public void A_ring_on_both_hands_needs_two()
    {
        var one = Match([(RingRight, 34152 + Hq), (RingLeft, 34152 + Hq)], [InArmoury(34152 + Hq, 20)]);
        Assert.Single(one, m => m.Held is null);

        var two = Match([(RingRight, 34152 + Hq), (RingLeft, 34152 + Hq)], [InArmoury(34152 + Hq, 20), InArmoury(34152 + Hq, 23)]);
        Assert.Equal([20, 23], two.Select(m => m.Held!.Slot));
    }

    [Fact]
    public void The_other_quality_counts_when_the_saved_one_is_not_held()
    {
        var matched = Match([(Feet, 300 + Hq)], [InArmoury(300)]);
        Assert.Equal(300u, matched.Single().Held!.ItemId);
    }

    [Fact]
    public void The_exact_quality_goes_to_the_piece_saved_with_it()
    {
        // The NQ piece comes first, but taking the HQ ring for it would leave the HQ piece with the wrong one.
        var matched = Match([(RingRight, 400u), (RingLeft, 400 + Hq)], [InArmoury(400 + Hq, 1), InArmoury(400, 2)]);
        Assert.Equal(400u, matched.Single(m => m.Slot == RingRight).Held!.ItemId);
        Assert.Equal(400 + Hq, matched.Single(m => m.Slot == RingLeft).Held!.ItemId);
    }

    [Fact]
    public void Worn_items_come_before_the_armoury_and_bags()
    {
        var matched = Match([(Hands, 500u)], [new Held(500, Place.Bags, "Inventory1", 3), InArmoury(500), new Held(500, Place.Equipped, "EquippedItems", Hands)]);
        Assert.Equal(Place.Equipped, matched.Single().Held!.Place);
    }

    [Fact]
    public void Held_gear_sorted_once_serves_every_set_on_its_own()
    {
        // All() matches up to 100 sets against the same items; one set taking an item must not take it from the next.
        var holdings = new Holdings([new Held(600, Place.Bags, "Inventory1", 3), new Held(600, Place.Equipped, "EquippedItems", Legs)]);
        var first = holdings.Match([(Legs, 600u)]);
        var second = holdings.Match([(Legs, 600u)]);
        Assert.Equal(Place.Equipped, first.Single().Held!.Place);
        Assert.Equal(Place.Equipped, second.Single().Held!.Place);
    }
}
