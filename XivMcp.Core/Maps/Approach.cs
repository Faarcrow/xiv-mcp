using System;
using System.Collections.Generic;
using System.Numerics;

namespace XivMcp.Maps;

/// <summary>
/// Whether a walk towards a target still makes progress: a character running into a wall (or sliding along it) keeps moving a
/// little, but stops getting closer. The walk has stalled once the distance hasn't dropped by <see cref="MinGain"/> yalms for
/// the given window. Feed it the distance left along the path (<see cref="PathLength"/>) where known: a path around a wall leads
/// away from the target for a while.
/// </summary>
public sealed class Approach(TimeSpan window)
{
    /// <summary>The length of a path from a point through its waypoints.</summary>
    public static float PathLength(Vector3 from, IReadOnlyList<Vector3> waypoints)
    {
        var length = 0f;
        var at = from;
        foreach (var next in waypoints)
        {
            length += Vector3.Distance(at, next);
            at = next;
        }
        return length;
    }

    /// <summary>Yalms the distance must drop by to count as progress.</summary>
    public const float MinGain = 0.5f;

    private float best = float.MaxValue;
    private DateTime bestAt;

    /// <summary>Adds the current distance to the target; true when the walk has stalled.</summary>
    public bool Stalled(float distance, DateTime now)
    {
        if (best == float.MaxValue || distance < best - MinGain)
        {
            best = distance;
            bestAt = now;
            return false;
        }
        return now - bestAt >= window;
    }
}
