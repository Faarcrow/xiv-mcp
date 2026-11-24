using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace XivMcp.Util;

/// <summary>
/// The game's idle camera (/icam, or System Configuration's switch to it when AFK) sets WatchingCutscene like a cutscene, but it
/// is only a screen saver that any key press ends. Framework thread.
/// </summary>
internal static class IdleCamera
{
    /// <summary>Ends the idle camera the way a key press would. True if it was on.</summary>
    public static unsafe bool Leave()
    {
        if (!GameMain.IsInIdleCam()) return false;
        UIModule.Instance()->ExitIdleCam();
        return true;
    }
}
