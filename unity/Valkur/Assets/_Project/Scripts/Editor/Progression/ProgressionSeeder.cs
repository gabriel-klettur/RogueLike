using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.EditorTools.Progression
{
    /// <summary>
    /// Generates the progression content the runtime needs: the XP curve, the per-level
    /// stat curve, one talent tree per class, the schools of the grimoire and the
    /// <see cref="ProgressionCatalog"/> that ties them together.
    ///
    /// **It creates, it does not overwrite.** An asset that already exists on disk is left
    /// exactly as the designer last edited it, and only missing ones are written — the
    /// "creation defaults, authored value wins" contract <c>TilesetRulesetImporter</c> and
    /// the persona importer already use. Re-running after adding a class or a spell fills
    /// the gap and touches nothing else. <see cref="RegenerateEverything"/> is the escape
    /// hatch and says so in a confirmation dialog.
    ///
    /// It deliberately does NOT use <c>Undo.RecordObject</c>. Recording 100+ generated
    /// assets onto the global editor undo stack is what once reverted 193 building
    /// templates to their empty creation state the first time the EditMode suite popped
    /// that stack — see CLAUDE.md. <c>EditorUtility.SetDirty</c> alone is correct for data
    /// an operator re-runs rather than undoes.
    /// </summary>
    public static class ProgressionSeeder
    {
        private const string ProgressionRoot = "Assets/_Project/Data/Progression";
        private const string SkillTreeRoot   = ProgressionRoot + "/SkillTrees";
        private const string SpellTreeRoot   = ProgressionRoot + "/SpellTrees";
        private const string CatalogFolder   = "Assets/_Project/Resources/Progression";
        private const string CatalogPath     = CatalogFolder + "/ProgressionCatalog.asset";
        private const string SpellFolder     = "Assets/_Project/Data/Catalogs/Spells";

        [MenuItem("Valkur/Progression/Seed Progression Content")]
        public static void Seed() => Run(overwrite: false);

        [MenuItem("Valkur/Progression/Regenerate Progression Content (Overwrite)")]
        public static void RegenerateEverything()
        {
            bool ok = EditorUtility.DisplayDialog(
                "Regenerate progression content",
                "This DELETES and rewrites every generated skill tree, spell tree and curve, " +
                "discarding any tuning done in the Inspector.\n\n" +
                "Player saves are unaffected as long as node ids do not change — but a node " +
                "whose id changes is silently un-learned on every existing save.\n\nContinue?",
                "Regenerate", "Cancel");
            if (!ok) return;

            AssetDatabase.DeleteAsset(SkillTreeRoot);
            AssetDatabase.DeleteAsset(SpellTreeRoot);
            Run(overwrite: true);
        }

        private static void Run(bool overwrite)
        {
            EnsureFolder(ProgressionRoot);
            EnsureFolder(SkillTreeRoot);
            EnsureFolder(SpellTreeRoot);
            EnsureFolder(CatalogFolder);

            var xpCurve = SeedXpCurve();
            var levelCurve = SeedLevelCurve();
            var skillTrees = SkillTreeSeeds.BuildAll(SkillTreeRoot);
            var spellTrees = SpellTreeSeeds.BuildAll(SpellTreeRoot, LoadPlayerSpells());

            var catalog = LoadOrCreate<ProgressionCatalog>(CatalogPath, out bool createdCatalog);
            if (createdCatalog || overwrite)
            {
                catalog.xpCurve = xpCurve;
                catalog.levelStatCurve = levelCurve;
                catalog.skillPointsPerLevel = 1;
                catalog.arcanePointsPerGrant = 1;
                catalog.arcanePointLevelInterval = 2;
                catalog.startingSkillPoints = 0;
                catalog.startingArcanePoints = 1;
                catalog.alwaysKnownSpellKeys = SpellTreeSeeds.InnateSpellKeys;
            }

            // The tree arrays are refreshed even on a non-overwrite run: they are the
            // catalog's INDEX, not tuning, and a tree that exists on disk but is missing
            // from the index is content nobody can reach — the exact failure mode this
            // whole layer was built to end.
            catalog.skillTrees = skillTrees.ToArray();
            catalog.spellTrees = spellTrees.ToArray();

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int spellNodes = 0;
            foreach (var t in spellTrees) spellNodes += t.Count;
            int skillNodes = 0;
            foreach (var t in skillTrees) skillNodes += t.Count;

            Debug.Log($"[ProgressionSeeder] Done. {skillTrees.Count} skill trees " +
                      $"({skillNodes} talents), {spellTrees.Count} schools ({spellNodes} spell " +
                      $"nodes), catalog at {CatalogPath}.");
        }

        private static XpCurveDefinition SeedXpCurve()
        {
            string path = ProgressionRoot + "/XpCurve.asset";
            var asset = LoadOrCreate<XpCurveDefinition>(path, out bool created);
            if (!created) return asset;

            asset.baseXp = 100;
            asset.exponent = 1.55f;
            // A cap, unlike the state the project shipped in. Without one IsAtLevelCap is
            // false forever and the level curve keeps granting hit points with no ceiling,
            // which makes every monster in the game irrelevant given enough time.
            asset.levelCap = 60;
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static LevelStatCurve SeedLevelCurve()
        {
            string path = ProgressionRoot + "/LevelStatCurve.asset";
            var asset = LoadOrCreate<LevelStatCurve>(path, out bool created);
            if (!created) return asset;

            asset.hpPerLevel = 8;
            asset.manaPerLevel = 4;
            // Half a point of melee damage per level. Fractional on purpose: the stat is
            // rounded once at the push, so it climbs 1 point every two levels instead of
            // doubling a level-1 character's swing the first time they level.
            asset.perLevelModifiers = new[]
            {
                StatModifier.Flat(StatKind.MeleeDamage, 0.5f),
                StatModifier.Flat(StatKind.Defense, 0.25f),
                StatModifier.Flat(StatKind.ManaRegen, 0.05f),
            };
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static List<SpellDefinition> LoadPlayerSpells()
        {
            var result = new List<SpellDefinition>();
            foreach (var guid in AssetDatabase.FindAssets("t:SpellDefinition", new[] { SpellFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<SpellDefinition>(path);
                if (def != null && !string.IsNullOrWhiteSpace(def.spellKey))
                    result.Add(def);
            }
            return result;
        }

        // ── Shared helpers, used by both seed tables ────────────────────────────

        internal static T LoadOrCreate<T>(string path, out bool created) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) { created = false; return existing; }

            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            created = true;
            return asset;
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) return;

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
