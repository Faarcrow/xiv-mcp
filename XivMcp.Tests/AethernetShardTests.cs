using System.Numerics;
using XivMcp.Maps;

namespace XivMcp.Tests;

/// <summary>Values read from the game files (patch of 2026-11-23) with a scratch Lumina console; the few made up ones say so.</summary>
internal static class ShardFixtures
{
    public static ScriptEntry E(string instruction, uint argument) => new(instruction, argument);

    public static bool IsEObj(uint id) => id is >= 2_000_000 and < 2_100_000;

    // ------------------------------------------------------------------ the Firmament (886)

    public static readonly Transpoint Firmament = new(721597, "CtsHwdTranspoint_00701",
    [
        E("POPRANGE01", 8554068), E("POPRANGE02", 8627071), E("POPRANGE03", 8627079), E("POPRANGE04", 8627085),
        E("POPRANGE05", 8627114), E("POPRANGE06", 8627116), E("POPRANGE07", 8627120), E("POPRANGE08", 8627122),
        E("EOBJ01", 2011373), E("EOBJ02", 2011374), E("EOBJ03", 2011384), E("EOBJ04", 2011385),
        E("EOBJ05", 2011386), E("EOBJ06", 2011387), E("EOBJ07", 2011388), E("EOBJ08", 2011389),
        E("PLACENAME01", 3436), E("PLACENAME02", 3473), E("PLACENAME03", 3475), E("PLACENAME04", 3474),
        E("PLACENAME05", 3525), E("PLACENAME06", 3528), E("PLACENAME07", 3646), E("PLACENAME08", 3645),
    ], [2011373, 2011374, 2011384, 2011385, 2011386, 2011387, 2011388, 2011389]);

    /// <summary>planlive.lgb, layer Gimmick, and a made-up unrelated event object.</summary>
    public static readonly Placement[] FirmamentLayout =
    [
        new(2011373, new(23.9f, -16.0f, 169.4f)), new(2011374, new(76.0f, -18.5f, 10.3f)),
        new(2011384, new(149.5f, -50.0f, 98.6f)), new(2011385, new(207.8f, -40.0f, -25.6f)),
        new(2011386, new(-78.8f, 0f, 76.0f)), new(2011387, new(-132.5f, 10.0f, -14.7f)),
        new(2011388, new(-91.7f, 0f, -115.2f)), new(2011389, new(114.3f, -20.0f, -107.4f)),
        new(2000470, new(10f, 0f, 10f)),
    ];

    // ------------------------------------------------------------------ Bozja (920) and Zadnor (975)

    /// <summary>Only the first Bozja hub points at the script; the other hubs' EObj.Data is another handler.</summary>
    public static readonly Transpoint Bozja = new(721578, "CtsMycTranspoint_00682",
    [
        E("COND_ERR_FIGHTING", 7726),
        E("HUB1", 2011160), E("HUB2", 2011162), E("HUB3", 2011163), E("HUB4", 2011142),
        E("HUB2_QUEST", 69481), E("HUB34_QUEST", 69484),
        E("HUB55_1", 2011572), E("HUB55_2", 2011573), E("HUB55_3", 2011574), E("HUB55_4", 2011575),
        E("HUB55_2_QUEST", 69628), E("HUB55_34_QUEST", 69629),
    ], [2011160]);

    public static readonly Placement[] BozjaLayout =
    [
        new(2011160, new(-202.0f, 5.0f, 847.0f)), new(2011162, new(486.8f, 34.9f, 531.4f)),
        new(2011163, new(-258.0f, 35.9f, 534.4f)), new(2011142, new(169.8f, 3.0f, 192.3f)),
    ];

    /// <summary>The labelled markers of map 606 next to the shards (labels in PlaceNameSubtext), 0.4 to 2.2 yalms from them.</summary>
    public static readonly MapLabel[] BozjaLabels =
    [
        new(3575, 168.0f, 191.0f), new(3529, -202.0f, 846.0f), new(3530, 486.0f, 531.0f), new(3531, -258.0f, 534.0f),
    ];

    public static readonly Placement[] ZadnorLayout =
    [
        new(2011572, new(679.7f, 297.3f, 660.0f)), new(2011573, new(-356.5f, 286.0f, 758.4f)),
        new(2011574, new(-689.4f, 276.6f, -292.1f)), new(2011575, new(106.4f, 301.0f, -130.8f)),
    ];

    /// <summary>Map 665's labels stand 9.2 to 14.9 yalms from the shards (Camp Vrdelnis farthest; an unlabelled marker is 14.8 away).</summary>
    public static readonly MapLabel[] ZadnorLabels =
    [
        new(3664, 694.0f, 656.0f), new(3665, -364.0f, 768.0f), new(3666, -702.0f, -298.0f), new(3667, 114.0f, -136.0f),
    ];

    // ------------------------------------------------------------------ Eureka (732, 763, 795, 827)

    public static readonly Transpoint Eureka = new(721369, "CtsErkEureka1Transpoint_00473", [E("COND_ERR_FIGHTING", 7726)],
        [2009421, 2009432, 2009433, 2009562, 2009563, 2009564, 2009565, 2009615, 2009616, 2009617, 2009618, 2009713, 2009714, 2009715]);

    /// <summary>The EurekaAethernet sheet, rows 1 to 14.</summary>
    public static readonly uint[] EurekaAethernet = [2415, 2429, 2436, 2463, 2474, 2472, 2473, 2531, 2541, 2542, 2540, 2876, 2891, 2892];

    public static readonly Placement[] EurekaPlacements =
    [
        new(2009421, new(-138.9f, -12.3f, 543.2f)), new(2009432, new(-372.1f, 63.8f, -458.6f)), new(2009433, new(435.5f, 34.8f, -48.1f)),
        new(2009562, new(-893.7f, -668.6f, 159.0f)), new(2009563, new(91.1f, -731.0f, 303.2f)),
        new(2009564, new(-707.1f, -624.2f, -318.9f)), new(2009565, new(346.0f, -523.2f, -289.7f)),
        new(2009615, new(-253.5f, 680.7f, 146.8f)), new(2009616, new(127.9f, 659.9f, -196.1f)),
        new(2009617, new(-443.3f, 671.2f, -622.6f)), new(2009618, new(125.7f, 753.7f, 795.3f)),
        new(2009713, new(-61.6f, 523.2f, -875.6f)), new(2009714, new(-587.3f, 505.7f, -148.4f)), new(2009715, new(781.1f, 512.2f, -417.5f)),
    ];

    public static Placement[] PyrosLayout => EurekaPlacements.Where(p => p.Id is >= 2009615 and <= 2009618).ToArray();

    /// <summary>
    /// The labels of each Eureka map within 30 yalms of a shard: ten of the fourteen shards have one, 7.7 to 24.5 yalms away (five within
    /// 15). The four base camps (Port Surgate, Icepoint, Northpoint, Central Point) have none.
    /// </summary>
    public static readonly (Placement[] Shards, MapLabel[] Labels)[] EurekaZones =
    [
        (EurekaPlacements[..3], [new(2429, -389.0f, -463.0f), new(2436, 454.0f, -36.0f)]),
        (EurekaPlacements[3..7], [new(2474, 105.0f, 303.0f), new(2472, -719.0f, -308.0f), new(2473, 361.0f, -309.0f)]),
        (EurekaPlacements[7..11], [new(2541, 122.0f, -201.0f), new(2542, -448.0f, -632.0f), new(2540, 120.0f, 815.0f)]),
        (EurekaPlacements[11..], [new(2891, -601.0f, -143.0f), new(2892, 793.0f, -419.0f)]),
    ];

    public static MapLabel[] PyrosLabels => EurekaZones[2].Labels;

    // ------------------------------------------------------------------ the Occult Crescent: South Horn (1252)

    public static readonly Transpoint Occult = new(721820, "CtsMkdTranspoint_00924", [],
        [2014664, 2014665, 2014666, 2014667, 2014744, 2015429, 2015430, 2015431, 2015432, 2015433, 2015434]);

    public static readonly Placement[] SouthHornLayout =
    [
        new(2014664, new(830.7f, 73.0f, -696.0f)), new(2014665, new(-173.0f, 8.2f, -611.1f)), new(2014666, new(-358.1f, 102.0f, -121.0f)),
        new(2014667, new(306.9f, 105.2f, 305.7f)), new(2014744, new(-384.1f, 99.2f, 281.4f)),
    ];

    /// <summary>Map 967's labels with the aetheryte icon (60959), 0.1 to 1.0 yalms from the shards.</summary>
    public static readonly MapLabel[] SouthHornLabels =
    [
        new(4927, 830.0f, -696.0f), new(4928, -173.0f, -611.0f), new(4929, -358.0f, -121.0f), new(4930, 306.0f, 306.0f), new(4947, -384.0f, 281.0f),
    ];

    // ------------------------------------------------------------------ Cosmic Exploration (1237, 1291, 1310, 1319)

    public static readonly Transpoint Cosmic = new(721858, "CtsWksTranspoint_00962", [],
    [
        2015052, 2015053, 2015054, 2015055, 2015056, 2015057, 2015058, 2015059, 2015060, 2015061,
        2015062, 2015063, 2015064, 2015065, 2015066, 2015100, 2015422, 2015423, 2015424, 2015425, 2015426,
    ]);

    /// <summary>Sinus Ardorum's planlive.lgb, layer WKSAetheryte: 2015100 is a second object on the hub shard, 1.6 yalms higher.</summary>
    public static readonly Placement[] SinusArdorumLayout =
    [
        new(2015052, new(-3.8f, 0f, -32.2f)), new(2015053, new(-540.0f, 59.6f, -526.8f)), new(2015054, new(427.2f, 45.6f, 497.5f)),
        new(2015055, new(-619.6f, 50.6f, 399.8f)), new(2015056, new(629.8f, -74.0f, -572.8f)), new(2015100, new(-3.8f, 1.6f, -32.2f)),
    ];

    public static readonly Placement[] PhaennaLayout =
    [
        new(2015057, new(336.2f, 52.6f, -381.8f)), new(2015058, new(-150.8f, 29.1f, 314.4f)), new(2015059, new(-640.1f, -1.5f, -627.1f)),
        new(2015060, new(672.9f, -241.5f, 422.7f)), new(2015061, new(-590.9f, 28.5f, 722.3f)),
    ];

    public static readonly Placement[] OizysLayout =
    [
        new(2015062, new(-164.1f, 0.5f, 83.5f)), new(2015063, new(-141.2f, -75.0f, -591.2f)), new(2015064, new(-454.2f, 102.1f, 760.8f)),
        new(2015065, new(733.9f, 218.8f, -101.0f)), new(2015066, new(-124.6f, -193.7f, -801.5f)),
    ];

    public static readonly Placement[] AuxesiaLayout =
    [
        new(2015422, new(259.8f, 205.6f, 356.3f)), new(2015423, new(-226.4f, 145.0f, -560.4f)), new(2015424, new(-242.7f, 168.1f, 321.2f)),
        new(2015425, new(-599.9f, 206.6f, -375.1f)), new(2015426, new(-674.7f, 115.5f, 621.7f)),
    ];

    /// <summary>The WKSAetheryte sheet, rows 1 to 20: Name, and the EObj rows of its ObjectGroup (WKSAetheryteObjectGroup, Unknown0).</summary>
    public static readonly (uint PlaceName, IReadOnlyList<uint> Objects)[] WksAetheryte =
    [
        (5446, [2015052, 2015100]), (5447, [2015053]), (5448, [2015054]), (5449, [2015055]), (5450, [2015056]),
        (5451, [2015057]), (5452, [2015058]), (5453, [2015059]), (5454, [2015060]), (5455, [2015061]),
        (5456, [2015062]), (5457, [2015063]), (5458, [2015064]), (5459, [2015065]), (5460, [2015066]),
        (5546, [2015422]), (5547, [2015423]), (5548, [2015424]), (5549, [2015425]), (5550, [2015426]),
    ];

    // ------------------------------------------------------------------ names

    public static readonly Dictionary<uint, string> PlaceNames = new()
    {
        [3436] = "The Mendicant's Court", [3473] = "The Mattock", [3475] = "The New Nest", [3474] = "Saint Roelle's Dais",
        [3525] = "Featherfall", [3528] = "Hoarfrost Hall", [3646] = "Western Risensong Quarter", [3645] = "Eastern Risensong Quarter",
        [3575] = "Camp Steva", [3529] = "Utya's Aegis", [3530] = "Olana's Stand", [3531] = "Lunya's Stand",
        [3664] = "Camp Vrdelnis", [3665] = "Zuprtik Point", [3666] = "Ljeban Point", [3667] = "Hrmovir Point",
        [2415] = "Port Surgate", [2429] = "Abandoned Laboratory", [2436] = "Windtorn Cabin", [2463] = "Icepoint",
        [2474] = "Vlondette's Retreat", [2472] = "Geothermal Studies", [2473] = "Gravitational Studies", [2531] = "Northpoint",
        [2541] = "The Firing Chamber", [2542] = "Carbonatite Quarry", [2540] = "The Dragon Star Observatory", [2876] = "Central Point",
        [2891] = "Unverified Research", [2892] = "Dormitory",
        [4927] = "Expedition Base Camp", [4928] = "The Wanderer's Haven", [4929] = "Crystallized Caverns", [4930] = "Eldergrowth",
        [4947] = "Stonemarsh",
        [5446] = "Moongate Hub", [5447] = "Calabash Cove", [5448] = "Weddingway's Bower", [5449] = "Gleamslope", [5450] = "Lunar Nadir",
    };

    public static string? Name(uint placeName) => PlaceNames.GetValueOrDefault(placeName);

    public static readonly Transpoint[] AllTalks = [Firmament, Bozja, Eureka, Occult, Cosmic];

    public static IReadOnlyList<Shard> Build(IEnumerable<Placement> layout, IReadOnlyList<MapLabel>? labels = null,
                                             IReadOnlyDictionary<uint, uint>? listNames = null) =>
        ShardData.Build(AllTalks, layout, listNames ?? new Dictionary<uint, uint>(), labels ?? [], IsEObj, Name);

    public static Shard ByName(this IReadOnlyList<Shard> shards, string name) => shards.Single(s => s.Name == name);
}

public class ShardDataTests
{
    [Theory]
    [InlineData("CtsHwdTranspoint_00701", true)]
    [InlineData("CtsMycTranspoint_00682", true)]
    [InlineData("CtsErkEureka1Transpoint_00473", true)]
    [InlineData("CtsMkdTranspoint_00924", true)]
    [InlineData("CtsWksTranspoint_00962", true)]
    [InlineData("CtsWksEmergencyTransport_00958", false)]
    [InlineData("RegFstAetheryteGuid_00032", false)]
    public void Shard_scripts_are_the_transpoint_custom_talks(string name, bool expected) =>
        Assert.Equal(expected, ShardData.IsTranspoint(name));

    [Fact]
    public void The_firmament_script_names_its_shards_and_the_layout_places_them()
    {
        var shards = ShardFixtures.Build(ShardFixtures.FirmamentLayout);

        Assert.Equal(8, shards.Count);
        var nest = shards.ByName("The New Nest");
        Assert.Equal(2011384u, nest.Id);
        Assert.Equal(3475u, nest.PlaceName);
        Assert.Equal(new Vector3(149.5f, -50.0f, 98.6f), nest.Position);
        Assert.Null(nest.UnlockQuest);
        Assert.Equal(2011388u, shards.ByName("Western Risensong Quarter").Id);
        Assert.Equal(2011389u, shards.ByName("Eastern Risensong Quarter").Id);
    }

    [Fact]
    public void Script_names_pair_by_their_number()
    {
        var names = ShardData.ListedNames(ShardFixtures.Firmament.Script);
        Assert.Equal(3436u, names[2011373]);
        Assert.Equal(3645u, names[2011389]);
        Assert.Equal(8, names.Count);
    }

    [Fact]
    public void Bozja_takes_the_hubs_its_script_lists_that_stand_in_the_zone()
    {
        var shards = ShardFixtures.Build(ShardFixtures.BozjaLayout, ShardFixtures.BozjaLabels);

        Assert.Equal(["Camp Steva", "Lunya's Stand", "Olana's Stand", "Utya's Aegis"], shards.Select(s => s.Name).Order().ToArray());
        Assert.Null(shards.ByName("Utya's Aegis").UnlockQuest);
        Assert.Equal(69481u, shards.ByName("Olana's Stand").UnlockQuest);
        Assert.Equal(69484u, shards.ByName("Lunya's Stand").UnlockQuest);
        Assert.Equal(69484u, shards.ByName("Camp Steva").UnlockQuest);
    }

    [Fact]
    public void Zadnor_uses_the_same_script_and_labels_up_to_fifteen_yalms_away()
    {
        var shards = ShardFixtures.Build(ShardFixtures.ZadnorLayout, ShardFixtures.ZadnorLabels);

        Assert.Equal(4, shards.Count);
        Assert.Equal(2011572u, shards.ByName("Camp Vrdelnis").Id);
        Assert.Null(shards.ByName("Camp Vrdelnis").UnlockQuest);
        Assert.Equal(69628u, shards.ByName("Zuprtik Point").UnlockQuest);
        Assert.Equal(69629u, shards.ByName("Ljeban Point").UnlockQuest);
        Assert.Equal(69629u, shards.ByName("Hrmovir Point").UnlockQuest);
    }

    [Fact]
    public void A_quest_named_after_several_hubs_opens_each_of_them()
    {
        var quests = ShardData.UnlockQuests(ShardFixtures.Bozja.Script, ShardFixtures.IsEObj);
        Assert.Equal(69484u, quests[2011163]);
        Assert.Equal(69484u, quests[2011142]);
        Assert.Equal(69628u, quests[2011573]);
        Assert.False(quests.ContainsKey(2011160));
        Assert.False(quests.ContainsKey(2011572));
    }

    [Fact]
    public void A_label_farther_than_fifteen_yalms_names_nothing()
    {
        // Camp Vrdelnis's label moved to 16 yalms from its shard (made up; the real one is 14.9 away).
        var far = new MapLabel(3664, 679.7f, 676.0f);
        Assert.Null(ShardData.NearestLabel(new Vector3(679.7f, 297.3f, 660.0f), [far]));
        Assert.Equal(3664u, ShardData.NearestLabel(new Vector3(679.7f, 297.3f, 660.0f), ShardFixtures.ZadnorLabels)?.PlaceName);
    }

    [Fact]
    public void Eureka_names_come_from_its_sheet_in_object_order()
    {
        var names = ShardData.NamesInIdOrder(ShardFixtures.EurekaPlacements.Select(p => p.Id), ShardFixtures.EurekaAethernet);

        Assert.NotNull(names);
        var shards = ShardFixtures.Build(ShardFixtures.PyrosLayout, ShardFixtures.PyrosLabels, names);
        Assert.Equal(["Northpoint", "The Firing Chamber", "Carbonatite Quarry", "The Dragon Star Observatory"], shards.OrderBy(s => s.Id).Select(s => s.Name).ToArray());
    }

    [Fact]
    public void The_object_order_is_the_order_of_the_ids_not_of_the_layout()
    {
        var names = ShardData.NamesInIdOrder(ShardFixtures.EurekaPlacements.Select(p => p.Id).Reverse(), ShardFixtures.EurekaAethernet);
        Assert.NotNull(names);
        Assert.Equal(2415u, names[2009421]);
        Assert.Equal(2892u, names[2009715]);
    }

    [Fact]
    public void The_object_order_agrees_with_every_label_eureka_has()
    {
        // Ten of Eureka's fourteen shards have a label of one of the sheet's names within 30 yalms; the order names all ten alike.
        var names = ShardData.NamesInIdOrder(ShardFixtures.EurekaPlacements.Select(p => p.Id), ShardFixtures.EurekaAethernet)!;
        var zones = ShardFixtures.EurekaZones.Select(z => ((IReadOnlyList<Placement>)z.Shards, (IReadOnlyList<MapLabel>)z.Labels)).ToList();
        Assert.Equal((10, 0), ShardData.CheckOrder(names, zones));
    }

    [Fact]
    public void An_order_the_labels_contradict_is_caught()
    {
        var names = ShardData.NamesInIdOrder(ShardFixtures.EurekaPlacements.Select(p => p.Id), ShardFixtures.EurekaAethernet)!
            .ToDictionary();
        (names[2009432], names[2009433]) = (names[2009433], names[2009432]);
        var zones = ShardFixtures.EurekaZones.Select(z => ((IReadOnlyList<Placement>)z.Shards, (IReadOnlyList<MapLabel>)z.Labels)).ToList();
        Assert.Equal((8, 2), ShardData.CheckOrder(names, zones));
    }

    [Fact]
    public void Labels_of_other_places_do_not_count_against_the_order()
    {
        // A made-up label of a name the sheet does not list, right on Port Surgate's shard.
        var names = ShardData.NamesInIdOrder(ShardFixtures.EurekaPlacements.Select(p => p.Id), ShardFixtures.EurekaAethernet)!;
        var anemos = ShardFixtures.EurekaZones[0];
        IReadOnlyList<MapLabel> labels = [.. anemos.Labels, new MapLabel(9999, -138.9f, 543.2f)];
        Assert.Equal((2, 0), ShardData.CheckOrder(names, [(anemos.Shards, labels)]));
    }

    [Fact]
    public void An_order_no_label_confirms_is_not_used()
    {
        var names = ShardData.NamesInIdOrder(ShardFixtures.EurekaPlacements.Select(p => p.Id), ShardFixtures.EurekaAethernet)!;
        var unlabelled = ShardFixtures.EurekaZones.Select(z => ((IReadOnlyList<Placement>)z.Shards, (IReadOnlyList<MapLabel>)[])).ToList();
        var check = ShardData.CheckOrder(names, unlabelled);
        Assert.Equal((0, 0), check);
        Assert.False(ShardData.OrderConfirmed(check));
        Assert.True(ShardData.OrderConfirmed((10, 0)));
        Assert.False(ShardData.OrderConfirmed((8, 2)));
    }

    [Fact]
    public void Only_five_eureka_shards_have_a_label_close_enough_to_name_them()
    {
        var named = ShardFixtures.EurekaZones.Sum(z => ShardFixtures.Build(z.Shards, z.Labels).Count);
        Assert.Equal(5, named);
    }

    [Fact]
    public void A_name_list_that_does_not_fit_is_not_used() =>
        Assert.Null(ShardData.NamesInIdOrder(ShardFixtures.EurekaPlacements.Skip(1).Select(p => p.Id), ShardFixtures.EurekaAethernet));

    [Fact]
    public void Cosmic_exploration_links_each_name_to_its_objects()
    {
        var names = ShardData.LinkedNames(ShardFixtures.WksAetheryte);
        Assert.Equal(21, names.Count);
        Assert.Equal(5446u, names[2015052]);
        Assert.Equal(5446u, names[2015100]);
        Assert.Equal(5460u, names[2015066]);
        Assert.Equal(5546u, names[2015422]);
    }

    [Fact]
    public void Two_objects_with_one_name_are_one_shard()
    {
        // Sinus Ardorum's hub has a second object (2015100), 1.6 yalms above the first: the menu lists one Moongate Hub.
        var shards = ShardFixtures.Build(ShardFixtures.SinusArdorumLayout, listNames: ShardData.LinkedNames(ShardFixtures.WksAetheryte));
        Assert.Equal(["Moongate Hub", "Calabash Cove", "Weddingway's Bower", "Gleamslope", "Lunar Nadir"], shards.OrderBy(s => s.Id).Select(s => s.Name).ToArray());
        Assert.Equal(2015052u, shards.ByName("Moongate Hub").Id);
    }

    [Theory]
    [InlineData("CtsErkEureka1Transpoint_00473", "EurekaAethernet")]
    [InlineData("CtsWksTranspoint_00962", null)]
    [InlineData("CtsHwdTranspoint_00701", null)]
    [InlineData("CtsMkdTranspoint_00924", null)]
    public void A_name_sheet_goes_with_the_script_named_after_it(string talk, string? sheet) =>
        Assert.Equal(sheet, ShardData.NameSheetFor(talk, ["EurekaAethernet"]));

    [Fact]
    public void The_occult_crescent_names_its_shards_on_the_map()
    {
        var shards = ShardFixtures.Build(ShardFixtures.SouthHornLayout, ShardFixtures.SouthHornLabels);
        Assert.Equal(5, shards.Count);
        Assert.Equal(2014664u, shards.ByName("Expedition Base Camp").Id);
        Assert.Equal(2014744u, shards.ByName("Stonemarsh").Id);
    }

    [Fact]
    public void A_shard_without_a_name_is_left_out() =>
        Assert.Empty(ShardFixtures.Build(ShardFixtures.SouthHornLayout));

    [Fact]
    public void A_zone_without_shard_objects_has_no_network() =>
        Assert.Empty(ShardFixtures.Build([new Placement(2000470, new(1, 2, 3))]));
}

public class ShardRouteTests
{
    private static readonly IReadOnlyList<Shard> Firmament = ShardFixtures.Build(ShardFixtures.FirmamentLayout);

    // Charlemend's corner, near the Western Risensong Quarter, and the vendor Anna by the New Nest.
    private static readonly Vector3 Charlemend = new(-111f, 0f, -136f);
    private static readonly Vector3 Anna = new(149.7f, -50f, 68.5f);

    [Fact]
    public void Across_the_firmament_the_aethernet_is_taken()
    {
        var jump = ShardRoute.Choose(Firmament, Charlemend, Anna, retry: false, tried: []);

        Assert.NotNull(jump);
        Assert.Equal("Western Risensong Quarter", jump.Board.Name);
        Assert.Equal("The New Nest", jump.Destination.Name);
    }

    [Fact]
    public void A_short_way_is_walked() =>
        Assert.Null(ShardRoute.Choose(Firmament, Charlemend, new Vector3(-60f, 0f, -90f), retry: false, tried: []));

    [Fact]
    public void A_jump_has_to_save_more_than_the_walk_slack()
    {
        // From the New Nest to Saint Roelle's Dais: walking is 137 yalms, the jump leaves nothing to walk.
        var player = new Vector3(149.5f, -50f, 98.6f);
        var dais = new Vector3(207.8f, -40f, -25.6f);
        Assert.NotNull(ShardRoute.Choose(Firmament, player, dais, retry: false, tried: []));
        // 50 yalms to walk, and no shard is within 20 yalms of the target.
        var between = new Vector3(110f, -20f, 60f);
        Assert.Null(ShardRoute.Choose(Firmament, between, new Vector3(150f, -50f, 30f), retry: false, tried: []));
    }

    [Fact]
    public void The_walk_to_the_first_shard_counts()
    {
        // 94 yalms to walk, and The New Nest stands 9 yalms from the target, but the nearest other shard is 61 yalms from the player.
        Assert.Null(ShardRoute.Choose(Firmament, new Vector3(60f, 0f, 120f), new Vector3(149.5f, -50f, 90f), retry: false, tried: []));
    }

    [Fact]
    public void The_player_boards_at_the_nearest_shard_that_is_not_the_destination()
    {
        // Standing at the New Nest, going to the Western Risensong Quarter: the jump starts right here.
        var jump = ShardRoute.Choose(Firmament, new Vector3(149.5f, -50f, 98.6f), new Vector3(-92f, 0f, -120f), retry: false, tried: []);
        Assert.NotNull(jump);
        Assert.Equal("The New Nest", jump.Board.Name);
        Assert.Equal("Western Risensong Quarter", jump.Destination.Name);
    }

    [Fact]
    public void After_a_blocked_walk_the_tried_shard_is_skipped_and_it_never_walks()
    {
        var newNest = Firmament.ByName("The New Nest").Id;
        var jump = ShardRoute.Choose(Firmament, new Vector3(149.5f, -50f, 98.6f), Anna, retry: true, tried: [newNest]);
        Assert.NotNull(jump);
        Assert.NotEqual(newNest, jump.Destination.Id);
    }

    [Fact]
    public void A_shard_that_could_not_be_reached_is_not_boarded_again()
    {
        var western = Firmament.ByName("Western Risensong Quarter").Id;
        var jump = ShardRoute.Choose(Firmament, Charlemend, Anna, retry: false, tried: [], unreachable: [western]);
        Assert.Equal("Hoarfrost Hall", jump?.Board.Name);
        Assert.Equal("The New Nest", jump?.Destination.Name);

        var newNest = Firmament.ByName("The New Nest").Id;
        var again = ShardRoute.Choose(Firmament, Charlemend, Anna, retry: true, tried: [newNest], unreachable: [western]);
        Assert.NotNull(again);
        Assert.NotEqual(western, again.Board.Id);
    }

    [Fact]
    public void Locked_shards_are_not_used()
    {
        var bozja = ShardFixtures.Build(ShardFixtures.BozjaLayout, ShardFixtures.BozjaLabels);
        var open = ShardRoute.Usable(bozja, quest => quest == 69481);
        Assert.Equal(["Olana's Stand", "Utya's Aegis"], open.Select(s => s.Name).Order().ToArray());
        // From Utya's Aegis to Camp Steva: while Camp Steva is locked, the jump goes to Olana's Stand and walks on from there.
        var utya = new Vector3(-202f, 5f, 847f);
        var steva = new Vector3(169.8f, 3f, 200f);
        Assert.Equal("Olana's Stand", ShardRoute.Choose(open, utya, steva, retry: false, tried: [])?.Destination.Name);
        Assert.Equal("Camp Steva", ShardRoute.Choose(ShardRoute.Usable(bozja, _ => true), utya, steva, retry: false, tried: [])?.Destination.Name);
    }

    [Fact]
    public void A_destination_that_did_not_open_is_left_out()
    {
        var newNest = Firmament.ByName("The New Nest").Id;
        var usable = ShardRoute.Usable(Firmament, _ => true, closed: [newNest]);
        Assert.Equal(7, usable.Count);
        Assert.NotEqual("The New Nest", ShardRoute.Choose(usable, Charlemend, Anna, retry: false, tried: [])?.Destination.Name);
    }

    [Fact]
    public void A_network_of_one_shard_cannot_be_used() =>
        Assert.Null(ShardRoute.Choose([Firmament[0]], Charlemend, Anna, retry: false, tried: []));
}

public class ZoneEntryTests
{
    private static readonly AetheryteInfo Foundation = new(70, 418, IsAetheryte: true, AethernetGroup: 4);
    private static readonly AetheryteInfo BestwaysBurrow = new(175, 959, IsAetheryte: true, AethernetGroup: 0);
    private static readonly AetheryteInfo Limsa = new(8, 129, IsAetheryte: true, AethernetGroup: 1);

    [Fact]
    public void The_firmament_is_entered_by_the_foundation_aethernet() =>
        Assert.Equal(70u, ZoneEntry.Aetheryte(886, ownAetherytes: false, shards: 8, servedBy: Foundation));

    [Fact]
    public void A_zone_served_by_an_aetheryte_without_aethernet_is_not() =>
        Assert.Null(ZoneEntry.Aetheryte(1237, ownAetherytes: false, shards: 5, servedBy: BestwaysBurrow));

    [Fact]
    public void Rooms_without_shards_and_zones_with_their_own_aetherytes_are_not()
    {
        Assert.Null(ZoneEntry.Aetheryte(177, ownAetherytes: false, shards: 0, servedBy: Limsa));
        Assert.Null(ZoneEntry.Aetheryte(129, ownAetherytes: true, shards: 3, servedBy: Limsa));
        Assert.Null(ZoneEntry.Aetheryte(920, ownAetherytes: false, shards: 4, servedBy: null));
    }

    [Fact]
    public void The_unlock_quest_is_the_one_every_gated_warp_asks_for()
    {
        // Warps into the Firmament: Thomelin's (quest 69208), a quest warp without a condition, Cendresinne's (69208).
        Assert.Equal(69208u, ZoneEntry.UnlockQuest([[69208], [], [69208]]));
        Assert.Null(ZoneEntry.UnlockQuest([[69208], [70000]]));
        Assert.Null(ZoneEntry.UnlockQuest([[], []]));
    }
}

public class JumpWatchTests
{
    private static readonly DateTime Start = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    private static DateTime At(double seconds) => Start.AddSeconds(seconds);

    [Fact]
    public void Standing_at_the_destination_is_an_arrival()
    {
        var watch = new JumpWatch(Start);
        Assert.Equal(JumpOutcome.Waiting, watch.Next(At(1), arrived: false, idle: false, menuOpen: true));
        Assert.Equal(JumpOutcome.Arrived, watch.Next(At(4), arrived: true, idle: true, menuOpen: false));
    }

    [Fact]
    public void A_menu_without_the_destination_closes_it()
    {
        // The shard's menu opened, but the travel plugin found no entry for the destination and kept looking.
        var watch = new JumpWatch(Start);
        watch.Next(At(2), arrived: false, idle: false, menuOpen: true);
        Assert.Equal(JumpOutcome.Waiting, watch.Next(At(29), arrived: false, idle: false, menuOpen: true));
        Assert.Equal(JumpOutcome.NotInMenu, watch.Next(At(31), arrived: false, idle: false, menuOpen: true));
        Assert.True(JumpWatch.Closes(JumpOutcome.NotInMenu));
    }

    [Fact]
    public void A_menu_seen_once_still_counts_after_it_closed()
    {
        var watch = new JumpWatch(Start);
        watch.Next(At(2), arrived: false, idle: false, menuOpen: true);
        watch.Next(At(3), arrived: false, idle: true, menuOpen: false);
        Assert.Equal(JumpOutcome.Waiting, watch.Next(At(10), arrived: false, idle: true, menuOpen: false));
        Assert.Equal(JumpOutcome.NotInMenu, watch.Next(At(12), arrived: false, idle: true, menuOpen: false));
    }

    [Fact]
    public void Without_a_menu_the_destination_stays_open()
    {
        // Touching a shard the character had not attuned to attunes it and opens no menu.
        var watch = new JumpWatch(Start);
        watch.Next(At(1), arrived: false, idle: true, menuOpen: false);
        Assert.Equal(JumpOutcome.Waiting, watch.Next(At(8), arrived: false, idle: true, menuOpen: false));
        Assert.Equal(JumpOutcome.NoMenu, watch.Next(At(9.5), arrived: false, idle: true, menuOpen: false));
        Assert.False(JumpWatch.Closes(JumpOutcome.NoMenu));
    }

    [Fact]
    public void Being_busy_again_restarts_the_idle_time()
    {
        var watch = new JumpWatch(Start);
        watch.Next(At(1), arrived: false, idle: true, menuOpen: false);
        watch.Next(At(7), arrived: false, idle: false, menuOpen: false);
        watch.Next(At(8), arrived: false, idle: true, menuOpen: false);
        Assert.Equal(JumpOutcome.Waiting, watch.Next(At(15), arrived: false, idle: true, menuOpen: false));
        Assert.Equal(JumpOutcome.NoMenu, watch.Next(At(16.5), arrived: false, idle: true, menuOpen: false));
    }

    [Fact]
    public void Busy_without_a_menu_gives_up_after_the_time_limit() =>
        Assert.Equal(JumpOutcome.NoMenu, new JumpWatch(Start).Next(At(31), arrived: false, idle: false, menuOpen: false));
}
