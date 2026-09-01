using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The flare that covers a character while their weapon is drawn or stowed.
    ///
    /// Its job is to hide a cut. The swap replaces four sprite sets in a single frame, so
    /// without something over the top the character POPS from one set of hands to the other
    /// — one frame unarmed, the next armed, with nothing in between to read as a cause. A
    /// flash bright enough to wash the body out for a few frames turns that discontinuity
    /// into the reason for itself.
    ///
    /// Five pieces, and each is doing a different job:
    ///
    /// * <b>Bloom</b> over the silhouette, additive, punching in two frames and falling off
    ///   over the rest. This is the part that actually covers the body.
    /// * <b>Halo</b>, wide and faint, so the bloom sits in something instead of ending at a
    ///   hard edge.
    /// * <b>Sweep</b> — a bright band travelling along the body, UP as the weapon comes out
    ///   and DOWN as it goes away. This is the only piece that knows which direction the
    ///   swap went, and it is what stops a draw and a sheathe looking identical.
    /// * <b>Ring</b>, expanding outward from the body's middle: the flash pushing air.
    /// * <b>Motes</b> scattered through the silhouette, twinkling — the detail that makes it
    ///   read as magic rather than as a lighting glitch.
    ///
    /// Plus the body's own colour, driven through <see cref="SpriteTintStack"/> on
    /// <see cref="TintLayer.Equip"/>. That is what makes the character part of the effect
    /// rather than something standing behind it, and going through the stack is what stops
    /// it fighting a burn or a hit-flash that happens to overlap.
    ///
    /// It is world-space and FOLLOWS its owner rather than being parented to it, for two
    /// reasons: <c>allowMovement</c> is on for the toggle spell so the character can walk
    /// through the swap, and parenting would inherit the entity's scale — and scale a
    /// <c>Light2D</c> radius with it, which is the trap <c>WorldLightLoader</c> counter-scales
    /// its way out of.
    /// </summary>
    internal sealed class WeaponSwapFlashFX : MonoBehaviour
    {
        /// <summary>Short on purpose. The equip animation runs 1.2 s; this is punctuation at
        /// the front of it, not a second animation competing with it.</summary>
        private const float DURATION = 0.34f;

        private const int MOTE_COUNT = 22;

        private const int ORDER_HALO  = 60;
        private const int ORDER_RING  = 61;
        private const int ORDER_BLOOM = 62;
        private const int ORDER_SWEEP = 63;
        private const int ORDER_MOTE  = 64;

        /// <summary>Drawing reads as steel catching the light; stowing as it going warm and
        /// settling. Two palettes rather than one so the two halves of the toggle are
        /// distinguishable with the sound off.</summary>
        private static readonly Color DrawTint = new Color(0.68f, 0.90f, 1.00f, 1f);
        private static readonly Color StowTint = new Color(1.00f, 0.82f, 0.46f, 1f);

        private Transform _owner;
        private Vector3 _centerOffset;
        private Vector2 _size;
        private bool _stowing;
        private float _age;

        private Color _tint;
        private Color _hotTint;

        private SpriteRenderer _halo;
        private SpriteRenderer _bloom;
        private SpriteRenderer _ring;
        private SpriteRenderer _sweep;
        private Component _light;

        private SpriteTintStack _bodyTint;
        private Transform[] _moteTransforms;
        private SpriteRenderer[] _moteRenderers;
        private Vector3[] _moteSlots;
        private float[] _motePhase;
        private float[] _moteSpeed;

        // ── Entry point ───────────────────────────────────────────────────────

        /// <summary>
        /// Plays one swap flare on <paramref name="owner"/>. Safe to call on a character with
        /// no sprite: the silhouette falls back to a sensible body-sized box, because a flash
        /// with no flash is a worse failure than one that is slightly the wrong size.
        /// </summary>
        public static void Play(Transform owner, bool stowing)
        {
            if (owner == null) return;

            SpriteRenderer body = ResolveBodyRenderer(owner);
            Vector2 size = body != null && body.sprite != null
                ? (Vector2)body.bounds.size
                : new Vector2(0.9f, 1.6f);
            Vector3 centerOffset = body != null && body.sprite != null
                ? body.bounds.center - owner.position
                : new Vector3(0f, 0.8f, 0f);

            var go = new GameObject("WeaponSwapFlashFX");
            go.transform.position = owner.position + centerOffset;

            var fx = go.AddComponent<WeaponSwapFlashFX>();
            fx._owner = owner;
            fx._centerOffset = centerOffset;
            fx._size = new Vector2(Mathf.Max(0.25f, size.x), Mathf.Max(0.4f, size.y));
            fx._stowing = stowing;
            fx._tint = stowing ? StowTint : DrawTint;
            fx._hotTint = Color.Lerp(fx._tint, Color.white, 0.8f);
            fx._bodyTint = SpriteTintStack.Attach(owner.gameObject);
            fx.BuildRig();
        }

        private static SpriteRenderer ResolveBodyRenderer(Transform owner)
        {
            var sr = owner.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null) return sr;

            foreach (var candidate in owner.GetComponentsInChildren<SpriteRenderer>())
                if (candidate != null && candidate.sprite != null) return candidate;

            return null;
        }

        // ── Construction ──────────────────────────────────────────────────────

        private void BuildRig()
        {
            ElementalSprites.EnsureAll();

            _halo = CreateSprite("Halo", ElementalSprites.Halo, _tint, ORDER_HALO);
            _halo.transform.localScale = new Vector3(_size.x * 3.4f, _size.y * 2.2f, 1f);

            // Ring's bright band peaks at normalized radius 0.78, so a scale of r / 0.39 puts
            // the drawn contour exactly at world radius r — see the ElementalSprites note in
            // CLAUDE.md. Sized from the body's half-height so it starts at the silhouette.
            _ring = CreateSprite("Ring", ElementalSprites.Ring, _tint, ORDER_RING);

            _bloom = CreateSprite("Bloom", ElementalSprites.Glow, _hotTint, ORDER_BLOOM);
            _bloom.transform.localScale = new Vector3(_size.x * 1.9f, _size.y * 1.35f, 1f);

            // Wide and thin: a band across the body, not a blob on it.
            _sweep = CreateSprite("Sweep", ElementalSprites.Glow, Color.white, ORDER_SWEEP);
            _sweep.transform.localScale = new Vector3(_size.x * 2.3f, _size.y * 0.22f, 1f);

            BuildMotes();
            BuildLight();
        }

        private void BuildMotes()
        {
            _moteTransforms = new Transform[MOTE_COUNT];
            _moteRenderers = new SpriteRenderer[MOTE_COUNT];
            _moteSlots = new Vector3[MOTE_COUNT];
            _motePhase = new float[MOTE_COUNT];
            _moteSpeed = new float[MOTE_COUNT];

            for (int i = 0; i < MOTE_COUNT; i++)
            {
                var sr = CreateSprite("Mote_" + i.ToString("00"), ElementalSprites.SparkleStar,
                    Color.Lerp(_tint, Color.white, Random.Range(0.35f, 1f)), ORDER_MOTE);
                sr.transform.localScale = Vector3.one * Random.Range(0.05f, 0.13f);

                // Inside the silhouette and tapering upward, so the scatter has the shape of
                // a person rather than of the box that contains one.
                float heightFraction = Random.value;
                float taper = Mathf.Lerp(1f, 0.6f, heightFraction);
                _moteSlots[i] = new Vector3(
                    Random.Range(-0.5f, 0.5f) * _size.x * taper * 1.1f,
                    (heightFraction - 0.5f) * _size.y,
                    0f);

                _motePhase[i] = heightFraction;
                _moteSpeed[i] = Random.Range(34f, 72f);
                _moteTransforms[i] = sr.transform;
                _moteRenderers[i] = sr;
            }
        }

        private void BuildLight()
        {
            var lightType = ElementalProjectileVisual.GetLight2DType();
            if (lightType == null) return;
            try
            {
                _light = gameObject.AddComponent(lightType);
                var typeProp = ElementalProjectileVisual.GetLight2DLightTypeProp();
                // 3 = Point. The three URP 2D light enum literals are a documented trap; this
                // is the one the other effects in this folder use.
                if (typeProp != null)
                    typeProp.SetValue(_light, System.Enum.ToObject(typeProp.PropertyType, 3));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _hotTint);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, _size.y * 2.6f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.15f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.85f);
            }
            catch { _light = null; }
        }

        private SpriteRenderer CreateSprite(string objectName, Sprite sprite, Color color, int order)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = WithAlpha(color, 0f);
            // Additive, not the unlit alpha material: on alpha the brightest pixel a glow can
            // produce is its own colour, so a flash meant to wash the body out cannot blow
            // out. SharedAdditiveMaterial is SrcAlpha/One, so alpha still fades it.
            sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
            sr.sortingLayerName = SortingConfig.LAYER_VFX;
            sr.sortingOrder = order;
            return sr;
        }

        // ── Per-frame ─────────────────────────────────────────────────────────

        private void Update()
        {
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / DURATION);

            FollowOwner();

            // Two frames up, the rest down. A symmetric curve reads as a pulse; this reads as
            // something igniting, which is what a swap is.
            float punch = t < 0.12f
                ? Mathf.SmoothStep(0f, 1f, t / 0.12f)
                : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.12f) / 0.88f);

            UpdateBloom(punch);
            UpdateRing(t);
            UpdateSweep(t);
            UpdateMotes(t, punch);
            UpdateBody(punch);
            UpdateLight(punch);

            if (_age >= DURATION) Destroy(gameObject);
        }

        /// <summary>
        /// Keeps the flare on the character, who is free to walk through the swap. Losing the
        /// owner mid-cycle (a zone change, a death) is not an error — the flare simply stays
        /// where it was and finishes.
        /// </summary>
        private void FollowOwner()
        {
            if (_owner != null)
                transform.position = _owner.position + _centerOffset;
        }

        private void UpdateBloom(float punch)
        {
            if (_halo != null) _halo.color = WithAlpha(_tint, punch * 0.34f);
            if (_bloom != null) _bloom.color = WithAlpha(_hotTint, punch * punch * 0.85f);
        }

        private void UpdateRing(float t)
        {
            if (_ring == null) return;

            // Expands from just inside the silhouette to well past it, thinning as it goes.
            float radius = Mathf.Lerp(_size.y * 0.30f, _size.y * 1.5f, Mathf.SmoothStep(0f, 1f, t));
            float scale = radius / 0.39f;
            _ring.transform.localScale = new Vector3(scale, scale * 0.72f, 1f);
            _ring.color = WithAlpha(_tint, (1f - t) * (1f - t) * 0.7f);
        }

        private void UpdateSweep(float t)
        {
            if (_sweep == null) return;

            // The one piece that knows which way the swap went. Travels the body's height,
            // bottom to top on a draw and top to bottom on a stow, so the flare agrees with
            // the equip animation — which is itself played backwards when stowing.
            float travel = Mathf.SmoothStep(0f, 1f, t);
            float from = _stowing ? 0.75f : -0.75f;
            float to = _stowing ? -0.75f : 0.75f;
            float y = Mathf.Lerp(from, to, travel) * _size.y;

            _sweep.transform.localPosition = new Vector3(0f, y, 0f);
            // Brightest crossing the middle of the body, gone by the time it leaves.
            _sweep.color = WithAlpha(Color.white, Mathf.Sin(t * Mathf.PI) * 0.9f);
        }

        private void UpdateMotes(float t, float punch)
        {
            // Motes drift the way the sweep travels, so the whole effect moves as one.
            float drift = (_stowing ? -1f : 1f) * t * _size.y * 0.35f;

            for (int i = 0; i < _moteTransforms.Length; i++)
            {
                float born = Mathf.Clamp01((t - _motePhase[i] * 0.25f) / 0.14f);
                float twinkle = 0.4f + 0.6f * Mathf.Abs(Mathf.Sin(_age * _moteSpeed[i] + _motePhase[i] * 6f));

                _moteTransforms[i].localPosition = _moteSlots[i] + Vector3.up * drift;
                _moteRenderers[i].color =
                    WithAlpha(_moteRenderers[i].color, born * punch * twinkle);
            }
        }

        private void UpdateBody(float punch)
        {
            if (_bodyTint == null) return;
            // Toward white, never past it: the stack MULTIPLIES its layers, so a value above
            // one here would brighten whatever else is tinting the character too.
            _bodyTint.Set(TintLayer.Equip, Color.Lerp(Color.white, _hotTint, punch * 0.65f));
        }

        private void UpdateLight(float punch)
        {
            if (_light == null) return;
            try
            {
                ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 3.1f * punch);
            }
            catch { /* URP 2D lighting absent in this project configuration. */ }
        }

        private void OnDestroy()
        {
            // However the cycle ended — finished, scene torn down, character killed mid-swap
            // — the body must not be left holding this effect's tint.
            if (_bodyTint != null) _bodyTint.Clear(TintLayer.Equip);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
