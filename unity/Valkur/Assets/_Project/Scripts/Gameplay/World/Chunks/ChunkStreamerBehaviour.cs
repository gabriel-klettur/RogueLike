using UnityEngine;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;

namespace Valkur.Gameplay.World.Chunks
{
    /// <summary>
    /// Thin MonoBehaviour wrapper around <see cref="ChunkStreamer"/>:
    /// reads a focus <see cref="Transform"/> each LateUpdate, converts
    /// its world position to a chunk coord, and asks the streamer to
    /// sync. The streamer itself stays POCO so the streaming policy
    /// remains EditMode-testable without a scene.
    ///
    /// Wired via code or [SerializeField]: tests and the Phase 2.5
    /// PoC use the public properties instead of touching the inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChunkStreamerBehaviour : MonoBehaviour
    {
        [Tooltip("Transform to follow. Each chunk it sits in becomes the centre " +
                 "of the active streaming radius. Usually the player.")]
        [SerializeField] private Transform _focus;

        [Tooltip("Chunks within this Chebyshev distance of the focus chunk stay " +
                 "visible. Increase for further view distance, decrease for tighter " +
                 "memory budget.")]
        [SerializeField] private int _activeRadius = 2;

        [Tooltip("Tile side length per chunk. Must match the world's WorldConfig.ChunkSize. " +
                 "Phase 2.5 keeps this as an explicit field so the streamer doesn't have to " +
                 "thread the active world through every frame.")]
        [SerializeField] private int _chunkSize = 50;

        [Tooltip("World id this streamer is tied to. Tests set it via " +
                 "Configure(); production code wires it during world activation.")]
        [SerializeField] private string _worldSlug = "base";

        private ChunkStreamer _streamer;
        private ChunkCoord _lastFocusCoord;
        private bool _hasFocusCoord;

        public ChunkStreamer Streamer => _streamer;

        public void Configure(IChunkProvider provider, IChunkPainter painter,
                              int activeRadius, int chunkSize, WorldId worldId,
                              Transform focus)
        {
            _streamer       = new ChunkStreamer(provider, painter, activeRadius);
            _activeRadius   = activeRadius;
            _chunkSize      = chunkSize > 0 ? chunkSize : ChunkData.DefaultChunkSize;
            _worldSlug      = worldId.Slug;
            _focus          = focus;
            _hasFocusCoord  = false;
        }

        private void LateUpdate()
        {
            if (_streamer == null || _focus == null) return;
            ChunkCoord focusCoord = ResolveFocusChunk(_focus.position);
            if (_hasFocusCoord && focusCoord.Equals(_lastFocusCoord)) return;
            _streamer.ActiveRadius = _activeRadius;
            _streamer.SyncTo(focusCoord);
            _lastFocusCoord = focusCoord;
            _hasFocusCoord  = true;
        }

        /// <summary>Convert a Unity world position to the chunk coord it
        /// sits in. Tests drive this directly via public reflection so
        /// the LateUpdate path can stay private.</summary>
        public ChunkCoord ResolveFocusChunk(Vector3 worldPos)
        {
            int size = _chunkSize > 0 ? _chunkSize : ChunkData.DefaultChunkSize;
            int cx = Mathf.FloorToInt(worldPos.x / size);
            int cy = Mathf.FloorToInt(worldPos.y / size);
            // Slug-only WorldId is enough here — IsBase check on the streamer
            // (and downstream consumers) keys on Slug.
            return new ChunkCoord(new WorldId(System.Guid.Empty, _worldSlug ?? "base"), cx, cy);
        }
    }
}
