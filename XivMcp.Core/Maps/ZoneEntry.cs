using System.Collections.Generic;
using System.Linq;

namespace XivMcp.Maps;

/// <summary>An Aetheryte sheet row, as far as entering a zone needs it.</summary>
public sealed record AetheryteInfo(uint Id, uint Territory, bool IsAetheryte, uint AethernetGroup);

/// <summary>
/// Zones with their own aethernet but no aetheryte to teleport to. The Firmament is a district of Ishgard: its TerritoryType.Aetheryte is
/// the Foundation's, and that aetheryte's menu travels there (the game lists it in no aethernet group). Cosmic Exploration's zones also
/// name an aetheryte in another zone (Bestways Burrow), but one without an aethernet: they are left out, like the zones entered through
/// a duty (Eureka, Bozja, the Occult Crescent), which name none.
/// </summary>
public static class ZoneEntry
{
    /// <summary>The aetheryte whose aethernet leads into the zone, or null.</summary>
    /// <param name="ownAetherytes">Whether the Aetheryte sheet has an aetheryte or shard in the zone.</param>
    /// <param name="shards">How many shards the zone's own network has.</param>
    /// <param name="servedBy">The zone's TerritoryType.Aetheryte, if it names one.</param>
    public static uint? Aetheryte(uint territory, bool ownAetherytes, int shards, AetheryteInfo? servedBy) =>
        !ownAetherytes && shards > 0 && servedBy is { IsAetheryte: true, AethernetGroup: not 0 } a && a.Territory != territory ? a.Id : null;

    /// <summary>
    /// The quest that opens the zone: the one every warp into it that needs a quest asks for (the Firmament's entrance NPCs both ask for
    /// "Towards the Firmament"). Null when no warp needs one, or they disagree.
    /// </summary>
    /// <param name="warpQuests">For each warp into the zone, the quests its WarpCondition requires.</param>
    public static uint? UnlockQuest(IEnumerable<IReadOnlyList<uint>> warpQuests)
    {
        var gated = warpQuests.Select(q => q.Where(id => id != 0).ToHashSet()).Where(q => q.Count > 0).ToList();
        if (gated.Count == 0) return null;
        IEnumerable<uint> common = gated[0];
        foreach (var quests in gated.Skip(1)) common = common.Intersect(quests);
        var only = common.ToList();
        return only.Count == 1 ? only[0] : null;
    }
}
