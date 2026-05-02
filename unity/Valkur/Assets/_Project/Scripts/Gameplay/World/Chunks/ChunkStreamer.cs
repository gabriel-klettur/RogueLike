using System.Collections.Generic;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;

namespace Valkur.Gameplay.World.Chunks
{
    /// <summary>
    /// Computes which chunks should be visible around a focus tile and
    /// drives an <see cref="IChunkPainter"/> to make that set true.
    /// Pure POCO — does NOT touch <c>Time.time</c> or
    /// <c>Update()</c>; a thin MonoBehaviour wrapper feeds it a focus
    /// position each frame. This split keeps the streaming policy
    /// fully testable in EditMode without standing up a player or
    /// camera.
    ///
    /// Phase-2.5 streaming policy is intentionally simple:
    ///   - <see cref="ActiveRadius"/>: chunks within this Chebyshev
    ///     distance of the focus chunk are always visible.
    ///   - Chunks outside the radius are hidden on the next sync.
    ///   - No keep-alive band, no LOD, no async streaming yet — Phase 3
    ///     extends this once the procedural read-side proves stable in
    ///     real gameplay.
    ///
    /// <see cref="SyncTo"/> is idempotent: calling it twice with the
    /// same focus is a no-op (no re-paint of already-visible chunks,
    /// no re-hide of already-hidden ones).
    /// </summary>
    public sealed class ChunkStreamer
    {
        private readonly IChunkProvider _provider;
        private readonly IChunkPainter  _painter;
        private readonly HashSet<ChunkCoord> _active = new HashSet<ChunkCoord>();

        public int ActiveRadius { get; set; }

        /// <summary>Read-only view of the chunk coords currently active on
        /// screen. Tests assert against this; runtime code rarely needs it.</summary>
        public IReadOnlyCollection<ChunkCoord> ActiveChunks => _active;

        public ChunkStreamer(IChunkProvider provider, IChunkPainter painter, int activeRadius = 2)
        {
            _provider     = provider ?? throw new System.ArgumentNullException(nameof(provider));
            _painter      = painter  ?? throw new System.ArgumentNullException(nameof(painter));
            ActiveRadius  = activeRadius;
        }

        /// <summary>Make the active set match the chunks within
        /// <see cref="ActiveRadius"/> of <paramref name="focus"/>. Calls
        /// painter.Show for new entrants and painter.Hide for chunks
        /// that left.</summary>
        public void SyncTo(ChunkCoord focus)
        {
            // Compute the desired set deterministically so the order of
            // Show calls is stable across frames — useful for tests and
            // for predictable lighting batching once that lands.
            var desired = new HashSet<ChunkCoord>();
            int r = ActiveRadius;
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    desired.Add(new ChunkCoord(focus.World, focus.Cx + dx, focus.Cy + dy));

            // Hide chunks no longer wanted (do this BEFORE Show so the
            // painter / pool sees the freed resources before allocating
            // fresh ones — matters once TilemapPool lands).
            var toHide = new List<ChunkCoord>();
            foreach (var c in _active)
                if (!desired.Contains(c)) toHide.Add(c);
            for (int i = 0; i < toHide.Count; i++)
            {
                _painter.Hide(toHide[i]);
                _active.Remove(toHide[i]);
            }

            // Show chunks newly entering the radius.
            foreach (var c in desired)
            {
                if (_active.Contains(c)) continue;
                if (!_provider.Has(c)) continue;
                var chunk = _provider.Get(c);
                if (chunk == null) continue;
                _painter.Show(chunk);
                _active.Add(c);
            }
        }

        /// <summary>Clear the active set entirely. Used on world swap so
        /// the painter releases all tilemap regions before the next
        /// world's chunks come online.</summary>
        public void HideAll()
        {
            foreach (var c in _active) _painter.Hide(c);
            _active.Clear();
        }
    }
}
