using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The sigil over a marked enemy: two counter-rotating rings above the body and three
    /// motes orbiting its feet on the ground plane.
    ///
    /// <para>WHY IT TIGHTENS. A mark that looked the same at full health and at a sliver
    /// would be decoration. Everything here is driven off the bearer's HP fraction — the
    /// rings CONTRACT, the spin rate climbs and the colour heats from violet toward pale
    /// magenta — so the player can read at a glance whether the bet is about to pay. That
    /// single coupling is the piece the spell lives or dies on, which is why it is the first
    /// thing this rig does rather than a polish pass.</para>
    ///
    /// <para>WHY TWO RINGS TURNING OPPOSITE WAYS. One ring turning at a steady rate is
    /// continuous motion and stops being read inside a second. Counter-rotation gives the eye
    /// a changing relationship to track instead of a constant one, and it costs nothing.</para>
    /// </summary>
    internal sealed class ThrallMarkFX : MonoBehaviour
    {
        private const int MOTE_COUNT = 3;
        private const float MOTE_ORBIT_RADIUS = 0.55f;

        private const int ORDER_RING_OUTER = 50;
        private const int ORDER_RING_INNER = 51;
        private const int ORDER_MOTE = 52;

        /// <summary>Seconds of visible unravelling before the mark expires. An expiring mark
        /// and a firing mark must be unmistakable at a glance.</summary>
        private const float UNRAVEL_SECONDS = 2f;

        private static readonly Color Violet = new Color(0.60f, 0.35f, 0.75f, 1f);
        private static readonly Color Magenta = new Color(0.95f, 0.45f, 0.85f, 1f);

        private ThrallMarkEffect _mark;
        private Transform _bearer;
        private Vector3 _headOffset;
        private Transform _groundPlane;
        private SpriteRenderer _outer;
        private SpriteRenderer _inner;
        private SpriteRenderer[] _motes;
        private SpriteTintStack _tint;
        private float _spin;
        private float _age;

        public static void Attach(GameObject bearer, ThrallMarkEffect mark)
        {
            if (bearer == null || mark == null) return;
            if (!Application.isPlaying) return;

            var existing = bearer.GetComponentInChildren<ThrallMarkFX>();
            if (existing != null) Destroy(existing.gameObject);

            var body = bearer.GetComponent<SpriteRenderer>();
            if (body == null) body = bearer.GetComponentInChildren<SpriteRenderer>();
            float height = body != null && body.sprite != null ? body.bounds.size.y : 1.6f;

            var go = new GameObject("ThrallMarkFX");
            go.transform.position = bearer.transform.position;

            var fx = go.AddComponent<ThrallMarkFX>();
            fx._mark = mark;
            fx._bearer = bearer.transform;
            fx._headOffset = new Vector3(0f, height * 1.05f, 0f);
            fx._tint = SpriteTintStack.Attach(bearer);
            fx.BuildRig(height);
        }

        private void BuildRig(float height)
        {
            _outer = CreateSprite("RingOuter", ElementalSprites.Ring, ORDER_RING_OUTER, transform);
            _inner = CreateSprite("RingInner", ElementalSprites.Ring, ORDER_RING_INNER, transform);

            // The feet layer is squashed by ONE parent with the per-mote rotation on its
            // CHILDREN. Squashing each mote individually would foreshorten it without turning
            // it, and the orbit would slide across the floor instead of lying on it.
            var plane = new GameObject("GroundPlane");
            plane.transform.SetParent(transform, false);
            plane.transform.localPosition = -_headOffset;
            plane.transform.localScale = new Vector3(1f, 0.34f, 1f);
            _groundPlane = plane.transform;

            _motes = new SpriteRenderer[MOTE_COUNT];
            for (int i = 0; i < MOTE_COUNT; i++)
            {
                _motes[i] = CreateSprite($"Mote{i}", ElementalSprites.Sparkle, ORDER_MOTE, _groundPlane);
                _motes[i].transform.localScale = Vector3.one * 0.2f;
            }
        }

        private SpriteRenderer CreateSprite(string objectName, Sprite sprite, int order, Transform parent)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = new Color(Violet.r, Violet.g, Violet.b, 0f);
            sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
            sr.sortingLayerName = SortingConfig.LAYER_VFX;
            sr.sortingOrder = order;
            return sr;
        }

        private void Update()
        {
            _age += Time.deltaTime;

            // The mark going away — expiring, cleansed, or cashed in — takes the rig with it.
            // The raising has its own, far larger, beat and must not be drawn under a sigil.
            if (_mark == null || _bearer == null || _mark.Consumed || _mark.IsExpired)
            {
                Cleanup();
                return;
            }

            transform.position = _bearer.position + _headOffset;

            // 0 at full health, 1 at death. Everything below reads off this one number.
            float urgency = 1f - _mark.BearerHealthFraction;

            // Unravel independently of urgency: a mark about to time out on a healthy target
            // must look different from one tightening on a dying one.
            float remaining = Mathf.Max(0f, _mark.EndTime - Time.time);
            float unravel = remaining >= UNRAVEL_SECONDS ? 0f : 1f - Mathf.Clamp01(remaining / UNRAVEL_SECONDS);

            float ignite = Mathf.Clamp01(_age / 0.25f);
            Color hue = Color.Lerp(Violet, Magenta, urgency);

            _spin += Mathf.Lerp(45f, 240f, urgency) * Time.deltaTime;

            if (_outer != null)
            {
                // Contracting, not growing: a ring that opened out would read as the target
                // getting stronger.
                float r = Mathf.Lerp(0.95f, 0.55f, urgency) * (1f + unravel * 0.8f);
                _outer.transform.localScale = Vector3.one * r;
                _outer.transform.localRotation = Quaternion.Euler(0f, 0f, _spin);
                _outer.color = WithAlpha(hue, 0.75f * ignite * (1f - unravel));
            }

            if (_inner != null)
            {
                float r = Mathf.Lerp(0.58f, 0.30f, urgency);
                _inner.transform.localScale = Vector3.one * r;
                _inner.transform.localRotation = Quaternion.Euler(0f, 0f, -_spin * 1.6f);
                _inner.color = WithAlpha(hue, 0.85f * ignite * (1f - unravel));
            }

            if (_motes != null)
            {
                for (int i = 0; i < _motes.Length; i++)
                {
                    float a = _spin * Mathf.Deg2Rad * 0.7f + i * (Mathf.PI * 2f / MOTE_COUNT);
                    _motes[i].transform.localPosition =
                        new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * MOTE_ORBIT_RADIUS;
                    _motes[i].color = WithAlpha(hue, 0.8f * ignite * (1f - unravel));
                }
            }

            if (_tint != null)
            {
                // Weak on purpose: the sigil is what does the talking, and a strongly tinted
                // enemy stops reading as the creature it is.
                float k = Mathf.Lerp(0.14f, 0.30f, urgency) * ignite;
                _tint.Set(TintLayer.Marked, Color.Lerp(Color.white, hue, k));
            }
        }

        /// <summary>
        /// Clearing the tint here rather than only in Update is what makes the rig safe on
        /// every exit path — the mark expiring, the bearer dying, a zone change, scene
        /// unload. Only OnDestroy is on all of them.
        /// </summary>
        private void OnDestroy()
        {
            if (_tint != null) _tint.Clear(TintLayer.Marked);
        }

        private void Cleanup()
        {
            if (_tint != null) _tint.Clear(TintLayer.Marked);
            Destroy(gameObject);
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
