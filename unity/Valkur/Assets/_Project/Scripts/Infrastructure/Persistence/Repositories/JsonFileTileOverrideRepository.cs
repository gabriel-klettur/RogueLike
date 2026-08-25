using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Valkur.Core.Coordinates;

namespace Valkur.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Production <see cref="ITileOverrideRepository"/> implementation backed by
    /// the JSON files that live in
    /// <c>Application.persistentDataPath/MapOverrides/&lt;worldId&gt;/&lt;zone&gt;.overlay.json</c>.
    ///
    /// Behaviour intentionally mirrors the legacy static helpers on
    /// <c>TileOverlayPersistence</c> (where this implementation pulls its
    /// roots), with one Phase-0 deliberate change: paths now include
    /// <see cref="WorldId.Slug"/> so multi-world (Phase 1) doesn't require
    /// touching a single callsite. While still single-world today, the slug
    /// is always <c>"base"</c>.
    ///
    /// <para>Atomicity: <see cref="Write"/> uses tmp-then-replace via
    /// <see cref="File.Replace(string, string, string)"/> when an existing
    /// file is being overwritten so a crash mid-write cannot truncate the
    /// previous content. First-time writes (no prior file) fall back to a
    /// rename — the standard atomic pattern on NTFS.</para>
    /// </summary>
    public sealed class JsonFileTileOverrideRepository : ITileOverrideRepository
    {
        private const string OVERRIDES_DIR  = "MapOverrides";
        private const string FILE_EXTENSION = ".overlay.json";

        private readonly string _root;

        public JsonFileTileOverrideRepository()
            : this(Path.Combine(Application.persistentDataPath, OVERRIDES_DIR)) { }

        /// <summary>Test-friendly constructor that lets a fixture point at a
        /// scratch directory instead of the real persistentDataPath.</summary>
        public JsonFileTileOverrideRepository(string rootDirectory)
        {
            _root = rootDirectory ?? Path.Combine(Application.persistentDataPath, OVERRIDES_DIR);
        }

        public string PathFor(WorldId worldId, string zoneName)
        {
            EnsureDirectory(worldId);
            return Path.Combine(WorldDirectory(worldId), zoneName + FILE_EXTENSION);
        }

        public bool Exists(WorldId worldId, string zoneName)
            => !string.IsNullOrEmpty(zoneName) && File.Exists(PathFor(worldId, zoneName));

        public string Read(WorldId worldId, string zoneName)
        {
            if (string.IsNullOrEmpty(zoneName)) return null;
            string path = PathFor(worldId, zoneName);
            if (!File.Exists(path)) return null;
            try { return File.ReadAllText(path); }
            catch (Exception ex)
            {
                Debug.LogError($"[TileOverrideRepository] Read '{path}' failed: {ex.Message}");
                return null;
            }
        }

        public void Write(WorldId worldId, string zoneName, string overlayJson)
        {
            if (string.IsNullOrEmpty(zoneName))
                throw new ArgumentException("zoneName must be set", nameof(zoneName));
            string path = PathFor(worldId, zoneName);

            // Per-CALL unique temp name (GUID suffix), not per-path. A fixed
            // "<path>.tmp" is shared by every writer of this zone — two
            // overlapping writes (e.g. the debounced background autosave and
            // an explicit SaveZone racing it) open the same handle and the
            // loser throws "Access to the path is denied". See the
            // documented trap in CLAUDE.md: this is NOT hypothetical, it was
            // measured happening on this exact pattern elsewhere in the repo.
            string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tmp, overlayJson ?? string.Empty);
                SwapIntoPlace(tmp, path);
            }
            finally
            {
                // Best-effort cleanup: if something above threw before the
                // temp file was consumed (moved/replaced away), don't leave
                // an orphaned GUID-named .tmp file behind — it will never be
                // reused since the name is unique per call.
                if (File.Exists(tmp))
                {
                    try { File.Delete(tmp); } catch { /* best-effort only */ }
                }
            }
        }


        /// <summary>
        /// Move <paramref name="tmp"/> onto <paramref name="path"/>, tolerating a
        /// concurrent writer swapping the same target underneath us.
        /// <para>
        /// Both directions of the existence check race: if the target is absent we
        /// <see cref="File.Move(string,string)"/>, but another writer may create it
        /// first and Move then throws "Cannot create a file when that file already
        /// exists"; if the target is present we <see cref="File.Replace(string,string,string)"/>,
        /// but another writer may delete it first and Replace then throws. So neither
        /// branch is safe on its own — the loop re-reads the state and retries the
        /// OTHER branch instead of failing. Every writer owns a GUID-named temp, so a
        /// retry only ever re-attempts our own swap.
        /// </para>
        /// Note Mono's File.Replace is not Win32 ReplaceFile and is not truly atomic;
        /// what carries data across a crash in that window is the rotating backups and
        /// the checksum, not this method. What this does buy is that a reader never
        /// observes a half-written file.
        /// </summary>
        private static void SwapIntoPlace(string tmp, string path)
        {
            const int maxAttempts = 5;
            Exception lastError = null;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Replace(tmp, path, null);
                    else                   File.Move(tmp, path);
                    return;
                }
                catch (IOException ex) { lastError = ex; }
                catch (UnauthorizedAccessException ex) { lastError = ex; }
            }

            // Last resort: drop the target and move ours in. Wider crash window than
            // the swap above, which is why it only runs after the retries are spent.
            try
            {
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                throw new IOException(
                    $"Could not swap '{tmp}' onto '{path}' after {maxAttempts} attempts: {ex.Message}", lastError ?? ex);
            }
        }

        public bool Delete(WorldId worldId, string zoneName)
        {
            if (string.IsNullOrEmpty(zoneName)) return false;
            string path = PathFor(worldId, zoneName);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }

        public bool Rename(WorldId worldId, string fromZoneName, string toZoneName)
        {
            if (string.IsNullOrEmpty(fromZoneName) || string.IsNullOrEmpty(toZoneName)) return false;
            if (string.Equals(fromZoneName, toZoneName, StringComparison.Ordinal)) return true;

            string oldPath = PathFor(worldId, fromZoneName);
            if (!File.Exists(oldPath)) return true;

            string newPath = PathFor(worldId, toZoneName);
            if (File.Exists(newPath))
            {
                Debug.LogWarning(
                    $"[TileOverrideRepository] Cannot rename '{fromZoneName}' -> '{toZoneName}': " +
                    "destination already exists. Old file preserved.");
                return false;
            }

            try { File.Move(oldPath, newPath); return true; }
            catch (Exception ex)
            {
                Debug.LogError($"[TileOverrideRepository] Rename '{fromZoneName}' -> '{toZoneName}' failed: {ex.Message}");
                return false;
            }
        }

        public IEnumerable<string> ListAvailableZones(WorldId worldId)
        {
            string dir = WorldDirectory(worldId);
            if (!Directory.Exists(dir)) yield break;
            foreach (var path in Directory.GetFiles(dir, "*" + FILE_EXTENSION))
            {
                string name = Path.GetFileName(path);
                if (name.EndsWith(FILE_EXTENSION, StringComparison.OrdinalIgnoreCase))
                    name = name.Substring(0, name.Length - FILE_EXTENSION.Length);
                yield return name;
            }
        }

        // ── Path helpers ─────────────────────────────────────────────────────────

        // The "base" world keeps the historical flat layout (no /worlds/<slug>/
        // prefix) so existing on-disk overlays continue to load after the
        // repository introduction. Non-base worlds nest their overlays under
        // their slug from day one — Phase 1 will migrate "base" to the same
        // layout on first multi-world boot.
        private string WorldDirectory(WorldId worldId)
            => worldId.IsBase
                ? _root
                : Path.Combine(_root, worldId.Slug);

        private void EnsureDirectory(WorldId worldId)
        {
            string dir = WorldDirectory(worldId);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
    }
}
