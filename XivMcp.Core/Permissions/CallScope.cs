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

    /// <summary>
    /// Who a job started inside the call running now belongs to: the assistant's client ("opencode 2.3.0"), or "plugin:&lt;id&gt;"
    /// for a plugin's call, as the plugin API records the plugin's own jobs, so their steps stay held to that plugin's permissions.
    /// <paramref name="fallback"/> (the client that connected last) when the call names no client, or outside a call.
    /// </summary>
    public static string? JobClient(string? fallback) => Current switch
    {
        { IsPlugin: true } plugin => plugin.Id,
        { Client: { } client } => client,
        _ => fallback,
    };
}
