using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using XivMcp.CustomDeliveries;

namespace XivMcp.Util;

/// <summary>
/// Satisfier (vsatisfy), which automates custom deliveries and offers no IPC: reflection into its window's client list (refreshed by
/// calling its own update, as drawing the window does) and into clib's automation, which runs its tasks (AutoCraft, AutoGather,
/// AutoFish for one client each). Framework thread.
/// </summary>
internal static class SatisfierBridge
{
    public const string PluginId = "vsatisfy";
    private static readonly ForeignPlugin Plugin = new(PluginId, "Satisfier");

    public static bool Loaded => Plugin.Loaded;

    /// <summary>One client as Satisfier sees it, with its own object for starting a task.</summary>
    public sealed record Client(object Info, int Index, string Name, bool Unlocked, int Rank, int Satisfaction, int SatisfactionMax,
                                int DeliveriesLeft, uint[] Items, bool[] Bonus, int[] Remaining, bool HasCraft, bool HasGather, bool HasFish,
                                bool SpearFish, uint FishSpot, uint[] Rewards)
    {
        /// <summary>
        /// The client for <see cref="DeliveryPlan"/>: its deliveries left this week, whether it still ranks up (the game shows a
        /// satisfaction bar only below the top rank), the capped scrips each request pays, the deliveries its achievements still
        /// need (<see cref="AchievementProgress.DeliveriesLeft"/>) and the crafting and gathering requests that cannot be made, with
        /// the items in the bags counted as Satisfier counts them. Framework thread (reads the currencies and the bags).
        /// </summary>
        public DeliveryNpc ForPlan(int? achievementLeft, RequestData levels) =>
            new(Name, Unlocked, Remaining, Enumerable.Range(0, 3).Where(i => Bonus[i]).Select(i => (DeliveryKind)i).ToHashSet())
            {
                Left = DeliveriesLeft,
                GainsRank = SatisfactionMax > 0,
                Rewards = [.. Enumerable.Range(0, 3).Select(i => (IReadOnlyList<CappedReward>)SatisfierBridge.Rewards(this, (DeliveryKind)i))],
                AchievementLeft = achievementLeft,
                Blocked = new[] { DeliveryKind.Craft, DeliveryKind.Gather }
                    .Where(k => levels.Jobs[(int)k]?.Blocks(InBags(levels, k), Remaining[(int)k]) == true).ToHashSet(),
                Satisfaction = Satisfaction,
                SatisfactionNeeded = SatisfactionMax,
                SatisfactionPerDelivery = levels.SatisfactionPerDelivery,
            };

        /// <summary>The request's items in the bags that Satisfier counts as delivered or made (<see cref="RequestData.MinCollectability"/>). Framework thread.</summary>
        public int InBags(RequestData levels, DeliveryKind kind) => Util.Items.CountInBags(Items[(int)kind], levels.MinCollectability[(int)kind]);
    }

    /// <summary>
    /// Per request (craft, gather, fish) of one client, from game data: the job that makes it with its level against the request's
    /// (null where it is not known: Satisfier's settings cannot be read, a spearfishing request), that job's name, the collectability
    /// from which Satisfier counts an item in the bags (AutoCraft and AutoFish count from 1, AutoGather from the request's lowest
    /// tier), and what one delivery adds to the client's satisfaction (its reward row's, at the highest tier, as Satisfier counts it;
    /// 0: not known).
    /// </summary>
    public sealed record RequestData(DeliveryJob?[] Jobs, string?[] JobNames, short[] MinCollectability, int[] SatisfactionPerDelivery)
    {
        public static RequestData Unknown => new(new DeliveryJob?[3], new string?[3], [1, 1, 1], [0, 0, 0]);
    }

    private static object Window => Plugin.Get(Plugin.Self, "_wndMain") ?? throw Plugin.Unsupported("Plugin._wndMain");

    /// <summary>Every client, refreshed from the game.</summary>
    public static List<Client> Clients()
    {
        var window = Window;
        Plugin.Call(window, "UpdateData");
        var list = (IEnumerable)(Plugin.Get(window, "_npcs") ?? throw Plugin.Unsupported("MainWindow._npcs"));
        var clients = new List<Client>();
        foreach (var npc in list)
        {
            var index = Int(npc, "Index");
            var used = Int(npc, "UsedDeliveries");
            var fish = Plugin.Get(npc, "FishData");
            int max;
            int[] remaining;
            // While a rank-up is left to the player, Satisfier's own count is lowered (LimitTurnIns): the real numbers come from the count
            // it had, counted as Satisfier counts them.
            if (LoweredFor(npc) is { } lowered)
            {
                max = lowered.Original;
                remaining = [.. lowered.PerDelivery.Select(per => RankProgress.StillTakes(Math.Max(0, max - used), Int(npc, "SatisfactionCur"), Int(npc, "SatisfactionMax"), per))];
            }
            else
            {
                max = Int(npc, "MaxDeliveries");
                remaining = [.. Enumerable.Range(0, 3).Select(i => Convert.ToInt32(Plugin.Call(npc, "RemainingTurnins", i)))];
            }
            var rewards = (uint[])(Plugin.Get(npc, "Rewards") ?? new uint[3]);
            clients.Add(new Client(npc, index, Plugin.Get(npc, "Name") as string ?? "?", IsUnlocked(index),
                Int(npc, "Rank"), Int(npc, "SatisfactionCur"), Int(npc, "SatisfactionMax"), Math.Max(0, max - used),
                (uint[])(Plugin.Get(npc, "TurnInItems") ?? new uint[3]), (bool[])(Plugin.Get(npc, "IsBonusEffective") ?? new bool[3]), remaining,
                Plugin.Get(npc, "CraftData") is not null, Plugin.Get(npc, "GatherData") is not null, fish is not null,
                fish is not null && Plugin.Get(fish, "IsSpearFish") is true,
                fish is null ? 0 : Convert.ToUInt32(Plugin.Get(fish, "FishSpotId")),
                rewards));
        }
        return clients;
    }

    /// <summary>
    /// Satisfier's job choice (its config), or null when this version of Satisfier keeps it elsewhere or has a choice XIV MCP does
    /// not know: then no level is checked. Framework thread.
    /// </summary>
    private static SatisfierJobSettings? JobSettings()
    {
        try
        {
            var config = Plugin.StaticValue("Satisfy.Plugin", "Config") ?? throw Plugin.Unsupported("Plugin.Config");
            var name = Plugin.Get(config, "CraftJobType")?.ToString();
            if (SatisfierJobs.Choice(name) is not { } choice)
            {
                Svc.Log.Warning($"[MCP] Satisfier's job choice '{name}' is not known to XIV MCP: no levels are checked.");
                return null;
            }
            return new(choice, Convert.ToUInt32(Plugin.Get(config, "SelectedCraftJob")), Convert.ToUInt32(Plugin.Get(config, "SelectedGatherJob")));
        }
        catch (Exception ex)
        {
            Svc.Log.Warning($"[MCP] Satisfier's job choice could not be read, no levels are checked: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// The crafters (ClassJobCategory 33) and gatherers (Miner, Botanist) in the ClassJob sheet's order, and Fisher. Built off the
    /// framework thread (<see cref="PrepareJobRows"/>) before <see cref="PickJobs"/> reads the levels on it.
    /// </summary>
    private static readonly Lazy<(ClassJob[] Crafters, ClassJob[] Gatherers, ClassJob Fisher)> JobRows = new(() =>
    {
        var sheet = Svc.Data.GetExcelSheet<ClassJob>();
        return ([.. sheet.Where(row => row.ClassJobCategory.RowId == 33)], [.. sheet.Where(row => row.RowId is 16 or 17)], sheet.GetRow(18));
    }, System.Threading.LazyThreadSafetyMode.PublicationOnly);

    /// <summary>Builds the job rows <see cref="PickJobs"/> needs, from game data: call it off the framework thread first. Never throws.</summary>
    public static void PrepareJobRows()
    {
        try { _ = JobRows.Value; }
        catch (Exception ex) { Svc.Log.Warning($"[MCP] The crafter and gatherer rows could not be read: {ex.Message}"); }
    }

    /// <summary>
    /// The crafter and gatherer Satisfier would use now (<see cref="SatisfierJobs"/>) and the character's levels in them and in Fisher,
    /// once per call. Null, with one warning, when Satisfier's settings cannot be read. Framework thread.
    /// </summary>
    public static JobPick? PickJobs()
    {
        if (JobSettings() is not { } settings) return null;
        try
        {
            var (crafterRows, gathererRows, fisher) = JobRows.Value;
            var crafters = crafterRows.Select(row => new JobLevel(row.RowId, Svc.PlayerState.GetClassJobLevel(row))).ToList();
            var gatherers = gathererRows.Select(row => new JobLevel(row.RowId, Svc.PlayerState.GetClassJobLevel(row))).ToList();
            var crafter = SatisfierJobs.Crafter(settings, crafters);
            var gatherer = SatisfierJobs.Gatherer(settings, gatherers, Svc.PlayerState.ClassJob.RowId);
            return new(crafters.FirstOrDefault(j => j.Job == crafter, new(crafter, 0)), gatherers.FirstOrDefault(j => j.Job == gatherer, new(gatherer, 0)),
                       Svc.PlayerState.GetClassJobLevel(fisher));
        }
        catch (Exception ex)
        {
            Svc.Log.Warning($"[MCP] The jobs Satisfier would use could not be worked out, no levels are checked: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Per client (by name), its requests' game data (<see cref="RequestData"/>): run it off the framework thread. Never throws: when
    /// game data or Satisfier's client data is not as expected, it logs one warning and leaves the client's data unknown, so nothing
    /// is checked.
    /// </summary>
    public static Dictionary<string, RequestData> Requests(IEnumerable<Client> clients, JobPick? pick)
    {
        var result = new Dictionary<string, RequestData>();
        string? problem = null;
        foreach (var client in clients)
        {
            try { result[client.Name] = LevelsOf(client, pick); }
            catch (Exception ex)
            {
                problem ??= ex.Message;
                result[client.Name] = RequestData.Unknown;
            }
        }
        if (problem is not null) Svc.Log.Warning($"[MCP] Custom delivery levels could not be read, they are not checked: {problem}");
        return result;
    }

    private static RequestData LevelsOf(Client client, JobPick? pick)
    {
        var rewards = Svc.Data.GetExcelSheet<SatisfactionSupplyReward>();
        int[] perDelivery = [.. client.Rewards.Select(r => (int)(rewards.GetRowOrDefault(r)?.SatisfactionHigh ?? 0))];
        var jobs = new DeliveryJob?[3];
        var names = new string?[3];
        short gatherMin = SupplyCollectability.Value.TryGetValue(client.Items[1], out var low) ? (short)low : (short)1;
        if (pick is { } p)
        {
            if (RecipeLevel(client.Items[0], p.Crafter.Job) is { } recipe) jobs[0] = DeliveryJob.Craft(p.Crafter.Job, p.Crafter.Level, recipe);
            if (GatheringLevels.Value.TryGetValue((client.Items[1], p.Gatherer.Job), out var point))
                jobs[1] = new(p.Gatherer.Job, p.Gatherer.Level, point.Level, point.MinLevel);
            if (!client.SpearFish && FishingLevel(client.Items[2], client.FishSpot) is { } fish) jobs[2] = DeliveryJob.Fish(p.FisherLevel, fish);
            for (var i = 0; i < 3; i++) names[i] = jobs[i] is { } j ? JobName(j.Job) : null;
        }
        return new(jobs, names, [1, gatherMin, 1], perDelivery);
    }

    public static string JobName(uint job) =>
        Svc.Data.GetExcelSheet<ClassJob>().GetRowOrDefault(job) is { } row ? Game.TitleCase(row.Name.ExtractText()) : $"job {job}";

    /// <summary>The level of the item's recipe for the crafter, the one Satisfier crafts (its GetRecipe: Carpenter's for other jobs).</summary>
    private static int? RecipeLevel(uint item, uint crafter)
    {
        if (Svc.Data.GetExcelSheet<RecipeLookup>().GetRowOrDefault(item) is not { } lookup) return null;
        var recipe = crafter switch
        {
            9 => lookup.BSM, 10 => lookup.ARM, 11 => lookup.GSM, 12 => lookup.LTW, 13 => lookup.WVR, 14 => lookup.ALC, 15 => lookup.CUL,
            _ => lookup.CRP,
        };
        return recipe.RowId == 0 ? null : recipe.ValueNullable?.RecipeLevelTable.ValueNullable?.ClassJobLevel;
    }

    /// <summary>
    /// Per item and gathering job (Miner, Botanist), over the gathering points that have it: the request's level (the lowest point's, or
    /// the item's own if higher) and the lowest job level that finds such a point and can gather the item (<see cref="DeliveryJob.GatherLevels"/>).
    /// </summary>
    private static readonly Lazy<Dictionary<(uint Item, uint Job), (int Level, int MinLevel)>> GatheringLevels = new(() =>
    {
        var gatheringItems = Svc.Data.GetExcelSheet<GatheringItem>();
        var levels = new Dictionary<(uint, uint), (int Level, int MinLevel)>();
        foreach (var point in Svc.Data.GetExcelSheet<GatheringPointBase>())
        {
            uint job = point.GatheringType.RowId switch { 0 or 1 => 16, 2 or 3 => 17, _ => 0 };
            if (job == 0) continue;
            foreach (var gi in point.Item)
            {
                if (gi.RowId == 0 || gatheringItems.GetRowOrDefault(gi.RowId) is not { } row || row.Item.RowId == 0) continue;
                var (level, minLevel) = DeliveryJob.GatherLevels(point.GatheringLevel, row.GatheringItemLevel.ValueNullable?.GatheringItemLevel ?? 0);
                var key = (row.Item.RowId, job);
                levels[key] = levels.TryGetValue(key, out var known) ? (Math.Min(level, known.Level), Math.Min(minLevel, known.MinLevel)) : (level, minLevel);
            }
        }
        return levels;
    }, System.Threading.LazyThreadSafetyMode.PublicationOnly);

    /// <summary>Per fish, its level (the first FishParameter row that has it).</summary>
    private static readonly Lazy<Dictionary<uint, int>> FishLevels = new(() =>
    {
        var levels = new Dictionary<uint, int>();
        foreach (var fish in Svc.Data.GetExcelSheet<FishParameter>())
            if (fish.Item.RowId != 0) levels.TryAdd(fish.Item.RowId, fish.GatheringItemLevel.ValueNullable?.GatheringItemLevel ?? 0);
        return levels;
    }, System.Threading.LazyThreadSafetyMode.PublicationOnly);

    /// <summary>Per delivery item, the collectability of its lowest tier, from the first SatisfactionSupply row that has it, as Satisfier reads it.</summary>
    private static readonly Lazy<Dictionary<uint, ushort>> SupplyCollectability = new(() =>
    {
        var lows = new Dictionary<uint, ushort>();
        foreach (var rows in Svc.Data.GetSubrowExcelSheet<SatisfactionSupply>())
            foreach (var row in rows)
                if (row.Item.RowId != 0) lows.TryAdd(row.Item.RowId, row.CollectabilityLow);
        return lows;
    }, System.Threading.LazyThreadSafetyMode.PublicationOnly);

    /// <summary>The fish's level or its fishing spot's, whichever is higher.</summary>
    private static int? FishingLevel(uint item, uint spot) =>
        FishLevels.Value.TryGetValue(item, out var level)
            ? Math.Max(level, Svc.Data.GetExcelSheet<FishingSpot>().GetRowOrDefault(spot)?.GatheringLevel ?? 0)
            : null;

    private static int Int(object o, string member) => Convert.ToInt32(Plugin.Get(o, member));

    /// <summary>
    /// Whether the client's unlock quest is done. Satisfier's own flag looks at the previous client's row (its index is the row id
    /// minus one), so XIV MCP reads the right row itself.
    /// </summary>
    private static bool IsUnlocked(int index) =>
        Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.SatisfactionNpc>().GetRowOrDefault((uint)index + 1) is { } row
        && QuestManager.IsQuestComplete(row.QuestRequired.RowId);

    /// <summary>
    /// The game's custom delivery agent as Satisfier judges it ("is the turn-in window open?"): whether it is active, for which
    /// client, and which focused window its addon id points at. Satisfier calls the turn-in window open when that window is visible,
    /// without checking that it is the turn-in window. Framework thread.
    /// </summary>
    public static unsafe object TurnInAgent()
    {
        var agent = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentSatisfactionSupply.Instance();
        if (agent == null) return new { available = false };
        FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase* focused = null;
        ref var list = ref FFXIVClientStructs.FFXIV.Component.GUI.AtkStage.Instance()->RaptureAtkUnitManager->AtkUnitManager.FocusedUnitsList;
        for (var i = 0; i < Math.Min((int)list.Count, list.Entries.Length); i++)
            if (list.Entries[i].Value is var unit && unit != null && unit->Id == agent->AddonId) { focused = unit; break; }
        return new
        {
            active = agent->IsAgentActive(),
            clientIndex = agent->NpcInfo.Id - 1,
            valid = agent->NpcInfo.Valid,
            initialized = agent->NpcInfo.Initialized,
            addonId = agent->AddonId,
            focusedWindow = focused == null ? null : focused->NameString,
            focusedWindowVisible = focused != null && focused->IsVisible,
        };
    }

    /// <summary>
    /// The capped currencies one delivery of a request pays (scrips), at the highest collectability tier, with what the player has.
    /// The reward row is the one Satisfier resolved (the bonus row when the bonus applies). Framework thread.
    /// </summary>
    public static unsafe List<CustomDeliveries.CappedReward> Rewards(Client client, DeliveryKind kind)
    {
        var rewards = new List<CustomDeliveries.CappedReward>();
        if (Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.SatisfactionSupplyReward>().GetRowOrDefault(client.Rewards[(int)kind]) is not { } row) return rewards;
        var cm = CurrencyManager.Instance();
        var level = Svc.Objects.LocalPlayer?.Level ?? 0;
        for (var i = 0; i < row.SatisfactionSupplyRewardData.Count; i++)
        {
            var data = row.SatisfactionSupplyRewardData[i];
            if (data.RewardCurrency == 0 || (i == 1 && level < row.MinLevelForSecondReward)) continue;
            var itemId = cm->GetItemIdBySpecialId((byte)data.RewardCurrency);
            if (itemId == 0) continue;
            rewards.Add(new CustomDeliveries.CappedReward(Util.Items.Name(itemId), Collectables.CurrencyHeld(itemId), cm->GetItemMaxCount(itemId),
                data.QuantityHigh * row.BonusMultiplier / 100));
        }
        return rewards;
    }

    /// <summary>Deliveries left this week for all clients together.</summary>
    public static unsafe int Allowances() => SatisfactionSupplyManager.Instance()->GetRemainingAllowances();

    private static object Automation =>
        Plugin.StaticValue(Plugin.Library("clib"), "clib.Services.Svc", "Automation") ?? throw Plugin.Unsupported("clib's automation");

    /// <summary>Starts Satisfier's task for the client: AutoCraft, AutoGather or AutoFish.</summary>
    public static void Start(Client client, DeliveryKind kind)
    {
        var type = Plugin.Type(kind switch { DeliveryKind.Craft => "Satisfy.AutoCraft", DeliveryKind.Gather => "Satisfy.AutoGather", _ => "Satisfy.AutoFish" });
        var task = Activator.CreateInstance(type, client.Info) ?? throw Plugin.Unsupported(type.Name);
        Plugin.Call(Automation, "Start", task, null, false);
    }

    /// <summary>
    /// Has the task just started for the client turn in only <paramref name="deliveries"/> (none: it still makes the items and walks to
    /// the client), until disposed. Satisfier's turn-in goes on until the client has no deliveries left this week, then closes the
    /// client's windows by itself; its count of the client's weekly deliveries is lowered to end there. Its task reads that count once
    /// when it starts, for how many to buy, craft or gather, so the lower count goes in on the first frame the task shows a status:
    /// it then still makes all of them. On that first frame the task can already be at the client and turn in with the old count when
    /// the delivery window is open; with nothing to turn in, the caller leaves Satisfier out then (<see cref="RankUpTurnIn.SkipSatisfier"/>).
    /// If the count cannot be lowered, Satisfier is stopped. The count goes back when this is disposed, once Satisfier is no longer
    /// running, or when XIV MCP unloads (<see cref="RestoreTurnInLimits"/>); a Satisfier still running then is stopped first, as with
    /// the old count it would hand in the player's delivery and click through the story. <paramref name="perDelivery"/> is what one
    /// delivery of each request adds, for the real numbers <see cref="Clients"/> shows meanwhile. Call right after <see cref="Start"/>.
    /// Framework thread.
    /// </summary>
    public static IDisposable LimitTurnIns(Client client, int deliveries, int[] perDelivery) =>
        new TurnInLimit(client.Info, Int(client.Info, "UsedDeliveries") + deliveries, perDelivery);

    private static readonly List<TurnInLimit> Limits = [];

    /// <summary>The lowered count of a client of Satisfier's (its NPCInfo), if one is in place now.</summary>
    private static TurnInLimit? LoweredFor(object npc)
    {
        lock (Limits) return Limits.FirstOrDefault(l => ReferenceEquals(l.Npc, npc) && l.Lowered);
    }

    /// <summary>
    /// Puts back every count still lowered, right away (stopping Satisfier first if it still runs): XIV MCP unloads, and the framework
    /// may not run its updates any more. Framework thread.
    /// </summary>
    public static void RestoreTurnInLimits()
    {
        List<TurnInLimit> live;
        lock (Limits) live = [.. Limits];
        foreach (var limit in live) limit.Dispose();
    }

    private sealed class TurnInLimit : IDisposable
    {
        private readonly int max;
        private IDisposable? applied;
        private bool done;
        private long nextCheck;

        public TurnInLimit(object npc, int max, int[] perDelivery)
        {
            Npc = npc;
            this.max = max;
            PerDelivery = perDelivery;
            lock (Limits) Limits.Add(this);
            Svc.Framework.Update += OnUpdate;
        }

        public object Npc { get; }

        public int[] PerDelivery { get; }

        /// <summary>Satisfier's count before it was lowered.</summary>
        public int Original { get; private set; }

        public bool Lowered => applied is not null && !done;

        private void OnUpdate(Dalamud.Plugin.Services.IFramework framework)
        {
            if (done) return;
            try
            {
                if (applied is null)
                {
                    if (Status.Length == 0) return;
                    Original = Int(Npc, "MaxDeliveries");
                    applied = Plugin.Override(Npc, ("MaxDeliveries", max));
                    nextCheck = Environment.TickCount64 + 1000;
                }
                // Once Satisfier is done the turn-in is over: the count goes back, also when the delivery that lowered it never ends.
                else if (Environment.TickCount64 >= nextCheck)
                {
                    nextCheck = Environment.TickCount64 + 1000;
                    if (!Running) Dispose();
                }
            }
            catch (Exception ex)
            {
                Svc.Log.Warning($"[MCP] Could not limit Satisfier's turn-in, stopping it: {ex.Message}");
                Dispose();
                try { Stop(); }
                catch (Exception stop) { Svc.Log.Warning($"[MCP] Could not stop Satisfier: {stop.Message}"); }
            }
        }

        /// <summary>Framework thread.</summary>
        public void Dispose()
        {
            if (done) return;
            done = true;
            Svc.Framework.Update -= OnUpdate;
            lock (Limits) Limits.Remove(this);
            if (applied is null) return;
            // With the old count back, a Satisfier still turning in would hand in the player's delivery too.
            try { if (Running) Stop(); }
            catch (Exception ex) { Svc.Log.Warning($"[MCP] Could not stop Satisfier before its count goes back: {ex.Message}"); }
            applied.Dispose();
        }
    }

    /// <summary>
    /// The character is at the client: the client's delivery window (the game's agent for this client) or a hand-over window is open,
    /// or the character stands within reach of the client. Satisfier's first frame would turn in from there. Framework thread.
    /// </summary>
    public static unsafe bool AtClient(Client client)
    {
        var agent = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentSatisfactionSupply.Instance();
        if (agent != null && agent->IsAgentActive() && agent->NpcInfo.Id == client.Index + 1) return true;
        if (RetainerUi.Ready("Request")) return true;
        try
        {
            return Plugin.Get(client.Info, "CraftData") is { } craft && Plugin.Get(craft, "TurnInLocation") is System.Numerics.Vector3 at &&
                   Svc.Objects.LocalPlayer is { } me && System.Numerics.Vector3.Distance(me.Position, at) <= 4f;
        }
        catch (Mcp.ToolException) { return false; }
    }

    public static bool Running => Plugin.Get(Automation, "Running") is true;

    /// <summary>
    /// What Questionable needs to gather and turn in a client's gathering request (Questionable.StartGatheringComplex), as Satisfier
    /// passes it: the client's turn-in id, the item, the gathering class and the collectability to reach.
    /// </summary>
    public static (uint TurnInId, uint ItemId, byte ClassJob, ushort Collectability) GatherRequest(Client client)
    {
        var gather = Plugin.Get(client.Info, "GatherData") ?? throw Plugin.Unsupported("NPCInfo.GatherData");
        return (Convert.ToUInt32(Plugin.Get(client.Info, "TurninId")), Convert.ToUInt32(Plugin.Get(gather, "GatherItemId")),
                Convert.ToByte(Plugin.Get(gather, "ClassJobId")), Convert.ToUInt16(Plugin.Get(gather, "CollectabilityHigh")));
    }

    public static string Status => Plugin.Get(Automation, "Status") as string ?? "";

    public static void Stop() => Plugin.Call(Automation, "Stop");

    /// <summary>Keeps Satisfier's window open until disposed: it refreshes the client data its tasks rely on while drawn.</summary>
    public static IDisposable KeepWindowOpen() => Plugin.Override(Window, ("IsOpen", true));
}
