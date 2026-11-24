using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using XivMcp.CustomDeliveries;
using XivMcp.Jobs;
using XivMcp.Mcp;
using XivMcp.Util;

namespace XivMcp.Tools;

/// <summary>
/// Custom deliveries with Satisfier: this week's requests, delivering one client's request by crafting (Artisan), gathering
/// (Questionable) or fishing (XIV MCP's own AutoHook fishing, as Satisfier only travels to the spot), and a job for the whole week.
/// </summary>
internal static class CustomDeliveryTools
{
    public static IEnumerable<McpTool> Create(Configuration config, Func<JobManager> jobs, Func<string?> client)
    {
        yield return new McpTool
        {
            Name = "get_custom_deliveries",
            Description = "This week's custom deliveries, from Satisfier: the deliveries left this week (for all clients together), and per " +
                          "client whether you unlocked them, their rank and satisfaction, the deliveries left, and their three requests (to " +
                          "craft, to gather, to fish) with the item, whether it carries the bonus, how many it still takes and how many you " +
                          "carry. 'stillTakes' is the client's deliveries left this week, or fewer when the client ranks up first; it is not " +
                          "the way to the next rank: a client below the top rank has 'satisfactionPerDelivery' (the same for all three " +
                          "requests, bonus or not) and 'deliveriesToRankUp', which can be more than this week allows (a rank-up brings new " +
                          "requests). Per request also its 'level' (the recipe's for the crafter Satisfier uses, the lowest gathering point " +
                          "that has it for the gatherer, the fish's or its spot's for Fisher), that 'job' and its 'jobLevel', and 'levelOk': " +
                          "the job can make it (a crafter starts recipes up to 5 levels above its own, a gatherer finds points up to 4 above " +
                          "its own), or enough are in the bags at the collectability Satisfier counts. Crafting and gathering with levelOk " +
                          "false are refused. Whether the items reach the collectability the client asks for depends on gear as much as on " +
                          "level, so it is not judged. Fishing levels are only shown, never enforced: no " +
                          "margin is known, so levelOk is null for fishing. The level fields are null when Satisfier's settings cannot be " +
                          "read (then nothing is checked). The refusal counts what Satisfier makes: all the request still takes less what " +
                          "is in the bags, whatever the scrip cap. Also which kinds can be " +
                          "delivered automatically: crafting needs Artisan, gathering Questionable, fishing AutoHook.",
            InputSchema = """{ "type": "object", "properties": {} }""",
            ReadOnly = true,
            Handler = async (_, ct) =>
            {
                var (clients, allowances, levels) = await ReadClients(ct).ConfigureAwait(false);
                return await Game.Run(() => Overview(clients, allowances, levels)).ConfigureAwait(false);
            },
        };

        yield return new McpTool
        {
            Name = "deliver_custom_delivery",
            Description = "Delivers one client's request with Satisfier and waits until it is done. 'kind' craft: buys the ingredient from " +
                          "the client's vendor, crafts with Artisan and turns in; gather: gathers with Questionable and turns in; fish: turns " +
                          "in the fish you carry (catch them first: do_custom_deliveries does both, or catch_fish). Without 'kind': the bonus " +
                          "request, else crafting, else gathering (Miner or Botanist); fishing only when the player asks for it. Delivers only as many as fit under " +
                          "the reward scrips' cap (get_custom_deliveries shows fitUnderScripCap), and refuses when none fit: spend the scrips " +
                          "first. Satisfier crafts and gathers with the job its 'Job choice' setting picks, without looking at the request's " +
                          "level: a crafting or gathering request that job cannot make (levelOk false in get_custom_deliveries) is refused " +
                          "before anything is bought, and without 'kind' the next kind is chosen. With 'player_ranks_up' the delivery that " +
                          "ranks the client up is left to the player, and the tool waits for them: run it as a job step (start_job, or " +
                          "do_custom_deliveries with the same argument). Requires 'Custom Deliveries' in /xivmcp.",
            InputSchema = """
                {
                  "type": "object",
                  "properties": {
                    "client": { "type": "string", "description": "The client's name (as get_custom_deliveries names them)." },
                    "kind": { "type": "string", "enum": ["craft", "gather", "fish"], "description": "Default: the bonus request, else craft, else gather. Use 'fish' only when the player asked for fishing." },
                    "player_ranks_up": { "type": "boolean", "description": "Leave the delivery that ranks the client up to the player, so they can watch the client's rank-up story (default false: Satisfier hands it in and the story is clicked through). The deliveries before it go in as usual, always with Satisfier (Questionable would hand in the rank-up item too). For the rank-up one the item is made and the character taken to the client; then the player is told in game, and the tool waits until the client's rank changes and the player is out of the story, without clicking anything. Stopping or failing the step while the player is in a conversation they started, or in the story, leaves it alone too. When the hand-in goes in without reaching the next rank (an item below the top collectability tier gives less satisfaction), the step ends saying so. After 30 minutes without a hand-in it fails (in a conversation the player started after being asked, after 2 hours): when the player handed it in after all, skip the step; calling it again makes the item again if it is gone, has Satisfier take the character back to the client, then asks again. A run after the client ranked up (a resume or retry of the same job step) delivers nothing and ends. When the story holds the character for over 10 minutes after the hand-in, it fails saying so: the delivery is done, skip the step. Changes nothing when no delivery of this week reaches the next rank." }
                  },
                  "required": ["client"]
                }
                """,
            ReadOnly = false,
            Handler = (args, ct) => Deliver(args.String("client") ?? throw new ToolException("'client' is required."), Kind(args),
                                            args.Bool("player_ranks_up", false), ct),
        };

        yield return new McpTool
        {
            Name = "do_custom_deliveries",
            Description = "Starts a background job for this week's custom deliveries: plans every client's remaining deliveries within the " +
                          "weekly allowance (clients below their top rank first; clients whose 'The Customer Is " +
                          "Always Right' achievements are done last; then clients with a bonus request, then those needing the most deliveries; per client the bonus request first, then " +
                          "crafting as far as the scrip cap allows, gathering with Miner or Botanist for the rest; never fishing unless " +
                          "asked for; only kinds a plugin can do; not crafting or gathering a request the job Satisfier picks cannot make: " +
                          "'leftOutForLevel' names those the plan would otherwise have delivered), then " +
                          "delivers them one client after another with deliver_custom_delivery. Fishing requests are caught first with " +
                          "AutoHook (switch to Fisher, bait, the spot, fish_until), only with kind 'fish'. Optional 'kind' for everyone and 'clients' to limit it. " +
                          "The plan shows per run 'rankUpDelivery': which of its deliveries ranks the client up, if one does. With " +
                          "'player_ranks_up' each rank-up delivery is left to the player (see deliver_custom_delivery), and the job's " +
                          "deliveries stop at the first one, followed by a do_custom_deliveries step that plans the rest again, as the client " +
                          "has new requests then. With player_ranks_up as a job step it puts its steps into that job right after itself " +
                          "(the job's later steps stay) instead of starting another job. " +
                          "Follow it with get_job. Requires 'Custom Deliveries' in /xivmcp; each step follows its own module.",
            InputSchema = """
                {
                  "type": "object",
                  "properties": {
                    "kind": { "type": "string", "enum": ["craft", "gather", "fish"], "description": "Deliver this kind for every client. Use 'fish' only when the player asked for fishing." },
                    "clients": { "type": "array", "items": { "type": "string" }, "description": "Only these clients." },
                    "player_ranks_up": { "type": "boolean", "description": "Leave each delivery that ranks a client up to the player, to watch the client's rank-up story (default false: Satisfier hands it in and the story is clicked through). The job then waits for the player at each rank-up (30 minutes, then that step fails and the job waits for retry or skip), and plans again after it. While it waits, other tool calls that act in the game are refused, as during any job step." }
                  }
                }
                """,
            ReadOnly = false,
            Handler = async (args, ct) =>
            {
                var playerRanksUp = args.Bool("player_ranks_up", false);
                var only = args.Array("clients")?.Select(n => n?.ToString() ?? "").Where(n => n.Length > 0).ToList();
                var (clients, allowances, levels) = await ReadClients(ct).ConfigureAwait(false);
                var achievementLeft = new Dictionary<string, int?>();
                foreach (var c in clients.Where(c => c.Unlocked && c.DeliveriesLeft > 0))
                    achievementLeft[c.Name] = await AchievementProgress.DeliveriesLeft(c.Name, ct).ConfigureAwait(false);
                var npcs = await Game.Run(() => clients.Select(c => c.ForPlan(achievementLeft.GetValueOrDefault(c.Name), levels[c.Name])).ToList()).ConfigureAwait(false);
                var possible = Possible();
                var plan = DeliveryPlan.Choose(npcs, possible, allowances, Kind(args), only);
                // The requests the plan would have delivered if their job could make them, so the plan's choice is explained.
                var leftOut = DeliveryPlan.LeftOutForLevel(npcs, possible, allowances, Kind(args), only)
                    .Select(x => CannotMake(clients.First(c => c.Name == x.Npc.Name), x.Kind, levels[x.Npc.Name])).OfType<string>().ToList();
                if (plan.Count == 0)
                {
                    var why = allowances == 0 ? "No custom deliveries left this week."
                                              : string.Join(" ", ["Nothing to deliver: no unlocked client has a request a plugin can do.", .. leftOut]);
                    // Planning again after a rank-up: nothing left ends the deliveries, not the job.
                    if (playerRanksUp && JobManager.Instance?.InsertAfterCurrent("do_custom_deliveries", []) == true) return new { done = true, message = why };
                    throw new ToolException(why);
                }

                var rankUps = DeliveryPlan.RankUps(plan);
                // After a rank-up the client has new requests, and the plan from there on no longer holds: plan again then.
                var (runs, planAgain) = RankUpJob.Cut(rankUps, playerRanksUp);
                var steps = new List<JobManager.Step>();
                foreach (var (run, i) in plan.Take(runs).Select((r, i) => (r, i)))
                {
                    var c = clients.First(x => x.Name == run.Npc.Name);
                    if (run.Kind == DeliveryKind.Fish)
                    {
                        if (c.SpearFish) throw new ToolException($"{c.Name} wants a spearfished catch, which XIV MCP can't do yet. Leave out {c.Name}, or choose another kind.");
                        var have = await Game.Run(() => Items.CountInBags(c.Items[2])).ConfigureAwait(false);
                        if (have < run.Count)
                        {
                            var fishArgs = new ToolArgs(new JsonObject { ["fish"] = c.Items[2].ToString(), ["spot"] = c.FishSpot.ToString() });
                            var (_, catchSteps, _) = await AutoHookTools.CatchSteps(fishArgs, config, run.Count - have, 99, 240, $"c{i}-", ct).ConfigureAwait(false);
                            steps.AddRange(catchSteps);
                        }
                    }
                    var deliver = new JobManager.Step
                    {
                        Id = $"d{i}", Tool = "deliver_custom_delivery",
                        Args = new JsonObject { ["client"] = c.Name, ["kind"] = run.Kind.ToString().ToLowerInvariant() },
                        Note = $"Deliver {run.Count} {Items.Name(c.Items[(int)run.Kind])} to {c.Name} ({run.Kind.ToString().ToLowerInvariant()})",
                    };
                    if (playerRanksUp) deliver.Args["player_ranks_up"] = true;
                    if (playerRanksUp && rankUps[i] is not null) deliver.Note += RankUpJob.RankUpNote;
                    steps.Add(deliver);
                }
                if (planAgain)
                    steps.Add(new()
                    {
                        Id = $"r{runs - 1}", Tool = "do_custom_deliveries", Args = RankUpJob.PlanAgainArgs(args.String("kind"), only),
                        Note = $"Plan the rest again after {plan[runs - 1].Npc.Name}'s rank-up",
                    });
                var planned = plan.Select((p, i) => new
                {
                    client = p.Npc.Name, kind = p.Kind.ToString().ToLowerInvariant(), count = p.Count, bonus = p.Npc.Bonus.Contains(p.Kind),
                    ranksUp = p.Npc.GainsRank, rankUpDelivery = rankUps[i], achievementDeliveriesLeft = p.Npc.AchievementLeft,
                });
                var plansAgain = planAgain ? $"The deliveries stop after {plan[runs - 1].Npc.Name}'s rank-up, and the rest is planned again then: the client has new requests." : null;
                if (playerRanksUp && JobManager.Instance?.InsertAfterCurrent("do_custom_deliveries", steps) == true)
                    return new { added = $"{steps.Count} step(s) after this one.", allowancesLeft = allowances, plan = planned, plansAgain,
                                 leftOutForLevel = leftOut.Count > 0 ? leftOut : null };
                var clientCount = plan.Select(p => p.Npc.Name).Distinct().Count();
                // A job that plans again changes its deliveries on the way: its name keeps no counts.
                var job = jobs().Start(playerRanksUp ? "Custom deliveries, rank-ups by you"
                                                     : $"Custom deliveries: {plan.Sum(p => p.Count)} for {clientCount} client{(clientCount == 1 ? "" : "s")}", steps, client());
                return new
                {
                    started = JobManager.Describe(job),
                    allowancesLeft = allowances,
                    plan = planned,
                    plansAgain,
                    leftOutForLevel = leftOut.Count > 0 ? leftOut : null,
                };
            },
        };

        yield return new McpTool
        {
            Name = "stop_custom_delivery",
            Description = "Stops the custom delivery Satisfier is doing. Requires 'Custom Deliveries' in /xivmcp.",
            InputSchema = """{ "type": "object", "properties": {} }""",
            ReadOnly = false,
            Handler = (_, ct) => Game.Run(() =>
            {
                var was = SatisfierBridge.Running ? SatisfierBridge.Status : null;
                SatisfierBridge.Stop();
                return (object)new { stopped = was is not null, was };
            }),
        };
    }

    private static DeliveryKind? Kind(ToolArgs args) => args.String("kind")?.ToLowerInvariant() switch
    {
        null => null,
        "craft" => DeliveryKind.Craft,
        "gather" => DeliveryKind.Gather,
        "fish" => DeliveryKind.Fish,
        var k => throw new ToolException($"'kind' must be craft, gather or fish, not '{k}'."),
    };

    /// <summary>The kinds a plugin can do now: crafting with Artisan, gathering with Questionable, fishing with AutoHook.</summary>
    private static HashSet<DeliveryKind> Possible()
    {
        var kinds = new HashSet<DeliveryKind>();
        if (PluginCompat.IsLoaded("Artisan")) kinds.Add(DeliveryKind.Craft);
        if (PluginCompat.IsLoaded("Questionable")) kinds.Add(DeliveryKind.Gather);
        if (AutoHookTools.Loaded) kinds.Add(DeliveryKind.Fish);
        return kinds;
    }

    private static readonly string[] KindNames = ["craft", "gather", "fish"];

    /// <summary>
    /// Satisfier's clients, the week's deliveries left, and per client its requests' levels against the jobs Satisfier would use:
    /// Satisfier's state and the job levels on the framework thread, the game data off it.
    /// </summary>
    private static async Task<(List<SatisfierBridge.Client> Clients, int Allowances, Dictionary<string, SatisfierBridge.RequestData> Levels)> ReadClients(
        CancellationToken ct, string? only = null)
    {
        await Task.Run(SatisfierBridge.PrepareJobRows, ct).ConfigureAwait(false);
        var (clients, allowances, pick) = await Game.Run(() =>
            (only is null ? SatisfierBridge.Clients() : [Find(only)], SatisfierBridge.Allowances(), SatisfierBridge.PickJobs())).ConfigureAwait(false);
        var levels = await Task.Run(() => SatisfierBridge.Requests(clients, pick), ct).ConfigureAwait(false);
        return (clients, allowances, levels);
    }

    /// <summary>Framework thread (reads the bags and currencies).</summary>
    private static object Overview(List<SatisfierBridge.Client> clients, int allowances, Dictionary<string, SatisfierBridge.RequestData> levels)
    {
        var possible = Possible();
        return new
        {
            deliveriesLeftThisWeek = allowances,
            automatic = KindNames.Where((_, i) => possible.Contains((DeliveryKind)i)),
            turnInAgent = SatisfierBridge.TurnInAgent(),
            clients = clients.Select(c =>
            {
                var request = levels[c.Name];
                // Every request of a rank adds the same satisfaction (game data); the smallest counts if that ever changes.
                var perDelivery = request.SatisfactionPerDelivery.Where(s => s > 0).DefaultIfEmpty(0).Min();
                return new
                {
                    client = c.Name,
                    unlocked = c.Unlocked,
                    rank = c.Rank,
                    satisfaction = c.SatisfactionMax > 0 ? $"{c.Satisfaction}/{c.SatisfactionMax}" : null,
                    satisfactionPerDelivery = c.Unlocked && c.SatisfactionMax > 0 && perDelivery > 0 ? perDelivery : (int?)null,
                    deliveriesToRankUp = c.Unlocked ? RankProgress.DeliveriesToRankUp(c.Satisfaction, c.SatisfactionMax, perDelivery) : null,
                    deliveriesLeft = c.DeliveriesLeft,
                    requests = c.Unlocked && c.Rank > 0
                        ? Enumerable.Range(0, 3).Select(i =>
                        {
                            var job = request.Jobs[i];
                            var usable = c.InBags(request, (DeliveryKind)i);
                            return new
                            {
                                kind = KindNames[i],
                                item = Items.Name(c.Items[i]),
                                bonus = c.Bonus[i],
                                stillTakes = c.Remaining[i],
                                inBags = Items.CountInBags(c.Items[i]),
                                // Deliveries past this would take a scrip over its cap (the game asks first; nothing answers).
                                fitUnderScripCap = ScripCap.DeliveriesThatFit(SatisfierBridge.Rewards(c, (DeliveryKind)i), c.Remaining[i]),
                                cappedBy = ScripCap.Limiting(SatisfierBridge.Rewards(c, (DeliveryKind)i), c.Remaining[i]) is { } cap
                                    ? $"{cap.Currency} {cap.Have:N0}/{cap.Max:N0}, up to {cap.PerDelivery} per delivery" : null,
                                level = job?.Level,
                                job = request.JobNames[i],
                                jobLevel = job?.JobLevel,
                                levelOk = job?.LevelOk(usable, c.Remaining[i]),
                                note = i == 2 && c.SpearFish ? "spearfishing" : null,
                            };
                        })
                        : null,
                };
            }),
        };
    }

    /// <summary>Why the job Satisfier would use cannot make a crafting or gathering request, or null when its level is not known.</summary>
    private static string? CannotMake(SatisfierBridge.Client c, DeliveryKind kind, SatisfierBridge.RequestData levels) =>
        levels.Jobs[(int)kind] is { } job && levels.JobNames[(int)kind] is { } name
            ? DeliveryLevelText.CannotMake(kind, Items.Name(c.Items[(int)kind]), c.Name, name, job)
            : null;

    /// <summary>The plugins that are missing for crafting or gathering, as sentences.</summary>
    private static IEnumerable<string> MissingPlugins()
    {
        if (!PluginCompat.IsLoaded("Artisan")) yield return "Crafting needs Artisan, which is not loaded.";
        if (!PluginCompat.IsLoaded("Questionable")) yield return "Gathering needs Questionable, which is not loaded.";
    }

    /// <summary>
    /// The client's cutscene after a turn-in (the week's last delivery, a rank-up) still holds the character when the deliveries are
    /// in, and dialogue follows it: wait the cutscene out (recovery doesn't interrupt one), then click through, so the character is
    /// free when the delivery returns.
    /// </summary>
    private static async Task<Recovery.Result> FinishConversation(CancellationToken ct)
    {
        var watch = new TurnInWatch(TimeSpan.FromSeconds(45));
        var actions = new List<string>();
        while (true)
        {
            // After a rank-up the client's cutscene ends in the rewards window, which waits for Accept while the game still counts
            // the character as watching the cutscene, so recovery alone would wait for it forever.
            if (await Game.Run(AcceptRankUp).ConfigureAwait(false))
            {
                actions.Add("Accepted the rank-up rewards.");
                watch.BlockedTooLong(false, DateTime.UtcNow);
                await Task.Delay(1500, ct).ConfigureAwait(false);
                continue;
            }
            var freed = await Recovery.Run(ct).ConfigureAwait(false);
            actions.AddRange(freed.Actions);
            // Dialogue clicked through in the client's cutscene is progress: only a cutscene that stands still for long gives up.
            if (freed.Actions.Count > 0) watch.BlockedTooLong(false, DateTime.UtcNow);
            if (!freed.Blocked || watch.BlockedTooLong(true, DateTime.UtcNow)) return freed with { Actions = actions };
            await Task.Delay(1000, ct).ConfigureAwait(false);
        }
    }

    private static async Task<object> Deliver(string name, DeliveryKind? kind, bool playerRanksUp, CancellationToken ct)
    {
        var (found, allowances, levelsOf) = await ReadClients(ct, name).ConfigureAwait(false);
        var c = found[0];
        var levels = levelsOf[c.Name];
        // A run of this step after the player ranked the client up (a resume, a retry) must not start on the next rank's requests.
        // Only this step's own memory counts: a step added after a skipped one is meant for the next rank, and the skipped step's
        // memory would wrongly say the rank-up already happened.
        var memory = playerRanksUp ? JobManager.Instance?.StepMemory(DeliverTool, lend: false) : null;
        if (RankUpMemory.RankedUpSince(memory, c.Name, c.Rank))
            return new { client = c.Name, delivered = 0, rank = c.Rank, done = true, rankedUp = PlayerRankUp.AlreadyRankedUp(c.Name, c.Rank) };
        if (!c.Unlocked) throw new ToolException($"{c.Name} is not unlocked yet.");
        if (c.DeliveriesLeft == 0) throw new ToolException($"{c.Name} takes no more deliveries this week.");
        if (allowances == 0) throw new ToolException("No custom deliveries left this week.");
        var forPlan = await Game.Run(() => c.ForPlan(null, levels)).ConfigureAwait(false);
        if ((kind ?? DeliveryPlan.Choose([forPlan], Possible(), 99).FirstOrDefault()?.Kind) is not { } chosen)
        {
            var why = MissingPlugins().Concat(forPlan.Blocked?.Select(k => CannotMake(c, k, levels)).OfType<string>() ?? []).ToList();
            throw new ToolException($"None of {c.Name}'s requests can be delivered automatically now. " +
                                    (why.Count > 0 ? string.Join(" ", why) : "The scrips they pay may be at their cap: get_custom_deliveries shows fitUnderScripCap."));
        }
        switch (chosen)
        {
            case DeliveryKind.Craft when !PluginCompat.IsLoaded("Artisan"):
                throw new ToolException("Crafting deliveries need Artisan, which isn't loaded.");
            case DeliveryKind.Gather when !PluginCompat.IsLoaded("Questionable"):
                throw new ToolException("Gathering deliveries need Questionable, which isn't loaded.");
            case DeliveryKind.Fish:
                var have = await Game.Run(() => Items.CountInBags(c.Items[2])).ConfigureAwait(false);
                if (have == 0)
                    throw new ToolException($"Catch {Items.Name(c.Items[2])} first (catch_fish), or let do_custom_deliveries catch and deliver them.");
                break;
        }

        var item = Items.Name(c.Items[(int)chosen]);
        var planned = c.Remaining[(int)chosen];
        var statuses = new List<string>();

        // A turn-in that would take a scrip over its cap makes the game ask first, and nobody answers that question: deliver only
        // what fits, and say why.
        var rewards = await Game.Run(() => SatisfierBridge.Rewards(c, chosen)).ConfigureAwait(false);
        if (ScripCap.Limiting(rewards, planned) is { } cap)
        {
            var fits = ScripCap.DeliveriesThatFit(rewards, planned);
            var why = $"{cap.Currency} is at {cap.Have:N0}/{cap.Max:N0} and one delivery of {item} gives up to {cap.PerDelivery}";
            if (fits == 0)
                throw new ToolException($"Delivering {item} to {c.Name} would take {cap.Currency} over its cap: {why}. Spend some {cap.Currency} first.");
            statuses.Add($"Only {fits} of {planned} deliveries fit under the cap: {why}.");
            planned = fits;
        }
        var (inBags, usable, atClient) = await Game.Run(() =>
            (Items.CountInBags(c.Items[(int)chosen]), c.InBags(levels, chosen), SatisfierBridge.AtClient(c))).ConfigureAwait(false);
        // With player_ranks_up the delivery that ranks the client up is left to the player; the ones before it go in as usual.
        var plan = RankUpTurnIn.Plan(playerRanksUp, chosen == DeliveryKind.Gather, c.Satisfaction, c.SatisfactionMax, levels.SatisfactionPerDelivery[(int)chosen],
                                     planned, allowances, inBags, usable, atClient);
        var toTurnIn = plan.ToTurnIn;
        var viaQuestionable = plan.ViaQuestionable;
        // Satisfier crafts and gathers with the job its settings pick, whatever the request's level, and makes all the request still
        // takes, whatever the scrip cap: refuse before it buys anything when that job cannot make them.
        if (levels.Jobs[(int)chosen] is { } job && levels.JobNames[(int)chosen] is { } jobName &&
            job.Refuses(SatisfierMakes.Items(chosen, c.Remaining[(int)chosen], usable, viaQuestionable)))
            throw new ToolException($"{DeliveryLevelText.CannotMake(chosen, item, c.Name, jobName, job)} Nothing was bought or made. Raise {jobName} " +
                                    $"to level {job.MinLevel}, choose another job in Satisfier's settings (Job choice), or deliver another kind.");
        if (plan.LeftToPlayer && !RankUpMemory.Remembers(memory, c.Name)) JobManager.Instance?.SetStepMemory(DeliverTool, RankUpMemory.Remember(c.Name, c.Rank));
        IDisposable? limit = null;
        var rankedUp = false;
        var lastLeft = c.DeliveriesLeft;
        var progressAt = DateTime.UtcNow;
        var turnIn = new TurnInWatch(TimeSpan.FromSeconds(45));
        DateTime? supplyOpenSince = null;
        DateTime? questionSince = null;
        var window = await Game.Run(SatisfierBridge.KeepWindowOpen).ConfigureAwait(false);
        // Satisfier and Questionable turn in themselves; YesAlready's own turn-in races them (see PluginCompat.PauseYesAlready).
        using var yesAlready = PluginCompat.PauseYesAlready("custom deliveries");
        // Who drives the delivery: Satisfier (which may run Questionable to gather), or Questionable alone turning in.
        bool Busy() => viaQuestionable ? QuestionableTools.Running : SatisfierBridge.Running;
        // Questionable is stopped after every gathering delivery, also one Satisfier ran: left running after a turn-in that ranked the
        // client up, it goes on to the next quest in the journal (the client's new sidequest) on its own.
        bool StopAll()
        {
            SatisfierBridge.Stop();
            if (chosen == DeliveryKind.Gather) QuestionableTools.StopIfRunning();
            return true;
        }
        try
        {
            if (chosen == DeliveryKind.Gather)
            {
                // A Questionable run left over from an earlier delivery can start at its turn-in step and wait there: reset it first.
                await Game.Run(() =>
                {
                    try { Svc.PluginInterface.GetIpcSubscriber<string, bool>("Questionable.Stop").InvokeFunc("XIV MCP"); }
                    catch (Exception ex) { Svc.Log.Debug($"[MCP] Questionable.Stop: {ex.Message}"); }
                    return true;
                }).ConfigureAwait(false);
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
            if (plan.SkipSatisfier)
                statuses.Add($"You are at {c.Name} with {item} in your bags, and the rank-up delivery is the first: Satisfier is left out.");
            else if (viaQuestionable)
            {
                await Game.Run(() =>
                {
                    var request = SatisfierBridge.GatherRequest(c);
                    if (!QuestionableTools.StartDeliveryGathering(request.TurnInId, request.ItemId, request.ClassJob, toTurnIn, request.Collectability))
                        throw new ToolException($"Questionable didn't take the turn-in of {item} for {c.Name}.");
                    return true;
                }).ConfigureAwait(false);
                statuses.Add($"Turning in {toTurnIn}x {item} with Questionable (Satisfier's own turn-in fails for some clients).");
            }
            else
                await Game.Run(() =>
                {
                    SatisfierBridge.Start(c, chosen);
                    // Satisfier turns in all a request still takes, the rank-up one too: it gets the client with only the deliveries
                    // before it left, so its turn-in ends there by itself, at the client, with every item made.
                    if (plan.LeftToPlayer) limit = SatisfierBridge.LimitTurnIns(c, toTurnIn, levels.SatisfactionPerDelivery);
                    return true;
                }).ConfigureAwait(false);
            if (!plan.SkipSatisfier) await Task.Delay(1000, ct).ConfigureAwait(false);
            var deadline = DateTime.UtcNow.AddHours(2);
            while (!plan.SkipSatisfier && await Game.Run(Busy).ConfigureAwait(false))
            {
                if (DateTime.UtcNow > deadline) throw new ToolException($"The delivery is still running after two hours ({statuses.LastOrDefault()}).");
                // A rank-up shows its rewards in a window that waits for Accept; Satisfier doesn't press it. Left to the player, the
                // window is theirs.
                if (!plan.LeftToPlayer && await Game.Run(AcceptRankUp).ConfigureAwait(false)) statuses.Add("Accepted the rank-up rewards.");
                var status = await Game.Run(() => SatisfierBridge.Status).ConfigureAwait(false);
                if (status.Length > 0 && statuses.LastOrDefault() != status)
                {
                    statuses.Add(status);
                    JobManager.Instance?.StepProgress(DeliverTool, status);
                }
                // Done once the planned deliveries are in. A rank-up brings new requests, which Satisfier would wait for in the
                // turn-in window without the items: stop it there too. With a rank-up left to the player Satisfier runs to its end,
                // closing the client's windows itself.
                var now = await Game.Run(() => Find(c.Name)).ConfigureAwait(false);
                if (now.DeliveriesLeft != lastLeft) { lastLeft = now.DeliveriesLeft; progressAt = DateTime.UtcNow; }
                // Checked before the conversation: the client's cutscene after the last turn-in still holds the character then.
                rankedUp = now.Rank != c.Rank;
                if (plan.Done(c.DeliveriesLeft - now.DeliveriesLeft, rankedUp))
                {
                    await Game.Run(StopAll).ConfigureAwait(false);
                    // A story left to the player is theirs, also when the rank-up came early: PlayerRanksUp waits it out.
                    if (plan.LeftToPlayer) break;
                    var freed = await FinishConversation(ct).ConfigureAwait(false);
                    if (freed.Actions.Count > 0) statuses.Add($"After the turn-in: {string.Join(" ", freed.Actions)}");
                    if (freed.Problem is { } problem) statuses.Add($"The character is still held after the turn-in: {problem}");
                    if (rankedUp) statuses.Add($"{c.Name} ranked up: new requests.");
                    break;
                }

                // The turn-in conversation can get in the way: Questionable opens the turn-in window before gathering and leaves it
                // open, and the game then counts the player as busy; a turn-in window can also load hidden, or vanish while the game
                // still holds the conversation. Get free unless someone is turning in: Satisfier ("Turning in"), or Questionable,
                // which turns in what it gathered itself once all of it is gathered. When turning in stalls, it is stuck.
                var stillNeeded = toTurnIn - (c.DeliveriesLeft - now.DeliveriesLeft);
                var (inConversation, questionableTurningIn) = await Game.Run(() =>
                    (RetainerUi.Ready("SatisfactionSupply") || RetainerUi.Ready("SelectString") || Recovery.OrphanedEvent,
                     TurnInWatch.QuestionableTurningIn(QuestionableTools.Running, Items.CountInBags(c.Items[(int)chosen]), stillNeeded))).ConfigureAwait(false);
                supplyOpenSince = inConversation ? supplyOpenSince ?? DateTime.UtcNow : null;
                var turningIn = TurnInWatch.SatisfierTurningIn(status) || questionableTurningIn;
                var asking = turningIn && await Game.Run(() => RetainerUi.Ready("SelectYesno")).ConfigureAwait(false);
                questionSince = asking ? questionSince ?? DateTime.UtcNow : null;
                if (supplyOpenSince is { } openSince && !turningIn && DateTime.UtcNow - openSince > TimeSpan.FromSeconds(5))
                {
                    var freed = await Recovery.Run(ct).ConfigureAwait(false);
                    if (freed.Actions.Count > 0) statuses.Add($"The turn-in conversation was in the way: {string.Join(" ", freed.Actions)}");
                    // A cutscene (the client's, after a rank-up) ends on its own: wait it out, unless it lasts.
                    if (freed.Problem is { } problem && (!freed.Blocked || turnIn.BlockedTooLong(true, DateTime.UtcNow)))
                        throw new ToolException($"The turn-in conversation with {c.Name} is stuck: {problem}");
                    if (!freed.Blocked) turnIn.BlockedTooLong(false, DateTime.UtcNow);
                    supplyOpenSince = null;
                }
                else if (questionSince is { } asked && DateTime.UtcNow - asked > TimeSpan.FromSeconds(3))
                {
                    // Neither Satisfier nor Questionable answers a question during the turn-in (the game asks e.g. before a turn-in
                    // that would overcap a currency): stop and say what it asked, rather than waiting for the stall.
                    await Game.Run(StopAll).ConfigureAwait(false);
                    var freed = await Recovery.Run(ct).ConfigureAwait(false);
                    statuses.Add($"The game asked a question during the turn-in, which nothing answers: stopped it. {string.Join(" ", freed.Actions)}".TrimEnd());
                    break;
                }
                else if (turnIn.Stalled(turningIn && plan.WatchStall, progressAt, DateTime.UtcNow))
                {
                    await Game.Run(StopAll).ConfigureAwait(false);
                    var freed = await Recovery.Run(ct).ConfigureAwait(false);
                    statuses.Add($"Turning in stalled for 45 seconds: stopped it. {string.Join(" ", freed.Actions)}".TrimEnd());
                    break;
                }
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
            if (plan.LeftToPlayer)
            {
                // Satisfier is done: its count goes back, and its window closes so it does not cover the client's story.
                await Game.Run(() => { limit?.Dispose(); window.Dispose(); return true; }).ConfigureAwait(false);
                rankedUp = await PlayerRanksUp(c, levels, chosen, item, toTurnIn, statuses, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            await Game.Run(StopAll).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await Game.Run(() => { limit?.Dispose(); window.Dispose(); return true; }).ConfigureAwait(false);
        }

        // What went in: the client's deliveries left this week (Satisfier's per-request count starts over after a rank-up).
        var after = await Game.Run(() => Find(c.Name)).ConfigureAwait(false);
        var delivered = c.DeliveriesLeft - after.DeliveriesLeft;
        if (delivered <= 0)
            throw new ToolException($"Satisfier stopped without delivering {item} to {c.Name}. Its last step: {statuses.LastOrDefault() ?? "none"}. " +
                                    "Satisfier's window (/vsatisfy) and the Dalamud log say more.");
        return new
        {
            client = c.Name,
            kind = chosen.ToString().ToLowerInvariant(),
            item,
            delivered,
            rank = after.Rank,
            rankedUp = rankedUp ? $"{c.Name} reached rank {after.Rank}: new requests. Plan the rest again (get_custom_deliveries, do_custom_deliveries)." : null,
            deliveriesLeft = after.DeliveriesLeft,
            deliveriesLeftThisWeek = await Game.Run(SatisfierBridge.Allowances).ConfigureAwait(false),
            steps = statuses,
        };
    }

    private const string DeliverTool = "deliver_custom_delivery";

    /// <summary>
    /// The rank-up delivery, left to the player: once the deliveries before it are in and its item is in the bags, asks the player in
    /// game to hand it in and waits until the client's rank changes (or the delivery goes in short of it), then until the player is out
    /// of the client's story. Never clicks or recovers anything in a conversation the player started meanwhile, also when the step is
    /// stopped or fails (<see cref="JobManager.PlayerInConversation"/>); see <see cref="HandInWait"/> and <see cref="StoryWait"/> for
    /// when it gives up. Returns true once the client ranked up.
    /// </summary>
    private static async Task<bool> PlayerRanksUp(SatisfierBridge.Client c, SatisfierBridge.RequestData levels, DeliveryKind kind, string item, int before,
                                                  List<string> statuses, CancellationToken ct)
    {
        var (now, have) = await Game.Run(() => (Find(c.Name), c.InBags(levels, kind))).ConfigureAwait(false);
        var delivered = c.DeliveriesLeft - now.DeliveriesLeft;
        if (now.Rank == c.Rank && (delivered < before || have == 0))
            throw new ToolException($"Satisfier stopped before the rank-up delivery to {c.Name} was ready (" +
                                    (before > 0 ? $"{delivered} of the {before} before it are in, " : "") +
                                    $"{have} {item} in the bags). Its last step: {statuses.LastOrDefault() ?? "none"}.");
        var askedPlayer = now.Rank == c.Rank;
        var outcome = askedPlayer ? HandIn.Wait : HandIn.RankedUp;
        if (askedPlayer)
        {
            ct.ThrowIfCancellationRequested();
            PlayerGuide.Ask(PlayerRankUp.Ask(item, c.Name));
            statuses.Add($"The rank-up delivery is left to you ({before} delivered before it): asked you in game to hand in {item}.");
            JobManager.Instance?.StepProgress(DeliverTool, $"Waiting for you to hand in {item} to {c.Name}.");
            var wait = new HandInWait(DateTime.UtcNow, now.DeliveriesLeft);
            try
            {
                while (outcome == HandIn.Wait)
                {
                    var (client, inConversation) = await Game.Run(() => (Find(c.Name), InConversation())).ConfigureAwait(false);
                    outcome = wait.Poll(DateTime.UtcNow, inConversation, client.Rank != c.Rank, client.DeliveriesLeft);
                    // Only a conversation the player started is theirs: one held from before (a menu left open) recovery may close.
                    JobManager.Instance?.PlayerInConversation(DeliverTool, wait.PlayersConversation || outcome is HandIn.RankedUp or HandIn.WentInShort);
                    if (outcome is HandIn.TimedOut or HandIn.TimedOutInConversation)
                    {
                        var stillHave = await Game.Run(() => c.InBags(levels, kind)).ConfigureAwait(false) > 0;
                        throw new ToolException(PlayerRankUp.TimedOut(item, c.Name, stillHave, outcome == HandIn.TimedOutInConversation));
                    }
                    if (outcome == HandIn.Wait) await Task.Delay(2000, ct).ConfigureAwait(false);
                }
            }
            finally { PlayerGuide.Done(); }
        }

        // The client's story goes on after the delivery: the next step must not find the player in it.
        JobManager.Instance?.PlayerInConversation(DeliverTool, true);
        var story = new StoryWait(DateTime.UtcNow);
        while (true)
        {
            var (inConversation, heldWithoutWindow) = await Game.Run(() =>
                (InConversation(), Recovery.OrphanedEvent && !InCutscene() && !StoryWindows.Any(RetainerUi.Ready))).ConfigureAwait(false);
            switch (story.Poll(DateTime.UtcNow, inConversation, heldWithoutWindow))
            {
                case StoryEnd.Over:
                    JobManager.Instance?.PlayerInConversation(DeliverTool, false);
                    var rank = (await Game.Run(() => Find(c.Name)).ConfigureAwait(false)).Rank;
                    statuses.Add(outcome == HandIn.WentInShort ? PlayerRankUp.WentInShort(item, c.Name)
                                 : askedPlayer ? $"You handed in {item}: {c.Name} reached rank {rank}."
                                 : $"{c.Name} reached rank {rank}, and you are out of the story.");
                    return outcome == HandIn.RankedUp;
                case StoryEnd.TooLong:
                    throw new ToolException(PlayerRankUp.StillInStory(c.Name));
                case StoryEnd.HeldWithoutWindow:
                    throw new ToolException(PlayerRankUp.HeldWithoutWindow(c.Name));
            }
            await Task.Delay(2000, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Windows a client's story can show that are not on recovery's list of conversation windows (it only sees them while focused): a
    /// quest the client offers, a quest it completes. While one is open the character is not held without a window.
    /// </summary>
    private static readonly string[] StoryWindows = ["JournalAccept", "JournalResult"];

    private static bool InCutscene() =>
        Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.OccupiedInCutSceneEvent] ||
        Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.WatchingCutscene] ||
        Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.WatchingCutscene78];

    /// <summary>The character is in a conversation, a cutscene or the rank-up rewards window. Framework thread.</summary>
    private static bool InConversation() => Recovery.Occupied || RetainerUi.Ready("SatisfactionSupplyResult") || InCutscene();

    /// <summary>Presses Accept on the rank-up rewards window (SatisfactionSupplyResult) when it is open.</summary>
    private static unsafe bool AcceptRankUp()
    {
        if (!RetainerUi.Ready("SatisfactionSupplyResult")) return false;
        var addon = (FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)RetainerUi.Ptr("SatisfactionSupplyResult").Address;
        return VentureTools.Click(addon, addon->GetComponentButtonById(36));
    }

    private static SatisfierBridge.Client Find(string name)
    {
        var clients = SatisfierBridge.Clients();
        return clients.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
               ?? clients.FirstOrDefault(c => Game.Matches(c.Name, name))
               ?? throw new ToolException($"No custom delivery client named '{name}'. Clients: {string.Join(", ", clients.Select(c => c.Name))}.");
    }
}
