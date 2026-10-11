using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Mahjong.Engine;
using Mahjong.Plugin.Dalamud.Actions;
using Mahjong.Plugin.Dalamud.GameState;
using Mahjong.Plugin.Dalamud.GameState.Variants;
using Mahjong.Policy;
using Mahjong.Policy.Efficiency;

namespace Mahjong.Plugin.Dalamud.UI;

/// <summary>Locates hand tiles by geometry — walk visible nodes, cluster by Y, take the tightest horizontal row of expected length.</summary>
public sealed class HandOverlay : IDisposable
{
    private const float MinTileWidth = 28f;
    private const float MaxTileWidth = 120f;
    private const float MinTileHeight = 45f;
    private const float MaxTileHeight = 160f;

    private const float MaxRowYSpread = 12f;

    private readonly Plugin plugin;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly MahjongAddon addon;
    private bool disposed;

    /// <summary>Dev console toggle: outline every detected tile rect, not just the picked slot.</summary>
    public bool DebugDrawAllRects { get; set; }

    public HandOverlay(Plugin plugin, IDalamudPluginInterface pluginInterface, MahjongAddon addon)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(pluginInterface);
        ArgumentNullException.ThrowIfNull(addon);
        this.plugin = plugin;
        this.pluginInterface = pluginInterface;
        this.addon = addon;
        pluginInterface.UiBuilder.Draw += Draw;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        pluginInterface.UiBuilder.Draw -= Draw;
    }

    private unsafe void Draw()
    {
        var cfg = plugin.Configuration;
        bool prodEnabled = cfg.TosAccepted && cfg.ShowInGameHighlight && cfg.AutomationArmed && cfg.SuggestionOnly;
        if (!DebugDrawAllRects && !prodEnabled)
            return;

        if (!addon.TryGet(out var unit, out _))
            return;
        if (!unit->IsVisible)
            return;

        var viewportOffset = ImGui.GetMainViewport().Pos;
        uint meldContainer = plugin.AddonReader.ActiveLayout?.NodeIds.MeldContainer ?? 0u;
        var candidates = CollectTileCandidates(unit, meldContainer);

        if (DebugDrawAllRects)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                var r = candidates[i];
                r.Pos += viewportOffset;
                DrawDebugOutline(r, i);
            }
        }

        if (!prodEnabled)
            return;

        var snap = plugin.Aggregator.Latest;
        if (snap is null || snap.Hand.Count < 2)
            return;

        var rects = PickHandRowFromCandidates(candidates, snap.Hand.Count, out bool rowConfident);
        if (rects is null)
            return;

        var choice = plugin.Aggregator.LastChoice;
        Tile? suggested = choice?.DiscardTile;
        if (suggested is null)
            return;

        int slot = -1;
        bool mappingConfident = rowConfident;
        var raw = plugin.AddonReader.DumpHandArrayRaw();
        var layout = plugin.AddonReader.ActiveLayout;

        if (raw is null || layout is null)
        {
            mappingConfident = false;
        }
        else
        {
            int textureBase = HandArrayDecoder.ResolveTextureBase(raw, layout.TileTextureBase, out _);
            bool detachedDraw = IsLastVisualTileDetached(rects);

            // When a red and normal 5 of the same suit are integrated into the sorted row,
            // their relative visual order is not exposed by the addon array. Do not guess.
            if (!detachedDraw && HasMixedRedAndNormalFive(raw, textureBase, suggested.Value.Id))
            {
                mappingConfident = false;
            }
            else
            {
                slot = HandArrayDecoder.FindRenderedHandIndex(
                    raw,
                    textureBase,
                    suggested.Value.Id,
                    detachedDraw);

                if (slot < 0 || slot >= rects.Count)
                    mappingConfident = false;
            }
        }

        if (!mappingConfident)
        {
            DrawFallbackHint(suggested.Value, rects, viewportOffset);
            return;
        }

        var rect = rects[slot];
        rect.Pos += viewportOffset;
        bool isDrawnTile = IsLastVisualTileDetached(rects) && slot == rects.Count - 1;

        var color = (isDrawnTile ? cfg.HighlightColorTsumogiri : cfg.HighlightColorDiscard).ToVector3();
        float intensity = Math.Clamp(cfg.HighlightIntensity, 0.4f, 1.6f);
        var dl = ImGui.GetForegroundDrawList();

        switch (cfg.HighlightStyle)
        {
            case HighlightStyle.Arrow:
                DrawHighlightArrow(dl, rect, color, intensity, isDrawnTile);
                break;
            default:
                DrawHighlightNeonGlow(dl, rect, color, intensity);
                break;
        }
    }

    private static void DrawDebugOutline((Vector2 Pos, Vector2 Size) rect, int index)
    {
        var dl = ImGui.GetForegroundDrawList();
        var min = rect.Pos - new Vector2(1, 1);
        var max = rect.Pos + rect.Size + new Vector2(1, 1);
        dl.AddRect(min, max, Theme.Pack(Theme.Info, 0.8f), 2f, ImDrawFlags.None, 1.5f);
        dl.AddText(new Vector2(min.X + 2, min.Y + 1), Theme.Pack(Theme.Info), index.ToString());
    }

    /// <summary>Visible tile-sized nodes in NodeList order, excluding the meld subtree (<paramref name="meldContainerId"/>). Melded tiles render on the hand's row but are not part of the concealed hand, so leaving them in shifted the highlight after a call (#53). <paramref name="meldContainerId"/> 0 disables the exclusion.</summary>
    private static unsafe List<(Vector2 Pos, Vector2 Size)> CollectTileCandidates(AtkUnitBase* unit, uint meldContainerId)
    {
        // Parent-chain walk already includes root position and scale — do NOT add unit->X/Y or multiply by unit->Scale on top.
        var result = new List<(Vector2 Pos, Vector2 Size)>(32);
        var uld = unit->UldManager;
        if (uld.NodeList == null || uld.NodeListCount <= 0)
            return result;

        for (int i = 0; i < uld.NodeListCount; i++)
        {
            var n = uld.NodeList[i];
            if (n == null || !n->IsVisible())
                continue;
            if (meldContainerId != 0 && HasAncestorId(n, meldContainerId))
                continue;

            float w = n->Width;
            float h = n->Height;
            if (w < MinTileWidth || w > MaxTileWidth)
                continue;
            if (h < MinTileHeight || h > MaxTileHeight)
                continue;
            if (w > h)
                continue;

            AbsolutePosition(n, out float nx, out float ny, out float sx, out float sy);
            result.Add((new Vector2(nx, ny), new Vector2(w * sx, h * sy)));
        }

        return result;
    }

    /// <summary>
    /// Finds the bottom-most tile row. Unlike the old sliding-window approach, this first
    /// removes overlapping duplicate nodes and then requires the selected row to contain
    /// exactly the expected number of concealed tiles. If the row is ambiguous, callers
    /// must not guess a tile position.
    /// </summary>
    internal static List<(Vector2 Pos, Vector2 Size)>? PickHandRowFromCandidates(
        List<(Vector2 Pos, Vector2 Size)> candidates,
        int expected,
        out bool confident)
    {
        confident = false;
        if (expected <= 0 || candidates.Count < expected)
            return null;

        var deduped = DeduplicateOverlappingRects(candidates);
        if (deduped.Count < expected)
            return null;

        deduped.Sort((a, b) => CenterY(a).CompareTo(CenterY(b)));

        // Build Y-clusters, then inspect them from bottom to top. The player's concealed
        // hand is the lowest row on screen after the meld subtree has been excluded.
        var clusters = new List<List<(Vector2 Pos, Vector2 Size)>>();
        foreach (var rect in deduped)
        {
            float cy = CenterY(rect);
            List<(Vector2 Pos, Vector2 Size)>? cluster = null;
            for (int i = clusters.Count - 1; i >= 0; i--)
            {
                if (Math.Abs(CenterY(clusters[i][0]) - cy) <= MaxRowYSpread)
                {
                    cluster = clusters[i];
                    break;
                }
            }

            if (cluster is null)
            {
                cluster = new List<(Vector2 Pos, Vector2 Size)>();
                clusters.Add(cluster);
            }
            cluster.Add(rect);
        }

        clusters.Sort((a, b) => CenterY(a[0]).CompareTo(CenterY(b[0])));

        for (int ci = clusters.Count - 1; ci >= 0; ci--)
        {
            var row = clusters[ci];
            if (row.Count < expected)
                continue;

            row.Sort((a, b) => a.Pos.X.CompareTo(b.Pos.X));

            if (row.Count == expected)
            {
                confident = true;
                return row;
            }

            // More tile-shaped nodes than the concealed hand means geometry alone cannot
            // prove which subset is the real hand. Return the row for text placement, but
            // deliberately mark it untrusted so no specific tile is highlighted.
            return row;
        }

        return null;
    }

    private static List<(Vector2 Pos, Vector2 Size)> DeduplicateOverlappingRects(
        List<(Vector2 Pos, Vector2 Size)> candidates)
    {
        var ordered = new List<(Vector2 Pos, Vector2 Size)>(candidates);
        ordered.Sort((a, b) => (b.Size.X * b.Size.Y).CompareTo(a.Size.X * a.Size.Y));

        var kept = new List<(Vector2 Pos, Vector2 Size)>(ordered.Count);
        foreach (var rect in ordered)
        {
            bool duplicate = false;
            foreach (var existing in kept)
            {
                if (OverlapRatio(rect, existing) >= 0.80f)
                {
                    duplicate = true;
                    break;
                }
            }
            if (!duplicate)
                kept.Add(rect);
        }
        return kept;
    }

    private static float OverlapRatio(
        (Vector2 Pos, Vector2 Size) a,
        (Vector2 Pos, Vector2 Size) b)
    {
        float left = Math.Max(a.Pos.X, b.Pos.X);
        float top = Math.Max(a.Pos.Y, b.Pos.Y);
        float right = Math.Min(a.Pos.X + a.Size.X, b.Pos.X + b.Size.X);
        float bottom = Math.Min(a.Pos.Y + a.Size.Y, b.Pos.Y + b.Size.Y);
        if (right <= left || bottom <= top)
            return 0f;

        float intersection = (right - left) * (bottom - top);
        float smaller = Math.Min(a.Size.X * a.Size.Y, b.Size.X * b.Size.Y);
        return smaller <= 0f ? 0f : intersection / smaller;
    }

    private static float CenterY((Vector2 Pos, Vector2 Size) rect)
        => rect.Pos.Y + rect.Size.Y * 0.5f;

    internal static bool IsLastVisualTileDetached(List<(Vector2 Pos, Vector2 Size)> rects)
    {
        if (rects.Count < 2)
            return false;

        var gaps = new List<float>(rects.Count - 1);
        for (int i = 0; i + 1 < rects.Count; i++)
        {
            float gap = rects[i + 1].Pos.X - (rects[i].Pos.X + rects[i].Size.X);
            gaps.Add(Math.Max(0f, gap));
        }

        float lastGap = gaps[^1];
        float medianWidth = Median(rects.ConvertAll(r => r.Size.X));

        if (gaps.Count == 1)
            return lastGap > Math.Max(8f, medianWidth * 0.18f);

        var baselineGaps = gaps.GetRange(0, gaps.Count - 1);
        float baseline = Median(baselineGaps);
        float threshold = baseline + Math.Max(8f, medianWidth * 0.18f);
        return lastGap > threshold;
    }

    private static float Median(List<float> values)
    {
        if (values.Count == 0)
            return 0f;
        values.Sort();
        int mid = values.Count / 2;
        return values.Count % 2 == 0
            ? (values[mid - 1] + values[mid]) * 0.5f
            : values[mid];
    }

    private static bool HasMixedRedAndNormalFive(
        ReadOnlySpan<int> raw,
        int textureBase,
        int targetTileId)
    {
        if (targetTileId is not (4 or 13 or 22))
            return false;

        bool red = false;
        bool normal = false;
        for (int i = 0; i < raw.Length; i++)
        {
            int id = HandArrayDecoder.DecodeTileId(raw[i], textureBase, out bool isRed);
            if (id != targetTileId)
                continue;
            red |= isRed;
            normal |= !isRed;
        }
        return red && normal;
    }

    private static void DrawFallbackHint(
        Tile tile,
        List<(Vector2 Pos, Vector2 Size)> rects,
        Vector2 viewportOffset)
    {
        var dl = ImGui.GetForegroundDrawList();
        string label = $"DISCARD {tile.ShortName}";
        Vector2 textSize = ImGui.CalcTextSize(label);

        Vector2 center;
        float topY;
        if (rects.Count > 0)
        {
            float left = rects.Min(r => r.Pos.X);
            float right = rects.Max(r => r.Pos.X + r.Size.X);
            topY = rects.Min(r => r.Pos.Y);
            center = new Vector2((left + right) * 0.5f, topY - 28f) + viewportOffset;
        }
        else
        {
            var vp = ImGui.GetMainViewport();
            center = new Vector2(vp.Pos.X + vp.Size.X * 0.5f, vp.Pos.Y + vp.Size.Y * 0.78f);
        }

        var pad = new Vector2(10f, 5f);
        var min = new Vector2(center.X - textSize.X * 0.5f - pad.X, center.Y - textSize.Y * 0.5f - pad.Y);
        var max = new Vector2(center.X + textSize.X * 0.5f + pad.X, center.Y + textSize.Y * 0.5f + pad.Y);

        dl.AddRectFilled(min, max, Theme.Pack(new Vector4(0.04f, 0.05f, 0.07f, 0.92f)), 6f);
        dl.AddRect(min, max, Theme.Pack(Theme.Warn, 0.95f), 6f, ImDrawFlags.None, 2f);
        dl.AddText(
            new Vector2(center.X - textSize.X * 0.5f, center.Y - textSize.Y * 0.5f),
            Theme.Pack(Theme.Warn),
            label);
    }

    /// <summary>True when <paramref name="id"/> is <paramref name="node"/> or any ancestor. Used to drop the meld subtree from the tile pool.</summary>
    private static unsafe bool HasAncestorId(AtkResNode* node, uint id)
    {
        for (var cur = node; cur != null; cur = cur->ParentNode)
            if (cur->NodeId == id)
                return true;
        return false;
    }

    /// <summary>Walks parent chain — result is game-window-local (before multi-viewport desktop offset) and already includes root node position.</summary>
    private static unsafe void AbsolutePosition(AtkResNode* node, out float x, out float y, out float scaleX, out float scaleY)
    {
        x = 0;
        y = 0;
        scaleX = 1f;
        scaleY = 1f;
        var cur = node;
        while (cur != null)
        {
            x = cur->X + x * cur->ScaleX;
            y = cur->Y + y * cur->ScaleY;
            scaleX *= cur->ScaleX;
            scaleY *= cur->ScaleY;
            cur = cur->ParentNode;
        }
    }

    /// <summary>Sine-eased pulse with a higher floor than Theme.Pulse so the overlay never fades to faint.</summary>
    private static float OverlayPulse(float period = 1.4f, float lo = 0.78f, float hi = 1.0f)
    {
        float t = (float)((DateTime.UtcNow.TimeOfDay.TotalSeconds % period) / period);
        float s = 0.5f + 0.5f * MathF.Sin(t * MathF.PI * 2f);
        return lo + (hi - lo) * s;
    }

    private static float ArrowBounce(float period = 0.9f, float amplitude = 5f)
    {
        float t = (float)((DateTime.UtcNow.TimeOfDay.TotalSeconds % period) / period);
        return amplitude * (0.5f + 0.5f * MathF.Sin(t * MathF.PI * 2f));
    }

    private static uint Pack(Vector3 rgb, float alpha)
        => Theme.Pack(new Vector4(rgb.X, rgb.Y, rgb.Z, Math.Clamp(alpha, 0f, 1f)));

    internal static void DrawHighlightNeonGlow(ImDrawListPtr dl, (Vector2 Pos, Vector2 Size) rect, Vector3 color, float intensity)
    {
        float pulse = OverlayPulse() * intensity;

        var min = rect.Pos - new Vector2(2, 2);
        var max = rect.Pos + rect.Size + new Vector2(2, 2);

        // Multi-ring outer glow: 4 expanding rings with decreasing alpha.
        for (int i = 4; i >= 1; i--)
        {
            float expand = i * 2.5f;
            float alpha = pulse * (0.42f / i);
            dl.AddRect(
                min - new Vector2(expand, expand),
                max + new Vector2(expand, expand),
                Pack(color, alpha),
                6f + expand, ImDrawFlags.None, 2f);
        }

        // Subtle inner fill so the tile reads as "active".
        dl.AddRectFilled(min, max, Pack(color, pulse * 0.18f), 6f);

        // Solid bright inner border.
        dl.AddRect(min, max, Pack(color, pulse), 6f, ImDrawFlags.None, 2.5f);

        // L-shaped corner brackets that pulse inward with the beat.
        float bracketLen = 10f + 2f * pulse;
        float bracketOff = 4f;
        float thick = 2.5f;
        uint bracket = Pack(color, pulse + 0.05f);
        DrawCornerBracket(dl, new Vector2(min.X - bracketOff, min.Y - bracketOff), bracketLen, +1, +1, thick, bracket);
        DrawCornerBracket(dl, new Vector2(max.X + bracketOff, min.Y - bracketOff), bracketLen, -1, +1, thick, bracket);
        DrawCornerBracket(dl, new Vector2(min.X - bracketOff, max.Y + bracketOff), bracketLen, +1, -1, thick, bracket);
        DrawCornerBracket(dl, new Vector2(max.X + bracketOff, max.Y + bracketOff), bracketLen, -1, -1, thick, bracket);

        // Bouncing arrow above the tile.
        float cx = (min.X + max.X) * 0.5f;
        float bounce = ArrowBounce();
        float tipY = min.Y - 8f - bounce;
        float baseY = tipY - 18f;
        uint arrowFill = Pack(color, pulse);
        uint arrowShadow = Theme.Pack(Theme.TileShadow, 0.6f);
        dl.AddTriangleFilled(
            new Vector2(cx - 13f, baseY + 2f),
            new Vector2(cx + 13f, baseY + 2f),
            new Vector2(cx, tipY + 2f),
            arrowShadow);
        dl.AddTriangleFilled(
            new Vector2(cx - 13f, baseY),
            new Vector2(cx + 13f, baseY),
            new Vector2(cx, tipY),
            arrowFill);
    }

    private static void DrawCornerBracket(ImDrawListPtr dl, Vector2 origin, float length, int dirX, int dirY, float thickness, uint color)
    {
        var hEnd = new Vector2(origin.X + length * dirX, origin.Y);
        var vEnd = new Vector2(origin.X, origin.Y + length * dirY);
        dl.AddLine(origin, hEnd, color, thickness);
        dl.AddLine(origin, vEnd, color, thickness);
    }

    internal static void DrawHighlightArrow(ImDrawListPtr dl, (Vector2 Pos, Vector2 Size) rect, Vector3 color, float intensity, bool isDrawnTile)
    {
        float pulse = OverlayPulse(1.1f, 0.82f, 1.0f) * intensity;

        var min = rect.Pos - new Vector2(2, 2);
        var max = rect.Pos + rect.Size + new Vector2(2, 2);

        // Minimal tile treatment: thin outline so the user still sees which tile, but the art isn't covered.
        dl.AddRect(min, max, Pack(color, pulse * 0.9f), 6f, ImDrawFlags.None, 2f);

        // Big bouncing arrow with a label pill above the tile.
        string label = isDrawnTile ? "TSUMOGIRI" : "DISCARD";
        var textSize = ImGui.CalcTextSize(label);

        float cx = (min.X + max.X) * 0.5f;
        float bounce = ArrowBounce(0.85f, 6f);

        // Arrow geometry (large, filled triangle).
        float arrowHalfWidth = 18f;
        float arrowHeight = 22f;
        float tipY = min.Y - 8f - bounce;
        float arrowTopY = tipY - arrowHeight;

        // Label pill sits above the arrow.
        float pillPadX = 10f;
        float pillPadY = 4f;
        float pillW = textSize.X + pillPadX * 2f;
        float pillH = textSize.Y + pillPadY * 2f;
        var pillMin = new Vector2(cx - pillW * 0.5f, arrowTopY - pillH - 2f);
        var pillMax = pillMin + new Vector2(pillW, pillH);

        // Shadow drop.
        uint shadow = Theme.Pack(Theme.TileShadow, 0.7f);
        dl.AddRectFilled(pillMin + new Vector2(1, 2), pillMax + new Vector2(1, 2), shadow, pillH * 0.5f);
        dl.AddTriangleFilled(
            new Vector2(cx - arrowHalfWidth + 1, arrowTopY + 2),
            new Vector2(cx + arrowHalfWidth + 1, arrowTopY + 2),
            new Vector2(cx + 1, tipY + 2),
            shadow);

        // Filled pill.
        dl.AddRectFilled(pillMin, pillMax, Pack(color, pulse), pillH * 0.5f);
        dl.AddText(pillMin + new Vector2(pillPadX, pillPadY), Theme.Pack(new Vector4(0.07f, 0.08f, 0.10f, 1f)), label);

        // Filled arrow.
        dl.AddTriangleFilled(
            new Vector2(cx - arrowHalfWidth, arrowTopY),
            new Vector2(cx + arrowHalfWidth, arrowTopY),
            new Vector2(cx, tipY),
            Pack(color, pulse));
    }

}
