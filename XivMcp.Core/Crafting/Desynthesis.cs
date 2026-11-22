using System;
using System.Collections.Generic;
using System.Linq;

namespace XivMcp.Crafting;

/// <summary>One desynthesis run on a bag stack: the whole stack at once (the game's "Desynthesize entire stack"), or single items.</summary>
public sealed record DesynthStep(int Stack, int Count, bool Bulk);

/// <summary>What an item is like, as far as desynthesizing it from the bags goes.</summary>
public sealed record DesynthItem(bool Desynthesizable, bool Unique, bool Untradable, bool Collectable, bool Melded, bool InGearset, bool Glamoured);

/// <summary>How many of an item a desynthesis call takes, <paramref name="Hq"/> of them HQ.</summary>
public sealed record DesynthCount(string Name, int Count, int Hq);

/// <summary>What the player is asked in game: a title, and one line per item.</summary>
public sealed record DesynthApproval(string Title, IReadOnlyList<string> Details);

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
    /// untradable, collectable and melded items; those are left for the player to do by hand, as is gear a gear set uses. So is
    /// glamoured gear: whether the game warns before its glamour goes with it is unknown, and a lost glamour can't be had back.
    /// </summary>
    public static string? Refusal(DesynthItem item) => item switch
    {
        { Desynthesizable: false } => "It can't be desynthesized.",
        { InGearset: true } => "A gear set uses it.",
        { Melded: true } => "It has materia melded; desynthesize it by hand if you mean to.",
        { Glamoured: true } => "It is glamoured; desynthesize it by hand if you mean to.",
        { Unique: true } or { Untradable: true } => "It is unique or untradable; desynthesize it by hand if you mean to.",
        { Collectable: true } => "It is a collectable; desynthesize it by hand if you mean to.",
        _ => null,
    };

    /// <summary>
    /// Why the list can't go as it is, naming the first item listed more than once, or null. Two entries for one item would plan
    /// the same stacks twice, and the second would find them changed.
    /// </summary>
    public static string? ListedTwice(IEnumerable<(uint ItemId, string Name)> items) =>
        items.GroupBy(i => i.ItemId).FirstOrDefault(g => g.Count() > 1) is { } twice
            ? $"{twice.First().Name} is listed more than once; list each item once."
            : null;

    /// <summary>
    /// The in-game question for the whole list. One item is named in the title; several are counted there, and each has its own
    /// line. How many are HQ is told, since those are often worth more.
    /// </summary>
    public static DesynthApproval Approval(IReadOnlyList<DesynthCount> items)
    {
        var details = items.Select(i => $"{i.Count} × {i.Name}{(i.Hq == 0 ? "" : i.Hq == i.Count ? " (HQ)" : $" ({i.Hq} of them HQ)")}").ToList();
        var title = items.Count == 1 ? $"Desynthesize {details[0]}?" : $"Desynthesize {items.Sum(i => i.Count)} items?";
        return new DesynthApproval(title, details);
    }
}
