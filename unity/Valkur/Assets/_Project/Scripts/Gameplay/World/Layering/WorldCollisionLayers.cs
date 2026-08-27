using UnityEngine;

namespace Valkur.Gameplay.World.Layering
{
    /// <summary>
    /// Canonical accessors for the M2 per-visual-layer Physics2D layers
    /// (<c>WorldL0..WorldL8</c> + <c>WorldAll</c>) allocated at indices 18..27 in
    /// the project's TagManager. Resolves names → indices lazily on first access
    /// and caches the result so layer lookups don't pay a string compare every
    /// frame inside hot paths like <see cref="VisualLayerColliderSync"/>.
    ///
    /// One slot per visual layer (matching the
    /// <see cref="World.TilemapLayerSetup.TilemapLayer"/> enum 0..8) + a
    /// wildcard <c>WorldAll</c> slot for collider cells tagged <c>"*"</c>.
    /// Entities that need per-layer collision filtering (Player today; NPCs +
    /// Projectiles in M2.2) live on their own gameplay layer and use
    /// <see cref="Collider2D.includeLayers"/> to enable exactly one
    /// <c>WorldL{N}</c> slot at a time, plus the always-on <c>WorldAll</c>.
    /// </summary>
    public static class WorldCollisionLayers
    {
        public const int LayerCount = 9; // WorldL0..WorldL8

        /// <summary>Legacy gameplay layer holding the building <c>CollTile_*</c> boxes.</summary>
        public const int WorldLayer = 11;

        /// <summary>Legacy gameplay layer for whole-building colliders.</summary>
        public const int BuildingLayer = 14;

        private static readonly int[] _worldLayerIndices = new int[LayerCount];
        private static int _worldAllIndex = -2;          // -1 means resolved-but-missing; -2 means not yet resolved
        private static bool _resolved;

        // Derived masks, computed once per resolve. PathFinder.IsWalkable asks for
        // the blocking mask once per expanded cell — up to maxNodes*4 times per
        // search — so rebuilding it from a loop on every call is not free.
        private static int _allWorldLayersMask;
        private static int _blockingMask;

        /// <summary>
        /// Resolve the physics-layer index for <c>WorldL{visualLayer}</c>. Returns
        /// <c>-1</c> if the layer hasn't been added to TagManager yet.
        /// </summary>
        public static int GetWorldLayerIndex(int visualLayer)
        {
            EnsureResolved();
            if (visualLayer < 0 || visualLayer >= LayerCount) return -1;
            return _worldLayerIndices[visualLayer];
        }

        /// <summary>Resolve the physics-layer index for <c>WorldAll</c>. -1 when missing.</summary>
        public static int GetWorldAllIndex()
        {
            EnsureResolved();
            return _worldAllIndex;
        }

        /// <summary>
        /// LayerMask containing exactly <c>WorldL{visualLayer}</c> + <c>WorldAll</c>.
        /// What an entity assigns to <see cref="Collider2D.includeLayers"/> to
        /// restrict its collisions to "my own layer + the wildcard".
        /// </summary>
        public static int IncludeMaskFor(int visualLayer)
        {
            int mask = 0;
            int wl = GetWorldLayerIndex(visualLayer);
            if (wl >= 0) mask |= 1 << wl;
            int wa = GetWorldAllIndex();
            if (wa >= 0) mask |= 1 << wa;
            return mask;
        }

        /// <summary>
        /// LayerMask containing every <c>WorldL{N}</c> (0..8) + <c>WorldAll</c>.
        /// Used by entities that should collide with EVERY painted collider
        /// regardless of tag (NPCs, projectiles in M2.1).
        /// </summary>
        public static int AllWorldLayersMask()
        {
            EnsureResolved();
            return _allWorldLayersMask;
        }

        /// <summary>
        /// Everything that physically blocks movement, pathing, a blink and a
        /// projectile: the legacy <c>World</c>(11) + <c>Building</c>(14) layers
        /// PLUS every painted <c>WorldL{N}</c> / <c>WorldAll</c> slot that
        /// <see cref="WorldCollisionBaker"/> writes its cells into.
        ///
        /// Query-based systems MUST use this rather than a hand-rolled
        /// <c>(1 &lt;&lt; 11) | (1 &lt;&lt; 14)</c>: the baker disables the source
        /// <c>Collision</c> tilemap's own collider and re-emits every painted cell
        /// onto 18..27, so the two legacy layers alone see only the building boxes.
        /// A path solved against them routes straight through painted walls, and a
        /// projectile flies through them.
        /// </summary>
        public static int BlockingMask()
        {
            EnsureResolved();
            return _blockingMask;
        }

        /// <summary>
        /// Force a re-resolve next access. Call after the project's TagManager
        /// changes (test setup, editor scripts adding layers, etc.).
        /// </summary>
        /// <summary>
        /// Domain Reload is OFF, so the resolved indices and the two derived masks
        /// would otherwise carry into the next Play session. They are stable while the
        /// TagManager is, but "stable in practice" is how a cache becomes a
        /// second-Play bug — and this type now owns four more statics than it did.
        /// <see cref="VisualLayerPhysicsSetup"/> already calls
        /// <see cref="Invalidate"/> from its own hook; this one makes the type
        /// self-sufficient rather than dependent on another type running first.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() => Invalidate();

        public static void Invalidate()
        {
            _resolved = false;
            _worldAllIndex = -2;
            _allWorldLayersMask = 0;
            _blockingMask = 0;
            for (int i = 0; i < LayerCount; i++) _worldLayerIndices[i] = 0;
        }

        private static void EnsureResolved()
        {
            if (_resolved) return;
            for (int i = 0; i < LayerCount; i++)
                _worldLayerIndices[i] = LayerMask.NameToLayer($"WorldL{i}");
            _worldAllIndex = LayerMask.NameToLayer("WorldAll");

            _allWorldLayersMask = 0;
            for (int i = 0; i < LayerCount; i++)
                if (_worldLayerIndices[i] >= 0) _allWorldLayersMask |= 1 << _worldLayerIndices[i];
            if (_worldAllIndex >= 0) _allWorldLayersMask |= 1 << _worldAllIndex;

            _blockingMask = (1 << WorldLayer) | (1 << BuildingLayer) | _allWorldLayersMask;

            _resolved = true;
        }
    }
}
