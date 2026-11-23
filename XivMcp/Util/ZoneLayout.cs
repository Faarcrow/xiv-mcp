using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using Lumina.Excel.Sheets;
using XivMcp.Maps;

namespace XivMcp.Util;

/// <summary>
/// What a zone's layout files place, read the first time a zone is needed and kept per territory: its aetherytes and aethernet
/// shards (<see cref="AetheryteSpot"/>). Static data only, so any thread.
/// </summary>
internal static class ZoneLayout
{
    // Every aetheryte and shard the game places stands in planmap.lgb; planevent and planlive place the zone's other objects.
    private static readonly string[] Files = ["planmap.lgb"];
    private static readonly ConcurrentDictionary<uint, IReadOnlyDictionary<uint, Vector3>> Aetherytes = new();

    /// <summary>Where the zone's layout places an aetheryte or shard, or null when it has no object there.</summary>
    public static Vector3? Aetheryte(uint territory, uint aetheryte) =>
        Aetherytes.GetOrAdd(territory, ReadAetherytes).TryGetValue(aetheryte, out var at) ? at : null;

    private static IReadOnlyDictionary<uint, Vector3> ReadAetherytes(uint territory)
    {
        try
        {
            var placed = AetheryteSpot.Placed(Instances(territory)
                .Where(i => i.AssetType == LayerEntryType.Aetheryte)
                .Select(i => i.Object is LayerCommon.AetheryteInstanceObject a
                    ? (a.ParentData.BaseId, new Vector3(i.Transform.Translation.X, i.Transform.Translation.Y, i.Transform.Translation.Z))
                    : (0u, default)));
            if (placed.Count > 0) Svc.Log.Debug($"[MCP] Territory {territory}: {placed.Count} aetherytes and aethernet shards in its layout.");
            return placed;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning($"[MCP] Reading the layout of territory {territory} failed: {ex.Message}");
            return new Dictionary<uint, Vector3>();
        }
    }

    private static IEnumerable<LayerCommon.InstanceObject> Instances(uint territory)
    {
        if (Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(territory) is not { } row) yield break;
        var bg = row.Bg.ExtractText();
        var slash = bg.LastIndexOf('/');
        if (slash < 0) yield break;
        foreach (var file in Files)
        {
            if (Svc.Data.GetFile<LgbFile>($"bg/{bg[..(slash + 1)]}{file}") is not { } lgb) continue;
            foreach (var layer in lgb.Layers)
                foreach (var instance in layer.InstanceObjects)
                    yield return instance;
        }
    }
}
