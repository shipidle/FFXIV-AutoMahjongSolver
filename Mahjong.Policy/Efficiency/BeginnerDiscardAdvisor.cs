using Mahjong.Engine;

namespace Mahjong.Policy.Efficiency;

public enum BeginnerSafetyLevel : byte
{
    NoThreat = 0,
    Safe = 1,
    Low = 2,
    Medium = 3,
    High = 4,
}

public sealed record BeginnerDiscardRecommendation(
    Tile Discard,
    int ShantenAfter,
    IReadOnlyList<Tile> AcceptedKinds,
    int LiveUkeire,
    double YakuPotential,
    int DoraRetained,
    int YakuhaiRetained,
    BeginnerSafetyLevel Safety,
    string SafetyReason,
    bool DefenseMode);

/// <summary>
/// A deliberately conservative, explainable discard advisor for beginners.
/// It uses lexicographic priorities instead of the production weighted score:
/// shanten first, then live ukeire, then hand value and finally safety.
/// Against riichi while 3+ shanten away, it switches to a simple fold-first rule.
/// </summary>
public static class BeginnerDiscardAdvisor
{
    public static BeginnerDiscardRecommendation? Recommend(StateSnapshot state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.Legal.Can(ActionFlags.Discard))
            return null;

        var hand = Hand.FromTiles(state.Hand, state.OurMelds);
        if (hand.TotalShantenTileCount != 14)
            return null;

        var wall = BuildVisibleWall(state);
        var entries = UkeireEnumerator.Enumerate(hand, wall);
        if (entries.Length == 0)
            return null;

        // Reuse the existing scorer only as a source of value metadata.
        // Its aggregate Score is intentionally NOT used for beginner ranking.
        var valueRows = DiscardScorer.Score(state, wall: wall)
            .ToDictionary(x => x.Discard.Id);

        bool anyRiichi = HasRiichiThreat(state);
        int bestReachableShanten = entries.Min(x => x.ShantenAfter);
        bool defenseMode = anyRiichi && bestReachableShanten >= 3;

        BeginnerDiscardRecommendation? best = null;

        foreach (var e in entries)
        {
            valueRows.TryGetValue(e.Discard.Id, out var value);
            var (safety, safetyReason) = EvaluateSafety(state, e.Discard, wall);

            var candidate = new BeginnerDiscardRecommendation(
                Discard: e.Discard,
                ShantenAfter: e.ShantenAfter,
                AcceptedKinds: e.AcceptedKinds,
                LiveUkeire: e.WeightedCount,
                YakuPotential: value.YakuPotential,
                DoraRetained: value.DoraRetained,
                YakuhaiRetained: value.YakuhaiRetained,
                Safety: safety,
                SafetyReason: safetyReason,
                DefenseMode: defenseMode);

            if (best is null || IsBetter(candidate, best, defenseMode))
                best = candidate;
        }

        return best;
    }

    private static bool IsBetter(
        BeginnerDiscardRecommendation a,
        BeginnerDiscardRecommendation b,
        bool defenseMode)
    {
        if (defenseMode)
        {
            int sa = SafetyRank(a.Safety);
            int sb = SafetyRank(b.Safety);
            if (sa != sb) return sa < sb;
            if (a.ShantenAfter != b.ShantenAfter) return a.ShantenAfter < b.ShantenAfter;
        }
        else
        {
            if (a.ShantenAfter != b.ShantenAfter) return a.ShantenAfter < b.ShantenAfter;
            if (a.LiveUkeire != b.LiveUkeire) return a.LiveUkeire > b.LiveUkeire;
            if (a.AcceptedKinds.Count != b.AcceptedKinds.Count) return a.AcceptedKinds.Count > b.AcceptedKinds.Count;
        }

        int yakuCmp = a.YakuPotential.CompareTo(b.YakuPotential);
        if (yakuCmp != 0) return yakuCmp > 0;

        int aValue = a.DoraRetained + a.YakuhaiRetained;
        int bValue = b.DoraRetained + b.YakuhaiRetained;
        if (aValue != bValue) return aValue > bValue;

        if (!defenseMode)
        {
            int sa = SafetyRank(a.Safety);
            int sb = SafetyRank(b.Safety);
            if (sa != sb) return sa < sb;
        }

        return a.Discard.Id < b.Discard.Id;
    }

    private static int SafetyRank(BeginnerSafetyLevel level) => level switch
    {
        BeginnerSafetyLevel.Safe => 0,
        BeginnerSafetyLevel.Low => 1,
        BeginnerSafetyLevel.Medium => 2,
        BeginnerSafetyLevel.High => 3,
        _ => 4,
    };

    private static bool HasRiichiThreat(StateSnapshot state)
    {
        for (int seat = 0; seat < state.Seats.Count; seat++)
            if (seat != state.OurSeat && state.Seats[seat].Riichi)
                return true;
        return false;
    }

    public static Wall BuildVisibleWall(StateSnapshot state)
    {
        var seen = new int[Tile.Count34];

        static void Add(int[] counts, Tile tile)
        {
            if (counts[tile.Id] < Tile.CopiesPerKind)
                counts[tile.Id]++;
        }

        foreach (var t in state.Hand)
            Add(seen, t);

        foreach (var m in state.OurMelds)
            foreach (var t in m.Tiles)
                Add(seen, t);

        for (int seat = 0; seat < state.Seats.Count; seat++)
        {
            foreach (var t in state.Seats[seat].Discards)
                Add(seen, t);

            // OurMelds is the authoritative self copy; avoid counting it twice.
            if (seat == state.OurSeat)
                continue;

            foreach (var m in state.Seats[seat].Melds)
                foreach (var t in m.Tiles)
                    Add(seen, t);
        }

        foreach (var t in state.DoraIndicators)
            Add(seen, t);

        var wall = new Wall();
        wall.ObserveCounts(seen);
        return wall;
    }

    public static (BeginnerSafetyLevel Level, string Reason) EvaluateSafety(
        StateSnapshot state,
        Tile tile,
        Wall? wall = null)
    {
        wall ??= BuildVisibleWall(state);

        var threatLevels = new List<BeginnerSafetyLevel>(3);
        for (int seat = 0; seat < state.Seats.Count; seat++)
        {
            if (seat == state.OurSeat || !state.Seats[seat].Riichi)
                continue;

            threatLevels.Add(EvaluateAgainstRiichi(state.Seats[seat], tile, wall));
        }

        if (threatLevels.Count == 0)
            return (BeginnerSafetyLevel.NoThreat, "현재 리치한 상대가 없음.");

        var worst = threatLevels.Max();

        string reason = worst switch
        {
            BeginnerSafetyLevel.Safe =>
                "현재 확인 가능한 리치 상대 모두에게 매우 안전한 패임.",
            BeginnerSafetyLevel.Low =>
                "상대 버림패와 보이는 패 수를 보면 비교적 안전한 편이지만 100% 안전한 건 아님.",
            BeginnerSafetyLevel.Medium =>
                "일부 안전 근거는 있지만 다른 대기 모양에는 맞을 수 있음.",
            _ =>
                "뚜렷한 안전 근거가 없어 리치 상대에게 맞을 가능성을 무시하기 어려움.",
        };

        return (worst, reason);
    }

    private static BeginnerSafetyLevel EvaluateAgainstRiichi(
        SeatView opponent,
        Tile tile,
        Wall wall)
    {
        // Genbutsu: the opponent has already discarded this exact tile.
        if (Contains(opponent.Discards, tile.Id))
            return BeginnerSafetyLevel.Safe;

        if (tile.IsHonor)
        {
            int live = wall.LiveOf(tile);
            return live switch
            {
                <= 0 => BeginnerSafetyLevel.Safe,
                1 => BeginnerSafetyLevel.Low,
                2 => BeginnerSafetyLevel.Medium,
                _ => BeginnerSafetyLevel.High,
            };
        }

        int pos = tile.Id % TileIds.SuitSize;
        int suitBase = (tile.Id / TileIds.SuitSize) * TileIds.SuitSize;

        bool lowerSuji = pos >= 3 && Contains(opponent.Discards, suitBase + pos - 3);
        bool upperSuji = pos <= 5 && Contains(opponent.Discards, suitBase + pos + 3);

        if (pos <= 2 && upperSuji)
            return BeginnerSafetyLevel.Low;
        if (pos >= 6 && lowerSuji)
            return BeginnerSafetyLevel.Low;
        if (pos is >= 3 and <= 5)
        {
            if (lowerSuji && upperSuji)
                return BeginnerSafetyLevel.Low;
            if (lowerSuji || upperSuji)
                return BeginnerSafetyLevel.Medium;
        }

        if (HasKabeSupport(tile, wall))
            return BeginnerSafetyLevel.Medium;

        return BeginnerSafetyLevel.High;
    }

    private static bool HasKabeSupport(Tile tile, Wall wall)
    {
        if (tile.IsHonor)
            return false;

        int pos = tile.Id % TileIds.SuitSize;
        int suitBase = (tile.Id / TileIds.SuitSize) * TileIds.SuitSize;

        bool leftDead = pos > 0 && wall.LiveOf(suitBase + pos - 1) == 0;
        bool rightDead = pos < TileIds.SuitSize - 1 && wall.LiveOf(suitBase + pos + 1) == 0;
        return leftDead || rightDead;
    }

    private static bool Contains(IReadOnlyList<Tile> tiles, int tileId)
    {
        foreach (var t in tiles)
            if (t.Id == tileId)
                return true;
        return false;
    }
}
