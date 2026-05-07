using System;
using System.IO;
using UnityEngine;

// RunGroupInfo / SaveSlotInfo live in the parent Valkur.Gameplay namespace;
// C# resolves them implicitly from the nested Valkur.Gameplay.Save scope, so
// no explicit `using` is needed here (matches SaveFileManager.Listing.cs).
namespace Valkur.Gameplay.Save
{
    public static partial class SaveFileManager
    {
        // ── Phantom-run pruning ──────────────────────────────────────────────
        //
        // A "phantom run" is a Saves/<runId>/ folder whose only content is a
        // single autosave.json that captures the player at the spawn defaults
        // (level <= 1, experience == 0). These accumulate when the user opens
        // the game, looks around the lobby, and exits without doing anything
        // worth saving — every Exit through the pause menu used to force a
        // QuickSave ignoring the dirty flag, leaving a fresh phantom folder
        // behind. PauseMenu now gates that QuickSave on IsSessionDirty, but
        // existing phantoms still pollute the Load Game panel until pruned.
        //
        // The check is intentionally strict (single autosave + level <= 1 +
        // 0 XP) so a real run that just hasn't gained XP yet survives intact.
        // Manual saves are NEVER pruned — once the user explicitly named a
        // slot, the run is kept regardless of progression metrics.

        /// <summary>
        /// Deletes phantom run folders. Returns the number of folders pruned.
        /// </summary>
        /// <param name="activeRunIdToPreserve">
        /// When set, the run with this id is left untouched even if it would
        /// otherwise qualify as phantom (so an in-progress session that just
        /// started — e.g. the player is still in the lobby — is never wiped
        /// from under the running SaveService).
        /// </param>
        public static int PrunePhantomRuns(string activeRunIdToPreserve = null)
        {
            int prunedCount = 0;
            var groups = ListSavesByRun();
            foreach (var group in groups)
            {
                if (!IsPrunableGroup(group, activeRunIdToPreserve)) continue;

                string runDir = GetRunDirectory(group.runId);
                try
                {
                    if (Directory.Exists(runDir))
                    {
                        Directory.Delete(runDir, recursive: true);
                        prunedCount++;
                        Debug.Log($"[SaveFileManager] Pruned phantom run: {group.runId} " +
                                  $"({group.displayName})");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SaveFileManager] Failed to prune phantom run " +
                                     $"'{group.runId}' at '{runDir}': {ex.Message}");
                }
            }
            if (prunedCount > 0)
                Debug.Log($"[SaveFileManager] PrunePhantomRuns deleted {prunedCount} folder(s).");
            return prunedCount;
        }

        /// <summary>
        /// Pure predicate exposed for tests / diagnostics. True when the group
        /// represents a phantom run, i.e. one of:
        ///   1. Single-autosave folder with no meaningful progression
        ///      (level &lt;= 1 + 0 XP). The "Lobby walked-around" case.
        ///   2. Single-autosave folder whose runOrdinal is 0 (or the meta
        ///      key is missing). A run never gets ordinal=0 once
        ///      ProfileTelemetrySystem.StartRun finalises it — that value
        ///      only exists in the brief bootstrap window between
        ///      SaveService.BeginNewRun and StartRun. A saved file with
        ///      ordinal=0 means a write fired inside that window and left
        ///      an orphan Saves/&lt;guid&gt;/ folder, the "phantom burst" bug.
        /// </summary>
        public static bool IsPhantomRun(RunGroupInfo group)
        {
            if (group == null || group.saves == null || group.saves.Count != 1) return false;
            var only = group.saves[0];
            if (!only.isAutoSave) return false;
            if (only.isCorrupted) return false;

            // Pattern 1: low-progression Lobby phantom.
            bool lowProgress = only.level <= 1 && only.experience == 0;
            if (lowProgress) return true;

            // Pattern 2: orphan-ordinal phantom (write fired before
            // StartTelemetryRun set the per-profile ordinal). Legitimate
            // resumed runs always carry a positive ordinal because Load
            // restores it from disk; legitimate fresh runs always carry
            // a positive ordinal because the bootstrap calls StartRun
            // before any event fires. ordinal=0 in a persisted save is
            // therefore always an artefact of the BeginNewRun→StartRun
            // race window.
            if (only.runOrdinal == 0) return true;

            return false;
        }

        private static bool IsPrunableGroup(RunGroupInfo group, string activeRunIdToPreserve)
        {
            if (group == null) return false;
            // Legacy bucket has runId="" — never auto-delete legacy saves;
            // those represent old players whose data we want to keep visible.
            if (group.isLegacy) return false;
            if (string.IsNullOrEmpty(group.runId)) return false;
            if (!string.IsNullOrEmpty(activeRunIdToPreserve)
                && string.Equals(group.runId, activeRunIdToPreserve, StringComparison.OrdinalIgnoreCase))
                return false;
            return IsPhantomRun(group);
        }
    }
}
