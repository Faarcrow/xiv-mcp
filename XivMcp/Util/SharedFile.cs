using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using XivMcp.Connect;

namespace XivMcp.Util;

/// <summary>
/// Read-modify-write of a file in pluginConfigs/XivMcp that several game clients on one PC share (they run the same plugin install):
/// a named mutex per file keeps two clients from writing it at once.
/// </summary>
internal static class SharedFile
{
    public static void Locked(string path, Action body) => Locked(path, () => { body(); return 0; });

    public static T Locked<T>(string path, Func<T> body)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToLowerInvariant())))[..24];
        using var mutex = new Mutex(false, $@"Local\XivMcp.{hash}");
        var owned = false;
        try
        {
            try { owned = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { owned = true; } // the other client died while holding it: the file is ours now
            return body();
        }
        finally
        {
            if (owned) mutex.ReleaseMutex();
        }
    }
}

/// <summary>
/// The MCP port of each game account seen on this PC (pluginConfigs/XivMcp/ports.json), so every client gets its own and an account
/// keeps its port across restarts (see <see cref="PortPlan"/>).
/// </summary>
internal static class PortStore
{
    private static string File => Path.Combine(Svc.PluginInterface.GetPluginConfigDirectory(), "ports.json");

    public static int ForAccount(string account, int basePort, Func<int, bool> isFree) =>
        SharedFile.Locked(File, () =>
        {
            var map = Read();
            var port = PortPlan.ForAccount(account, map, basePort, isFree);
            if (!map.TryGetValue(account, out var known) || known != port) { map[account] = port; Write(map); }
            return port;
        });

    public static void Rename(string from, string to) =>
        SharedFile.Locked(File, () =>
        {
            var map = Read();
            PortPlan.Rename(map, from, to);
            Write(map);
        });

    /// <summary>The configured first port changed: assign ports afresh from it.</summary>
    public static void Reset() => SharedFile.Locked(File, () => { try { System.IO.File.Delete(File); } catch { /* none */ } });

    public static IReadOnlyDictionary<string, int> All() => SharedFile.Locked(File, Read);

    private static Dictionary<string, int> Read()
    {
        try { return System.IO.File.Exists(File) ? JsonSerializer.Deserialize<Dictionary<string, int>>(System.IO.File.ReadAllText(File)) ?? [] : []; }
        catch { return []; }
    }

    private static void Write(Dictionary<string, int> map)
    {
        try { System.IO.File.WriteAllText(File, JsonSerializer.Serialize(map)); }
        catch (Exception ex) { Svc.Log.Warning($"[MCP] Could not save ports.json: {ex.Message}"); }
    }

    /// <summary>Whether nothing listens on the port yet (tries to take it for a moment).</summary>
    public static bool IsFree(int port)
    {
        var probe = new HttpListener();
        try
        {
            probe.Prefixes.Add($"http://localhost:{port}/");
            probe.Start();
            return true;
        }
        catch { return false; }
        finally
        {
            try { probe.Close(); } catch { /* not started */ }
        }
    }
}
