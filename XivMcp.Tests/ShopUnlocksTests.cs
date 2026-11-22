using System.Collections.Generic;
using XivMcp.Shops;

namespace XivMcp.Tests;

public class ShopUnlocksTests
{
    private const uint Tooling = 66959;

    [Fact]
    public void AnItemOnlyQuestLockedShopsSellNamesTheQuest()
    {
        var done = new HashSet<uint>();
        Assert.Equal(Tooling, ShopUnlocks.MissingQuest([Tooling, Tooling], soldForGil: false, done.Contains));
    }

    [Fact]
    public void ACompletedQuestUnlocksTheShop()
    {
        var done = new HashSet<uint> { Tooling };
        Assert.Null(ShopUnlocks.MissingQuest([Tooling], soldForGil: false, done.Contains));
    }

    [Fact]
    public void AShopWithoutQuestKeepsTheItemBuyable()
    {
        Assert.Null(ShopUnlocks.MissingQuest([Tooling, 0], soldForGil: false, _ => false));
    }

    [Fact]
    public void AGilShopKeepsTheItemBuyable()
    {
        Assert.Null(ShopUnlocks.MissingQuest([Tooling], soldForGil: true, _ => false));
    }

    [Fact]
    public void AnItemNoExchangeSellsIsNotLocked()
    {
        Assert.Null(ShopUnlocks.MissingQuest([], soldForGil: false, _ => false));
    }

    [Fact]
    public void AUniqueItemAlreadyLearnedIsNotBoughtAgain()
    {
        // A master recipe book once read: the scrip exchange refuses to sell it again.
        Assert.True(ShopUnlocks.AlreadyLearned(unlockable: true, unlocked: true, unique: true));
    }

    [Fact]
    public void ItemsNotLearnedYetOrNotUniqueCanBeBought()
    {
        Assert.False(ShopUnlocks.AlreadyLearned(unlockable: true, unlocked: false, unique: true));
        Assert.False(ShopUnlocks.AlreadyLearned(unlockable: true, unlocked: true, unique: false));
        Assert.False(ShopUnlocks.AlreadyLearned(unlockable: false, unlocked: false, unique: true));
    }
}
