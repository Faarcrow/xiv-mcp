using XivMcp.CustomDeliveries;
using XivMcp.Quests;

namespace XivMcp.Tests;

public class PlayerStoryTests
{
    private static readonly QuestInfo Daily1 = new(69438, "Lakeland Neighbors") { Repeatable = true };
    private static readonly QuestInfo Daily2 = new(69439, "Clean Beard, Clear Mind") { Repeatable = true };
    private static readonly QuestInfo RankUp = new(69436, "Chief Concerns");
    // The Qitari's third stela: each quest shuts the other out once done (the Quest sheet's QuestLock).
    private static readonly QuestInfo Famine = new(69340, "The Third Stela: A Famine for Fishmen") { Locks = [69341] };
    private static readonly QuestInfo Feast = new(69341, "The Third Stela: A Feast for Fishmen") { Locks = [69340] };
    private static readonly ChoiceSeen StoryChoice = new(InConversation: true, QuestBattle: false, Travelling: false, SoloDutyStart: false, KnownPrompt: false);

    [Fact]
    public void Only_the_quests_that_are_not_dailies_are_watched_and_only_when_asked()
    {
        Assert.True(PlayerStory.Watched(RankUp, playerRanksUp: true));
        Assert.False(PlayerStory.Watched(Daily1, playerRanksUp: true));
        Assert.False(PlayerStory.Watched(RankUp, playerRanksUp: false));
    }

    [Fact]
    public void A_watched_quest_has_a_round_of_its_own_and_other_rounds_leave_it_out()
    {
        bool Watched(QuestInfo q) => PlayerStory.Watched(q, true);
        Assert.Equal([RankUp], PlayerStory.Round([RankUp, Daily1, Daily2], RankUp, Watched));
        Assert.Equal([Daily1, Daily2], PlayerStory.Round([Daily1, RankUp, Daily2], Daily1, Watched));
        Assert.Equal([Daily1, RankUp], PlayerStory.Round([Daily1, RankUp], Daily1, _ => false));
    }

    [Fact]
    public void A_choice_in_the_quest_conversation_is_the_players()
    {
        // Chief Concerns, 2026-11-25: a CutSceneSelectString in a cutscene.
        Assert.True(PlayerStory.PlayersChoice(StoryChoice));
    }

    [Fact]
    public void Choices_Questionable_answers_anyway_or_that_are_no_story_stay_its_own()
    {
        Assert.False(PlayerStory.PlayersChoice(StoryChoice with { InConversation = false })); // Return, a ticket while teleporting
        Assert.False(PlayerStory.PlayersChoice(StoryChoice with { QuestBattle = true }));     // it answers there even while stopped
        Assert.False(PlayerStory.PlayersChoice(StoryChoice with { Travelling = true }));      // an aetheryte, Lifestream
        Assert.False(PlayerStory.PlayersChoice(StoryChoice with { SoloDutyStart = true }));   // it fights the solo duty
        Assert.False(PlayerStory.PlayersChoice(StoryChoice with { KnownPrompt = true }));     // a high-quality turn-in, a purchase
    }

    [Fact]
    public void The_job_names_the_kind_of_choice()
    {
        Assert.Equal("a Yes or No question", PlayerStory.Describe("SelectYesno"));
        Assert.Equal("a dialogue choice", PlayerStory.Describe("CutSceneSelectString"));
        Assert.Equal("the quest offer", PlayerStory.Describe(PlayerStory.OfferWindow));
    }

    [Fact]
    public void Quests_that_shut_each_other_out_are_a_choice_and_others_stand_alone()
    {
        Assert.Equal([Famine, Feast], PlayerStory.Choice(Famine, [Famine, Feast, RankUp]));
        Assert.Equal([Feast, Famine], PlayerStory.Choice(Feast, [Famine, Feast]));
        // One way is enough: a quest sheet that lists the lock on one side only.
        var oneSided = Famine with { Locks = [] };
        Assert.Equal([oneSided, Feast], PlayerStory.Choice(oneSided, [Feast]));
        Assert.Equal([RankUp], PlayerStory.Choice(RankUp, [Famine, Feast]));
    }

    [Fact]
    public void A_quest_the_player_did_not_take_hands_over_to_the_next_round_and_round()
    {
        IReadOnlyList<QuestInfo> choice = [Famine, Feast];
        Assert.Equal(Famine, PlayerStory.Offer(choice, declined: null));
        Assert.Equal(Feast, PlayerStory.Offer(choice, Famine.Id));
        Assert.Equal(Famine, PlayerStory.Offer(choice, Feast.Id));
        // A quest no longer in the choice (taken by hand in between, say): from the first again.
        Assert.Equal(Famine, PlayerStory.Offer(choice, RankUp.Id));
        Assert.Equal(RankUp, PlayerStory.Offer([RankUp], RankUp.Id));
    }

    [Fact]
    public void The_story_is_waited_out_however_long_the_player_takes()
    {
        var since = new DateTime(2026, 11, 25, 1, 5, 0, DateTimeKind.Utc);
        var wait = new StoryWait();
        Assert.Equal(StoryEnd.Wait, wait.Poll(since.AddHours(3), inConversation: true, heldWithoutWindow: false));
        Assert.Equal(StoryEnd.Wait, wait.Poll(since.AddHours(3).AddSeconds(2), inConversation: false, heldWithoutWindow: false));
        Assert.Equal(StoryEnd.Over, wait.Poll(since.AddHours(3).AddSeconds(4), inConversation: false, heldWithoutWindow: false));
    }
}
