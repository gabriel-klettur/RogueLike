using System.IO;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Save
{
    public static partial class SaveFileManager
    {
        // ── Position checkpoint ──────────────────────────────────────────────

        private const string POSITION_CHECKPOINT_FILE     = "position_checkpoint";
        private const string POSITION_CHECKPOINT_BAK_FILE = "position_checkpoint_bak";

        public static string GetPositionCheckpointPath() =>
            Path.Combine(GetRecoveryDirectory(), POSITION_CHECKPOINT_FILE + SAVE_EXTENSION);

        public static string GetPositionCheckpointBakPath() =>
            Path.Combine(GetRecoveryDirectory(), POSITION_CHECKPOINT_BAK_FILE + SAVE_EXTENSION);

        private static string GetLegacyPositionCheckpointPath() =>
            Path.Combine(GetSaveDirectory(), POSITION_CHECKPOINT_FILE + SAVE_EXTENSION);
        private static string GetLegacyPositionCheckpointBakPath() =>
            Path.Combine(GetSaveDirectory(), POSITION_CHECKPOINT_BAK_FILE + SAVE_EXTENSION);

        /// <summary>
        /// Writes the crash-recovery position checkpoint.
        ///
        /// Deliberately calls <see cref="EnsureSaveDirectoriesExist"/> and NOT
        /// <c>EnsureSaveDirectory</c>. This runs several times a second and every
        /// full save mirrors it, so it used to drag the whole maintenance pass —
        /// legacy migration plus <c>PruneEmptyRunFolders</c> — along with it. The
        /// cost was a directory scan per checkpoint; the DEFECT was that the prune
        /// deleted the live run's folder while the async autosave was still on its
        /// way into it. A writer that only needs its directory to exist asks for
        /// exactly that.
        ///
        /// The write goes through <see cref="WriteTextAtomic"/> for the reason the
        /// save files do: a fixed "<c>.tmp</c>" name is one handle shared by every
        /// writer of this path, and the hand-rolled delete-then-move it replaces
        /// left the checkpoint existing nowhere for the width of the rename.
        /// </summary>
        public static void WritePositionCheckpoint(PositionCheckpointData data)
        {
            EnsureSaveDirectoriesExist();
            string json = JsonUtility.ToJson(data, false);

            WriteTextAtomic(GetPositionCheckpointPath(), json);

            try { WriteTextAtomic(GetPositionCheckpointBakPath(), json); }
            catch { /* backup is best-effort */ }
        }

        public static PositionCheckpointData ReadPositionCheckpoint() =>
            TryReadPositionCheckpoint(GetPositionCheckpointPath())
            ?? TryReadPositionCheckpoint(GetPositionCheckpointBakPath());

        private static PositionCheckpointData TryReadPositionCheckpoint(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;
                var data = JsonUtility.FromJson<PositionCheckpointData>(json);
                return (data != null && !string.IsNullOrEmpty(data.timestamp)) ? data : null;
            }
            catch { return null; }
        }

        public static void DeletePositionCheckpoint()
        {
            try { if (File.Exists(GetPositionCheckpointPath()))    File.Delete(GetPositionCheckpointPath()); }    catch { }
            try { if (File.Exists(GetPositionCheckpointBakPath())) File.Delete(GetPositionCheckpointBakPath()); } catch { }
            try { if (File.Exists(GetLegacyPositionCheckpointPath()))    File.Delete(GetLegacyPositionCheckpointPath()); }    catch { }
            try { if (File.Exists(GetLegacyPositionCheckpointBakPath())) File.Delete(GetLegacyPositionCheckpointBakPath()); } catch { }
        }
    }
}
