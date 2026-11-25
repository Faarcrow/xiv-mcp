using System;
using System.Threading.Tasks;

namespace XivMcp.Util;

/// <summary>
/// TextAdvance's "external control", held while the player watches a story quest: one plugin at a time tells TextAdvance which of its
/// features to use, in memory only (the player's TextAdvance settings are never changed), until it lets go. Questionable takes it
/// whenever it runs and TextAdvance is free, and leaves it alone while another plugin holds it. Framework thread.
/// </summary>
internal static class TextAdvanceControl
{
    private const string PluginId = "TextAdvance";

    /// <summary>XIV MCP holds TextAdvance's external control.</summary>
    public static bool Held { get; private set; }

    // Whether the hold has TextAdvance accept quest offers (off while the player chooses between quests).
    private static bool accepting;

    // Counts the takes, so a release waiting for the player does not let go of a later take.
    private static int generation;

    public static bool Loaded => PluginCompat.IsLoaded(PluginId);

    /// <summary>Another plugin holds TextAdvance's external control (Questionable during its runs). False while TextAdvance is not ready.</summary>
    public static bool HeldByOther()
    {
        if (Held || !Loaded) return false;
        try { return Svc.PluginInterface.GetIpcSubscriber<bool>("TextAdvance.IsInExternalControl").InvokeFunc(); }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Takes TextAdvance's external control for a quest the player watches: what Questionable asks for with its "but don't skip
    /// cutscenes or dialogue" (no talk skip, no cutscene skip), and quest rewards left to the player too; quests are still completed
    /// and items handed in, and quest offers accepted unless <paramref name="acceptQuests"/> is false (the player chooses between
    /// quests). Taking it again while XIV MCP holds it keeps it with those settings (and a release waiting for the player is dropped).
    /// False when another plugin holds it or TextAdvance is not ready. True when TextAdvance is not loaded: nothing skips then.
    /// </summary>
    public static bool Take(bool acceptQuests = true)
    {
        if (!Loaded) return true;
        try
        {
            if (!Svc.PluginInterface.GetIpcSubscriber<string, ExternalTerritoryConfig, bool>("TextAdvance.EnableExternalControl")
                    .InvokeFunc(Svc.PluginInterface.InternalName, new ExternalTerritoryConfig { EnableQuestAccept = acceptQuests })) return false;
        }
        catch (Exception e)
        {
            Svc.Log.Warning($"[MCP] Could not take over TextAdvance: {e.Message}");
            return false;
        }
        if (!Held || accepting != acceptQuests)
            Svc.Log.Information("[MCP] Holding TextAdvance for a story quest: it skips no talk, cutscene or reward" +
                                (acceptQuests ? "." : ", and leaves quest offers to you."));
        Held = true;
        accepting = acceptQuests;
        generation++;
        return true;
    }

    /// <summary>Lets go of TextAdvance's external control if XIV MCP holds it (TextAdvance refuses it for another plugin's hold).</summary>
    public static void Release()
    {
        try
        {
            if (Loaded) Svc.PluginInterface.GetIpcSubscriber<string, bool>("TextAdvance.DisableExternalControl").InvokeFunc(Svc.PluginInterface.InternalName);
            if (Held) Svc.Log.Information("[MCP] Gave TextAdvance back.");
        }
        catch (Exception e)
        {
            Svc.Log.Warning($"[MCP] Could not give TextAdvance back: {e.Message}");
        }
        Held = false;
    }

    /// <summary>
    /// Lets go of TextAdvance once the player is out of the conversation or cutscene they are in, however long that takes, so the
    /// player's own TextAdvance settings do not skip the rest of it. Not when XIV MCP took it again meanwhile, or let go already (also
    /// as XIV MCP unloads).
    /// </summary>
    public static void ReleaseWhenFree()
    {
        if (!Held) return;
        var mine = generation;
        _ = Task.Run(async () =>
        {
            try
            {
                var free = 0;
                while (free < 2)
                {
                    var (stillMine, inStory) = await Game.Run(() => (Held && generation == mine, StoryLook.InStory())).ConfigureAwait(false);
                    if (!stillMine) return;
                    free = inStory ? 0 : free + 1;
                    if (free < 2) await Task.Delay(1000).ConfigureAwait(false);
                }
                await Game.Run(() => { if (generation == mine && Held) Release(); return true; }).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Svc.Log.Warning($"[MCP] Waiting to give TextAdvance back failed ({e.Message}); it is given back when XIV MCP unloads.");
            }
        });
    }

    /// <summary>TextAdvance's IPC type, matched by its field names (null keeps the player's setting).</summary>
    private sealed class ExternalTerritoryConfig
    {
        public bool? EnableQuestAccept = true;
        public bool? EnableQuestComplete = true;
        public bool? EnableRewardPick = false;
        public bool? EnableRequestHandin = true;
        public bool? EnableCutsceneEsc = false;
        public bool? EnableCutsceneSkipConfirm = false;
        public bool? EnableTalkSkip = false;
        public bool? EnableRequestFill = true;
        public bool? EnableAutoInteract = false;
    }
}
