using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Lumina.Excel.Sheets;
using XivMcp.Maps;

namespace XivMcp.Util;

/// <summary>
/// The aethernet shards of zones the Aetheryte sheet has none for (the Firmament, Eureka, Bozja, Zadnor, the Occult Crescent, Cosmic
/// Exploration), read from the game data the first time a zone is needed and kept per territory. The rules are in <see cref="ShardData"/>;
/// this only reads the sheets, and the zone's layout through <see cref="ZoneLayout"/>. Static data only, so any thread.
/// </summary>
internal static class ShardNetworks
{
    private static readonly string[] OrderSheets = [nameof(EurekaAethernet)];
    private static readonly IReadOnlyDictionary<uint, uint> NoNames = new Dictionary<uint, uint>();

    private static readonly ConcurrentDictionary<uint, IReadOnlyList<Shard>> Zones = new();
    // Keyed by the zone kind too: the order covers every zone of the script's kind (Eureka's four), which are read together.
    private static readonly ConcurrentDictionary<(uint Talk, uint IntendedUse), IReadOnlyDictionary<uint, uint>> OrderNames = new();
    private static readonly Lazy<IReadOnlyList<Transpoint>> Talks = new(ReadTalks, LazyThreadSafetyMode.PublicationOnly);
    private static readonly Lazy<IReadOnlyDictionary<uint, uint>> Linked = new(ReadLinked, LazyThreadSafetyMode.PublicationOnly);

    /// <summary>
    /// The zone's own shards. Empty for zones whose aetherytes and shards the Aetheryte sheet lists: those are left to it. A read that
    /// failed is not kept, so the next call tries again.
    /// </summary>
    public static IReadOnlyList<Shard> Of(uint territory) =>
        Zones.TryGetValue(territory, out var known) ? known : Load(territory) is { } loaded ? Zones.GetOrAdd(territory, loaded) : [];

    /// <summary>The aetheryte whose menu leads into a zone that has its own shards but no aetheryte (<see cref="ZoneEntry"/>), and the quest that opens the zone.</summary>
    public static (uint Aetheryte, uint? Quest)? Entry(uint territory)
    {
        if (Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territory) is not { } row) return null;
        var served = row.Aetheryte.RowId != 0 && row.Aetheryte.ValueNullable is { } a
            ? new AetheryteInfo(a.RowId, a.Territory.RowId, a.IsAetheryte, a.AethernetGroup)
            : null;
        if (ZoneEntry.Aetheryte(territory, HasOwnAetherytes(territory), Of(territory).Count, served) is not { } id) return null;
        var quests = Svc.Data.GetExcelSheet<Warp>().Where(w => w.TerritoryType.RowId == territory)
            .Select(w => w.WarpCondition.ValueNullable is { } c
                ? (IReadOnlyList<uint>)[c.RequiredQuest1.RowId, c.RequiredQuest2.RowId, c.RequiredQuest3.RowId, c.RequiredQuest4.RowId]
                : []);
        return (id, ZoneEntry.UnlockQuest(quests));
    }

    /// <summary>
    /// Whether the zone is a duty, entered through the game (Eureka, Bozja, Zadnor, the Occult Crescent): its TerritoryType names a
    /// ContentFinderCondition. The Firmament and Cosmic Exploration name none.
    /// </summary>
    public static bool IsDuty(uint territory) =>
        Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territory)?.ContentFinderCondition.RowId is > 0;

    private static bool HasOwnAetherytes(uint territory) =>
        Svc.Data.GetExcelSheet<Aetheryte>().Any(a => a.Territory.RowId == territory && (a.IsAetheryte || a.AethernetName.RowId != 0));

    /// <summary>The zone's network, or null when its data could not be read.</summary>
    private static IReadOnlyList<Shard>? Load(uint territory)
    {
        if (territory == 0 || HasOwnAetherytes(territory)) return [];
        try
        {
            var eobjs = Svc.Data.GetExcelSheet<EObj>();
            bool IsEObj(uint id) => id != 0 && eobjs.HasRow(id);
            if (ZoneLayout.EventObjects(territory) is not { } placed) return null;
            var ids = placed.Select(p => p.Id).ToHashSet();
            var talks = Talks.Value.Where(t => ShardData.Candidates(t, IsEObj).Any(ids.Contains)).ToList();
            if (talks.Count == 0) return [];

            var names = new Dictionary<uint, uint>(Linked.Value);
            foreach (var talk in talks)
                foreach (var (id, name) in NamesInOrder(talk, territory, IsEObj))
                    names[id] = name;
            var places = Svc.Data.GetExcelSheet<PlaceName>();
            var shards = ShardData.Build(talks, placed, names, Labels(territory), IsEObj, id => places.GetRowOrDefault(id)?.Name.ExtractText());
            Svc.Log.Debug($"[MCP] Territory {territory}: {shards.Count} aethernet shards from the game data ({string.Join(", ", shards.Select(s => s.Name))}).");
            return shards;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning($"[MCP] Reading the aethernet shards of territory {territory} failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>The CustomTalk scripts that run shards, with the EObj rows that point at each.</summary>
    private static IReadOnlyList<Transpoint> ReadTalks()
    {
        var usedBy = Svc.Data.GetExcelSheet<EObj>().Where(e => e.Data.RowId != 0).GroupBy(e => e.Data.RowId)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<uint>)g.Select(e => e.RowId).ToList());
        return Svc.Data.GetExcelSheet<CustomTalk>()
            .Select(t => (Row: t, Name: t.Name.ExtractText()))
            .Where(t => ShardData.IsTranspoint(t.Name))
            .Select(t => new Transpoint(t.Row.RowId, t.Name,
                t.Row.Script.Where(s => !s.ScriptInstruction.IsEmpty).Select(s => new ScriptEntry(s.ScriptInstruction.ExtractText(), s.ScriptArg)).ToList(),
                usedBy.GetValueOrDefault(t.Row.RowId) ?? []))
            .ToList();
    }

    /// <summary>
    /// Shard names WKSAetheryte links to their objects (Cosmic Exploration): its ObjectGroup's subrows hold the EObj rows, in the column
    /// Lumina calls Unknown0.
    /// </summary>
    private static IReadOnlyDictionary<uint, uint> ReadLinked()
    {
        var groups = Svc.Data.GetSubrowExcelSheet<WKSAetheryteObjectGroup>();
        return ShardData.LinkedNames(Svc.Data.GetExcelSheet<WKSAetheryte>().Select(r => (r.Name.RowId,
            (IReadOnlyList<uint>)(groups.GetRowOrDefault(r.ObjectGroup.RowId) is { } group ? group.Select(o => o.Unknown0).ToList() : []))));
    }

    /// <summary>The zone map's labelled markers, in world X/Z.</summary>
    private static List<MapLabel> Labels(uint territory)
    {
        if (Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territory)?.Map.ValueNullable is not { } map) return [];
        if (Svc.Data.GetSubrowExcelSheet<MapMarker>().GetRowOrDefault(map.MapMarkerRange) is not { } markers) return [];
        var scale = map.SizeFactor / 100f;
        return markers.Where(m => m.PlaceNameSubtext.RowId != 0)
            .Select(m => new MapLabel(m.PlaceNameSubtext.RowId, (m.X - 1024) / scale - map.OffsetX, (m.Y - 1024) / scale - map.OffsetY))
            .ToList();
    }

    /// <summary>
    /// Names from a sheet that lists the shards of a script in EObj id order (<see cref="ShardData.NamesInIdOrder"/>: EurekaAethernet). The
    /// list covers all zones of the script, so the shards of every zone of the same kind (TerritoryIntendedUse: Eureka's four) are read
    /// together, and the order is only used when their maps' labels confirm it (<see cref="ShardData.OrderConfirmed"/>).
    /// </summary>
    private static IReadOnlyDictionary<uint, uint> NamesInOrder(Transpoint talk, uint territory, Func<uint, bool> isEObj)
    {
        if (ShardData.ListedNames(talk.Script).Count > 0 || ShardData.NameSheetFor(talk.Name, OrderSheets) is null) return NoNames;
        var types = Svc.Data.GetExcelSheet<TerritoryType>();
        var use = types.GetRow(territory).TerritoryIntendedUse.RowId;
        return OrderNames.GetOrAdd((talk.Id, use), _ =>
        {
            var names = Svc.Data.GetExcelSheet<EurekaAethernet>().Select(r => r.Location.RowId).Where(id => id != 0).ToList();
            var candidates = ShardData.Candidates(talk, isEObj).ToHashSet();
            var zones = types.Where(t => t.TerritoryIntendedUse.RowId == use)
                .DistinctBy(t => t.Bg.ExtractText())
                .Select(t => (Shards: (IReadOnlyList<Placement>)(ZoneLayout.EventObjects(t.RowId)
                                  ?? throw new InvalidOperationException($"the layout of territory {t.RowId} could not be read"))
                              .Where(p => candidates.Contains(p.Id)).ToList(),
                              Labels: (IReadOnlyList<MapLabel>)Labels(t.RowId)))
                .Where(z => z.Shards.Count > 0)
                .ToList();
            if (ShardData.NamesInIdOrder(zones.SelectMany(z => z.Shards).Select(p => p.Id), names) is not { } byId)
            {
                Svc.Log.Warning($"[MCP] {OrderSheets[0]} does not fit the shards of {talk.Name}; their names come from the map instead.");
                return NoNames;
            }
            var check = ShardData.CheckOrder(byId, zones);
            if (!ShardData.OrderConfirmed(check))
            {
                Svc.Log.Warning($"[MCP] The maps do not confirm {OrderSheets[0]}'s order for the shards of {talk.Name} ({check.Agree} labels " +
                                $"agree, {check.Disagree} disagree); their names come from the map instead.");
                return NoNames;
            }
            Svc.Log.Debug($"[MCP] {OrderSheets[0]} names the {byId.Count} shards of {talk.Name}; {check.Agree} map labels agree.");
            return byId;
        });
    }
}
