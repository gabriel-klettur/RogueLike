#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor.Buildings
{
    /// <summary>
    /// Writes the hand-drawn trunk boxes from <c>tools/atlas/generated/tree_trunks.json</c> onto
    /// every <see cref="BuildingTemplateData"/> whose <c>assetPath</c> they name.
    ///
    /// <para><b>Why a manifest and not a measurement.</b> A trunk is not an alpha question: it is
    /// where bark stops and branches start, where roots stop and ground starts, and a crown of
    /// leaves hangs over both. An automatic proposal was tried and got the palms, the swamp trees
    /// and every tree on a rock wrong. The 428 boxes were drawn one sprite at a time and reviewed
    /// on contact sheets, so the manifest is the source and this tool only copies it.</para>
    ///
    /// <para>Keyed by SPRITE, not by template id: 553 tree templates share 428 sprites and every
    /// template of one sprite gets the same box. Re-runnable; writes only the one field and saves
    /// only what it dirtied. Never <c>Undo.RecordObject</c> — see the building-template note in
    /// CLAUDE.md.</para>
    /// </summary>
    public static class TreeTrunkBoxApplier
    {
        public const string ManifestPath = "../../../tools/atlas/generated/tree_trunks.json";
        private const string MenuPath = "Valkur/Buildings/Apply Tree Trunk Boxes";

        [Serializable]
        public sealed class Entry
        {
            public string assetPath;
            public float x0, y0, x1, y1;

            public Rect ToRect() => Rect.MinMaxRect(x0, y0, x1, y1);
        }

        [Serializable]
        public sealed class Manifest
        {
            public string note;
            public List<Entry> trunks = new List<Entry>();
        }

        /// <summary>The manifest parsed, or null when the file is missing.</summary>
        public static Manifest Load()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, ManifestPath));
            if (!File.Exists(path)) return null;
            return JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
        }

        /// <summary>The manifest as assetPath -> normalized trunk rect.</summary>
        public static Dictionary<string, Rect> LoadByAssetPath()
        {
            var map = new Dictionary<string, Rect>(StringComparer.Ordinal);
            var manifest = Load();
            if (manifest?.trunks == null) return map;
            foreach (var e in manifest.trunks)
                if (e != null && !string.IsNullOrEmpty(e.assetPath)) map[e.assetPath] = e.ToRect();
            return map;
        }

        [MenuItem(MenuPath)]
        public static void Run()
        {
            var boxes = LoadByAssetPath();
            if (boxes.Count == 0)
            {
                Debug.LogError($"[TreeTrunkBoxApplier] No trunk boxes read from {ManifestPath}.");
                return;
            }

            var changed = new List<BuildingTemplateData>();
            var used = new HashSet<string>(StringComparer.Ordinal);
            int templates = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(BuildingTemplateData)))
            {
                var tpl = AssetDatabase.LoadAssetAtPath<BuildingTemplateData>(AssetDatabase.GUIDToAssetPath(guid));
                if (tpl == null || string.IsNullOrEmpty(tpl.assetPath)) continue;
                if (!boxes.TryGetValue(tpl.assetPath, out var box)) continue;

                templates++;
                used.Add(tpl.assetPath);
                if (Approximately(tpl.trunkNormalized, box)) continue;

                tpl.trunkNormalized = box;
                EditorUtility.SetDirty(tpl);
                changed.Add(tpl);
            }

            foreach (var tpl in changed) AssetDatabase.SaveAssetIfDirty(tpl);

            var sb = new StringBuilder();
            sb.AppendLine($"[TreeTrunkBoxApplier] {boxes.Count} boxes, {templates} templates matched, " +
                          $"{changed.Count} written.");
            foreach (var key in boxes.Keys)
                if (!used.Contains(key)) sb.AppendLine($"  no template for sprite {key}");
            Debug.Log(sb.ToString());
        }

        private static bool Approximately(Rect a, Rect b) =>
            Mathf.Abs(a.xMin - b.xMin) < 0.0005f && Mathf.Abs(a.yMin - b.yMin) < 0.0005f &&
            Mathf.Abs(a.xMax - b.xMax) < 0.0005f && Mathf.Abs(a.yMax - b.yMax) < 0.0005f;
    }
}
#endif
