using System;
using System.IO;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Production <see cref="IBuildingInstanceRepository"/> backed by
    /// <c>buildings_instances.json</c>.
    ///
    /// Path resolution is map-slot aware via <see cref="MapEditorActiveSlot"/>:
    ///   • Default slot → <c>StreamingAssets/Buildings/buildings_instances.json</c>
    ///     (the baseline world that ships with the build).
    ///   • Custom slot  → <c>persistentDataPath/Maps/&lt;slot&gt;/Buildings/buildings_instances.json</c>
    ///     (per-slot, runtime-writable on every Unity target).
    ///
    /// We deliberately resolve the slot AT CALL TIME (rather than at
    /// construction) so the same long-lived repository instance follows the
    /// user as they switch maps via the Map Editor without anyone having to
    /// remember to rebuild it.
    /// </summary>
    public sealed class JsonFileBuildingInstanceRepository : IBuildingInstanceRepository
    {
        private const string FILE_NAME = "buildings_instances.json";

        // Optional override for tests — points at a temp directory instead of
        // the live StreamingAssets / persistentDataPath roots. Only honoured
        // when MapEditorActiveSlot is in default-slot mode (custom slots route
        // through the helper's own test overrides for parity).
        private readonly string _streamingRootOverride;

        public JsonFileBuildingInstanceRepository() : this(null) { }

        public JsonFileBuildingInstanceRepository(string streamingRootOverride)
        {
            _streamingRootOverride = streamingRootOverride;
        }

        public string PathFor(WorldId worldId)
        {
            string dir = ResolveBuildingsDir(worldId);
            EnsureDirectory(dir);
            return Path.Combine(dir, FILE_NAME);
        }

        public bool   Exists(WorldId worldId)                    => File.Exists(PathFor(worldId));
        public string ReadRawJson(WorldId worldId)               => ReadFile(worldId);
        public void   WriteRawJson(WorldId worldId, string json) => WriteFileAtomic(worldId, json);

        // ── IO helpers ──────────────────────────────────────────────────────────

        private string ReadFile(WorldId worldId)
        {
            string path = PathFor(worldId);
            if (!File.Exists(path)) return null;
            try { return File.ReadAllText(path); }
            catch (Exception ex)
            {
                Debug.LogError($"[JsonFileBuildingInstanceRepository] Read '{path}' failed: {ex.Message}");
                return null;
            }
        }

        private void WriteFileAtomic(WorldId worldId, string content)
        {
            string path = PathFor(worldId);
            string tmp  = path + ".tmp";
            File.WriteAllText(tmp, content ?? string.Empty);
            if (File.Exists(path))
                File.Replace(tmp, path, path + ".bak");
            else
                File.Move(tmp, path);
        }

        private string ResolveBuildingsDir(WorldId worldId)
        {
            // Test override wins over everything: legacy fixtures construct
            // the repo with a temp dir to keep their writes off the project's
            // real StreamingAssets — we must keep honouring that contract or
            // those tests start corrupting shipping data.
            if (!string.IsNullOrEmpty(_streamingRootOverride))
            {
                if (!worldId.IsBase)
                    return Path.Combine(_streamingRootOverride, "Worlds", worldId.Slug, "Buildings");
                return Path.Combine(_streamingRootOverride, "Buildings");
            }

            // Multi-world (WorldId != Base) takes precedence over map slots:
            // it's a separate axis (planned per-world content like "shop world",
            // "tutorial world") and was already wired up before slots existed.
            // Within a non-Base world we keep the legacy flat layout.
            if (!worldId.IsBase)
                return Path.Combine(StreamingRoot, "Worlds", worldId.Slug, "Buildings");

            // Base world routes through the slot-aware helper. Default slot
            // resolves to StreamingAssets/Buildings (legacy); custom slots to
            // persistentDataPath/Maps/<slot>/Buildings.
            return MapEditorActiveSlot.BuildingsDirForActiveSlot();
        }

        private string StreamingRoot
            => _streamingRootOverride ?? Application.streamingAssetsPath;

        private static void EnsureDirectory(string dir)
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
    }
}
