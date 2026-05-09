#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Data.Dungeon.Udemy;

namespace Valkur.Editor.Dungeon
{
    /// <summary>
    /// One-shot importer that converts the DungeonGunner "Catacombs" theme
    /// (Udemy course assets, used here with the creator's permission) into
    /// Valkur-native <see cref="RoomTemplateSO"/> assets.
    ///
    /// Pipeline:
    /// 1. Read every <c>Room_*Catacombs*.asset</c> already copied to
    ///    <c>Resources/Dungeon/Catacombs/RoomTemplates/</c>.
    /// 2. Parse its YAML for bounds + doorways + prefab GUID + room-node type GUID.
    /// 3. Build a Valkur RoomTemplateSO at <c>Resources/Dungeon/Catacombs/Valkur/</c>
    ///    with the same geometry, the prefab reference, and the right
    ///    <see cref="RoomNodeTypeSO"/> resolved by name (Entrance/Corridor*/Room/BossRoom).
    /// 4. Strip the missing-script MonoBehaviours from each Catacombs .prefab
    ///    so Unity stops emitting "Referenced script unknown" warnings.
    /// 5. Add every imported template to the existing
    ///    <c>RoomTemplateCatalog.asset</c> and rebuild a 5-room demo level
    ///    that references them.
    ///
    /// Idempotent — re-running rebuilds the Valkur SOs in place. The Udemy
    /// .asset files stay around as the source-of-truth data we re-read on
    /// every run; deleting them from the imported folder will cause this
    /// importer to skip silently.
    /// </summary>
    public static class CatacombsImporter
    {
        private const string Root = "Assets/_Project/Resources/Dungeon/Catacombs";
        private const string SourceTemplatesDir = Root + "/RoomTemplates";
        private const string ValkurTemplatesDir = Root + "/Valkur";
        private const string PrefabsDir = Root + "/Prefabs";
        private const string SamplesDir = "Assets/_Project/Resources/Dungeon/Samples";

        // Udemy script GUIDs we strip from prefabs (they target classes that
        // don't exist in Valkur). Keeping them as missing references
        // generates warnings on every prefab load.
        private static readonly HashSet<string> UdemyScriptsToStrip = new HashSet<string>
        {
            "e6dbde3e998f1d049be1d57d8f539e67", // InstantiatedRoom
            "eee43d1cce0924c49848f833da77970f", // RoomLightingControl
        };

        [MenuItem("Valkur/Dungeon/Import Catacombs Theme")]
        public static void ImportCatacombs()
        {
            EnsureFolder(ValkurTemplatesDir);

            // 1) Resolve the existing Valkur RoomNodeTypeSOs we generated in
            //    the sample creator. The mapping is name-based.
            var typeListPath = $"{SamplesDir}/RoomNodeTypeList.asset";
            var typeList = AssetDatabase.LoadAssetAtPath<RoomNodeTypeListSO>(typeListPath);
            if (typeList == null)
            {
                Debug.LogError("[CatacombsImporter] RoomNodeTypeList.asset not found. " +
                               "Run 'Valkur > Dungeon > Create Sample Assets' first.");
                return;
            }
            var entranceType = typeList.FindByName("Entrance");
            var corridorNSType = typeList.FindByName("CorridorNS");
            var corridorEWType = typeList.FindByName("CorridorEW");
            var roomType = typeList.FindByName("Room");
            var bossType = typeList.FindByName("BossRoom");

            // 2) Process every Udemy .asset in the source dir.
            var sourceFiles = Directory.GetFiles(SourceTemplatesDir, "Room_*.asset");
            var templatesByName = new Dictionary<string, RoomTemplateSO>();

            foreach (var assetPath in sourceFiles)
            {
                var name = Path.GetFileNameWithoutExtension(assetPath); // Room_BossRoom_Catacombs_1
                var parsed = ParseUdemyRoomAsset(assetPath);
                if (parsed == null) continue;

                var nodeType = ResolveNodeType(name, entranceType, corridorNSType,
                    corridorEWType, roomType, bossType);
                if (nodeType == null)
                {
                    Debug.LogWarning($"[CatacombsImporter] Could not resolve node type for '{name}', skipping.");
                    continue;
                }

                var prefabAsset = ResolvePrefabFromGuid(parsed.PrefabGuid);
                if (prefabAsset == null)
                {
                    Debug.LogWarning($"[CatacombsImporter] Prefab not found for '{name}' (guid {parsed.PrefabGuid}), skipping.");
                    continue;
                }

                // Build / update the Valkur RoomTemplateSO.
                var valkurPath = $"{ValkurTemplatesDir}/{name}.asset";
                var valkurSo = AssetDatabase.LoadAssetAtPath<RoomTemplateSO>(valkurPath);
                if (valkurSo == null)
                {
                    valkurSo = ScriptableObject.CreateInstance<RoomTemplateSO>();
                    AssetDatabase.CreateAsset(valkurSo, valkurPath);
                    valkurSo.TestRegenerateGuid();
                }
                valkurSo.roomNodeType = nodeType;
                valkurSo.lowerBounds = parsed.LowerBounds;
                valkurSo.upperBounds = parsed.UpperBounds;
                valkurSo.prefab = prefabAsset;
                valkurSo.doorwayList.Clear();
                foreach (var d in parsed.Doorways) valkurSo.doorwayList.Add(d);
                valkurSo.spawnPositionArray = parsed.SpawnPositions;
                EditorUtility.SetDirty(valkurSo);
                templatesByName[name] = valkurSo;
            }

            // 3) Strip missing scripts from all imported prefabs.
            int strippedFiles = StripMissingScriptsFromPrefabs();

            // 4) Update RoomTemplateCatalog with all imported templates.
            var catalogPath = $"{SamplesDir}/RoomTemplateCatalog.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<RoomTemplateCatalog>(catalogPath);
            if (catalog != null)
            {
                foreach (var so in templatesByName.Values) catalog.UpsertTemplate(so);
                EditorUtility.SetDirty(catalog);
            }

            // 5) Rebuild DungeonLevel_Demo to reference Catacombs templates.
            var levelPath = $"{SamplesDir}/DungeonLevel_Demo.asset";
            var level = AssetDatabase.LoadAssetAtPath<DungeonLevelSO>(levelPath);
            if (level != null)
            {
                level.levelName = "Catacombs Demo";
                level.roomTemplateList.Clear();
                foreach (var so in templatesByName.Values) level.roomTemplateList.Add(so);
                EditorUtility.SetDirty(level);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[CatacombsImporter] Imported {templatesByName.Count} Catacombs templates. " +
                      $"Stripped scripts from {strippedFiles} prefab files. " +
                      $"DungeonLevel_Demo now references the Catacombs theme.");
        }

        // ─────────────────────────────────────────────────────────────────
        // Udemy .asset YAML parser. Pulls just the bounds + doorways +
        // prefab GUID; the rest of the SO (music, enemies-by-level) is
        // dropped because Valkur uses different reference shapes.
        // ─────────────────────────────────────────────────────────────────

        private sealed class ParsedUdemyTemplate
        {
            public Vector2Int LowerBounds;
            public Vector2Int UpperBounds;
            public string PrefabGuid;
            public List<Doorway> Doorways = new List<Doorway>();
            public Vector2Int[] SpawnPositions = System.Array.Empty<Vector2Int>();
        }

        private static ParsedUdemyTemplate ParseUdemyRoomAsset(string assetPath)
        {
            string yaml;
            try { yaml = File.ReadAllText(assetPath); }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[CatacombsImporter] Could not read '{assetPath}': {ex.Message}");
                return null;
            }

            var result = new ParsedUdemyTemplate();

            // prefab: {fileID: 4327421925113694216, guid: 9fb6f8f004f241940b9e1447a0f4570a, type: 3}
            var prefabMatch = Regex.Match(yaml, @"^\s*prefab:\s*\{[^}]*guid:\s*([a-f0-9]{32})", RegexOptions.Multiline);
            if (prefabMatch.Success) result.PrefabGuid = prefabMatch.Groups[1].Value;

            // lowerBounds: {x: -24, y: -2}
            var lbMatch = Regex.Match(yaml, @"^\s*lowerBounds:\s*\{x:\s*(-?\d+),\s*y:\s*(-?\d+)\}", RegexOptions.Multiline);
            if (lbMatch.Success)
            {
                result.LowerBounds = new Vector2Int(
                    int.Parse(lbMatch.Groups[1].Value),
                    int.Parse(lbMatch.Groups[2].Value));
            }

            var ubMatch = Regex.Match(yaml, @"^\s*upperBounds:\s*\{x:\s*(-?\d+),\s*y:\s*(-?\d+)\}", RegexOptions.Multiline);
            if (ubMatch.Success)
            {
                result.UpperBounds = new Vector2Int(
                    int.Parse(ubMatch.Groups[1].Value),
                    int.Parse(ubMatch.Groups[2].Value));
            }

            // doorwayList entries — each starts with "- position:" then
            // orientation, doorPrefab, and the start/copy width/height.
            // We greedily walk the file with a multi-line regex.
            var doorwayPattern = new Regex(
                @"-\s*position:\s*\{x:\s*(-?\d+),\s*y:\s*(-?\d+)\}\s*\n" +
                @"\s*orientation:\s*(\d+)\s*\n" +
                @"\s*doorPrefab:[^\n]*\n" +
                @"\s*doorwayStartCopyPosition:\s*\{x:\s*(-?\d+),\s*y:\s*(-?\d+)\}\s*\n" +
                @"\s*doorwayCopyTileWidth:\s*(\d+)\s*\n" +
                @"\s*doorwayCopyTileHeight:\s*(\d+)",
                RegexOptions.Multiline);
            foreach (Match m in doorwayPattern.Matches(yaml))
            {
                result.Doorways.Add(new Doorway
                {
                    position = new Vector2Int(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)),
                    orientation = (Orientation)int.Parse(m.Groups[3].Value),
                    doorwayStartCopyPosition = new Vector2Int(int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value)),
                    doorwayCopyTileWidth = int.Parse(m.Groups[6].Value),
                    doorwayCopyTileHeight = int.Parse(m.Groups[7].Value),
                });
            }

            // spawnPositionArray — parse the "- {x:_, y:_}" entries.
            var spawnSection = Regex.Match(yaml,
                @"spawnPositionArray:\s*\n((?:\s*-\s*\{x:\s*-?\d+,\s*y:\s*-?\d+\}\s*\n?)+)",
                RegexOptions.Multiline);
            if (spawnSection.Success)
            {
                var positions = new List<Vector2Int>();
                foreach (Match m in Regex.Matches(spawnSection.Groups[1].Value,
                    @"\{x:\s*(-?\d+),\s*y:\s*(-?\d+)\}"))
                {
                    positions.Add(new Vector2Int(
                        int.Parse(m.Groups[1].Value),
                        int.Parse(m.Groups[2].Value)));
                }
                result.SpawnPositions = positions.ToArray();
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────
        // Lookups + helpers.
        // ─────────────────────────────────────────────────────────────────

        private static RoomNodeTypeSO ResolveNodeType(string templateName,
            RoomNodeTypeSO entrance, RoomNodeTypeSO corridorNS, RoomNodeTypeSO corridorEW,
            RoomNodeTypeSO room, RoomNodeTypeSO boss)
        {
            // Names follow Room_<Kind>_Catacombs_<n>
            if (templateName.Contains("BossRoom")) return boss;
            if (templateName.Contains("Entrance")) return entrance;
            if (templateName.Contains("CorridorNS")) return corridorNS;
            if (templateName.Contains("CorridorEW")) return corridorEW;
            // SmallRoom / MediumRoom / LargeRoom / ChestRoom all map to plain "Room".
            return room;
        }

        private static GameObject ResolvePrefabFromGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static int StripMissingScriptsFromPrefabs()
        {
            int filesChanged = 0;
            var prefabFiles = Directory.GetFiles(PrefabsDir, "*.prefab");
            // Build a regex that matches any MonoBehaviour block whose script
            // GUID is in our strip list. We replace the GUID with all-zeros
            // so Unity treats the component as a benign "missing reference"
            // without spamming the console for a known-stripped class.
            foreach (var path in prefabFiles)
            {
                var content = File.ReadAllText(path);
                var modified = content;
                foreach (var guid in UdemyScriptsToStrip)
                {
                    var pattern = $@"m_Script:\s*\{{fileID:\s*\d+,\s*guid:\s*{guid},\s*type:\s*3\}}";
                    modified = Regex.Replace(modified, pattern,
                        "m_Script: {fileID: 0}");
                }
                if (modified != content)
                {
                    File.WriteAllText(path, modified);
                    filesChanged++;
                }
            }
            return filesChanged;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            var current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
