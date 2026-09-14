using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor.Players
{
    /// <summary>
    /// <c>Valkur &gt; Players &gt; Import Locomotion Cycles</c>: copies the measured cycles from
    /// <c>tools/atlas/generated/locomotion_cycles.json</c> into
    /// <c>Resources/Skills/LocomotionCycleCatalog.asset</c>.
    ///
    /// <para>The JSON is the record, reviewed on its contact sheets; the asset is what the runtime
    /// reads. The import REPLACES the list, because the measurement owns every field — there is no
    /// authored value on a cycle to preserve. No <c>Undo.RecordObject</c> and no global
    /// <c>SaveAssets</c>, for the reasons the building importer records.</para>
    /// </summary>
    public static class LocomotionCycleImporter
    {
        private const string JsonPath = "../../../tools/atlas/generated/locomotion_cycles.json";
        private const string AssetPath = "Assets/_Project/Resources/Skills/LocomotionCycleCatalog.asset";

        [System.Serializable]
        private sealed class Document
        {
            public List<LocomotionCycle> list = new List<LocomotionCycle>();
        }

        [MenuItem("Valkur/Players/Import Locomotion Cycles")]
        public static void ImportMenu() => Debug.Log(Import());

        public static string Import()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, JsonPath));
            if (!File.Exists(path)) return "[LocomotionCycleImporter] no JSON at " + path;

            var doc = JsonUtility.FromJson<Document>(File.ReadAllText(path));
            if (doc?.list == null || doc.list.Count == 0) return "[LocomotionCycleImporter] the JSON has no 'list'.";

            var catalog = AssetDatabase.LoadAssetAtPath<LocomotionCycleCatalog>(AssetPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<LocomotionCycleCatalog>();
                AssetDatabase.CreateAsset(catalog, AssetPath);
            }

            catalog.cycles = doc.list;
            catalog.InvalidateLookup();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
            LocomotionCycleCatalog.InvalidateCache();
            return $"[LocomotionCycleImporter] imported {doc.list.Count} cycles into {AssetPath}";
        }
    }
}
