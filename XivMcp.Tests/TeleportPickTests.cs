using System.Numerics;
using XivMcp.Maps;

namespace XivMcp.Tests;

public class TeleportPickTests
{
    // Limsa Upper Decks: the Airship Landing shard is right above the vendor (flat distance), but on a platform reached by lift.
    private static readonly TeleportOption Landing = new(1, false, ToTarget: 20, ToPlayer: null);
    private static readonly TeleportOption Aftcastle = new(2, false, ToTarget: 90, ToPlayer: null);
    private static readonly TeleportOption City = new(3, true, ToTarget: 300, ToPlayer: null);
    private static readonly TeleportOption[] Options = [Landing, Aftcastle, City];

    [Fact]
    public void From_another_zone_the_nearest_shard_is_taken() =>
        Assert.Equal(Landing, TeleportPick.Choose(Options, playerToTarget: null, retry: false, tried: []));

    [Fact]
    public void A_close_player_walks() =>
        Assert.Null(TeleportPick.Choose(Options, playerToTarget: 40, retry: false, tried: []));

    [Fact]
    public void After_a_blocked_walk_the_tried_shard_is_skipped() =>
        Assert.Equal(Aftcastle, TeleportPick.Choose(Options, playerToTarget: 20, retry: true, tried: [1]));

    [Fact]
    public void After_a_blocked_walk_the_shard_the_player_stands_at_is_skipped()
    {
        // Walked from the platform (nothing tried yet), the player stands at the landing shard.
        var here = new[] { Landing with { ToPlayer = 5 }, Aftcastle with { ToPlayer = 100 }, City with { ToPlayer = 280 } };
        Assert.Equal(here[1], TeleportPick.Choose(here, playerToTarget: 20, retry: true, tried: []));
    }

    [Fact]
    public void A_retry_never_walks_even_when_close() =>
        Assert.Equal(Aftcastle, TeleportPick.Choose(Options, playerToTarget: 10, retry: true, tried: [1]));

    [Fact]
    public void Nothing_left_to_try_is_null() =>
        Assert.Null(TeleportPick.Choose(Options, playerToTarget: 20, retry: true, tried: [1, 2, 3]));

    [Fact]
    public void An_aetheryte_wins_a_tie() =>
        Assert.Equal(3u, TeleportPick.Choose([new(4, false, 50, null), new(3, true, 50, null)], null, false, [])?.Id);
}

public class AetheryteSpotTests
{
    // Old Gridania (133) as its layout places the shards (planmap.lgb, LVD_townportal); O'rhoyod, the Maple Log vendor, as seen in game.
    private static readonly Vector3 ORhoyod = new(158.13f, 15.7f, -114.52f);
    private static readonly (uint Id, Vector3 Placed)[] OldGridania =
    [
        (26, new(101.3f, 9.0f, -111.3f)),   // Leatherworkers' Guild & Shaded Bower
        (27, new(121.3f, 12.7f, -229.6f)),  // Lancers' Guild
        (28, new(-145.1f, 5.0f, -11.7f)),   // Conjurers' Guild
        (29, new(-311.1f, 8.0f, -177.0f)),  // Botanists' Guild
        (30, new(-73.9f, 8.0f, -140.1f)),   // Mih Khetto's Amphitheatre
    ];

    // The map markers named like 29 and 30, which label their districts (80 and 55 yalms from the shards). 26 to 28 have none.
    private static Vector3? Label(uint id) => id switch { 29 => new(-237.5f, 0, -145.5f), 30 => new(-49.0f, 0, -90.5f), _ => null };

    private static uint? Pick(Vector3 target, bool fromLayout) =>
        TeleportPick.Choose(OldGridania.Select(s =>
        {
            var at = AetheryteSpot.Choose(fromLayout ? s.Placed : null, level: null, () => Label(s.Id));
            return new TeleportOption(s.Id, false, at is { } p ? (float)MapMath.GroundDistance(p.X, p.Z, target.X, target.Z) : null, null);
        }).ToList(), null, false, [])?.Id;

    [Fact]
    public void A_vendor_in_Old_Gridania_gets_the_shard_next_to_it()
    {
        // Regression: from the markers alone only 29 and 30 had a position, so travel took the aethernet to Mih Khetto's Amphitheatre,
        // 233 yalms from O'rhoyod, past Leatherworkers' Guild & Shaded Bower, 57 yalms from her.
        Assert.Equal(30u, Pick(ORhoyod, fromLayout: false));
        Assert.Equal(26u, Pick(ORhoyod, fromLayout: true));
    }

    [Fact]
    public void The_layout_wins_over_a_marker_named_like_the_shard()
    {
        // Between the two: Mih Khetto's district label is 62 yalms away, its shard 109, Leatherworkers' Guild 100.
        Assert.Equal(26u, Pick(new(10, 0, -70), fromLayout: true));
    }

    [Fact]
    public void The_marker_is_only_looked_up_when_nothing_else_has_the_position()
    {
        var looked = 0;
        Vector3? Marker() { looked++; return new(7, 0, 8); }
        Assert.Equal(new Vector3(1, 2, 3), AetheryteSpot.Choose(new(1, 2, 3), new(4, 5, 6), Marker));
        Assert.Equal(new Vector3(4, 5, 6), AetheryteSpot.Choose(null, new(4, 5, 6), Marker));
        Assert.Equal(0, looked);
        Assert.Equal(new Vector3(7, 0, 8), AetheryteSpot.Choose(null, null, Marker));
        Assert.Equal(1, looked);
    }

    [Fact]
    public void A_zone_layout_keeps_the_first_placement_of_each_aetheryte()
    {
        var placed = AetheryteSpot.Placed([(26, new(101.3f, 9.0f, -111.3f)), (26, new(0, 0, 0)), (0, new(5, 5, 5)), (27, new(121.3f, 12.7f, -229.6f))]);
        Assert.Equal(2, placed.Count);
        Assert.Equal(new Vector3(101.3f, 9.0f, -111.3f), placed[26]);
    }
}

public class TeleportAttunedTests
{
    // Labyrinthos: Sharlayan Hamlet is nearest the NPC, but this character has only The Archeion attuned.
    private static readonly TeleportOption Hamlet = new(1, true, ToTarget: 250, ToPlayer: null, Attuned: false);
    private static readonly TeleportOption Archeion = new(2, true, ToTarget: 620, ToPlayer: null);

    [Fact]
    public void An_aetheryte_the_character_has_not_attuned_is_never_picked()
    {
        // Regression: travel picked the nearest aetheryte whether or not it was attuned, and the teleport then failed with
        // "could not travel to Sharlayan Hamlet (is the aetheryte attuned?)".
        Assert.Equal(Archeion, TeleportPick.Choose([Hamlet, Archeion], playerToTarget: null, retry: false, tried: []));
        Assert.Null(TeleportPick.Choose([Hamlet], playerToTarget: null, retry: false, tried: []));
        Assert.True(TeleportPick.NoneAttuned([Hamlet]));
        Assert.False(TeleportPick.NoneAttuned([Hamlet, Archeion]));
        Assert.False(TeleportPick.NoneAttuned([]));
    }

    [Fact]
    public void In_the_zone_an_unattuned_shard_only_leaves_walking()
    {
        // Standing 300 yalms from the NPC with the only shard near it not attuned: walk.
        Assert.Null(TeleportPick.Choose([Hamlet], playerToTarget: 300, retry: false, tried: []));
    }
}

public class AetheryteAccessTests
{
    [Fact]
    public void A_shard_in_another_city_needs_that_city_s_aetheryte_too()
    {
        // Regression (in game): from Thavnair, travel chose the Hall of the Radiant Host shard in Radz-at-Han, which the character had
        // touched, but Radz-at-Han's own aetheryte was not attuned, so Lifestream could not get there ("did not leave the zone").
        Assert.False(AetheryteAccess.Usable(isAetheryte: false, attuned: true, cityAttuned: false, inCity: false));
        Assert.True(AetheryteAccess.Usable(isAetheryte: false, attuned: true, cityAttuned: true, inCity: false));
        Assert.True(AetheryteAccess.Usable(isAetheryte: false, attuned: true, cityAttuned: false, inCity: true));
        Assert.False(AetheryteAccess.Usable(isAetheryte: false, attuned: false, cityAttuned: true, inCity: true));
        Assert.True(AetheryteAccess.Usable(isAetheryte: true, attuned: true, cityAttuned: false, inCity: false));
        Assert.False(AetheryteAccess.Usable(isAetheryte: true, attuned: false, cityAttuned: true, inCity: true));
    }

    [Fact]
    public void A_gateway_shard_in_the_field_does_not_make_its_zone_part_of_the_city()
    {
        // Radz-at-Han's aethernet group: the city (963, town) and The Gate of First Sight in Thavnair (957, field). Regression: standing
        // in Thavnair counted as being in the city, so a city shard was planned without Radz-at-Han's aetheryte.
        var radz = new[] { new AethernetZone(963, IntendedUse: 0, HasMain: true), new AethernetZone(957, IntendedUse: 1, HasMain: false) };
        Assert.Equal([963u], AetheryteAccess.CityZones(radz));
        // Gridania: both cities count, the Shroud gates do not.
        var gridania = new[] { new AethernetZone(132, 0, true), new AethernetZone(133, 0, false), new AethernetZone(148, 1, false), new AethernetZone(154, 1, false) };
        Assert.Equal([132u, 133u], AetheryteAccess.CityZones(gridania).Order());
        // The Gold Saucer and Chocobo Square share their own intended use.
        Assert.Equal(2, AetheryteAccess.CityZones([new(144, 23, true), new(388, 23, false)]).Count);
    }
}
