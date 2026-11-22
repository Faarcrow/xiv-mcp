using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using XivMcp.Mcp;
using XivMcp.Shops;
using XivMcp.Util;

namespace XivMcp.Tools;

/// <summary>
/// Selling items for gil, as a player does with a merchant's shop open (the context menu's "Sell") or with a retainer's
/// inventory open at a summoning bell ("Have Retainer Sell Items"), and the game's own confirmation where it asks. Whole
/// stacks only (the game sells the stack the menu was opened on).
/// </summary>
internal static class VendorTools
{
    /// <summary>
    /// The context menu's "Sell" entry (Addon rows that read "Sell"), and "Have Retainer Sell Items" (5480), which the game only
    /// shows while a retainer is open at a summoning bell.
    /// </summary>
    private static readonly uint[] SellTexts = [93, 516, 530, 3886, 9520, 17655, 5480];

    /// <summary>"Sell … for … gil?": the confirmation the game shows for some items.</summary>
    private const uint SellPrompt = 3407;

    /// <summary>"This item is unique and untradable. Are you certain you wish to sell it?"</summary>
    private const uint UntradableWarning = 399;

    private static readonly string[] InventoryAddons = ["InventoryExpansion", "InventoryLarge", "Inventory"];

    private static readonly InventoryType[] Bags = [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];

    public static IEnumerable<McpTool> Create(Configuration config)
    {
        yield return new McpTool
        {
            Name = "sell_to_vendor",
            Description = "Sells items from the bags for gil, as the inventory's context menu does: 'Sell' while a merchant's shop is " +
                          "open (talk to a merchant, open its shop), or 'Have Retainer Sell Items' while a retainer's inventory is open " +
                          "at a summoning bell (open_retainer), which saves walking to a merchant. Whole stacks, smallest first: all of " +
                          "an item, at most 'quantity', or all but 'keep'. Vendors pay little (often 1 to 10 gil each), so check the " +
                          "market board first (get_market_prices). Items a vendor does not buy are reported and left alone. Untradable items " +
                          "are only sold after the player approves them in game (one question for all of them in the call); declined ones " +
                          "are left alone. A stack the player moved or changed meanwhile is left alone and reported under 'skipped'. Sold items can be bought back: from a merchant's buyback tab until the shop is closed, or from " +
                          "the retainer's \"Item buyback\" until it is dismissed. Before XIV MCP itself closes that shop or dismisses that " +
                          "retainer, the player is asked in game when what was sold there is worth more than their limit on the market " +
                          "board (default 15,000 gil) or its value is unknown; if they decline, it stays open and the closing tool fails. " +
                          "Requires 'Market & purchases' in /xivmcp.",
            InputSchema = """
                {
                  "type": "object",
                  "properties": {
                    "items": {
                      "type": "array", "minItems": 1, "maxItems": 50,
                      "items": {
                        "type": "object",
                        "properties": {
                          "item": { "type": "string", "description": "Item name or id." },
                          "quantity": { "type": "integer", "minimum": 1, "description": "Sell at most this many (whole stacks; default all)." },
                          "keep": { "type": "integer", "minimum": 0, "description": "Keep at least this many (default 0)." }
                        },
                        "required": ["item"]
                      }
                    }
                  },
                  "required": ["items"]
                }
                """,
            ReadOnly = false,
            Destructive = true,
            Handler = async (args, ct) =>
            {
                if (!config.AllowMarketPurchases)
                    throw new ToolException("Selling is disabled. Enable \"Market & purchases\" in the XIV MCP settings window (/xivmcp) in game.");
                var wanted = (args.Array("items") ?? throw new ToolException("'items' is required.")).OfType<JsonObject>()
                    .Select(o => new ToolArgs(o))
                    .Select(a => (Query: a.String("item") ?? throw new ToolException("Each entry needs 'item'."),
                                  Quantity: a.Node("quantity") is null ? (int?)null : a.Int("quantity", 1, 1),
                                  Keep: a.Int("keep", 0, 0)))
                    .ToList();

                await InventoryActionTools.Gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var resolved = await Game.RunLoggedIn(() =>
                    {
                        if (!CanSell()) throw new ToolException("No shop or retainer inventory is open. Talk to a merchant and open its shop first (navigate_to destination npc or object), or open a retainer at a summoning bell (open_retainer), then sell.");
                        return wanted.Select(w => (Item: Items.Resolve(w.Query), w.Quantity, w.Keep)).ToList();
                    }).ConfigureAwait(false);
                    var plans = new List<(Lumina.Excel.Sheets.Item Item, List<Stack> Stacks, IReadOnlyList<int> Pick)>();
                    foreach (var (item, quantity, keep) in resolved)
                    {
                        var stacks = await Game.Run(() => StacksOf(item.RowId)).ConfigureAwait(false);
                        try { plans.Add((item, stacks, VendorSale.Pick(stacks.Select(s => s.Quantity).ToList(), quantity, keep))); }
                        catch (ArgumentOutOfRangeException e) { throw new ToolException($"{item.Name.ExtractText()}: {e.Message}"); }
                    }
                    // Only what would really be sold is asked about.
                    var untradableApproved = await ApproveUntradable(plans.Where(p => p.Pick.Count > 0).Select(p => p.Item), args.Caller, ct).ConfigureAwait(false);
                    // What is sold stays in the shop's or the retainer's buyback; its market value decides whether XIV MCP asks before
                    // closing that shop or dismissing that retainer. One lookup for everything; untradable items have no market value.
                    var place = await Game.Run(Buyback.SalePlace).ConfigureAwait(false);
                    var tradable = plans.Where(p => p.Pick.Count > 0 && !p.Item.IsUntradable && p.Item.PriceLow > 0).Select(p => p.Item.RowId).Distinct().ToList();
                    var values = await Buyback.MarketValues(tradable, config.AllowOnlineData, ct).ConfigureAwait(false);
                    long? UnitValue(Lumina.Excel.Sheets.Item item, bool hq) =>
                        !item.IsUntradable && values.TryGetValue(item.RowId, out var v) ? (hq ? v.Hq : v.Nq) : null;

                    var gilBefore = await Game.Run(Gil).ConfigureAwait(false);
                    var results = new List<object>();
                    foreach (var (item, stacks, pick) in plans)
                    {
                        var name = item.Name.ExtractText();
                        if (item.IsUntradable && item.PriceLow > 0 && pick.Count > 0 && !untradableApproved.Contains(item.RowId))
                        {
                            results.Add(new { item = name, sold = 0, note = "Untradable: the player did not approve selling it in game, so it was left alone." });
                            continue;
                        }
                        if (item.PriceLow == 0)
                        {
                            results.Add(new { item = name, sold = 0, note = "Vendors do not buy this item." });
                            continue;
                        }
                        var sold = 0;
                        string? stopped = null;
                        var skipped = new List<string>();
                        foreach (var stack in pick.Select(i => stacks[i]))
                        {
                            ct.ThrowIfCancellationRequested();
                            try
                            {
                                if (await SellStack(stack, name, untradableApproved, ct).ConfigureAwait(false) is { } changed)
                                {
                                    skipped.Add($"A stack of {stack.Quantity} was not sold: {changed}.");
                                    continue;
                                }
                                sold += stack.Quantity;
                                if (place is not null)
                                    Buyback.Ledger.Add(place, new BuybackSale(item.RowId, name, stack.Hq, stack.Quantity, UnitValue(item, stack.Hq)));
                            }
                            catch (ToolException e)
                            {
                                stopped = e.Message;
                                break;
                            }
                            await Task.Delay(Random.Shared.Next(350, 650), ct).ConfigureAwait(false);
                        }
                        var left = stacks.Sum(s => s.Quantity) - sold;
                        results.Add(new { item = name, sold, left, vendorPrice = item.PriceLow, stopped, skipped = skipped.Count > 0 ? skipped : null });
                        if (stopped is not null) break;
                    }
                    var gilAfter = await Game.Run(Gil).ConfigureAwait(false);
                    return new { sold = results, gilGained = gilAfter - gilBefore, gil = gilAfter };
                }
                finally { InventoryActionTools.Gate.Release(); }
            },
        };
    }

    private sealed record Stack(InventoryType Container, int Slot, uint ItemId, bool Hq, int Quantity)
    {
        public SlotContent Planned => new(ItemId, Hq, Quantity);
    }

    private static readonly TimeSpan ConsentTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Asks the player in game once whether the untradable items of this call may be sold: once gone (and no longer in a buyback
    /// list), they can't be bought from other players again. "For this session" (offered to the assistant only) covers just these
    /// items. Returns the untradable items that may be sold: all of them if approved, none if not.
    /// </summary>
    private static async Task<IReadOnlySet<uint>> ApproveUntradable(IEnumerable<Lumina.Excel.Sheets.Item> items, Permissions.Caller? caller, CancellationToken ct)
    {
        var untradable = items.Where(i => i.IsUntradable && i.PriceLow > 0).DistinctBy(i => i.RowId).ToList();
        if (untradable.Count == 0) return new HashSet<uint>();
        var names = untradable.Select(i => i.Name.ExtractText()).ToList();
        var details = names.Select(n => $"{n} (untradable)").ToList();
        details.Add("They can be bought back from the merchant's buyback tab, or the retainer's \"Item buyback\", until that closes.");
        Consent.Request Request(bool offerSession) => new(names.Count == 1 ? $"Sell {names[0]}?" : $"Sell {names.Count} untradable items?", details)
        {
            Deadline = DateTime.UtcNow + ConsentTimeout,
            Source = caller?.Name ?? "your AI assistant",
            Warning = "Untradable items can't be bought from other players again.",
            Risk = Permissions.RiskLevel.High,
            OfferSession = offerSession,
        };
        var approved = await Consent.AskDuringCall("sell_to_vendor", untradable.Select(i => $"untradable:{i.RowId}").ToList(), caller, Request, ConsentTimeout, ct)
                                    .ConfigureAwait(false);
        return approved ? untradable.Select(i => i.RowId).ToHashSet() : new HashSet<uint>();
    }

    /// <summary>
    /// Sells one stack: its context menu, "Sell", the confirmation if the game asks; waits until it is gone from the bag. Returns
    /// why it was left alone when its slot no longer holds the planned stack (the player may have sorted or moved items while the
    /// approval was open), else null. That is checked in the same frame the context menu opens, so nothing else can be sold.
    /// </summary>
    private static async Task<string?> SellStack(Stack stack, string name, IReadOnlySet<uint> untradableApproved, CancellationToken ct)
    {
        var changed = await Game.Run(() =>
        {
            if (VendorSale.Changed(stack.Planned, ReadSlot(stack)) is { } why) return why;
            OpenContextMenu(stack);
            return null;
        }).ConfigureAwait(false);
        if (changed is not null) return changed;
        if (!await GameWindows.WaitFor(() => GameWindows.Ready("ContextMenu"), TimeSpan.FromSeconds(3), ct).ConfigureAwait(false))
            throw new ToolException($"The context menu of {name} did not open.");
        await Game.Run(() => PickSell(name)).ConfigureAwait(false);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(200, ct).ConfigureAwait(false);
            string? question = null;
            var state = await Game.Run(() =>
            {
                if (SlotQuantity(stack) != stack.Quantity) return "sold";
                return GameWindows.Ready("SelectYesno") ? AnswerPrompt(stack, untradableApproved, out question) : "waiting";
            }).ConfigureAwait(false);
            if (state == "sold") return null;
            if (state == "untradable") throw new ToolException($"The game warned that {name} is unique and untradable, and the player had not approved selling what is in that slot; it was answered No.");
            if (state == "refused") throw new ToolException($"Selling {name} asked something unexpected (\"{question}\"); it was answered No and nothing more was sold.");
        }
        throw new ToolException($"{name} was not sold (the merchant may not buy it, or the shop closed).");
    }

    /// <summary>
    /// Answers the yes/no that came up: Yes when it asks to sell ("Sell … for … gil?"), Yes to the unique and untradable warning
    /// only when the player approved selling this item and its slot still holds the planned stack, else No.
    /// </summary>
    private static unsafe string AnswerPrompt(Stack stack, IReadOnlySet<uint> untradableApproved, out string question)
    {
        var yesno = GameWindows.Addon("SelectYesno");
        var texts = GameWindows.AllTexts(yesno);
        question = string.Join(" ", texts.Where(t => !string.IsNullOrWhiteSpace(t)));
        var sellPrompt = GameWindows.TemplatePieces(SellPrompt);
        var untradable = GameWindows.TemplatePieces(UntradableWarning);
        var sellUntradable = VendorSale.MayConfirmUntradable(stack.Planned, ReadSlot(stack), untradableApproved);
        var yes = texts.Any(t => VendorSale.Confirms(t, sellPrompt, untradable, sellUntradable));
        RetainerUi.Fire(yesno, true, yes ? 0 : 1);
        if (yes) return "confirmed";
        return texts.Any(t => VendorSale.IsUntradableWarning(t, untradable)) ? "untradable" : "refused";
    }

    private static unsafe void OpenContextMenu(Stack stack)
    {
        AtkUnitBase* owner = null;
        foreach (var name in InventoryAddons)
        {
            var a = GameWindows.Addon(name);
            if (a != null && a->IsVisible) { owner = a; break; }
        }
        AgentInventoryContext.Instance()->OpenForItemSlot(stack.Container, stack.Slot, 0, owner == null ? 0u : (uint)owner->Id);
    }

    /// <summary>Picks the context menu's "Sell" entry by its text; closes the menu and refuses when there is none.</summary>
    private static unsafe bool PickSell(string name)
    {
        var texts = GameWindows.Texts(SellTexts);
        var menu = GameWindows.Addon("ContextMenu");
        var count = (int)menu->AtkValues[0].UInt;
        for (var i = 0; i < count && 8 + i < menu->AtkValuesCount; i++)
        {
            var v = menu->AtkValues[8 + i];
            if (v.Type is not (AtkValueType.String or AtkValueType.ManagedString or AtkValueType.String8) || v.String.Value == null) continue;
            if (texts.Contains(Dalamud.Memory.MemoryHelper.ReadSeStringNullTerminated((nint)v.String.Value).TextValue.Trim()))
            {
                RetainerUi.Fire(menu, true, 0, i, 0u);
                return true;
            }
        }
        RetainerUi.Fire(menu, true, -1);
        throw new ToolException($"{name}'s context menu has no Sell entry: the shop or retainer window may have closed, or merchants do not buy it.");
    }

    /// <summary>A merchant's shop, or a retainer's inventory (whose context menu offers "Have Retainer Sell Items"), is open.</summary>
    private static bool CanSell() => GameWindows.Ready("Shop") || RetainerUi.InventoryOpen;

    private static unsafe long Gil() => InventoryManager.Instance()->GetGil();

    private static unsafe List<Stack> StacksOf(uint itemId)
    {
        var im = InventoryManager.Instance();
        var stacks = new List<Stack>();
        foreach (var container in Bags)
        {
            var c = im->GetInventoryContainer(container);
            if (c == null) continue;
            for (var i = 0; i < c->Size; i++)
            {
                var slot = c->GetInventorySlot(i);
                if (slot != null && slot->ItemId == itemId && slot->Quantity > 0)
                    stacks.Add(new Stack(container, i, itemId, slot->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality), (int)slot->Quantity));
            }
        }
        return stacks;
    }

    /// <summary>What the stack's slot holds right now; null when it can't be read. Framework thread.</summary>
    private static unsafe SlotContent? ReadSlot(Stack stack)
    {
        var container = InventoryManager.Instance()->GetInventoryContainer(stack.Container);
        var slot = container == null ? null : container->GetInventorySlot(stack.Slot);
        return slot == null ? null : new SlotContent(slot->ItemId, slot->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality), (int)slot->Quantity);
    }

    private static unsafe int SlotQuantity(Stack stack)
    {
        var slot = InventoryManager.Instance()->GetInventoryContainer(stack.Container)->GetInventorySlot(stack.Slot);
        return slot == null || slot->ItemId != stack.ItemId ? 0 : (int)slot->Quantity;
    }
}
