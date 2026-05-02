using System;
using System.IO;
using UnityEngine;
using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Production <see cref="IMapEditorZonesRepository"/> backed by
    /// <c>Application.persistentDataPath/map_editor_zones.json</c> (and a
    /// sidecar <c>.bak</c>). Encapsulates the atomic-write +
    /// sidecar-fallback pattern that previously lived inline in
    /// <c>MapEditorManager.Persistence</c>.
    ///
    /// Path layout: <see cref="WorldId.Base"/> uses the legacy flat path
    /// (<c>persistentDataPath/map_editor_zones.json</c>) so existing user
    /// data is byte-compatible. Non-base worlds nest under
    /// <c>persistentDataPath/Worlds/&lt;slug&gt;/map_editor_zones.json</c> from
    /// day one — Phase 1 multi-world drops in without churn.
    /// </summary>
    public sealed class JsonFileMapEditorZonesRepository : IMapEditorZonesRepository
    {
        private const string FILE_NAME = "map_editor_zones.json";

        private readonly string _rootOverride;

        public JsonFileMapEditorZonesRepository() : this(null) { }

        /// <summary>Test-friendly ctor: lets a fixture point at a temp directory
        /// instead of <see cref="Application.persistentDataPath"/>.</summary>
        public JsonFileMapEditorZonesRepository(string rootOverride)
        {
            _rootOverride = rootOverride;
        }

        public string PathFor(WorldId worldId) => Path.Combine(WorldDirectory(worldId), FILE_NAME);

        public bool Exists(WorldId worldId)
        {
            string p = PathFor(worldId);
            return File.Exists(p) || File.Exists(p + ".bak");
        }

        public string ReadWithSidecarFallback(WorldId worldId, out bool recoveredFromSidecar)
        {
            recoveredFromSidecar = false;
            string primary = PathFor(worldId);
            string[] candidates = { primary, primary + ".bak" };
            for (int i = 0; i < candidates.Length; i++)
            {
                string path = candidates[i];
                if (!File.Exists(path)) continue;
                try
                {
                    string content = File.ReadAllText(path);
                    if (i > 0) recoveredFromSidecar = true;
                    return content;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[MapEditorZonesRepository] Read '{path}' failed: {ex.Message} — trying next candidate.");
                }
            }
            return null;
        }

        public void WriteAtomic(WorldId worldId, string json)
        {
            string path = PathFor(worldId);
            string dir  = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string tmp = path + ".tmp";
            string bak = path + ".bak";
            File.WriteAllText(tmp, json ?? string.Empty);
            if (File.Exists(path))
            {
                // Replace bumps current target -> .bak, promotes tmp -> target.
                File.Replace(tmp, path, bak);
            }
            else
            {
                File.Move(tmp, path);
                // First save with no prior file: still seed a .bak so the
                // very next write isn't unprotected.
                try { File.Copy(path, bak, overwrite: true); } catch { /* best-effort */ }
            }
        }

        // ── Path helpers ─────────────────────────────────────────────────────────

        private string PersistenceRoot
            => _rootOverride ?? Application.persistentDataPath;

        // Base world keeps the historical flat layout so the
        // MapEditorDataGuard recovery flow keeps finding the file at the
        // exact path it has always used.
        private string WorldDirectory(WorldId worldId)
        {
            if (worldId.Equals(WorldId.Base) || string.IsNullOrEmpty(worldId.Slug))
                return PersistenceRoot;
            return Path.Combine(PersistenceRoot, "Worlds", worldId.Slug);
        }
    }
}
