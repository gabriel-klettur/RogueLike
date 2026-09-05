using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Makes the absorb pool VISIBLE.
    ///
    /// <para>An absorb shield and an invincibility shield are different tools, and the whole
    /// difference is that one is a resource the player can spend. Until this existed
    /// <c>ShieldController.Integrity</c> had zero readers in the project: the pool drained, the
    /// shell broke on the frame it emptied, and nothing in between said how much was left — so
    /// <c>guardian_light</c> was visually indistinguishable from <c>sphere_magic_shield</c>
    /// except in hue and size.</para>
    ///
    /// <para>Three readings move together, because one alone is ambiguous. Falling OPACITY on
    /// its own reads as the effect expiring; accumulating CRACKS on their own read as damage
    /// already taken rather than as capacity remaining; a COOLING colour on its own reads as a
    /// palette change. Together they say "one more hit".</para>
    /// </summary>
    internal sealed partial class ShieldSphereFX
    {
        /// <summary>Cracks appear a quarter at a time, so each is an EVENT rather than a ramp.</summary>
        private const int CRACK_STAGES = 4;

        private const float CRACK_ORDER_BIAS = 3f;

        private readonly SpriteRenderer[] _cracks = new SpriteRenderer[CRACK_STAGES];
        private readonly Vector3[] _crackDirections = new Vector3[CRACK_STAGES];
        private readonly float[] _crackAges = new float[CRACK_STAGES];

        /// <summary>1 at full pool, 0 at break. Always 1 for a pure timer shell.</summary>
        private float _integrity = 1f;
        private int _crackStage;
        private bool _cracksBuilt;

        /// <summary>
        /// Pushed every frame by <see cref="ShieldController"/>. Cheap and idempotent: a timer
        /// shell simply passes 1 forever and nothing below changes.
        /// </summary>
        public void SetIntegrity(float integrity01)
        {
            float next = Mathf.Clamp01(integrity01);
            _integrity = next;

            // Quarters lost, so a crack lands on a BLOW rather than creeping in continuously.
            int stage = Mathf.Clamp(Mathf.FloorToInt((1f - next) * CRACK_STAGES), 0, CRACK_STAGES);
            if (stage <= _crackStage) return;

            EnsureCracksBuilt();
            for (int i = _crackStage; i < stage && i < CRACK_STAGES; i++)
                _crackAges[i] = 0f;
            _crackStage = stage;
        }

        /// <summary>
        /// Facet opacity as the pool drains. It never reaches zero — a shell that fades out
        /// entirely before it breaks tells the player they are already unprotected when they
        /// are not, which is worse than showing nothing.
        /// </summary>
        private float IntegrityAlphaScale => Mathf.Lerp(0.45f, 1f, _integrity);

        /// <summary>
        /// How far the palette has cooled toward its own pale end. Kept as a fraction rather
        /// than a colour so callers stay in charge of which two colours they are blending.
        /// </summary>
        private float IntegrityCool => 1f - _integrity;

        private void EnsureCracksBuilt()
        {
            if (_cracksBuilt || _root == null) return;
            _cracksBuilt = true;

            for (int i = 0; i < CRACK_STAGES; i++)
            {
                // Spread around the sphere rather than clustered, so a nearly-empty shell is
                // visibly damaged from any viewing angle.
                float theta = (i + 0.5f) / CRACK_STAGES * Mathf.PI * 2f + 0.6f;
                float z = Mathf.Lerp(-0.55f, 0.75f, (i * 0.37f) % 1f);
                float ring = Mathf.Sqrt(Mathf.Max(0.05f, 1f - z * z));
                _crackDirections[i] = new Vector3(Mathf.Cos(theta) * ring, Mathf.Sin(theta) * ring, z);

                var go = new GameObject("Crack" + i);
                go.transform.SetParent(_root, false);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = IceSprites.Crack(i);
                sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
                sr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_VFX);
                sr.sortingLayerName = SortingConfig.LAYER_VFX;
                sr.sortingOrder = _baseOrder + FRONT_ORDER + (int)CRACK_ORDER_BIAS;
                sr.color = new Color(1f, 1f, 1f, 0f);
                _cracks[i] = sr;
                _crackAges[i] = 999f;
            }
        }

        /// <summary>
        /// Cracks flare on the blow that opens them and then settle to a thin permanent line,
        /// which is what separates "this just happened" from "this shell is damaged".
        /// </summary>
        private void UpdateCracks(float envelope, float assemble, float breakTime, float deltaTime)
        {
            if (!_cracksBuilt) return;

            for (int i = 0; i < CRACK_STAGES; i++)
            {
                var sr = _cracks[i];
                if (sr == null) continue;

                if (i >= _crackStage)
                {
                    if (sr.color.a != 0f) sr.color = new Color(1f, 1f, 1f, 0f);
                    continue;
                }

                _crackAges[i] += deltaTime;

                // Same sphere maths as the facets, so a crack sits ON the shell rather than
                // beside it: the lattice direction is turned by the shell's own rotation and
                // only its X/Y reach the transform, with Z left to drive depth.
                Vector3 d = _shellRotation * _crackDirections[i];
                sr.transform.localPosition = _config.BodyOffset
                    + new Vector3(d.x, d.y, 0f) * (_config.Radius * FACET_SHELL * assemble);

                float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                sr.transform.localRotation = Quaternion.Euler(0f, 0f, angle);

                float foreshorten = Mathf.Max(0.18f, Mathf.Abs(d.z));
                float birth = Mathf.Clamp01(_crackAges[i] / 0.22f);
                float size = _config.Radius * 0.62f * assemble * Mathf.Lerp(1.5f, 1f, birth);
                sr.transform.localScale = new Vector3(size * foreshorten, size, 1f);

                bool inFront = d.z >= 0f;
                sr.sortingOrder = _baseOrder + (inFront ? FRONT_ORDER : BACK_ORDER)
                                + (int)CRACK_ORDER_BIAS;

                float flare = Mathf.Exp(-Mathf.Pow(_crackAges[i] / 0.26f, 2f));
                float depthFade = inFront ? 1f : 0.38f;
                float alpha = (0.30f + 0.85f * flare) * depthFade * envelope * (1f - breakTime);

                // Cools with the shell for the same reason the rim does — a gold crack on a
                // whitening sphere reads as a separate effect stuck to it.
                var c = Color.Lerp(_config.Palette.Core, Color.white, IntegrityCool * 0.7f);
                sr.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(alpha));
            }
        }
    }
}
