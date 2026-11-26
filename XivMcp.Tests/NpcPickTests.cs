using XivMcp.Maps;

namespace XivMcp.Tests;

public class NpcPickTests
{
    // House Fortemps manservant as the game data has him (2026-11): quest copies in the Foundation and the Pillars, the one who plays
    // Triple Triad in Fortemps Manor (433).
    private const uint Ishgard = 512, Coerthas = 2;

    private static readonly NpcCandidate FoundationCopy = new(1012310, 418, Ishgard, [0x1062C]);
    private static readonly NpcCandidate PillarsCopy = new(1012316, 419, Ishgard, [0x1062D, 0x1025F]);
    private static readonly NpcCandidate InTheManor = new(1012337, 433, Ishgard, [0x1A000F, 0x1F0103, 0x10668, 0x230038]);

    [Fact]
    public void The_npc_that_does_more_than_quests_wins_over_quest_copies_even_in_the_players_zone() =>
        Assert.Equal(InTheManor, NpcPick.Best([FoundationCopy, PillarsCopy, InTheManor], playerTerritory: 418, playerArea: Ishgard));

    [Fact]
    public void Among_equals_the_players_zone_comes_first_then_the_npc_with_the_most_data()
    {
        Assert.Equal(PillarsCopy, NpcPick.Best([FoundationCopy, PillarsCopy], playerTerritory: 419, playerArea: Ishgard));
        Assert.Equal(PillarsCopy, NpcPick.Best([FoundationCopy, PillarsCopy], playerTerritory: 1, playerArea: 0));
    }

    [Fact]
    public void An_npc_with_no_data_counts_as_a_quest_copy() =>
        Assert.True(NpcPick.OnlyQuests(new NpcCandidate(1, 1, 0, [])));

    [Fact]
    public void Of_two_npcs_that_both_stand_there_for_good_the_one_in_the_players_area_wins()
    {
        // The two House Fortemps guards: the door into Fortemps Manor in the Pillars, and one in Coerthas with more data.
        var pillars = new NpcCandidate(1011217, 419, Ishgard, [0x20078, 0x1A0011]);
        var coerthas = new NpcCandidate(1009973, 155, Coerthas, [0x1A0002, 0x1A000F, 0x2005E]);
        Assert.Equal(pillars, NpcPick.Best([coerthas, pillars], playerTerritory: 433, playerArea: Ishgard));
        Assert.Equal(coerthas, NpcPick.Best([coerthas, pillars], playerTerritory: 1, playerArea: 0));
    }
}
