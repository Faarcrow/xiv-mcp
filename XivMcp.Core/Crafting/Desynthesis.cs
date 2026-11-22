using System;
using System.Collections.Generic;
using System.Linq;

namespace XivMcp.Crafting;

/// <summary>One desynthesis run on a bag stack: the whole stack at once (the game's "Desynthesize entire stack"), or single items.</summary>
public sealed record DesynthStep(int Stack, int Count, bool Bulk);

/// <summary>What an item is like, as far as desynthesizing it from the bags goes.</summary>
public sealed record DesynthItem(bool Desynthesizable, bool Unique, bool Untradable, bool Collectable, bool Melded, bool InGearset);

/// <summary>Which items to desynthesize, and how many from which stacks.</summary>
public static class Desynthesis
{
    /// <summary>
    /// The runs that desynthesize all of the stacks, at most <paramref name="quantity"/> items, and never so many that fewer than
    /// <paramref name="keep"/> remain. Small stacks go first, so the large ones are what stays; a stack used up whole goes in one bulk
    /// run, the last one only partly used goes item by item.
    /// </summary>
    public static IReadOnlyList<DesynthStep> Plan(IReadOnlyList<int> stacks, int? quantity, int keep)
    {
        if (quantity is <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Desynthesize at least one.");
        if (keep < 0) throw new ArgumentOutOfRangeException(nameof(keep), "Cannot keep fewer than none.");
        var limit = Math.Min(quantity ?? int.MaxValue, stacks.Sum() - keep);
        var steps = new List<DesynthStep>();
        var done = 0;
        foreach (var i in Enumerable.Range(0, stacks.Count).OrderBy(i => stacks[i]).ThenBy(i => i))
        {
            var take = Math.Min(stacks[i], limit - done);
            if (take <= 0) break;
            steps.Add(new DesynthStep(i, take, take == stacks[i] && take > 1));
            done += take;
        }
        return steps;
    }

    /// <summary>
    /// Why the item is left alone, or null when it may be desynthesized. The game asks for an extra tick in its window for unique,
    /// untradable, collectable and melded items; those are left for the player to do by hand, as is gear a gear set uses.
    /// </summary>
    public static string? Refusal(DesynthItem item) => item switch
    {
        { Desynthesizable: false } => "It can't be desynthesized.",
        { InGearset: true } => "A gear set uses it.",
        { Melded: true } => "It has materia melded; desynthesize it by hand if you mean to.",
        { Unique: true } or { Untradable: true } => "It is unique or untradable; desynthesize it by hand if you mean to.",
        { Collectable: true } => "It is a collectable; desynthesize it by hand if you mean to.",
        _ => null,
    };
}
