using System;
using System.Collections.Generic;
using Mahjong.Core;

namespace Mahjong.Plugin.Dalamud.GameState.Variants;

/// <summary>Pure helpers for decoding the addon hand-array. Scans the full array (post-call layouts park the claimed tile at slot 13 with [10..12] empty).</summary>
internal static class HandArrayDecoder
{
    /// <summary>34-id decode of a raw addon int; out <paramref name="isRed"/> when the raw matches the akadora aliases 5m*/5p*/5s*. Returns -1 for zero/unknown.</summary>
    public static int DecodeTileId(int raw, int textureBase, out bool isRed)
    {
        isRed = false;
        if (raw == 0)
            return -1;
        int idx = raw - textureBase;
        if (idx >= 0 && idx < Tile.Count34)
            return idx;
        switch (idx)
        {
            case 34: isRed = true; return 4;
            case 35: isRed = true; return 13;
            case 36: isRed = true; return 22;
            default: return -1;
        }
    }

    /// <summary>Distance in texture-IDs to hunt around the configured base; the only shift seen in the wild was 2 (issue #52), 8 covers a wider patch without inviting false matches.</summary>
    private const int BaseSearchRadius = 8;

    /// <summary>A frame below this many populated slots carries too little evidence to retune the base safely.</summary>
    private const int MinSlotsForRetune = 5;

    /// <summary>Populated (non-zero) slots that fail to decode at <paramref name="textureBase"/>.</summary>
    private static int CountUndecodable(ReadOnlySpan<int> rawSlots, int textureBase)
    {
        int bad = 0;
        for (int i = 0; i < rawSlots.Length; i++)
            if (rawSlots[i] != 0 && DecodeTileId(rawSlots[i], textureBase, out _) < 0)
                bad++;
        return bad;
    }

    /// <summary>Base to decode with: the configured one unless it drops populated slots, in which case the nearest base that decodes the whole array, so a client tile-ID shift (issue #52) self-heals instead of silently mis-reading every tile; <paramref name="shifted"/> flags a change.</summary>
    public static int ResolveTextureBase(ReadOnlySpan<int> rawSlots, int configuredBase, out bool shifted)
    {
        shifted = false;
        if (CountUndecodable(rawSlots, configuredBase) == 0)
            return configuredBase;

        int populated = 0;
        for (int i = 0; i < rawSlots.Length; i++)
            if (rawSlots[i] != 0)
                populated++;
        if (populated < MinSlotsForRetune)
            return configuredBase;

        // Nearest base wins; downward before upward at equal distance (every observed shift went down).
        for (int radius = 1; radius <= BaseSearchRadius; radius++)
        {
            if (CountUndecodable(rawSlots, configuredBase - radius) == 0)
            {
                shifted = true;
                return configuredBase - radius;
            }
            if (CountUndecodable(rawSlots, configuredBase + radius) == 0)
            {
                shifted = true;
                return configuredBase + radius;
            }
        }
        return configuredBase;
    }

    public static (List<Tile> Tiles, int Akadora) ReadHand(ReadOnlySpan<int> rawSlots, int textureBase)
    {
        var tiles = new List<Tile>(rawSlots.Length);
        int akadora = 0;
        for (int i = 0; i < rawSlots.Length; i++)
        {
            int tileId = DecodeTileId(rawSlots[i], textureBase, out bool isRed);
            if (tileId < 0)
                continue;
            tiles.Add(Tile.FromId(tileId));
            if (isRed)
                akadora++;
        }
        return (tiles, akadora);
    }

    /// <summary>Addon slot whose raw decodes to <paramref name="targetTileId"/>. Prefers slot 13 (last-drawn), else lowest match. -1 if not found.</summary>
    public static int FindAddonSlot(ReadOnlySpan<int> rawSlots, int textureBase, int targetTileId)
    {
        if (rawSlots.Length > 13
            && DecodeTileId(rawSlots[13], textureBase, out _) == targetTileId)
            return 13;
        for (int i = 0; i < rawSlots.Length; i++)
        {
            if (i == 13)
                continue;
            if (DecodeTileId(rawSlots[i], textureBase, out _) == targetTileId)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Same lookup as <see cref="FindAddonSlot"/> but, for a canonical 5 tile, prefers a
    /// non-red copy when one exists. This keeps aka dora in hand without changing Tile's
    /// compact 34-id representation.
    /// </summary>
    public static int FindAddonSlotPreservingAkadora(
        ReadOnlySpan<int> rawSlots,
        int textureBase,
        int targetTileId)
    {
        static bool IsCanonicalFive(int id) => id is 4 or 13 or 22;
        if (!IsCanonicalFive(targetTileId))
            return FindAddonSlot(rawSlots, textureBase, targetTileId);

        if (rawSlots.Length > 13)
        {
            int id = DecodeTileId(rawSlots[13], textureBase, out bool red);
            if (id == targetTileId && !red)
                return 13;
        }

        for (int i = 0; i < rawSlots.Length; i++)
        {
            if (i == 13)
                continue;
            int id = DecodeTileId(rawSlots[i], textureBase, out bool red);
            if (id == targetTileId && !red)
                return i;
        }

        // No normal copy exists; discarding the red five is unavoidable.
        return FindAddonSlot(rawSlots, textureBase, targetTileId);
    }

    /// <summary>
    /// Maps a tile kind to its left-to-right rendered hand position.
    /// When the last visual tile is detached, addon slot 13 is the drawn tile and stays
    /// at the far right. When it is not detached (notably immediately after a call), the
    /// rendered hand is sorted by tile kind even though addon slot 13 can hold a parked tile.
    /// </summary>
    public static int FindRenderedHandIndex(
        ReadOnlySpan<int> rawSlots,
        int textureBase,
        int targetTileId,
        bool lastVisualTileDetached)
    {
        var entries = new List<(int Slot, int TileId, bool Red)>(rawSlots.Length);
        for (int i = 0; i < rawSlots.Length; i++)
        {
            int id = DecodeTileId(rawSlots[i], textureBase, out bool red);
            if (id >= 0)
                entries.Add((i, id, red));
        }

        if (entries.Count == 0)
            return -1;

        if (lastVisualTileDetached)
        {
            entries.Sort((a, b) =>
            {
                if (a.Slot == 13 && b.Slot != 13) return 1;
                if (b.Slot == 13 && a.Slot != 13) return -1;
                return a.Slot.CompareTo(b.Slot);
            });
        }
        else
        {
            // Stable sort by tile kind. Equal tiles keep addon-slot order.
            entries.Sort((a, b) =>
            {
                int cmp = a.TileId.CompareTo(b.TileId);
                return cmp != 0 ? cmp : a.Slot.CompareTo(b.Slot);
            });
        }

        static bool IsCanonicalFive(int id) => id is 4 or 13 or 22;

        if (IsCanonicalFive(targetTileId))
        {
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].TileId == targetTileId && !entries[i].Red)
                    return i;
        }

        for (int i = 0; i < entries.Count; i++)
            if (entries[i].TileId == targetTileId)
                return i;

        return -1;
    }

    /// <summary>
    /// Converts an addon-array slot to the compressed hand-list index produced by
    /// <see cref="ReadHand"/>. Needed by overlays when sparse post-call arrays contain gaps.
    /// </summary>
    public static int HandIndexForAddonSlot(
        ReadOnlySpan<int> rawSlots,
        int textureBase,
        int addonSlot)
    {
        if (addonSlot < 0 || addonSlot >= rawSlots.Length)
            return -1;

        int handIndex = 0;
        for (int i = 0; i < rawSlots.Length; i++)
        {
            if (DecodeTileId(rawSlots[i], textureBase, out _) < 0)
                continue;
            if (i == addonSlot)
                return handIndex;
            handIndex++;
        }
        return -1;
    }
}
