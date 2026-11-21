using XivMcp.Jobs;

namespace XivMcp.Tests;

public class StepRetryTests
{
    [Theory]
    [InlineData("Can't change the inventory right now (character state: BetweenAreas).")]
    [InlineData("The market board did not send the listings (it may be busy; try again in a moment).")]
    [InlineData("No character is currently logged in.")]
    [InlineData("recovery: Not free: The character is loading a zone, which recovery doesn't interrupt.")]
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
