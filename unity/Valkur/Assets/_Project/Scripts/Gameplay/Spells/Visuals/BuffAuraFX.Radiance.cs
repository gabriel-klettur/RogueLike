using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// <b>Radiance</b> — a column of light descending onto the caster, then a held halo.
    ///
    /// <para>SLICES, NOT A LINE. A <c>LineRenderer</c> can bound a shape and can never fill
    /// one, which is the lesson <c>FlameConeFX</c>, <c>IceWallVisual</c> and
    /// <c>VortexFunnelFX</c> each record after shipping the outline version first. The column
    /// is a stack of quads along the vertical axis whose cross extent is the shaft's real
    /// width at that height, overlapping by <see cref="COLUMN_OVERLAP"/> so the stack closes
    /// into one shape rather than reading as separate puffs.</para>
    ///
    /// <para>NARROW AT THE TOP, WIDE AT THE FLOOR — the OPPOSITE taper to a vortex, and that
    /// is the entire difference between something arriving and something being taken away. A
    /// funnel is recognised by its shape long before anything inside it is.</para>
    ///
    /// <para>THE SLICE COUNT IS A RESOLUTION DIAL, NOT A BRIGHTNESS ONE. On an additive stack
    /// a pixel receives the SUM of everything over it, so the per-slice alpha is
    /// <see cref="COLUMN_ALPHA_BUDGET"/> divided by the count. Without that, doubling the
    /// slices would double the brightness and a gold column would wash out to white —
    /// measured on the vortex, whose summed band alpha went 3.99 to 7.97 when its count
    /// doubled.</para>
    ///
    /// <para>THE BUDGET IS NOT WHAT A PIXEL RECEIVES, and the distinction matters when tuning
    /// it. A slice is drawn <see cref="COLUMN_OVERLAP"/> times longer than its own spacing, so
    /// a point on the shaft is covered by about that many slices and no more — roughly
    /// <c>budget / count x overlap</c>, or 0.62 here, not the 3.4 the name suggests. The
    /// remaining headroom under the ~3 ceiling is what the ring, the motes and the
    /// <c>SpellCastFlourishFX</c> playing over the same frames spend.</para>
    ///
    /// <para>NO OPAQUE LAYER, and that is the deliberate exception rather than an oversight.
    /// Every other rig here carries exactly one — the shell's plates, bark's tendrils, the
    /// shout's dust — because one dark thing is what separates "the world is being affected"
    /// from "something is lit". A shaft of light out of the sky is genuinely just light, the
    /// same carve-out <c>ProjectileVisualProfile.Wisp</c> takes.</para>
    ///
    /// <para>THE SUSTAINED HALF IS DELIBERATELY QUIET: one thin ring at chest height and a
    /// mote every 0.7 s, about 12 % duty. A fifteen-second buff that is busy is a
    /// fifteen-second distraction.</para>
    /// </summary>
    internal sealed partial class BuffAuraFX
    {
        private const float COLUMN_ALPHA_BUDGET = 3.40f;

        /// <summary>How much longer than its own spacing a slice is drawn. Below ~1.8 the stack reads as puffs.</summary>
        private const float COLUMN_OVERLAP = 2.20f;

        /// <summary>Seconds the shaft takes to fade out once it has landed, leaving the halo.</summary>
        private const float COLUMN_LINGER = 0.55f;

        /// <summary>Column height above the body centre, as a multiple of the body's own height.</summary>
        private const float COLUMN_RISE = 2.60f;

        private SpriteRenderer[] _columnSlices;
        private float[] _columnDepth;

        private void ClearRadianceState()
        {
            _columnSlices = null;
            _columnDepth = null;
        }

        private void BuildRadiance()
        {
            // The halo orbits at CHEST height rather than at the feet: it is what the column
            // resolved INTO, so it belongs on the body, not on the floor the shaft landed on.
            if (_groundPlane != null)
                _groundPlane.localPosition = new Vector3(0f, _size.y * 0.04f, 0f);

            int n = Mathf.Max(4, _profile.PieceCount);
            _columnSlices = new SpriteRenderer[n];
            _columnDepth = new float[n];

            float top = _size.y * COLUMN_RISE;
            float bottom = -_size.y * 0.5f;
            float span = top - bottom;
            float sliceHeight = span / n * COLUMN_OVERLAP;

            float topHalf = _size.x * 0.10f;
            float bottomHalf = _size.x * 0.5f * _profile.StandOff * 1.55f;

            for (int i = 0; i < n; i++)
            {
                // 0 at the top, 1 at the floor. The descent reads this, so the array order is
                // the order the shaft arrives in.
                float t = i / (n - 1f);
                _columnDepth[i] = t;

                float y = Mathf.Lerp(top, bottom, t);
                float halfWidth = Mathf.Lerp(topHalf, bottomHalf, t * t);

                var sr = MakeSprite(_root, "ColumnSlice" + i, ElementalSprites.Glow,
                                    _profile.Palette.core, SortingConfig.LAYER_VFX,
                                    ORDER_COLUMN + i, additive: true);
                sr.transform.localPosition = new Vector3(0f, y, 0f);
                sr.transform.localScale = new Vector3(halfWidth * 2f, sliceHeight, 1f);
                _columnSlices[i] = sr;
            }
        }

        /// <summary>
        /// <paramref name="warn"/> is deliberately unread: this silhouette's expiry beat is the
        /// ring speeding up and contracting, which the shared <c>TickGroundRing</c> owns. The
        /// column is long gone by then — it lasts about a second of a fifteen-second buff.
        /// </summary>
        private void TickRadiance(float onset, float warn)
        {
            if (_columnSlices == null) return;

            // The shaft is a one-shot arrival, so it is driven off the raw age rather than off
            // the shared onset: it has to be able to outlive its own ramp and fade separately.
            float span = Mathf.Max(0.01f, _profile.OnsetSeconds);
            float fade = 1f - Mathf.Clamp01((_age - span) / COLUMN_LINGER);
            float perSlice = COLUMN_ALPHA_BUDGET / _columnSlices.Length;

            for (int i = 0; i < _columnSlices.Length; i++)
            {
                // A wavefront travelling DOWN. Lighting the whole shaft at once would be a
                // lamp switching on; this is something descending.
                float front = Mathf.Clamp01(onset * 1.15f);
                float arrived = Mathf.Clamp01((front - _columnDepth[i]) * 6f);

                // Brighter at the floor, where the shaft is widest and where it lands. The
                // colour carries the intensity and the alpha stays the coverage budget, so
                // the bottom blooms instead of merely spreading.
                float weight = Mathf.Lerp(0.75f, 1.35f, _columnDepth[i]);
                Color c = _profile.Palette.core * Mathf.Lerp(1f, 1.9f, _columnDepth[i]);
                _columnSlices[i].color = WithAlpha(c, perSlice * weight * arrived * fade);
            }

            // Nothing is destroyed when the shaft goes out. Twelve renderers at alpha zero are
            // cheaper than a teardown, and keeping them means a recast of the same buff can
            // replay the descent without rebuilding the rig.
        }
    }
}
