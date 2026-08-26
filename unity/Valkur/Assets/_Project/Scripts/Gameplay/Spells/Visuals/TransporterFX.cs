using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// A transporter cycle, in the Star Trek sense: the body does not vanish behind a
    /// portal, it is taken apart into a shimmering column of light and put back together
    /// somewhere else.
    ///
    /// Two things carry that reading and both are essential. The first is that the
    /// character's own sprite fades — a departure whose body stays solid is a body that
    /// walked off, not one that was beamed away. The second is that the sparkles occupy the
    /// silhouette the body left behind: they are distributed through its bounds rather than
    /// puffed from a point, so the column has the shape of the person inside it.
    ///
    /// One class covers both ends of the trip. Dematerialising drives a ghost copy of the
    /// sprite left at the origin; materialising drives the real renderer at the destination
    /// and restores its colour when it is done.
    /// </summary>
    internal sealed class TransporterFX : MonoBehaviour
    {
        internal enum Mode { Dematerialize, Materialize }

        private const float DURATION = 0.36f;
        private const int SPARKLE_COUNT = 54;

        /// <summary>When the arrival chirp plays, as a fraction of the cycle.</summary>
        private const float ARRIVE_SFX_AT = 0.14f;

        private const int ORDER_PAD = 48;
        private const int ORDER_HAZE = 49;
        private const int ORDER_GHOST = 52;
        private const int ORDER_CORE = 53;
        private const int ORDER_SPARKLE = 56;

        private Mode _mode;
        private float _age;
        private Color _tint;
        private Color _hotTint;
        private Vector2 _size;

        private SpriteRenderer _ghost;
        private SpriteTintStack _bodyTint;
        private bool _arriveSfxPlayed;

        private SpriteRenderer _haze;
        private SpriteRenderer _core;
        private SpriteRenderer _pad;
        private Component _light;

        private Transform[] _sparkleTransforms;
        private SpriteRenderer[] _sparkleRenderers;
        private Vector3[] _slots;
        private Vector3[] _scatter;
        private float[] _phase;
        private float[] _twinkleSpeed;

        /// <summary>
        /// Leaves a dissolving copy of <paramref name="sprite"/> at the point departed from.
        /// The caster has usually already moved by the time this runs, which is why the
        /// silhouette is passed in rather than read back off the renderer.
        /// </summary>
        public static void Dematerialize(Vector3 center, Vector2 size, Sprite sprite,
                                         bool flipX, int sortingLayerId, int sortingOrder,
                                         Color tint)
        {
            var fx = Create(Mode.Dematerialize, center, size, tint);
            fx.BuildGhost(sprite, flipX, sortingLayerId, sortingOrder);
            fx.BuildRig();
        }

        /// <summary>
        /// Reassembles <paramref name="body"/> at the point arrived at. The renderer's colour
        /// is driven for the length of the cycle and restored afterwards, so nothing else
        /// that tints the character is left holding a stale value.
        /// </summary>
        public static void Materialize(Transform owner, Vector3 center, Vector2 size, Color tint)
        {
            var fx = Create(Mode.Materialize, center, size, tint);
            fx.TakeBody(owner);
            fx.BuildRig();
        }

        private static TransporterFX Create(Mode mode, Vector3 center, Vector2 size, Color tint)
        {
            var go = new GameObject("TransporterFX");
            go.transform.position = center;
            var fx = go.AddComponent<TransporterFX>();
            fx._mode = mode;
            fx._size = new Vector2(Mathf.Max(0.2f, size.x), Mathf.Max(0.3f, size.y));
            fx._tint = tint.a > 0.05f ? tint : new Color(1f, 0.87f, 0.5f, 1f);
            fx._hotTint = Color.Lerp(fx._tint, Color.white, 0.75f);
            return fx;
        }

        // ── Construction ──────────────────────────────────────────────────────

        private void BuildGhost(Sprite sprite, bool flipX, int sortingLayerId, int sortingOrder)
        {
            if (sprite == null) return;

            _ghost = CreateSprite("Ghost", sprite, Color.white, ORDER_GHOST);
            _ghost.flipX = flipX;
            _ghost.sortingLayerID = sortingLayerId;
            _ghost.sortingOrder = sortingOrder;

            // Match the size the body actually occupied instead of copying a transform
            // chain, so a scaled or nested character still leaves a silhouette that fits.
            Vector3 local = sprite.bounds.size;
            _ghost.transform.localScale = new Vector3(
                local.x > 0.0001f ? _size.x / local.x : 1f,
                local.y > 0.0001f ? _size.y / local.y : 1f,
                1f);
        }

        /// <summary>
        /// Take the arriving character's alpha for the length of the cycle. The stack is
        /// resolved from the ENTITY, not from the renderer: status effects and the hit flash
        /// attach theirs at the entity root, and two stacks on one character would each hold
        /// a different idea of the resting colour.
        /// </summary>
        private void TakeBody(Transform owner)
        {
            if (owner == null) return;
            _bodyTint = SpriteTintStack.Attach(owner.gameObject);
        }

        private void BuildRig()
        {
            ElementalSprites.EnsureAll();

            _haze = CreateSprite("ColumnHaze", ElementalSprites.Halo, _tint, ORDER_HAZE);
            _haze.transform.localScale = new Vector3(_size.x * 2.6f, _size.y * 2.4f, 1f);

            _core = CreateSprite("ColumnCore", ElementalSprites.Glow, _hotTint, ORDER_CORE);
            _core.transform.localScale = new Vector3(_size.x * 0.85f, _size.y * 2.0f, 1f);

            _pad = CreateSprite("PadGlow", ElementalSprites.Ring, _tint, ORDER_PAD);
            _pad.transform.localPosition = new Vector3(0f, -_size.y * 0.5f, 0f);
            _pad.transform.localScale = new Vector3(_size.x * 2.4f, _size.x * 0.9f, 1f);

            BuildSparkles();
            BuildLight();
        }

        private void BuildSparkles()
        {
            _sparkleTransforms = new Transform[SPARKLE_COUNT];
            _sparkleRenderers = new SpriteRenderer[SPARKLE_COUNT];
            _slots = new Vector3[SPARKLE_COUNT];
            _scatter = new Vector3[SPARKLE_COUNT];
            _phase = new float[SPARKLE_COUNT];
            _twinkleSpeed = new float[SPARKLE_COUNT];

            for (int i = 0; i < SPARKLE_COUNT; i++)
            {
                var sr = CreateSprite("Sparkle_" + i.ToString("00"), ElementalSprites.Sparkle,
                    Color.Lerp(_tint, Color.white, Random.Range(0.2f, 1f)), ORDER_SPARKLE);
                float size = Random.Range(0.035f, 0.085f);
                sr.transform.localScale = Vector3.one * size;

                // Inside the silhouette, narrowed towards the top so the column tapers the
                // way a body does rather than filling a rectangle.
                float heightFraction = Random.value;
                float taper = Mathf.Lerp(1f, 0.62f, heightFraction);
                _slots[i] = new Vector3(
                    Random.Range(-0.5f, 0.5f) * _size.x * taper,
                    (heightFraction - 0.5f) * _size.y,
                    0f);

                // Where a materialising mote comes in from, biased upward: the beam arrives
                // from above, so its stragglers should too.
                _scatter[i] = new Vector3(
                    Random.Range(-1f, 1f) * _size.x * 1.6f,
                    Random.Range(0.2f, 1.8f) * _size.y,
                    0f);

                _phase[i] = heightFraction;
                _twinkleSpeed[i] = Random.Range(38f, 74f);
                _sparkleTransforms[i] = sr.transform;
                _sparkleRenderers[i] = sr;
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
                if (typeProp != null)
                    typeProp.SetValue(_light, System.Enum.ToObject(typeProp.PropertyType, 3));
                ElementalProjectileVisual.GetLight2DColorProp()?.SetValue(_light, _tint);
                ElementalProjectileVisual.GetLight2DOuterProp()?.SetValue(_light, _size.y * 2.2f);
                ElementalProjectileVisual.GetLight2DInnerProp()?.SetValue(_light, 0.2f);
                ElementalProjectileVisual.GetLight2DFalloffProp()?.SetValue(_light, 0.8f);
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
            sr.sharedMaterial = ElementalSprites.SharedUnlitMaterial;
            sr.sortingLayerName = SortingConfig.LAYER_VFX;
            sr.sortingOrder = order;
            return sr;
        }

        // ── Per-frame ─────────────────────────────────────────────────────────

        private void Update()
        {
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / DURATION);

            // The shimmer peaks in the middle of the cycle at both ends of the trip.
            float column = Mathf.Sin(t * Mathf.PI);

            UpdateColumn(column);
            UpdateSparkles(t);
            UpdateBody(t);
            UpdateLight(column);

            if (_mode == Mode.Materialize && !_arriveSfxPlayed && t >= ARRIVE_SFX_AT)
            {
                _arriveSfxPlayed = true;
                ServiceLocator.Get<IAudioService>()?.PlaySfxById("spell_teleport_arrive");
            }

            if (_age >= DURATION) Destroy(gameObject);
        }

        private void UpdateColumn(float column)
        {
            if (_haze != null) _haze.color = WithAlpha(_tint, column * 0.42f);
            if (_core != null) _core.color = WithAlpha(_hotTint, column * column * 0.62f);
            if (_pad == null) return;

            float spread = Mathf.Lerp(0.7f, 1.35f, column);
            _pad.transform.localScale = new Vector3(_size.x * 2.4f * spread, _size.x * 0.9f * spread, 1f);
            _pad.color = WithAlpha(_tint, column * 0.55f);
        }

        private void UpdateSparkles(float t)
        {
            for (int i = 0; i < _sparkleTransforms.Length; i++)
            {
                float visibility;
                Vector3 position;

                if (_mode == Mode.Dematerialize)
                {
                    // Motes wink in from the feet up as the body gives way, then drift and
                    // scatter — the body is not moving, its material is.
                    float born = Mathf.Clamp01((t - _phase[i] * 0.45f) / 0.18f);
                    visibility = born * (1f - Mathf.SmoothStep(0.68f, 1f, t));
                    position = _slots[i] + Vector3.up * (t * _size.y * 0.42f)
                             + _scatter[i] * (t * t * 0.22f);
                }
                else
                {
                    // Motes fall in from the beam, find their place in the silhouette, and
                    // go out as the body underneath them takes over.
                    float arrival = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.72f));
                    visibility = Mathf.Clamp01(t / 0.10f) * (1f - Mathf.SmoothStep(0.58f, 1f, t));
                    position = Vector3.Lerp(_slots[i] + _scatter[i], _slots[i], arrival);
                }

                float twinkle = 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(_age * _twinkleSpeed[i] + _phase[i] * 6f));
                _sparkleTransforms[i].localPosition = position;
                _sparkleRenderers[i].color =
                    WithAlpha(_sparkleRenderers[i].color, visibility * twinkle);
            }
        }

        private void UpdateBody(float t)
        {
            // A steady ramp reads as a cross-fade; the flutter on top of it reads as a
            // signal being resolved.
            float flutter = 0.78f + 0.22f * Mathf.Sin(_age * 64f);

            if (_mode == Mode.Dematerialize)
            {
                if (_ghost == null) return;
                float solidity = 1f - Mathf.SmoothStep(0.04f, 0.72f, t);
                _ghost.color = WithAlpha(Color.white, solidity * flutter);
                return;
            }

            if (_bodyTint == null) return;
            float presence = Mathf.SmoothStep(0.22f, 0.92f, t);
            _bodyTint.Set(TintLayer.Teleport, WithAlpha(Color.white, presence * flutter));
        }

        private void UpdateLight(float column)
        {
            if (_light == null) return;
            try
            {
                ElementalProjectileVisual.GetLight2DIntensityProp()?.SetValue(_light, 2.6f * column);
            }
            catch { /* URP 2D lighting absent in this project configuration. */ }
        }

        private void OnDestroy()
        {
            // Whatever happened — cycle finished, scene torn down, character killed mid-beam
            // — the renderer must not be left holding the alpha this effect was driving.
            if (_bodyTint != null) _bodyTint.Clear(TintLayer.Teleport);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
