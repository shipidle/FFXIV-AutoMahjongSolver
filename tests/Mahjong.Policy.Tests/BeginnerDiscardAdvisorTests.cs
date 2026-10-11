using Mahjong.Engine;
using Mahjong.Policy.Efficiency;

namespace Mahjong.Policy.Tests;

public class BeginnerDiscardAdvisorTests
{
    [Fact]
    public void Recommendation_never_sacrifices_reachable_shanten_in_attack_mode()
    {
        var s = Snapshots.Closed14("123m456p789s234s5z6z");
        var rec = BeginnerDiscardAdvisor.Recommend(s);

        Assert.NotNull(rec);
        Assert.False(rec!.DefenseMode);

        var hand = Hand.FromTiles(s.Hand, s.OurMelds);
        var wall = BeginnerDiscardAdvisor.BuildVisibleWall(s);
        int bestShanten = UkeireEnumerator.Enumerate(hand, wall).Min(x => x.ShantenAfter);

        Assert.Equal(bestShanten, rec.ShantenAfter);
    }

    [Fact]
    public void Far_hand_against_riichi_switches_to_defense_mode()
    {
        var s = Snapshots.Closed14("147m258p369s12345z");
        var seats = s.Seats.ToArray();
        seats[1] = seats[1] with
        {
            Riichi = true,
            Discards = Tiles.Parse("1m4p7s"),
        };
        s = s with { Seats = seats };

        var rec = BeginnerDiscardAdvisor.Recommend(s);

        Assert.NotNull(rec);
        Assert.True(rec!.DefenseMode);
    }

    [Fact]
    public void Exact_tile_already_discarded_by_riichi_opponent_is_safe()
    {
        var s = Snapshots.Closed14("123m456p789s234s5z6z");
        var seats = s.Seats.ToArray();
        seats[1] = seats[1] with
        {
            Riichi = true,
            Discards = Tiles.Parse("5m"),
        };
        s = s with { Seats = seats };

        var (level, _) = BeginnerDiscardAdvisor.EvaluateSafety(s, Tiles.Parse("5m")[0]);

        Assert.Equal(BeginnerSafetyLevel.Safe, level);
    }

    [Fact]
    public void Middle_suji_needs_both_outer_discards_for_low_risk()
    {
        var s = Snapshots.Closed14("123m456p789s234s5z6z");
        var seats = s.Seats.ToArray();

        seats[1] = seats[1] with
        {
            Riichi = true,
            Discards = Tiles.Parse("2m"),
        };
        var oneSide = s with { Seats = seats };
        var (oneSideLevel, _) = BeginnerDiscardAdvisor.EvaluateSafety(oneSide, Tiles.Parse("5m")[0]);

        seats[1] = seats[1] with
        {
            Riichi = true,
            Discards = Tiles.Parse("2m8m"),
        };
        var bothSides = s with { Seats = seats };
        var (bothSidesLevel, _) = BeginnerDiscardAdvisor.EvaluateSafety(bothSides, Tiles.Parse("5m")[0]);

        Assert.Equal(BeginnerSafetyLevel.Medium, oneSideLevel);
        Assert.Equal(BeginnerSafetyLevel.Low, bothSidesLevel);
    }
}
