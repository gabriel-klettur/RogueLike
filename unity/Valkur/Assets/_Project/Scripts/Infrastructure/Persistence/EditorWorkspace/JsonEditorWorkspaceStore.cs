using System;
using System.IO;
using UnityEngine;
using Valkur.Core.Editors;

namespace Valkur.Infrastructure.Persistence.EditorWorkspaces
{
    /// <summary>
    /// One JSON document per editor under
    /// <c>Application.persistentDataPath/EditorWorkspace/&lt;editor&gt;.json</c>.
    ///
    /// Per-machine, per-user, outside git: a panel layout is a personal preference, not
    /// project data, and versioning it would turn every panel drag into a diff.
    /// </summary>
    public sealed class JsonEditorWorkspaceStore : IEditorWorkspaceStore
    {
        public const string FOLDER_NAME = "EditorWorkspace";

        private const int SWAP_ATTEMPTS   = 4;
        private const int SWAP_BACKOFF_MS = 12;

        private readonly string _root;
        private readonly bool   _isProductionRoot;

        /// <summary>
        /// Production constructor — writes under <see cref="Application.persistentDataPath"/>.
        /// </summary>
        public JsonEditorWorkspaceStore()
            : this(Path.Combine(Application.persistentDataPath, FOLDER_NAME), true) { }

        /// <summary>
        /// Test constructor — writes wherever it is told.
        ///
        /// An injectable root rather than a blanket "refuse outside Play Mode" guard,
        /// because that guard would make the round trip untestable in EditMode, which is
        /// where this layer's contract tests live. The hazard it exists for — an EditMode
        /// test scribbling into the player's real folder, which cost this project a run
        /// once (<c>.github/incidents/RUN_TWIN_SAVE.md</c>) — is answered more directly by
        /// pointing tests somewhere else entirely. The Play-Mode refusal below still guards
        /// the PRODUCTION root, so a stray edit-time write cannot reach it either.
        /// </summary>
        public JsonEditorWorkspaceStore(string rootDirectory)
            : this(rootDirectory, false) { }

        private JsonEditorWorkspaceStore(string rootDirectory, bool isProductionRoot)
        {
            _root             = rootDirectory;
            _isProductionRoot = isProductionRoot;
        }

        public string RootDirectory => _root;

        // ── Load ────────────────────────────────────────────────────────────────

        public EditorWorkspace Load(string editorName)
        {
            string path = PathFor(editorName);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                // A workspace that cannot be read is not an error worth a red console: the
                // author loses their layout, not their work. Say it once, at warning level,
                // and open with defaults.
                Debug.LogWarning($"[EditorWorkspace] Could not read '{path}': {ex.Message}");
                return null;
            }

            if (string.IsNullOrWhiteSpace(json)) return null;

            EditorWorkspace ws;
            try
            {
                ws = JsonUtility.FromJson<EditorWorkspace>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EditorWorkspace] Malformed document '{path}': {ex.Message}");
                return null;
            }

            if (ws == null) return null;

            // An unknown schema version is discarded WHOLE, never read partially. Half a
            // remembered layout is worse than none: the author cannot tell which panels are
            // where they left them and which are at some build-old default.
            if (ws.schemaVersion != EditorWorkspace.CURRENT_SCHEMA_VERSION)
                return null;

            ws.panels    ??= new System.Collections.Generic.List<EditorPanelState>();
            ws.session   ??= new System.Collections.Generic.List<EditorWorkspaceEntry>();
            ws.selection ??= new EditorSelectionRecord();
            return ws;
        }

        // ── Save ────────────────────────────────────────────────────────────────

        public void Save(EditorWorkspace workspace)
        {
            if (workspace == null || string.IsNullOrEmpty(workspace.editorName)) return;
            if (RefuseProductionWriteOutsidePlayMode("Save")) return;

            string path = PathFor(workspace.editorName);
            if (string.IsNullOrEmpty(path)) return;

            workspace.schemaVersion = EditorWorkspace.CURRENT_SCHEMA_VERSION;

            try
            {
                Directory.CreateDirectory(_root);
                WriteAtomic(path, JsonUtility.ToJson(workspace, prettyPrint: true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EditorWorkspace] Could not write '{path}': {ex.Message}");
            }
        }

        public void Delete(string editorName)
        {
            if (RefuseProductionWriteOutsidePlayMode("Delete")) return;

            string path = PathFor(editorName);
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EditorWorkspace] Could not delete '{path}': {ex.Message}");
            }
        }

        // ── Internals ───────────────────────────────────────────────────────────

        /// <summary>
        /// Temp-and-rename, with a GUID temp name per write.
        ///
        /// A SHARED temp name is neither atomic nor safe — this project already paid for
        /// that lesson: two overlapping writers opened the same <c>&lt;path&gt;.tmp</c> and
        /// the loser threw "Access to the path is denied". The swap retries because the
        /// existence check races the other writer whichever way round it is written.
        ///
        /// What this buys is that a READER never sees a half-written document. It is not a
        /// crash guarantee: measured elsewhere in this project, Mono's
        /// <c>File.Replace</c> is not Win32 <c>ReplaceFile</c> and still leaves the target
        /// momentarily absent. For a panel layout that is an acceptable loss — the cost of
        /// the worst case is opening with default docks.
        /// </summary>
        private static void WriteAtomic(string path, string contents)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, contents);

            for (int attempt = 0; attempt < SWAP_ATTEMPTS; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(temp, path);
                    return;
                }
                catch (IOException) when (attempt < SWAP_ATTEMPTS - 1)
                {
                    System.Threading.Thread.Sleep(SWAP_BACKOFF_MS);
                }
                catch (UnauthorizedAccessException) when (attempt < SWAP_ATTEMPTS - 1)
                {
                    System.Threading.Thread.Sleep(SWAP_BACKOFF_MS);
                }
            }

            // Every attempt lost the race. Drop the temp rather than leaving litter behind.
            try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
        }

        private bool RefuseProductionWriteOutsidePlayMode(string operation)
        {
            if (!_isProductionRoot || Application.isPlaying) return false;
            Debug.LogWarning(
                $"[EditorWorkspace] {operation} refused: not in Play Mode. " +
                "Edit-time writes to the player's own folder are what produced the " +
                "twin-save incident; construct the store with an explicit root in tests.");
            return true;
        }

        private string PathFor(string editorName)
        {
            string safe = Sanitize(editorName);
            return string.IsNullOrEmpty(safe) ? null : Path.Combine(_root, safe + ".json");
        }

        /// <summary>
        /// Editor names are human strings ("Time &amp; Weather", "Dungeon NodeGraph"), so
        /// they cannot go straight into a filename. Anything outside the safe set collapses
        /// to '_'; the result is lowercased so two editors differing only in case cannot
        /// address two files on a case-sensitive filesystem and one file on Windows.
        /// </summary>
        public static string Sanitize(string editorName)
        {
            if (string.IsNullOrWhiteSpace(editorName)) return null;

            var sb = new System.Text.StringBuilder(editorName.Length);
            foreach (char c in editorName.Trim())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
                else if (c == '-' || c == '_') sb.Append(c);
                else sb.Append('_');
            }

            string s = sb.ToString().Trim('_');
            return s.Length == 0 ? null : s;
        }
    }
}
