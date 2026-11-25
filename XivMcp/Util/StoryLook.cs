using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace XivMcp.Util;

/// <summary>What the game shows of a story the player is in: a conversation, a cutscene, or the quest windows that close one. Framework thread.</summary>
internal static class StoryLook
{
    private static readonly string[] StoryWindows = ["Talk", "JournalAccept", "JournalResult"];

    private static unsafe bool Cutscene()
    {
        var c = Svc.Condition;
        // The idle camera sets WatchingCutscene too, and is no story.
        return c[ConditionFlag.OccupiedInCutSceneEvent] || ((c[ConditionFlag.WatchingCutscene] || c[ConditionFlag.WatchingCutscene78]) && !GameMain.IsInIdleCam());
    }

    /// <summary>
    /// The player is in a conversation or cutscene: letting go of TextAdvance or starting Questionable now would act in it. Any event
    /// the game holds the character in counts, a shop too: between two parts of a story that is all there is to see.
    /// </summary>
    public static bool InStory() => InConversation() || Svc.Condition[ConditionFlag.OccupiedInEvent];

    /// <summary>
    /// The player is in a conversation or cutscene, as it shows: a cutscene, a quest event, talk, a quest window, or a choice in an
    /// event. Not a shop or the retainer list, which XIV MCP may close before a story quest begins.
    /// </summary>
    public static bool InConversation()
    {
        var c = Svc.Condition;
        return Cutscene() || c[ConditionFlag.OccupiedInQuestEvent] || c[ConditionFlag.Occupied33] || System.Array.Exists(StoryWindows, RetainerUi.Ready) ||
               (c[ConditionFlag.OccupiedInEvent] && System.Array.Exists(XivMcp.Quests.PlayerStory.ChoiceWindows, RetainerUi.Ready));
    }

    /// <summary>The game holds the character in an event with no window and no cutscene left (it does not end by waiting).</summary>
    public static bool HeldWithoutWindow() => Recovery.OrphanedEvent && !Cutscene() && !System.Array.Exists(StoryWindows, RetainerUi.Ready);
}
