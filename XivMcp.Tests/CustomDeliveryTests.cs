using XivMcp.CustomDeliveries;

namespace XivMcp.Tests;

public class DeliveryPlanTests
{
    private static readonly HashSet<DeliveryKind> All = [DeliveryKind.Craft, DeliveryKind.Gather, DeliveryKind.Fish];

    private static DeliveryNpc Npc(string name, int craft = 6, int gather = 6, int fish = 6, bool unlocked = true, params DeliveryKind[] bonus) =>
        new(name, unlocked, [craft, gather, fish], bonus.ToHashSet());

    [Fact]
    public void Each_npc_gets_its_remaining_deliveries_until_the_weekly_allowance_runs_out()
    {
        var plan = DeliveryPlan.Choose([Npc("Zhloe"), Npc("M'naago"), Npc("Kurenai")], All, allowances: 12);
        Assert.Equal([("Zhloe", 6), ("M'naago", 6)], plan.Select(p => (p.Npc.Name, p.Count)));
    }

    [Fact]
    public void A_client_gets_no_more_than_it_takes_this_week()
    {
        var plan = DeliveryPlan.Choose([Npc("Zhloe") with { Left = 2 }], All, allowances: 12);
        Assert.Equal([(DeliveryKind.Craft, 2)], plan.Select(p => (p.Kind, p.Count)));
    }

    [Fact]
    public void Crafting_comes_first_then_gathering()
    {
        var plan = DeliveryPlan.Choose([Npc("Zhloe"), Npc("Adkiragh", craft: 0)], All, allowances: 12);
        Assert.Equal([("Zhloe", DeliveryKind.Craft), ("Adkiragh", DeliveryKind.Gather)], plan.Select(p => (p.Npc.Name, p.Kind)));
    }

    [Fact]
    public void Clients_with_a_bonus_come_next_with_the_bonus_request_first()
    {
        var plan = DeliveryPlan.Choose(
            [Npc("Zhloe"), Npc("Adkiragh", craft: 3, gather: 3, bonus: DeliveryKind.Gather) with { Left = 6 }, Npc("Ameliance", bonus: DeliveryKind.Craft)],
            All, allowances: 12);
        Assert.Equal([("Adkiragh", DeliveryKind.Gather), ("Adkiragh", DeliveryKind.Craft), ("Ameliance", DeliveryKind.Craft)],
                     plan.Select(p => (p.Npc.Name, p.Kind)));
    }

    [Fact]
    public void A_fishing_bonus_counts_only_when_fishing()
    {
        var plan = DeliveryPlan.Choose([Npc("Zhloe"), Npc("Ehll Tou", bonus: DeliveryKind.Fish)], All, allowances: 6);
        Assert.Equal(["Zhloe"], plan.Select(p => p.Npc.Name));
        plan = DeliveryPlan.Choose([Npc("Zhloe"), Npc("Ehll Tou", bonus: DeliveryKind.Fish)], All, allowances: 6, kind: DeliveryKind.Fish);
        Assert.Equal(["Ehll Tou"], plan.Select(p => p.Npc.Name));
    }

    [Fact]
    public void Then_clients_that_need_more_deliveries_for_their_achievement()
    {
        var plan = DeliveryPlan.Choose(
            [Npc("Zhloe") with { AchievementLeft = 0 }, Npc("Kurenai") with { AchievementLeft = 40 }, Npc("Anden") with { AchievementLeft = 90 },
             Npc("Margrat", bonus: DeliveryKind.Craft) with { AchievementLeft = 10 }],
            All, allowances: 18);
        Assert.Equal(["Margrat", "Anden", "Kurenai"], plan.Select(p => p.Npc.Name));
    }

    [Fact]
    public void A_bonus_does_not_put_a_finished_achievement_first()
    {
        var plan = DeliveryPlan.Choose(
            [Npc("Zhloe", bonus: DeliveryKind.Craft) with { AchievementLeft = 0 }, Npc("Nitowikwe") with { AchievementLeft = 103 },
             Npc("Anden", bonus: DeliveryKind.Craft) with { AchievementLeft = 98 }],
            All, allowances: 12);
        Assert.Equal(["Anden", "Nitowikwe"], plan.Select(p => p.Npc.Name));
    }

    [Fact]
    public void Rank_ups_come_before_a_bonus()
    {
        var plan = DeliveryPlan.Choose([Npc("Ameliance", bonus: DeliveryKind.Craft), Npc("Tiisol Ja") with { GainsRank = true }], All, allowances: 6);
        Assert.Equal(["Tiisol Ja"], plan.Select(p => p.Npc.Name));
    }

    [Fact]
    public void Fishing_only_when_asked_for()
    {
        var onlyFish = new HashSet<DeliveryKind> { DeliveryKind.Fish };
        Assert.Empty(DeliveryPlan.Choose([Npc("Zhloe", bonus: DeliveryKind.Fish)], onlyFish, allowances: 12));
        Assert.Equal(DeliveryKind.Fish, DeliveryPlan.Choose([Npc("Zhloe")], onlyFish, allowances: 12, kind: DeliveryKind.Fish).Single().Kind);
    }

    [Fact]
    public void Only_kinds_that_can_be_done_are_chosen()
    {
        var plan = DeliveryPlan.Choose([Npc("Zhloe")], new HashSet<DeliveryKind> { DeliveryKind.Craft }, allowances: 12);
        Assert.Equal(DeliveryKind.Craft, plan.Single().Kind);
    }

    [Fact]
    public void A_chosen_kind_is_used_for_everyone()
    {
        var plan = DeliveryPlan.Choose([Npc("Zhloe", bonus: DeliveryKind.Craft), Npc("Ehll Tou")], All, allowances: 12, kind: DeliveryKind.Fish);
        Assert.All(plan, p => Assert.Equal(DeliveryKind.Fish, p.Kind));
    }

    [Fact]
    public void Locked_and_finished_npcs_are_skipped_and_a_partial_last_one_is_capped()
    {
        var plan = DeliveryPlan.Choose(
            [Npc("Locked", unlocked: false), Npc("Done", craft: 0, gather: 0, fish: 0), Npc("Zhloe", craft: 0, gather: 3), Npc("Kurenai")], All, allowances: 5);
        Assert.Equal([("Zhloe", 3), ("Kurenai", 2)], plan.Select(p => (p.Npc.Name, p.Count)));
    }

    [Fact]
    public void A_request_that_takes_fewer_than_the_client_still_takes_is_topped_up_with_gathering()
    {
        // Tiisol Ja at rank 4: six deliveries left this week, but each request takes only three more.
        var tiisol = Npc("Tiisol Ja", craft: 3, gather: 3, fish: 3) with { Left = 6 };
        var plan = DeliveryPlan.Choose([tiisol], All, allowances: 12);
        Assert.Equal([(DeliveryKind.Craft, 3), (DeliveryKind.Gather, 3)], plan.Select(p => (p.Kind, p.Count)));
    }

    [Fact]
    public void Crafting_stops_at_the_scrip_cap_and_gathering_takes_the_rest()
    {
        // 3,399/4,000 crafters' scrips, 270 per bonus delivery: two fit.
        var crafters = new CappedReward("Purple Crafters' Scrip", 3399, 4000, 270);
        var nito = Npc("Nitowikwe") with { Rewards = [[crafters], [], []] };
        var plan = DeliveryPlan.Choose([nito], All, allowances: 12);
        Assert.Equal([(DeliveryKind.Craft, 2), (DeliveryKind.Gather, 4)], plan.Select(p => (p.Kind, p.Count)));
    }

    [Fact]
    public void The_scrip_cap_is_shared_by_every_client_in_the_plan()
    {
        var crafters = new CappedReward("Purple Crafters' Scrip", 3399, 4000, 180);
        CappedReward[][] rewards = [[crafters], [], []];
        var plan = DeliveryPlan.Choose([Npc("Zhloe") with { Rewards = rewards }, Npc("Kurenai") with { Rewards = rewards }], All, allowances: 12);
        // 601 scrips fit three deliveries of 180: all three go to the first client.
        Assert.Equal([("Zhloe", DeliveryKind.Craft, 3), ("Zhloe", DeliveryKind.Gather, 3), ("Kurenai", DeliveryKind.Gather, 6)],
                     plan.Select(p => (p.Npc.Name, p.Kind, p.Count)));
    }

    [Fact]
    public void Clients_who_still_rank_up_come_first()
    {
        var plan = DeliveryPlan.Choose([Npc("Zhloe"), Npc("Nitowikwe"), Npc("Tiisol Ja") with { GainsRank = true }], All, allowances: 12);
        Assert.Equal(["Tiisol Ja", "Zhloe"], plan.Select(p => p.Npc.Name));
    }

    [Fact]
    public void A_chosen_kind_is_not_topped_up_with_another()
    {
        var tiisol = Npc("Tiisol Ja", craft: 3, gather: 3, fish: 3) with { Left = 6 };
        var plan = DeliveryPlan.Choose([tiisol], All, allowances: 12, kind: DeliveryKind.Gather);
        Assert.Equal([(DeliveryKind.Gather, 3)], plan.Select(p => (p.Kind, p.Count)));
    }

    [Fact]
    public void Only_the_named_npcs_when_given()
    {
        var plan = DeliveryPlan.Choose([Npc("Zhloe"), Npc("Kurenai")], All, allowances: 12, only: ["kurenai"]);
        Assert.Equal(["Kurenai"], plan.Select(p => p.Npc.Name));
    }

    [Fact]
    public void Nothing_to_do_without_allowances()
    {
        Assert.Empty(DeliveryPlan.Choose([Npc("Zhloe")], All, allowances: 0));
    }

    [Fact]
    public void A_request_whose_job_is_below_its_level_gives_way_to_the_next_kind()
    {
        // Kai-Shirr's rank 4 recipe is level 72, and Satisfier would craft with a level 66 crafter.
        var kai = Npc("Kai-Shirr", craft: 3, gather: 3, bonus: DeliveryKind.Craft) with { Left = 3, Blocked = new HashSet<DeliveryKind> { DeliveryKind.Craft } };
        var plan = DeliveryPlan.Choose([kai], All, allowances: 12);
        Assert.Equal([(DeliveryKind.Gather, 3)], plan.Select(p => (p.Kind, p.Count)));
    }

    [Fact]
    public void A_bonus_the_job_cannot_make_does_not_put_the_client_first()
    {
        var kai = Npc("Kai-Shirr", bonus: DeliveryKind.Craft) with { Blocked = new HashSet<DeliveryKind> { DeliveryKind.Craft } };
        var plan = DeliveryPlan.Choose([Npc("Zhloe"), kai, Npc("Kurenai", bonus: DeliveryKind.Gather)], All, allowances: 6);
        Assert.Equal([("Kurenai", DeliveryKind.Gather)], plan.Select(p => (p.Npc.Name, p.Kind)));
    }

    [Fact]
    public void A_chosen_kind_the_job_cannot_make_skips_the_client()
    {
        var kai = Npc("Kai-Shirr") with { Blocked = new HashSet<DeliveryKind> { DeliveryKind.Craft } };
        var plan = DeliveryPlan.Choose([kai, Npc("Zhloe")], All, allowances: 12, kind: DeliveryKind.Craft);
        Assert.Equal(["Zhloe"], plan.Select(p => p.Npc.Name));
    }

    private static readonly HashSet<DeliveryKind> CraftBlocked = [DeliveryKind.Craft];
    private static readonly HashSet<DeliveryKind> GatherBlocked = [DeliveryKind.Gather];

    [Fact]
    public void Left_out_for_level_names_the_request_the_plan_would_have_made()
    {
        var charlemend = Npc("Charlemend", craft: 3, gather: 3) with { Left = 3, Blocked = CraftBlocked };
        var leftOut = DeliveryPlan.LeftOutForLevel([charlemend], All, allowances: 12);
        Assert.Equal([("Charlemend", DeliveryKind.Craft)], leftOut.Select(x => (x.Npc.Name, x.Kind)));
    }

    [Fact]
    public void Left_out_for_level_skips_a_blocked_request_the_clients_crafting_covers()
    {
        // Crafting takes all six of Zhloe's deliveries, so the plan would never have gathered for her.
        var zhloe = Npc("Zhloe") with { Left = 6, Blocked = GatherBlocked };
        Assert.Empty(DeliveryPlan.LeftOutForLevel([zhloe], All, allowances: 12));
    }

    [Fact]
    public void Left_out_for_level_skips_clients_the_allowance_never_reaches()
    {
        var blocked = Npc("Kurenai") with { Blocked = CraftBlocked };
        Assert.Empty(DeliveryPlan.LeftOutForLevel([Npc("Zhloe"), Npc("M'naago"), blocked], All, allowances: 12));
    }

    [Fact]
    public void Left_out_for_level_skips_kinds_whose_plugin_is_missing()
    {
        var blocked = Npc("Kurenai") with { Blocked = CraftBlocked };
        Assert.Empty(DeliveryPlan.LeftOutForLevel([blocked], new HashSet<DeliveryKind> { DeliveryKind.Gather }, allowances: 12));
    }

    [Fact]
    public void Left_out_for_level_does_not_count_a_run_that_moved_for_another_reason()
    {
        // Room for six crafts under the cap. If Kai-Shirr could craft, Kai-Shirr would craft six and Zhloe gather six; as it is,
        // Kai-Shirr gathers and Zhloe crafts. Zhloe's gathering moved for the scrips, not for a level.
        var crafters = new CappedReward("Purple Crafters' Scrip", 2920, 4000, 180);
        CappedReward[][] rewards = [[crafters], [], []];
        var kai = Npc("Kai-Shirr") with { Rewards = rewards, Blocked = CraftBlocked };
        var zhloe = Npc("Zhloe") with { Rewards = rewards };
        var leftOut = DeliveryPlan.LeftOutForLevel([kai, zhloe], All, allowances: 12);
        Assert.Equal([("Kai-Shirr", DeliveryKind.Craft)], leftOut.Select(x => (x.Npc.Name, x.Kind)));
    }
}

public class SatisfierJobsTests
{
    // Levels on 2026-11-22: Armorer, Goldsmith, Leatherworker, Alchemist and Culinarian at 71, Carpenter at 73.
    private static readonly JobLevel[] Crafters =
        [new(8, 73), new(9, 74), new(10, 71), new(11, 71), new(12, 71), new(13, 80), new(14, 71), new(15, 71)];

    private static readonly JobLevel[] Gatherers = [new(16, 87), new(17, 80)];

    [Fact]
    public void Specific_uses_the_selected_jobs()
    {
        var settings = new SatisfierJobSettings(SatisfierJobChoice.Specific, CraftJob: 13, GatherJob: 17);
        Assert.Equal(13u, SatisfierJobs.Crafter(settings, Crafters));
        Assert.Equal(17u, SatisfierJobs.Gatherer(settings, Gatherers, currentJob: 16));
    }

    [Fact]
    public void Satisfiers_defaults_are_Carpenter_and_Miner()
    {
        Assert.Equal(8u, SatisfierJobs.Crafter(new SatisfierJobSettings(), Crafters));
        Assert.Equal(16u, SatisfierJobs.Gatherer(new SatisfierJobSettings(), Gatherers, currentJob: 9));
    }

    [Fact]
    public void Lowest_XP_takes_the_lowest_level_and_the_first_job_in_the_sheet_on_a_tie()
    {
        var settings = new SatisfierJobSettings(SatisfierJobChoice.LowestXP);
        Assert.Equal(10u, SatisfierJobs.Crafter(settings, Crafters));
        Assert.Equal(17u, SatisfierJobs.Gatherer(settings, Gatherers, currentJob: 16));
    }

    [Fact]
    public void Lowest_XP_ignores_jobs_not_unlocked()
    {
        var settings = new SatisfierJobSettings(SatisfierJobChoice.LowestXP);
        Assert.Equal(9u, SatisfierJobs.Crafter(settings, [new(8, 0), new(9, 50), new(10, 60)]));
        Assert.Equal(16u, SatisfierJobs.Gatherer(settings, [new(16, 40), new(17, 0)], currentJob: 0));
    }

    [Fact]
    public void Highest_XP_takes_the_highest_level()
    {
        var settings = new SatisfierJobSettings(SatisfierJobChoice.HighestXP);
        Assert.Equal(13u, SatisfierJobs.Crafter(settings, Crafters));
        Assert.Equal(16u, SatisfierJobs.Gatherer(settings, Gatherers, currentJob: 17));
    }

    [Fact]
    public void Highest_XP_takes_the_first_job_in_the_sheet_on_a_tie()
    {
        var settings = new SatisfierJobSettings(SatisfierJobChoice.HighestXP);
        Assert.Equal(9u, SatisfierJobs.Crafter(settings, [new(8, 60), new(9, 80), new(10, 80), new(11, 80)]));
        Assert.Equal(16u, SatisfierJobs.Gatherer(settings, [new(16, 90), new(17, 90)], currentJob: 17));
    }

    [Fact]
    public void Highest_XP_without_an_unlocked_job_uses_the_selected_one()
    {
        var settings = new SatisfierJobSettings(SatisfierJobChoice.HighestXP, CraftJob: 14, GatherJob: 17);
        Assert.Equal(14u, SatisfierJobs.Crafter(settings, [new(8, 0), new(9, 0)]));
        Assert.Equal(17u, SatisfierJobs.Gatherer(settings, [new(16, 0)], currentJob: 16));
    }

    [Fact]
    public void Without_an_unlocked_job_the_selected_one_is_used()
    {
        var settings = new SatisfierJobSettings(SatisfierJobChoice.LowestXP, CraftJob: 15, GatherJob: 17);
        Assert.Equal(15u, SatisfierJobs.Crafter(settings, [new(8, 0)]));
        Assert.Equal(17u, SatisfierJobs.Gatherer(settings, [], currentJob: 16));
    }

    [Fact]
    public void Current_crafts_with_the_selected_crafter_as_Satisfier_does()
    {
        var settings = new SatisfierJobSettings(SatisfierJobChoice.Current, CraftJob: 11);
        Assert.Equal(11u, SatisfierJobs.Crafter(settings, Crafters));
    }

    [Theory]
    [InlineData(17u, 17u)]
    [InlineData(16u, 16u)]
    [InlineData(18u, 16u)]
    [InlineData(9u, 16u)]
    public void Current_gathers_with_the_current_job_when_it_is_Miner_or_Botanist(uint current, uint expected)
    {
        var settings = new SatisfierJobSettings(SatisfierJobChoice.Current);
        Assert.Equal(expected, SatisfierJobs.Gatherer(settings, Gatherers, current));
    }

    [Theory]
    [InlineData("LowestXP", SatisfierJobChoice.LowestXP)]
    [InlineData("HighestXP", SatisfierJobChoice.HighestXP)]
    [InlineData("Current", SatisfierJobChoice.Current)]
    [InlineData("Specific", SatisfierJobChoice.Specific)]
    public void The_setting_is_read_by_name(string value, SatisfierJobChoice expected) =>
        Assert.Equal(expected, SatisfierJobs.Choice(value));

    [Theory]
    [InlineData("LowestLevel")]
    [InlineData("2")]
    [InlineData("")]
    [InlineData(null)]
    public void A_setting_this_version_does_not_know_is_not_guessed(string? value) => Assert.Null(SatisfierJobs.Choice(value));

    [Theory]
    [InlineData(72, 72, true)]
    [InlineData(71, 72, true)]
    [InlineData(67, 72, true)]
    [InlineData(66, 72, false)]
    public void A_crafter_can_start_recipes_up_to_five_levels_above_its_own(int jobLevel, int recipeLevel, bool canMake) =>
        Assert.Equal(canMake, DeliveryJob.Craft(10, jobLevel, recipeLevel).CanMake);

    [Theory]
    [InlineData(70, 66, 66, true)]
    [InlineData(70, 66, 65, false)]
    [InlineData(70, 60, 65, false)]
    [InlineData(70, 60, 66, true)]
    public void A_gatherer_finds_points_up_to_four_levels_above_its_own(int pointLevel, int itemLevel, int jobLevel, bool canMake)
    {
        var (level, minLevel) = DeliveryJob.GatherLevels(pointLevel, itemLevel);
        var job = new DeliveryJob(17, jobLevel, level, minLevel);
        Assert.Equal(70, job.Level);
        Assert.Equal(canMake, job.CanMake);
    }

    [Fact]
    public void An_item_above_the_gatherers_level_is_not_refused()
    {
        // Nothing says an item above the gatherer's level cannot be gathered at a point it finds.
        var (level, minLevel) = DeliveryJob.GatherLevels(pointLevel: 70, itemLevel: 68);
        var job = new DeliveryJob(17, 67, level, minLevel);
        Assert.True(job.CanMake);
        Assert.False(job.Refuses(3));
    }

    [Fact]
    public void A_job_that_cannot_make_the_request_blocks_only_when_items_are_still_to_be_made()
    {
        var crafter = DeliveryJob.Craft(10, jobLevel: 60, recipeLevel: 72);
        Assert.True(crafter.Blocks(inBags: 1, toDeliver: 3));
        Assert.False(crafter.Blocks(inBags: 3, toDeliver: 3));
        Assert.False(crafter.Blocks(inBags: 0, toDeliver: 0));
        Assert.False(DeliveryJob.Craft(10, jobLevel: 71, recipeLevel: 72).Blocks(inBags: 0, toDeliver: 3));
    }

    [Fact]
    public void Level_ok_says_whether_the_job_can_make_the_request_also_with_no_deliveries_left_this_week()
    {
        // Regression: with the week's deliveries used up nothing is blocked, and levelOk said true for a crafter that cannot start the
        // recipe, just when the next week is planned.
        var crafter = DeliveryJob.Craft(10, jobLevel: 60, recipeLevel: 72);
        Assert.False(crafter.LevelOk(inBags: 0, toDeliver: 0));
        Assert.False(crafter.LevelOk(inBags: 1, toDeliver: 3));
        Assert.True(crafter.LevelOk(inBags: 3, toDeliver: 3));
        Assert.True(DeliveryJob.Craft(10, jobLevel: 71, recipeLevel: 72).LevelOk(inBags: 0, toDeliver: 0));
    }

    [Fact]
    public void Fishing_levels_are_only_shown()
    {
        // No margin is known for fishing: a Fisher a level below is neither refused nor called unable.
        var fisher = DeliveryJob.Fish(fisherLevel: 70, level: 72);
        Assert.Null(fisher.LevelOk(inBags: 0, toDeliver: 3));
        Assert.False(fisher.Blocks(inBags: 0, toDeliver: 3));
        Assert.False(fisher.Refuses(3));
    }

    [Theory]
    [InlineData(60, 3, true)]
    [InlineData(60, 0, false)]
    [InlineData(67, 3, false)]
    [InlineData(71, 3, false)]
    [InlineData(72, 3, false)]
    public void A_delivery_is_refused_only_when_the_job_cannot_make_the_items_it_makes(int jobLevel, int toMake, bool refused) =>
        Assert.Equal(refused, DeliveryJob.Craft(10, jobLevel, recipeLevel: 72).Refuses(toMake));

    [Fact]
    public void Satisfier_makes_what_the_request_still_takes_whatever_the_scrip_cap()
    {
        // Six still to take, two fit under the scrip cap, two in the bags: Satisfier still crafts four.
        Assert.Equal(4, SatisfierMakes.Items(DeliveryKind.Craft, stillTakes: 6, usable: 2, viaQuestionable: false));
        Assert.Equal(4, SatisfierMakes.Items(DeliveryKind.Gather, stillTakes: 6, usable: 2, viaQuestionable: false));
        Assert.Equal(0, SatisfierMakes.Items(DeliveryKind.Craft, stillTakes: 3, usable: 5, viaQuestionable: false));
    }

    [Fact]
    public void Nothing_is_made_when_Questionable_turns_in_or_for_fishing()
    {
        Assert.Equal(0, SatisfierMakes.Items(DeliveryKind.Gather, stillTakes: 6, usable: 2, viaQuestionable: true));
        Assert.Equal(0, SatisfierMakes.Items(DeliveryKind.Fish, stillTakes: 6, usable: 0, viaQuestionable: false));
    }

    [Fact]
    public void Level_texts_name_the_levels_and_use_no_contractions()
    {
        var craft = DeliveryLevelText.CannotMake(DeliveryKind.Craft, "Medical Supplies", "Charlemend", "Armorer", DeliveryJob.Craft(10, 60, 72));
        Assert.Contains("level 60", craft);
        Assert.Contains("level 72", craft);
        var (level, minLevel) = DeliveryJob.GatherLevels(70, 66);
        var gather = DeliveryLevelText.CannotMake(DeliveryKind.Gather, "Strong Steppe Spice", "Adkiragh", "Botanist", new DeliveryJob(17, 60, level, minLevel));
        Assert.Contains("level 60", gather);
        Assert.Contains("are level 70", gather);
        foreach (var text in new[] { craft, gather })
            Assert.DoesNotMatch(@"(n't|'re|'ve|'ll|'d|(it|that|there|what)'s|I'm)", text);
    }
}

public class RankProgressTests
{
    [Theory]
    [InlineData(0, 90, 30, 3)]
    [InlineData(0, 480, 80, 6)]
    [InlineData(240, 480, 80, 3)]
    [InlineData(390, 780, 130, 3)]
    [InlineData(0, 1080, 180, 6)]
    [InlineData(100, 480, 80, 5)]
    public void Deliveries_to_the_next_rank_round_up(int satisfaction, int needed, int perDelivery, int expected) =>
        Assert.Equal(expected, RankProgress.DeliveriesToRankUp(satisfaction, needed, perDelivery));

    [Fact]
    public void The_top_rank_has_no_next_rank() => Assert.Null(RankProgress.DeliveriesToRankUp(0, 0, 0));
}

public class DeliveryAchievementTests
{
    private static readonly DeliveryAchievement[] Sheet =
    [
        new(1774, "Satisfaction Guaranteed: Zhloe Aliapoh I", 1),
        new(1779, "The Customer Is Always Right: Zhloe Aliapoh I", 36),
        new(1784, "The Customer Is Always Right: Zhloe Aliapoh VI", 150),
        new(1782, "The Customer Is Always Right: Zhloe Aliapoh IV", 100),
        new(1800, "The Customer Is Always Right: M'naago VI", 150),
    ];

    [Fact]
    public void A_clients_series_is_asked_about_its_last_tier()
    {
        Assert.Equal(1784u, DeliveryAchievement.LastTier("Zhloe Aliapoh", Sheet)?.Id);
        Assert.Equal(1800u, DeliveryAchievement.LastTier("m'naago", Sheet)?.Id);
        Assert.Null(DeliveryAchievement.LastTier("Kurenai", Sheet));
    }

    [Theory]
    [InlineData(40u, 150u, 110)]
    [InlineData(150u, 150u, 0)]
    [InlineData(170u, 150u, 0)]
    public void Deliveries_left_are_what_the_counter_still_lacks(uint current, uint max, int left)
    {
        Assert.Equal(left, DeliveryAchievement.Left(current, max));
    }
}

public class CustomDeliveryModuleTests
{
    [Theory]
    [InlineData("get_custom_deliveries")]
    [InlineData("deliver_custom_delivery")]
    [InlineData("do_custom_deliveries")]
    [InlineData("stop_custom_delivery")]
    public void The_tools_come_with_Satisfier(string tool)
    {
        Assert.False(XivMcp.Integrations.IntegrationCatalog.IsAvailable(tool, _ => false));
        Assert.True(XivMcp.Integrations.IntegrationCatalog.IsAvailable(tool, id => id == "vsatisfy"));
    }

    [Fact]
    public void Delivering_names_the_plugins_that_do_each_kind()
    {
        var needs = XivMcp.Integrations.ToolRequirements.For("do_custom_deliveries").Select(r => r.PluginId).ToList();
        Assert.Equal(["vsatisfy", "Artisan", "Questionable", "AutoHook", "vnavmesh"], needs);
    }
}
