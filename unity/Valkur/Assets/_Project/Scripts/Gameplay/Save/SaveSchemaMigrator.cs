using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay.Save
{
    /// <summary>
    /// Migrates save data from older schema versions to current.
    /// Pure logic — no IO, no Unity lifecycle.
    /// Delegates to <see cref="SaveMigrationChain"/> so registered steps run in order.
    /// </summary>
    public static class SaveSchemaMigrator
    {
        public const string CURRENT_SCHEMA = "1.1";

        // Register built-in steps on first access.
        static SaveSchemaMigrator()
        {
            SaveMigrationChain.Register("1.0", "1.1", data =>
            {
                // v1.0 -> v1.1: nothing structural changed; the version bump itself
                // is the migration marker. Keep explicit so the chain walks.
            });
        }

        /// <summary>
        /// Migrate save data to the current schema version if needed.
        /// Returns the (possibly mutated) data with updated schemaVersion.
        /// </summary>
        public static GameSaveData Migrate(GameSaveData data)
        {
            if (data == null) return null;
            if (data.schemaVersion == CURRENT_SCHEMA) return data;

            string from = string.IsNullOrEmpty(data.schemaVersion) ? "1.0" : data.schemaVersion;
            int steps = SaveMigrationChain.MigrateTo(data, CURRENT_SCHEMA);
            if (data.schemaVersion != CURRENT_SCHEMA)
            {
                Debug.LogWarning($"[SaveSchemaMigrator] Could not reach v{CURRENT_SCHEMA} from '{from}' (applied {steps} step(s)). Forcing current version tag.");
                data.schemaVersion = CURRENT_SCHEMA;
            }
            else if (steps > 0)
            {
                Debug.Log($"[SaveSchemaMigrator] Migrated save from v{from} to v{CURRENT_SCHEMA} in {steps} step(s)");
            }
            return data;
        }
    }
}
