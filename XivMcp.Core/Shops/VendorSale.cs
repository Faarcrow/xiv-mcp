using System;
using System.Collections.Generic;
using System.Linq;

namespace XivMcp.Shops;

/// <summary>Which stacks to sell to a vendor. The game sells whole stacks from the inventory's context menu.</summary>
public static class VendorSale
{
    /// <summary>
    /// The indexes of the stacks to sell, smallest first (so the big stacks are what stays): all of them, at most
    /// <paramref name="quantity"/> items, and never so many that fewer than <paramref name="keep"/> remain.
    /// </summary>
    public static IReadOnlyList<int> Pick(IReadOnlyList<int> stacks, int? quantity, int keep)
    {
        if (quantity is <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Sell at least one.");
        if (keep < 0) throw new ArgumentOutOfRangeException(nameof(keep), "Cannot keep fewer than none.");
        var total = stacks.Sum();
        var limit = Math.Min(quantity ?? int.MaxValue, total - keep);
        var picked = new List<int>();
        var sold = 0;
        foreach (var i in Enumerable.Range(0, stacks.Count).OrderBy(i => stacks[i]).ThenBy(i => i))
        {
            if (sold + stacks[i] > limit) break;
            picked.Add(i);
            sold += stacks[i];
        }
        return picked;
    }

    /// <summary>
    /// Whether to answer Yes to a question the game asks while selling: its "Sell … for … gil?", and its warning about a unique
    /// and untradable item only when <paramref name="sellUntradable"/> is set. Anything else gets No. The prompts are given as
    /// their text sheet rows' fixed pieces (see <see cref="Ui.PromptTemplate"/>).
    /// </summary>
    public static bool Confirms(string prompt, IReadOnlyList<string> sellPrompt, IReadOnlyList<string> untradableWarning, bool sellUntradable) =>
        Ui.PromptTemplate.Matches(prompt, sellPrompt)
        || (sellUntradable && Ui.PromptTemplate.Matches(prompt, untradableWarning));

    /// <summary>Whether the question is the game's warning about selling a unique and untradable item.</summary>
    public static bool IsUntradableWarning(string prompt, IReadOnlyList<string> untradableWarning) =>
        Ui.PromptTemplate.Matches(prompt, untradableWarning);
}
