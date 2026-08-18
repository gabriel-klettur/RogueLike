using System;
using System.IO;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Shared scaffolding for raw-JSON repositories backed by a single file
    /// per world inside <c>StreamingAssets</c>. Lights, spawners, particles,
    /// and buildings all follow the same file layout and atomic-write
    /// pattern; centralising it here keeps the per-domain subclasses small
    /// and makes a future change to the IO mechanism (e.g. compression,
    /// hashing, server backend) a one-place edit.
    ///
    /// Path layout:
    ///   - <see cref="WorldId.Base"/> -> StreamingAssets/&lt;Subdir&gt;/&lt;FileName&gt;
    ///     (legacy flat layout; preserves byte-compatibility with existing
    ///     builds and saves).
    ///   - other worlds -> StreamingAssets/Worlds/&lt;slug&gt;/&lt;Subdir&gt;/&lt;FileName&gt;.
    ///   - base world + a non-default Map Editor slot, when the subclass opts
    ///     in via <see cref="IsMapSlotAware"/> -> persistentDataPath/Maps/&lt;slot&gt;/&lt;Subdir&gt;/&lt;FileName&gt;.
    ///
    /// The map-slot axis is orthogonal to the world axis: worlds are designed
    /// dimensions that ship with the game, slots are user-authored maps created
    /// at runtime from the Map Editor (F11). Slot routing only ever applies
    /// inside the base world, so a non-base world keeps its own flat layout
    /// regardless of which slot is active.
    ///
    /// Atomic writes: tmp file + <see cref="File.Replace(string, string, string)"/>
    /// with sidecar .bak so a crash mid-write cannot truncate the previous content.
    /// </summary>
    public abstract class WorldStreamingFileRepositoryBase
    {
        protected abstract string Subdir   { get; }
        protected abstract string FileName { get; }

        // Optional override for tests — points at a temp directory instead
        // of Application.streamingAssetsPath.
        private readonly string _streamingRootOverride;

        protected WorldStreamingFileRepositoryBase(string streamingRootOverride = null)
        {
            _streamingRootOverride = streamingRootOverride;
        }

        public string PathFor(WorldId worldId)
        {
            string dir = WorldDirectory(worldId);
            EnsureDirectory(dir);
            return Path.Combine(dir, FileName);
        }

        public bool ExistsFile(WorldId worldId) => File.Exists(PathFor(worldId));

        public string ReadFile(WorldId worldId)
        {
            string path = PathFor(worldId);
            if (!File.Exists(path)) return null;
            try { return File.ReadAllText(path); }
            catch (Exception ex)
            {
                Debug.LogError($"[{GetType().Name}] Read '{path}' failed: {ex.Message}");
                return null;
            }
        }

        public void WriteFileAtomic(WorldId worldId, string content)
        {
            string path = PathFor(worldId);
            string tmp  = path + ".tmp";
            File.WriteAllText(tmp, content ?? string.Empty);
            if (File.Exists(path))
                File.Replace(tmp, path, path + ".bak");
            else
                File.Move(tmp, path);
        }

        protected string StreamingRoot
            => _streamingRootOverride ?? Application.streamingAssetsPath;

        /// <summary>
        /// Opt-in flag for per-map-slot routing inside the base world. Domains
        /// whose content is authored per map (spawners, lights, particles, item
        /// drops) override this to <c>true</c> so editing one slot can never
        /// overwrite another's file — the same isolation Buildings gets through
        /// <see cref="MapEditorActiveSlot"/>.
        ///
        /// Stays <c>false</c> for shipped catalog data that is shared by every
        /// slot (e.g. <c>Maps/zones_database.json</c>): routing that per slot
        /// would fork the zone catalog and break zone lookups on custom maps.
        /// </summary>
        protected virtual bool IsMapSlotAware => false;

        private string WorldDirectory(WorldId worldId)
        {
            if (!worldId.IsBase)
                return Path.Combine(StreamingRoot, "Worlds", worldId.Slug, Subdir);

            // A pinned root (tests, run-scoped drop stores) is an explicit
            // instruction about where the data lives — never second-guess it
            // with slot routing, or a run's WorldDrops folder would migrate
            // into Maps/<slot>/ the moment the user opens a custom map.
            if (IsMapSlotAware && string.IsNullOrEmpty(_streamingRootOverride))
                return MapEditorActiveSlot.DirForActiveSlot(Subdir);

            return Path.Combine(StreamingRoot, Subdir);
        }

        private static void EnsureDirectory(string dir)
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
    }
}
