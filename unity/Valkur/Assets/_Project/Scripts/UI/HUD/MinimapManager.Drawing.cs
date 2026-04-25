using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;

namespace Valkur.UI.HUD
{
    public partial class MinimapManager : MonoBehaviour
    {

        private void DrawDot(int cx, int cy, int half, Color col)
        {
            for (int dx = -half; dx <= half; dx++)
            for (int dy = -half; dy <= half; dy++)
            {
                int x = cx + dx;
                int y = cy + dy;
                if (x >= 0 && x < texWidth && y >= 0 && y < texHeight)
                    _tex.SetPixel(x, y, col);
            }
        }

        private void DrawBorder()
        {
            for (int x = 0; x < texWidth;  x++) { _tex.SetPixel(x, 0, borderColor); _tex.SetPixel(x, texHeight - 1, borderColor); }
            for (int y = 0; y < texHeight; y++) { _tex.SetPixel(0, y, borderColor); _tex.SetPixel(texWidth - 1, y, borderColor); }
        }

        private int GetDotHalf(MinimapDot dot)
        {
            switch (dot.DotType)
            {
                case MinimapDotType.Player:  return playerDotSize  / 2;
                case MinimapDotType.Monster: return monsterDotSize / 2;
                default:                     return npcDotSize     / 2;
            }
        }

        // ── Color helpers ─────────────────────────────────────────────────

        public Color GetDefaultColor(MinimapDotType type)
        {
            switch (type)
            {
                case MinimapDotType.Player:  return playerColor;
                case MinimapDotType.Monster: return monsterColor;
                default:                     return npcColor;
            }
        }

        // ── Fog-of-war helpers ────────────────────────────────────────────

        /// <summary>Marks all fog cells within radius of the given world-space position as explored.</summary>
        public void RevealAround(Vector2 worldCenter, float radius)
        {
            if (fogCellSize <= 0f) return;
            int r = Mathf.CeilToInt(radius / fogCellSize);
            int cx = Mathf.FloorToInt(worldCenter.x / fogCellSize);
            int cy = Mathf.FloorToInt(worldCenter.y / fogCellSize);
            float rSqr = radius * radius;
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    float wx = (cx + dx) * fogCellSize + fogCellSize * 0.5f;
                    float wy = (cy + dy) * fogCellSize + fogCellSize * 0.5f;
                    float dX = wx - worldCenter.x;
                    float dY = wy - worldCenter.y;
                    if (dX * dX + dY * dY <= rSqr)
                        _exploredCells.Add(CellKey(cx + dx, cy + dy));
                }
            }
        }

        /// <summary>Forgets all explored fog cells (e.g. on new game / map change).</summary>
        public void ClearFog() => _exploredCells.Clear();

        /// <summary>Returns true if the given world-space position is currently considered explored.</summary>
        public bool IsExplored(Vector2 worldPos)
        {
            if (!fogOfWarEnabled) return true;
            int cx = Mathf.FloorToInt(worldPos.x / fogCellSize);
            int cy = Mathf.FloorToInt(worldPos.y / fogCellSize);
            return _exploredCells.Contains(CellKey(cx, cy));
        }

        private static long CellKey(int x, int y)
            => ((long)(uint)x << 32) | (uint)y;

        private void PaintFog(Vector2 center)
        {
            // For each minimap pixel, back-project to world and check exploration.
            float worldPerPxX = (viewRadius * 2f) / texWidth;
            float worldPerPxY = (viewRadius * 2f) / texHeight;
            for (int py = 0; py < texHeight; py++)
            {
                float wy = center.y + (py - texHeight * 0.5f) * worldPerPxY;
                int cy = Mathf.FloorToInt(wy / fogCellSize);
                for (int px = 0; px < texWidth; px++)
                {
                    float wx = center.x + (px - texWidth * 0.5f) * worldPerPxX;
                    int cx = Mathf.FloorToInt(wx / fogCellSize);
                    if (!_exploredCells.Contains(CellKey(cx, cy)))
                        _tex.SetPixel(px, py, fogColor);
                }
            }
        }

        // ── Projection & marker drawing ──────────────────────────────────

        private bool TryProject(Vector2 worldPos, Vector2 center, out int px, out int py)
        {
            Vector2 rel = worldPos - center;
            px = Mathf.RoundToInt((rel.x / viewRadius) * (texWidth  * 0.5f) + texWidth  * 0.5f);
            py = Mathf.RoundToInt((rel.y / viewRadius) * (texHeight * 0.5f) + texHeight * 0.5f);
            return px >= 0 && px < texWidth && py >= 0 && py < texHeight;
        }

        private void DrawMarker(int cx, int cy, int pixelSize, MinimapMarker.MarkerShape shape, Color color)
        {
            int half = Mathf.Max(1, pixelSize / 2);
            switch (shape)
            {
                case MinimapMarker.MarkerShape.Square:
                    DrawDot(cx, cy, half, color);
                    break;
                case MinimapMarker.MarkerShape.Diamond:
                    for (int dy = -half; dy <= half; dy++)
                    {
                        int span = half - Mathf.Abs(dy);
                        for (int dx = -span; dx <= span; dx++)
                            SetPixelSafe(cx + dx, cy + dy, color);
                    }
                    break;
                case MinimapMarker.MarkerShape.Plus:
                    for (int d = -half; d <= half; d++)
                    {
                        SetPixelSafe(cx + d, cy, color);
                        SetPixelSafe(cx, cy + d, color);
                    }
                    break;
            }
        }

        private void SetPixelSafe(int x, int y, Color c)
        {
            if (x >= 0 && x < texWidth && y >= 0 && y < texHeight)
                _tex.SetPixel(x, y, c);
        }
    }

    // ── Companion enum ────────────────────────────────────────────────────
    public enum MinimapDotType { Player, Monster, NPC }
}