using System;
using System.IO;
using UnityEngine;
using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Production <see cref="IBuildingInstanceRepository"/> backed by
    /// <c>StreamingAssets/Buildings/buildings_instances.json</c>. The legacy
    /// path layout is preserved for <see cref="WorldId.Base"/> so existing
    /// builds and saves continue to read / write to exactly the same file.
    /// Non-base worlds nest under
    /// <c>StreamingAssets/Worlds/&lt;slug&gt;/Buildings/buildings_instances.json</c>.
    ///
    /// Atomicity: <see cref="WriteRawJson"/> writes via a temp file +
    /// <see cref="File.Replace"/> with sidecar <c>.bak</c>, mirroring the
    /// protection pattern adopted across the persistence layer this Phase
    /// (see <c>MapEditorManager.Persistence</c>, tile-override repository).
    /// </summary>
    public sealed class JsonFileBuildingInstanceRepository : IBuildingInstanceRepository
    {
        private const string LEGACY_DIR_BASE = "Buildings";
        private const string FILE_NAME       = "buildings_instances.json";

        // Optional override for tests that want a temp directory instead of
        // Application.streamingAssetsPath. Production callers use the
        // parameterless constructor.
        private readonly string _streamingRootOverride;

        public JsonFileBuildingInstanceRepository() : this(null) { }

        public JsonFileBuildingInstanceRepository(string streamingRootOverride)
        {
            _streamingRootOverride = streamingRootOverride;
        }

        public string PathFor(WorldId worldId)
        {
            string dir = WorldDirectory(worldId);
            EnsureDirectory(dir);
            return Path.Combine(dir, FILE_NAME);
        }

        public bool Exists(WorldId worldId) => File.Exists(PathFor(worldId));

        public string ReadRawJson(WorldId worldId)
        {
            string path = PathFor(worldId);
            if (!File.Exists(path)) return null;
            try { return File.ReadAllText(path); }
            catch (Exception ex)
            {
                Debug.LogError($"[BuildingInstanceRepository] Read '{path}' failed: {ex.Message}");
                return null;
            }
        }

        public void WriteRawJson(WorldId worldId, string json)
        {
            string path = PathFor(worldId);
            string tmp  = path + ".tmp";
            File.WriteAllText(tmp, json ?? string.Empty);
            if (File.Exists(path))
                File.Replace(tmp, path, path + ".bak");
            else
                File.Move(tmp, path);
        }

        // ── Path helpers ─────────────────────────────────────────────────────────

        private string StreamingRoot
            => _streamingRootOverride ?? Application.streamingAssetsPath;

        // The base world keeps the historical flat layout
        // (StreamingAssets/Buildings/buildings_instances.json). Non-base
        // worlds nest under Worlds/<slug>/Buildings/. Phase 1 multi-world
        // can introduce additional worlds without disturbing existing data.
        private string WorldDirectory(WorldId worldId)
        {
            if (worldId.Equals(WorldId.Base) || string.IsNullOrEmpty(worldId.Slug))
                return Path.Combine(StreamingRoot, LEGACY_DIR_BASE);
            return Path.Combine(StreamingRoot, "Worlds", worldId.Slug, LEGACY_DIR_BASE);
        }

        private static void EnsureDirectory(string dir)
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
    }
}
