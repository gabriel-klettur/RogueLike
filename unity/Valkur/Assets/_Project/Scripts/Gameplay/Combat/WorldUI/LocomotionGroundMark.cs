using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Player;
using Valkur.Gameplay.World.Ambience;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// The impulse arc under the player's feet: a horseshoe of ticks, its gap facing the way the
    /// player is walking, that lights one segment per completed stride while sustained walking
    /// builds momentum toward a run. It answers ONE question — how close is this walk to
    /// breaking into a run — the same single-job discipline <see cref="FacingIndicator"/> keeps:
    /// this file does not read a spell, a cooldown or the stance, and never will.
    ///
    /// <para><b>Segments are the skill, drawn.</b> Their count is
    /// <c>LocomotionTuning.StartSteps</c> at the player's current athletics skill — a beginner
    /// sees three or four ticks fill before breaking into a run, a master one. The mark TEACHES
    /// the skill rather than merely gating it.</para>
    ///
    /// <para><b>Every light is an EVENT, never a continuous fill.</b> A segment pops on
    /// <see cref="FootstepEmitter.StrideCompleted"/> — synced to the same stride the dust and the
    /// noise come from — rather than tracking <c>Momentum</c> every frame, which would be a
    /// second readout of a state the energy row and the ground mark would then say twice. On
    /// <see cref="PlayerController.RunStarted"/> the whole arc closes into a ring, flashes and
    /// fades in ~0.3 s — corriendo no hay marca, the running state needs no reading. On
    /// <see cref="PlayerController.MomentumBroken"/> with something actually lost, the lit
    /// segments go dark in a REVERSE cascade over ~0.15 s, so the player watches what they just
    /// threw away. Winded shows nothing: there is no impulse left to draw.</para>
    ///
    /// <para><b>Geometry.</b> The same split <see cref="FacingIndicator"/> uses and for the same
    /// reason: an unrotated, unscaled root that FOLLOWS the player (parenting would inherit the
    /// entity scale), one GroundPlane child carrying the vertical squash, and a Pivot that turns
    /// under it to keep the horseshoe's gap pointed the way the player is heading. Squashing each
    /// segment separately would foreshorten its length without turning its direction and slide it
    /// across the floor instead of lying on it.</para>
    ///
    /// <para><b>Depth.</b> Always BEHIND the body, a small fixed gap under the feet's own Y-sort
    /// order — never the sign-based front/behind split <see cref="FacingIndicator"/> uses, because
    /// a floor mark under the feet has no reason to ever draw in front of them.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LocomotionGroundMark : MonoBehaviour
    {
        // ── Geometry ─────────────────────────────────────────────────────────────────

        private const float GroundSquash = 0.42f;
        private const float ArcRadius = 0.50f;
        private const float GapDegrees = 70f;      // forward gap, centred on the heading
        private const float SegmentSize = 0.16f;
        private const float RingSize = 1.0f;

        private const float SegmentAlpha = 0.55f;
        private const float RingFadeSeconds = 0.3f;
        private const float RingPeakAlpha = 0.7f;
        private const float CascadeSeconds = 0.15f;

        private const int DepthGap = 8;
        private const float TurnResponse = 22f;

        private PlayerController _player;
        private FootstepEmitter _footsteps;
        private SpriteRenderer _bodySr;

        private Transform _root, _ground, _pivot;
        private SpriteRenderer[] _segments;
        private SpriteRenderer _ring;

        private int _segmentCount;
        private int _litCount;
        private float _drawnAngleDeg;

        private bool _cascading;
        private float _cascadeAge;
        private int _cascadeFromCount;

        private float _ringAge = float.PositiveInfinity;

        private void Start()
        {
            _player = GetComponent<PlayerController>();
            _bodySr = GetComponent<SpriteRenderer>();
            BuildRig();
        }

        private void OnEnable()
        {
            if (_player == null) _player = GetComponent<PlayerController>();
            _footsteps = GetComponent<FootstepEmitter>();
            if (_player != null)
            {
                _player.RunStarted += OnRunStarted;
                _player.MomentumBroken += OnMomentumBroken;
            }
            if (_footsteps != null) _footsteps.StrideCompleted += OnStrideCompleted;
        }

        private void OnDisable()
        {
            if (_player != null)
            {
                _player.RunStarted -= OnRunStarted;
                _player.MomentumBroken -= OnMomentumBroken;
            }
            if (_footsteps != null) _footsteps.StrideCompleted -= OnStrideCompleted;
        }

        private void OnDestroy()
        {
            // The root is not a child of the player, so nothing else would take it down.
            if (_root != null) Destroy(_root.gameObject);
        }

        private void LateUpdate()
        {
            if (_root == null) return;
            ApplyState(Time.deltaTime, snapHeading: false);
        }

        /// <summary>
        /// Build the rig. Internal so an EditMode test can assemble one without Play Mode:
        /// <c>Start</c> never runs on a component added outside Play Mode.
        /// </summary>
        internal void BuildRig()
        {
            if (_root != null) return;
            if (_bodySr == null) _bodySr = GetComponent<SpriteRenderer>();

            _segmentCount = ComputeSegmentCount();

            var rootGo = new GameObject("LocomotionGroundMark");
            _root = rootGo.transform;
            var container = GameObject.Find("[VFX]");
            if (container != null) _root.SetParent(container.transform, false);
            _root.position = transform.position;
            _root.rotation = Quaternion.identity;
            _root.localScale = Vector3.one;

            var groundGo = new GameObject("GroundPlane");
            _ground = groundGo.transform;
            _ground.SetParent(_root, false);
            _ground.localPosition = Vector3.zero;
            _ground.localRotation = Quaternion.identity;
            _ground.localScale = new Vector3(1f, GroundSquash, 1f);

            var pivotGo = new GameObject("Pivot");
            _pivot = pivotGo.transform;
            _pivot.SetParent(_ground, false);
            _pivot.localPosition = Vector3.zero;
            _pivot.localScale = Vector3.one;

            BuildSegments();
            BuildRing();

            ApplyState(Time.deltaTime, snapHeading: true);
        }

        private void BuildSegments()
        {
            _segments = new SpriteRenderer[_segmentCount];
            for (int i = 0; i < _segmentCount; i++)
            {
                float theta = SegmentAngle(i, _segmentCount);
                var spoke = new GameObject("Spoke_" + i.ToString("00")).transform;
                spoke.SetParent(_pivot, false);
                spoke.localPosition = Vector3.zero;
                spoke.localRotation = Quaternion.Euler(0f, 0f, theta);

                var go = new GameObject("Segment_" + i.ToString("00"));
                go.transform.SetParent(spoke, false);
                go.transform.localPosition = new Vector3(ArcRadius, 0f, 0f);
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one * SegmentSize;

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = Spells.ElementalSprites.Sparkle;
                sr.sharedMaterial = Spells.ElementalSprites.SharedAdditiveMaterial;
                sr.sortingLayerName = SortingConfig.LAYER_ENTITIES;
                sr.sortingOrder = 0;
                sr.color = new Color(1f, 1f, 1f, 0f);
                sr.enabled = false;
                _segments[i] = sr;
            }
        }

        private void BuildRing()
        {
            var go = new GameObject("Ring");
            go.transform.SetParent(_pivot, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * RingSize;

            _ring = go.AddComponent<SpriteRenderer>();
            _ring.sprite = Spells.ElementalSprites.Ring;
            _ring.sharedMaterial = Spells.ElementalSprites.SharedAdditiveMaterial;
            _ring.sortingLayerName = SortingConfig.LAYER_ENTITIES;
            _ring.sortingOrder = 0;
            _ring.color = new Color(1f, 1f, 1f, 0f);
            _ring.enabled = false;
        }

        /// <summary>Angle in the pivot's local space, degrees, 0 = forward (+X, the heading —
        /// the same convention <see cref="FacingIndicator"/>'s tip uses). The gap straddles 0 and
        /// the segments fan out behind it, trailing the way a wake trails a boat.</summary>
        private static float SegmentAngle(int index, int count)
        {
            if (count <= 1) return 180f;
            float span = 360f - GapDegrees;
            float t = index / (float)(count - 1);
            return GapDegrees * 0.5f + t * span;
        }

        // ── Segment count ────────────────────────────────────────────────────────────

        /// <summary>N = steps of sustained walking before the skill breaks into a run: the mark
        /// SHOWS the skill rather than merely gating it. Pure and static so a test can ask it
        /// without building a rig or a <see cref="PlayerController"/>.</summary>
        public static int ComputeSegmentCount(LocomotionTuning tuning, float skill01, float walkSpeed, float strideLength)
            => (tuning != null ? tuning : LocomotionTuning.Active).StartSteps(skill01, walkSpeed, strideLength);

        /// <summary>Same, against the shipped tuning.</summary>
        public static int ComputeSegmentCount(float skill01, float walkSpeed, float strideLength)
            => ComputeSegmentCount(null, skill01, walkSpeed, strideLength);

        private int ComputeSegmentCount()
        {
            float skill = _player != null ? _player.AthleticsSkill01 : 0f;
            float walk = _player != null ? _player.WalkSpeed : 4f;
            return ComputeSegmentCount(skill, walk, FootstepEmitter.Stride);
        }

        // ── Events ───────────────────────────────────────────────────────────────────

        private void OnStrideCompleted()
        {
            if (_root == null || _player == null) return;
            var gait = _player.Gait;
            // Only while genuinely building toward a run: not already running (the arc is
            // hidden then), not Winded (no impulse to show), not mid-cascade (a stride landing
            // while the arc is unlighting must not fight that animation).
            if (gait.State != GaitState.Walk || gait.IsWinded) return;
            if (_cascading) return;
            if (_litCount >= _segmentCount) return;

            var sr = _segments[_litCount];
            sr.enabled = true;
            sr.color = new Color(1f, 1f, 1f, SegmentAlpha);
            _litCount++;
        }

        private void OnRunStarted()
        {
            _cascading = false;
            for (int i = 0; i < _segmentCount; i++)
            {
                _segments[i].enabled = false;
                _segments[i].color = new Color(1f, 1f, 1f, 0f);
            }
            _litCount = 0;

            _ringAge = 0f;
            _ring.color = new Color(1f, 1f, 1f, RingPeakAlpha);
            _ring.enabled = true;
        }

        private void OnMomentumBroken(GaitBreak reason, float lost)
        {
            if (lost <= 0f || _litCount <= 0) return;
            _cascading = true;
            _cascadeAge = 0f;
            _cascadeFromCount = _litCount;
        }

        // ── Per-frame ────────────────────────────────────────────────────────────────

        internal void ApplyState(float dt, bool snapHeading)
        {
            if (_root == null) return;
            _root.position = transform.position;

            TickCascade(dt);
            TickRing(dt);

            Vector2 heading = _player != null ? _player.Gait.Heading : Vector2.zero;
            if (heading.sqrMagnitude > 0.0001f)
            {
                float target = Mathf.Atan2(heading.y, heading.x) * Mathf.Rad2Deg;
                _drawnAngleDeg = snapHeading
                    ? target
                    : Mathf.LerpAngle(_drawnAngleDeg, target, Response(TurnResponse, dt));
                _pivot.localRotation = Quaternion.Euler(0f, 0f, _drawnAngleDeg);
            }

            ApplyDepth();
        }

        private void TickCascade(float dt)
        {
            if (!_cascading) return;
            _cascadeAge += dt;
            float u = Mathf.Clamp01(_cascadeAge / CascadeSeconds);
            int shouldBeLit = Mathf.RoundToInt((1f - u) * _cascadeFromCount);

            // Reverse cascade: the most recently lit segment (highest index) goes dark first.
            while (_litCount > shouldBeLit && _litCount > 0)
            {
                _litCount--;
                _segments[_litCount].enabled = false;
                _segments[_litCount].color = new Color(1f, 1f, 1f, 0f);
            }

            if (u >= 1f) { _cascading = false; _litCount = 0; }
        }

        private void TickRing(float dt)
        {
            if (!_ring.enabled) return;
            _ringAge += dt;
            float u = Mathf.Clamp01(_ringAge / RingFadeSeconds);
            float alpha = RingPeakAlpha * (1f - u) * (1f - u);
            _ring.color = new Color(1f, 1f, 1f, alpha);
            if (u >= 1f) _ring.enabled = false;
        }

        private void ApplyDepth()
        {
            int bodyOrder = SortingConfig.ComputeSortingOrder(SortingConfig.Z_ENTITY, transform.position.y);
            int layerId = _bodySr != null ? _bodySr.sortingLayerID : SortingLayer.NameToID(SortingConfig.LAYER_ENTITIES);
            int order = bodyOrder - DepthGap;

            for (int i = 0; i < _segments.Length; i++) SetDepth(_segments[i], layerId, order);
            SetDepth(_ring, layerId, order);
        }

        private static void SetDepth(SpriteRenderer sr, int layerId, int order)
        {
            if (sr == null) return;
            if (sr.sortingLayerID != layerId) sr.sortingLayerID = layerId;
            if (sr.sortingOrder != order) sr.sortingOrder = order;
        }

        private static float Response(float rate, float dt) => 1f - Mathf.Exp(-rate * Mathf.Max(0f, dt));

        // ── Test seams ───────────────────────────────────────────────────────────────

        internal Transform RootTransform => _root;
        internal Transform PivotTransform => _pivot;
        internal int SegmentCount => _segmentCount;
        internal int LitCount => _litCount;
        internal SpriteRenderer[] Segments => _segments;
        internal SpriteRenderer RingRenderer => _ring;

        /// <summary>True while any segment or the ring is actually enabled. Used by
        /// "nothing is drawn at rest" — the design's whole promise for this rig.</summary>
        internal bool AnythingVisible
        {
            get
            {
                if (_ring != null && _ring.enabled) return true;
                if (_segments == null) return false;
                for (int i = 0; i < _segments.Length; i++)
                    if (_segments[i] != null && _segments[i].enabled) return true;
                return false;
            }
        }
    }
}
