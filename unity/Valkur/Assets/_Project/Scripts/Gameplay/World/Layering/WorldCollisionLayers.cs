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

        private static readonly int[] _worldLayerIndices = new int[LayerCount];
        private static int _worldAllIndex = -2;          // -1 means resolved-but-missing; -2 means not yet resolved
        private static bool _resolved;

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
            int mask = 0;
            for (int i = 0; i < LayerCount; i++)
                if (_worldLayerIndices[i] >= 0) mask |= 1 << _worldLayerIndices[i];
            if (_worldAllIndex >= 0) mask |= 1 << _worldAllIndex;
            return mask;
        }

        /// <summary>
        /// Force a re-resolve next access. Call after the project's TagManager
        /// changes (test setup, editor scripts adding layers, etc.).
        /// </summary>
        public static void Invalidate()
        {
            _resolved = false;
            _worldAllIndex = -2;
            for (int i = 0; i < LayerCount; i++) _worldLayerIndices[i] = 0;
        }

        private static void EnsureResolved()
        {
            if (_resolved) return;
            for (int i = 0; i < LayerCount; i++)
                _worldLayerIndices[i] = LayerMask.NameToLayer($"WorldL{i}");
            _worldAllIndex = LayerMask.NameToLayer("WorldAll");
            _resolved = true;
        }
    }
}
