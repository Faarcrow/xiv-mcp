using System.Collections.Generic;
using System.Linq;

namespace XivMcp.Maps;

/// <param name="Id">Aetheryte row.</param>
/// <param name="ToTarget">Flat distance from it to the target, if its position is known.</param>
/// <param name="ToPlayer">Flat distance from it to the player, if the player is in its zone and its position is known.</param>
/// <param name="Attuned">The character has attuned to it: the game teleports only to attuned aetherytes and shards.</param>
public readonly record struct TeleportOption(uint Id, bool IsAetheryte, float? ToTarget, float? ToPlayer, bool Attuned = true);

/// <summary>
/// Which aetheryte or aethernet shard gets the player near a target. Map positions carry no height, so the nearest shard can sit
/// on a level the target can't be walked to from (Limsa's Airship Landing, reached only by lift). When the walk from there runs
/// into an obstacle, travel is planned again as a retry: away from the shards already tried and from the one the player stands
/// at, and never "just walk".
/// </summary>
public static class TeleportPick
{
    /// <summary>A shard this close to the player is where the player already is.</summary>
    public const float StandingAt = 40f;

    /// <summary>Walking is preferred when the player is at most this much farther from the target than the best option.</summary>
    public const float WalkSlack = 30f;

    /// <param name="playerToTarget">Flat distance from the player to the target, or null when the player is in another zone.</param>
    /// <returns>The option to travel to, or null to walk (on a retry: nothing else is left).</returns>
    public static TeleportOption? Choose(IReadOnlyList<TeleportOption> options, float? playerToTarget, bool retry, ICollection<uint> tried)
    {
        var candidates = options.Where(o => o.Attuned && !tried.Contains(o.Id) && !(retry && o.ToPlayer < StandingAt)).ToList();
        if (candidates.Count == 0) return null;
        var best = candidates.OrderBy(o => o.ToTarget ?? float.MaxValue).ThenByDescending(o => o.IsAetheryte).First();
        if (!retry && playerToTarget is { } walk && walk <= (best.ToTarget ?? float.MaxValue) + WalkSlack) return null;
        return best;
    }

    /// <summary>The zone has aetherytes or shards, but the character has attuned to none of them.</summary>
    public static bool NoneAttuned(IReadOnlyList<TeleportOption> options) => options.Count > 0 && !options.Any(o => o.Attuned);
}

/// <summary>A zone with aetherytes or shards of one aethernet group: its territory, intended use, and whether the group's main aetheryte stands there.</summary>
public readonly record struct AethernetZone(uint Territory, uint IntendedUse, bool HasMain);

/// <summary>Whether the character can travel to an aetheryte or aethernet shard.</summary>
public static class AetheryteAccess
{
    /// <summary>
    /// The zones of an aethernet group that are its city: those of the same kind (intended use) as the main aetheryte's zone, such as
    /// both Gridanias or the Gold Saucer and Chocobo Square. Most groups also have destinations in field zones (the city gates, Radz-at-Han's
    /// The Gate of First Sight in Thavnair, Gangos); being in such a zone is not being in the city.
    /// </summary>
    public static IReadOnlySet<uint> CityZones(IEnumerable<AethernetZone> zones)
    {
        var list = zones.ToList();
        var main = list.Where(z => z.HasMain).Select(z => (uint?)z.IntendedUse).FirstOrDefault();
        return list.Where(z => main is null ? z.HasMain : z.IntendedUse == main).Select(z => z.Territory).ToHashSet();
    }

    /// <summary>
    /// An aetheryte: once attuned. An aethernet shard: once attuned, and from outside its city only when the city's own aetheryte is
    /// attuned too, since getting there means teleporting to that aetheryte first and taking the aethernet from it.
    /// </summary>
    /// <param name="inCity">The player is in one of the city zones of the shard's aethernet group (see <see cref="CityZones"/>).</param>
    public static bool Usable(bool isAetheryte, bool attuned, bool cityAttuned, bool inCity) =>
        attuned && (isAetheryte || inCity || cityAttuned);
}
