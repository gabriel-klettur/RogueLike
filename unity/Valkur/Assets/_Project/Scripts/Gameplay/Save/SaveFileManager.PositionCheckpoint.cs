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

        public static void WritePositionCheckpoint(PositionCheckpointData data)
        {
            EnsureSaveDirectory();
            string json = JsonUtility.ToJson(data, false);
            string path = GetPositionCheckpointPath();
            string tmp  = path + ".tmp";

            File.WriteAllText(tmp, json);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);

            try { File.WriteAllText(GetPositionCheckpointBakPath(), json); }
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
