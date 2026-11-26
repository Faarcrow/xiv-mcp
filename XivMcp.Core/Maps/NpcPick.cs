using System.Collections.Generic;
using System.Linq;

namespace XivMcp.Maps;

/// <summary>
/// One NPC of a name: where the game places it (its zone, and the area the zone belongs to, e.g. Ishgard for Fortemps Manor and the
/// Pillars), and what its ENpcData lists (quests, talk, shops, a door, ...).
/// </summary>
public sealed record NpcCandidate(uint Id, uint Territory, uint Area, IReadOnlyList<uint> Data);

/// <summary>
/// Which of several NPCs with the same name to go to. Many names are placed in several zones (over a thousand): a quest puts copies
/// of an NPC where its story happens, and those copies only show while the quest asks for them. Their ENpcData lists quests and
/// nothing else, so the NPC that also does something else (talks, sells, plays Triple Triad, opens a door) is the one that stands
/// there for good.
/// </summary>
public static class NpcPick
{
    private const uint QuestHandler = 1; // EventHandlerType of a Quest row: the high 16 bits of its id

    /// <summary>
    /// The NPC to go to: one that stands there for good, then the nearest (in the player's zone, then in the player's area), then the
    /// one with the most data. Two NPCs that both stand there for good share a name too (two House Fortemps guards, in the Pillars
    /// and in Coerthas): then only nearness decides.
    /// </summary>
    public static NpcCandidate? Best(IEnumerable<NpcCandidate> candidates, uint playerTerritory, uint playerArea) =>
        candidates.OrderBy(c => OnlyQuests(c) ? 1 : 0)
                  .ThenBy(c => c.Territory == playerTerritory ? 0 : playerArea != 0 && c.Area == playerArea ? 1 : 2)
                  .ThenByDescending(c => c.Data.Count)
                  .FirstOrDefault();

    /// <summary>An NPC whose data lists only quests (or nothing): a copy that appears during a quest.</summary>
    public static bool OnlyQuests(NpcCandidate c) => c.Data.All(d => d >> 16 == QuestHandler);
}
