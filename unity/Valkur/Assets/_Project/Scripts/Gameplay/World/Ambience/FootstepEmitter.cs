using System;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Core.Rendering;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.World.Weather;

namespace Valkur.Gameplay.World.Ambience
{
    /// <summary>
    /// Kicks up a puff of dust under a creature every stride it walks.
    ///
    /// Distance-driven, not animation-driven: every character and monster has its own frame
    /// count and its own speed multiplier, so "a step is a frame" would put one creature's
    /// dust on every third frame and another's on none. A stride is a length on the ground,
    /// which is what a footprint is.
    ///
    /// The ground decides the puff: lying snow makes it white and slow, open water makes it
    /// a splash, everything else a warm puff. The probe is a swappable delegate so the
    /// fixture can stand a creature on snow without building a world; the default reads the
    /// snow clock and the Ground tilemap under the feet.
    ///
    /// Sorts just under the body's contact shadow, on the body's own layer, so it is dust
    /// UNDER the feet rather than smoke on the shins — and the same Y-sort as the body, so a
    /// creature behind walks through it and one in front covers it.
    ///
    /// <para><b>ON THE CONTACT FRAME WHEN THE ART IS MEASURED.</b> A sheet with a measured cycle
    /// (<c>LocomotionCycleCatalog</c>) says which frames plant a foot, and
    /// <c>DirectionalAnimator.FootContact</c> fires on them. While such a cycle is on screen the
    /// puff, the noise and <see cref="StrideCompleted"/> come from that event and the distance clock
    /// only keeps count, so the dust lands under the foot the drawing puts down instead of between
    /// two of them. Every unmeasured walker (all monsters today) keeps the distance rule above.</para>
    ///
    /// <para>A player at a run gets a longer, heavier stride: bigger, longer-lived puffs kicked
    /// BACKWARD opposite the travel, and a noise event a walking stride never raises. Walking
    /// stays silent on purpose — it is the only sigil the stealth layer has.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FootstepEmitter : MonoBehaviour
    {
        /// <summary>World units between puffs, walking.</summary>
        public const float Stride = 0.62f;

        /// <summary>World units between puffs at a run — a longer, harder stride, only used
        /// while the host carries a <see cref="PlayerController"/> whose gait is running.</summary>
        public const float RunStride = 0.95f;

        /// <summary>Below this speed (u/s) nothing is walking, and the stride resets.</summary>
        public const float MinSpeed = 0.45f;

        /// <summary>How much bigger and longer-lived a running puff is than a walking one.</summary>
        private const float RunPuffScaleMul = 1.75f;
        private const float RunPuffLifeMul = 1.4f;

        /// <summary>World units/second the run's dust is thrown backward, opposite the travel.</summary>
        private const float RunKickSpeed = 1.1f;

        /// <summary>Order under the body: beneath the projected shadow (-1) and the blob (-2).</summary>
        private const int OrderOffset = -3;

        /// <summary>Snow depth from which a step lands on snow rather than on the ground.</summary>
        private const float SnowThreshold = 0.35f;

        /// <summary>
        /// What the ground is at a world point. Replaceable for tests; the default consults the
        /// snow clock and the Ground tilemap.
        /// </summary>
        public static Func<Vector2, GroundKind> GroundProbe = DefaultGroundProbe;

        private static WorldGridBuilder s_grid;
        private static Tilemap          s_ground;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            GroundProbe = DefaultGroundProbe;
            s_grid      = null;
            s_ground    = null;
        }

        private Rigidbody2D    _body;
        private SpriteRenderer _sprite;
        private PlayerController _player;
        private DirectionalAnimator _animator;
        private Vector2        _lastHeading = Vector2.right;
        private Vector3        _lastPos;
        private float          _travelled;
        private bool           _leftFoot;

        /// <summary>Puffs this emitter has produced. Test seam.</summary>
        public int Emitted { get; private set; }

        /// <summary>Fired every completed stride, whatever the ground setting — the ground mark
        /// syncs its lit segments to this and must see it even with dust turned off.</summary>
        public event Action StrideCompleted;

        /// <summary>True only for a host carrying a <see cref="PlayerController"/> whose gait is
        /// actually running — every other walker (monsters, a player still walking) is unaffected.</summary>
        private bool IsPlayerRunning
        {
            get
            {
                if (_player == null) _player = GetComponent<PlayerController>();
                return _player != null && _player.IsRunning;
            }
        }

        private void Awake()
        {
            _body    = GetComponent<Rigidbody2D>();
            _sprite  = SpriteTintStack.ResolveBodyRenderer(gameObject);
            _player  = GetComponent<PlayerController>();
            _lastPos = transform.position;
        }

        private void OnEnable()
        {
            if (_animator == null) _animator = GetComponent<DirectionalAnimator>();
            if (_animator != null) _animator.FootContact += HandleFootContact;
        }

        private void OnDisable()
        {
            if (_animator != null) _animator.FootContact -= HandleFootContact;
        }

        /// <summary>True while the animator shows a measured walk or run: footfalls come from its
        /// contact frames and the distance clock stays quiet.</summary>
        private bool ContactDriven
        {
            get
            {
                if (_animator == null) return false;
                var cycle = _animator.CurrentCycle;
                return cycle != null && cycle.HasContacts;
            }
        }

        /// <summary>A drawn foot landed. <paramref name="foot"/> picks the side of the trail.</summary>
        private void HandleFootContact(int foot)
        {
            if (!isActiveAndEnabled) return;
            Vector2 velocity = _body != null ? _body.velocity : Vector2.zero;
            // The last stride of a settling stop is still a footfall, drawn with the body at rest,
            // so the heading falls back to the last one the body travelled.
            Vector2 heading = velocity.sqrMagnitude > 0.01f ? velocity.normalized : _lastHeading;
            _leftFoot = foot == 0;
            Emit(heading, alternate: false);
            StrideCompleted?.Invoke();
        }

        private void Update()
        {
            Vector2 velocity = _body != null
                ? _body.velocity
                : (Vector2)(transform.position - _lastPos) / Mathf.Max(Time.deltaTime, 1e-4f);
            _lastPos = transform.position;
            Tick(Time.deltaTime, velocity);
        }

        /// <summary>
        /// Advance the stride clock by one frame at <paramref name="velocity"/>. Public so a
        /// fixture can walk a creature a known distance without a physics step.
        /// </summary>
        public void Tick(float dt, Vector2 velocity)
        {
            float speed = velocity.magnitude;
            if (speed < MinSpeed)
            {
                _travelled = 0f;
                return;
            }

            _lastHeading = velocity / speed;
            float stride = IsPlayerRunning ? RunStride : Stride;
            _travelled += speed * dt;
            while (_travelled >= stride)
            {
                _travelled -= stride;
                if (ContactDriven) continue;
                Emit(velocity / speed);
                StrideCompleted?.Invoke();
            }
        }

        private void Emit(Vector2 heading, bool alternate = true)
        {
            bool running = IsPlayerRunning;

            // A running stride is heard even with the dust setting off — WorldLookSettings only
            // gates the puff, never the noise a heavy footfall makes.
            if (running)
                NoiseEvents.EmitAt(gameObject, NoiseEvents.LoudnessFootstep * LocomotionTuning.Active.runLoudnessMultiplier);

            if (!WorldLookSettings.Footsteps) return;
            if (_sprite == null) _sprite = SpriteTintStack.ResolveBodyRenderer(gameObject);

            // The feet, alternating left and right of the heading so a straight walk leaves
            // two staggered trails rather than one line of puffs.
            Vector3 feet = FeetPosition();
            if (alternate) _leftFoot = !_leftFoot;
            var side = new Vector2(-heading.y, heading.x) * (_leftFoot ? 0.11f : -0.11f);
            var pos  = feet + (Vector3)side - (Vector3)heading * 0.10f;

            var kind = GroundProbe != null ? GroundProbe(pos) : GroundKind.Dust;

            int layer = _sprite != null ? _sprite.sortingLayerID : SortingLayer.NameToID(Valkur.Core.SortingConfig.LAYER_ENTITIES);
            int order = _sprite != null ? _sprite.sortingOrder + OrderOffset
                                        : Valkur.Core.SortingConfig.ComputeSortingOrder(Valkur.Core.SortingConfig.Z_ENTITY, pos.y) + OrderOffset;

            float scaleMul = running ? RunPuffScaleMul : 1f;
            float lifeMul  = running ? RunPuffLifeMul  : 1f;
            Vector2 kick   = running ? -heading * RunKickSpeed : Vector2.zero;

            if (FootstepDust.Spawn(pos, kind, layer, order, scaleMul, lifeMul, kick) != null) Emitted++;
        }

        private Vector3 FeetPosition()
        {
            if (_sprite != null && _sprite.sprite != null)
            {
                var b = _sprite.bounds;
                return new Vector3(b.center.x, b.min.y + 0.03f, 0f);
            }
            return transform.position;
        }

        private static GroundKind DefaultGroundProbe(Vector2 at)
        {
            if (SnowAccumulation.Amount >= SnowThreshold) return GroundKind.Snow;

            if (s_ground == null)
            {
                if (s_grid == null) s_grid = UnityEngine.Object.FindObjectOfType<WorldGridBuilder>();
                if (s_grid != null) s_ground = s_grid.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            }
            if (s_ground != null)
            {
                var cell = s_ground.WorldToCell(at);
                if (WaterTileIndex.IsWater(s_ground.GetSprite(cell))) return GroundKind.Water;
            }
            return GroundKind.Dust;
        }
    }
}
