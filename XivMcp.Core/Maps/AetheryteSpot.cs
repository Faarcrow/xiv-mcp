using System;
using System.Collections.Generic;
using System.Numerics;

namespace XivMcp.Maps;

/// <summary>
/// Where an aetheryte or aethernet shard stands. The zone's layout places it exactly, with its height: an Aetheryte instance whose
/// BaseId is the Aetheryte row, in the zone's planmap.lgb (197 of the game's 229 aetherytes and shards). The other 32, mostly
/// aethernet destinations in another zone such as a city's gates into the field, have no object of their own in their zone; for
/// those, a Level row or a map marker. The layout comes first: a marker that points at the aetheryte is right to within 15 yalms,
/// but one only named like the shard usually labels its district, and was off by up to 171 yalms (57 of the 60 found that way).
/// </summary>
public static class AetheryteSpot
{
    /// <param name="marker">Looked up only when neither the layout nor a Level row has the position (it may scan every map's markers).</param>
    public static Vector3? Choose(Vector3? placed, Vector3? level, Func<Vector3?> marker) => placed ?? level ?? marker();

    /// <summary>A zone's aetherytes and shards by Aetheryte row, from its layout's Aetheryte instances (the first one of each row).</summary>
    public static IReadOnlyDictionary<uint, Vector3> Placed(IEnumerable<(uint Aetheryte, Vector3 Position)> instances)
    {
        var placed = new Dictionary<uint, Vector3>();
        foreach (var (id, position) in instances)
            if (id != 0) placed.TryAdd(id, position);
        return placed;
    }
}
