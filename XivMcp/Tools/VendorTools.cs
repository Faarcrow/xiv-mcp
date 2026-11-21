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
    /// <summary>The context menu's "Sell" entry (Addon rows that read "Sell"), and "Have Retainer Sell Items" (5480).</summary>
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
                          "are left alone. Sold items can be bought back: from a merchant's buyback tab until the shop is closed, or from " +
                          "the retainer's \"Item buyback\" until it is dismissed. Requires 'Market & purchases' in /xivmcp.",
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
                    var untradableApproved = await ApproveUntradable(resolved.Select(r => r.Item), ct).ConfigureAwait(false);
                    var gilBefore = await Game.Run(Gil).ConfigureAwait(false);
                    var results = new List<object>();
                    foreach (var (item, quantity, keep) in resolved)
                    {
                        var name = item.Name.ExtractText();
                        if (item.IsUntradable && item.PriceLow > 0 && !untradableApproved)
                        {
                            results.Add(new { item = name, sold = 0, note = "Untradable: the player did not approve selling it in game, so it was left alone." });
                            continue;
                        }
                        var stacks = await Game.Run(() => StacksOf(item.RowId)).ConfigureAwait(false);
                        IReadOnlyList<int> pick;
                        try { pick = VendorSale.Pick(stacks.Select(s => s.Quantity).ToList(), quantity, keep); }
                        catch (ArgumentOutOfRangeException e) { throw new ToolException($"{name}: {e.Message}"); }
                        if (item.PriceLow == 0)
                        {
                            results.Add(new { item = name, sold = 0, note = "Vendors do not buy this item." });
                            continue;
                        }
                        var sold = 0;
                        string? stopped = null;
                        foreach (var stack in pick.Select(i => stacks[i]))
                        {
                            ct.ThrowIfCancellationRequested();
                            try
                            {
                                await SellStack(stack, name, item.IsUntradable && untradableApproved, ct).ConfigureAwait(false);
                                sold += stack.Quantity;
                            }
                            catch (ToolException e)
                            {
                                stopped = e.Message;
                                break;
                            }
                            await Task.Delay(Random.Shared.Next(350, 650), ct).ConfigureAwait(false);
                        }
                        var left = stacks.Sum(s => s.Quantity) - sold;
                        results.Add(new { item = name, sold, left, vendorPrice = item.PriceLow, stopped });
                        if (stopped is not null) break;
                    }
                    var gilAfter = await Game.Run(Gil).ConfigureAwait(false);
                    return new { sold = results, gilGained = gilAfter - gilBefore, gil = gilAfter };
                }
                finally { InventoryActionTools.Gate.Release(); }
            },
        };
    }

    private sealed record Stack(InventoryType Container, int Slot, uint ItemId, int Quantity);

    private static readonly TimeSpan ConsentTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Asks the player in game once whether the untradable items of this call may be sold: once gone (and no longer in a buyback
    /// list), they can't be bought from other players again. True when there are none to ask about.
    /// </summary>
    private static async Task<bool> ApproveUntradable(IEnumerable<Lumina.Excel.Sheets.Item> items, CancellationToken ct)
    {
        var untradable = items.Where(i => i.IsUntradable && i.PriceLow > 0).Select(i => i.Name.ExtractText()).Distinct().ToList();
        if (untradable.Count == 0) return true;
        var details = untradable.Select(n => $"{n} (untradable)").ToList();
        details.Add("They can be bought back from the merchant's buyback tab, or the retainer's \"Item buyback\", until that closes.");
        var request = new Consent.Request(untradable.Count == 1 ? $"Sell {untradable[0]}?" : $"Sell {untradable.Count} untradable items?", details)
        {
            Deadline = DateTime.UtcNow + ConsentTimeout,
            Warning = "Untradable items can't be bought from other players again.",
            Risk = Permissions.RiskLevel.High,
            OfferSession = true,
        };
        return await Consent.AskOrSession("sell_to_vendor:untradable", request, ConsentTimeout, ct).ConfigureAwait(false);
    }

    /// <summary>Sells one stack: its context menu, "Sell", the confirmation if the game asks; waits until it is gone from the bag.</summary>
    private static async Task SellStack(Stack stack, string name, bool sellUntradable, CancellationToken ct)
    {
        await Game.Run(() => { OpenContextMenu(stack); return true; }).ConfigureAwait(false);
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
                return GameWindows.Ready("SelectYesno") ? AnswerPrompt(sellUntradable, out question) : "waiting";
            }).ConfigureAwait(false);
            if (state == "sold") return;
            if (state == "untradable") throw new ToolException($"The game warned that {name} is unique and untradable, and the player had not approved selling it; it was answered No.");
            if (state == "refused") throw new ToolException($"Selling {name} asked something unexpected (\"{question}\"); it was answered No and nothing more was sold.");
        }
        throw new ToolException($"{name} was not sold (the merchant may not buy it, or the shop closed).");
    }

    /// <summary>
    /// Answers the yes/no that came up: Yes when it asks to sell ("Sell … for … gil?"), Yes to the unique and untradable warning
    /// only when allowed, else No.
    /// </summary>
    private static unsafe string AnswerPrompt(bool sellUntradable, out string question)
    {
        var yesno = GameWindows.Addon("SelectYesno");
        var texts = GameWindows.AllTexts(yesno);
        question = string.Join(" ", texts.Where(t => !string.IsNullOrWhiteSpace(t)));
        var sellPrompt = GameWindows.TemplatePieces(SellPrompt);
        var untradable = GameWindows.TemplatePieces(UntradableWarning);
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
                if (slot != null && slot->ItemId == itemId && slot->Quantity > 0) stacks.Add(new Stack(container, i, itemId, (int)slot->Quantity));
            }
        }
        return stacks;
    }

    private static unsafe int SlotQuantity(Stack stack)
    {
        var slot = InventoryManager.Instance()->GetInventoryContainer(stack.Container)->GetInventorySlot(stack.Slot);
        return slot == null || slot->ItemId != stack.ItemId ? 0 : (int)slot->Quantity;
    }
}
