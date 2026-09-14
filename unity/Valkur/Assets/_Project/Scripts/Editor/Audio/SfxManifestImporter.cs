using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Binds the sound effects staged by <c>tools/audio/import_sfx_choices.py</c> into
    /// <see cref="AudioCatalogSO"/>. The Python half copies the chosen files under
    /// <c>Audio/SFX/</c> and writes <c>tools/audio/generated/sfx_import.json</c>; this half only
    /// maps ids to clips.
    ///
    /// <para>An id that already has a clip is left alone: a sound somebody authored by hand wins
    /// over a downloaded one, the same "authored value wins" contract the persona and tileset
    /// importers follow. Empty slots on <see cref="CombatSfxConfigSO"/> that the game reads
    /// (NPC death, level up) are filled the same way — only when empty.</para>
    ///
    /// <para>No <c>Undo.RecordObject</c> (a bulk import on the global undo stack can be reverted
    /// in memory by the test suite) and no <c>AssetDatabase.SaveAssets</c> (it writes every
    /// dirty asset in the project, not just these two).</para>
    /// </summary>
    public static class SfxManifestImporter
    {
        private const string ManifestPath = "../../../tools/audio/generated/sfx_import.json";
        private const string CombatConfigPath = "Assets/_Project/Data/CombatSfxConfig.asset";

        [Serializable]
        private class ImportRow
        {
            public string id;
            public string group;
            public string assetPath;
        }

        [Serializable]
        private class ImportFile
        {
            public ImportRow[] rows = Array.Empty<ImportRow>();
            public string[] npcDeathIds = Array.Empty<string>();
            public string levelUpId = string.Empty;
        }

        [MenuItem("Valkur/Audio/Import Downloaded SFX")]
        public static void Import()
        {
            string file = Path.GetFullPath(Path.Combine(Application.dataPath, ManifestPath));
            if (!File.Exists(file))
            {
                Debug.LogError($"[SfxImport] Manifest not found: {file}");
                return;
            }

            var data = JsonUtility.FromJson<ImportFile>(File.ReadAllText(file));
            var catalog = AudioCatalogLocator.Find();
            if (catalog == null) return;

            AssetDatabase.Refresh();
            var entries = new List<SfxEntry>(catalog.SfxEntries);
            var byId = new Dictionary<string, SfxEntry>(StringComparer.Ordinal);
            foreach (var e in entries)
                if (!string.IsNullOrEmpty(e.id)) byId[e.id] = e;

            int added = 0, filled = 0, kept = 0, missing = 0;
            foreach (var row in data.rows)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(row.assetPath);
                if (clip == null)
                {
                    Debug.LogWarning($"[SfxImport] No AudioClip at {row.assetPath} (id '{row.id}').");
                    missing++;
                    continue;
                }

                if (byId.TryGetValue(row.id, out var existing))
                {
                    if (existing.clip != null) { kept++; continue; }
                    existing.clip = clip;
                    existing.group = row.group;
                    filled++;
                    continue;
                }

                var entry = new SfxEntry { id = row.id, clip = clip, group = row.group };
                entries.Add(entry);
                byId[row.id] = entry;
                added++;
            }

            catalog.EditorSetSfx(entries.ToArray());
            catalog.InvalidateCache();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);

            var combat = AssetDatabase.LoadAssetAtPath<CombatSfxConfigSO>(CombatConfigPath);
            if (combat != null)
            {
                bool dirty = false;
                if ((combat.NpcDeathSfxIds == null || combat.NpcDeathSfxIds.Length == 0) && data.npcDeathIds.Length > 0)
                {
                    combat.EditorSetNpcDeath(data.npcDeathIds);
                    dirty = true;
                }
                if (string.IsNullOrEmpty(combat.LevelUpSfxId) && !string.IsNullOrEmpty(data.levelUpId))
                {
                    combat.EditorSetLevelUp(data.levelUpId);
                    dirty = true;
                }
                if (dirty)
                {
                    EditorUtility.SetDirty(combat);
                    AssetDatabase.SaveAssetIfDirty(combat);
                }
            }

            Debug.Log($"[SfxImport] added {added}, filled {filled}, kept authored {kept}, missing clip {missing}.");
        }
    }
}
