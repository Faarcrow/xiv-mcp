using XivMcp.Mcp;
using XivMcp.Permissions;

namespace XivMcp.Tests;

/// <summary>Questions XIV MCP's own tools ask the player during a call, and their answers for this session.</summary>
public class InCallQuestionTests
{
    private sealed class FakeGate : IApprovalGate
    {
        public Task<ApprovalDecision> RequestAsync(ApprovalRequest request, CancellationToken ct) => Task.FromResult(ApprovalDecision.Denied);
    }

    private sealed class FakeProbe : IGameProbe
    {
        public GameSnapshot? Capture() => null;
    }

    private sealed class FakeNotifier : ISecurityNotifier
    {
        public void Flagged(ToolProvider provider, string tool, IReadOnlyList<SideEffect> effects, bool suspended) { }
    }

    private static readonly ToolProvider Plugin = new("HelloMcp", "Hello MCP", ProviderTrust.ThirdParty);
    private static readonly Caller Assistant = Caller.Assistant("claude-code");
    private static readonly Caller PluginCaller = Caller.ForPlugin(Plugin);

    private readonly AuditLog audit = new(100);
    private readonly ToolGate gate;

    /// <summary>What the player answers, and what each question offered (true: "Approve for this session").</summary>
    private ApprovalDecision answer = ApprovalDecision.ApprovedForSession;
    private readonly List<bool> asked = [];

    public InCallQuestionTests() =>
        gate = new ToolGate(new InMemoryPolicyStore(), new FakeGate(), new FakeProbe(), audit, new FakeNotifier(),
                            () => new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc));

    private Task<bool> Ask(Caller? caller, params string[] scopes) =>
        gate.AskDuringCallAsync("sell_to_vendor", scopes, "Sell Wind-up Cid?", caller, offerSession =>
        {
            asked.Add(offerSession);
            return Task.FromResult(answer);
        });

    [Fact]
    public async Task The_assistants_answer_for_the_session_covers_the_same_question_later()
    {
        Assert.True(await Ask(Assistant, "untradable:100"));
        Assert.True(await Ask(Assistant, "untradable:100"));
        Assert.Equal([true], asked);
        Assert.Equal("approved_session", audit.Recent().First().Decision);
    }

    [Fact]
    public async Task A_call_without_a_caller_is_the_assistant()
    {
        Assert.True(await Ask(null, "untradable:100"));
        Assert.True(await Ask(Assistant, "untradable:100"));
        Assert.Equal([true], asked);
    }

    [Fact]
    public async Task It_covers_only_what_was_asked_about()
    {
        await Ask(Assistant, "untradable:100");
        Assert.True(await Ask(Assistant, "untradable:100", "untradable:200"));
        Assert.Equal(2, asked.Count);
        Assert.True(await Ask(Assistant, "untradable:200"));
        Assert.Equal(2, asked.Count);
    }

    [Fact]
    public async Task Plugins_are_never_offered_the_session_and_are_asked_every_time()
    {
        Assert.True(await Ask(PluginCaller, "untradable:100"));
        Assert.True(await Ask(PluginCaller, "untradable:100"));
        Assert.Equal([false, false], asked);
        Assert.Equal(0, gate.Sessions.Count(Plugin.Id));
    }

    [Fact]
    public async Task The_assistants_answer_never_covers_a_plugin()
    {
        await Ask(Assistant, "untradable:100");
        await Ask(PluginCaller, "untradable:100");
        Assert.Equal([true, false], asked);
    }

    [Fact]
    public async Task Once_is_once_and_a_refusal_is_no()
    {
        answer = ApprovalDecision.ApprovedOnce;
        Assert.True(await Ask(Assistant, "untradable:100"));
        answer = ApprovalDecision.Denied;
        Assert.False(await Ask(Assistant, "untradable:100"));
        Assert.Equal(2, asked.Count);
        Assert.Equal(["denied", "approved"], audit.Recent().Select(e => e.Decision));
    }

    [Fact]
    public async Task Each_question_is_audited_with_who_asked()
    {
        await Ask(PluginCaller, "untradable:100");
        var entry = audit.Recent().Single();
        Assert.Equal(("approval", "sell_to_vendor", "plugin:HelloMcp", "Sell Wind-up Cid?"), (entry.Kind, entry.Tool, entry.Caller, entry.Summary));
        Assert.True(entry.BuiltIn);
        Assert.Equal(["untradable:100"], entry.Capabilities);
    }

    [Fact]
    public async Task Clearing_the_assistants_session_asks_again()
    {
        await Ask(Assistant, "untradable:100");
        gate.Sessions.Clear(ToolProvider.Core.Id);
        await Ask(Assistant, "untradable:100");
        Assert.Equal(2, asked.Count);
    }
}
