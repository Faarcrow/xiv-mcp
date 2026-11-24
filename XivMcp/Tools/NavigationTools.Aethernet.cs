using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using XivMcp.Maps;
using XivMcp.Mcp;
using XivMcp.Util;
using static XivMcp.Util.Navigation;

namespace XivMcp.Tools;

/// <summary>
/// Zones with their own aethernet that the Aetheryte sheet does not list (see <see cref="ShardNetworks"/>): inside, walk to a shard and
/// take the aethernet when that beats walking; into the Firmament, take the Foundation aetheryte's menu. Lifestream picks the entry in
/// the shard's or aetheryte's menu; without it, the player is asked and XIV MCP waits.
/// </summary>
internal static partial class NavigationTools
{
    /// <summary>The Foundation's aetheryte (Aetheryte row): Lifestream's call into the Firmament uses its menu, and no other aetheryte's.</summary>
    private const uint FoundationAetheryte = 70;

    /// <summary>
    /// Destinations a jump did not reach, per character and zone, left out of later trips until XIV MCP restarts. Which shards a character
    /// has attuned to is not in the game data, so without this every trip would try the same shard again.
    /// </summary>
    private static readonly ConcurrentDictionary<(ulong Character, uint Territory, uint Shard), byte> ClosedShards = new();

    private static HashSet<uint> Closed(uint territory)
    {
        var me = Svc.PlayerState.ContentId;
        return ClosedShards.Keys.Where(k => k.Character == me && k.Territory == territory).Select(k => k.Shard).ToHashSet();
    }

    /// <summary>In a zone with its own network: jumps when <see cref="ShardRoute"/> says so, and otherwise leaves the walk to the caller.</summary>
    private static async Task CrossZone(NpcSpot spot, IReadOnlyList<Shard> shards, List<string> steps, CancellationToken ct, bool retry, ICollection<uint>? tried,
                                        ICollection<uint>? unreachable)
    {
        var (jump, reason) = await Game.Run(() =>
        {
            var player = Svc.Objects.LocalPlayer?.Position ?? spot.Position;
            var usable = ShardRoute.Usable(shards, quest => QuestManager.IsQuestComplete(quest), Closed(spot.Territory));
            var choice = ShardRoute.Choose(usable, player, spot.Position, retry, tried ?? [], unreachable);
            return (choice, choice is null ? WalkReason(spot, shards, usable) : null);
        }).ConfigureAwait(false);
        if (jump is null)
        {
            if (retry) throw new ToolException($"No other aetheryte or aethernet shard leads to {spot.Name}.");
            steps.Add(reason!);
            return;
        }
        tried?.Add(jump.Destination.Id);
        await Jump(jump, spot, steps, unreachable, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Walks to the first shard and takes the aethernet. When the trip does not happen (the destination is not attuned or not open to this
    /// character yet), it says so in the steps and returns, so the caller walks. A first shard the player could not walk to goes into
    /// <paramref name="unreachable"/>.
    /// </summary>
    private static async Task Jump(ShardJump jump, NpcSpot spot, List<string> steps, ICollection<uint>? unreachable, CancellationToken ct)
    {
        // Without either plugin one request covers the walk to the shard and the jump; otherwise the player gets to the shard first.
        var walkFirst = LifestreamLoaded || VnavmeshLoaded;
        if (walkFirst && !await ReachShard(jump.Board, spot.Territory, steps, ct).ConfigureAwait(false))
        {
            unreachable?.Add(jump.Board.Id);
            steps.Add($"Could not get close enough to the aethernet shard at {jump.Board.Name}; walking instead.");
            return;
        }
        if (!LifestreamLoaded)
        {
            await GuideJump(jump, spot, walkFirst, steps, ct).ConfigureAwait(false);
            return;
        }
        phase = $"taking the aethernet to {jump.Destination.Name}";
        var asked = await Game.Run(() =>
        {
            try { return AethernetTo(jump.Destination.PlaceName); }
            catch (Exception ex) { Svc.Log.Warning($"[MCP] Lifestream aethernet failed: {ex.Message}"); return false; }
        }).ConfigureAwait(false);
        if (!asked)
        {
            steps.Add($"Lifestream did not take the aethernet to {jump.Destination.Name} (it was busy); walking instead.");
            return;
        }
        steps.Add($"Lifestream: aethernet from {jump.Board.Name} to {jump.Destination.Name}.");
        var outcome = await ArrivedAt(jump.Destination, ct).ConfigureAwait(false);
        if (outcome == JumpOutcome.Arrived)
        {
            steps.Add($"Arrived at {jump.Destination.Name}.");
            return;
        }
        // The shard's menu only lists destinations the character has attuned to (or whose quest is done): Lifestream then waits in vain.
        // Without a menu, the first shard was most likely just attuned by touching it, which says nothing about the destination.
        var closes = JumpWatch.Closes(outcome);
        await Game.Run(() =>
        {
            LifestreamAbort();
            if (closes) ClosedShards.TryAdd((Svc.PlayerState.ContentId, spot.Territory, jump.Destination.Id), 0);
            return true;
        }).ConfigureAwait(false);
        if (await Game.Run(() => Recovery.Occupied).ConfigureAwait(false) && await Recovery.Run(ct).ConfigureAwait(false) is { } freed)
            steps.AddRange(freed.Actions);
        steps.Add(closes
            ? $"The aethernet did not take you to {jump.Destination.Name} (it may not be attuned or open to this character yet); walking " +
              "instead, and leaving it out of later trips until XIV MCP restarts."
            : $"The aethernet shard at {jump.Board.Name} opened no menu (touching it may have just attuned it); walking instead.");
    }

    /// <summary>Waits until the character stands at the destination shard, or the trip evidently did not happen (<see cref="JumpWatch"/>).</summary>
    private static async Task<JumpOutcome> ArrivedAt(Shard destination, CancellationToken ct)
    {
        var watch = new JumpWatch(DateTime.UtcNow);
        await Task.Delay(1000, ct).ConfigureAwait(false);
        while (true)
        {
            var (there, idle, menu) = await Game.Run(() =>
            {
                var loading = Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51];
                var near = !loading && Svc.Objects.LocalPlayer is { } p && ShardRoute.Flat(p.Position, destination.Position) <= ShardRoute.ArrivedWithin;
                // The shard's destinations are a choice window.
                return (near, !loading && !LifestreamBusy, RetainerUi.Ready("SelectString"));
            }).ConfigureAwait(false);
            var outcome = watch.Next(DateTime.UtcNow, there, idle, menu);
            if (outcome == JumpOutcome.Arrived) await WaitUntilSettled(ct).ConfigureAwait(false);
            if (outcome != JumpOutcome.Waiting) return outcome;
            await Task.Delay(250, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Gets the player within reach of a shard: walks there with vnavmesh, or flags it and waits. Returns whether they are in reach.</summary>
    private static async Task<bool> ReachShard(Shard shard, uint territory, List<string> steps, CancellationToken ct)
    {
        float Away() => Svc.Objects.LocalPlayer is { } p ? Vector3.Distance(p.Position, shard.Position) : float.MaxValue;
        bool InReach() => Away() <= ShardRoute.UseRange - 1;
        if (await Game.Run(Away).ConfigureAwait(false) <= ShardRoute.UseRange) return true;
        var label = $"the aethernet shard at {shard.Name}";
        if (!VnavmeshLoaded)
            await GuideWalk(territory, shard.Position, label, ShardRoute.UseRange - 1, InReach, steps, ct).ConfigureAwait(false);
        else
        {
            await WaitForMesh(ct).ConfigureAwait(false);
            // The shard is solid: aim for the walkable ground next to it. The layout gives its height, so no guessing which level.
            var goal = await Game.Run(() =>
                Ipc<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable", shard.Position, 3f, 5f) ?? shard.Position).ConfigureAwait(false);
            try
            {
                await WalkTowardsExact(goal, label, InReach, steps, ct).ConfigureAwait(false);
            }
            catch (ToolException ex)
            {
                // No path, or it runs into an obstacle: the caller walks to the target instead.
                steps.Add(ex.Message);
                return false;
            }
            if (!await Game.Run(InReach).ConfigureAwait(false) && await Game.Run(() => Navigation.MoveCloseTo(goal, 1f)).ConfigureAwait(false))
            {
                // The path ends a few yalms short of its goal; the last step is a short one.
                var until = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < until && await Game.Run(() => PathRunning && !InReach()).ConfigureAwait(false))
                    await Task.Delay(200, ct).ConfigureAwait(false);
                await Game.Run(() => { StopMoving(); return true; }).ConfigureAwait(false);
            }
            await GetOffMount(steps, ct).ConfigureAwait(false);
        }
        var away = await Game.Run(Away).ConfigureAwait(false);
        if (away > ShardRoute.UseRange) return false;
        steps.Add($"At {label} ({away:0.#} yalms).");
        return true;
    }

    /// <summary>
    /// Without a travel plugin: asks the player to take the aethernet, and waits. When they do not stand at the first shard yet
    /// (<paramref name="atShard"/> false: no walking plugin either), the request includes the walk there, with the flag on it.
    /// </summary>
    private static async Task GuideJump(ShardJump jump, NpcSpot spot, bool atShard, List<string> steps, CancellationToken ct)
    {
        var zone = TerritoryName(spot.Territory);
        if (!atShard)
            await Game.Run(() =>
            {
                if (Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(spot.Territory)?.Map.ValueNullable is { } map)
                    SetFlag(spot.Territory, map.RowId, jump.Board.Position, jump.Board.Name);
                return true;
            }).ConfigureAwait(false);
        PlayerGuide.Ask(atShard
            ? PlayerGuidance.UseShardHere(jump.Board.Name, jump.Destination.Name)
            : PlayerGuidance.UseShard(jump.Board.Name, jump.Destination.Name, zone));
        steps.Add($"Asked you to take the aethernet from {jump.Board.Name} to {jump.Destination.Name}{(atShard ? "" : " (flag on the map)")}.");
        phase = $"waiting for you to take the aethernet to {jump.Destination.Name}";
        // Walking on instead ends the wait too, once the player is as close to the target as the destination shard is.
        var rest = ShardRoute.Flat(jump.Destination.Position, spot.Position);
        bool AtDestination() => Svc.Objects.LocalPlayer is { } p && ShardRoute.Flat(p.Position, jump.Destination.Position) <= ShardRoute.ArrivedWithin;
        try
        {
            await WaitFor(() => Svc.ClientState.TerritoryType == spot.Territory && Svc.Objects.LocalPlayer is { } p &&
                                (AtDestination() || ShardRoute.Flat(p.Position, spot.Position) <= rest), ct).ConfigureAwait(false);
            steps.Add(await Game.Run(AtDestination).ConfigureAwait(false) ? $"You arrived at {jump.Destination.Name}." : $"You walked on towards {spot.Name}.");
        }
        finally
        {
            if (!atShard) await Game.Run(() => { ClearFlag(); return true; }).ConfigureAwait(false);
            PlayerGuide.Done();
        }
    }

    /// <summary>
    /// Into a zone entered from another zone's aetheryte (<see cref="ShardNetworks.Entry"/>: the Firmament from the Foundation): teleports
    /// there unless already close (teleports cost gil), then uses the aetheryte's menu. Without a travel plugin, asks the player.
    /// </summary>
    private static async Task EnterThroughAethernet(NpcSpot spot, uint aetheryteId, uint? quest, List<string> steps, CancellationToken ct)
    {
        var zone = TerritoryName(spot.Territory);
        await WarmLayouts(aetheryteId, ct).ConfigureAwait(false);
        var (name, territory, position, open, questName, attuned) = await Game.Run(() =>
        {
            var row = Svc.Data.GetExcelSheet<Aetheryte>().GetRow(aetheryteId);
            return (Excel.Name(row.PlaceName) ?? $"aetheryte {aetheryteId}", row.Territory.RowId, AetherytePosition(row),
                    quest is not { } q || QuestManager.IsQuestComplete(q), quest is { } id ? Excel.NameOf<Quest>(id) : null, Usable(row));
        }).ConfigureAwait(false);
        if (!open) throw new ToolException($"{zone} is not open to this character yet: it opens with the quest \"{questName}\".");
        if (!attuned) throw new ToolException($"This character cannot travel to {zone} yet: it is reached from the {name} aetheryte, which is not attuned. Go there once and attune to it, then retry.");

        if (!LifestreamLoaded)
        {
            await GuideEnter(spot, name, territory, position, steps, ct).ConfigureAwait(false);
            return;
        }
        // Checked before the teleport, so a zone Lifestream cannot enter costs no gil.
        if (aetheryteId != FoundationAetheryte)
            throw new ToolException($"{zone} is entered from the menu of the {name} aetheryte, which XIV MCP can only use for the Firmament so far; go to {zone} first, then retry.");
        if (await Game.Run(() => Recovery.Occupied).ConfigureAwait(false) && await Recovery.Run(ct).ConfigureAwait(false) is { } freed)
            steps.AddRange(freed.Actions);
        phase = $"travelling to {zone}";
        if (await Game.Run(() => Svc.ClientState.TerritoryType).ConfigureAwait(false) != territory)
            await TeleportToAetheryte(aetheryteId, name, territory, steps, ct).ConfigureAwait(false);
        else if (position is { } at && await Game.Run(() => Svc.Objects.LocalPlayer is { } p && ShardRoute.Flat(p.Position, at) > 20).ConfigureAwait(false))
        {
            // Lifestream walks up to the aetheryte from close by, not from across the city.
            // The city's aethernet shards are aetherytes too: only the one that leads there counts.
            bool Near() => Svc.Objects.Any(o => o.ObjectKind == ObjectKind.Aetheryte && o.BaseId == aetheryteId && Game.DistanceToPlayer(o.Position) < 8);
            if (VnavmeshLoaded) await WalkTowards(at, name, Near, steps, ct).ConfigureAwait(false);
            else await GuideWalk(territory, at, name, 8f, Near, steps, ct).ConfigureAwait(false);
        }

        // Lifestream's call for the Foundation aetheryte's "Travel to the Firmament", the only zone entered this way (checked above).
        for (var attempt = 1; ; attempt++)
        {
            var ok = await Game.Run(() =>
            {
                try { return AethernetToFirmament(); }
                catch (Exception ex) { Svc.Log.Warning($"[MCP] Lifestream aethernet failed: {ex.Message}"); return false; }
            }).ConfigureAwait(false);
            if (!ok) throw new ToolException($"Lifestream could not take the aetheryte's menu to {zone} (it was busy).");
            steps.Add($"Lifestream: from the {name} aetheryte to {zone}.");
            await Task.Delay(2000, ct).ConfigureAwait(false);
            await WaitUntilSettled(ct).ConfigureAwait(false);
            var now = await Game.Run(() => Svc.ClientState.TerritoryType).ConfigureAwait(false);
            if (now == spot.Territory) break;
            if (now != territory)
                throw new ToolException($"Ended up in {await Game.Run(ZoneName).ConfigureAwait(false)} instead of {zone}.");
            if (attempt == 3)
                throw new ToolException($"Lifestream did not travel to {zone}: still in {name} after three tries (is the Firmament option of the Foundation aetheryte turned on in Lifestream?).");
            steps.Add("Lifestream did not leave the zone; asking again.");
            await Task.Delay(3000, ct).ConfigureAwait(false);
        }
        steps.Add($"Arrived in {zone}.");
    }

    /// <summary>Teleports to an aetheryte in another zone: the game's own teleport from an inn room (<see cref="InnExit"/>), Lifestream's otherwise.</summary>
    private static async Task TeleportToAetheryte(uint aetheryteId, string name, uint territory, List<string> steps, CancellationToken ct)
    {
        var (start, gameTeleport) = await Game.Run(() => (Svc.ClientState.TerritoryType, InnExit.Plan(CurrentIntendedUse(), aetheryteId, false, null).Teleport)).ConfigureAwait(false);
        var ok = await Game.Run(() =>
        {
            if (gameTeleport is { } first) return GameTeleport(first);
            try { return Svc.PluginInterface.GetIpcSubscriber<uint, byte, bool>("Lifestream.Teleport").InvokeFunc(aetheryteId, 0); }
            catch (Exception ex) { Svc.Log.Warning($"[MCP] Lifestream travel failed: {ex.Message}"); return false; }
        }).ConfigureAwait(false);
        if (!ok) throw new ToolException($"The teleport to {name} was refused (is the aetheryte attuned, is there enough gil?).");
        steps.Add(gameTeleport is not null ? $"Teleported out of the inn room to {name}." : $"Lifestream: teleport to {name}.");
        await WaitForTeleport(start, name, ct).ConfigureAwait(false);
        if (await Game.Run(() => Svc.ClientState.TerritoryType).ConfigureAwait(false) != territory)
            throw new ToolException($"Ended up in {await Game.Run(ZoneName).ConfigureAwait(false)} instead of {TerritoryName(territory)}.");
    }

    /// <summary>Without a travel plugin: flags the aetheryte, asks the player to teleport there and use its menu, and waits until they are in the zone.</summary>
    private static async Task GuideEnter(NpcSpot spot, string aetheryte, uint territory, Vector3? position, List<string> steps, CancellationToken ct)
    {
        var zone = TerritoryName(spot.Territory);
        await Game.Run(() =>
        {
            if (position is { } at && Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territory)?.Map.ValueNullable is { } map)
                SetFlag(territory, map.RowId, at, aetheryte);
            return true;
        }).ConfigureAwait(false);
        PlayerGuide.Ask(PlayerGuidance.EnterThrough(aetheryte, zone));
        steps.Add($"Asked you to teleport to {aetheryte} and travel to {zone} from its aetheryte.");
        phase = $"waiting for you to travel to {zone}";
        try
        {
            await WaitFor(() => Svc.ClientState.TerritoryType == spot.Territory, ct).ConfigureAwait(false);
            await WaitUntilSettled(ct).ConfigureAwait(false);
            steps.Add($"You arrived in {zone}.");
        }
        finally
        {
            await Game.Run(() => { ClearFlag(); return true; }).ConfigureAwait(false);
            PlayerGuide.Done();
        }
    }
}
