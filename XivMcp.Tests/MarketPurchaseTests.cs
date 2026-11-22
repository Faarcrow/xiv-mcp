using XivMcp.Shops;

namespace XivMcp.Tests;

public class MarketPurchaseTests
{
    [Fact]
    public void Enough_bought_is_no_shortfall()
    {
        Assert.Null(MarketPurchase.Shortfall("Rroneek Fleece", wanted: 12, got: 13, spent: 600, nextPrice: null, maxPrice: 50));
    }

    [Fact]
    public void Short_because_the_rest_costs_more_says_what_it_costs()
    {
        var message = MarketPurchase.Shortfall("Rroneek Fleece", wanted: 12, got: 1, spent: 315, nextPrice: 500, maxPrice: 350);
        Assert.Equal("Bought only 1 of 12 Rroneek Fleece (315 gil): the next listing costs 500 gil each, above your limit of 350. " +
                     "11 are still missing.", message);
    }

    [Fact]
    public void Short_because_nothing_else_is_listed()
    {
        var message = MarketPurchase.Shortfall("Rroneek Fleece", wanted: 12, got: 5, spent: 25, nextPrice: null, maxPrice: 350);
        Assert.Equal("Bought only 5 of 12 Rroneek Fleece (25 gil): nobody sells more on this world right now. 7 are still missing.", message);
    }

    private static MarketOffer Offer(ulong id, uint price, uint quantity, uint tax = 0) => new(id, price, quantity, tax);

    private static ulong[] Ids(IEnumerable<MarketOffer> offers) => offers.Select(o => o.Id).OrderBy(i => i).ToArray();

    [Fact]
    public void A_small_dearer_listing_beats_a_cheap_big_stack()
    {
        // 96 at 1,897 would cost 182k for 2 wanted; the pair at 2,000 costs 4k.
        var offers = new[] { Offer(1, 1897, 96), Offer(2, 1998, 35), Offer(3, 2000, 2), Offer(4, 2000, 2) };
        var chosen = MarketPurchase.Cheapest(offers, wanted: 2);
        Assert.Single(chosen);
        Assert.Equal(2u, chosen[0].Quantity);
        Assert.Equal(2000u, chosen[0].UnitPrice);
    }

    [Fact]
    public void Combines_small_listings_when_that_is_cheapest()
    {
        var offers = new[] { Offer(1, 100, 50), Offer(2, 150, 1), Offer(3, 160, 2) };
        Assert.Equal([2ul, 3ul], Ids(MarketPurchase.Cheapest(offers, wanted: 3)));
    }

    [Fact]
    public void Takes_the_cheap_stack_when_it_is_cheapest_in_total()
    {
        var offers = new[] { Offer(1, 1, 99), Offer(2, 500, 1) };
        Assert.Equal([1ul], Ids(MarketPurchase.Cheapest(offers, wanted: 1)));
    }

    [Fact]
    public void Counts_the_tax()
    {
        var offers = new[] { Offer(1, 100, 1, tax: 50), Offer(2, 120, 1, tax: 6) };
        Assert.Equal([2ul], Ids(MarketPurchase.Cheapest(offers, wanted: 1)));
    }

    [Fact]
    public void Not_enough_listed_takes_everything()
    {
        var offers = new[] { Offer(1, 10, 2), Offer(2, 20, 3) };
        Assert.Equal([1ul, 2ul], Ids(MarketPurchase.Cheapest(offers, wanted: 10)));
    }

    [Fact]
    public void Nothing_listed_is_empty()
    {
        Assert.Empty(MarketPurchase.Cheapest([], wanted: 3));
    }

    [Fact]
    public void A_big_stack_that_costs_more_than_the_wanted_items_at_the_limit_is_too_dear()
    {
        // 4 Deepeye Tears wanted at most 490 each; only a stack of 20 at 480 was left: 10,080 gil for 4 needed.
        Assert.True(MarketPurchase.TooDear(total: 10_080, wanted: 4, maxPrice: 490));
        // 1 Boar Hide at most 700; a stack of 4 at 700.
        Assert.True(MarketPurchase.TooDear(total: 2_940, wanted: 1, maxPrice: 700));
    }

    [Fact]
    public void Buying_a_few_extra_within_the_limit_for_the_wanted_ones_is_fine()
    {
        // 9 wanted at most 240: 2,016 gil incl. 5 % tax is within 9 x 240 + tax.
        Assert.False(MarketPurchase.TooDear(total: 1_988, wanted: 9, maxPrice: 240));
        // A cheap stack (99 at 1) for 1 wanted at most 500.
        Assert.False(MarketPurchase.TooDear(total: 104, wanted: 1, maxPrice: 500));
        // 3 at the limit plus the 5 % tax.
        Assert.False(MarketPurchase.TooDear(total: 1_575, wanted: 3, maxPrice: 500));
    }

    [Fact]
    public void One_missing_is_singular()
    {
        var message = MarketPurchase.Shortfall("Rroneek Fleece", wanted: 2, got: 1, spent: 5, nextPrice: null, maxPrice: 10);
        Assert.EndsWith("1 is still missing.", message);
    }
}
