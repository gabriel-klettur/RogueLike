using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor.Spells
{
    /// <summary>
    /// Walks every <see cref="SpellDefinition"/> asset and:
    ///   1. Auto-assigns <c>iconSprite</c> by matching <c>spellKey</c> against
    ///      PNG filenames under <c>Assets/_Project/Art/UI/spells/</c>.
    ///   2. Clears <c>sprite</c> if it points to a PNG under that same folder
    ///      (rescue: <c>sprite</c> drives the in-world projectile / area visual,
    ///      so polluting it with a HUD icon makes the projectile fly the icon
    ///      around instead of letting <c>FireballVisual</c> / similar render).
    /// </summary>
    public static class SpellIconAutoAssigner
    {
        private const string ICON_ROOT = "Assets/_Project/Art/UI/spells";
        private const string SPELLS_ROOT = "Assets/_Project/Data/Catalogs/Spells";

        /// <summary>
        /// Spells that don't ship their own HUD PNG and visually re-use another
        /// spell's icon. Hostile slash variants share the player slash icon;
        /// hostile dash shares the player dash icon. Any entry here is only
        /// applied when the spell does NOT have an icon PNG of its own — drop
        /// a <c>&lt;spellKey&gt;.png</c> under <c>Art/UI/spells/</c> at any
        /// point to take over from the alias automatically.
        /// </summary>
        private static readonly Dictionary<string, string> ICON_ALIASES =
            new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            { "hostile_slash",        "slash" },
            { "hostile_slash_red",    "slash" },
            { "hostile_slash_cyan",   "slash" },
            { "hostile_slash_dark",   "slash" },
            { "hostile_slash_purple", "slash" },
            { "hostile_slash_gray",   "slash" },
            { "hostile_slash_giant",  "slash" },
            { "boss_barbol_slash",    "slash" },
            { "hostile_dash",         "dash"  },
        };

        [MenuItem("Valkur/Spells/Assign Icons (Dry Run)")]
        private static void AssignDryRun() => Run(applyChanges: false);

        [MenuItem("Valkur/Spells/Assign Icons")]
        private static void AssignAndSave() => Run(applyChanges: true);

        private static void Run(bool applyChanges)
        {
            var spritePaths = BuildSpriteLookup();
            var report = new Report();

            string[] spellGuids = AssetDatabase.FindAssets(
                "t:SpellDefinition", new[] { SPELLS_ROOT });

            foreach (string guid in spellGuids)
            {
                string defPath = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<SpellDefinition>(defPath);
                if (def == null) continue;

                string key = string.IsNullOrEmpty(def.spellKey) ? def.name : def.spellKey;

                ClearHudIconFromInWorldSpriteField(def, key, applyChanges, report);
                AssignHudIcon(def, key, spritePaths, applyChanges, report);
            }

            if (applyChanges && (report.NewlyAssigned.Count > 0
                                 || report.Reassigned.Count > 0
                                 || report.SpriteFieldCleared.Count > 0))
            {
                AssetDatabase.SaveAssets();
            }

            LogSummary(report, applyChanges);
        }

        private static void ClearHudIconFromInWorldSpriteField(
            SpellDefinition def, string key, bool applyChanges, Report report)
        {
            if (def.sprite == null) return;
            string spritePath = AssetDatabase.GetAssetPath(def.sprite);
            if (string.IsNullOrEmpty(spritePath)) return;
            if (!spritePath.Replace('\\', '/').StartsWith(ICON_ROOT)) return;

            report.SpriteFieldCleared.Add($"{key} (was {spritePath})");
            if (applyChanges)
            {
                Undo.RecordObject(def, "Clear HUD icon from Spell sprite field");
                def.sprite = null;
                EditorUtility.SetDirty(def);
            }
        }

        private static void AssignHudIcon(
            SpellDefinition def,
            string key,
            Dictionary<string, string> spritePaths,
            bool applyChanges,
            Report report)
        {
            // Direct match wins; aliases are the fallback (so a future spell
            // dropping a `<key>.png` immediately takes over without code change).
            string lookupKey = key;
            if (!spritePaths.ContainsKey(lookupKey)
                && ICON_ALIASES.TryGetValue(key, out string aliasKey))
            {
                lookupKey = aliasKey;
            }

            if (!spritePaths.TryGetValue(lookupKey, out string iconPath))
            {
                report.MissingIcon.Add(key);
                return;
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
            if (sprite == null)
            {
                Debug.LogWarning(
                    $"[SpellIconAutoAssigner] Failed to load Sprite at '{iconPath}' " +
                    $"for spell '{key}'. Check Texture Type=Sprite (2D and UI).");
                report.LoadFailed.Add(key);
                return;
            }

            if (def.iconSprite == sprite)
            {
                report.AlreadyOk.Add(key);
                return;
            }

            if (def.iconSprite != null)
                report.Reassigned.Add(
                    $"{key}: {AssetDatabase.GetAssetPath(def.iconSprite)} -> {iconPath}");
            else
                report.NewlyAssigned.Add($"{key} -> {iconPath}");

            if (applyChanges)
            {
                Undo.RecordObject(def, "Assign Spell HUD Icon");
                def.iconSprite = sprite;
                EditorUtility.SetDirty(def);
            }
        }

        private static Dictionary<string, string> BuildSpriteLookup()
        {
            var map = new Dictionary<string, string>(System.StringComparer.Ordinal);
            string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { ICON_ROOT });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string nameNoExt = Path.GetFileNameWithoutExtension(path);
                map[nameNoExt] = path;
            }
            return map;
        }

        private sealed class Report
        {
            public readonly List<string> NewlyAssigned = new List<string>();
            public readonly List<string> Reassigned = new List<string>();
            public readonly List<string> AlreadyOk = new List<string>();
            public readonly List<string> MissingIcon = new List<string>();
            public readonly List<string> LoadFailed = new List<string>();
            public readonly List<string> SpriteFieldCleared = new List<string>();
        }

        private static void LogSummary(Report r, bool applied)
        {
            string mode = applied ? "APPLIED" : "DRY RUN";
            string summary =
                $"[SpellIconAutoAssigner] {mode} | iconSprite: " +
                $"new={r.NewlyAssigned.Count}, " +
                $"reassigned={r.Reassigned.Count}, " +
                $"alreadyOk={r.AlreadyOk.Count}, " +
                $"missing={r.MissingIcon.Count}, " +
                $"loadFailed={r.LoadFailed.Count} | " +
                $"sprite-field-cleared={r.SpriteFieldCleared.Count}";

            if (r.NewlyAssigned.Count > 0)
                summary += "\n  + iconSprite assigned:\n      " + string.Join("\n      ", r.NewlyAssigned);
            if (r.Reassigned.Count > 0)
                summary += "\n  ~ iconSprite reassigned:\n      " + string.Join("\n      ", r.Reassigned);
            if (r.SpriteFieldCleared.Count > 0)
                summary += "\n  - cleared HUD icon from sprite field:\n      " +
                           string.Join("\n      ", r.SpriteFieldCleared);
            if (r.MissingIcon.Count > 0)
                summary += "\n  ! no icon PNG found for: " + string.Join(", ", r.MissingIcon);
            if (r.LoadFailed.Count > 0)
                summary += "\n  ! sprite load failed for: " + string.Join(", ", r.LoadFailed);

            Debug.Log(summary);
        }
    }
}
