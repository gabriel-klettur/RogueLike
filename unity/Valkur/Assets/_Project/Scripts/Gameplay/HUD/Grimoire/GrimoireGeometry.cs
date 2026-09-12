using System.Collections.Generic;
using UnityEngine;
using Valkur.Core.UI;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// Where everything in the grimoire goes, as arithmetic.
    ///
    /// <para><b>Why it is a pure static class and not code inside the view.</b> uGUI performs
    /// no layout in Edit Mode — reading a rect back returns what was written to it, never what
    /// a layout pass would have made of it — so anything decided inside the view is effectively
    /// unpinnable. Every structural probe of the shipped Controls editor was green while its
    /// window was unreadable. Stated here, the whole layout is a function a test can call.</para>
    ///
    /// <para><b>Why not <see cref="SpellGraphGeometry"/>.</b> That one is excellent and its
    /// spacing is fixed in graph pixels (76 px nodes, 168 px depth), which is right for a
    /// full-screen slab an author pans. This window is a panel on a texel grid whose node size
    /// is authored in <see cref="GrimoireStyle"/>, so the spacing has to be DERIVED from it or
    /// the two disagree the first time anybody retunes the style. The placement ALGORITHM —
    /// the valuable half — is still <see cref="SpellGraphLayout.Resolve"/>; only the distances
    /// are ours.</para>
    /// </summary>
    internal static class GrimoireGeometry
    {
        /// <summary>Gap between the three columns, in texels.</summary>
        internal const int ColumnGap = 3;

        /// <summary>
        /// Fraction of the screen the panel spans. It matches the character sheet's tab strip,
        /// which is anchored at these same fractions and owned by another assembly — so until
        /// the sheet's chrome is rebuilt, the OUTER rect stays in screen fractions and
        /// everything drawn INSIDE it sits on the texel grid. Closing that seam is the sheet's
        /// job, not the grimoire's.
        /// </summary>
        internal const float PanelLeft = 0.20f, PanelRight = 0.80f;

        /// <summary>
        /// The TOP is fixed by the character sheet's tab strip, which is anchored at 0.855 and
        /// belongs to another assembly: move it and the strip floats off the panel.
        ///
        /// <para>The BOTTOM is ours, and it was too low — twice. Measured across the nine
        /// shipped schools, a constellation is either 117 or 173 texels tall (three schools and
        /// six), so the TALLEST is what the band has to hold and everything above that is air
        /// on every screen in the game. At 0.15 the band was 262 texels against a worst case
        /// of 186 including the filter row; at 0.19 it was 230, still 44 spare. At 0.29 it is
        /// 190, which holds the worst case with four to give.</para>
        ///
        /// <para><b>It cannot be fixed by scaling the board up.</b> The fit is bound by WIDTH
        /// in every one of the nine — measured 1.006 for all of them, because every school is
        /// exactly four depth steps across — so raising the zoom cap buys nothing at all, and
        /// the three short schools keep their own air whatever this number is. That is the
        /// honest remainder: a window whose height changed with the school clicked would be a
        /// window that jumps under the player's cursor, which is worse than a quiet margin.
        /// </para>
        ///
        /// <para>The number is pinned to the DATA rather than to itself by
        /// <c>GrimoireGeometryTests.TheBand_HoldsTheTallestShippedSchool_WithoutMuchToSpare</c>,
        /// which fails in BOTH directions — a school too tall to fit, and a band grown so far
        /// past the tallest that the window is drawing black over the world for nothing.</para>
        /// </summary>
        internal const float PanelBottom = 0.29f, PanelTop = 0.85f;

        /// <summary>The panel's size in CANVAS units at the HUD reference resolution.</summary>
        internal static Vector2 PanelCanvasSize() => new Vector2(
            (PanelRight - PanelLeft) * HudLayout.ReferenceWidth,
            (PanelTop - PanelBottom) * HudLayout.ReferenceHeight);

        /// <summary>
        /// The panel's size in TEXELS at a given pixel scale. Floored, because half a texel of
        /// room is no room: a rect that claims it lands off the grid, which is the whole point
        /// of having one.
        /// </summary>
        internal static Vector2Int PanelTexels(int pixelScale)
        {
            var canvas = PanelCanvasSize();
            int scale = Mathf.Max(1, pixelScale);
            return new Vector2Int(Mathf.FloorToInt(canvas.x / scale),
                                  Mathf.FloorToInt(canvas.y / scale));
        }

        // The pixel scale has a CEILING, and it is derived
        // --------------------------------------------------------------------
        // PanelTexels divides a FIXED canvas size by the scale, so a higher-DPI screen gets
        // FEWER texels for this window, not more. That is right for everything whose size is
        // authored in texels and scales with the board - and wrong for the two things in here
        // whose size is fixed by a BITMAP FACE, which has exactly one size by construction:
        // the nine school names in the rail, and the eight words in the filter row.
        //
        // Measured against the shipped data before the cap existed: at scale 2 the rail box
        // was 67 texels against a longest name of 62 and the chip row asked 256 into 256. At
        // scale 3 - which is what the shipped HudPixelScaleFor answers for 1080p, i.e. the
        // commonest screen there is - the board fell to 110 and the chip row asked 256 into
        // 96, so every label was squeezed to its ten-texel floor. At scale 4 the rail itself
        // was clamped to 76 and the longest school name overflowed its box by 19.
        //
        // So the window was at its best on the smallest screen and unreadable on a good one,
        // silently, in a direction nobody thinks to test. The cap is the honest trade: the
        // panel is drawn CHUNKIER on a big screen than its neighbours would like, rather than
        // being drawn at a scale where it cannot spell its own contents. Widening the panel
        // instead is not available - PanelLeft/PanelRight are pinned to the character sheet's
        // tab strip, which belongs to another assembly.

        /// <summary>
        /// The panel width, in texels, below which the window cannot lay itself out:
        /// an unclamped rail, its card, and a board wide enough for the filter row.
        /// </summary>
        internal static int MinPanelWidth(GrimoireStyle style, int boardMinTexels)
        {
            int pad = Mathf.Max(0, style.paddingTexels) * 2;

            // Split gives the rail at most a third of the inner column, so anything narrower
            // CLAMPS it - and a clamped rail is a truncated school name.
            int unclampedRail = pad + style.railWidthTexels * 3;

            int roomForAll = pad + style.railWidthTexels + style.cardWidthTexels
                           + ColumnGap * 2 + Mathf.Max(0, boardMinTexels);

            return Mathf.Max(unclampedRail, roomForAll);
        }

        /// <summary>
        /// The panel height, in texels, below which the window cannot lay itself out: a band
        /// that holds the tallest constellation and the filter row, plus the two bands and the
        /// padding above and below them.
        /// </summary>
        internal static int MinPanelHeight(GrimoireStyle style, int bandMinTexels)
        {
            return Mathf.Max(0, style.paddingTexels) * 2
                 + Mathf.Max(0, style.titleBarTexels)
                 + Mathf.Max(0, style.footerTexels)
                 + Mathf.Max(0, bandMinTexels);
        }

        /// <summary>
        /// The tallest thing the band must hold: the constellation, the filter row under it,
        /// and the rail beside it — which does not scroll, so nine schools of
        /// <c>railRowTexels</c> is a hard floor of its own.
        /// </summary>
        internal static int BandMinTexels(GrimoireStyle style, int schoolTexels, int railRows)
        {
            int forBoard = Mathf.Max(0, schoolTexels) + Mathf.Max(0, style.filterChipTexels);
            int forRail = railRows > 0
                ? railRows * style.railRowTexels + (railRows - 1) * style.railRowGapTexels
                : 0;
            return Mathf.Max(forBoard, forRail);
        }

        /// <summary>
        /// The largest scale at or below <paramref name="requested"/> whose panel can still
        /// hold its fixed-size content, in BOTH axes. Never returns less than 1.
        ///
        /// <para>Both axes, because the width happened to be the binding one for the shipped
        /// data and that is luck rather than a reason: measured at scale 3 the panel is
        /// 320x176, which fails the width by 160 AND the band by 44 — the nine rail rows alone
        /// need 170 texels against 142 of column, and <c>RebuildRail</c> answers a row that
        /// would land at a negative y by breaking out of its loop, i.e. by dropping schools off
        /// the bottom in silence. A cap that is right for one axis by coincidence stops being
        /// right the first time anybody retunes a row height.</para>
        /// </summary>
        internal static int PixelScaleFor(int requested, GrimoireStyle style,
                                          int boardMinTexels, int bandMinTexels)
        {
            int needW = MinPanelWidth(style, boardMinTexels);
            int needH = MinPanelHeight(style, bandMinTexels);

            for (int scale = Mathf.Max(1, requested); scale > 1; scale--)
            {
                var texels = PanelTexels(scale);
                if (texels.x >= needW && texels.y >= needH) return scale;
            }
            return 1;
        }

        /// <summary>The three columns and two bands of the window, all in texels.</summary>
        internal readonly struct Frame
        {
            internal readonly RectInt Rail;
            internal readonly RectInt Board;
            internal readonly RectInt Card;
            internal readonly RectInt Title;
            internal readonly RectInt Footer;

            internal Frame(RectInt rail, RectInt board, RectInt card, RectInt title, RectInt footer)
            {
                Rail = rail; Board = board; Card = card; Title = title; Footer = footer;
            }
        }

        /// <summary>
        /// Splits the panel into rail | board | card, with a title band above and a footer
        /// below. The BOARD takes whatever is left, because it is the only part whose content
        /// can be scaled to fit — squeezing the rail truncates school names, which is the
        /// defect this window was rebuilt out of.
        /// </summary>
        internal static Frame Split(Vector2Int panel, GrimoireStyle style)
        {
            int pad = Mathf.Max(0, style.paddingTexels);
            int title = Mathf.Max(0, style.titleBarTexels);
            int footer = Mathf.Max(0, style.footerTexels);

            int innerW = Mathf.Max(0, panel.x - pad * 2);
            int bandBottom = pad + footer;
            int bandHeight = Mathf.Max(0, panel.y - pad * 2 - title - footer);

            int rail = Mathf.Min(style.railWidthTexels, Mathf.Max(0, innerW / 3));
            int card = Mathf.Min(style.cardWidthTexels, Mathf.Max(0, (innerW - rail) / 2));
            int board = Mathf.Max(0, innerW - rail - card - ColumnGap * 2);

            return new Frame(
                rail:   new RectInt(pad, bandBottom, rail, bandHeight),
                board:  new RectInt(pad + rail + ColumnGap, bandBottom, board, bandHeight),
                card:   new RectInt(pad + rail + ColumnGap + board + ColumnGap, bandBottom, card, bandHeight),
                title:  new RectInt(pad, panel.y - pad - title, innerW, title),
                footer: new RectInt(pad, pad, innerW, footer));
        }

        // ── The rail row, as arithmetic ──────────────────────────────────────
        // These were literals inside the builder, and both of them overflowed on a rendered
        // frame before anybody could see the number: the longest school name inked 62 texels
        // into a box of 56. Stated here, "does the longest name fit" is a question a test can
        // ask of the shipped data and the shipped font, with no canvas and no layout pass.

        /// <summary>The school mark's drawn size. Its NATIVE 9 — a point-filtered draw at any
        /// other size drops or repeats rows, which is what took the arms off the martial X.</summary>
        internal const int RailSigilTexels = 9;

        /// <summary>Left margin, and the gap between the mark and the name.</summary>
        internal const int RailInset = 4, RailSigilGap = 3, RailNameGap = 2;

        /// <summary>Room kept for "0/8" on the right of the row.</summary>
        internal const int RailCountTexels = 15;

        /// <summary>Where the name starts inside a rail row.</summary>
        internal static int RailNameX => RailInset + RailSigilTexels + RailSigilGap;

        /// <summary>
        /// How much room a school's name actually gets, from the rail's REAL width.
        ///
        /// <para>It takes the measured width rather than the style's, because
        /// <see cref="Split"/> clamps the rail to a third of the inner column — so a style
        /// whose rail is wider than the window allows would have this answer a box the row
        /// never gets, and the name would overflow again while the number said it fitted.</para>
        /// </summary>
        internal static int RailNameBox(int railWidth) =>
            Mathf.Max(0, railWidth - RailNameX - RailNameGap - RailCountTexels);

        // ── The filter row, as arithmetic ────────────────────────────────────

        /// <summary>Padding added to each chip's own ink.</summary>
        internal const int ChipPadding = 3;

        /// <summary>
        /// Room the chips have once the gaps between them are paid.
        ///
        /// <para>The chips are sized by their own ink, which is right — the words are not the
        /// same length — and it is exactly why the TOTAL has to be checked: they fitted for two
        /// iterations and then the rail took width from the board and the eighth chip was cut
        /// in half by the panel edge. A layout whose pieces are sized independently is correct
        /// until another piece moves.</para>
        /// </summary>
        internal static int FilterAvailable(int boardWidth, int chipCount, int gap) =>
            Mathf.Max(0, boardWidth - gap * Mathf.Max(0, chipCount - 1));

        /// <summary>A chip narrower than this cannot hold two glyphs and a chamfer.</summary>
        internal const int ChipMinTexels = 10;

        /// <summary>
        /// The board width at which the filter row needs no squeeze at all — every word at
        /// its own ink, every gap paid.
        ///
        /// <para>It is measured from the SHIPPED strings and the SHIPPED face rather than
        /// declared, because the row's whole design is that a chip is as wide as its own word:
        /// a constant here would be a second opinion about the same quantity, and the one that
        /// is wrong is always the one nobody re-measured after a translation.</para>
        /// </summary>
        internal static int FilterRowIdealWidth(int chipCount, int gap, int inkTotal) =>
            inkTotal + ChipPadding * Mathf.Max(0, chipCount)
                     + gap * Mathf.Max(0, chipCount - 1);

        /// <summary>
        /// Trims <paramref name="widths"/> in place until they sum to at most
        /// <paramref name="available"/>, taking from each chip IN PROPORTION to what it has.
        ///
        /// <para>Proportional rather than left-to-right because the point of sizing a chip by
        /// its own ink is that the longest word stays the widest chip, and a greedy walk from
        /// the left hands the whole overflow to whichever chip happens to come first — which
        /// on this row is "TODO", the shortest word on it.</para>
        ///
        /// <para>Integer division leaves a remainder, so a second pass takes one texel at a
        /// time from the widest chips until the total fits. Without it a row can stay a texel
        /// or two over, which is exactly the amount that shows as a clipped chamfer and
        /// nothing else.</para>
        /// </summary>
        internal static void SqueezeToFit(int[] widths, int available)
        {
            if (widths == null || widths.Length == 0) return;

            int total = 0;
            for (int i = 0; i < widths.Length; i++) total += widths[i];
            if (total <= available) return;

            int over = total - available;
            int spare = 0;
            for (int i = 0; i < widths.Length; i++)
                spare += Mathf.Max(0, widths[i] - ChipMinTexels);
            if (spare <= 0) return;   // every chip is already at the floor; nothing to give

            int take = Mathf.Min(over, spare);

            for (int i = 0; i < widths.Length; i++)
            {
                int chipSpare = Mathf.Max(0, widths[i] - ChipMinTexels);
                widths[i] -= (int)((long)take * chipSpare / spare);
            }

            // The remainder, one texel at a time off whoever is widest.
            total = 0;
            for (int i = 0; i < widths.Length; i++) total += widths[i];
            while (total > available)
            {
                int widest = -1;
                for (int i = 0; i < widths.Length; i++)
                    if (widths[i] > ChipMinTexels && (widest < 0 || widths[i] > widths[widest]))
                        widest = i;
                if (widest < 0) break;
                widths[widest]--;
                total--;
            }
        }

        // ── The constellation ────────────────────────────────────────────────

        /// <summary>
        /// Distance between two depth steps, derived from the node's own size.
        ///
        /// <para>2.0 and not 2.3. Measured across the nine shipped schools, WIDTH is the axis
        /// that binds the fit — the tightest board was 249 texels in 258 of usable viewport,
        /// 3.6 % of headroom, while vertically there was 40 %. So the depth step is what pays
        /// for a bigger node, and shortening it from 2.3 to 2.0 bought the socket 18 %.</para>
        /// </summary>
        internal static float DepthSpacing(GrimoireStyle style) => style.nodeTexels * 2.0f;

        /// <summary>
        /// Distance between two siblings, which is the VERTICAL step.
        ///
        /// <para>A node's caption block is THREE lines — two for the name, one for the reason —
        /// so the spacing has to clear the socket plus all three plus their gaps, or the block
        /// falls across the node below. Captured live at 2.6 caption heights, it did.</para>
        /// </summary>
        internal static float SiblingSpacing(GrimoireStyle style) =>
            style.nodeTexels + style.captionTexels * 3.4f + 6f;

        /// <summary>
        /// Width of a node's two caption lines.
        ///
        /// <para>Derived from the DEPTH step, which is the HORIZONTAL one — depth runs along X
        /// and siblings along Y. Sizing it off the sibling step instead is measuring the wrong
        /// axis, and it shows: captured live, "Slash (Stab)", "Slash (Cleave)" and "Slash
        /// (Combo)" were printed on top of each other because each caption was 1.6 sibling
        /// steps wide against a depth step half that. Kept under the step so two neighbouring
        /// names can never touch.</para>
        /// </summary>
        internal static float CaptionWidth(GrimoireStyle style) =>
            Mathf.Max(8f, DepthSpacing(style) - 4f);

        /// <summary>How far a node's halo and captions reach past its own centre.</summary>
        internal static float ReachUp(GrimoireStyle style) => style.nodeTexels * 0.93f;

        internal static float ReachDown(GrimoireStyle style) =>
            style.nodeTexels * 0.5f + 2f + style.captionTexels * 3f + 1f;

        internal static float ReachSide(GrimoireStyle style) =>
            Mathf.Max(style.nodeTexels * 0.93f, CaptionWidth(style) * 0.5f);

        /// <summary>Where a link has to stop so it is not drawn across a socket's face.</summary>
        internal static float RimRadius(GrimoireStyle style) =>
            style.nodeTexels * 0.5f * SpellGraphSprites.SOCKET_OUTER_R;

        /// <summary>One school's board, measured around its placements.</summary>
        internal readonly struct Board
        {
            internal readonly Vector2 Size;
            private readonly float _spanX;
            private readonly float _spanY;
            private readonly float _minSlot;
            private readonly float _shiftY;
            private readonly float _depth;
            private readonly float _sibling;

            internal Board(Vector2 size, float spanX, float spanY, float minSlot, float shiftY,
                         float depth, float sibling)
            {
                Size = size; _spanX = spanX; _spanY = spanY;
                _minSlot = minSlot; _shiftY = shiftY; _depth = depth; _sibling = sibling;
            }

            /// <summary>
            /// Board-local position of one placement. Depth grows RIGHT (a root on the left,
            /// its consequences to the right — the direction this language reads), siblings
            /// grow DOWN so a root sits at the top of the fan it opens.
            /// </summary>
            internal Vector2 Position(SpellGraphLayout.Placement p) => new Vector2(
                p.Row * _depth - _spanX * 0.5f,
                _spanY * 0.5f + _shiftY - (p.Column - _minSlot) * _sibling);
        }

        /// <summary>
        /// Sizes the board around a school.
        ///
        /// <para>The vertical reaches are ASYMMETRIC — two caption lines hang below and only
        /// the halo reaches above — so the content is nudged by half that difference to sit
        /// centred inside a box whose own pivot is its middle. Skipping the nudge leaves every
        /// school riding a few texels high inside its own frame, which the fit then centres
        /// wrongly.</para>
        /// </summary>
        internal static Board Measure(IReadOnlyList<SpellGraphLayout.Placement> placements,
                                    GrimoireStyle style)
        {
            float depth = DepthSpacing(style);
            float sibling = SiblingSpacing(style);
            if (placements == null || placements.Count == 0)
                return new Board(Vector2.one, 0f, 0f, 0f, 0f, depth, sibling);

            float minSlot = float.MaxValue, maxSlot = float.MinValue;
            int maxDepth = 0;
            for (int i = 0; i < placements.Count; i++)
            {
                minSlot = Mathf.Min(minSlot, placements[i].Column);
                maxSlot = Mathf.Max(maxSlot, placements[i].Column);
                maxDepth = Mathf.Max(maxDepth, placements[i].Row);
            }

            float spanX = maxDepth * depth;
            float spanY = (maxSlot - minSlot) * sibling;
            float up = ReachUp(style), down = ReachDown(style), side = ReachSide(style);

            var size = new Vector2(spanX + side * 2f, spanY + up + down);
            return new Board(size, spanX, spanY, minSlot, (down - up) * 0.5f, depth, sibling);
        }

        /// <summary>
        /// The scale that seats a board inside a viewport with a margin all round.
        ///
        /// <para>Capped at 1 rather than at a larger number: this board is already authored at
        /// the size it should read, so scaling a small school UP would make one school's nodes
        /// bigger than another's and a node would visibly resize on every rail click. It is NOT
        /// clamped at the bottom — a school too big to fit must still be shown whole, because
        /// the shape is the one thing the board exists to show.</para>
        /// </summary>
        internal static float FitZoom(Vector2 board, Vector2 viewport, float margin)
        {
            float usableW = viewport.x - margin * 2f;
            float usableH = viewport.y - margin * 2f;
            if (board.x < 1f || board.y < 1f || usableW < 1f || usableH < 1f) return 1f;
            return Mathf.Min(1f, Mathf.Min(usableW / board.x, usableH / board.y));
        }
    }
}
