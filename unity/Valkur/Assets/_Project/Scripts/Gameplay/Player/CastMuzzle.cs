using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Where a cast leaves THIS creature's body, when the shared four-value
    /// <see cref="Valkur.Data.SpellDefinition.SpellCastAnchor"/> cannot say it.
    ///
    /// <para>That enum is a signed fraction of the caster's half-HEIGHT — Feet, Center,
    /// Hands, Head — plus a forward clearance along the AIM. It is exactly right for a
    /// humanoid, whose sprite is roughly as wide as it is deep and whose hands are directly
    /// above its feet. The red dragon is <b>8.23 x 4.55 world units</b> and its mouth is
    /// three and a half units in FRONT of its pivot: measured, its breath was born at
    /// (+0.50, 3.29) against a mouth at (±3.5, 2.4), i.e. out of the middle of its back.
    /// No value of the enum reaches that, because the error is horizontal and the enum is
    /// vertical.</para>
    ///
    /// <para><b>A muzzle belongs to the CREATURE, never to the spell.</b> The same argument
    /// <c>CastVariant.spellKeys</c> makes: a <c>SpellDefinition</c> is shared by everything
    /// that casts it and knows nothing about the art of any of them, so authoring
    /// <c>castForwardOffset: 3.5</c> on <c>flame_breath</c> would put every other caster's
    /// fire three and a half units out in the open. It is also the wrong axis — that offset
    /// runs along the AIM, and this art only exists facing east and west, so a dragon aiming
    /// north would breathe from a point above its own spine.</para>
    ///
    /// <para><b>The offset is a fraction of the CURRENT frame's own sprite bounds, never a
    /// world distance.</b> Each frame is trimmed to its own alpha, so the sheets differ in
    /// width — the dragon's idle frames are 638 px and its cast frames 527 — and a fixed
    /// world offset would be correct for one state and wrong for the other. A fraction is
    /// also what survives <c>scaleConfig.scaleIdle</c>, since <c>SpriteRenderer.bounds</c>
    /// is world space and already carries the entity's transform scale.</para>
    ///
    /// <para><b>Measured PER FRAME, because the head sweeps.</b> A single pair was tried
    /// first and is kept as the fallback: across the dragon's eight cast frames it lands on
    /// the mouth in four and in open air in the other four, because rearing moves the mouth
    /// from (2.82, 1.31) to (4.09, 3.98) world units — 1.3 units forward and 2.7 up. The
    /// per-frame table (<c>EntityAssetConfig.castMuzzleFrames</c>) is baked from the art by
    /// <c>CastMuzzleBaker</c> and hits the mouth on all eight.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CastMuzzle : MonoBehaviour
    {
        private Vector2 _normalized;
        private SpriteRenderer _renderer;
        private DirectionalAnimator _animator;

        // Per-frame measurements, keyed by sprite name. Null for an entity that only
        // declares the single fallback pair, so the common case allocates nothing.
        private Dictionary<string, Vector2> _perFrame;

        /// <summary>
        /// True when this entity has a muzzle worth consulting. (0,0) is the "nobody
        /// authored one" sentinel — see <c>EntityAssetConfig.HasCastMuzzle</c>.
        /// </summary>
        public bool IsAuthored =>
            !Mathf.Approximately(_normalized.x, 0f) || !Mathf.Approximately(_normalized.y, 0f) ||
            (_perFrame != null && _perFrame.Count > 0);

        /// <summary>The authored pair, for tests and debug overlays.</summary>
        public Vector2 Normalized => _normalized;

        /// <summary>
        /// Installed by <c>EntityAnimationBinder</c> from <c>EntityAssetConfig.castMuzzle</c>,
        /// on the same pass that binds the sprite sets — so a loadout swap that changes the
        /// art re-states the muzzle rather than leaving a stale one behind.
        /// </summary>
        public void Configure(Vector2 normalized, IReadOnlyList<CastMuzzleFrame> perFrame,
                              SpriteRenderer renderer, DirectionalAnimator animator)
        {
            _normalized = normalized;
            _renderer = renderer;
            _animator = animator;

            _perFrame = null;
            if (perFrame == null || perFrame.Count == 0) return;

            _perFrame = new Dictionary<string, Vector2>(perFrame.Count);
            for (int i = 0; i < perFrame.Count; i++)
            {
                var entry = perFrame[i];
                if (entry == null || string.IsNullOrEmpty(entry.frame)) continue;
                _perFrame[entry.frame] = entry.offset;
            }
        }

        /// <summary>
        /// The measurement for the frame currently drawn, or the entity-wide fallback for a
        /// frame the bake did not cover.
        ///
        /// <para>An atlas-packed sprite keeps its own name, which is what makes the lookup
        /// possible at all: <c>AssetDatabase.GetAssetPath</c> returns EMPTY for one, so the
        /// path is not available to identify a frame by, and the name is.</para>
        /// </summary>
        private Vector2 OffsetFor(Sprite sprite)
        {
            if (_perFrame != null && sprite != null &&
                _perFrame.TryGetValue(sprite.name, out Vector2 measured))
                return measured;

            return _normalized;
        }

        /// <summary>
        /// The muzzle in world space, or false when this entity has none (or is not
        /// currently rendering anything, which is what an off-screen culled entity looks
        /// like). Callers fall back to the shared anchor path on false.
        /// </summary>
        public bool TryResolve(out Vector3 world)
        {
            world = transform.position;
            if (!IsAuthored) return false;

            var sr = ResolveRenderer();
            if (sr == null || sr.sprite == null) return false;

            Bounds b = sr.bounds;
            if (b.extents.x <= 0.01f || b.extents.y <= 0.01f) return false;

            Vector2 n = OffsetFor(sr.sprite);
            world = new Vector3(
                b.center.x + DrawnFacingSign() * b.extents.x * n.x,
                b.center.y + b.extents.y * n.y,
                transform.position.z);
            return true;
        }

        private SpriteRenderer ResolveRenderer()
        {
            if (_renderer != null) return _renderer;
            _renderer = GetComponent<SpriteRenderer>();
            if (_renderer == null) _renderer = GetComponentInChildren<SpriteRenderer>();
            return _renderer;
        }

        /// <summary>
        /// Which way the art is DRAWN — +1 east, -1 west — read off the sprite the renderer
        /// is showing this frame.
        ///
        /// <para><b>Read from the frame's own name rather than from a direction table, and
        /// the two really do disagree.</b> <see cref="DirectionalAnimator"/> never flips a
        /// sprite, so an entity drawn in one direction ships baked mirrors and its eight
        /// buckets are filled from two halves — but WHICH buckets take which half is a
        /// per-pipeline fact, not a constant. Measured on the shipped data: the player
        /// pipeline puts S, SE, E, NE and N on the east half, while the wave13 monster
        /// pipeline puts only SE, E and NE there and gives S and N to the west half. A table
        /// here would be right for one of them and silently wrong for the other, which is
        /// the two-lists-that-drift shape this project has paid for more than once. The
        /// sprite's own name is the ground truth and cannot drift from itself.</para>
        ///
        /// <para>Frames from the sheet pipelines are named <c>&lt;key&gt;_&lt;state&gt;_&lt;e|w&gt;&lt;index&gt;</c>.
        /// A sprite that carries no such suffix — the legacy 8-direction strip characters —
        /// falls back to the animator's facing, which for those is honest: they have real
        /// per-direction art, so nothing is mirrored and the head is drawn where the entity
        /// is looking.</para>
        /// </summary>
        private float DrawnFacingSign()
        {
            var sr = ResolveRenderer();
            if (sr != null && sr.sprite != null && TryReadSuffix(sr.sprite.name, out float sign))
                return sign;

            if (_animator == null) return 1f;
            switch (_animator.CurrentDirection)
            {
                case DirectionalAnimator.Direction.NorthWest:
                case DirectionalAnimator.Direction.West:
                case DirectionalAnimator.Direction.SouthWest:
                    return -1f;
                default:
                    return 1f;
            }
        }

        /// <summary>
        /// +1 for a name ending <c>_e&lt;digits&gt;</c>, -1 for <c>_w&lt;digits&gt;</c>,
        /// false for anything else. Walks back over the trailing digits rather than matching
        /// a fixed index width, because a sheet with ten or more frames per half exists
        /// (the mague ships eleven) and a single-digit assumption would silently stop
        /// matching at frame 10.
        /// </summary>
        internal static bool TryReadSuffix(string spriteName, out float sign)
        {
            sign = 1f;
            if (string.IsNullOrEmpty(spriteName)) return false;

            int i = spriteName.Length - 1;
            if (!char.IsDigit(spriteName[i])) return false;
            while (i >= 0 && char.IsDigit(spriteName[i])) i--;
            if (i <= 0) return false;

            char half = spriteName[i];
            if (spriteName[i - 1] != '_') return false;

            if (half == 'e' || half == 'E') { sign = 1f; return true; }
            if (half == 'w' || half == 'W') { sign = -1f; return true; }
            return false;
        }
    }
}
