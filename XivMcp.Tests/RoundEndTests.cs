using XivMcp.Quests;

namespace XivMcp.Tests;

public class RoundEndTests
{
    private static readonly QuestInfo Daily = new(69440, "Quality Parts, Guaranteed") { Repeatable = true };
    private static readonly QuestInfo Daily2 = new(69441, "The New Batch") { Repeatable = true };
    private static readonly QuestInfo RankUp = new(69436, "Chief Concerns");

    [Fact]
    public void Armed_on_the_last_quest_while_Questionable_is_on_it()
    {
        Assert.True(RoundEnd.Arm([RankUp], RankUp.QuestionableId, lastAccepted: true));
        Assert.True(RoundEnd.Arm([Daily], Daily.QuestionableId, lastAccepted: true));
    }

    [Fact]
    public void Not_while_more_than_one_quest_is_left_or_Questionable_is_on_another()
    {
        Assert.False(RoundEnd.Arm([Daily, Daily2], Daily.QuestionableId, lastAccepted: true));
        Assert.False(RoundEnd.Arm([Daily], RankUp.QuestionableId, lastAccepted: true));
        Assert.False(RoundEnd.Arm([Daily], null, lastAccepted: true));
        Assert.False(RoundEnd.Arm([], Daily.QuestionableId, lastAccepted: true));
    }

    [Fact]
    public void A_daily_only_once_accepted_as_one_done_on_an_earlier_day_counts_as_complete()
    {
        Assert.False(RoundEnd.Arm([Daily], Daily.QuestionableId, lastAccepted: false));
        // A story quest is never complete before it is done, so it may be armed before it is accepted.
        Assert.True(RoundEnd.Arm([RankUp], RankUp.QuestionableId, lastAccepted: false));
    }

    [Fact]
    public void Quests_Questionable_has_no_path_for_never_hold_the_round_open()
    {
        var noPath = new HashSet<uint> { Daily2.Id };
        var left = RoundEnd.Left([Daily, Daily2], _ => false, noPath);
        Assert.Equal([Daily], left);
        Assert.True(RoundEnd.Arm(left, Daily.QuestionableId, lastAccepted: true));
        Assert.Empty(RoundEnd.Left([Daily, Daily2], q => q == Daily, noPath));
    }
}
