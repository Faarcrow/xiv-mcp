using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace XivMcp.Maps;

/// <summary>One instruction of a CustomTalk script (CustomTalk.Script): a name and its number.</summary>
public readonly record struct ScriptEntry(string Instruction, uint Argument);

/// <summary>An event object in a zone's layout (planevent, planlive or planmap.lgb): its EObj row and where it stands.</summary>
public readonly record struct Placement(uint Id, Vector3 Position);

/// <summary>A map marker with a label (PlaceNameSubtext), in world X/Z.</summary>
public readonly record struct MapLabel(uint PlaceName, float X, float Z);

/// <summary>A CustomTalk script that runs aethernet shards, and the EObj rows whose Data points at it.</summary>
public sealed record Transpoint(uint Id, string Name, IReadOnlyList<ScriptEntry> Script, IReadOnlyCollection<uint> UsedBy);

/// <summary>
/// How a zone's own aethernet is found in the game data. Shards are event objects whose script is a CustomTalk named Cts…Transpoint…
/// (CtsHwdTranspoint_00701 in the Firmament). The script may list its shards by EObj id (EOBJ01 to EOBJ08 in the Firmament, HUB1 and
/// HUB55_1 in Bozja and Zadnor, whose other hubs point at another handler), with their names (PLACENAME01, paired by number) and unlock
/// quests (HUB34_QUEST). A zone's shards are those that stand in its layout. Names come from the script, else from a sheet: WKSAetheryte
/// links each name to its objects (Cosmic Exploration), EurekaAethernet only lists one name per shard in EObj id order. Else from the
/// nearest labelled map marker (Bozja, Zadnor, the Occult Crescent).
/// </summary>
public static class ShardData
{
    /// <summary>How far a map label can be from its shard: Zadnor's labels stand 9 to 15 yalms away (Camp Vrdelnis 14.9).</summary>
    public const float LabelReach = 15f;

    /// <summary>How far a label is looked for when checking a name order: Eureka's stand 7.7 to 24.5 yalms from their shards.</summary>
    public const float OrderCheckReach = 30f;

    private const string QuestSuffix = "_QUEST";

    public static bool IsTranspoint(string talkName) =>
        talkName.StartsWith("Cts", StringComparison.Ordinal) && talkName.Contains("Transpoint", StringComparison.OrdinalIgnoreCase);

    /// <summary>The objects a script can belong to: those that point at it, and those it lists by EObj id.</summary>
    public static IEnumerable<uint> Candidates(Transpoint talk, Func<uint, bool> isEObj) =>
        talk.UsedBy.Concat(talk.Script.Where(e => isEObj(e.Argument)).Select(e => e.Argument)).Distinct();

    /// <summary>Names the script gives its shards: PLACENAMEnn names EOBJnn. EObj row → PlaceName row.</summary>
    public static IReadOnlyDictionary<uint, uint> ListedNames(IReadOnlyList<ScriptEntry> script)
    {
        const string Object = "EOBJ", Name = "PLACENAME";
        var objects = script.Where(e => e.Instruction.StartsWith(Object, StringComparison.Ordinal))
            .GroupBy(e => e.Instruction[Object.Length..]).ToDictionary(g => g.Key, g => g.First().Argument);
        var names = new Dictionary<uint, uint>();
        foreach (var e in script.Where(e => e.Instruction.StartsWith(Name, StringComparison.Ordinal)))
            if (objects.TryGetValue(e.Instruction[Name.Length..], out var id)) names[id] = e.Argument;
        return names;
    }

    /// <summary>
    /// Unlock quests the script names: KEY_QUEST opens the shard listed as KEY, and a key ending in several digits opens each of those
    /// shards (HUB34_QUEST opens HUB3 and HUB4, HUB55_34_QUEST opens HUB55_3 and HUB55_4). That reading only works for hub numbers of one
    /// digit, which is all the game uses. EObj row → Quest row.
    /// </summary>
    public static IReadOnlyDictionary<uint, uint> UnlockQuests(IReadOnlyList<ScriptEntry> script, Func<uint, bool> isEObj)
    {
        var shards = script.Where(e => isEObj(e.Argument)).Select(e => (Key: Split(e.Instruction), Id: e.Argument)).ToList();
        var quests = new Dictionary<uint, uint>();
        foreach (var quest in script.Where(e => e.Instruction.EndsWith(QuestSuffix, StringComparison.Ordinal) && e.Argument != 0))
        {
            var (stem, digits) = Split(quest.Instruction[..^QuestSuffix.Length]);
            foreach (var ((shardStem, number), id) in shards)
                if (shardStem == stem && number.Length == 1 && digits.Contains(number[0])) quests[id] = quest.Argument;
        }
        return quests;
    }

    /// <summary>
    /// Names a sheet links to their shard objects: each WKSAetheryte row's ObjectGroup lists the EObj rows of its shard (Moongate Hub has
    /// two). EObj row → PlaceName row.
    /// </summary>
    public static IReadOnlyDictionary<uint, uint> LinkedNames(IEnumerable<(uint PlaceName, IReadOnlyList<uint> Objects)> rows)
    {
        var names = new Dictionary<uint, uint>();
        foreach (var (name, objects) in rows)
            if (name != 0)
                foreach (var id in objects.Where(id => id != 0))
                    names.TryAdd(id, name);
        return names;
    }

    /// <summary>
    /// Names from a sheet that lists one per shard of a script, in EObj id order (EurekaAethernet for Eureka's four zones). The sheet links
    /// no names to objects, so this is a heuristic: it is only used when the counts match and the maps' labels confirm it
    /// (<see cref="OrderConfirmed"/>). Pass the script's shards of all its zones. EObj row → PlaceName row.
    /// </summary>
    public static IReadOnlyDictionary<uint, uint>? NamesInIdOrder(IEnumerable<uint> shardIds, IReadOnlyList<uint> names)
    {
        var ids = shardIds.Distinct().Order().ToList();
        if (ids.Count == 0 || ids.Count != names.Count) return null;
        return ids.Zip(names).ToDictionary(p => p.First, p => p.Second);
    }

    /// <summary>
    /// Checks names given by order against the maps: where a zone's map has a label of one of those names near a shard, it has to be that
    /// shard's name. Labels of other places are ignored. In Eureka ten of the fourteen shards have such a label, and all ten agree.
    /// </summary>
    /// <param name="zones">Each zone the names cover, with its shards and its map's labels (maps of different zones overlap).</param>
    public static (int Agree, int Disagree) CheckOrder(IReadOnlyDictionary<uint, uint> names,
                                                       IEnumerable<(IReadOnlyList<Placement> Shards, IReadOnlyList<MapLabel> Labels)> zones,
                                                       float within = OrderCheckReach)
    {
        var listed = names.Values.ToHashSet();
        int agree = 0, disagree = 0;
        foreach (var (shards, labels) in zones)
        {
            var theirs = labels.Where(l => listed.Contains(l.PlaceName)).ToList();
            foreach (var shard in shards.Where(s => names.ContainsKey(s.Id)).DistinctBy(s => s.Id))
            {
                if (NearestLabel(shard.Position, theirs, within) is not { } label) continue;
                if (label.PlaceName == names[shard.Id]) agree++;
                else disagree++;
            }
        }
        return (agree, disagree);
    }

    /// <summary>Whether a <see cref="CheckOrder"/> result confirms the order: some label agrees with it, and none disagrees.</summary>
    public static bool OrderConfirmed((int Agree, int Disagree) check) => check.Agree > 0 && check.Disagree == 0;

    /// <summary>The name sheet for a script: the one named like it ("Eureka" in CtsErkEureka1Transpoint).</summary>
    public static string? NameSheetFor(string talkName, IEnumerable<string> sheets) =>
        sheets.FirstOrDefault(sheet => sheet.Replace("Aethernet", "").Replace("Aetheryte", "") is { Length: > 0 } stem &&
                                       talkName.Contains(stem, StringComparison.OrdinalIgnoreCase));

    public static MapLabel? NearestLabel(Vector3 position, IEnumerable<MapLabel> labels, float within = LabelReach) =>
        labels.Select(l => (Label: l, Distance: MapMath.GroundDistance(l.X, l.Z, position.X, position.Z)))
            .Where(x => x.Distance <= within)
            .OrderBy(x => x.Distance)
            .Select(x => (MapLabel?)x.Label)
            .FirstOrDefault();

    /// <summary>
    /// A zone's network from what was read of it: the shard scripts, the event objects in its layout, the names from a sheet
    /// (<see cref="LinkedNames"/>, <see cref="NamesInIdOrder"/>) and its map's labels. Shards without a name are left out: their menu entry
    /// could not be picked. The menu lists a destination by name, so of two objects with one name (Sinus Ardorum's hub has a second one,
    /// 1.6 yalms higher) only the lower EObj row is kept.
    /// </summary>
    public static IReadOnlyList<Shard> Build(IReadOnlyList<Transpoint> talks, IEnumerable<Placement> placed, IReadOnlyDictionary<uint, uint> listNames,
                                             IReadOnlyList<MapLabel> labels, Func<uint, bool> isEObj, Func<uint, string?> placeName)
    {
        var talkOf = new Dictionary<uint, Transpoint>();
        foreach (var talk in talks)
            foreach (var id in Candidates(talk, isEObj))
                talkOf.TryAdd(id, talk);
        var scriptNames = new Dictionary<uint, IReadOnlyDictionary<uint, uint>>();
        var quests = new Dictionary<uint, IReadOnlyDictionary<uint, uint>>();

        var shards = new List<Shard>();
        foreach (var p in placed.Where(p => talkOf.ContainsKey(p.Id)).OrderBy(p => p.Id))
        {
            var talk = talkOf[p.Id];
            if (!scriptNames.TryGetValue(talk.Id, out var listed)) scriptNames[talk.Id] = listed = ListedNames(talk.Script);
            if (!quests.TryGetValue(talk.Id, out var unlocks)) quests[talk.Id] = unlocks = UnlockQuests(talk.Script, isEObj);
            var name = listed.TryGetValue(p.Id, out var n) ? n
                       : listNames.TryGetValue(p.Id, out var s) ? s
                       : NearestLabel(p.Position, labels)?.PlaceName ?? 0;
            if (name == 0 || shards.Any(s => s.PlaceName == name) || placeName(name) is not { Length: > 0 } text) continue;
            shards.Add(new Shard(p.Id, name, text, p.Position, unlocks.TryGetValue(p.Id, out var quest) ? quest : null));
        }
        return shards;
    }

    private static (string Stem, string Digits) Split(string key)
    {
        var i = key.Length;
        while (i > 0 && char.IsAsciiDigit(key[i - 1])) i--;
        return (key[..i], key[i..]);
    }
}
