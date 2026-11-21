using System;

namespace XivMcp.Jobs;

/// <summary>
/// Which failed job steps are worth running again by themselves: failures that only mean the game was briefly busy (a zone
/// still loading after travel, the market board not answering, a short logout during world travel, a cast such as a teleport
/// still running when the next step wants to change gear). Everything else stops the
/// job for the agent, like before.
/// </summary>
public static class StepRetry
{
    /// <summary>Automatic retries per step (on top of the first attempt).</summary>
    public const int MaxAutoRetries = 2;

    /// <summary>How long to wait before an automatic retry.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(10);

    private static readonly string[] PassingStates =
    [
        "BetweenAreas",
        "loading a zone",
        "did not send the listings",
        "No character is currently logged in",
        "Can't change gear right now",
        "The character is casting",
        "Artisan's solver hung",
    ];

    /// <param name="attempts">How many times the step ran so far, the failed run included.</param>
    public static bool ShouldRetry(string error, int attempts)
    {
        if (attempts > MaxAutoRetries) return false;
        foreach (var s in PassingStates)
            if (error.Contains(s, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
