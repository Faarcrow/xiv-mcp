using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using XivMcp.Gear;
using XivMcp.Mcp;

namespace XivMcp.Util;

/// <summary>The player's gearsets (the game's Gear Set list) and switching between them, like clicking one in the list.</summary>
internal static class Gearsets
{
    public sealed record Set(int Number, string Name, uint JobId, string Job, string JobName, int ItemLevel, bool Current, bool CombatJob, int MissingItems,
                             IReadOnlyList<Piece> Pieces);

    /// <summary>
    /// A piece of a set as saved: slot (numbered like <see cref="Fashion.GearSlots"/>), item id (with the HQ offset), glamour, dyes and
    /// materia (type, grade), and the held item it would be equipped from (null when missing, or when the containers aren't loaded).
    /// </summary>
    public sealed record Piece(int Slot, uint ItemId, uint GlamourId, byte[] Stains, (ushort Type, byte Grade)[] Materia, GearsetPieces.Held? Held, bool Missing);

    /// <summary>All gearsets; Number is 1-based like in game. Framework thread.</summary>
    public static unsafe List<Set> All()
    {
        var module = RaptureGearsetModule.Instance();
        if (module == null) return [];
        var jobs = Svc.Data.GetExcelSheet<ClassJob>();
        var current = module->CurrentGearsetIndex;
        var held = HeldGear();
        var sets = new List<Set>();
        for (var i = 0; i < 100; i++)
        {
            if (!module->IsValidGearset(i)) continue;
            var e = module->GetGearset(i);
            var job = jobs.GetRowOrDefault(e->ClassJob);
            var saved = new List<(int Slot, uint ItemId)>();
            for (var s = 0; s < e->Items.Length; s++) saved.Add((s, e->Items[s].ItemId));
            // The game sets ItemMissing only after an equip attempt failed, so look for each piece ourselves (when the containers are loaded).
            var found = held is null ? null : GearsetPieces.Match(saved, held).ToDictionary(m => m.Slot, m => m.Held);
            var pieces = new List<Piece>();
            for (var s = 0; s < e->Items.Length; s++)
            {
                ref var item = ref e->Items[s];
                if (!GearsetPieces.IsPiece(s, item.ItemId)) continue;
                var materia = new List<(ushort, byte)>();
                for (var m = 0; m < item.Materia.Length; m++)
                    if (item.Materia[m] != 0) materia.Add((item.Materia[m], item.MateriaGrades[m]));
                var at = found?.GetValueOrDefault(s);
                pieces.Add(new Piece(s, item.ItemId, item.GlamourId, [item.Stain0Id, item.Stain1Id], [.. materia], at,
                    found is null ? item.Flags.HasFlag(RaptureGearsetModule.GearsetItemFlag.ItemMissing) : at is null));
            }
            sets.Add(new Set(i + 1, e->NameString, e->ClassJob, job?.Abbreviation.ExtractText() ?? "?", job?.Name.ExtractText() ?? "?", e->ItemLevel,
                i == current, job is { } j && j.DohDolJobIndex < 0, pieces.Count(p => p.Missing), pieces));
        }
        return sets;
    }

    private static readonly FFXIVClientStructs.FFXIV.Client.Game.InventoryType[] GearContainers =
    [
        FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory1, FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory2,
        FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory3, FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory4,
        FFXIVClientStructs.FFXIV.Client.Game.InventoryType.EquippedItems,
        FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryMainHand, FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryOffHand,
        FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryHead, FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryBody,
        FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryHands, FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryWaist,
        FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryLegs, FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryFeets,
        FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryEar, FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryNeck,
        FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryWrist, FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryRings,
        FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmorySoulCrystal,
    ];

    /// <summary>The items in the bags, worn and in the armoury chest; null while a container isn't loaded. Framework thread.</summary>
    private static unsafe List<GearsetPieces.Held>? HeldGear()
    {
        var inv = FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance();
        if (inv == null) return null;
        var held = new List<GearsetPieces.Held>();
        foreach (var type in GearContainers)
        {
            var c = inv->GetInventoryContainer(type);
            if (c == null || !c->IsLoaded) return null;
            var place = type switch
            {
                FFXIVClientStructs.FFXIV.Client.Game.InventoryType.EquippedItems => GearsetPieces.Place.Equipped,
                >= FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory1 and <= FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory4 => GearsetPieces.Place.Bags,
                _ => GearsetPieces.Place.Armoury,
            };
            for (var i = 0; i < c->Size; i++)
            {
                var slot = c->GetInventorySlot(i);
                if (slot->ItemId == 0) continue;
                var hq = slot->Flags.HasFlag(FFXIVClientStructs.FFXIV.Client.Game.InventoryItem.ItemFlags.HighQuality);
                held.Add(new GearsetPieces.Held(slot->ItemId + (hq ? GearsetPieces.HqOffset : 0), place, type.ToString(), i));
            }
        }
        return held;
    }

    public static bool IsCombatJob(uint jobId) => Svc.Data.GetExcelSheet<ClassJob>().GetRowOrDefault(jobId) is { } j && j.DohDolJobIndex < 0 && jobId != 0;

    /// <summary>A gearset by number, name or job (abbreviation or name; the highest item level set of that job). Framework thread.</summary>
    public static Set Resolve(string query)
    {
        var sets = All();
        if (sets.Count == 0) throw new ToolException("No gearsets are saved.");
        if (int.TryParse(query, out var n))
            return sets.FirstOrDefault(s => s.Number == n) ?? throw new ToolException($"There is no gearset {n}.");
        var byName = sets.Where(s => s.Name.Equals(query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byName.Count > 0) return byName[0];
        var byJob = sets.Where(s => s.Job.Equals(query, StringComparison.OrdinalIgnoreCase) || s.JobName.Equals(query, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(s => s.MissingItems > 0).ThenByDescending(s => s.ItemLevel).ToList();
        if (byJob.Count > 0) return byJob[0];
        var partial = sets.Where(s => Game.Matches(s.Name, query)).ToList();
        return partial.Count switch
        {
            1 => partial[0],
            0 => throw new ToolException($"No gearset matches '{query}' (by number, name or job). Saved: {string.Join(", ", sets.Select(s => $"{s.Number} {s.Name} ({s.Job})"))}."),
            _ => throw new ToolException($"'{query}' matches several gearsets: {string.Join(", ", partial.Select(s => $"{s.Number} {s.Name}"))}."),
        };
    }

    /// <summary>Addon rows of the game's "… registered to this gear set could not be found … Replace it with …?" prompt.</summary>
    private static readonly uint[] ReplaceMissingPrompts = [4384, 4385];

    /// <summary>
    /// Equips a gearset and waits until the game has switched to its job. When an item of the set is missing, the game asks
    /// whether to replace it with another one it found: <paramref name="replaceMissing"/> answers Yes (the default, like the
    /// player usually does) or No; what was asked lands in <paramref name="steps"/>. Any other prompt stops with its text.
    /// </summary>
    public static async Task<Set> Switch(string query, CancellationToken ct, bool replaceMissing = true, List<string>? steps = null)
    {
        var set = await Game.RunLoggedIn(() =>
        {
            var s = Resolve(query);
            EnsureCanChangeGear();
            return s;
        }).ConfigureAwait(false);
        if (set.Current && await Game.Run(CurrentJob).ConfigureAwait(false) == set.JobId) return set;

        var templates = await Game.Run(() => ReplaceMissingPrompts.Select(GameWindows.TemplatePieces).ToList()).ConfigureAwait(false);
        bool AsksToReplace(string? p) => p is not null && templates.Any(t => Ui.PromptTemplate.Matches(p, t));
        // An earlier attempt may have left the prompt open: answering it finishes that switch instead of starting another.
        if (!AsksToReplace(await Game.Run(GameWindows.YesNoPrompt).ConfigureAwait(false)))
        {
            var result = await Game.Run(() => { unsafe { return RaptureGearsetModule.Instance()->EquipGearset(set.Number - 1); } }).ConfigureAwait(false);
            if (result < 0) throw new ToolException($"The game refused to equip gearset {set.Number} ({set.Name}).");
        }
        var answered = 0;
        // The job changes after the server confirms, which can take a while (longer after answering a prompt); the deadline
        // starts again after each answer (one prompt per missing item at most).
        var deadline = DateTime.UtcNow + Gear.GearSettle.JobChangeTimeout;
        for (var first = true; ; first = false)
        {
            if (!first) await Task.Delay(250, ct).ConfigureAwait(false);
            if (await Game.Run(CurrentJob).ConfigureAwait(false) == set.JobId)
            {
                // The set's pieces still move in after the job changed; gear moves sent meanwhile get dropped.
                await WaitUntilSettled(set.JobId, Gear.GearSettle.SettleTimeout, ct).ConfigureAwait(false);
                return set with { Current = true };
            }
            if (DateTime.UtcNow > deadline) break;
            var prompt = await Game.Run(GameWindows.YesNoPrompt).ConfigureAwait(false);
            if (prompt is null) continue;
            if (!AsksToReplace(prompt))
                throw new ToolException($"Equipping gearset {set.Number} ({set.Name}) shows a prompt: \"{prompt}\". Answer it with select_menu_option (\"Yes\" or \"No\").");
            if (++answered > 14) throw new ToolException($"The game keeps asking \"{prompt}\" while equipping gearset {set.Number} ({set.Name}).");
            await Game.Run(() => { GameWindows.AnswerYesNo(replaceMissing); return true; }).ConfigureAwait(false);
            steps?.Add($"The game asked: \"{prompt}\" Answered {(replaceMissing ? "Yes" : "No")}.");
            deadline = DateTime.UtcNow + Gear.GearSettle.JobChangeTimeout;
        }
        throw new ToolException($"Equipped gearset {set.Number} ({set.Name}), but the job did not change to {set.Job} within " +
                                $"{Gear.GearSettle.JobChangeTimeout.TotalSeconds:0} seconds.");
    }

    /// <summary>
    /// Waits until a gear change has finished: the job is <paramref name="jobId"/> (any when null), no inventory change is pending,
    /// no prompt is open, the character may change gear and the worn gear stayed the same for a moment. False when that did
    /// not happen within <paramref name="timeout"/>.
    /// </summary>
    public static async Task<bool> WaitUntilSettled(uint? jobId, TimeSpan timeout, CancellationToken ct)
    {
        var settle = new Gear.GearSettle(Gear.GearSettle.DefaultQuiet);
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var state = await Game.Run(() => new Gear.GearState(jobId is null || CurrentJob() == jobId, GearBusy(), EquippedSignature())).ConfigureAwait(false);
            if (settle.Observe(DateTime.UtcNow, state)) return true;
            if (DateTime.UtcNow > deadline) return false;
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
    }

    /// <summary>An inventory change is pending, a prompt is open or the character's state forbids changing gear. Framework thread.</summary>
    private static bool GearBusy()
    {
        var c = Svc.Condition;
        return Tools.InventoryActionTools.HasPendingOperation() || GameWindows.YesNoPrompt() is not null
               || c[ConditionFlag.Casting] || c[ConditionFlag.BetweenAreas] || c[ConditionFlag.BetweenAreas51] || c[ConditionFlag.Occupied]
               || c[ConditionFlag.Occupied39] || c[ConditionFlag.OccupiedInCutSceneEvent] || c[ConditionFlag.Crafting] || c[ConditionFlag.Gathering];
    }

    /// <summary>The worn items' ids, to notice the game still moving gear. Framework thread.</summary>
    private static unsafe string EquippedSignature()
    {
        var equipped = FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance()->GetInventoryContainer(FFXIVClientStructs.FFXIV.Client.Game.InventoryType.EquippedItems);
        if (equipped == null) return "";
        var ids = new uint[equipped->Size];
        for (var i = 0; i < equipped->Size; i++) ids[i] = equipped->GetInventorySlot(i)->ItemId;
        return string.Join(",", ids);
    }

    /// <summary>Gear can't change in combat, while crafting, gathering, casting, in a cutscene or a zone change. Framework thread.</summary>
    public static void EnsureCanChangeGear()
    {
        var c = Svc.Condition;
        if (c[ConditionFlag.InCombat]) throw new ToolException("Can't change gear in combat.");
        if (c[ConditionFlag.Crafting] || c[ConditionFlag.Gathering] || c[ConditionFlag.Casting] || c[ConditionFlag.OccupiedInCutSceneEvent]
            || c[ConditionFlag.BetweenAreas] || c[ConditionFlag.Occupied39])
            throw new ToolException("Can't change gear right now (crafting, gathering, casting, in a cutscene or a zone change).");
    }

    public static uint CurrentJob() => Svc.Objects.LocalPlayer?.ClassJob.RowId ?? 0;
}
