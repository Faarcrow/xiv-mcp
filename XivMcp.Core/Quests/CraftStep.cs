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

    /// <summary>Questionable's shop step; it can hang in the open shop when the bags already hold the item.</summary>
    public const string PurchaseItem = "PurchaseItem";

    /// <summary>Whether XIV MCP checks this kind of step against the bags.</summary>
    public static bool Covers(string? interactionType) =>
        string.Equals(interactionType, Craft, StringComparison.Ordinal) || string.Equals(interactionType, PurchaseItem, StringComparison.Ordinal);

    /// <param name="quality">The step's ItemQuality: "NQ", "HQ" or "Any" (null = Any).</param>
    public static bool IsDone(string? interactionType, int? itemCount, int nqOwned, int hqOwned, string? quality)
    {
        if (!Covers(interactionType) || itemCount is not > 0) return false;
        var owned = quality switch
        {
            "NQ" => nqOwned,
            "HQ" => hqOwned,
            _ => nqOwned + hqOwned,
        };
        return owned >= itemCount;
    }
}
