using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spawners;

namespace Valkur.Editor.Spawners
{
    /// <summary>
    /// Migrates <c>StreamingAssets/Spawners/spawners_instances.json</c> from schema v1 (a row
    /// is a pointer at a preset) to v2 (a row owns its configuration).
    ///
    /// <para>The runtime loader performs the same freeze and write-back the first time anyone
    /// presses Play in the Editor, which is what carries an author's own maps forward. This
    /// exists so the SHIPPED file can be migrated deterministically, once, as a reviewable
    /// diff, rather than depending on whoever next happens to hit Play — a tracked
    /// StreamingAssets file that rewrites itself on somebody's machine is a diff nobody
    /// intended to author.</para>
    ///
    /// <para>Both paths call <see cref="SpawnerInstanceSerializer"/>, so they cannot produce
    /// different files.</para>
    /// </summary>
    public static class SpawnerInstanceMigrationTool
    {
        private const string InstancesPath = "Assets/StreamingAssets/Spawners/spawners_instances.json";
        private const string CatalogPath   = "Assets/_Project/Data/Catalogs/Spawners/SpawnerTemplateCatalog.asset";

        [MenuItem("Valkur/Spawners/Migrate Instances To v2 (Dry Run)")]
        public static void DryRun() => Run(apply: false);

        [MenuItem("Valkur/Spawners/Migrate Instances To v2")]
        public static void Apply() => Run(apply: true);

        private static void Run(bool apply)
        {
            string absolute = Path.Combine(Directory.GetCurrentDirectory(), InstancesPath);
            if (!File.Exists(absolute))
            {
                Debug.LogError($"[SpawnerMigration] No instances file at '{InstancesPath}'.");
                return;
            }

            var catalog = AssetDatabase.LoadAssetAtPath<SpawnerTemplateCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError($"[SpawnerMigration] No SpawnerTemplateCatalog at '{CatalogPath}'. " +
                               "Without it a v1 row has nothing to freeze against.");
                return;
            }

            var records = SpawnerInstanceSerializer.ParseAll(File.ReadAllText(absolute));
            if (records == null)
            {
                // Refusing is the safe direction. A file this cannot parse is a file a write
                // would destroy, and the author still has whatever is there.
                Debug.LogError("[SpawnerMigration] Could not parse the instances file. Refusing " +
                               "to write anything over it.");
                return;
            }

            int before = 0;
            foreach (var r in records) if (r != null && r.Config == null) before++;

            if (before == 0)
            {
                Debug.Log($"[SpawnerMigration] All {records.Count} record(s) already carry a " +
                          $"config — nothing to do.");
                return;
            }

            SpawnerInstanceSerializer.Freeze(records, catalog);

            int unresolved = 0;
            foreach (var r in records)
            {
                if (r == null || r.Config != null) continue;
                unresolved++;
                // Named individually: a row left at v1 is one whose preset is missing from the
                // catalogue, which is a data defect worth fixing rather than a migration
                // failure. It is deliberately left alone rather than frozen against a blank
                // preset, which would replace an author's data with defaults in silence.
                Debug.LogWarning($"[SpawnerMigration] '{r.InstanceId}' stays v1 — preset " +
                                 $"'{r.TemplateId}' is not in the catalogue.");
            }

            int migrated = before - unresolved;

            if (!apply)
            {
                Debug.Log($"[SpawnerMigration] DRY RUN — would migrate {migrated} of " +
                          $"{records.Count} record(s); {unresolved} would stay v1.");
                return;
            }

            File.WriteAllText(absolute, SpawnerInstanceSerializer.Serialize(records));
            AssetDatabase.ImportAsset(InstancesPath, ImportAssetOptions.ForceUpdate);

            Debug.Log($"[SpawnerMigration] Migrated {migrated} of {records.Count} record(s) to " +
                      $"schema v{SpawnerInstanceSerializer.SchemaVersion}. Each placement now " +
                      "owns its configuration and no longer tracks the preset it came from.");
        }
    }
}
