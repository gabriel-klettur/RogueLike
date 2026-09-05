using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Combat;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The sigil over a cursed enemy: two counter-rotating rings above the body and three
    /// motes orbiting its feet on the ground plane.
    ///
    /// <para>This is the rig <c>VulnerableEffect</c>'s own doc-comment has been pointing at
    /// since it was written — it justifies holding the body tint deliberately weak on the
    /// grounds that "the sigil carries the reading", and until now that sigil did not exist.
    /// The result was a curse whose entire presence was a 28 % violet wash a player could not
    /// reliably tell from a lighting change.</para>
    ///
    /// <para>WHY IT PULSES RATHER THAN TIGHTENS. Its sibling <see cref="ThrallMarkFX"/> is a
    /// BET — it contracts and heats as the bearer weakens, because the player needs to read
    /// whether the kill will land inside the window. A curse is not a bet: it is a window of
    /// opportunity that is simply open. So this one keeps a steady geometry and marks the
    /// passage of time with a slow pulse at roughly 15 % duty. A curse should feel patient,
    /// and a rig that copied the thrall's urgency would tell the player something false.</para>
    /// </summary>
    internal sealed class CurseMarkFX : MonoBehaviour
    {
        private const int MOTE_COUNT = 3;
        private const float MOTE_ORBIT_RADIUS = 0.52f;

        private const int ORDER_RING_OUTER = 50;
        private const int ORDER_RING_INNER = 51;
        private const int ORDER_MOTE = 52;

        /// <summary>Ring diameters in world units. Both are small: this sits over a creature
        /// roughly 1.6 u tall and a sigil wider than its bearer stops reading as attached.</summary>
        private const float OUTER_DIAMETER = 0.92f;
        private const float INNER_DIAMETER = 0.58f;

        private const float PULSE_MIN_INTERVAL = 1.4f;
        private const float PULSE_MAX_INTERVAL = 2.6f;
        /// <summary>Rings snap out and back over this long. 0.2 s against a ~2 s interval is
        /// the ~15 % duty the patience above is made of.</summary>
        private const float PULSE_SECONDS = 0.2f;

        private const float UNRAVEL_SECONDS = 1.2f;

        private static readonly Color Violet = new Color(0.62f, 0.32f, 0.60f, 1f);

        private VulnerableEffect _curse;
        private Transform _bearer;
        private Vector3 _headOffset;
        private Transform _groundPlane;
        private SpriteRenderer _outer;
        private SpriteRenderer _inner;
        private SpriteRenderer[] _motes;
        private float _spin;
        private float _age;
        private float _pulseClock;
        private float _pulse;

        public static void Attach(GameObject bearer, VulnerableEffect curse)
        {
            if (bearer == null || curse == null) return;
            // Edit Mode has no Update loop to drive this and Destroy is an outright error
            // there, so the rig is a Play-Mode-only concern.
            if (!Application.isPlaying) return;

            var existing = bearer.GetComponentInChildren<CurseMarkFX>();
            if (existing != null) Destroy(existing.gameObject);

            var body = bearer.GetComponent<SpriteRenderer>();
            if (body == null) body = bearer.GetComponentInChildren<SpriteRenderer>();
            float height = body != null && body.sprite != null ? body.bounds.size.y : 1.6f;

            var go = new GameObject("CurseMarkFX");
            go.transform.position = bearer.transform.position;

            var fx = go.AddComponent<CurseMarkFX>();
            fx._curse = curse;
            fx._bearer = bearer.transform;
            fx._headOffset = new Vector3(0f, height * 1.05f, 0f);
            fx._pulseClock = Random.Range(PULSE_MIN_INTERVAL, PULSE_MAX_INTERVAL);
            fx.BuildRig();
        }

        private void BuildRig()
        {
            _outer = CreateSprite("RingOuter", ElementalSprites.Ring, ORDER_RING_OUTER, transform);
            _inner = CreateSprite("RingInner", ElementalSprites.Ring, ORDER_RING_INNER, transform);

            // The motes live on a SQUASHED parent rather than being flattened one by one: a
            // radial item squashed individually is foreshortened in LENGTH without being
            // turned, so it slides across the floor instead of lying on it.
            var plane = new GameObject("GroundPlane");
            plane.transform.SetParent(transform, false);
            plane.transform.localScale = new Vector3(1f, 0.34f, 1f);
            _groundPlane = plane.transform;

            _motes = new SpriteRenderer[MOTE_COUNT];
            for (int i = 0; i < MOTE_COUNT; i++)
            {
                _motes[i] = CreateSprite("Mote" + i, ElementalSprites.Sparkle,
                                         ORDER_MOTE, _groundPlane);
                _motes[i].transform.localScale = Vector3.one * 0.20f;
            }
        }

        private static SpriteRenderer CreateSprite(string name, Sprite sprite, int order,
                                                   Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
            sr.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_VFX);
            sr.sortingLayerName = SortingConfig.LAYER_VFX;
            sr.sortingOrder = order;
            sr.color = Violet;
            return sr;
        }

        private void Update()
        {
            // The curse object outlives nothing: when the effect ends or the bearer dies the
            // rig has no reason to exist, and a sigil hanging over a corpse is worse than none.
            if (_curse == null || _curse.IsExpired || _bearer == null)
            {
                Destroy(gameObject);
                return;
            }

            float dt = Time.deltaTime;
            _age += dt;
            transform.position = _bearer.position;

            AdvancePulse(dt);

            float fade = ResolveFade();
            LayoutRings(fade);
            LayoutMotes(fade);
        }

        private void AdvancePulse(float dt)
        {
            _pulseClock -= dt;
            if (_pulseClock <= 0f)
            {
                _pulseClock = Random.Range(PULSE_MIN_INTERVAL, PULSE_MAX_INTERVAL);
                _pulse = 1f;
            }
            _pulse = Mathf.Max(0f, _pulse - dt / PULSE_SECONDS);
        }

        /// <summary>
        /// The last stretch visibly comes apart. An expiring curse and a live one have to be
        /// unmistakable, or the player spends their burst window on a target that is no longer
        /// vulnerable — which is the one mistake this spell exists to prevent.
        /// </summary>
        private float ResolveFade()
        {
            float remaining = _curse.EndTime - Time.time;
            if (remaining >= UNRAVEL_SECONDS) return 1f;
            return Mathf.Clamp01(remaining / UNRAVEL_SECONDS);
        }

        private void LayoutRings(float fade)
        {
            // Counter-rotation is the point: one ring turning at a steady rate is continuous
            // motion and the eye files it as texture within a second. Two turning against each
            // other give it a changing relationship to track, and it costs nothing.
            _spin += 34f * Time.deltaTime;

            float snap = 1f + 0.22f * _pulse;

            if (_outer != null)
            {
                _outer.transform.localPosition = _headOffset;
                _outer.transform.localRotation = Quaternion.Euler(0f, 0f, _spin);
                float d = OUTER_DIAMETER * snap;
                _outer.transform.localScale = new Vector3(d, d * 0.42f, 1f);
                SetAlpha(_outer, (0.42f + 0.38f * _pulse) * fade);
            }

            if (_inner != null)
            {
                _inner.transform.localPosition = _headOffset;
                _inner.transform.localRotation = Quaternion.Euler(0f, 0f, -_spin * 1.45f);
                float d = INNER_DIAMETER * snap;
                _inner.transform.localScale = new Vector3(d, d * 0.42f, 1f);
                SetAlpha(_inner, (0.30f + 0.44f * _pulse) * fade);
            }
        }

        /// <summary>
        /// The motes are the half that says the curse exists in the WORLD and not only above
        /// the target: they orbit at the feet, on the ground plane, where the creature is
        /// actually standing.
        /// </summary>
        private void LayoutMotes(float fade)
        {
            if (_motes == null || _groundPlane == null) return;

            _groundPlane.localPosition = Vector3.zero;
            for (int i = 0; i < _motes.Length; i++)
            {
                if (_motes[i] == null) continue;
                float phase = _age * 1.15f + i * (Mathf.PI * 2f / MOTE_COUNT);
                _motes[i].transform.localPosition = new Vector3(
                    Mathf.Cos(phase) * MOTE_ORBIT_RADIUS,
                    Mathf.Sin(phase) * MOTE_ORBIT_RADIUS,
                    0f);
                SetAlpha(_motes[i], (0.50f + 0.40f * _pulse) * fade);
            }
        }

        private static void SetAlpha(SpriteRenderer sr, float alpha)
        {
            var c = sr.color;
            sr.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(alpha));
        }
    }
}
