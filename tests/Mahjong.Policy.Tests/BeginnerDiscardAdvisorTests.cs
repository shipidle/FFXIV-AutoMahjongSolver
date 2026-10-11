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
    public void Random_closed_hands_never_choose_worse_shanten_or_ukeire()
    {
        var rng = new Random(20261011);

        for (int sample = 0; sample < 500; sample++)
        {
            var counts = new int[Tile.Count34];
            var tiles = new List<Tile>(14);
            while (tiles.Count < 14)
            {
                int id = rng.Next(Tile.Count34);
                if (counts[id] >= Tile.CopiesPerKind)
                    continue;
                counts[id]++;
                tiles.Add(Tile.FromId(id));
            }

            var state = StateSnapshot.Empty with
            {
                Hand = tiles,
                Legal = new LegalActions(
                    ActionFlags.Discard,
                    [],
                    [],
                    [],
                    []),
            };

            var rec = BeginnerDiscardAdvisor.Recommend(state);
            if (rec is null)
                continue;

            var hand = Hand.FromTiles(state.Hand, state.OurMelds);
            var wall = BeginnerDiscardAdvisor.BuildVisibleWall(state);
            var entries = UkeireEnumerator.Enumerate(hand, wall);
            if (entries.Length == 0)
                continue;

            int bestShanten = entries.Min(x => x.ShantenAfter);
            int bestUkeire = entries
                .Where(x => x.ShantenAfter == bestShanten)
                .Max(x => x.WeightedCount);

            Assert.Equal(bestShanten, rec.ShantenAfter);
            Assert.Equal(bestUkeire, rec.LiveUkeire);
        }
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
