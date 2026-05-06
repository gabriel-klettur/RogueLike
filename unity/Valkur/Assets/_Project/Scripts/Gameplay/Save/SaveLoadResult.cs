using Valkur.Data;

namespace Valkur.Gameplay.Save
{
    /// <summary>
    /// Outcome of a save-load attempt. <c>Data == null</c> means every
    /// candidate file (primary + every backup slot) was either missing or
    /// failed checksum/parse validation. <c>RecoveredFromBackup</c> tells
    /// the UI whether the player's primary autosave was corrupted so it
    /// can surface a toast — silent corruption recovery is a footgun, the
    /// player should know their main file was repaired.
    /// </summary>
    public readonly struct SaveLoadResult
    {
        public readonly GameSaveData Data;
        public readonly bool   RecoveredFromBackup;
        /// <summary>1-based index of the backup slot used (1 = newest backup).
        /// -1 when the primary file was used or the load failed entirely.</summary>
        public readonly int    BackupSlotIndex;
        /// <summary>Absolute path of the file that produced <see cref="Data"/>.
        /// Null when the load failed.</summary>
        public readonly string SourcePath;

        public SaveLoadResult(GameSaveData data, bool recoveredFromBackup, int backupSlotIndex, string sourcePath)
        {
            Data                = data;
            RecoveredFromBackup = recoveredFromBackup;
            BackupSlotIndex     = backupSlotIndex;
            SourcePath          = sourcePath;
        }

        public bool IsSuccess => Data != null;

        public static readonly SaveLoadResult Empty = new SaveLoadResult(null, false, -1, null);
    }
}
