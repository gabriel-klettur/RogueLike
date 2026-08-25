using UnityEngine;

namespace Valkur.Gameplay.TileEditor
{
    public partial class TileEditorGridOverlay
    {
        // ── Per-tag GL bitmap-font glyphs ─────────────────────────────────────
        // Each painted Collision cell draws a centered white digit ("0".."8") or "*"
        // showing which visual layer the collider applies to. Rendered as solid GL
        // quads from a 5x7 monospace bitmap so the entire visualisation stays in the
        // GL hot path — no Canvas, no TextMeshPro, zero allocations per frame.
        // Glyph data: 7 rows × 5 cols, encoded as a byte per row (MSB = leftmost pixel
        // of the 5-bit column field, low 5 bits used). Indices 0..9 = digits; index 10 = "*";
        // index 11 = "," (added in M1.10 so multi-tag CSV strings render as authored).
        private static readonly byte[][] DigitMasks =
        {
            new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E }, // 0
            new byte[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E }, // 1
            new byte[] { 0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F }, // 2
            new byte[] { 0x0E, 0x11, 0x01, 0x06, 0x01, 0x11, 0x0E }, // 3
            new byte[] { 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 }, // 4
            new byte[] { 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E }, // 5
            new byte[] { 0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E }, // 6
            new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x04, 0x08, 0x08 }, // 7
            new byte[] { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E }, // 8
            new byte[] { 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C }, // 9
            new byte[] { 0x04, 0x15, 0x0E, 0x1F, 0x0E, 0x15, 0x04 }, // *
            new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x08 }, // ,  (low-left tail)
        };
        private const int CommaGlyphIndex = 11;

        private const int   GlyphRows    = 7;
        private const int   GlyphCols    = 5;
        // Glyph fits in a square ~60% of a cell, leaving a margin so the red border
        // (drawn just before the glyph in the same overlay pass) never visually
        // overlaps the digit's lit pixels.
        private const float GlyphHeight  = 0.70f;
        private const float GlyphWidth   = GlyphHeight * GlyphCols / GlyphRows; // ≈ 0.50 → keeps glyph aspect 5×7
        private const float GlyphPixelW  = GlyphWidth  / GlyphCols;
        private const float GlyphPixelH  = GlyphHeight / GlyphRows;

        // ── Multi-tag text rendering (M1.10) ──────────────────────────────────
        // CSV tags ("0,2,5") render as a horizontal row of glyphs scaled DOWN so
        // the entire string fits inside the cell minus a small margin. The single-
        // glyph rendering ("*", "0") continues to use the original constants above
        // (zero visual change for legacy maps). When the string has commas, every
        // char shares the same scaled pixel size so widths and heights stay aligned.
        private const float TagTextAvailableWidth = 0.85f;  // 1.0 cell − 0.075 margin each side
        private const float TagTextMaxHeight      = GlyphHeight;
        private static readonly Color GlyphColor = Color.white;

        /// <summary>
        /// Resolve a tag string to its index in <see cref="DigitMasks"/>: "0".."8" map
        /// to 0..8, "*" maps to 10, anything else returns -1 (caller skips the glyph).
        /// </summary>
        private static int TagToGlyphIndex(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return 10; // missing → wildcard
            if (tag == CollisionTagMap.Wildcard) return 10;
            if (tag.Length == 1 && tag[0] >= '0' && tag[0] <= '8') return tag[0] - '0';
            return -1;
        }

        /// <summary>
        /// Stamp the glyph's lit pixels as small GL.QUADS centred at (cx, cy). Must be
        /// called inside an active <c>GL.Begin(GL.QUADS)</c> block; the caller is
        /// expected to have set <see cref="GlyphColor"/> via <c>GL.Color</c> already.
        /// </summary>
        private static void DrawGlyphQuads(float cx, float cy, int glyphIdx)
        {
            byte[] mask = DigitMasks[glyphIdx];
            float left = cx - GlyphWidth  * 0.5f;
            float top  = cy + GlyphHeight * 0.5f;

            for (int row = 0; row < GlyphRows; row++)
            {
                byte bits = mask[row];
                if (bits == 0) continue;
                float py = top - row * GlyphPixelH;
                for (int col = 0; col < GlyphCols; col++)
                {
                    if ((bits & (1 << (GlyphCols - 1 - col))) == 0) continue;
                    float px = left + col * GlyphPixelW;
                    GL.Vertex3(px,                py - GlyphPixelH, 0f);
                    GL.Vertex3(px + GlyphPixelW,  py - GlyphPixelH, 0f);
                    GL.Vertex3(px + GlyphPixelW,  py,               0f);
                    GL.Vertex3(px,                py,               0f);
                }
            }
        }

        /// <summary>
        /// Render a canonical collision tag string as a horizontal sequence of
        /// glyphs centred at (cx, cy). Used by the Colliders overlay to show
        /// multi-tag CSV like "0,2,5" inside a single cell. Behaves identically
        /// to <see cref="DrawGlyphQuads"/> for the legacy single-glyph cases
        /// ("*", "0".."8") — same final pixel size + position — so existing
        /// maps see zero visual delta.
        ///
        /// Scaling rule: every char shares a uniform pixel size such that
        /// <c>charCount * GlyphCols * pixelSize ≤ TagTextAvailableWidth</c> AND
        /// <c>GlyphRows * pixelSize ≤ TagTextMaxHeight</c>. For 1-char strings
        /// the result equals the original constants (pixelSize = GlyphPixelW);
        /// longer strings shrink uniformly so the row fits without overflowing.
        ///
        /// Must be called inside an active <c>GL.Begin(GL.QUADS)</c> block;
        /// the caller must have set <see cref="GlyphColor"/> via <c>GL.Color</c>
        /// already (mirrors <see cref="DrawGlyphQuads"/>'s contract).
        /// </summary>
        private static void DrawTagTextQuads(float cx, float cy, string tag)
        {
            if (string.IsNullOrEmpty(tag)) return;

            // Single-glyph fast paths (legacy maps): "*" and "0".."8".
            int singleIdx = TagToGlyphIndex(tag);
            if (singleIdx >= 0)
            {
                DrawGlyphQuads(cx, cy, singleIdx);
                return;
            }

            int charCount = tag.Length;
            if (charCount == 0) return;

            // Uniform per-pixel size: shrink so the full string + horizontal
            // padding fits in TagTextAvailableWidth. Height is capped to the
            // single-glyph max so multi-tag strings don't grow vertically.
            float pixelW = TagTextAvailableWidth / (charCount * GlyphCols);
            float pixelH = TagTextMaxHeight / GlyphRows;
            if (pixelW > pixelH) pixelW = pixelH;       // height-capped path
            float pixelH_final = pixelW;                // square pixels — keeps aspect

            float totalWidth = charCount * GlyphCols * pixelW;
            float totalHeight = GlyphRows * pixelH_final;
            float left = cx - totalWidth * 0.5f;
            float top  = cy + totalHeight * 0.5f;

            for (int c = 0; c < charCount; c++)
            {
                int idx = CharToGlyphIndex(tag[c]);
                if (idx < 0) continue;
                byte[] mask = DigitMasks[idx];
                float charLeft = left + c * GlyphCols * pixelW;

                for (int row = 0; row < GlyphRows; row++)
                {
                    byte bits = mask[row];
                    if (bits == 0) continue;
                    float py = top - row * pixelH_final;
                    for (int col = 0; col < GlyphCols; col++)
                    {
                        if ((bits & (1 << (GlyphCols - 1 - col))) == 0) continue;
                        float px = charLeft + col * pixelW;
                        GL.Vertex3(px,           py - pixelH_final, 0f);
                        GL.Vertex3(px + pixelW,  py - pixelH_final, 0f);
                        GL.Vertex3(px + pixelW,  py,                0f);
                        GL.Vertex3(px,           py,                0f);
                    }
                }
            }
        }

        /// <summary>
        /// Map a single character to a <see cref="DigitMasks"/> index, or -1
        /// when it isn't part of the bitmap font (whitespace, unsupported
        /// punctuation, etc).
        /// </summary>
        private static int CharToGlyphIndex(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c == '*') return 10;
            if (c == ',') return CommaGlyphIndex;
            return -1;
        }
    }
}
