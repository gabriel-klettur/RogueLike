using System;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Core.Rendering;
using Valkur.Gameplay.Combat;
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
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FootstepEmitter : MonoBehaviour
    {
        /// <summary>World units between puffs.</summary>
        public const float Stride = 0.62f;

        /// <summary>Below this speed (u/s) nothing is walking, and the stride resets.</summary>
        public const float MinSpeed = 0.45f;

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
        private Vector3        _lastPos;
        private float          _travelled;
        private bool           _leftFoot;

        /// <summary>Puffs this emitter has produced. Test seam.</summary>
        public int Emitted { get; private set; }

        private void Awake()
        {
            _body    = GetComponent<Rigidbody2D>();
            _sprite  = SpriteTintStack.ResolveBodyRenderer(gameObject);
            _lastPos = transform.position;
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

            _travelled += speed * dt;
            while (_travelled >= Stride)
            {
                _travelled -= Stride;
                Emit(velocity / speed);
            }
        }

        private void Emit(Vector2 heading)
        {
            if (!WorldLookSettings.Footsteps) return;
            if (_sprite == null) _sprite = SpriteTintStack.ResolveBodyRenderer(gameObject);

            // The feet, alternating left and right of the heading so a straight walk leaves
            // two staggered trails rather than one line of puffs.
            Vector3 feet = FeetPosition();
            _leftFoot = !_leftFoot;
            var side = new Vector2(-heading.y, heading.x) * (_leftFoot ? 0.11f : -0.11f);
            var pos  = feet + (Vector3)side - (Vector3)heading * 0.10f;

            var kind = GroundProbe != null ? GroundProbe(pos) : GroundKind.Dust;

            int layer = _sprite != null ? _sprite.sortingLayerID : SortingLayer.NameToID(Valkur.Core.SortingConfig.LAYER_ENTITIES);
            int order = _sprite != null ? _sprite.sortingOrder + OrderOffset
                                        : Valkur.Core.SortingConfig.ComputeSortingOrder(Valkur.Core.SortingConfig.Z_ENTITY, pos.y) + OrderOffset;

            if (FootstepDust.Spawn(pos, kind, layer, order) != null) Emitted++;
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
