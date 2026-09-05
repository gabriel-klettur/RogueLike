using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Where the constellation's abstract slots land in board pixels, and how far the board
    /// has to be scaled to sit inside the viewport. Pure arithmetic — no Unity UI, so the
    /// framing can be measured without a canvas, the way <see cref="SpellGraphLayout"/> can.
    ///
    /// <para>DEPTH RUNS LEFT TO RIGHT, AND THAT IS A MEASUREMENT. <see cref="SpellGraphLayout"/>
    /// answers in abstract (depth, slot) pairs and deliberately says nothing about axes;
    /// choosing them is this file's job. All nine shipped schools are five deep and two or
    /// three wide, so depth-on-Y gives a board of aspect 0.52-0.71 inside a viewport whose
    /// aspect is 2.08 — a narrow vertical strip filling a fifth of the width, which is what
    /// this view looked like for as long as it existed. Depth-on-X puts those same boards at
    /// 1.64-2.19 against that same 2.08.</para>
    ///
    /// <para>THE BOARD BOX IS THE DRAWN EXTENT, NOT THE SPAN BETWEEN CENTRES. A node's halo
    /// reaches <see cref="HALO_SCALE"/> beyond its own box and its captions hang below it, so
    /// a box sized from centres alone crops both the moment the fit stops leaving slack — and
    /// leaving slack is exactly what this file removes. Every reach below is DERIVED from the
    /// constant the drawing actually uses, so the frame and the picture cannot drift apart.</para>
    ///
    /// <para>THE MARGIN IS IN VIEWPORT PIXELS, NOT BOARD PIXELS. The old padding lived inside
    /// the board and was therefore scaled by the zoom, so one authored number rendered as 54
    /// screen pixels zoomed out and 216 zoomed in — never the margin anyone asked for.</para>
    /// </summary>
    internal static class SpellGraphGeometry
    {
        /// <summary>Side of one node's own box. Every layer of a node is a scale of this.</summary>
        internal const float NODE_PX = 76f;

        /// <summary>Horizontal step: one prerequisite deeper.</summary>
        internal const float DEPTH_SPACING = 168f;

        /// <summary>Vertical step: one packing slot across.</summary>
        internal const float SIBLING_SPACING = 132f;

        /// <summary>Scale of the halo layer. Load-bearing: it is what a node really reaches.</summary>
        internal const float HALO_SCALE = 1.85f;

        internal const float CAPTION_NAME_DROP = 16f;
        internal const float CAPTION_COST_DROP = 29f;
        internal const float CAPTION_H = 14f;
        internal const float CAPTION_INSET = 8f;

        /// <summary>Clear screen pixels kept between the drawn board and the viewport edge.</summary>
        internal const float VIEW_MARGIN = 24f;

        /// <summary>
        /// How far the auto-fit may scale the board UP.
        ///
        /// <para>It exists so every school renders at the SAME node size. A fit left uncapped
        /// stops at whatever each school's own tightest axis allows, so the two-wide schools
        /// would come out at 167 px a node and the three-wide ones at 137, and a node would
        /// visibly resize on every tab click. Capping below the tightest shipped school's
        /// natural fit makes the cap itself the binding constraint everywhere, so all nine
        /// land identically.</para>
        ///
        /// <para>Measured: the tightest school fits at 1.94, so this leaves ~8 % of headroom.
        /// <c>SpellGraphFramingTests</c> asserts that margin over the shipped catalog, because
        /// a spacing bump that quietly pushed one school under this value would not fail — it
        /// would just make that one school smaller than the other eight.</para>
        /// </summary>
        internal const float FIT_MAX = 1.8f;

        /// <summary>
        /// Where a node ENDS, and therefore where a connector reaching it has to stop.
        ///
        /// <para>Derived from the socket sprite's own outer radius, so retuning the ring moves
        /// the wires with it. Trimming at the rim rather than tucking under it is deliberate:
        /// the ring is NOT opaque — its bevel runs down to 0.42 alpha on the unlit side and
        /// the plate under the icon sits at 0.72 — so a wire hidden "behind" the node is a
        /// wire drawn at up to 58 % straight across its face.</para>
        /// </summary>
        internal static float NodeRimRadius => NODE_PX * 0.5f * SpellGraphSprites.SOCKET_OUTER_R;

        /// <summary>How far a node's halo spills past its own centre.</summary>
        internal static float HaloReach => NODE_PX * HALO_SCALE * 0.5f;

        /// <summary>How far the lower caption's box spills below a node's centre.</summary>
        internal static float CaptionReach => NODE_PX * 0.5f + CAPTION_COST_DROP + CAPTION_H * 0.5f;

        /// <summary>
        /// Width of a node's caption boxes. Kept under the horizontal step so two neighbouring
        /// names can never touch — which only became a risk once depth ran along X.
        /// </summary>
        internal static float CaptionWidth => DEPTH_SPACING - CAPTION_INSET;

        internal static float ReachUp => HaloReach;
        internal static float ReachDown => Mathf.Max(HaloReach, CaptionReach);

        /// <summary>Sideways, the caption is the wider of the two once the depth step grows.</summary>
        internal static float ReachSide => Mathf.Max(HaloReach, CaptionWidth * 0.5f);

        // ── chrome ───────────────────────────────────────────────────────────────────

        internal const float HEADER_H = 38f;
        internal const float FOOTER_H = 26f;
        internal const float RAIL_ROW_H = 24f;
        internal const float RAIL_ROW_SPACING = 2f;
        internal const float RAIL_PAD = 4f;
        internal const float VIEWPORT_INSET = 6f;

        /// <summary>
        /// Narrowest a school tab may get before its label stops being readable. The rail
        /// wraps to whatever number of columns keeps every tab at least this wide.
        /// </summary>
        internal const float MIN_TAB_W = 104f;

        /// <summary>
        /// How many tabs fit across a rail <paramref name="width"/> px wide.
        ///
        /// <para>The count used to be a hardcoded 5, which is right at exactly one window
        /// size. On a 1754 px slab it wrapped nine schools onto two rows when they fit on one
        /// with 193 px each; on a narrow window it would have squeezed five tabs into 60 px
        /// apiece. Two is the floor because one tab per row turns the rail into a list.</para>
        /// </summary>
        internal static int RailColumns(float width, int tabCount)
        {
            if (tabCount <= 0) return 1;
            int fit = Mathf.FloorToInt(Mathf.Max(0f, width) / MIN_TAB_W);
            return Mathf.Clamp(fit, Mathf.Min(2, tabCount), tabCount);
        }

        internal static int RailRows(int tabCount, int columns)
            => tabCount <= 0 ? 0 : Mathf.CeilToInt(tabCount / (float)Mathf.Max(1, columns));

        /// <summary>
        /// Height the rail needs for <paramref name="rows"/> rows.
        ///
        /// <para>Computed from the row count rather than read back off the strip's
        /// <c>LayoutElement</c>, because uGUI performs no layout in Edit Mode — a measured
        /// height there is whatever the RectTransform defaulted to, and the chrome would size
        /// itself against a number that means nothing.</para>
        /// </summary>
        internal static float RailHeight(int rows)
            => rows <= 0 ? 0f : rows * RAIL_ROW_H + (rows - 1) * RAIL_ROW_SPACING;

        /// <summary>
        /// Distance from the top of the slab to the top of the viewport.
        ///
        /// <para>Derived, because the old <c>-100</c> was a guess that happened to clear a
        /// two-row rail. A third row — which any window under ~520 px of rail width produces —
        /// would have been drawn straight over the board.</para>
        /// </summary>
        internal static float ChromeTopInset(float railHeight)
            => HEADER_H + RAIL_PAD * 2f + railHeight;

        /// <summary>One school's board box, and the mapping from its placements into that box.</summary>
        internal readonly struct Frame
        {
            /// <summary>The board's <c>sizeDelta</c> — the full DRAWN extent, halo and captions in.</summary>
            public readonly Vector2 BoardSize;

            private readonly float _spanX, _spanY, _minSlot, _shiftY;

            internal Frame(Vector2 boardSize, float spanX, float spanY, float minSlot, float shiftY)
            {
                BoardSize = boardSize;
                _spanX = spanX;
                _spanY = spanY;
                _minSlot = minSlot;
                _shiftY = shiftY;
            }

            /// <summary>
            /// Board-local position of one placement. Depth grows RIGHT from the left edge;
            /// packing slots grow DOWN, so a root sits at the top of the fan it opens.
            /// </summary>
            public Vector2 Position(SpellGraphLayout.Placement p) => new Vector2(
                p.Row * DEPTH_SPACING - _spanX * 0.5f,
                _spanY * 0.5f + _shiftY - (p.Column - _minSlot) * SIBLING_SPACING);
        }

        /// <summary>
        /// Size the board around a school's placements.
        ///
        /// <para>The vertical reaches are ASYMMETRIC — captions hang below and nothing hangs
        /// above — so the content is nudged by half that difference to sit centred inside a
        /// box whose own pivot is its middle. Skipping the nudge leaves every school riding a
        /// few pixels high inside its own frame, which the fit then centres wrongly.</para>
        /// </summary>
        internal static Frame Measure(IReadOnlyList<SpellGraphLayout.Placement> placements)
        {
            if (placements == null || placements.Count == 0)
                return new Frame(Vector2.one, 0f, 0f, 0f, 0f);

            float minSlot = float.MaxValue, maxSlot = float.MinValue;
            int maxDepth = 0;
            for (int i = 0; i < placements.Count; i++)
            {
                minSlot = Mathf.Min(minSlot, placements[i].Column);
                maxSlot = Mathf.Max(maxSlot, placements[i].Column);
                maxDepth = Mathf.Max(maxDepth, placements[i].Row);
            }

            float spanX = maxDepth * DEPTH_SPACING;
            float spanY = (maxSlot - minSlot) * SIBLING_SPACING;

            var size = new Vector2(spanX + ReachSide * 2f, spanY + ReachUp + ReachDown);
            return new Frame(size, spanX, spanY, minSlot, (ReachDown - ReachUp) * 0.5f);
        }

        /// <summary>
        /// The scale that seats <paramref name="board"/> inside <paramref name="viewport"/>
        /// with <see cref="VIEW_MARGIN"/> of clear screen pixels all round.
        ///
        /// <para>Deliberately NOT clamped at the bottom. A board too big to fit even at the
        /// manual zoom floor must still be shown whole — clamping it up would crop the shape,
        /// which is the one thing this view exists to show. The caller widens its own zoom
        /// floor to whatever this returns instead.</para>
        /// </summary>
        internal static float FitZoom(Vector2 board, Vector2 viewport)
        {
            float usableW = viewport.x - VIEW_MARGIN * 2f;
            float usableH = viewport.y - VIEW_MARGIN * 2f;
            if (board.x < 1f || board.y < 1f || usableW < 1f || usableH < 1f) return 1f;
            return Mathf.Min(FIT_MAX, Mathf.Min(usableW / board.x, usableH / board.y));
        }
    }
}
