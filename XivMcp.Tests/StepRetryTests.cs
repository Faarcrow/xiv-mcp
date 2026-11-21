using XivMcp.Jobs;

namespace XivMcp.Tests;

public class StepRetryTests
{
    [Theory]
    [InlineData("Can't change the inventory right now (character state: BetweenAreas).")]
    [InlineData("The market board did not send the listings (it may be busy; try again in a moment).")]
    [InlineData("No character is currently logged in.")]
    [InlineData("The market board did not open.")]
    [InlineData("recovery: Not free: The character is loading a zone, which recovery doesn't interrupt.")]
    [InlineData("Can't change gear right now (crafting, gathering, casting, in a cutscene or a zone change).")]
    [InlineData("recovery: Not free: The character is casting, which recovery doesn't interrupt.")]
    [InlineData("Can't meld right now (busy: crafting, gathering, talking or in a cutscene).")]
    [InlineData("Questionable stopped on its own while on True as Steel. Its window says why (a duty, a level, a step it cannot do).")]
    public void Passing_game_states_are_retried(string error) => Assert.True(StepRetry.ShouldRetry(error, attempts: 1));

    [Theory]
    [InlineData("The cheapest Snurble Tufts costs 1.740 gil each, above your limit of 210.")]
    [InlineData("No Red Balloon in your bags. Buy some with buy_item.")]
    [InlineData("Questionable made no progress on Throw Some Rings on It after 3 restarts.")]
    public void Real_problems_wait_for_the_agent(string error) => Assert.False(StepRetry.ShouldRetry(error, attempts: 1));

    [Fact]
    public void Retries_are_limited() =>
        Assert.False(StepRetry.ShouldRetry("Can't change the inventory right now (character state: BetweenAreas).", attempts: 1 + StepRetry.MaxAutoRetries));
}
