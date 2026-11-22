using System;
using XivMcp.Shops;

namespace XivMcp.Tests;

public class BuybackLedgerTests
{
    private static readonly DateTime T0 = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string Shop = BuybackPlace.Shop;
    private static readonly string Wyn = BuybackPlace.Retainer("Wyn");

    private static BuybackSale Sale(int quantity, long? unitValue, uint id = 5, bool hq = false) => new(id, $"Item {id}", hq, quantity, unitValue);

    [Fact]
    public void Nothing_sold_never_asks()
    {
        var ledger = new BuybackLedger();
        Assert.False(ledger.Check(Shop, 15_000).Ask);
    }

    [Fact]
    public void Sales_worth_more_than_the_threshold_ask()
    {
        var ledger = new BuybackLedger();
        ledger.Add(Shop, Sale(10, 1_000));
        ledger.Add(Shop, Sale(1, 6_000, id: 7));
        var check = ledger.Check(Shop, 15_000);
        Assert.True(check.Ask);
        Assert.Equal(16_000, check.Total);
        Assert.Equal(2, check.Sales.Count);
    }

    [Fact]
    public void Sales_worth_the_threshold_or_less_close_without_asking()
    {
        var ledger = new BuybackLedger();
        ledger.Add(Shop, Sale(15, 1_000));
        var check = ledger.Check(Shop, 15_000);
        Assert.False(check.Ask);
        Assert.Equal(15_000, check.Total);
    }

    [Fact]
    public void An_unknown_value_asks()
    {
        // Online lookups off, Universalis down, or an untradable item: XIV MCP can't tell what would be lost.
        var ledger = new BuybackLedger();
        ledger.Add(Shop, Sale(1, 10));
        ledger.Add(Shop, Sale(1, null, id: 9));
        var check = ledger.Check(Shop, 15_000);
        Assert.True(check.Ask);
        Assert.Null(check.Total);
    }

    [Fact]
    public void A_threshold_of_zero_never_asks()
    {
        var ledger = new BuybackLedger();
        ledger.Add(Shop, Sale(1, null));
        ledger.Add(Wyn, Sale(99, 1_000_000));
        Assert.False(ledger.Check(Shop, 0).Ask);
        Assert.False(ledger.Check(Wyn, 0).Ask);
        Assert.False(ledger.Check(BuybackPlace.Retainer("Other"), 0, gameShowsBuyback: true).Ask);
    }

    [Fact]
    public void A_retainer_whose_buyback_xiv_mcp_did_not_fill_asks()
    {
        // The player sold things through the retainer themselves: their value is unknown.
        var ledger = new BuybackLedger();
        var check = ledger.Check(Wyn, 15_000, gameShowsBuyback: true);
        Assert.True(check.Ask);
        Assert.Null(check.Total);
        Assert.Empty(check.Sales);
    }

    [Fact]
    public void A_retainer_with_cheap_sales_of_xiv_mcp_is_dismissed_without_asking()
    {
        var ledger = new BuybackLedger();
        ledger.Add(Wyn, Sale(3, 20));
        Assert.False(ledger.Check(Wyn, 15_000, gameShowsBuyback: true).Ask);
    }

    [Fact]
    public void Places_are_kept_apart()
    {
        var ledger = new BuybackLedger();
        ledger.Add(Wyn, Sale(1, 50_000));
        Assert.False(ledger.Check(Shop, 15_000).Ask);
        Assert.False(ledger.Check(BuybackPlace.Retainer("Other"), 15_000).Ask);
        Assert.True(ledger.Check(Wyn, 15_000).Ask);
    }

    [Fact]
    public void A_place_closed_for_a_moment_is_cleared()
    {
        var ledger = new BuybackLedger();
        ledger.Add(Shop, Sale(1, 50_000));
        ledger.Seen(Shop, open: false, T0);
        Assert.NotEmpty(ledger.At(Shop));
        ledger.Seen(Shop, open: false, T0 + BuybackLedger.ClosedFor);
        Assert.Empty(ledger.At(Shop));
    }

    [Fact]
    public void A_place_that_flickers_keeps_its_sales()
    {
        // A window can be hidden for a frame while another opens over it; only a place closed for a while has lost its buyback.
        var ledger = new BuybackLedger();
        ledger.Add(Shop, Sale(1, 50_000));
        ledger.Seen(Shop, open: false, T0);
        ledger.Seen(Shop, open: true, T0 + TimeSpan.FromMilliseconds(100));
        ledger.Seen(Shop, open: false, T0 + BuybackLedger.ClosedFor);
        Assert.NotEmpty(ledger.At(Shop));
    }

    [Fact]
    public void Clearing_a_place_forgets_its_sales_only()
    {
        var ledger = new BuybackLedger();
        ledger.Add(Shop, Sale(1, 50_000));
        ledger.Add(Wyn, Sale(1, 50_000));
        ledger.Clear(Wyn);
        Assert.Empty(ledger.At(Wyn));
        Assert.NotEmpty(ledger.At(Shop));
        Assert.Equal([Shop], ledger.Places);
    }

    [Fact]
    public void Retainer_places_are_recognised()
    {
        Assert.True(BuybackPlace.IsRetainer(Wyn));
        Assert.False(BuybackPlace.IsRetainer(Shop));
        Assert.Equal("Wyn", BuybackPlace.RetainerName(Wyn));
    }

    [Theory]
    [InlineData(false, 100.0, 300.0, 150.0, 100L)]
    [InlineData(true, 100.0, 300.0, 150.0, 300L)]
    [InlineData(true, 100.0, null, 150.0, 150L)]
    [InlineData(false, null, null, null, null)]
    public void The_market_value_is_the_average_sale_price_of_that_quality(bool hq, double? nq, double? hqAverage, double? any, long? expected)
    {
        Assert.Equal(expected, BuybackRule.UnitValue(hq, nq, hqAverage, any));
    }

    [Fact]
    public void The_question_lists_each_sale_and_the_total()
    {
        var lines = BuybackRule.Describe([Sale(10, 1_000), Sale(1, null, id: 9, hq: true)], null);
        Assert.Contains("10x Item 5: about 10,000 gil", lines);
        Assert.Contains("1x Item 9 (HQ): value unknown", lines);
        Assert.Contains("Total: unknown", lines);
        Assert.Contains("Total: about 10,000 gil", BuybackRule.Describe([Sale(10, 1_000)], 10_000));
    }
}
