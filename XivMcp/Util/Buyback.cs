using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using XivMcp.Mcp;
using XivMcp.Permissions;
using XivMcp.Shops;

namespace XivMcp.Util;

/// <summary>
/// Keeps what sell_to_vendor sold within reach of the buyback lists: a merchant's buyback tab lasts until the shop window closes, a
/// retainer's "Item buyback" until the retainer is dismissed. Every XIV MCP code path that closes the "Shop" window goes through
/// <see cref="ConfirmShopClose"/> (or <see cref="CloseShop"/>), which asks in game first when the sold items there are worth more than
/// the player's limit or their value is unknown (<see cref="BuybackRule"/>). What the player closes themselves is their choice and is
/// never held up. The ledger forgets a place once the game closed it, whoever closed it.
/// </summary>
internal static class Buyback
{
    public static BuybackLedger Ledger { get; } = new();

    private static Func<int> threshold = () => Configuration.DefaultBuybackLimit;

    private static readonly TimeSpan ConsentTimeout = TimeSpan.FromMinutes(2);

    private const string DeclinedKey = "XivMcp.Buyback.Declined";

    /// <summary>Starts watching the shop and retainers with the player's limit.</summary>
    public static void Start(Configuration config)
    {
        threshold = () => config.AskBeforeLosingBuybackAboveGil;
        Svc.Framework.Update += OnUpdate;
    }

    public static void Stop() => Svc.Framework.Update -= OnUpdate;

    /// <summary>The limit set in /xivmcp (0: never ask).</summary>
    public static long Threshold => threshold();

    /// <summary>A merchant's gil shop is shown. Framework thread.</summary>
    public static bool ShopOpen => RetainerUi.Ptr("Shop") is { IsNull: false, IsVisible: true };

    /// <summary>Where a sale made right now goes: the shop if it is open, else the retainer being talked to, else null. Framework thread.</summary>
    public static string? SalePlace() =>
        ShopOpen ? BuybackPlace.Shop : RetainerUi.ActiveRetainerName is { } retainer ? BuybackPlace.Retainer(retainer) : null;

    /// <summary>Whether to ask before dismissing this retainer, whose menu shows "Item buyback.".</summary>
    public static BuybackCheck CheckRetainer(string retainer) => Ledger.Check(BuybackPlace.Retainer(retainer), Threshold, gameShowsBuyback: true);

    private static void OnUpdate(IFramework framework)
    {
        var places = Ledger.Places;
        if (places.Count == 0) return;
        var now = DateTime.UtcNow;
        // A retainer is still summoned while the player is at the bell and the retainer list isn't shown (the list comes back once it left).
        var summoned = RetainerUi.AtBell && !RetainerUi.RetainerListOpen ? ActiveRetainer() : null;
        foreach (var place in places)
            Ledger.Seen(place, BuybackPlace.IsRetainer(place) ? BuybackPlace.RetainerName(place) == summoned : ShopOpen, now);
    }

    /// <summary>The retainer the game reports as active (it keeps reporting it back at the list, hence the checks above). Framework thread.</summary>
    private static unsafe string? ActiveRetainer()
    {
        var rm = RetainerManager.Instance();
        var r = rm == null ? null : rm->GetActiveRetainer();
        return r == null ? null : r->NameString;
    }

    /// <summary>
    /// Call before XIV MCP closes the merchant's shop window (or anything that ends the conversation it belongs to). Returns at once
    /// when no shop is open or nothing worth asking about is in its buyback; otherwise asks in game. <paramref name="caller"/> (null:
    /// the caller of the tool call running now) is who the player is told asks, and whose session approvals count: plugins are asked
    /// every time. Throws when the player declines; the shop then stays open (see <see cref="Declined"/>).
    /// </summary>
    public static async Task ConfirmShopClose(Caller? caller, CancellationToken ct)
    {
        if (!await Game.Run(() => ShopOpen).ConfigureAwait(false)) return;
        var check = Ledger.Check(BuybackPlace.Shop, Threshold);
        if (!check.Ask) return;
        caller ??= CallScope.Current;
        var details = new List<string> { "Items XIV MCP sold here can be bought back from the shop's buyback tab until it closes:" };
        details.AddRange(BuybackRule.Describe(check.Sales, check.Total));
        Consent.Request Request(bool offerSession) => new("Close the shop and lose the buyback?", details)
        {
            Deadline = DateTime.UtcNow + ConsentTimeout,
            Source = caller?.Name ?? "your AI assistant",
            Warning = "Can't be undone.",
            Risk = RiskLevel.High,
            OfferSession = offerSession,
        };
        // Kept (and audited) as sell_to_vendor's question, whichever tool closes the shop.
        if (await Consent.AskDuringCall("sell_to_vendor", ["close_shop_with_buyback"], caller, Request, ConsentTimeout, ct).ConfigureAwait(false)) return;
        var declined = new ToolException("The shop holds items XIV MCP sold, which can be bought back (its buyback tab) only until it closes, " +
                                         $"worth {(check.Total is { } t ? $"about {t:N0} gil" : "an unknown amount")}. The player did not approve closing it " +
                                         "in game, so the shop stays open. Buy them back, or ask the player to close it.");
        declined.Data[DeclinedKey] = true;
        throw declined;
    }

    /// <summary>Closes the merchant's shop window after <see cref="ConfirmShopClose"/>. No-op when it isn't open.</summary>
    public static async Task CloseShop(Caller? caller, CancellationToken ct)
    {
        await ConfirmShopClose(caller, ct).ConfigureAwait(false);
        await Game.Run(() =>
        {
            unsafe
            {
                if (ShopOpen) RetainerUi.Fire(GameWindows.Addon("Shop"), true, -1);
            }
            return true;
        }).ConfigureAwait(false);
    }

    /// <summary>Whether <paramref name="e"/> is the player declining to give up the buyback: trying again would only ask again.</summary>
    public static bool Declined(Exception e) => e.Data.Contains(DeclinedKey);

    /// <summary>
    /// The market value of one of each item (average recent sale price on the home world, by quality), in one request. Empty when
    /// online lookups are off or Universalis doesn't answer: their value is then unknown, which asks.
    /// </summary>
    public static async Task<Dictionary<uint, (long? Nq, long? Hq)>> MarketValues(IReadOnlyCollection<uint> itemIds, bool online, CancellationToken ct)
    {
        var values = new Dictionary<uint, (long?, long?)>();
        if (!online || itemIds.Count == 0) return values;
        try
        {
            var world = await Game.Run(() => Universalis.HomeScopes().World).ConfigureAwait(false);
            foreach (var (id, m) in await Universalis.Get(itemIds, world, 0, 10, ct).ConfigureAwait(false))
                values[id] = (BuybackRule.UnitValue(false, m.AveragePriceNq, m.AveragePriceHq, m.AveragePrice),
                              BuybackRule.UnitValue(true, m.AveragePriceNq, m.AveragePriceHq, m.AveragePrice));
        }
        catch (ToolException e)
        {
            Svc.Log.Warning($"[MCP] No market values for the buyback check: {e.Message}");
        }
        return values;
    }
}
