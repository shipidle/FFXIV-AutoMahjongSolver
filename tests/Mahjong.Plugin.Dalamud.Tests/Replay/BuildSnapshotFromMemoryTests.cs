using System.IO;
using Mahjong.Core;
using Mahjong.Engine;
using Mahjong.Plugin.Dalamud.GameState;
using Mahjong.Plugin.Dalamud.GameState.Variants;
using Mahjong.Plugin.Dalamud.Tests.Stubs;
using Mahjong.Plugin.Game.Variants;

namespace Mahjong.Plugin.Dalamud.Tests.Replay;

/// <summary>Inline-builder smoke tests for the pure BuildSnapshotFromMemory entry. Fixture-replay JSON tests live in <see cref="ReplayFixtureTests"/>.</summary>
public class BuildSnapshotFromMemoryTests
{
    private static readonly LayoutProfile EmjProfile = LoadProfile("emj.json");

    private static LayoutProfile LoadProfile(string fileName) =>
        JsonLayoutProfileLoader.Load(Path.Combine(TestPaths.LayoutsDir, fileName));

    private static (BaseEmjVariant Variant, VariantReadContext Ctx) MakeVariant(LayoutProfile profile)
    {
        var variant = new BaseEmjVariant(profile, new StubPluginLog(), Path.GetTempPath());
        var ctx = new VariantReadContext(new MeldTracker(), EventLogger: null);
        return (variant, ctx);
    }

    [Fact]
    public void State30_our_turn_discard_emj()
    {
        var memory = new AddonMemoryBuilder(EmjProfile)
            .WithScores(25000, 25000, 25000, 25000)
            .WithDiscardCounts(0, 0, 0, 0)
            .WithHand("1234m456p789s1234z")
            .WithDoraIndicator("5z")
            .Build();

        var atkValues = new[] { AtkValueRecord.OfInt(30), AtkValueRecord.OfInt(0) };

        var (variant, ctx) = MakeVariant(EmjProfile);
        var snap = variant.BuildSnapshotFromMemory(
            memory, atkValues, ctx, callModalVisible: false);

        Assert.NotNull(snap);
        Assert.Equal(30, snap!.AddonStateCode);
        Assert.Equal(14, snap.Hand.Count);
        Assert.Equal(ActionFlags.Discard, snap.Legal.Flags);
        Assert.Equal(25000, snap.Scores[0]);
        Assert.Equal(70, snap.WallRemaining);
        Assert.Equal(0, snap.AkaDora);
        Assert.Empty(snap.OurMelds);
        Assert.False(snap.SeatInfoKnown);
    }

    [Fact]
    public void Implausible_scores_return_null()
    {
        var memory = new AddonMemoryBuilder(EmjProfile)
            .WithScores(99999999, 25000, 25000, 25000)
            .WithHand("1234m456p789s1234z")
            .Build();

        var (variant, ctx) = MakeVariant(EmjProfile);
        var snap = variant.BuildSnapshotFromMemory(
            memory, [AtkValueRecord.OfInt(30)], ctx, callModalVisible: false);

        Assert.Null(snap);
    }

    [Fact]
    public void Call_modal_invisible_at_state15_returns_no_call_actions()
    {
        // State-15 with modal NOT visible falls through to the our-turn-or-none branch — here, hand.Count=13 % 3 == 1, so LegalActions.None.
        var memory = new AddonMemoryBuilder(EmjProfile)
            .WithScores(25000, 25000, 25000, 25000)
            .WithHand("123m456p789s123z")
            .Build();

        var atkValues = new[] { AtkValueRecord.OfInt(15), AtkValueRecord.OfInt(0) };

        var (variant, ctx) = MakeVariant(EmjProfile);
        var snap = variant.BuildSnapshotFromMemory(
            memory, atkValues, ctx, callModalVisible: false);

        Assert.NotNull(snap);
        Assert.Equal(15, snap!.AddonStateCode);
        Assert.Equal(ActionFlags.None, snap.Legal.Flags);
    }

    [Fact]
    public void Call_modal_visible_at_state15_with_pon_label_emits_pon_flag()
    {
        var memory = new AddonMemoryBuilder(EmjProfile)
            .WithScores(25000, 25000, 25000, 25000)
            .WithHand("55m123p456s11234z")  // 13 tiles incl. a 5m5m pair to claim
            .Build();

        // Build an atkValues vector with the state code + a "Pon" button label + the claimed-tile pair in [16..21].
        var atkValues = new AtkValueRecord[22];
        atkValues[EmjProfile.AtkValues.StateCode] = AtkValueRecord.OfInt(15);
        atkValues[2] = AtkValueRecord.OfString("Pon");
        atkValues[3] = AtkValueRecord.OfString("Pass");
        // pon claim window: two copies of raw=textureBase+4 (5m) at slots 16, 17
        int claimed5mRaw = EmjProfile.TileTextureBase + 4;
        atkValues[16] = AtkValueRecord.OfInt(claimed5mRaw);
        atkValues[17] = AtkValueRecord.OfInt(claimed5mRaw);

        var (variant, ctx) = MakeVariant(EmjProfile);
        var snap = variant.BuildSnapshotFromMemory(
            memory, atkValues, ctx, callModalVisible: true);

        Assert.NotNull(snap);
        Assert.Equal(15, snap!.AddonStateCode);
        Assert.True((snap.Legal.Flags & ActionFlags.Pon) != 0);
        Assert.True((snap.Legal.Flags & ActionFlags.Pass) != 0);
        Assert.NotEmpty(snap.Legal.PonCandidates);
        Assert.Equal(4, snap.Legal.PonCandidates[0].ClaimedTile.Id);
    }

    [Fact]
    public void Exact_1000_point_drop_mid_hand_infers_riichi_and_new_hand_resets_it()
    {
        var (variant, ctx) = MakeVariant(EmjProfile);

        var before = new AddonMemoryBuilder(EmjProfile)
            .WithScores(25000, 25000, 25000, 25000)
            .WithDiscardCounts(2, 3, 2, 2)
            .WithHand("1234m456p789s1234z")
            .Build();

        var snap1 = variant.BuildSnapshotFromMemory(
            before, [AtkValueRecord.OfInt(30)], ctx, callModalVisible: false);
        Assert.NotNull(snap1);
        Assert.False(snap1!.Seats[1].Riichi);

        var afterRiichi = new AddonMemoryBuilder(EmjProfile)
            .WithScores(25000, 24000, 25000, 25000)
            .WithDiscardCounts(2, 4, 2, 2)
            .WithHand("1234m456p789s1234z")
            .Build();

        var snap2 = variant.BuildSnapshotFromMemory(
            afterRiichi, [AtkValueRecord.OfInt(30)], ctx, callModalVisible: false);
        Assert.NotNull(snap2);
        Assert.True(snap2!.Seats[1].Riichi);

        var nextHand = new AddonMemoryBuilder(EmjProfile)
            .WithScores(25000, 24000, 25000, 25000)
            .WithDiscardCounts(0, 0, 0, 0)
            .WithHand("1234m456p789s1234z")
            .Build();

        var snap3 = variant.BuildSnapshotFromMemory(
            nextHand, [AtkValueRecord.OfInt(30)], ctx, callModalVisible: false);
        Assert.NotNull(snap3);
        Assert.False(snap3!.Seats[1].Riichi);
    }

    [Fact]
    public void Akadora_red_5m_in_hand_increments_count()
    {
        var memory = new AddonMemoryBuilder(EmjProfile)
            .WithScores(25000, 25000, 25000, 25000)
            .WithHand("1234m456p789s1234z")
            .Build();
        // Overwrite slot 0 with the red-5m raw alias (idx 34 = textureBase + 34).
        int redRaw = EmjProfile.TileTextureBase + 34;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
            memory.AsSpan(EmjProfile.Offsets.HandArrayStart, 4), redRaw);

        var (variant, ctx) = MakeVariant(EmjProfile);
        var snap = variant.BuildSnapshotFromMemory(
            memory, [AtkValueRecord.OfInt(30)], ctx, callModalVisible: false);

        Assert.NotNull(snap);
        Assert.Equal(1, snap!.AkaDora);
        // Tile id 4 = 5m
        Assert.Equal(4, snap.Hand[0].Id);
    }
}
