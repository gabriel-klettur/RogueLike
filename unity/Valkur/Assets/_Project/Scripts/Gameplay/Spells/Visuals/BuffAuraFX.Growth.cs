using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// <b>Growth</b> — bark climbing the body from the feet.
    ///
    /// <para>THE MAIN LAYER IS OPAQUE, and that inversion is the whole spell. Every other rig
    /// in this folder is light with at most one dark layer in it; here the matter is the
    /// subject and the single additive thing is one faint leaf every 0.6 s. Make the tendrils
    /// additive and the character is wearing a green magic shell — which is what the previous
    /// implementation drew, because it set <c>SharedAdditiveMaterial</c> on every layer it
    /// built regardless of what the spell was.</para>
    ///
    /// <para>IT GROWS, over 0.8 s, staggered strip by strip. Bark that is simply present on
    /// frame one is bark that appeared, and the growing is the only part of a defensive buff
    /// the player has time to read before the fight resumes.</para>
    ///
    /// <para>NO LIGHT. Bark does not glow, and the rig it replaced fired a 3.1 u point light
    /// unconditionally — so the one character in the game made of wood was also the one lit
    /// like a lantern.</para>
    ///
    /// <para>FRONT AND BACK, sorted against the caster's live order, for the same reason the
    /// shell is: strips that all draw over the body are a decal painted on the character
    /// rather than something wrapped around them.</para>
    /// </summary>
    internal sealed partial class BuffAuraFX
    {
        /// <summary>How far apart in seconds successive strips start climbing.</summary>
        private const float TENDRIL_STAGGER = 0.07f;

        /// <summary>Alpha of a strip, near side and far side. Opaque enough to be matter.</summary>
        private const float TENDRIL_FRONT = 0.94f;
        private const float TENDRIL_BACK = 0.55f;

        private SpriteRenderer[] _tendrils;
        private float[] _tendrilHeight;
        private float[] _tendrilWidth;
        private float[] _tendrilDelay;
        private bool[] _tendrilInFront;
        private Color[] _tendrilColor;

        private void ClearGrowthState()
        {
            _tendrils = null;
            _tendrilHeight = null;
            _tendrilWidth = null;
            _tendrilDelay = null;
            _tendrilInFront = null;
            _tendrilColor = null;
        }

        private void BuildGrowth()
        {
            int n = Mathf.Max(3, _profile.PieceCount);
            _tendrils = new SpriteRenderer[n];
            _tendrilHeight = new float[n];
            _tendrilWidth = new float[n];
            _tendrilDelay = new float[n];
            _tendrilInFront = new bool[n];
            _tendrilColor = new Color[n];

            float rx = _size.x * 0.5f * _profile.StandOff;
            float feet = -_size.y * 0.5f;

            for (int i = 0; i < n; i++)
            {
                // Fixed angles: bark does not turn. The half-step offset stops the strips
                // pairing up symmetrically, which would read as a costume rather than growth.
                float a = (i + 0.37f) * Mathf.PI * 2f / n;
                float x = Mathf.Cos(a);
                float depth = Mathf.Sin(a);
                _tendrilInFront[i] = depth > 0f;

                // Strips nearer the silhouette edge are shorter, so the outline of the wood
                // follows the outline of the body instead of forming a flat palisade.
                _tendrilHeight[i] = _size.y * Mathf.Lerp(0.46f, 0.82f, Mathf.Abs(depth));
                _tendrilWidth[i] = _size.x * Mathf.Lerp(0.16f, 0.26f, Mathf.Abs(depth));
                _tendrilDelay[i] = i * TENDRIL_STAGGER;

                // Soil at the roots, bark up the body: the ramp runs dark to living, which is
                // the one thing that stops seven identical strips reading as one texture.
                _tendrilColor[i] = Color.Lerp(_profile.Bark.Soil, _profile.Bark.Bark,
                                              Mathf.Abs(depth));

                var sr = MakeSprite(_root, "Tendril" + i, RootSprites.Tendril, _tendrilColor[i],
                                    SortingConfig.LAYER_ENTITIES, ORDER_INFRONT_CASTER,
                                    additive: false);
                sr.transform.localPosition = new Vector3(x * rx, feet, 0f);
                // The sprite bends to the RIGHT in texture space, so half the strips are
                // mirrored — otherwise every stem on the character leans the same way.
                float mirror = (i % 2 == 0) ? 1f : -1f;
                sr.transform.localRotation = Quaternion.Euler(0f, 0f, -x * 12f);
                sr.transform.localScale = new Vector3(mirror * 0.001f, 0.001f, 1f);
                _tendrils[i] = sr;
            }
        }

        private void RebaseGrowthOrders(int casterOrder)
        {
            if (_profile.Silhouette != BuffSilhouette.Growth || _tendrils == null) return;
            for (int i = 0; i < _tendrils.Length; i++)
                _tendrils[i].sortingOrder = casterOrder +
                    (_tendrilInFront[i] ? ORDER_INFRONT_CASTER : ORDER_BEHIND_CASTER);
        }

        private void TickGrowth(float onset, float warn)
        {
            if (_tendrils == null) return;

            float span = Mathf.Max(0.01f, _profile.OnsetSeconds);

            for (int i = 0; i < _tendrils.Length; i++)
            {
                // Each strip runs its OWN clock off the shared onset, so the wood climbs in a
                // ripple around the body rather than all at once.
                float local = Mathf.Clamp01((_age - _tendrilDelay[i]) / span);
                float grow = EaseOutCubic(local);

                // The warning is a WITHER: the strips retract and their colour falls back
                // toward soil. A fade would say the bark turned transparent, which wood
                // cannot do; shrinking says it is dying back.
                float wither = Mathf.Lerp(1f, 0.35f, warn);
                float height = _tendrilHeight[i] * grow * wither;
                float mirror = (i % 2 == 0) ? 1f : -1f;

                float sx = mirror * (_tendrilWidth[i] / RootSprites.TendrilWorldWidth)
                         * Mathf.Lerp(0.55f, 1f, grow);
                _tendrils[i].transform.localScale = new Vector3(sx, Mathf.Max(0.001f, height), 1f);

                Color c = Color.Lerp(_tendrilColor[i], _profile.Bark.Soil, warn * 0.8f);
                _tendrils[i].color = WithAlpha(c,
                    (_tendrilInFront[i] ? TENDRIL_FRONT : TENDRIL_BACK) * Mathf.Clamp01(grow * 1.6f));
            }
        }
    }
}
