using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Save
{
    /// <summary>
    /// Migrates save data from older schema versions to current.
    /// Pure logic — no IO, no Unity lifecycle.
    /// </summary>
    public static class SaveSchemaMigrator
    {
        public const string CURRENT_SCHEMA = "1.1";

        /// <summary>
        /// Migrate save data to the current schema version if needed.
        /// Returns the (possibly mutated) data with updated schemaVersion.
        /// </summary>
        public static GameSaveData Migrate(GameSaveData data)
        {
            if (data.schemaVersion == CURRENT_SCHEMA)
                return data;

            string from = data.schemaVersion ?? "unknown";

            if (from == "1.0")
            {
                data.schemaVersion = CURRENT_SCHEMA;
                Debug.Log($"[SaveSchemaMigrator] Migrated save from v1.0 to v{CURRENT_SCHEMA}");
                return data;
            }

            Debug.LogWarning($"[SaveSchemaMigrator] Unknown schema version '{from}'. Loading as-is.");
            data.schemaVersion = CURRENT_SCHEMA;
            return data;
        }
    }
}
