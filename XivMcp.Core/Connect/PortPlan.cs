using System;
using System.Collections.Generic;
using System.Linq;

namespace XivMcp.Connect;

/// <summary>
/// Which port the MCP server listens on when several game clients run on one PC: each game account keeps a port of its own (a client
/// stays on one account while it switches characters), so an MCP app set up for an account always reaches the client playing it.
/// The first account gets the configured port; before the account is known (title screen), the lowest free port is used.
/// </summary>
public static class PortPlan
{
    private const int MaxTries = 100;

    /// <param name="assigned">Account key → port, for every account seen on this PC.</param>
    /// <param name="isFree">Whether nothing else listens on a port.</param>
    public static int ForAccount(string account, IReadOnlyDictionary<string, int> assigned, int basePort, Func<int, bool> isFree)
    {
        if (assigned.TryGetValue(account, out var known)) return known;
        var taken = assigned.Values.ToHashSet();
        for (var port = basePort; port < basePort + MaxTries; port++)
            if (!taken.Contains(port) && isFree(port)) return port;
        return basePort;
    }

    public static int BeforeLogin(int basePort, Func<int, bool> isFree)
    {
        for (var port = basePort; port < basePort + MaxTries; port++)
            if (isFree(port)) return port;
        return basePort;
    }

    /// <summary>Two account keys turned out to be the same account: one port stays (the surviving key's, else the old one's).</summary>
    public static void Rename(IDictionary<string, int> assigned, string from, string to)
    {
        if (!assigned.Remove(from, out var port)) return;
        assigned.TryAdd(to, port);
    }
}

/// <summary>
/// A list several game clients keep in one file (each client's own entries plus the others'): on saving, a client writes its own
/// entries from memory and takes everyone else's from the file as it is now, so neither overwrites the other's changes.
/// </summary>
public static class SharedList
{
    public static List<T> Merge<T>(IEnumerable<T> onDisk, IEnumerable<T> inMemory, Func<T, string> id, Func<T, bool> isMine)
    {
        var mine = inMemory.Where(isMine).ToList();
        var mineIds = mine.Select(id).ToHashSet();
        return onDisk.Where(d => !isMine(d) && !mineIds.Contains(id(d))).Concat(mine).ToList();
    }
}
