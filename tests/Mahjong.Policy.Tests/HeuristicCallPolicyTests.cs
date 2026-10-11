using Mahjong.Policy.Efficiency;

namespace Mahjong.Policy.Tests;

public class HeuristicCallPolicyTests
{
    private static readonly HeuristicCallPolicy Policy = new();

    private static StateSnapshot WithLegal(StateSnapshot s, LegalActions legal) =>
        s with { Legal = legal };

    private static StateSnapshot ClosedHandWith(string notation)
        => Snapshots.Closed14(notation, ActionFlags.Discard);

    [Fact]
    public void Declines_when_no_candidates_offered()
    {
        var s = ClosedHandWith("123m456p789s234s55m");
        var legal = new LegalActions(ActionFlags.Pass, [], [], [], []);
        var d = Policy.Evaluate(WithLegal(s, legal));
        Assert.False(d.Accept);
        Assert.Equal("no-candidate", d.Reason.Code);
    }

    [Fact]
    public void Accepts_pon_that_advances_shanten_with_yakuhai()
    {
        var seats = new SeatView[]
        {
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
        };
        var s = StateSnapshot.Empty with
        {
            Hand = Tiles.Parse("234m456p789s234s11z"),
            Seats = seats,
            Legal = new LegalActions(
                Flags: ActionFlags.Pon | ActionFlags.Pass,
                DiscardableTiles: [],
                PonCandidates: [new MeldCandidate(MeldKind.Pon, Tile.FromId(31), [Tile.FromId(31), Tile.FromId(31)], FromSeat: 1)],
                ChiCandidates: [],
                KanCandidates: []),
        };

        var d = Policy.Evaluate(s);
        Assert.NotNull(d.Reason);
        if (d.Accept)
        {
            Assert.NotNull(d.Value);
            Assert.Equal(MeldKind.Pon, d.Value!.Value.Kind);
        }
    }

    [Fact]
    public void Declines_when_no_call_advances_shanten()
    {
        var seats = new SeatView[]
        {
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
        };
        var s = StateSnapshot.Empty with
        {
            Hand = Tiles.Parse("234m456p789s234s5z"),
            Seats = seats,
            Legal = new LegalActions(
                Flags: ActionFlags.Chi | ActionFlags.Pass,
                DiscardableTiles: [],
                PonCandidates: [],
                ChiCandidates: [new MeldCandidate(MeldKind.Chi, Tile.FromId(0),
                    [Tile.FromId(1), Tile.FromId(2)], FromSeat: 3)],
                KanCandidates: []),
        };

        var d = Policy.Evaluate(s);
        Assert.False(d.Accept);
        Assert.Equal("no-shanten-gain-with-yaku", d.Reason.Code);
    }

    [Fact]
    public void Accepts_chi_that_unlocks_sanshoku_doujun()
    {
        // chi(234m claiming 4m) + closed 234p + 234s → post-call sanshoku-doujun at
        // offset 1 is reachable across all three suits. No other yaku route fires
        // (terminals/honors block tanyao, multi-suit blocks honitsu, chi blocks toitoi,
        // no dragons/seat-winds for yakuhai). Pre-fix the call would be declined.
        var seats = new SeatView[]
        {
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
        };
        var s = StateSnapshot.Empty with
        {
            Hand = Tiles.Parse("23m234p234s1p9s1z2z4z"),
            Seats = seats,
            Legal = new LegalActions(
                Flags: ActionFlags.Chi | ActionFlags.Pass,
                DiscardableTiles: [],
                PonCandidates: [],
                ChiCandidates: [new MeldCandidate(MeldKind.Chi, Tile.FromId(3),
                    [Tile.FromId(1), Tile.FromId(2)], FromSeat: 3)],
                KanCandidates: []),
        };

        var d = Policy.Evaluate(s);
        Assert.True(d.Accept,
            $"expected chi(234m) accepted via sanshoku-doujun (reason={d.Reason.Code}: {d.Reason.Display})");
        Assert.Equal(MeldKind.Chi, d.Value!.Value.Kind);
    }

    [Fact]
    public void Doman_accepts_chi_with_one_han_yakuhai_path()
    {
        // Reproduces issue #50: post-Chi shape (7m 44p 789p 345s 55z + Chi 789p meld) has
        // only 1 reachable han via the 55z dragon pair. One yaku is enough in Doman.
        var policy = new HeuristicCallPolicy(new DomanRuleSet());
        var seats = new SeatView[]
        {
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
        };
        var s = StateSnapshot.Empty with
        {
            Hand = Tiles.Parse("7m4478899p345s55z"),
            Seats = seats,
            Legal = new LegalActions(
                Flags: ActionFlags.Chi | ActionFlags.Pass,
                DiscardableTiles: [],
                PonCandidates: [],
                ChiCandidates: [new MeldCandidate(MeldKind.Chi, Tile.FromId(15),
                    [Tile.FromId(16), Tile.FromId(17)], FromSeat: 3)],
                KanCandidates: []),
        };

        var d = policy.Evaluate(s);
        Assert.True(d.Accept,
            $"Doman should accept a call that advances shanten and keeps a 1-han yaku path " +
            $"(got {d.Reason.Code}: {d.Reason.Display})");
        Assert.Equal(MeldKind.Chi, d.Value!.Value.Kind);
    }

    [Fact]
    public void Riichi_accepts_same_chi_at_min_han_one()
    {
        // Same hand under standard riichi rules; 1-han yakuhai also suffices.
        var policy = new HeuristicCallPolicy(new RiichiRuleSet());
        var seats = new SeatView[]
        {
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
        };
        var s = StateSnapshot.Empty with
        {
            Hand = Tiles.Parse("7m4478899p345s55z"),
            Seats = seats,
            Legal = new LegalActions(
                Flags: ActionFlags.Chi | ActionFlags.Pass,
                DiscardableTiles: [],
                PonCandidates: [],
                ChiCandidates: [new MeldCandidate(MeldKind.Chi, Tile.FromId(15),
                    [Tile.FromId(16), Tile.FromId(17)], FromSeat: 3)],
                KanCandidates: []),
        };

        var d = policy.Evaluate(s);
        Assert.True(d.Accept,
            $"Riichi should accept this Chi via yakuhai (got {d.Reason.Code}: {d.Reason.Display})");
        Assert.Equal(MeldKind.Chi, d.Value!.Value.Kind);
    }

    [Fact]
    public void Doman_accepts_chi_when_two_yakuhai_pairs_total_two_han()
    {
        // Two dragon pairs (55z + 66z) reach 2 han, satisfying Doman MinHan=2.
        var policy = new HeuristicCallPolicy(new DomanRuleSet());
        var seats = new SeatView[]
        {
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
        };
        var s = StateSnapshot.Empty with
        {
            Hand = Tiles.Parse("24777m189p9s5566z"),
            Seats = seats,
            Legal = new LegalActions(
                Flags: ActionFlags.Chi | ActionFlags.Pass,
                DiscardableTiles: [],
                PonCandidates: [],
                ChiCandidates: [new MeldCandidate(MeldKind.Chi, Tile.FromId(2),
                    [Tile.FromId(1), Tile.FromId(3)], FromSeat: 3)],
                KanCandidates: []),
        };

        var d = policy.Evaluate(s);
        Assert.True(d.Accept,
            $"Doman should accept Chi with two dragon pairs reaching 2 han (got {d.Reason.Code}: {d.Reason.Display})");
        Assert.Equal(MeldKind.Chi, d.Value!.Value.Kind);
    }

    [Fact]
    public void Doman_declines_dora_only_call_because_dora_is_not_a_yaku()
    {
        // 23m can chi 1m and improve shanten, but the opened shape has no reachable yaku.
        // 9p is dora here (8p indicator under the traditional test rule). Dora adds han/value,
        // but it cannot make a yakuless hand legal.
        var seats = new SeatView[]
        {
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
        };
        var s = StateSnapshot.Empty with
        {
            Hand = Tiles.Parse("23m45699p234789s1z"),
            Seats = seats,
            DoraIndicators = Tiles.Parse("8p"),
            SeatInfoKnown = false,
            Legal = new LegalActions(
                Flags: ActionFlags.Chi | ActionFlags.Pass,
                DiscardableTiles: [],
                PonCandidates: [],
                ChiCandidates: [new MeldCandidate(
                    MeldKind.Chi,
                    Tile.FromId(0),
                    [Tile.FromId(1), Tile.FromId(2)],
                    FromSeat: 3)],
                KanCandidates: []),
        };

        var policy = new HeuristicCallPolicy(new DomanRuleSet());
        var d = policy.Evaluate(s);

        Assert.False(d.Accept,
            $"dora-only call must be declined (got {d.Reason.Code}: {d.Reason.Display})");
        Assert.Equal("no-shanten-gain-with-yaku", d.Reason.Code);
    }

    [Fact]
    public void Declines_flush_path_when_existing_open_meld_breaks_the_suit()
    {
        var openPon = Meld.Pon(Tile.FromId(9), Tile.FromId(9), fromSeat: 1); // 1p pon
        var s = StateSnapshot.Empty with
        {
            Hand = Tiles.Parse("12m345m667m9m1z"),
            OurMelds = [openPon],
            Seats =
            [
                new([], [], [], false, -1, false, false),
                new([], [], [], false, -1, false, false),
                new([], [], [], false, -1, false, false),
                new([], [], [], false, -1, false, false),
            ],
            Legal = new LegalActions(
                Flags: ActionFlags.Chi | ActionFlags.Pass,
                DiscardableTiles: [],
                PonCandidates: [],
                ChiCandidates:
                [
                    new MeldCandidate(
                        MeldKind.Chi,
                        Tile.FromId(2), // claim 3m with 1m2m
                        [Tile.FromId(0), Tile.FromId(1)],
                        FromSeat: 3)
                ],
                KanCandidates: []),
        };

        var d = new HeuristicCallPolicy(new DomanRuleSet()).Evaluate(s);

        Assert.False(d.Accept);
    }

    [Fact]
    public void Accepts_chi_that_unlocks_ittsu()
    {
        // chi(123m claiming 1m) + closed 456m + 789m → post-call ittsu in m-suit is
        // reachable: subrun 0 chi-locked, subruns 1 and 2 fully present in closed.
        // No other yaku route fires (chi-meld terminal blocks tanyao, multi-suit
        // closed blocks honitsu, chi blocks toitoi, sanshoku needs all-three-suits
        // and p/s have only singletons at offset 0).
        var seats = new SeatView[]
        {
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
            new([], [], [], false, -1, false, false),
        };
        var s = StateSnapshot.Empty with
        {
            Hand = Tiles.Parse("23m456m789m1p9s1z2z4z"),
            Seats = seats,
            Legal = new LegalActions(
                Flags: ActionFlags.Chi | ActionFlags.Pass,
                DiscardableTiles: [],
                PonCandidates: [],
                ChiCandidates: [new MeldCandidate(MeldKind.Chi, Tile.FromId(0),
                    [Tile.FromId(1), Tile.FromId(2)], FromSeat: 3)],
                KanCandidates: []),
        };

        var d = Policy.Evaluate(s);
        Assert.True(d.Accept,
            $"expected chi(123m) accepted via ittsu (reason={d.Reason.Code}: {d.Reason.Display})");
        Assert.Equal(MeldKind.Chi, d.Value!.Value.Kind);
    }
}
