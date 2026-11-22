using System.Collections.Generic;
using System.Linq;
using XivMcp.Fashion;

namespace XivMcp.Gear;

/// <summary>Finds a saved gearset's pieces among what the player holds, as the game does when the set is equipped.</summary>
public static class GearsetPieces
{
    /// <summary>A gearset stores high-quality items with this added to the item id.</summary>
    public const uint HqOffset = 1_000_000;

    /// <summary>Where a held item is; a piece is looked for in this order.</summary>
    public enum Place { Equipped, Armoury, Bags }

    /// <summary>An item the player holds: its id (with <see cref="HqOffset"/> when high quality) and where it is.</summary>
    public sealed record Held(uint ItemId, Place Place, string Container, int Slot);

    /// <summary>
    /// Whether a saved slot (numbered like <see cref="GearSlots"/>) is a piece of the set at all: not empty, and not the waist.
    /// Belts were removed in Endwalker, but sets saved before that still remember one there; the game ignores it.
    /// </summary>
    public static bool IsPiece(int slot, uint itemId) => itemId != 0 && slot != GearSlots.Waist;

    /// <summary>
    /// Whether a piece counts as missing. The game sets its ItemMissing flag only after an equip attempt failed, so while the
    /// containers are loaded what <see cref="Match"/> found (<paramref name="held"/>) decides; only without them does the flag.
    /// </summary>
    public static bool IsMissing(bool containersLoaded, Held? held, bool gameSaysMissing) => containersLoaded ? held is null : gameSaysMissing;

    /// <summary>
    /// For each piece (slot, saved item id) the held item it would be equipped from, or null when it is missing. Each held item
    /// serves one piece only, so a ring worn on both hands needs two. The exact item (same quality) comes first, then the other
    /// quality of it; worn before armoury before bags. Empty and belt slots are left out.
    /// </summary>
    public static List<(int Slot, uint ItemId, Held? Held)> Match(IEnumerable<(int Slot, uint ItemId)> pieces, IEnumerable<Held> held) =>
        new Holdings(held).Match(pieces);

    /// <summary>
    /// The held items put in search order once, to match many sets against: listing the gearsets matches up to 100 of them on
    /// every switch, and sorting the bags and armoury again for each set would be wasted. Each set is matched on its own.
    /// </summary>
    public sealed class Holdings(IEnumerable<Held> held)
    {
        // OrderBy is stable, so items in the same place keep the order they were listed in (container, then slot).
        private readonly Held[] ordered = held.OrderBy(h => h.Place).ToArray();

        /// <summary>One set's pieces matched against the held items, as <see cref="GearsetPieces.Match"/> describes.</summary>
        public List<(int Slot, uint ItemId, Held? Held)> Match(IEnumerable<(int Slot, uint ItemId)> pieces)
        {
            var result = pieces.Where(p => IsPiece(p.Slot, p.ItemId)).Select(p => (p.Slot, p.ItemId, Held: (Held?)null)).ToList();
            var free = new List<Held>(ordered);
            foreach (var exact in new[] { true, false })
                for (var i = 0; i < result.Count; i++)
                {
                    if (result[i].Held is not null) continue;
                    var wanted = result[i].ItemId;
                    var index = free.FindIndex(h => exact ? h.ItemId == wanted : h.ItemId % HqOffset == wanted % HqOffset);
                    if (index < 0) continue;
                    result[i] = result[i] with { Held = free[index] };
                    free.RemoveAt(index);
                }
            return result;
        }
    }
}
