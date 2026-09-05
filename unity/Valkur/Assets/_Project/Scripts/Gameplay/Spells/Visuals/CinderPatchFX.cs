using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// ONE patch of burning ground. A trail is a row of these, and that is the point: a path is
    /// made of independent small fires, not of one big shape stretched along a line.
    ///
    /// <para>THE IGNITION IS DELAYED BY DISTANCE. Each patch is handed a delay proportional to
    /// how far behind the caster it was dropped, so the fire visibly CHASES the footsteps
    /// instead of the whole trail appearing at once. Without it a trail is a decal painted in
    /// one frame; with it the player can see their own path catching light behind them.</para>
    ///
    /// <para>IT DIES FROM THE EDGE INWARD. A patch that fades uniformly is a light being turned
    /// down; a fire goes out at its rim first and holds longest at its heart. The tongues are
    /// ranked by their distance from the patch centre and extinguish in that order.</para>
    ///
    /// <para>THE SCORCH OUTLIVES THE FLAME, by a couple of seconds. That residue is the whole of
    /// what makes the trail feel like it happened to the world rather than to the screen, and it
    /// is this rig's ONE opaque layer — everything else is additive light, and a dark mark on an
    /// additive surface adds nothing at all.</para>
    /// </summary>
    internal sealed class CinderPatchFX
    {
        private const int TONGUES = 5;
        private const int EMBERS = 4;

        private const float GROUND_SQUASH = 0.34f;
        private const float RING_BAND = 0.39f;

        /// <summary>Seconds of delay per world unit between the caster and the drop point.
        /// At the shipped drop spacing this is roughly a tenth of a second between neighbours,
        /// which reads as a fuse running rather than as patches arriving late.</summary>
        public const float IGNITION_SECONDS_PER_UNIT = 0.075f;

        /// <summary>How long the flame takes to reach full height once lit.</summary>
        private const float IGNITE_SECONDS = 0.22f;

        /// <summary>Seconds between the rim going dark and the heart going dark.</summary>
        private const float EDGE_DIE_SPAN = 0.55f;

        /// <summary>How long the scorch stays after the last flame. See the class doc.</summary>
        public const float SCORCH_LINGER = 2.0f;

        private const int ORDER_SCORCH = 38;
        private const int ORDER_GROUND_GLOW = 40;
        private const int ORDER_RING = 41;
        private const int ORDER_TONGUE = 2;
        /// <summary>Derived, never hand-written: an ember has to clear the whole tongue stack.
        /// A literal here is what sank the vortex debris behind its own funnel.</summary>
        private const int ORDER_EMBER = ORDER_TONGUE + TONGUES + 1;

        private Transform _root;
        private float _radius;
        private float _ttl;
        private float _ignitionDelay;
        private ElementPalette _palette;

        private SpriteRenderer _scorch;
        private SpriteRenderer _groundGlow;
        private SpriteRenderer _ring;

        private Transform[] _tongues;
        private SpriteRenderer[] _tongueRenderers;
        private float[] _tongueHeight;
        private float[] _tongueWidth;
        private float[] _tongueRank;        // 0 at the heart, 1 at the rim — the death order
        private float[] _tonguePhase;

        private Transform[] _embers;
        private SpriteRenderer[] _emberRenderers;
        private float[] _emberAge;
        private float[] _emberLife;
        private Vector2[] _emberVelocity;

        private GameObject _lightGo;
        private Component _light;

        private float _age;
        private bool _destroyed;

        /// <summary>Where this patch sits in the world. The controller sweeps damage on it.</summary>
        public Vector3 Position => _root != null ? _root.position : Vector3.zero;

        public float Radius => _radius;

        /// <summary>True while the patch is lit — after its ignition delay and before its ttl
        /// runs out. The controller damages only during this window, so a patch that has not
        /// caught yet cannot burn anybody and a scorch mark cannot either.</summary>
        public bool IsBurning => _age >= _ignitionDelay && _age < _ignitionDelay + _ttl;

        /// <summary>True once even the scorch has faded and the patch can be released.</summary>
        public bool IsSpent => _age >= _ignitionDelay + _ttl + SCORCH_LINGER;

        /// <summary>The scale that puts <see cref="ElementalSprites.Ring"/>'s bright band on a
        /// world radius. Exposed so a test asserts the composition rather than either half.</summary>
        public static float RingSpanFor(float worldRadius) => worldRadius / RING_BAND;

        public static CinderPatchFX Spawn(Transform parent, Vector3 worldPosition, float radius,
            float ttl, float ignitionDelay, ElementPalette palette)
        {
            ElementalSprites.EnsureAll();
            FieldSprites.EnsureAll();
            KiSprites.EnsureAll();

            var go = new GameObject("CinderPatch");
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = worldPosition;
            // Identity scale: every child below carries an absolute world size, which is what
            // keeps the Light2D at the radius it was given.
            go.transform.localScale = Vector3.one;

            var fx = new CinderPatchFX
            {
                _root = go.transform,
                _radius = Mathf.Max(0.25f, radius),
                _ttl = Mathf.Max(0.3f, ttl),
                _ignitionDelay = Mathf.Max(0f, ignitionDelay),
                _palette = palette,
            };

            fx.BuildGround();
            fx.BuildTongues();
            fx.BuildEmbers();
            fx.AttachLight();
            return fx;
        }

        private void BuildGround()
        {
            // The one OPAQUE layer, and the one that outlives the fire.
            float scorchSpan = _radius * 1.85f;
            _scorch = MakeSprite("Scorch", FieldSprites.Scorch, ScorchColor(), 0f,
                ORDER_SCORCH, SortingConfig.LAYER_FLOOR_DECALS, additive: false);
            _scorch.transform.localScale = new Vector3(scorchSpan, scorchSpan * GROUND_SQUASH, 1f);
            _scorch.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

            float glowSpan = _radius * 1.6f;
            _groundGlow = MakeSprite("Embers", ElementalSprites.Glow, _palette.glow, 0f,
                ORDER_GROUND_GLOW, SortingConfig.LAYER_FLOOR_DECALS, additive: true);
            _groundGlow.transform.localScale = new Vector3(glowSpan, glowSpan * GROUND_SQUASH, 1f);

            float ringSpan = RingSpanFor(_radius);
            _ring = MakeSprite("PatchRing", ElementalSprites.Ring, _palette.core, 0f,
                ORDER_RING, SortingConfig.LAYER_FLOOR_DECALS, additive: true);
            _ring.transform.localScale = new Vector3(ringSpan, ringSpan * GROUND_SQUASH, 1f);
        }

        /// <summary>Charcoal: the swatch's HUE at a fraction of its value, never black. A pure
        /// black mark reads as a hole in the tilemap.</summary>
        private Color ScorchColor()
        {
            Color.RGBToHSV(_palette.core, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Mathf.Clamp01(s * 0.55f), Mathf.Clamp01(v * 0.16f + 0.04f));
        }

        private void BuildTongues()
        {
            _tongues = new Transform[TONGUES];
            _tongueRenderers = new SpriteRenderer[TONGUES];
            _tongueHeight = new float[TONGUES];
            _tongueWidth = new float[TONGUES];
            _tongueRank = new float[TONGUES];
            _tonguePhase = new float[TONGUES];

            for (int i = 0; i < TONGUES; i++)
            {
                // One tongue at the heart, the rest scattered outward. A ring of five evenly
                // spaced flames reads as a decoration; a clump with outliers reads as fire.
                float q = i == 0 ? 0f : Mathf.Clamp01(0.30f + Random.value * 0.62f);
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float r = _radius * q * 0.82f;

                var sr = MakeSprite("Tongue" + i, KiSprites.Tongue(i), _palette.core, 0f,
                    ORDER_TONGUE + i, SortingConfig.LAYER_VFX, additive: true);
                sr.transform.localPosition = new Vector3(
                    Mathf.Cos(angle) * r, Mathf.Sin(angle) * r * GROUND_SQUASH, 0f);

                // The heart burns tallest, which is what gives a flat patch a silhouette.
                _tongueHeight[i] = Mathf.Lerp(0.86f, 0.42f, q) * Random.Range(0.85f, 1.15f);
                _tongueWidth[i] = Mathf.Lerp(0.34f, 0.20f, q);
                _tongueRank[i] = q;
                _tonguePhase[i] = Random.Range(0f, 6.28f);

                _tongues[i] = sr.transform;
                _tongueRenderers[i] = sr;
                KiSprites.ScaleTongue(sr.transform, _tongueWidth[i], 0.001f);
            }
        }

        private void BuildEmbers()
        {
            _embers = new Transform[EMBERS];
            _emberRenderers = new SpriteRenderer[EMBERS];
            _emberAge = new float[EMBERS];
            _emberLife = new float[EMBERS];
            _emberVelocity = new Vector2[EMBERS];

            for (int i = 0; i < EMBERS; i++)
            {
                var sr = MakeSprite("Ember" + i, ElementalSprites.Sparkle, _palette.hotCore, 0f,
                    ORDER_EMBER, SortingConfig.LAYER_VFX, additive: true);
                sr.transform.localScale = Vector3.one * Random.Range(0.07f, 0.13f);
                _embers[i] = sr.transform;
                _emberRenderers[i] = sr;
                // Staggered start, or all four leave the ground on the same frame forever.
                _emberAge[i] = Random.Range(0f, 1f);
                RespawnEmber(i);
            }
        }

        private void RespawnEmber(int i)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float r = _radius * Mathf.Sqrt(Random.value) * 0.8f;
            _embers[i].localPosition = new Vector3(
                Mathf.Cos(angle) * r, Mathf.Sin(angle) * r * GROUND_SQUASH, 0f);
            _emberLife[i] = Random.Range(0.55f, 1.05f);
            _emberAge[i] = 0f;
            _emberVelocity[i] = new Vector2(Random.Range(-0.35f, 0.35f), Random.Range(0.7f, 1.5f));
        }

        private void AttachLight()
        {
            var lightType = ElementalProjectileVisual.GetLight2DType();
            if (lightType == null) return;

            _lightGo = new GameObject("PatchLight");
            _lightGo.transform.SetParent(_root, false);
            _lightGo.transform.localScale = Vector3.one;
            try
            {
                _light = _lightGo.AddComponent(lightType);
                var typeProp = ElementalProjectileVisual.GetLight2DLightTypeProp();
                typeProp?.SetValue(_light, System.Enum.ToObject(typeProp.PropertyType, 3));   // Point
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _palette.lightColor);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, _radius * 2.1f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, _radius * 0.25f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.85f);
                SetLightIntensity(0f);
            }
            catch { _light = null; }
        }

        private void SetLightIntensity(float intensity)
        {
            if (_light == null) return;
            try { ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, intensity); }
            catch { }
        }

        // ── frame ────────────────────────────────────────────────────────────────────

        public void Tick(float deltaTime, float fieldFade)
        {
            if (_destroyed) return;
            float dt = Mathf.Max(0f, deltaTime);
            _age += dt;

            float local = _age - _ignitionDelay;
            if (local < 0f) return;                 // not lit yet: nothing is drawn

            float ignite = Mathf.Clamp01(local / IGNITE_SECONDS);
            float flicker = 0.78f + 0.22f * Mathf.PerlinNoise(_age * 6.5f, _radius * 3.1f);
            float alive = 0f;

            AdvanceTongues(local, ignite, flicker, fieldFade, ref alive);
            AdvanceEmbers(dt, local, fieldFade);
            AdvanceGround(local, ignite, flicker, alive, fieldFade);

            SetLightIntensity((0.35f + 1.05f * alive) * flicker * fieldFade);
        }

        private void AdvanceTongues(float local, float ignite, float flicker, float fieldFade,
            ref float aliveOut)
        {
            float alive = 0f;
            for (int i = 0; i < TONGUES; i++)
            {
                // Outer tongues go out first. death = ttl at the heart, ttl - SPAN at the rim.
                float death = _ttl - EDGE_DIE_SPAN * _tongueRank[i];
                float remaining = Mathf.Clamp01((death - local) / 0.30f);
                float amount = ignite * remaining;
                alive = Mathf.Max(alive, amount);

                float wobble = 0.82f + 0.18f * Mathf.Sin(_age * 7.3f + _tonguePhase[i]);
                KiSprites.ScaleTongue(_tongues[i], _tongueWidth[i] * (0.9f + 0.1f * wobble),
                    Mathf.Max(0.001f, _tongueHeight[i] * amount * wobble));
                SetAlpha(_tongueRenderers[i], 0.85f * amount * flicker * fieldFade);
            }
            aliveOut = alive;
        }

        private void AdvanceEmbers(float dt, float local, float fieldFade)
        {
            // Embers stop being thrown once the fire is out, but the ones already in the air
            // finish their rise — cutting them on the frame the flame dies is a hard edge
            // inside a soft ending.
            bool throwing = local < _ttl;
            for (int i = 0; i < EMBERS; i++)
            {
                _emberAge[i] += dt;
                if (_emberAge[i] >= _emberLife[i])
                {
                    if (!throwing) { SetAlpha(_emberRenderers[i], 0f); continue; }
                    RespawnEmber(i);
                }

                Vector3 p = _embers[i].localPosition;
                p.x += _emberVelocity[i].x * dt;
                p.y += _emberVelocity[i].y * dt;
                _embers[i].localPosition = p;

                float t = _emberAge[i] / _emberLife[i];
                SetAlpha(_emberRenderers[i], Mathf.Sin(t * Mathf.PI) * 0.9f * fieldFade);
            }
        }

        private void AdvanceGround(float local, float ignite, float flicker, float alive,
            float fieldFade)
        {
            // The scorch darkens as the patch catches and then simply stays, fading only once
            // even the heart has gone out. It is the record of what happened here.
            float burnt = Mathf.Clamp01(local / 0.45f);
            float linger = Mathf.Clamp01((_ignitionDelay + _ttl + SCORCH_LINGER - _age) / SCORCH_LINGER);
            SetAlpha(_scorch, 0.72f * burnt * linger * fieldFade);

            SetAlpha(_groundGlow, 0.34f * alive * flicker * fieldFade);
            SetAlpha(_ring, (0.16f + 0.10f * flicker) * alive * fieldFade);
        }

        public void Destroy()
        {
            if (_destroyed) return;
            _destroyed = true;
            if (_root == null) return;

            // Destroy is an outright ERROR in Edit Mode, where a test builds this rig directly.
            if (Application.isPlaying) Object.Destroy(_root.gameObject);
            else Object.DestroyImmediate(_root.gameObject);
            _root = null;
            _light = null;
            _lightGo = null;
        }

        private SpriteRenderer MakeSprite(string name, Sprite sprite, Color color, float alpha,
            int order, string layer, bool additive)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = new Color(color.r, color.g, color.b, alpha);
            sr.sharedMaterial = additive
                ? ElementalSprites.SharedAdditiveMaterial
                : ElementalSprites.SharedUnlitMaterial;
            sr.sortingLayerID = SortingLayer.NameToID(layer);
            sr.sortingLayerName = layer;
            sr.sortingOrder = order;
            return sr;
        }

        private static void SetAlpha(SpriteRenderer sr, float alpha)
        {
            if (sr == null) return;
            var c = sr.color;
            c.a = Mathf.Clamp01(alpha);
            sr.color = c;
        }
    }
}
