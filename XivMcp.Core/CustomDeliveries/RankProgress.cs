using System;

namespace XivMcp.CustomDeliveries;

/// <summary>
/// A client's way to its next rank: each delivery adds the satisfaction of its request's reward row (game data: 30 at rank 1, 80 at
/// rank 2, 130 at rank 3, 180 at rank 4, none at the top rank 5, the same for every request and with or without the bonus), and the
/// rank goes up once the satisfaction reaches what the client's rank needs (90, 480, 780, 1080).
/// </summary>
public static class RankProgress
{
    /// <summary>The deliveries still needed for the next rank, or null at the top rank (nothing needed) or when a delivery adds none.</summary>
    public static int? DeliveriesToRankUp(int satisfaction, int needed, int perDelivery) =>
        needed <= 0 || perDelivery <= 0 ? null : Math.Max(0, (needed - satisfaction + perDelivery - 1) / perDelivery);

    /// <summary>Which of <paramref name="count"/> deliveries ranks the client up (1: the first), or null when none of them does.</summary>
    public static int? RankUpAt(int satisfaction, int needed, int perDelivery, int count) =>
        DeliveriesToRankUp(satisfaction, needed, perDelivery) is { } k && k >= 1 && k <= count ? k : null;

    /// <summary>
    /// What a request still takes, as Satisfier counts it (NPCInfo.RemainingTurnins): the client's deliveries left this week, fewer
    /// when the next rank comes first.
    /// </summary>
    public static int StillTakes(int deliveriesLeft, int satisfaction, int needed, int perDelivery) =>
        Math.Max(0, needed > satisfaction && perDelivery > 0 ? Math.Min(deliveriesLeft, (needed - satisfaction + perDelivery - 1) / perDelivery) : deliveriesLeft);
}
