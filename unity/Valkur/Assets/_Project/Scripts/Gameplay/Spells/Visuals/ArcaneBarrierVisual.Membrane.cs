using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The surface itself: the hexagonal panels, the lattice edge that caps them and the faint
    /// haze that lifts the whole plane off the ground behind it.
    /// </summary>
    internal sealed partial class ArcaneBarrierVisual
    {
        /// <summary>
        /// Tile the plane with hexagons in offset COLUMNS.
        ///
        /// <para>The cell size is derived from the barrier's HEIGHT, not fixed: a short barrier
        /// with cells sized for a tall one is two rows of enormous hexagons and reads as a
        /// fence, and a tall one with small cells is a mesh. Deriving it keeps the weave at
        /// three to six rows whatever is authored, which is the band where the eye reads
        /// "woven" rather than "panelled" or "gauze".</para>
        /// </summary>
        /// <summary>
        /// How many rows of hexagons this barrier's height gets. Resolved before ANY part is
        /// built, because <c>OrderFor</c> derives every order above the weave from it.
        /// </summary>
        private int ResolveRows()
            => Mathf.Clamp(Mathf.RoundToInt(_config.Height / TargetRowHeight), MinRows, MaxRows);

        private void BuildPanels()
        {
            float size = _config.Height / (_rows * 2f * PanelHalfHeight);
            float halfWidth = PanelHalfWidth * size;
            float halfHeight = PanelHalfHeight * size;
            float columnPitch = 1.5f * halfWidth;
            float rowPitch = 2f * halfHeight;

            // Column CENTRES span the length minus one cell, so the outer EDGES land exactly
            // on the authored ends. Laying them out over the full length instead puts a
            // half-cell past each end — measured at 5.19 drawn against a 4.50 collider — and
            // this rig shrinks that collider to the span SurvivingHalfSpan reports, which is an
            // edge. Art wider than physics makes the barrier block a strip the player can see
            // straight through, and it is the same "drawn is what blocks" promise the erosion
            // depends on.
            float usable = Mathf.Max(columnPitch, _config.Length - 2f * halfWidth);

            int maxColumns = Mathf.Max(3, MaxPanels / _rows);
            int columns = Mathf.Clamp(Mathf.RoundToInt(usable / columnPitch) + 1, 3, maxColumns);

            // Re-derived so the cells divide that span evenly. Note the budget can bind before
            // the ideal pitch is reached: past roughly fifteen units of length the columns are
            // spaced further apart than a cell is wide and the weave opens up. No shipped wall
            // is close (wall_ice is 6, arcane_barrier 4.5); a longer one would want coarser
            // cells rather than a raised ceiling.
            columnPitch = columns > 1 ? usable / (columns - 1) : columnPitch;

            float bayHalf = _posts.Count > 1
                ? _config.Length / (_posts.Count - 1) * 0.5f
                : _config.Length * 0.5f;

            for (int c = 0; c < columns; c++)
            {
                float along = (c - (columns - 1) * 0.5f) * columnPitch;
                float stagger = (c & 1) == 1 ? rowPitch * 0.5f : 0f;

                for (int r = 0; r < _rows; r++)
                {
                    float up = rowPitch * (r + 0.5f) + stagger;
                    // The staggered column would otherwise poke a half-cell above the authored
                    // height. A cell mostly over the top is dropped; one barely over is kept,
                    // because a perfectly flat top edge reads as a cut rather than as a weave.
                    if (up - halfHeight > _config.Height - halfHeight * 0.35f) continue;

                    CreatePanel(along, up, size, bayHalf, r);
                }
            }
        }

        private void CreatePanel(float along, float up, float size, float bayHalf, int row)
        {
            var go = new GameObject("Panel");
            go.transform.SetParent(_root, false);
            go.transform.localPosition = AlongAxis(along) + new Vector3(0f, up, 0f);
            go.transform.localScale = Vector3.one * size;

            // How far this cell is from whichever post holds it. The weave grows OUT of the
            // anchors and the two halves of a bay meet in its middle, which is what says the
            // posts came first and the membrane is hanging off them.
            float toPost = float.MaxValue;
            for (int i = 0; i < _posts.Count; i++)
                toPost = Mathf.Min(toPost, Mathf.Abs(along - _posts[i].Along));
            float bayFraction = bayHalf > 1e-4f ? Mathf.Clamp01(toPost / bayHalf) : 0f;

            var panel = new Panel
            {
                Root = go.transform,
                Along = along,
                Size = size,
                Variant = _rng.Next(ArcaneSprites.PanelVariants),
                KnitRank = bayFraction,
                Phase = Range(0f, Mathf.PI * 2f),
                // Posts finish rising at ~0.16; the weave starts after that and takes 0.34 to
                // reach the middle of the widest bay. Rows are staggered by a hair so the
                // membrane fills in as a front rather than as a column of cells at once.
                KnitDelay = 0.16f + bayFraction * 0.34f + row * 0.018f + Range(0f, 0.03f),
            };

            panel.Body = Paint(go, ArcaneSprites.Panel(panel.Variant),
                _palette.Weave, additive: true, SortingConfig.LAYER_ENTITIES,
                OrderFor(Part.Panel, row));

            _panels.Add(panel);
        }

        /// <summary>
        /// The lattice: one bright line per bay, running post to post along the top.
        ///
        /// <para>It is the barrier's only hard contour, and a shape without one is a cloud —
        /// the same reason <c>VortexFunnelFX</c> pins a ground ring to a force radius the
        /// silhouette cannot express. It caps the weave at the authored height, so what the
        /// player reads as the top of the barrier is the number the spell was authored with,
        /// not wherever the topmost surviving hexagon happens to be.</para>
        /// </summary>
        private void BuildEdges()
        {
            float angle = Mathf.Atan2(_config.Axis.y, _config.Axis.x) * Mathf.Rad2Deg;

            for (int i = 0; i < _posts.Count - 1; i++)
            {
                float a = _posts[i].Along;
                float b = _posts[i + 1].Along;

                var go = new GameObject("LatticeEdge");
                go.transform.SetParent(_root, false);
                go.transform.localPosition =
                    AlongAxis((a + b) * 0.5f) + new Vector3(0f, _config.Height, 0f);
                go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                // The Edge sprite is 2 x 0.125 world units at scale 1.
                go.transform.localScale = new Vector3((b - a) / 2f, 0.10f / 0.125f, 1f);

                _edges.Add(Paint(go, ArcaneSprites.Edge, _palette.Lattice,
                    additive: true, SortingConfig.LAYER_ENTITIES, OrderFor(Part.Edge)));
            }
        }

        /// <summary>
        /// A few wide, very faint glows BEHIND the weave, spread along the line.
        ///
        /// <para>They do the job a radial rig's halo does — separate the effect from the ground
        /// it stands on — but spread along the barrier, so a long one does not end up with a
        /// bright spot in its middle and nothing at its ends. Deliberately much fainter than
        /// the ice wall's equivalent: this surface is supposed to be SEEN THROUGH, and a haze
        /// heavy enough to notice is the one thing that would stop it.</para>
        /// </summary>
        private void BuildHaze()
        {
            int count = Mathf.Clamp(Mathf.RoundToInt(_config.Length / 1.8f), 2, 8);
            float size = Mathf.Max(1.1f, _config.Height * 1.15f);

            for (int i = 0; i < count; i++)
            {
                float t = (i + 0.5f) / count;
                var go = new GameObject("PlaneHaze");
                go.transform.SetParent(_root, false);
                go.transform.localPosition = AlongAxis((t - 0.5f) * _config.Length) +
                                             new Vector3(0f, _config.Height * 0.48f, 0f);
                go.transform.localScale = new Vector3(size * 1.15f, size, 1f);

                _haze.Add(Paint(go, ElementalSprites.Glow, _palette.Deep,
                    additive: true, SortingConfig.LAYER_ENTITIES, OrderFor(Part.Haze)));
            }
        }
    }
}
