using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Valkur.Core.Coordinates;

namespace Valkur.Gameplay.Save
{
    /// <summary>
    /// Handles all file IO for the save system: read, write, checksum,
    /// backup rotation, directory management, and slot listing.
    /// Pure IO — no game state knowledge.
    ///
    /// Folder layout (post-refactor — per-run isolation):
    /// <code>
    ///   Saves/
    ///     .recovery/
    ///       position_checkpoint.json
    ///       position_checkpoint_bak.json
    ///     legacy/                         ← saves migrated without run_id
    ///       *.json
    ///     &lt;runId&gt;/
    ///       autosave.json                 ← single auto-save entry per run
    ///       &lt;manual_name&gt;.json           ← user-created manual saves
    ///       .backups/
    ///         autosave_1.json … autosave_5.json
    /// </code>
    /// All "Auto-Save" semantics (timer autosave, shutdown save, quicksave, exit save)
    /// collapse to the same per-run <c>autosave.json</c>. Manual saves are ANY save
    /// the player explicitly named via <see cref="SaveService.Save(string)"/>.
    /// </summary>
    public static partial class SaveFileManager
    {
        // ── Directory layout constants ───────────────────────────────────────
        private const string SAVE_DIR        = "Saves";
        private const string RECOVERY_SUBDIR = ".recovery";
        private const string BACKUPS_SUBDIR  = ".backups";
        private const string LEGACY_SUBDIR   = "legacy";

        public const string AUTOSAVE_NAME    = "autosave";
        public const string AUTOSAVE_DISPLAY = "Auto-Save";

        private const string SAVE_EXTENSION     = ".json";
        private const string CHECKSUM_EXTENSION = ".sha256";
        public  const int    MAX_BACKUPS        = 5;

        // Names that must never appear in a user-visible save list and that the
        // user is forbidden from picking when manually saving.
        private static readonly HashSet<string> ReservedSaveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "autosave",
            "position_checkpoint",
            "position_checkpoint_bak",
            // Legacy filenames — migrated into per-run autosave.json on boot.
            // Listed here so any leftover never leaks into the UI.
            "quicksave",
            "shutdown_save",
            "autosave_0", "autosave_1", "autosave_2", "autosave_3", "autosave_4",
        };

        // ── Path helpers ─────────────────────────────────────────────────────

        public static string GetSaveDirectory()      => System.IO.Path.Combine(Application.persistentDataPath, SAVE_DIR);
        public static string GetRecoveryDirectory()  => System.IO.Path.Combine(GetSaveDirectory(), RECOVERY_SUBDIR);
        public static string GetLegacyRunDirectory() => System.IO.Path.Combine(GetSaveDirectory(), LEGACY_SUBDIR);

        /// <summary>Returns the per-run folder for <paramref name="runId"/>. Empty/null routes to the legacy folder.</summary>
        public static string GetRunDirectory(string runId)
            => GetRunDirectory(runId, WorldId.Base);

        /// <summary>
        /// Phase 1 per-world overload. <see cref="WorldId.Base"/> preserves
        /// the legacy flat layout (<c>Saves/&lt;runId&gt;/...</c>) so existing
        /// saves remain readable byte-for-byte. Non-base worlds nest under
        /// <c>Saves/&lt;runId&gt;/worlds/&lt;slug&gt;/...</c> from day one so a
        /// session that visited multiple dimensions does not collapse them
        /// into one save folder.
        /// </summary>
        public static string GetRunDirectory(string runId, WorldId worldId)
        {
            if (string.IsNullOrEmpty(runId)) return GetLegacyRunDirectory();
            string runRoot = System.IO.Path.Combine(GetSaveDirectory(), SanitizeRunIdComponent(runId));
            if (worldId.IsBase)
                return runRoot;
            return System.IO.Path.Combine(runRoot, "worlds", SanitizeRunIdComponent(worldId.Slug));
        }

        public static string GetBackupsDirectory(string runId) =>
            GetBackupsDirectory(runId, WorldId.Base);

        public static string GetBackupsDirectory(string runId, WorldId worldId) =>
            System.IO.Path.Combine(GetRunDirectory(runId, worldId), BACKUPS_SUBDIR);

        public static string GetAutosavePath(string runId) =>
            GetAutosavePath(runId, WorldId.Base);

        public static string GetAutosavePath(string runId, WorldId worldId) =>
            System.IO.Path.Combine(GetRunDirectory(runId, worldId), AUTOSAVE_NAME + SAVE_EXTENSION);

        public static string GetManualSavePath(string runId, string slotName) =>
            GetManualSavePath(runId, slotName, WorldId.Base);

        public static string GetManualSavePath(string runId, string slotName, WorldId worldId) =>
            System.IO.Path.Combine(GetRunDirectory(runId, worldId), slotName + SAVE_EXTENSION);

        public static bool IsReservedSaveName(string name) =>
            !string.IsNullOrEmpty(name) && ReservedSaveNames.Contains(name);

        // Defensive: don't allow directory traversal or path separators in run-id components.
        private static string SanitizeRunIdComponent(string runId)
        {
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(runId.Length);
            foreach (char c in runId)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            string s = sb.ToString().Trim('.', ' ');
            return string.IsNullOrEmpty(s) ? "_invalid" : s;
        }

        public static string SanitizeSaveName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw.Trim())
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            string name = sb.ToString().Trim('.', ' ');
            return string.IsNullOrEmpty(name) ? null : name;
        }
    }
}
