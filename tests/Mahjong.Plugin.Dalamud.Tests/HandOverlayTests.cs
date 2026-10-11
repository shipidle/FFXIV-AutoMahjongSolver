using System.Numerics;
using Mahjong.Plugin.Dalamud.UI;

namespace Mahjong.Plugin.Dalamud.Tests;

public class HandOverlayTests
{
    private static (Vector2 Pos, Vector2 Size) R(float x, float y, float w = 40, float h = 64)
        => (new Vector2(x, y), new Vector2(w, h));

    [Fact]
    public void PickHandRow_chooses_exact_bottom_row_over_wall_row()
    {
        var candidates = new List<(Vector2 Pos, Vector2 Size)>();

        for (int i = 0; i < 14; i++)
            candidates.Add(R(i * 42, 100));

        for (int i = 0; i < 11; i++)
            candidates.Add(R(100 + i * 42, 500));

        var row = HandOverlay.PickHandRowFromCandidates(candidates, 11, out bool confident);

        Assert.NotNull(row);
        Assert.True(confident);
        Assert.Equal(11, row!.Count);
        Assert.All(row, r => Assert.True(r.Pos.Y >= 490));
    }

    [Fact]
    public void PickHandRow_marks_ambiguous_bottom_row_untrusted_instead_of_guessing()
    {
        var candidates = new List<(Vector2 Pos, Vector2 Size)>();
        for (int i = 0; i < 12; i++)
            candidates.Add(R(100 + i * 42, 500));

        var row = HandOverlay.PickHandRowFromCandidates(candidates, 11, out bool confident);

        Assert.NotNull(row);
        Assert.False(confident);
        Assert.Equal(12, row!.Count);
    }

    [Fact]
    public void PickHandRow_deduplicates_overlapping_nested_nodes()
    {
        var candidates = new List<(Vector2 Pos, Vector2 Size)>();
        for (int i = 0; i < 11; i++)
        {
            candidates.Add(R(100 + i * 42, 500));
            candidates.Add(R(101 + i * 42, 501, 38, 62));
        }

        var row = HandOverlay.PickHandRowFromCandidates(candidates, 11, out bool confident);

        Assert.NotNull(row);
        Assert.True(confident);
        Assert.Equal(11, row!.Count);
    }

    [Fact]
    public void Detached_draw_is_detected_from_last_visual_gap()
    {
        var rects = new List<(Vector2 Pos, Vector2 Size)>();
        for (int i = 0; i < 13; i++)
            rects.Add(R(i * 42, 500));
        rects.Add(R(13 * 42 + 18, 500));

        Assert.True(HandOverlay.TryDetermineLastVisualTileDetached(rects, out bool detached));
        Assert.True(detached);
    }

    [Fact]
    public void Ambiguous_last_gap_is_rejected_instead_of_guessed()
    {
        var rects = new List<(Vector2 Pos, Vector2 Size)>();
        for (int i = 0; i < 10; i++)
            rects.Add(R(i * 42, 500));

        // Normal gaps are 2px. Last gap is 7px: larger, but not enough evidence for a
        // detached draw at this tile width.
        rects.Add(R(10 * 42 + 5, 500));

        Assert.False(HandOverlay.TryDetermineLastVisualTileDetached(rects, out _));
    }

    [Fact]
    public void Integrated_sorted_hand_does_not_fake_detached_draw()
    {
        var rects = new List<(Vector2 Pos, Vector2 Size)>();
        for (int i = 0; i < 11; i++)
            rects.Add(R(i * 42, 500));

        Assert.True(HandOverlay.TryDetermineLastVisualTileDetached(rects, out bool detached));
        Assert.False(detached);
    }
}
