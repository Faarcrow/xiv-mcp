using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Lumina.Excel.Sheets;
using XivMcp.Farming;
using XivMcp.Mcp;
using XivMcp.Util;
using static XivMcp.Util.Navigation;

namespace XivMcp.Tools;

/// <summary>
/// Farming monsters for a drop: on the highest item level combat gearset, travel to where the monster lives (Teamcraft's drop data),
/// target the nearest living one, walk into range and let RotationSolver fight, until the bags hold enough.
/// </summary>
internal static partial class NavigationTools
{
    private const string RotationSolver = "RotationSolver";

    public static McpTool FarmMonsterDropsTool(Configuration config) => new()
    {
        Name = "farm_monster_drops",
        Description = "Kills monsters until the bags hold 'quantity' of an item they drop: switches to the combat gearset with the highest item " +
                      "level (or 'gearset'; when a set fails to equip, e.g. a missing weapon, the next best is tried), travels to where the monster lives (from Teamcraft's drop data; 'monster' picks one when several drop it), targets " +
                      "the nearest living one, walks into range and lets RotationSolver fight (its auto mode), and waits at the spawn area for " +
                      "respawns; below 70% HP out of combat it rests before pulling the next. Stops when enough are in the bags, on death, at the timeout, or after 'stall_minutes' " +
                      "without a kill or a drop. RotationSolver is switched off again at the end. Meant as a job step. Requires RotationSolver, " +
                      "vnavmesh, 'Game & navigation' and 'Online lookups'.",
        InputSchema = """
            {
              "type": "object",
              "properties": {
                "item": { "type": "string", "description": "The item (name or id) the monster drops." },
                "quantity": { "type": "integer", "minimum": 1, "description": "How many to have in the bags (default 1)." },
                "monster": { "type": "string", "description": "Which monster to farm, when several drop the item (default: the first Teamcraft lists with a position)." },
                "gearset": { "type": "string", "description": "Fight with this gearset (number, name or job) instead of the highest item level combat set." },
                "timeout_minutes": { "type": "integer", "minimum": 1, "maximum": 600, "description": "Give up after this long (default 120)." },
                "stall_minutes": { "type": "integer", "minimum": 1, "maximum": 120, "description": "Give up after this long without a kill or a drop (default 10)." }
              },
              "required": ["item"]
            }
            """,
        ReadOnly = false,
        Handler = async (args, ct) =>
        {
            if (!config.AllowGameNavigation)
                throw new ToolException("Farming is disabled. Enable \"Game & navigation\" in the XIV MCP settings window (/xivmcp) in game.");
            ItemSourceTools.RequireOnline(config);
            if (!PluginCompat.IsLoaded(RotationSolver)) throw new ToolException("RotationSolver isn't installed or loaded; it does the fighting.");
            if (!VnavmeshLoaded) throw new ToolException("vnavmesh isn't installed or loaded; it does the walking.");
            var wanted = args.Int("quantity", 1, 1, 9999);
            var timeout = TimeSpan.FromMinutes(args.Int("timeout_minutes", 120, 1, 600));
            var stall = TimeSpan.FromMinutes(args.Int("stall_minutes", 10, 1, 120));
            var monsterArg = args.String("monster");
            var item = await Game.Run(() => Items.Resolve(args.String("item") ?? throw new ToolException("'item' is required."))).ConfigureAwait(false);
            var sources = await Teamcraft.Sources(item.RowId, ct).ConfigureAwait(false);
            var target = await Game.Run(() => PickDrop(sources, monsterArg, item.Name.ExtractText())).ConfigureAwait(false);
            var steps = new List<string> { $"{target.Name} in {TerritoryName(target.Spot.Territory)} drops {item.Name.ExtractText()}." };

            // The strongest combat job; a set that turns out to miss a piece (the game only notices when equipping it) gives way to the next.
            var chosen = args.String("gearset");
            var candidates = chosen is not null
                ? new List<string> { chosen }
                : (await Game.Run(() => CombatGearset.Ranked(Gearsets.All().Select(s => new GearsetChoice(s.Number, s.CombatJob, s.ItemLevel, s.MissingItems))))
                      .ConfigureAwait(false)).Take(3).Select(n => n.ToString()).ToList();
            if (candidates.Count == 0) throw new ToolException("No combat gearset is saved.");
            Gearsets.Set? set = null;
            foreach (var candidate in candidates)
            {
                try { set = await Gearsets.Switch(candidate, ct, steps: steps).ConfigureAwait(false); break; }
                catch (ToolException e) when (candidate != candidates[^1]) { steps.Add($"{e.Message} Trying the next gearset."); }
            }
            steps.Add($"Gearset {set!.Number} {set.Name} ({set.Job}, item level {set.ItemLevel}).");

            await Game.RunLoggedIn(() => { EnsureCanTravel(); return true; }).ConfigureAwait(false);
            await TravelNear(target.Spot, steps, ct).ConfigureAwait(false);
            await WaitForMesh(ct).ConfigureAwait(false);
            // Map positions have no height: walk to the nearest reachable ground there.
            var spawn = await Game.Run(() => GroundNear(target.Spot.Position)).ConfigureAwait(false)
                        ?? throw new ToolException($"vnavmesh finds no ground to walk to where {target.Name} lives.");
            phase = $"farming {target.Name}";

            var started = DateTime.UtcNow;
            var progressAt = started;
            var have = await Game.Run(() => Items.CountInBags(item.RowId)).ConfigureAwait(false);
            var kills = 0;
            ulong? fighting = null;
            var lastAuto = DateTime.MinValue;
            var unreachable = new UnreachableMobs();
            XivMcp.Maps.Approach? approach = null;
            ulong? approachFor = null;
            ulong? flewFor = null;
            bool Usable(ulong id) => !unreachable.IsSkipped(id, DateTime.UtcNow);
            var range = await Game.Run(() => MobFarm.Range((byte)(Svc.Objects.LocalPlayer?.ClassJob.ValueNullable?.Role ?? 3))).ConfigureAwait(false);
            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    // No player for a moment is a zone load; for a minute it is a lost connection (Look then fails as logged out).
                    for (var gone = 0; gone < 60 && await Game.Run(() => Svc.Objects.LocalPlayer is null).ConfigureAwait(false); gone++)
                        await Task.Delay(1000, ct).ConfigureAwait(false);
                    var view = await Game.Run(() => Look(target.NameId, spawn, fighting, item.RowId, Usable)).ConfigureAwait(false);
                    if (view.Have != have) { have = view.Have; progressAt = DateTime.UtcNow; }
                    if (fighting is not null && !view.FightingAlive) { kills++; fighting = null; progressAt = DateTime.UtcNow; }
                    var now = DateTime.UtcNow;
                    var decision = MobFarm.Next(new MobFarmState(view.Have, wanted, view.HpPercent, view.Dead, view.InCombat, view.FightingAlive,
                        view.NearestDistance, view.DistanceToSpawn, now - started, timeout, now - progressAt, stall));
                    switch (decision.Action)
                    {
                        case MobFarmAction.Done:
                            return new { item = item.Name.ExtractText(), inBags = view.Have, wanted, kills, minutes = Math.Round((now - started).TotalMinutes, 1), steps };
                        case MobFarmAction.Fail:
                            throw new ToolException($"{decision.Reason} {view.Have}/{wanted} {item.Name.ExtractText()} after {kills} kill(s).");
                        case MobFarmAction.Engage:
                            if (await Game.Run(() => Svc.Condition[ConditionFlag.Mounted]).ConfigureAwait(false))
                                if (!await TryGetOff(spawn, steps, ct).ConfigureAwait(false)) break;
                            fighting = await Game.Run(() =>
                            {
                                var mob = NearestMob(target.NameId, Usable);
                                if (mob is null) return (ulong?)null;
                                Svc.Targets.Target = mob;
                                Svc.Commands.ProcessCommand("/rotation Auto");
                                return mob.GameObjectId;
                            }).ConfigureAwait(false);
                            break;
                        case MobFarmAction.Fight:
                            var pickedMob = fighting;
                            var inCombat = view.InCombat;
                            var sendAuto = DateTime.UtcNow - lastAuto > TimeSpan.FromSeconds(3);
                            var (autoSent, walkingDistance, inRange) = await Game.Run(() =>
                            {
                                // The monster we picked: the game doesn't take a target that is far away, so keep setting it
                                // while walking there, and fall back to the current target once a fight is on.
                                var mob = pickedMob is { } id ? Svc.Objects.OfType<IBattleNpc>().FirstOrDefault(o => o.GameObjectId == id) : null;
                                var t = mob ?? Svc.Targets.Target;
                                if (t is null) return (false, (float?)null, false);
                                if (mob is not null && Svc.Targets.Target?.GameObjectId != mob.GameObjectId) Svc.Targets.Target = mob;
                                if (Game.DistanceToPlayer(t.Position) is { } d && d > range + t.HitboxRadius)
                                {
                                    // Pathfinding again every half second would restart it each time and never move.
                                    if (!PathRunning) MoveCloseTo(t.Position, range + t.HitboxRadius - 0.5f);
                                    return (false, (float?)d, false);
                                }
                                if (PathRunning) StopMoving();
                                var here = Game.DistanceToPlayer(t.Position);
                                // RotationSolver switches itself off after a fight, so the next monster is never attacked: in range
                                // and not fighting yet, switch it on again (every few seconds, not every tick).
                                if (!inCombat && sendAuto) { Svc.Commands.ProcessCommand("/rotation Auto"); return (true, here, true); }
                                return (false, here, true);
                            }).ConfigureAwait(false);
                            if (autoSent) lastAuto = DateTime.UtcNow;
                            // Arrived by mount (after flying over an obstacle): get off, nothing can be cast from the saddle.
                            if (inRange && await Game.Run(() => Svc.Condition[ConditionFlag.Mounted]).ConfigureAwait(false))
                                await TryGetOff(spawn, steps, ct).ConfigureAwait(false);

                            // Walking up to a monster we don't fight yet but not getting closer (behind trees, a cliff), or standing in
                            // range while no fight starts (no line of sight, e.g. it stands on a ledge): fly there, then give it up.
                            if (walkingDistance is { } dist && !inCombat && pickedMob is { } walkingTo)
                            {
                                if (approachFor != walkingTo) { approach = new XivMcp.Maps.Approach(UnreachableMobs.StallWindow); approachFor = walkingTo; }
                                if (approach!.Stalled(dist, DateTime.UtcNow) && flewFor != walkingTo)
                                {
                                    // Walking doesn't get there (trees, a cliff): mount up and fly over it, where the zone allows it.
                                    flewFor = walkingTo;
                                    approach = new XivMcp.Maps.Approach(UnreachableMobs.StallWindow + UnreachableMobs.StallWindow);
                                    var flyTo = await Game.Run(() => Svc.Objects.OfType<IBattleNpc>().FirstOrDefault(o => o.GameObjectId == walkingTo)?.Position).ConfigureAwait(false);
                                    if (flyTo is { } pos)
                                    {
                                        await Game.Run(() => { StopMoving(); return true; }).ConfigureAwait(false);
                                        await StartPath(pos, inRange ? 3f : range, steps, ct, forceMount: true).ConfigureAwait(false);
                                        steps.Add($"Could not walk up to a {target.Name} {dist:0} yalms away; flying there.");
                                    }
                                }
                                else if (approach.Stalled(dist, DateTime.UtcNow))
                                {
                                    unreachable.Skip(walkingTo, DateTime.UtcNow);
                                    await Game.Run(() => { StopMoving(); Svc.Targets.Target = null; return true; }).ConfigureAwait(false);
                                    steps.Add($"Could not reach a {target.Name} {dist:0} yalms away; trying another one.");
                                    fighting = null;
                                    approachFor = null;
                                }
                            }
                            break;
                        case MobFarmAction.ReturnToSpawn:
                            if (!await Game.Run(() => PathRunning).ConfigureAwait(false))
                                await StartPath(spawn, 10f, steps, ct).ConfigureAwait(false); // mounts up (and flies) for a longer way
                            break;
                        case MobFarmAction.Rest:
                            await Game.Run(() => { if (PathRunning) StopMoving(); return true; }).ConfigureAwait(false);
                            break;
                        case MobFarmAction.Wait:
                            break;
                    }
                    await Task.Delay(500, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                // Monsters still attacking keep the character in combat for good once RotationSolver is off (no teleport, world
                // visit or job change): fight on until combat ends, for at most a minute, then switch it off.
                await Game.Run(() => { StopMoving(); return true; }).ConfigureAwait(false);
                for (var i = 0; i < 60 && await Game.Run(() => Svc.Objects.LocalPlayer is { IsDead: false } && Svc.Condition[ConditionFlag.InCombat]).ConfigureAwait(false); i++)
                {
                    if (i % 3 == 0) await Game.Run(() => { Svc.Commands.ProcessCommand("/rotation Auto"); return true; }).ConfigureAwait(false);
                    await Task.Delay(1000, CancellationToken.None).ConfigureAwait(false);
                }
                await Game.Run(() =>
                {
                    Svc.Commands.ProcessCommand("/rotation Off");
                    StopMoving();
                    return true;
                }).ConfigureAwait(false);
                phase = "idle";
            }
        },
    };

    /// <summary>
    /// Gets off the mount; where the ground below can't be landed on (water, a steep slope), flies a bit towards the spawn area's
    /// ground instead and returns false, so the next tick tries again from there rather than ending the farm.
    /// </summary>
    private static async Task<bool> TryGetOff(Vector3 spawn, List<string> steps, CancellationToken ct)
    {
        try { await GetOffMount(steps, ct).ConfigureAwait(false); return true; }
        catch (ToolException)
        {
            await Game.Run(() => { MoveCloseTo(spawn, 5f, Svc.Condition[ConditionFlag.InFlight]); return true; }).ConfigureAwait(false);
            await Task.Delay(2500, ct).ConfigureAwait(false);
            await Game.Run(() => { StopMoving(); return true; }).ConfigureAwait(false);
            steps.Add("Could not land here; moved towards the spawn area's ground.");
            return false;
        }
    }

    private sealed record DropTarget(string Name, uint NameId, NpcSpot Spot);

    /// <summary>The monster to farm, from Teamcraft's drops: the named one, else the first with a zone and position. Framework thread.</summary>
    private static DropTarget PickDrop(JsonArray? sources, string? monster, string itemName)
    {
        var drops = (sources ?? []).OfType<JsonObject>()
            .Where(s => JsonNumber.Int(s["type"]) == (int)Teamcraft.SourceType.Drops)
            .SelectMany(s => s["data"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Where(d => d["zoneid"] is not null && d["position"] is JsonObject)
            .ToList();
        var territories = Svc.Data.GetExcelSheet<TerritoryType>();
        foreach (var d in drops)
        {
            var nameId = (uint)(JsonNumber.Int(d["id"]) ?? 0);
            var name = Excel.NameOf<BNpcName>(nameId) is { Length: > 0 } n ? Game.TitleCase(n) : $"monster {nameId}";
            if (monster is not null && !Game.Matches(name, monster)) continue;
            var zone = (uint)(JsonNumber.Int(d["zoneid"]) ?? 0);
            var territory = territories.Where(t => t.PlaceName.RowId == zone && t.Map.RowId != 0)
                                       .OrderBy(t => t.TerritoryIntendedUse.RowId == 1 ? 0 : 1).FirstOrDefault();
            if (territory.RowId == 0 || territory.Map.ValueNullable is not { } map) continue;
            var p = (JsonObject)d["position"]!;
            var position = MapToWorld(map, p["x"]?.GetValue<double>() ?? 0, p["y"]?.GetValue<double>() ?? 0);
            return new DropTarget(name, nameId, new NpcSpot(0, name, territory.RowId, map.RowId, position, false));
        }
        throw new ToolException(drops.Count == 0
            ? $"Teamcraft knows no monster with a location that drops {itemName}."
            : $"No monster named '{monster}' drops {itemName}; it drops from {string.Join(", ", drops.Select(d => Excel.NameOf<BNpcName>((uint)(JsonNumber.Int(d["id"]) ?? 0))).Distinct())}.");
    }

    private sealed record FarmView(int Have, double HpPercent, bool Dead, bool InCombat, bool FightingAlive, double? NearestDistance, double DistanceToSpawn);

    /// <summary>What the farm loop needs to decide. Framework thread.</summary>
    private static FarmView Look(uint nameId, Vector3 spawn, ulong? fighting, uint itemId, Func<ulong, bool> usable)
    {
        // No player at all is a lost connection or a zone change, not a death (and the bags then read empty): fail as logged
        // out, which a job retries once the character is back.
        var p = Svc.Objects.LocalPlayer
                ?? throw new ToolException("No character is currently logged in (the connection may have been lost) while farming.");
        var hp = p.MaxHp > 0 ? 100.0 * p.CurrentHp / p.MaxHp : 0;
        var alive = fighting is { } id && Svc.Objects.FirstOrDefault(o => o.GameObjectId == id) is IBattleNpc b && !b.IsDead && b.CurrentHp > 0;
        var nearest = NearestMob(nameId, usable);
        return new FarmView(Items.CountInBags(itemId), hp, p.IsDead, Svc.Condition[ConditionFlag.InCombat], alive,
            nearest is null ? null : Game.DistanceToPlayer(nearest.Position),
            Vector2.Distance(new(p.Position.X, p.Position.Z), new(spawn.X, spawn.Z)));
    }

    /// <summary>The nearest reachable mesh point at a map position (no height), searching wider and higher step by step. Framework thread.</summary>
    private static Vector3? GroundNear(Vector3 position)
    {
        foreach (var (radius, height) in new[] { (10f, 60f), (30f, 300f), (60f, 600f) })
            if (Ipc<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable", position with { Y = PlayerHeight() }, radius, height) is { } p)
                return p;
        return null;
    }

    /// <summary>The nearest living, targetable monster of the kind. Framework thread.</summary>
    private static IBattleNpc? NearestMob(uint nameId, Func<ulong, bool> usable) =>
        Svc.Objects.OfType<IBattleNpc>()
            .Where(o => o.NameId == nameId && o.IsTargetable && !o.IsDead && o.CurrentHp > 0 && usable(o.GameObjectId))
            .OrderBy(o => Game.DistanceToPlayer(o.Position) ?? float.MaxValue)
            .FirstOrDefault();
}
