using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor
{
    /// <summary>
    /// Editor tool: imports Python audio.json â†’ AudioCatalogSO + CombatSfxConfigSO.
    /// Menu: Valkur > Audio > Import Catalog from Python JSON
    /// </summary>
    public static partial class AudioCatalogImporter
    {
        private const string PYTHON_AUDIO_JSON = "python/data/config/audio.json";
        private const string CATALOG_PATH      = "Assets/_Project/Data/AudioCatalog.asset";
        private const string COMBAT_SFX_PATH   = "Assets/_Project/Data/CombatSfxConfig.asset";
        private const string MUSIC_FOLDER      = "Assets/_Project/Audio/Music";
        private const string SFX_ROOT          = "Assets/_Project/Audio/SFX";
        private const string AMBIENT_ROOT      = "Assets/_Project/Audio/SFX/Ambient";

        [MenuItem("Valkur/Audio/Import Catalog from Python JSON")]
        public static void ImportFromPython()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            string jsonPath    = Path.Combine(projectRoot, PYTHON_AUDIO_JSON);

            if (!File.Exists(jsonPath))
            {
                Debug.LogError($"[AudioImporter] Python audio.json not found at: {jsonPath}");
                return;
            }

            string json = File.ReadAllText(jsonPath);
            // JsonUtility doesn't support Dictionary fields — always use manual MiniJson parser
            var data = ParseManual(json);

            var catalog    = LoadOrCreateAsset<AudioCatalogSO>(CATALOG_PATH);
            var combatSfx  = LoadOrCreateAsset<CombatSfxConfigSO>(COMBAT_SFX_PATH);

            ImportTracks(catalog, data);
            ImportSfxMap(catalog, data);
            ImportDefaults(catalog, data);
            ImportScopeOverrides(catalog, data);
            ImportCombatSfx(combatSfx, data);

            EditorUtility.SetDirty(catalog);
            EditorUtility.SetDirty(combatSfx);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[AudioImporter] Done. Catalog: {catalog.Tracks.Length} tracks, {catalog.SfxEntries.Length} SFX. " +
                      $"CombatSfx: {combatSfx.PlayerDamageSfxIds.Length} player damage, {combatSfx.SlashSfxIds.Length} slash.");
        }
    }
}
