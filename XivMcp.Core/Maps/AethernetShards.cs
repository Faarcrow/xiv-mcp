using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace XivMcp.Maps;

/// <summary>
/// An aethernet shard of a zone whose network the Aetheryte sheet does not list (the Firmament, Eureka, Bozja, Zadnor, the Occult
/// Crescent, Cosmic Exploration). Read from the zone's layout and the shards' own event scripts, see <see cref="ShardData"/>.
/// </summary>
/// <param name="Id">The shard's EObj row: the object that stands in the zone.</param>
/// <param name="PlaceName">PlaceName row of the name its menu lists it by.</param>
/// <param name="Position">World position, with height.</param>
/// <param name="UnlockQuest">The quest that opens the shard, where the game data names one.</param>
public sealed record Shard(uint Id, uint PlaceName, string Name, Vector3 Position, uint? UnlockQuest = null);

/// <summary>A jump through a zone's own aethernet: walk to <paramref name="Board"/>, travel to <paramref name="Destination"/>, walk on from there.</summary>
public sealed record ShardJump(Shard Board, Shard Destination);

public static class ShardRoute
{
    /// <summary>A shard is used from this close (Lifestream asks for about 4.6 yalms, the game a little more).</summary>
    public const float UseRange = 4.5f;

    /// <summary>After a jump the character stands next to the destination shard: this close counts as arrived.</summary>
    public const float ArrivedWithin = 15f;

    /// <summary>
    /// The shards the player can use: those without an unlock quest, or whose quest is complete, and not <paramref name="closed"/> (a jump
    /// there that did not happen, most likely a shard the character has not attuned to; the game data does not say which ones are).
    /// </summary>
    public static IReadOnlyList<Shard> Usable(IEnumerable<Shard> shards, Func<uint, bool> questComplete, IReadOnlyCollection<uint>? closed = null) =>
        shards.Where(s => (s.UnlockQuest is not { } quest || questComplete(quest)) && closed?.Contains(s.Id) != true).ToList();

    /// <summary>
    /// Walk straight to the target (null), or jump through the zone's network. It is <see cref="TeleportPick"/>'s rule with the walk to the
    /// first shard counted: the walking a jump leaves (to the shard nearest the player, then from the destination shard to the target) has
    /// to be shorter than walking straight there by more than <see cref="TeleportPick.WalkSlack"/>. On a retry (the walk from the last
    /// shard ran into an obstacle), the shards already tried and the one the player stands at are skipped, and it never walks.
    /// </summary>
    /// <param name="usable">The zone's shards the player can use.</param>
    /// <param name="unreachable">Shards the player could not walk to: not boarded again (they can still be a destination).</param>
    public static ShardJump? Choose(IReadOnlyList<Shard> usable, Vector3 player, Vector3 target, bool retry, ICollection<uint> tried,
                                    ICollection<uint>? unreachable = null)
    {
        var boards = new Dictionary<uint, Shard>();
        var options = new List<TeleportOption>();
        foreach (var destination in usable)
        {
            var board = usable.Where(s => s.Id != destination.Id && unreachable?.Contains(s.Id) != true)
                .OrderBy(s => Flat(s.Position, player)).FirstOrDefault();
            if (board is null) continue;
            boards[destination.Id] = board;
            options.Add(new TeleportOption(destination.Id, false, Flat(board.Position, player) + Flat(destination.Position, target),
                                           Flat(destination.Position, player)));
        }
        if (TeleportPick.Choose(options, Flat(player, target), retry, tried) is not { } pick) return null;
        return new ShardJump(boards[pick.Id], usable.First(s => s.Id == pick.Id));
    }

    public static float Flat(Vector3 a, Vector3 b) => (float)MapMath.GroundDistance(a.X, a.Z, b.X, b.Z);
}

/// <summary>How a jump that was asked for ended (see <see cref="JumpWatch"/>).</summary>
public enum JumpOutcome
{
    Waiting,
    /// <summary>The character stands at the destination shard.</summary>
    Arrived,
    /// <summary>The shard's menu opened, but the trip did not happen: the destination is not in it (not attuned, not open yet).</summary>
    NotInMenu,
    /// <summary>No menu opened: touching a shard the character had not attuned to attunes it and opens none.</summary>
    NoMenu,
}

/// <summary>
/// Watches a jump through a zone's aethernet after the travel plugin was asked for it. It has failed once the plugin has been idle for
/// <see cref="Idle"/> without the character arriving, or after <see cref="Limit"/> in all (a plugin that cannot find the destination in
/// the menu keeps looking). Only a failure with the menu seen open says the destination is closed to the character.
/// </summary>
public sealed class JumpWatch(DateTime start)
{
    public static readonly TimeSpan Idle = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    private DateTime? idleSince;
    private bool menuSeen;

    /// <param name="arrived">The character stands at the destination shard.</param>
    /// <param name="idle">The travel plugin is idle and no zone is loading.</param>
    /// <param name="menuOpen">The shard's menu (the game's choice window) is open.</param>
    public JumpOutcome Next(DateTime now, bool arrived, bool idle, bool menuOpen)
    {
        menuSeen |= menuOpen;
        if (arrived) return JumpOutcome.Arrived;
        idleSince = idle ? idleSince ?? now : null;
        var failed = now - start > Limit || (idleSince is { } since && now - since > Idle);
        return !failed ? JumpOutcome.Waiting : menuSeen ? JumpOutcome.NotInMenu : JumpOutcome.NoMenu;
    }

    /// <summary>Whether the destination should be left out of later trips.</summary>
    public static bool Closes(JumpOutcome outcome) => outcome == JumpOutcome.NotInMenu;
}
