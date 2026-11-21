using System;
using System.Collections.Generic;

namespace XivMcp.Farming;

/// <summary>
/// Monsters the farm could not walk up to (behind trees or a cliff, out of line of sight): left out of the "nearest monster"
/// choice for a few minutes, so the farm turns to another one instead of running into the obstacle forever.
/// </summary>
public sealed class UnreachableMobs
{
    public static readonly TimeSpan For = TimeSpan.FromMinutes(5);

    /// <summary>How long the walk towards a monster may make no progress before it is given up.</summary>
    public static readonly TimeSpan StallWindow = TimeSpan.FromSeconds(8);

    private readonly Dictionary<ulong, DateTime> until = new();

    public void Skip(ulong id, DateTime now) => until[id] = now + For;

    public bool IsSkipped(ulong id, DateTime now) => until.TryGetValue(id, out var t) && now < t;
}
