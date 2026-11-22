using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace XivMcp.Shops;

/// <summary>
/// One stack XIV MCP sold to a vendor, still in a buyback list. <paramref name="UnitValue"/> is the market value of one (the average
/// recent sale price on the home world); null when unknown: online lookups off, the lookup failed, no recent sales, or untradable.
/// </summary>
public sealed record BuybackSale(uint ItemId, string Name, bool Hq, int Quantity, long? UnitValue);

/// <summary>
/// Whether to ask before closing a place with buyback, what is in it, and their total market value (null: unknown).
/// </summary>
public sealed record BuybackCheck(bool Ask, IReadOnlyList<BuybackSale> Sales, long? Total);

/// <summary>Where sold items wait for buyback: the merchant's shop that is open, or a retainer (by name) until it is dismissed.</summary>
public static class BuybackPlace
{
    public const string Shop = "shop";
    private const string RetainerPrefix = "retainer:";

    public static string Retainer(string name) => RetainerPrefix + name;
    public static bool IsRetainer(string place) => place.StartsWith(RetainerPrefix, StringComparison.Ordinal);
    public static string RetainerName(string place) => IsRetainer(place) ? place[RetainerPrefix.Length..] : place;
}

/// <summary>The rule for asking before XIV MCP gives up buyback, and the text of the question.</summary>
public static class BuybackRule
{
    /// <summary>The total market value, or null when any sale's value is unknown.</summary>
    public static long? Total(IEnumerable<BuybackSale> sales)
    {
        long total = 0;
        foreach (var s in sales)
        {
            if (s.UnitValue is not { } v) return null;
            total += v * s.Quantity;
        }
        return total;
    }

    /// <summary>
    /// Whether to ask before the buyback is lost. A threshold of 0 or less never asks (the player's off switch). Otherwise an unknown
    /// value asks (it may be worth anything), and a known one asks only above the threshold. Without sales of XIV MCP there, only a
    /// buyback list the game shows anyway (the player sold things themselves) asks.
    /// </summary>
    public static bool ShouldAsk(IReadOnlyList<BuybackSale> sales, long threshold, bool gameShowsBuyback)
    {
        if (threshold <= 0) return false;
        if (sales.Count == 0) return gameShowsBuyback;
        return Total(sales) is not { } total || total > threshold;
    }

    /// <summary>The market value of one: the average sale price of that quality, else of either, rounded; null when there is none.</summary>
    public static long? UnitValue(bool hq, double? averageNq, double? averageHq, double? average) =>
        ((hq ? averageHq : averageNq) ?? average) is double v and > 0 ? (long)Math.Round(v) : null;

    /// <summary>One line per sale with its value, and the total.</summary>
    public static IReadOnlyList<string> Describe(IReadOnlyList<BuybackSale> sales, long? total)
    {
        var lines = sales.Select(s => $"{s.Quantity}x {s.Name}{(s.Hq ? " (HQ)" : "")}: " +
                                      (s.UnitValue is { } v ? $"about {Gil(v * s.Quantity)} gil" : "value unknown")).ToList();
        lines.Add(total is { } t ? $"Total: about {Gil(t)} gil" : "Total: unknown");
        return lines;
    }

    private static string Gil(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}

/// <summary>
/// What XIV MCP sold to vendors that can still be bought back, by place. A sale adds to it; a place that is seen closed for
/// <see cref="ClosedFor"/> (the shop window closed by anyone, the retainer dismissed) forgets its sales, since the game forgets their
/// buyback too. Thread-safe: sales are added from tool calls, places are watched every frame.
/// </summary>
public sealed class BuybackLedger
{
    /// <summary>How long a place must be seen closed before its sales count as gone (a window can be hidden for a frame).</summary>
    public static readonly TimeSpan ClosedFor = TimeSpan.FromSeconds(1);

    private readonly Dictionary<string, List<BuybackSale>> sales = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> closedSince = new(StringComparer.Ordinal);
    private readonly Lock sync = new();

    public void Add(string place, BuybackSale sale)
    {
        lock (sync)
        {
            if (!sales.TryGetValue(place, out var list)) sales[place] = list = [];
            list.Add(sale);
            closedSince.Remove(place);
        }
    }

    public IReadOnlyList<BuybackSale> At(string place)
    {
        lock (sync) return sales.TryGetValue(place, out var list) ? list.ToList() : [];
    }

    /// <summary>The places that hold sales.</summary>
    public IReadOnlyList<string> Places
    {
        get { lock (sync) return sales.Keys.ToList(); }
    }

    public void Clear(string place)
    {
        lock (sync)
        {
            sales.Remove(place);
            closedSince.Remove(place);
        }
    }

    /// <summary>Whether the place is open right now; once it was seen closed for <see cref="ClosedFor"/>, its sales are forgotten.</summary>
    public void Seen(string place, bool open, DateTime now)
    {
        lock (sync)
        {
            if (!sales.ContainsKey(place)) return;
            if (open) { closedSince.Remove(place); return; }
            if (!closedSince.TryGetValue(place, out var since)) { closedSince[place] = now; return; }
            if (now - since >= ClosedFor) Clear(place);
        }
    }

    /// <summary>Whether to ask before closing the place (see <see cref="BuybackRule.ShouldAsk"/>), with its sales and their total value.</summary>
    public BuybackCheck Check(string place, long threshold, bool gameShowsBuyback = false)
    {
        var list = At(place);
        return new BuybackCheck(BuybackRule.ShouldAsk(list, threshold, gameShowsBuyback), list, list.Count == 0 ? null : BuybackRule.Total(list));
    }
}
