using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

namespace Valkur.Gameplay.TileEditor
{
    public partial class TileEditorGridOverlay : MonoBehaviour
    {

        // ── Rendering ────────────────────────────────────────────────────────

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (_mat == null || _targetCamera == null) return;
            if (cam != _targetCamera) return;

            DrawGrid(cam);
        }

        private void DrawGrid(Camera cam)
        {
            // Camera world-space orthographic bounds.
            float halfH  = cam.orthographicSize;
            float halfW  = halfH * cam.aspect;
            Vector3 pos  = cam.transform.position;

            float left   = pos.x - halfW;
            float right  = pos.x + halfW;
            float bottom = pos.y - halfH;
            float top    = pos.y + halfH;

            // Integer tile boundaries, extended by Margin to avoid visible seams on pan.
            int xMin = Mathf.FloorToInt(left)   - Margin;
            int xMax = Mathf.CeilToInt(right)   + Margin;
            int yMin = Mathf.FloorToInt(bottom) - Margin;
            int yMax = Mathf.CeilToInt(top)     + Margin;

            // Defensive cap: at extreme zoom-out the grid would emit thousands of GL
            // line segments per frame. Skip drawing rather than tank FPS.
            const int MaxLinesPerAxis = 400;
            if ((xMax - xMin) > MaxLinesPerAxis || (yMax - yMin) > MaxLinesPerAxis)
                return;

            // Update hovered cell from current mouse position.
            Vector3 mouseWorld = cam.ScreenToWorldPoint(
                new Vector3(Input.mousePosition.x, Input.mousePosition.y, -cam.transform.position.z));
            _hoverCell = new Vector2Int(Mathf.FloorToInt(mouseWorld.x), Mathf.FloorToInt(mouseWorld.y));

            // Update Fill preview when using Fill tool
            if (_currentTool == TileEditorState.Tool.Fill)
            {
                CalculateFillPreview();
                _fillPreviewBlinkTime += Time.deltaTime;
            }
            else
            {
                _fillPreviewCells.Clear();
                _fillPreviewBlinkTime = 0f;
            }

            _mat.SetPass(0);

            GL.PushMatrix();
            GL.LoadProjectionMatrix(cam.projectionMatrix);
            GL.modelview = cam.worldToCameraMatrix;

            // ── Regular grid (lines) ──────────────────────────────────────────
            if (_showGridLines)
            {
                GL.Begin(GL.LINES);
                GL.Color(GridColor);

                for (int x = xMin; x <= xMax; x++)
                {
                    GL.Vertex3(x, yMin, 0f);
                    GL.Vertex3(x, yMax, 0f);
                }
                for (int y = yMin; y <= yMax; y++)
                {
                    GL.Vertex3(xMin, y, 0f);
                    GL.Vertex3(xMax, y, 0f);
                }
                GL.End();
            }

            // ── Collider overlay: red fill + red border for every solid Collision cell ──
            // Drawn between the grid lines and the cell highlights so the hover/selection
            // borders remain on top of the collider shading.
            if (_showColliderOverlay && _collisionTilemap != null)
                DrawColliderOverlay(cam, xMin, yMin, xMax, yMax);

            // ── Layer Jumps overlay (M1.8): blue fill + blue border + white digit ──
            // Independent of the Colliders overlay; both can be visible at once.
            if (_showLayerJumps && _layerJumpMap != null && _layerJumpMap.Count > 0)
                DrawLayerJumpsOverlay(cam, xMin, yMin, xMax, yMax);

            // ── Tile Layer overlay (View panel): white digit per visible painted cell ──
            // Cost is bounded by viewport size, not by map size.
            if (_showTileLayer && _layerTilemaps != null)
                DrawTileLayerOverlay(cam, xMin, yMin, xMax, yMax);

            // ── Fill preview: blinking yellow fill for Fill tool ──
            if (_currentTool == TileEditorState.Tool.Fill && _fillPreviewCells.Count > 0)
                DrawFillPreview(cam);

            // ── Cell highlight quads: hover (cyan) / selected (green) / brush (yellow) ──
            // Thickness is the same for all; compute once from current zoom level.
            float pixelSize = (cam.orthographicSize * 2f) / Screen.height;
            float t = pixelSize * HoverThicknessPx;

            GL.Begin(GL.QUADS);

            // Clipboard highlight: thick YELLOW border per cell most recently Copy/Cut.
            // Drawn below the green selection so green wins when a cell is in both.
            // Uses a proportionally larger thickness so the ring is visually distinct
            // from the 3 px hover ring (4 px = ClipboardOutlineThicknessPx).
            if (_copiedCells.Count > 0)
            {
                float clipT = pixelSize * TileEditorConstants.ClipboardOutlineThicknessPx;
                foreach (var c in _copiedCells)
                    DrawBorderQuads(c.x, c.y, TileEditorConstants.ClipboardOutlineColor, clipT);
            }

            // Persistent Select-tool selection: GREEN border per cell. Drawn first
            // so the live brush stroke (yellow) and hover (cyan) overlay on top.
            if (_selectedCells.Count > 0)
            {
                foreach (var c in _selectedCells)
                    DrawBorderQuads(c.x, c.y, SelectedColor, t);
            }

            // Live Rect-drag preview: yellow border around the rectangle the user is
            // dragging in SelectMode.Rect. The selection itself only commits on mouse-up.
            if (_rectDragStart.HasValue && _rectDragCurrent.HasValue)
            {
                int rxMin = Mathf.Min(_rectDragStart.Value.x, _rectDragCurrent.Value.x);
                int ryMin = Mathf.Min(_rectDragStart.Value.y, _rectDragCurrent.Value.y);
                int rxMax = Mathf.Max(_rectDragStart.Value.x, _rectDragCurrent.Value.x);
                int ryMax = Mathf.Max(_rectDragStart.Value.y, _rectDragCurrent.Value.y);
                DrawBorderRect(rxMin, ryMin, (rxMax - rxMin) + 1, (ryMax - ryMin) + 1, BrushColor, t);
            }

            // Yellow (Brush/Erase) or Green (Select) — all cells in the current stroke.
            Color strokeColor = _currentTool == TileEditorState.Tool.Select ? SelectedColor : BrushColor;
            foreach (var c in _brushCells)
                DrawBorderQuads(c.x, c.y, strokeColor, t);

            // Green — selected / last-clicked cell, sized to brush footprint (cursor = top-left,
            // footprint extends right + down).
            if (_selectedCell.HasValue)
                DrawBorderRect(_selectedCell.Value.x, _selectedCell.Value.y - (_brushSize - 1),
                               _brushSize, _brushSize, SelectedColor, t);

            // Cyan — hover, sized to brush footprint (drawn last / on top).
            DrawBorderRect(_hoverCell.x, _hoverCell.y - (_brushSize - 1),
                           _brushSize, _brushSize, HoverColor, t);

            GL.End();

            GL.PopMatrix();
        }

        /// <summary>
        /// Emits 4 GL quads that form a thick border around the tile cell at (cx, cy).
        /// Must be called between GL.Begin(GL.QUADS) and GL.End().
        /// </summary>
        private static void DrawBorderQuads(float cx, float cy, Color color, float t)
        {
            DrawBorderRect(cx, cy, 1, 1, color, t);
        }

        /// <summary>
        /// Emits 4 GL quads forming a thick border around an N×M cell rectangle whose
        /// bottom-left corner is at (cx, cy). Must be called between GL.Begin(GL.QUADS) and GL.End().
        /// </summary>
        private static void DrawBorderRect(float cx, float cy, int w, int h, Color color, float t)
        {
            float x0 = cx;
            float y0 = cy;
            float x1 = cx + w;
            float y1 = cy + h;

            GL.Color(color);

            // Bottom
            GL.Vertex3(x0,     y0,     0f); GL.Vertex3(x1,     y0,     0f);
            GL.Vertex3(x1,     y0 + t, 0f); GL.Vertex3(x0,     y0 + t, 0f);
            // Top
            GL.Vertex3(x0,     y1 - t, 0f); GL.Vertex3(x1,     y1 - t, 0f);
            GL.Vertex3(x1,     y1,     0f); GL.Vertex3(x0,     y1,     0f);
            // Left (inset so corners don't overlap)
            GL.Vertex3(x0,     y0 + t, 0f); GL.Vertex3(x0 + t, y0 + t, 0f);
            GL.Vertex3(x0 + t, y1 - t, 0f); GL.Vertex3(x0,     y1 - t, 0f);
            // Right
            GL.Vertex3(x1 - t, y0 + t, 0f); GL.Vertex3(x1,     y0 + t, 0f);
            GL.Vertex3(x1,     y1 - t, 0f); GL.Vertex3(x1 - t, y1 - t, 0f);
        }

        /// <summary>
        /// Draws a translucent red fill and an opaque red border for every solid cell
        /// of the bound Collision tilemap that lies within the visible rect.
        /// Uses <see cref="Tilemap.GetTilesBlock"/> to fetch all candidate tiles in a
        /// single managed call instead of N individual <c>GetTile</c> queries.
        /// </summary>
        private void DrawColliderOverlay(Camera cam, int xMin, int yMin, int xMax, int yMax)
        {
            // Clip the visible window against the tilemap's painted bounds so we never
            // sample empty regions of the world (massive zones with sparse colliders).
            var bounds = _collisionTilemap.cellBounds;
            int sx = Mathf.Max(xMin, bounds.xMin);
            int sy = Mathf.Max(yMin, bounds.yMin);
            int ex = Mathf.Min(xMax, bounds.xMax);
            int ey = Mathf.Min(yMax, bounds.yMax);

            int w = ex - sx;
            int h = ey - sy;
            if (w <= 0 || h <= 0) return;
            if (w * h > MaxColliderCells) return;

            var rect = new BoundsInt(sx, sy, 0, w, h, 1);
            TileBase[] tiles;
            try { tiles = _collisionTilemap.GetTilesBlock(rect); }
            catch { return; }
            if (tiles == null || tiles.Length == 0) return;

            float pixelSize = (cam.orthographicSize * 2f) / Screen.height;
            float t = pixelSize * ColliderBorderThicknessPx;

            // Translucent red fill — one quad per painted cell.
            GL.Begin(GL.QUADS);
            GL.Color(ColliderFillColor);
            for (int i = 0; i < tiles.Length; i++)
            {
                if (tiles[i] == null) continue;
                int cx = sx + (i % w);
                int cy = sy + (i / w);
                GL.Vertex3(cx,        cy,        0f);
                GL.Vertex3(cx + 1f,   cy,        0f);
                GL.Vertex3(cx + 1f,   cy + 1f,   0f);
                GL.Vertex3(cx,        cy + 1f,   0f);
            }
            GL.End();

            // Opaque red border — 4 thin quads per painted cell.
            GL.Begin(GL.QUADS);
            for (int i = 0; i < tiles.Length; i++)
            {
                if (tiles[i] == null) continue;
                int cx = sx + (i % w);
                int cy = sy + (i / w);
                DrawBorderQuads(cx, cy, ColliderBorderColor, t);
            }
            GL.End();

            // Per-tag bitmap glyph: a white 5x7 monospace digit ("0".."8") or "*"
            // centred on each painted Collision cell, showing which visual layer the
            // collider applies to. M1.10: multi-tag CSV like "0,2,5" renders as a
            // single horizontal text run that scales down uniformly so the whole
            // string fits inside the cell. Skipped entirely if no tag map is bound —
            // the legacy "all colliders apply to all entities" semantics remain
            // visually identical to before this feature shipped.
            if (_collisionTagMap == null) return;

            GL.Begin(GL.QUADS);
            GL.Color(GlyphColor);
            for (int i = 0; i < tiles.Length; i++)
            {
                if (tiles[i] == null) continue;
                int cx = sx + (i % w);
                int cy = sy + (i / w);
                string tag = _collisionTagMap.Get(new Vector2Int(cx, cy));
                DrawTagTextQuads(cx + 0.5f, cy + 0.5f, tag);
            }
            GL.End();
        }

        /// <summary>
        /// Paint a translucent blue fill + opaque blue border + centred white digit
        /// for every cell that has a <see cref="LayerJumpMap"/> entry within the
        /// visible viewport. Sparse iteration — walks the map's
        /// <see cref="LayerJumpMap.Cells"/> directly (no per-cell GetTilesBlock
        /// because there is no underlying tilemap) and filters by viewport bounds.
        /// Bounded by the natural sparsity of jump cells (a designer paints a
        /// handful, never thousands).
        /// </summary>
        private void DrawLayerJumpsOverlay(Camera cam, int xMin, int yMin, int xMax, int yMax)
        {
            float pixelSize = (cam.orthographicSize * 2f) / Screen.height;
            float t = pixelSize * ColliderBorderThicknessPx;

            // Pass 1 — fill quads (translucent blue).
            GL.Begin(GL.QUADS);
            GL.Color(LayerJumpFillColor);
            foreach (var kv in _layerJumpMap.Cells)
            {
                int cx = kv.Key.x;
                int cy = kv.Key.y;
                if (cx < xMin || cx > xMax || cy < yMin || cy > yMax) continue;
                GL.Vertex3(cx,        cy,        0f);
                GL.Vertex3(cx + 1f,   cy,        0f);
                GL.Vertex3(cx + 1f,   cy + 1f,   0f);
                GL.Vertex3(cx,        cy + 1f,   0f);
            }
            GL.End();

            // Pass 2 — opaque blue border, 4 quads per cell.
            GL.Begin(GL.QUADS);
            foreach (var kv in _layerJumpMap.Cells)
            {
                int cx = kv.Key.x;
                int cy = kv.Key.y;
                if (cx < xMin || cx > xMax || cy < yMin || cy > yMax) continue;
                DrawBorderQuads(cx, cy, LayerJumpBorderColor, t);
            }
            GL.End();

            // Pass 3 — white digit glyph centred on each cell using the existing
            // 5x7 bitmap font from the Colliders pipeline. TagToGlyphIndex maps
            // "0".."8" → 0..8; jumps never store "*" so the wildcard slot is unused.
            GL.Begin(GL.QUADS);
            GL.Color(GlyphColor);
            foreach (var kv in _layerJumpMap.Cells)
            {
                int cx = kv.Key.x;
                int cy = kv.Key.y;
                if (cx < xMin || cx > xMax || cy < yMin || cy > yMax) continue;
                int glyphIdx = TagToGlyphIndex(kv.Value);
                if (glyphIdx < 0 || glyphIdx > 8) continue; // skip wildcard / invalid
                DrawGlyphQuads(cx + 0.5f, cy + 0.5f, glyphIdx);
            }
            GL.End();
        }

        /// <summary>
        /// Per visible cell, stamp the white digit of the TOPMOST visual layer that has
        /// a tile painted at that cell. Iterates the 9 tilemaps from
        /// <see cref="TilemapLayerSetup.TilemapLayer.OverheadDetails"/> (index 8) down
        /// to <see cref="TilemapLayerSetup.TilemapLayer.Ground"/> (0), drawing exactly
        /// one digit per cell. The viewport-sized <see cref="_tileLayerDrawnGrid"/>
        /// bitmap skips cells a higher layer already claimed without allocating a
        /// HashSet per frame.
        ///
        /// Performance:
        ///   • <see cref="Tilemap.GetTilesBlock"/> fetches every candidate tile in a
        ///     single managed call per layer (≤ 9 managed calls total per frame),
        ///     clipped to each tilemap's painted <see cref="Tilemap.cellBounds"/> —
        ///     a sparse map costs near-zero, not "map size × 9".
        ///   • The viewport rect is capped by <see cref="MaxTileLayerCells"/>, mirroring
        ///     the collider overlay's defensive bail-out at extreme zoom-out.
        ///   • No per-frame allocations beyond what GetTilesBlock returns; the drawn
        ///     bitmap is grown only when the viewport expands.
        /// </summary>
        private void DrawTileLayerOverlay(Camera cam, int xMin, int yMin, int xMax, int yMax)
        {
            int w = xMax - xMin;
            int h = yMax - yMin;
            if (w <= 0 || h <= 0) return;
            int viewportCells = w * h;
            if (viewportCells > MaxTileLayerCells) return;

            // Grow + reset the per-frame "already drew here" bitmap.
            if (_tileLayerDrawnGrid == null || _tileLayerDrawnGrid.Length < viewportCells)
                _tileLayerDrawnGrid = new bool[viewportCells];
            else
                System.Array.Clear(_tileLayerDrawnGrid, 0, viewportCells);

            GL.Begin(GL.QUADS);
            GL.Color(GlyphColor);

            // Walk layers high → low so the topmost painted layer wins on each cell.
            for (int layerIdx = _layerTilemaps.Length - 1; layerIdx >= 0; layerIdx--)
            {
                var tm = _layerTilemaps[layerIdx];
                if (tm == null) continue;

                // Clip the visible window against THIS tilemap's painted bounds.
                // Sparse layers (e.g. ObjectsHigh painted in one corner) cost only
                // the intersection of their bounds with the viewport.
                var bounds = tm.cellBounds;
                int sx = Mathf.Max(xMin, bounds.xMin);
                int sy = Mathf.Max(yMin, bounds.yMin);
                int ex = Mathf.Min(xMax, bounds.xMax);
                int ey = Mathf.Min(yMax, bounds.yMax);
                int bw = ex - sx;
                int bh = ey - sy;
                if (bw <= 0 || bh <= 0) continue;

                var rect = new BoundsInt(sx, sy, 0, bw, bh, 1);
                TileBase[] tiles;
                try { tiles = tm.GetTilesBlock(rect); }
                catch { continue; }
                if (tiles == null || tiles.Length == 0) continue;

                // Glyph index 0..8 maps to layer 0..8 in DigitMasks (same encoding
                // as the collision-tag and layer-jump digits).
                int glyphIdx = layerIdx;
                if (glyphIdx < 0 || glyphIdx > 8) continue;

                for (int i = 0; i < tiles.Length; i++)
                {
                    if (tiles[i] == null) continue;
                    int cx = sx + (i % bw);
                    int cy = sy + (i / bw);
                    int drawnIdx = (cy - yMin) * w + (cx - xMin);
                    if (_tileLayerDrawnGrid[drawnIdx]) continue;
                    _tileLayerDrawnGrid[drawnIdx] = true;
                    DrawGlyphQuads(cx + 0.5f, cy + 0.5f, glyphIdx);
                }
            }

            GL.End();
        }

        /// <summary>
        /// Draws a blinking yellow fill for all cells that would be affected by the Fill operation.
        /// The alpha oscillates between 0.2 and 0.6 to create a blinking effect.
        /// </summary>
        private void DrawFillPreview(Camera cam)
        {
            // Calculate blinking alpha (oscillates between 0.2 and 0.6)
            float blinkAlpha = 0.4f + Mathf.Sin(_fillPreviewBlinkTime * 3f) * 0.2f;
            Color blinkColor = new Color(FillPreviewColor.r, FillPreviewColor.g, FillPreviewColor.b, blinkAlpha);

            GL.Begin(GL.QUADS);
            GL.Color(blinkColor);

            foreach (var cell in _fillPreviewCells)
            {
                // Draw filled quad for each cell in the Fill preview area
                GL.Vertex3(cell.x,        cell.y,        0f);
                GL.Vertex3(cell.x + 1f,   cell.y,        0f);
                GL.Vertex3(cell.x + 1f,   cell.y + 1f,   0f);
                GL.Vertex3(cell.x,        cell.y + 1f,   0f);
            }

            GL.End();
        }
    }
}