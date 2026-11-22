using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using XivMcp.Crafting;
using XivMcp.Mcp;
using XivMcp.Util;

namespace XivMcp.Tools;

/// <summary>
/// Desynthesizing items from the bags, as a player does it: the item's context menu, "Desynthesis", then the game's desynthesis
/// window, with "Desynthesize entire stack." ticked for a whole stack. The commands were recorded with capture_ui_events.
/// </summary>
internal static class DesynthTools
{
    /// <summary>Addon rows that read "Desynthesis" (the context menu's entry).</summary>
    private static readonly uint[] DesynthMenuTexts = [1815, 1816, 1821];

    /// <summary>UnlockLink of the Desynthesis general action (GeneralAction 5; quest "Gone to Pieces").</summary>
    private const uint DesynthesisUnlock = 95;

    /// <summary>SalvageDialog callbacks: tick or untick "Desynthesize entire stack." (then true or false), and the Desynthesize button.</summary>
    private const int BulkCheckbox = 13, DesynthesizeButton = 0;

    /// <summary>"Desynthesize entire stack." (Addon row), and where that box was when its callback was recorded (the second).</summary>
    private const uint BulkLabel = 5863;
    private const int BulkBoxPosition = 1;

    /// <summary>SalvageAutoDialog callback: "End Desynthesis" (stops a bulk run, or closes the window once it is done).</summary>
    private const int EndBulk = 1;

    /// <summary>A bulk run takes about three seconds an item; it has stalled when nothing changes for this long.</summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan ConsentTimeout = TimeSpan.FromMinutes(2);

    private static readonly InventoryType[] Bags = [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];

    private static readonly string[] InventoryAddons = ["InventoryExpansion", "InventoryLarge", "Inventory"];

    public static IEnumerable<McpTool> Create()
    {
        yield return new McpTool
        {
            Name = "desynthesize_items",
            Description = "Desynthesizes items from the bags, like the Desynthesis entry of their context menu: all of an item, at most 'quantity', " +
                          "or all but 'keep' ('hq' for only HQ or only NQ stacks). Small stacks go first. A stack used up whole goes in one run " +
                          "with the game's \"Desynthesize entire stack\", about 3 seconds an item; a stack used only partly goes one item at a " +
                          "time, about 5 seconds each (for many items run it as a job step, start_job). The player approves the whole list in " +
                          "game first, every time: desynthesis can't be undone. Items the game would ask an extra tick for (unique, untradable, " +
                          "collectable, with materia melded) and gear a gearset uses are left alone and reported. Returns how many of each item " +
                          "were desynthesized and what came into the bags. Desynthesis must be unlocked; out of combat, not mounted, with free " +
                          "bag slots for the results. Compare the vendor and market value of the item with what it gives first. Requires " +
                          "'Items & retainers' in /xivmcp.",
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
                          "quantity": { "type": "integer", "minimum": 1, "description": "Desynthesize at most this many (default all)." },
                          "keep": { "type": "integer", "minimum": 0, "description": "Keep at least this many (default 0)." },
                          "hq": { "type": "boolean", "description": "Only HQ (true) or only NQ (false) stacks." }
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
                var wanted = (args.Array("items") ?? throw new ToolException("'items' is required.")).OfType<JsonObject>()
                    .Select(o => new ToolArgs(o))
                    .Select(a => (Query: a.String("item") ?? throw new ToolException("Each entry needs 'item'."),
                                  Quantity: a.Node("quantity") is null ? (int?)null : a.Int("quantity", 1, 1),
                                  Keep: a.Int("keep", 0, 0),
                                  Hq: a.Node("hq") is null ? (bool?)null : a.Bool("hq", false)))
                    .ToList();

                await InventoryActionTools.Gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var plans = await Game.RunLoggedIn(() =>
                    {
                        unsafe
                        {
                            if (!UIState.Instance()->IsUnlockLinkUnlocked(DesynthesisUnlock))
                                throw new ToolException("This character hasn't unlocked desynthesis (quest \"Gone to Pieces\").");
                        }
                        EnsureCanDesynth();
                        var gearsets = Gearsets.ItemIds();
                        var resolved = wanted.Select(w => (Item: Items.Resolve(w.Query), w.Quantity, w.Keep, w.Hq)).ToList();
                        if (resolved.GroupBy(r => r.Item.RowId).FirstOrDefault(g => g.Count() > 1) is { } twice)
                            throw new ToolException($"{twice.First().Item.Name.ExtractText()} is listed more than once; list each item once.");
                        return resolved.Select(r => PlanItem(r.Item, r.Quantity, r.Keep, r.Hq, gearsets)).ToList();
                    }).ConfigureAwait(false);
                    var results = plans.Where(p => p.Refused is not null || p.Steps.Count == 0)
                        .Select(p => (object)new { item = p.Name, desynthesized = 0, note = p.Refused ?? "Nothing to desynthesize (none in the bags, none of the asked quality, or all kept)." })
                        .ToList();
                    var todo = plans.Where(p => p.Refused is null && p.Steps.Count > 0).ToList();
                    if (todo.Count == 0) return new { desynthesized = results, obtained = Array.Empty<object>() };

                    await Approve(todo, args.Caller, ct).ConfigureAwait(false);
                    var before = await Game.Run(BagCounts).ConfigureAwait(false);
                    string? stopped = null;
                    // YesAlready can press Desynthesize (or start a whole-stack run) the moment the window opens.
                    using (PluginCompat.PauseYesAlready("desynthesis"))
                    {
                        try
                        {
                            foreach (var plan in todo)
                            {
                                if (stopped is not null)
                                {
                                    results.Add(new { item = plan.Name, desynthesized = 0, note = "Not started: desynthesis stopped at an earlier item." });
                                    continue;
                                }
                                var had = await Game.Run(() => Items.CountInBags(plan.ItemId)).ConfigureAwait(false);
                                try
                                {
                                    foreach (var step in plan.Steps)
                                    {
                                        var stack = plan.Stacks[step.Stack];
                                        if (step.Bulk) await DesynthStack(stack, plan.Name, ct).ConfigureAwait(false);
                                        else
                                            for (var i = 0; i < step.Count; i++)
                                                await DesynthOne(stack, stack.Quantity - i, plan.Name, ct).ConfigureAwait(false);
                                    }
                                }
                                catch (ToolException e) { stopped = e.Message; }
                                var left = await Game.Run(() => Items.CountInBags(plan.ItemId)).ConfigureAwait(false);
                                results.Add(new { item = plan.Name, desynthesized = had - left, left, stopped });
                            }
                        }
                        finally
                        {
                            // Whatever happened (done, stopped, cancelled), no desynthesis window stays open.
                            await Game.Run(() => { EndBulkRun(); CloseDialog(); CloseResult(); return true; }).ConfigureAwait(false);
                        }
                    }
                    var after = await Game.Run(BagCounts).ConfigureAwait(false);
                    var obtained = after.Where(kv => kv.Value > before.GetValueOrDefault(kv.Key))
                        .Select(kv => (object)new { item = Items.Name(kv.Key), quantity = kv.Value - before.GetValueOrDefault(kv.Key) })
                        .ToList();
                    return new { desynthesized = results, obtained };
                }
                finally { InventoryActionTools.Gate.Release(); }
            },
        };
    }

    private sealed record Stack(InventoryType Container, int Slot, uint ItemId, int Quantity, bool Hq);

    private sealed record ItemPlan(uint ItemId, string Name, List<Stack> Stacks, IReadOnlyList<DesynthStep> Steps, string? Refused);

    /// <summary>The item's stacks in the bags and the runs for them, or why it is left alone. Framework thread.</summary>
    private static unsafe ItemPlan PlanItem(Lumina.Excel.Sheets.Item item, int? quantity, int keep, bool? hq, HashSet<uint> gearsets)
    {
        var name = item.Name.ExtractText();
        var im = InventoryManager.Instance();
        var stacks = new List<Stack>();
        var melded = false;
        var collectable = item.AlwaysCollectable;
        foreach (var container in Bags)
        {
            var c = im->GetInventoryContainer(container);
            if (c == null) continue;
            for (var i = 0; i < c->Size; i++)
            {
                var slot = c->GetInventorySlot(i);
                if (slot == null || slot->ItemId != item.RowId || slot->Quantity == 0) continue;
                if (hq is { } wantHq && slot->IsHighQuality() != wantHq) continue;
                stacks.Add(new Stack(container, i, item.RowId, (int)slot->Quantity, slot->IsHighQuality()));
                melded |= slot->GetMateriaCount() > 0;
                collectable |= slot->IsCollectable();
            }
        }
        var refused = Desynthesis.Refusal(new DesynthItem(item.Desynth > 0, item.IsUnique, item.IsUntradable, collectable, melded, gearsets.Contains(item.RowId)));
        if (refused is not null) return new ItemPlan(item.RowId, name, stacks, [], refused);
        try { return new ItemPlan(item.RowId, name, stacks, Desynthesis.Plan(stacks.Select(s => s.Quantity).ToList(), quantity, keep), null); }
        catch (ArgumentOutOfRangeException e) { throw new ToolException($"{name}: {e.Message}"); }
    }

    private static void EnsureCanDesynth()
    {
        if (Busy() is { } reason) throw new ToolException(reason);
    }

    /// <summary>Why the character can't desynthesize right now, or null. Framework thread.</summary>
    private static string? Busy()
    {
        var c = Svc.Condition;
        if (c[ConditionFlag.InCombat]) return "Can't desynthesize during combat.";
        if (c[ConditionFlag.Mounted]) return "Can't desynthesize while mounted; dismount first.";
        if (!c[ConditionFlag.NormalConditions] || c[ConditionFlag.Occupied39] || c[ConditionFlag.Jumping] || c[ConditionFlag.Crafting] || c[ConditionFlag.Gathering])
            return "Can't desynthesize right now (busy: crafting, gathering, talking or in a cutscene).";
        return null;
    }

    /// <summary>Asks the player in game to approve the whole list; desynthesis can't be undone, so this is asked every time.</summary>
    private static async Task Approve(List<ItemPlan> todo, Permissions.Caller? caller, CancellationToken ct)
    {
        var details = todo.Select(p =>
        {
            var count = p.Steps.Sum(s => s.Count);
            var hq = p.Steps.Where(s => p.Stacks[s.Stack].Hq).Sum(s => s.Count);
            return $"{count} × {p.Name}{(hq == 0 ? "" : hq == count ? " (HQ)" : $" ({hq} of them HQ)")}";
        }).ToList();
        var total = todo.Sum(p => p.Steps.Sum(s => s.Count));
        var request = new Consent.Request(todo.Count == 1 ? $"Desynthesize {details[0]}?" : $"Desynthesize {total} items?", details)
        {
            Deadline = DateTime.UtcNow + ConsentTimeout,
            Source = caller?.Name ?? "your AI assistant",
            Warning = "Desynthesis can't be undone.",
            Risk = Permissions.RiskLevel.Critical,
        };
        if (await Consent.Ask(request, ConsentTimeout, ct).ConfigureAwait(false) != Permissions.ApprovalDecision.Denied) return;
        throw new ToolException(DateTime.UtcNow >= request.Deadline
            ? $"No answer in game within {ConsentTimeout.TotalSeconds:0} s; nothing was desynthesized. Ask the player to watch for the XIV MCP approval window."
            : "The player declined in game; nothing was desynthesized.");
    }

    /// <summary>Desynthesizes one item of the stack, which must still hold <paramref name="expected"/>; returns once it is done.</summary>
    private static async Task DesynthOne(Stack stack, int expected, string name, CancellationToken ct)
    {
        await OpenDialog(stack, expected, name, bulk: false, ct).ConfigureAwait(false);
        await Game.Run(() => PressDesynthesize(stack, expected, name, bulk: false)).ConfigureAwait(false);
        if (!await GameWindows.WaitFor(() => SlotQuantity(stack) < expected || BulkRunOpen(), TimeSpan.FromSeconds(10), ct).ConfigureAwait(false))
            throw new ToolException($"{name} was not desynthesized (the game may have refused it, or the bags are full).");
        // The item leaves the stack before the desynthesis is over; its result window opens once it is. Until then the next
        // context menu has no Desynthesis entry.
        await GameWindows.WaitFor(() => GameWindows.Ready("SalvageResult") || BulkRunOpen(), TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        if (await Game.Run(BulkRunOpen).ConfigureAwait(false))
        {
            // Should not happen (the stack box was unticked), but a whole-stack run is stopped at once if it does.
            await Game.Run(EndBulkRun).ConfigureAwait(false);
            throw new ToolException($"The game started desynthesizing the whole stack of {name} instead of one item; it was stopped.");
        }
    }

    /// <summary>Desynthesizes the whole stack in one bulk run; it must still hold what was planned.</summary>
    private static async Task DesynthStack(Stack stack, string name, CancellationToken ct)
    {
        await OpenDialog(stack, stack.Quantity, name, bulk: true, ct).ConfigureAwait(false);
        await Game.Run(() => PressDesynthesize(stack, stack.Quantity, name, bulk: true)).ConfigureAwait(false);

        var left = stack.Quantity;
        var changed = DateTime.UtcNow;
        try
        {
            while (left > 0 && DateTime.UtcNow - changed < StallTimeout)
            {
                await Task.Delay(500, ct).ConfigureAwait(false);
                var now = await Game.Run(() => SlotQuantity(stack)).ConfigureAwait(false);
                if (now != left) { left = now; changed = DateTime.UtcNow; }
            }
            // As with a single one, the last item leaves the stack before its desynthesis is over.
            if (left == 0) await GameWindows.WaitFor(() => Busy() is null, TimeSpan.FromSeconds(4), ct).ConfigureAwait(false);
        }
        finally
        {
            // Done, stalled (bags full) or cancelled: "End Desynthesis" stops the run and closes its window.
            await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);
            await Game.Run(EndBulkRun).ConfigureAwait(false);
        }
        if (left == stack.Quantity) throw new ToolException($"{name} was not desynthesized (the game may have refused it, or the bags are full).");
        if (left > 0) throw new ToolException($"The bulk run on {name} stopped with {left} left (the bags may be full).");
    }

    /// <summary>
    /// Opens the desynthesis window for the stack through its context menu and sets its "Desynthesize entire stack." box. The stack
    /// must hold exactly <paramref name="expected"/>: anything else means it changed since the player approved.
    /// </summary>
    private static async Task OpenDialog(Stack stack, int expected, string name, bool bulk, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await Game.Run(CloseResult).ConfigureAwait(false);
        // Right after a desynthesis the character may still be busy with it for a moment.
        await GameWindows.WaitFor(() => Busy() is null && !BulkRunOpen(), TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        await Game.Run(() =>
        {
            if (BulkRunOpen())
            {
                EndBulkRun();
                throw new ToolException("A whole-stack desynthesis was still running; it was stopped.");
            }
            EnsureCanDesynth();
            if (SlotQuantity(stack) != expected) throw new ToolException($"{name} changed in the bags since you approved; nothing more was desynthesized.");
            if (Items.FreeBagSlots() == 0) throw new ToolException("The bags are full: the results need room.");
            return true;
        }).ConfigureAwait(false);
        // A menu opened while the game still finishes the last desynthesis lacks the entry; it is there a moment later.
        for (var attempt = 1; ; attempt++)
        {
            await Game.Run(() => { OpenContextMenu(stack); return true; }).ConfigureAwait(false);
            if (!await GameWindows.WaitFor(() => GameWindows.Ready("ContextMenu"), TimeSpan.FromSeconds(3), ct).ConfigureAwait(false))
                throw new ToolException($"The context menu of {name} did not open.");
            var entries = await Game.Run(PickDesynth).ConfigureAwait(false);
            if (entries is null) break;
            if (attempt == 3)
                throw new ToolException($"{name}'s context menu has no Desynthesis entry (it offers {string.Join(", ", entries)}); desynthesis may not be unlocked for it.");
            await Task.Delay(1000, ct).ConfigureAwait(false);
        }
        if (!await GameWindows.WaitFor(() => GameWindows.Ready("SalvageDialog"), TimeSpan.FromSeconds(5), ct).ConfigureAwait(false))
            throw new ToolException($"The desynthesis window for {name} did not open.");
        await Task.Delay(Random.Shared.Next(250, 450), ct).ConfigureAwait(false);
        // The game remembers the box from the last desynthesis, so it is set every time. The callback alone proves nothing: a box
        // left ticked turns one item into the whole stack, more than the player approved. So the box itself is read back, and the
        // callback sent again while it shows the wrong state; if it never takes, the window is closed before anything happens.
        if (await Game.Run(() => SetBulk(bulk, expected, name)).ConfigureAwait(false))
            for (var attempt = 1; ; attempt++)
            {
                if (await GameWindows.WaitFor(() => BulkTicked() == bulk, TimeSpan.FromSeconds(1), ct).ConfigureAwait(false)) break;
                if (attempt == 3)
                {
                    await Game.Run(CloseDialog).ConfigureAwait(false);
                    throw new ToolException($"The \"Desynthesize entire stack\" box for {name} could not be {(bulk ? "ticked" : "unticked")}; nothing was desynthesized.");
                }
                await Game.Run(() => SetBulk(bulk, expected, name)).ConfigureAwait(false);
            }
        await Task.Delay(300, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Ticks or unticks the window's "Desynthesize entire stack." box; true when it sent that, false when the window has no box. It
    /// has none for a single item (then only one can be desynthesized anyway); for a bigger stack a missing box means the window
    /// isn't what XIV MCP knows. Framework thread.
    /// </summary>
    private static unsafe bool SetBulk(bool bulk, int stackSize, string name)
    {
        var dialog = GameWindows.Addon("SalvageDialog");
        if (dialog == null || !dialog->IsVisible) throw new ToolException($"The desynthesis window for {name} closed unexpectedly.");
        // SalvageDialog values: its check boxes from [16] on, three values each (kind, label, ticked when the window opened).
        var label = GameWindows.Texts(BulkLabel);
        var box = -1;
        for (var k = 0; k < 3 && 18 + 3 * k < dialog->AtkValuesCount; k++)
        {
            var v = dialog->AtkValues[17 + 3 * k];
            if (v.Type is (AtkValueType.String or AtkValueType.ManagedString or AtkValueType.String8) && v.String.Value != null &&
                label.Contains(Dalamud.Memory.MemoryHelper.ReadSeStringNullTerminated((nint)v.String.Value).TextValue.Trim()))
                box = k;
        }
        if (box == -1 && stackSize == 1 && !bulk) return false;
        // The callback was recorded with the box second, after "Guarantee NQ item results."; another layout is left alone.
        if (box != BulkBoxPosition)
        {
            CloseDialog();
            throw new ToolException($"The desynthesis window for {name} looks different than XIV MCP knows; nothing was desynthesized.");
        }
        RetainerUi.Fire(dialog, true, BulkCheckbox, bulk);
        return true;
    }

    /// <summary>
    /// Whether the window's "Desynthesize entire stack." box is ticked, as the box itself shows it; null when the window is closed or
    /// shows no such box. Framework thread.
    /// </summary>
    private static unsafe bool? BulkTicked()
    {
        var dialog = GameWindows.Addon("SalvageDialog");
        if (dialog == null || !dialog->IsVisible) return null;
        // The box is found by what it is and what it says, not by a node id: of the window's visible component nodes, the check box
        // whose own text is "Desynthesize entire stack.". The other boxes ("Guarantee NQ item results." and the like) say something
        // else, so they can't be mistaken for it.
        var label = GameWindows.Texts(BulkLabel);
        var uld = &dialog->UldManager;
        for (var i = 0; i < uld->NodeListCount; i++)
        {
            var node = uld->NodeList[i];
            if (node == null || (int)node->Type < 1000 || !node->IsVisible()) continue;
            var component = ((AtkComponentNode*)node)->Component;
            if (component == null || component->GetComponentType() != ComponentType.CheckBox) continue;
            if (GameWindows.Label(component) is { } text && label.Contains(text)) return ((AtkComponentCheckBox*)component)->IsChecked;
        }
        return null;
    }

    /// <summary>
    /// Presses Desynthesize, unless the window closed, a whole-stack run is going on, the stack no longer holds
    /// <paramref name="expected"/>, or the "Desynthesize entire stack." box isn't as <paramref name="bulk"/> wants it. Framework thread.
    /// </summary>
    private static unsafe bool PressDesynthesize(Stack stack, int expected, string name, bool bulk)
    {
        var dialog = GameWindows.Addon("SalvageDialog");
        if (dialog == null || !dialog->IsVisible || BulkRunOpen())
            throw new ToolException("The desynthesis window closed unexpectedly; nothing more was desynthesized.");
        if (SlotQuantity(stack) != expected)
        {
            CloseDialog();
            throw new ToolException($"{name} changed in the bags since you approved; nothing more was desynthesized.");
        }
        // Checked once more in the same frame as the press: the box must show what was asked for. Without a box only a single item
        // may go (the window has none for it).
        if (BulkTicked() is { } ticked ? ticked != bulk : bulk || expected > 1)
        {
            CloseDialog();
            throw new ToolException($"The \"Desynthesize entire stack\" box for {name} isn't {(bulk ? "ticked" : "unticked")}; nothing more was desynthesized.");
        }
        RetainerUi.Fire(dialog, true, DesynthesizeButton, null);
        return true;
    }

    /// <summary>The window of a whole-stack run is open (running, or done and not yet closed). Framework thread.</summary>
    private static bool BulkRunOpen() => RetainerUi.Ptr("SalvageAutoDialog") is { IsNull: false, IsVisible: true };

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

    /// <summary>
    /// Picks the context menu's "Desynthesis" entry by its text and returns null; when there is none, closes the menu and returns
    /// the entries it had.
    /// </summary>
    private static unsafe List<string>? PickDesynth()
    {
        var texts = GameWindows.Texts(DesynthMenuTexts);
        var menu = GameWindows.Addon("ContextMenu");
        if (menu == null || !menu->IsVisible) throw new ToolException("The context menu closed unexpectedly.");
        var entries = new List<string>();
        // ContextMenu values: [0] = entry count, entry texts from [8] on. Only values that are text are read.
        var count = (int)menu->AtkValues[0].UInt;
        for (var i = 0; i < count && 8 + i < menu->AtkValuesCount; i++)
        {
            var v = menu->AtkValues[8 + i];
            if (v.Type is not (AtkValueType.String or AtkValueType.ManagedString or AtkValueType.String8) || v.String.Value == null) continue;
            var text = Dalamud.Memory.MemoryHelper.ReadSeStringNullTerminated((nint)v.String.Value).TextValue.Trim();
            if (texts.Contains(text))
            {
                RetainerUi.Fire(menu, true, 0, i, 0u);
                return null;
            }
            entries.Add(text);
        }
        RetainerUi.Fire(menu, true, -1);
        return entries;
    }

    private static unsafe bool CloseDialog()
    {
        var dialog = GameWindows.Addon("SalvageDialog");
        if (dialog != null && dialog->IsVisible) dialog->Close(true);
        return true;
    }

    /// <summary>Closes the window a single desynthesis leaves open with its results.</summary>
    private static unsafe bool CloseResult()
    {
        var result = GameWindows.Addon("SalvageResult");
        if (result != null && result->IsVisible) result->Close(true);
        return true;
    }

    private static unsafe bool EndBulkRun()
    {
        var auto = GameWindows.Addon("SalvageAutoDialog");
        if (auto != null && auto->IsVisible) RetainerUi.Fire(auto, true, EndBulk);
        return true;
    }

    /// <summary>How many the stack's slot holds of its item in its quality; 0 when something else is there.</summary>
    private static unsafe int SlotQuantity(Stack stack)
    {
        var slot = InventoryManager.Instance()->GetInventoryContainer(stack.Container)->GetInventorySlot(stack.Slot);
        return slot == null || slot->ItemId != stack.ItemId || slot->IsHighQuality() != stack.Hq ? 0 : (int)slot->Quantity;
    }

    /// <summary>How many of each item the bags and the crystal pouch hold, HQ and NQ together. Framework thread.</summary>
    private static unsafe Dictionary<uint, int> BagCounts()
    {
        var im = InventoryManager.Instance();
        var counts = new Dictionary<uint, int>();
        foreach (var container in Bags.Append(InventoryType.Crystals))
        {
            var c = im->GetInventoryContainer(container);
            if (c == null) continue;
            for (var i = 0; i < c->Size; i++)
            {
                var slot = c->GetInventorySlot(i);
                if (slot != null && slot->ItemId != 0) counts[slot->ItemId] = counts.GetValueOrDefault(slot->ItemId) + (int)slot->Quantity;
            }
        }
        return counts;
    }
}
