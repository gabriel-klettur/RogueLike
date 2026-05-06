#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor.Buildings
{
    /// <summary>
    /// One-shot maintenance tool that repairs <see cref="BuildingTemplateData.originalScale"/>
    /// for templates whose value drifted away from the actual PNG dimensions during the
    /// Python → Unity migration.
    ///
    /// Two distinct drift cases are repaired:
    ///   1. <c>originalScale = (0, 0)</c> — the field was missing from the source Python
    ///      data and never recomputed. Affects ~200 templates (gardens, portals,
    ///      forest_decoration, totems, flowers, etc.). Without backfill they fall through
    ///      to the runtime fallback in <c>BuildingObject.Apply</c> but the catalog still
    ///      reports zero, which confuses the Buildings Editor preview.
    ///   2. <c>originalScale aspect != PNG aspect</c> — a few templates have non-zero
    ///      values whose aspect doesn't match the PNG (e.g. <c>castle_2</c> says 3072×2048
    ///      but the PNG is 1024×1024 because the asset was re-exported smaller during
    ///      migration). The runtime now fits-not-stretches in this case, but the catalog
    ///      data is still misleading. We rewrite <c>originalScale</c> = PNG dims so the
    ///      data is the single source of truth.
    ///
    /// Always confirms before writing. Lists every change in the Console.
    /// </summary>
    public static class BuildingTemplateOriginalScaleBackfill
    {
        private const string MENU_PATH = "Valkur/Buildings/Backfill Original Scale (from PNG)";

        [MenuItem(MENU_PATH)]
        public static void Run()
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(BuildingTemplateData));
            var fillZero  = new List<(BuildingTemplateData tpl, Vector2Int oldVal, Vector2Int newVal)>();
            var fixDrift  = new List<(BuildingTemplateData tpl, Vector2Int oldVal, Vector2Int newVal)>();
            var noSprite  = new List<BuildingTemplateData>();
            int alreadyClean = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var tpl = AssetDatabase.LoadAssetAtPath<BuildingTemplateData>(path);
                if (tpl == null || string.IsNullOrEmpty(tpl.assetPath)) continue;

                Sprite sprite = Resources.Load<Sprite>(tpl.assetPath);
                if (sprite == null)
                {
                    noSprite.Add(tpl);
                    continue;
                }

                int pngW = Mathf.RoundToInt(sprite.rect.width);
                int pngH = Mathf.RoundToInt(sprite.rect.height);
                if (pngW <= 0 || pngH <= 0)
                {
                    noSprite.Add(tpl);
                    continue;
                }

                Vector2Int oldVal = tpl.originalScale;
                if (oldVal.x <= 0 || oldVal.y <= 0)
                {
                    fillZero.Add((tpl, oldVal, new Vector2Int(pngW, pngH)));
                }
                else if (Mathf.Abs((float)oldVal.x / oldVal.y - (float)pngW / pngH) > 0.01f)
                {
                    fixDrift.Add((tpl, oldVal, new Vector2Int(pngW, pngH)));
                }
                else
                {
                    alreadyClean++;
                }
            }

            int totalChanges = fillZero.Count + fixDrift.Count;
            if (totalChanges == 0)
            {
                EditorUtility.DisplayDialog(
                    "Backfill Original Scale",
                    $"All {alreadyClean} templates already have a valid originalScale that matches their PNG.\n\n" +
                    $"({noSprite.Count} skipped — no sprite at Resources/<assetPath>.)",
                    "OK");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Found {totalChanges} templates that need an originalScale fix:");
            sb.AppendLine();
            sb.AppendLine($"  • {fillZero.Count}  templates with originalScale = (0,0)  → backfill from PNG");
            sb.AppendLine($"  • {fixDrift.Count}  templates with aspect drift            → reset to PNG dims");
            sb.AppendLine($"  • {alreadyClean}    templates already clean (no change)");
            if (noSprite.Count > 0)
                sb.AppendLine($"  • {noSprite.Count}   templates skipped (sprite not found)");
            sb.AppendLine();
            sb.AppendLine("Apply changes?");

            bool ok = EditorUtility.DisplayDialog(
                "Backfill Original Scale",
                sb.ToString(),
                "Yes, fix data",
                "Cancel");
            if (!ok) return;

            int written = 0;
            foreach (var entry in fillZero)
            {
                Apply(entry.tpl, entry.newVal);
                Debug.Log($"[BackfillOriginalScale] FILL  Tpl#{entry.tpl.templateId} '{entry.tpl.name}': " +
                          $"{entry.oldVal} → {entry.newVal}", entry.tpl);
                written++;
            }
            foreach (var entry in fixDrift)
            {
                Apply(entry.tpl, entry.newVal);
                Debug.Log($"[BackfillOriginalScale] DRIFT Tpl#{entry.tpl.templateId} '{entry.tpl.name}': " +
                          $"{entry.oldVal} → {entry.newVal}", entry.tpl);
                written++;
            }
            foreach (var tpl in noSprite)
            {
                Debug.LogWarning($"[BackfillOriginalScale] SKIP  Tpl#{tpl.templateId} '{tpl.name}': " +
                                 $"sprite not found at Resources/{tpl.assetPath}", tpl);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "Backfill Original Scale — Done",
                $"Wrote {written} template(s).\n\n" +
                $"Filled (0,0) defaults: {fillZero.Count}\n" +
                $"Fixed aspect drift:    {fixDrift.Count}\n" +
                $"Skipped (no sprite):   {noSprite.Count}\n" +
                $"Already clean:         {alreadyClean}",
                "OK");
        }

        private static void Apply(BuildingTemplateData tpl, Vector2Int value)
        {
            Undo.RecordObject(tpl, "Backfill Building originalScale");
            tpl.originalScale = value;
            EditorUtility.SetDirty(tpl);
        }
    }
}
#endif
