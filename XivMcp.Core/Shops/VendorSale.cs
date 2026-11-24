using System;
using System.Collections.Generic;
using System.Linq;

namespace XivMcp.Shops;

/// <summary>What a slot of the bags or the armoury chest holds: the item (0 when empty), whether it is high quality, and how many.</summary>
public readonly record struct SlotContent(uint ItemId, bool Hq, int Quantity);

/// <summary>Which stacks to sell to a vendor. The game sells whole stacks from the context menu of the bags and the armoury chest.</summary>
public static class VendorSale
{
    /// <summary>
    /// Null when the slot still holds the stack planned for sale (same item, quality and quantity); otherwise why it doesn't. The
    /// slots are read before the player is asked about untradable items, which can take minutes, so they may have sorted, moved or
    /// used the stack meanwhile, and selling the slot then would sell something else.
    /// </summary>
    public static string? Changed(SlotContent planned, SlotContent? now)
    {
        if (now is not { ItemId: not 0, Quantity: > 0 } held) return "the slot is empty now";
        if (held.ItemId != planned.ItemId) return "another item is in the slot now";
        if (held.Hq != planned.Hq) return held.Hq ? "the slot holds the high-quality one now" : "the slot holds the normal-quality one now";
        if (held.Quantity != planned.Quantity) return $"the slot holds {held.Quantity} now, not {planned.Quantity}";
        return null;
    }

    /// <summary>
    /// Whether the game's warning about a unique and untradable item may be answered Yes for this stack: only when the player
    /// approved selling that item, and the slot still holds the planned stack (not another untradable item moved there).
    /// </summary>
    public static bool MayConfirmUntradable(SlotContent planned, SlotContent? now, IReadOnlySet<uint> approved) =>
        approved.Contains(planned.ItemId) && Changed(planned, now) is null;

    /// <summary>
    /// Why none of an item is sold, before its stacks are picked, or null: vendors don't buy it, there is none to sell or to move
    /// (<paramref name="held"/> counts the bags and the armoury chest), a gear set uses it (so selling by name could take a piece a set
    /// equips, and moving it out of the saddlebag wouldn't help), or it is only in the saddlebag, with what to do about that.
    /// </summary>
    public static string? Refusal(bool vendorsBuy, int held, int inSaddlebag, bool inGearset, bool atShop) => (vendorsBuy, held, inSaddlebag, inGearset) switch
    {
        (false, _, _, _) => "Vendors do not buy this item.",
        (_, 0, 0, _) => "None in the bags or the armoury chest.",
        (_, _, _, true) => "A gear set uses it, so it is not sold.",
        (_, 0, _, _) => $"None in the bags or the armoury chest, but {inSaddlebag} in the saddlebag. {FromSaddlebag(atShop)}",
        _ => null,
    };

    /// <summary>For an item that was sold from the bags or the armoury chest: the copies left in the saddlebag and what to do, or null.</summary>
    public static string? LeftInSaddlebag(int inSaddlebag, bool atShop) =>
        inSaddlebag > 0 ? $"{inSaddlebag} more in the saddlebag, not sold. {FromSaddlebag(atShop)}" : null;

    /// <summary>
    /// How to sell what is in the saddlebag. The game sells nothing from it (its menu has no Sell or Have Retainer Sell Items), and
    /// won't open it while a merchant's shop is open ("Unable to execute command at this time."); moving items needs its window open.
    /// Next to a retainer it opens.
    /// </summary>
    public static string FromSaddlebag(bool atShop) => atShop
        ? "The game does not sell from the saddlebag and does not open it while a shop is open: close the shop (close_window), open the " +
          "saddlebag (open_window \"Chocobo Saddlebag\"), move them to the bags (move_items), then open the shop again and sell."
        : "The game does not sell from the saddlebag: open it next to the retainer (open_window \"Chocobo Saddlebag\"), move them to the " +
          "bags (move_items) and sell again.";

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
