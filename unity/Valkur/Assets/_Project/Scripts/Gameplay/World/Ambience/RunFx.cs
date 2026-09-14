using UnityEngine;
using Valkur.Core;
using Valkur.Core.Rendering;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Player;

namespace Valkur.Gameplay.World.Ambience
{
    /// <summary>
    /// Every EVENT-driven particle a run throws off, in one component: the fan of dust at the
    /// push-off, the lateral skid when a hard turn snaps momentum away, the breath at the top of
    /// an empty tank, and the faint speed lines once a sprint has held a while.
    ///
    /// <para><b>An event, never a state.</b> The same rule <c>FacingIndicatorStyle</c>'s pulse
    /// follows: a state has to be READ — a rig that dimmed while momentum drained would be a
    /// second readout competing with the ground mark and the energy bar. An EVENT tells the
    /// player nothing they did not just do. Nothing here polls the gait every frame for its own
    /// sake; the one thing that IS sampled continuously (the speed-line timer, against
    /// <c>SecondsAtFullRun</c>) only decides WHEN to fire the next discrete spawn, never draws a
    /// continuous readout of a value.</para>
    ///
    /// <para><b>Player-only, and wired through <see cref="PlayerController"/>'s own events</b> —
    /// <see cref="PlayerController.RunStarted"/>, <see cref="PlayerController.MomentumBroken"/>,
    /// <see cref="PlayerController.BecameWinded"/> — subscribed in <c>OnEnable</c> and dropped in
    /// <c>OnDisable</c>, so a respawn under Domain-Reload-off never doubles a handler.</para>
    ///
    /// <para>Every particle is a pooled <see cref="FootstepDust"/> puff — reused rather than a
    /// second effect system, the way <see cref="FootstepEmitter"/>'s own running stride does —
    /// except the speed lines, which need a shape dust cannot give them and keep their own tiny
    /// fixed pool so nothing here allocates per frame.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RunFx : MonoBehaviour
    {
        private const int DustOrderOffset = -3;   // matches FootstepEmitter.OrderOffset

        private const int BURST_MIN = 8, BURST_MAX = 13;      // 8..12 puffs
        private const int SKID_MIN = 4, SKID_MAX = 7;         // 4..6 puffs
        private const int BREATH_MIN = 2, BREATH_MAX = 4;     // 2..3 puffs

        private const int STREAK_POOL = 6;
        private const float STREAK_LIFE = 0.28f;
        private const float STREAK_INTERVAL_MIN = 0.5f;       // ~1-2 streaks/second
        private const float STREAK_INTERVAL_MAX = 0.9f;
        private const float STREAK_PEAK_ALPHA = 0.16f;

        private PlayerController _player;
        private SpriteRenderer _body;
        private Vector2 _prevHeading;
        private float _streakTimer;

        private SpriteRenderer[] _streaks;
        private float[] _streakAge;
        private bool[] _streakLive;

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
            _body = SpriteTintStack.ResolveBodyRenderer(gameObject);
        }

        private void OnEnable()
        {
            if (_player == null) _player = GetComponent<PlayerController>();
            if (_player == null) return;
            _player.RunStarted += OnRunStarted;
            _player.MomentumBroken += OnMomentumBroken;
            _player.BecameWinded += OnBecameWinded;
        }

        private void OnDisable()
        {
            if (_player == null) return;
            _player.RunStarted -= OnRunStarted;
            _player.MomentumBroken -= OnMomentumBroken;
            _player.BecameWinded -= OnBecameWinded;
        }

        private void Update()
        {
            TickStreaks(Time.deltaTime);
            TickSpeedLineTimer(Time.deltaTime);

            // Last-known heading, sampled once a frame so a turn's skid can tell which side it
            // came FROM — the gait only ever reports where it is heading NOW.
            var gait = _player != null ? _player.Gait : null;
            if (gait != null && gait.Heading.sqrMagnitude > 0.0001f) _prevHeading = gait.Heading;
        }

        // ── Events ───────────────────────────────────────────────────────────────────

        /// <summary>The push-off: a fan of dust thrown behind the feet the instant momentum
        /// breaks into a run.</summary>
        private void OnRunStarted()
        {
            Vector3 feet = FeetPosition();
            Vector2 heading = HeadingNow();
            Vector2 back = -heading;

            int layer = LayerFor();
            int order = OrderFor();
            int count = Random.Range(BURST_MIN, BURST_MAX);
            for (int i = 0; i < count; i++)
            {
                float spread = Random.Range(-40f, 40f) * Mathf.Deg2Rad;
                Vector2 dir = Rotate(back, spread);
                Vector3 pos = feet + (Vector3)(dir * Random.Range(0.05f, 0.22f));
                var kind = FootstepEmitter.GroundProbe != null ? FootstepEmitter.GroundProbe(pos) : GroundKind.Dust;
                FootstepDust.Spawn(pos, kind, layer, order,
                    Random.Range(1.1f, 1.6f), 1.1f, dir * Random.Range(0.8f, 1.6f));
            }
        }

        /// <summary>The skid: lateral dust kicked out on the OUTER side of a turn hard enough to
        /// throw momentum away. Every other break (a stop, a wall, a blow, Winded) leaves no
        /// mark here — a skid is what a sideways snap of the feet looks like, not what stopping,
        /// being hit or running dry look like.</summary>
        private void OnMomentumBroken(GaitBreak reason, float lost)
        {
            if (reason != GaitBreak.Turned || lost <= 0f) return;

            Vector2 newHeading = HeadingNow();
            if (newHeading.sqrMagnitude < 0.0001f) return;

            // Outer side = away from the way the heading swung. Cross > 0 is a left (CCW) turn,
            // whose outer edge is to the RIGHT of the new heading.
            float cross = _prevHeading.sqrMagnitude > 0.0001f
                ? _prevHeading.x * newHeading.y - _prevHeading.y * newHeading.x
                : 0f;
            float side = cross >= 0f ? -1f : 1f;
            Vector2 perp = new Vector2(-newHeading.y, newHeading.x) * side;

            Vector3 feet = FeetPosition();
            int layer = LayerFor();
            int order = OrderFor();
            int count = Random.Range(SKID_MIN, SKID_MAX);
            for (int i = 0; i < count; i++)
            {
                Vector3 pos = feet + (Vector3)(perp * Random.Range(0.08f, 0.28f))
                                   - (Vector3)(newHeading * Random.Range(0f, 0.15f));
                var kind = FootstepEmitter.GroundProbe != null ? FootstepEmitter.GroundProbe(pos) : GroundKind.Dust;
                FootstepDust.Spawn(pos, kind, layer, order,
                    Random.Range(0.9f, 1.3f), 0.9f, perp * Random.Range(0.6f, 1.3f));
            }
        }

        /// <summary>The breath: a couple of pale puffs above the head the instant the tank runs
        /// dry. Reuses the snow puff's colour for "white-ish" rather than a dedicated sprite —
        /// a breath and a snowflake are close enough that a new asset would be pure duplication.</summary>
        private void OnBecameWinded()
        {
            if (_body == null) _body = SpriteTintStack.ResolveBodyRenderer(gameObject);
            Vector3 headPos = _body != null && _body.sprite != null
                ? new Vector3(_body.bounds.center.x, _body.bounds.max.y + 0.08f, 0f)
                : transform.position + Vector3.up * 0.8f;

            int layer = LayerFor();
            int order = _body != null ? _body.sortingOrder + 2
                                       : SortingConfig.ComputeSortingOrder(SortingConfig.Z_ENTITY, headPos.y) + 2;

            int count = Random.Range(BREATH_MIN, BREATH_MAX);
            for (int i = 0; i < count; i++)
            {
                Vector3 pos = headPos + new Vector3(Random.Range(-0.08f, 0.08f), i * 0.06f, 0f);
                FootstepDust.Spawn(pos, GroundKind.Snow, layer, order,
                    Random.Range(0.5f, 0.75f), 1.3f,
                    new Vector2(Random.Range(-0.1f, 0.1f), Random.Range(0.35f, 0.6f)));
            }
        }

        // ── Speed lines ──────────────────────────────────────────────────────────────

        private void TickSpeedLineTimer(float dt)
        {
            var gait = _player != null ? _player.Gait : null;
            if (gait == null || gait.SecondsAtFullRun < LocomotionTuning.Active.speedLinesAfterSeconds)
            {
                _streakTimer = 0f;
                return;
            }

            _streakTimer -= dt;
            if (_streakTimer > 0f) return;
            _streakTimer = Random.Range(STREAK_INTERVAL_MIN, STREAK_INTERVAL_MAX);

            Vector2 dir = gait.Heading.sqrMagnitude > 0.0001f ? gait.Heading : Vector2.right;
            float bodyWidth = _body != null && _body.sprite != null ? _body.bounds.size.x : 0.7f;
            SpawnSpeedLine(dir, bodyWidth);
        }

        private void EnsureStreakPool()
        {
            if (_streaks != null) return;
            _streaks = new SpriteRenderer[STREAK_POOL];
            _streakAge = new float[STREAK_POOL];
            _streakLive = new bool[STREAK_POOL];

            var container = GameObject.Find("[VFX]");
            for (int i = 0; i < STREAK_POOL; i++)
            {
                var go = new GameObject("RunSpeedLine_" + i.ToString("00"));
                if (container != null) go.transform.SetParent(container.transform, false);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = Spells.ElementalSprites.Sparkle;
                sr.sharedMaterial = Spells.ElementalSprites.SharedAdditiveMaterial;
                sr.sortingLayerName = SortingConfig.LAYER_VFX;
                sr.sortingOrder = 40;
                sr.color = new Color(1f, 1f, 1f, 0f);
                sr.enabled = false;
                _streaks[i] = sr;
            }
        }

        private void SpawnSpeedLine(Vector2 velocityDir, float bodyWidth)
        {
            EnsureStreakPool();
            int slot = -1;
            for (int i = 0; i < STREAK_POOL; i++) if (!_streakLive[i]) { slot = i; break; }
            if (slot < 0) return;   // pool spent this instant — dropped, never queued

            float side = Random.value < 0.5f ? -1f : 1f;
            Vector2 perp = new Vector2(-velocityDir.y, velocityDir.x) * side;
            Vector3 pos = FeetPosition()
                + (Vector3)(perp * (bodyWidth * 0.55f))
                + Vector3.up * (bodyWidth * 0.5f);

            var sr = _streaks[slot];
            sr.transform.position = pos;
            sr.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(velocityDir.y, velocityDir.x) * Mathf.Rad2Deg);
            sr.transform.localScale = new Vector3(0.9f, 0.10f, 1f);
            sr.color = new Color(1f, 1f, 1f, 0f);
            sr.enabled = true;
            _streakLive[slot] = true;
            _streakAge[slot] = 0f;
        }

        private void TickStreaks(float dt)
        {
            if (_streaks == null) return;
            for (int i = 0; i < STREAK_POOL; i++)
            {
                if (!_streakLive[i]) continue;
                _streakAge[i] += dt;
                float u = _streakAge[i] / STREAK_LIFE;
                if (u >= 1f)
                {
                    _streakLive[i] = false;
                    _streaks[i].enabled = false;
                    continue;
                }

                // Rise, then fall, inside one life — a discrete pop rather than a fade the eye
                // would read as a fixture left burning.
                var sr = _streaks[i];
                var c = sr.color;
                c.a = Mathf.Sin(u * Mathf.PI) * STREAK_PEAK_ALPHA;
                sr.color = c;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────

        private Vector2 HeadingNow()
        {
            var gait = _player != null ? _player.Gait : null;
            if (gait != null && gait.Heading.sqrMagnitude > 0.0001f) return gait.Heading;
            return _prevHeading.sqrMagnitude > 0.0001f ? _prevHeading : Vector2.right;
        }

        private Vector3 FeetPosition()
        {
            if (_body != null && _body.sprite != null)
            {
                var b = _body.bounds;
                return new Vector3(b.center.x, b.min.y + 0.03f, 0f);
            }
            return transform.position;
        }

        private int LayerFor() => _body != null ? _body.sortingLayerID : SortingLayer.NameToID(SortingConfig.LAYER_ENTITIES);

        private int OrderFor() => _body != null
            ? _body.sortingOrder + DustOrderOffset
            : SortingConfig.ComputeSortingOrder(SortingConfig.Z_ENTITY, transform.position.y) + DustOrderOffset;

        private static Vector2 Rotate(Vector2 v, float radians)
        {
            float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }
    }
}
