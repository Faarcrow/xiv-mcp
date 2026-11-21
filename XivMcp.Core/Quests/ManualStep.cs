using System;
using System.Text.RegularExpressions;

namespace XivMcp.Quests;

/// <summary>
/// Questionable's steps it leaves to the player ("WaitForManualProgress"): it waits for a click on its Skip button even when the
/// thing is already done. The ones XIV MCP can check for itself are moved on without the player.
/// </summary>
public static class ManualStep
{
    public const string WaitForManualProgress = "WaitForManualProgress";

    private static readonly Regex MeldOn = new(@"\bmeld\b.*\bmateria\b.*\bon\s+(?:an?\s+|the\s+)?(?<item>.+?)\s*[.!]?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The gear a "meld materia on X" step names, or null when the step asks for something else.</summary>
    public static string? MeldItem(string? interactionType, string? comment)
    {
        if (!string.Equals(interactionType, WaitForManualProgress, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(comment)) return null;
        var m = MeldOn.Match(comment.Trim());
        return m.Success ? m.Groups["item"].Value.Trim() : null;
    }
}
