using System;

namespace XivMcp.Quests;

/// <summary>
/// Questionable's "Craft" steps first ask Artisan to run its list for the whole quest and, when that starts, craft every turn-in
/// (intermediates included) without checking what the bags already hold. That re-crafts items already made and uses up materials
/// meant for the rest. A step whose items are already owned is done.
/// </summary>
public static class CraftStep
{
    public const string Craft = "Craft";

    /// <param name="quality">The step's ItemQuality: "NQ", "HQ" or "Any" (null = Any).</param>
    public static bool IsDone(string? interactionType, int? itemCount, int nqOwned, int hqOwned, string? quality)
    {
        if (!string.Equals(interactionType, Craft, StringComparison.Ordinal) || itemCount is not > 0) return false;
        var owned = quality switch
        {
            "NQ" => nqOwned,
            "HQ" => hqOwned,
            _ => nqOwned + hqOwned,
        };
        return owned >= itemCount;
    }
}
