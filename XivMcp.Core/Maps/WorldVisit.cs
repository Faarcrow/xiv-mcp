using System;

namespace XivMcp.Maps;

public enum WorldVisitAction { Wait, Arrived, Retry, GiveUp }

/// <param name="LoggedIn">Logged in with a character (false in the lobby or a data center transfer).</param>
/// <param name="OnTarget">The current world is the target.</param>
/// <param name="Busy">The travel plugin reports it is still working.</param>
/// <param name="Loading">A zone change or world transfer is in progress.</param>
/// <param name="SinceCommand">Since the travel command was (last) sent.</param>
/// <param name="Attempts">How many times the command was sent.</param>
public readonly record struct WorldVisitState(bool LoggedIn, bool OnTarget, bool Busy, bool Loading, TimeSpan SinceCommand, int Attempts);

/// <summary>
/// Watching an automatic world visit: the travel plugin sometimes gives up without travelling (e.g. with the aetheryte's
/// menu left open), and then nothing happens until the timeout. When it has been idle for a while off the target world,
/// the command is sent again, a few times.
/// </summary>
public static class WorldVisit
{
    public const int MaxAttempts = 3;
    public static readonly TimeSpan IdleBeforeRetry = TimeSpan.FromSeconds(20);

    public static WorldVisitAction Next(WorldVisitState s)
    {
        if (s.Busy || s.Loading || !s.LoggedIn) return WorldVisitAction.Wait;
        if (s.OnTarget) return WorldVisitAction.Arrived;
        if (s.SinceCommand < IdleBeforeRetry) return WorldVisitAction.Wait;
        return s.Attempts >= MaxAttempts ? WorldVisitAction.GiveUp : WorldVisitAction.Retry;
    }
}
