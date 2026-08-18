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

        // ── Write-from-EditMode guard ──────────────────────────────────────────
        //
        // The May 23 incident lost 38 user zones from the default map because
        // an EditMode test (running without an InMemory repo or temp-path
        // override) hit the default ctor here, wrote its 7-zone seed straight
        // into the user's real `Application.persistentDataPath/map_editor_zones.json`,
        // and orphaned the user's working state. Tests have backup/restore in
        // their own SetUp/TearDown, but any chain hiccup leaves the live file
        // corrupted.
        //
        // This guard mirrors `SaveService.RefuseWriteOutsidePlayMode`. It
        // refuses writes that originate from EditMode AGAINST the production
        // path (default ctor). Tests have two ways to opt in to file IO
        // intentionally:
        //   • Use the (string rootOverride) ctor with a temp directory — most
        //     tests don't need real-path semantics and should switch to this.
        //   • Set `AllowEditModeWritesToRealPath = true` inside a using-block
        //     around a deliberate test of the real path (MapEditorPersistenceIntegrationTests).
        // Static so RAII patterns can scope the override per-test.

        /// <summary>
        /// Set <see langword="true"/> from a test's <c>[SetUp]</c> when the
        /// test deliberately needs to read/write the real persistentDataPath
        /// file (and does its own backup/restore). Restore to <see langword="false"/>
        /// in <c>[TearDown]</c>. Default-ctor writes go through the guard,
        /// override-path writes never do — that path is implicitly trusted.
        /// </summary>
        public static bool AllowEditModeWritesToRealPath { get; set; }

        /// <summary>
        /// Re-arms the guard on every Play. A test that opted in and failed before its
        /// TearDown would otherwise leave the door open for the rest of the domain's
        /// life — which is exactly the 38-zone loss of 2026-05-23 this guard exists to
        /// prevent.
        /// </summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetGuardOnPlayModeEnter()
        {
            AllowEditModeWritesToRealPath = false;
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
            // Refuse EditMode writes against the production path unless a
            // test has explicitly opted in. See the AllowEditModeWritesToRealPath
            // comment above for the full rationale (May 23 38-zone loss).
            // The override-ctor path (_rootOverride != null) is implicitly
            // trusted: a caller that pointed at a temp dir is by definition
            // not writing to user data.
            if (_rootOverride == null
                && !Application.isPlaying
                && !AllowEditModeWritesToRealPath)
            {
                Debug.LogWarning(
                    "[MapEditorZonesRepository] Refused EditMode write to production " +
                    "map_editor_zones.json. Tests must inject an InMemoryMapEditorZonesRepository " +
                    "OR construct JsonFileMapEditorZonesRepository(tempPath) OR set " +
                    "AllowEditModeWritesToRealPath = true around a deliberate real-path test " +
                    "(remember to reset it in TearDown). This guard prevents the May 23 " +
                    "zone-loss class of bug where a test seed clobbered user data.");
                return;
            }

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
            if (worldId.IsBase)
                return PersistenceRoot;
            return Path.Combine(PersistenceRoot, "Worlds", worldId.Slug);
        }
    }
}
