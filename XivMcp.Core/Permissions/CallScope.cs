using System.Threading;

namespace XivMcp.Permissions;

/// <summary>
/// Who the tool call running right now is for, for helpers deep inside it that ask the player (e.g. recovery closing a shop whose
/// buyback holds sold items): their question must name that caller and use only that caller's session approvals. Set by
/// <see cref="ToolGate.InvokeAsync"/> for the duration of the call; null outside a call.
/// </summary>
public static class CallScope
{
    private static readonly AsyncLocal<Caller?> current = new();

    public static Caller? Current
    {
        get => current.Value;
        internal set => current.Value = value;
    }
}
