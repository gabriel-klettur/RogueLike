using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The funnel: a stack of spinning arcs that reads as a tornado standing on the caster.
    ///
    /// <para>WHY IT IS NOT MADE OF MOTES. The rest of this rig places points, and a cloud of
    /// points has no silhouette — a tornado is recognised by its OUTLINE, a cone narrow at the
    /// floor and flared at the top, before any debris is noticed at all. Each band is a partial
    /// ring (<see cref="TornadoSprites"/>) placed at its own height and radius along that cone.</para>
    ///
    /// <para>EVERY BAND IS TWO TRANSFORMS, and the split is the whole trick. The PARENT carries
    /// the position and the vertical squash that turns a circle into the ellipse a top-down
    /// camera sees; the CHILD carries the spin. Put the rotation on the squashed transform
    /// instead and the ellipse turns like a wheel — corners rising and falling — rather than
    /// the arc running around its rim, which is the one motion that says "spinning" here.</para>
    ///
    /// <para>Upper bands lead the lower ones slightly. A funnel whose every height shares one
    /// angle is a rigid cone being rotated; the lag is what makes it read as air being dragged
    /// round, and it is the difference between a shape and a wind.</para>
    /// </summary>
    internal sealed partial class SpellCastFlourishFX
    {
        private const int FUNNEL_ORDER = 59;

        /// <summary>
        /// How flat the funnel's rings are drawn. The camera looks down at a shallow angle, so
        /// a horizontal circle is a wide, thin ellipse — the same reason the ki aura's ground
        /// pulses are flattened and its body-facing sphere is not.
        /// </summary>
        private const float FUNNEL_SQUASH = 0.34f;

        /// <summary>Extra degrees per second the top of the funnel leads the bottom by.</summary>
        private const float FUNNEL_TWIST = 0.45f;

        /// <summary>
        /// The gather draws the SAME <c>TornadoSprites</c> bands the field does, so doubling
        /// their weight doubled the light here too. Same constant, same reason as
        /// <c>VortexFunnelFX.BAND_AREA_COMPENSATION</c>: leaving one of the two uncompensated
        /// makes the cast and the field it hands over to disagree on brightness.
        /// </summary>
        private const float BAND_AREA_COMPENSATION = 0.5f;

        private Transform[] _bandPivots;      // position + squash
        private Transform[] _bandSpinners;    // rotation only
        private SpriteRenderer[] _bandRenderers;
        private float[] _bandHeight01;        // 0 at the floor, 1 at the flared top
        private float[] _bandPhase;

        private void BuildFunnel()
        {
            if (!CastFlourishPieces.IsOn(_profile, CastFlourishPieces.Funnel)) return;
            int count = _profile.FunnelBands;

            TornadoSprites.EnsureAll();

            _bandPivots = new Transform[count];
            _bandSpinners = new Transform[count];
            _bandRenderers = new SpriteRenderer[count];
            _bandHeight01 = new float[count];
            _bandPhase = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0.5f : i / (count - 1f);
                _bandHeight01[i] = t;
                // Spread the starting angles so the stack never resolves into one seam.
                _bandPhase[i] = t * 210f + (i % 2 == 0 ? 0f : 95f);

                var pivot = new GameObject("FunnelBand" + i).transform;
                pivot.SetParent(transform, false);

                var spinner = new GameObject("Spin").transform;
                spinner.SetParent(pivot, false);

                var sr = spinner.gameObject.AddComponent<SpriteRenderer>();
                sr.sprite = TornadoSprites.Band(i);
                sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
                sr.sortingLayerName = SortingConfig.LAYER_VFX;
                // Under the hands and the burst: the caster works IN FRONT of their own wind.
                sr.sortingOrder = FUNNEL_ORDER;
                sr.color = WithAlpha(Color.Lerp(_palette.core, _palette.glow, t), 0f);

                _bandPivots[i] = pivot;
                _bandSpinners[i] = spinner;
                _bandRenderers[i] = sr;
            }
        }

        /// <summary>
        /// Advance the funnel. It rises out of the ground over the gather, holds through the
        /// cast, and is torn apart on the release rather than fading — a vortex that dims in
        /// place looks switched off, where one that flies apart looks spent.
        /// </summary>
        private void UpdateFunnel(float gather, float punch, float afterglow, bool released)
        {
            if (_bandRenderers == null) return;

            // Grows upward out of the floor. Below its own height the band is simply not there
            // yet, which is what makes it climb rather than fade in all at once.
            float grown = EaseOutCubic(gather);
            float spread = released ? 1f + afterglow * 1.9f : 1f;

            for (int i = 0; i < _bandRenderers.Length; i++)
            {
                float t = _bandHeight01[i];

                float reveal = Mathf.Clamp01((grown - t * 0.55f) / 0.45f);
                if (reveal <= 0f) { SetAlpha(_bandRenderers[i], 0f); continue; }

                // The cone: narrow where it touches down, flared where it opens.
                float radius = Mathf.Lerp(_profile.FunnelBaseRadius, _profile.FunnelTopRadius,
                                          Mathf.Pow(t, 0.75f)) * spread;
                float height = _profile.FunnelHeight * t * grown;

                // Anchored at the caster's feet, not their centre: a tornado stands ON the
                // ground. transform.position already sits at the owner's origin.
                _bandPivots[i].localPosition = new Vector3(0f, height, 0f);
                _bandPivots[i].localScale = new Vector3(radius * 2f, radius * 2f * FUNNEL_SQUASH, 1f);

                float spin = _profile.FunnelSpin * (1f + t * FUNNEL_TWIST);
                _bandSpinners[i].localRotation =
                    Quaternion.Euler(0f, 0f, _bandPhase[i] + _age * spin);

                // Densest through the middle of the column: the top is where it disperses and
                // the very bottom is hidden by the caster's own feet.
                float body = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * 0.55f + 0.45f;
                float alpha = reveal * body * (0.42f + 0.30f * punch) * BAND_AREA_COMPENSATION;
                if (released) alpha *= Mathf.Pow(afterglow, 0.8f);

                SetAlpha(_bandRenderers[i], alpha);
            }
        }

        private static void SetAlpha(SpriteRenderer renderer, float alpha)
        {
            if (renderer == null) return;
            var color = renderer.color;
            color.a = Mathf.Clamp01(alpha);
            renderer.color = color;
        }
    }
}
