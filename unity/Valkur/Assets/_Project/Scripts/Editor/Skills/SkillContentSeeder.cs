using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor.Skills
{
    /// <summary>
    /// Seeds the skill roster — every row of the skills table — and wires each crafting trade to
    /// the skill it trains. <c>Valkur &gt; Skills &gt; Seed Skill Content</c>.
    ///
    /// <para><b>WHAT IT OWNS AND WHAT IT DOES NOT.</b> It writes each skill's IDENTITY (key,
    /// category, order, accent) every run, and its prose (name, description) and its gain curve
    /// only when the asset is created — a designer's retune of the cooking curve must survive a
    /// re-run. Woodcutting's wood table, training nodes and tree profiles belong to
    /// <c>WoodcuttingContentSeeder</c>; this only files it in the table. Recipes' requirements come
    /// from the crafting manifest through <c>CraftingContentImporter</c>, which needs the skills
    /// this creates, so run this first.</para>
    ///
    /// <para><b>A SKILL WITH NO CONTENT IS STILL SEEDED.</b> Mining and fishing have no nodes and
    /// no recipes yet. They ship as assets anyway, because the skills table shows them LOCKED —
    /// "próximamente" is decided by what the game can actually train (see
    /// <c>SkillAvailability</c>), not by a flag somebody has to remember to clear.</para>
    ///
    /// <para>No <c>Undo.RecordObject</c> and no global <c>SaveAssets</c>, for the reasons
    /// <c>WoodcuttingContentSeeder</c> records. Idempotent.</para>
    /// </summary>
    public static class SkillContentSeeder
    {
        private const string SkillsDir = "Assets/_Project/Data/Catalogs/Skills";
        private const string CatalogPath = "Assets/_Project/Resources/Skills/SkillCatalog.asset";
        private const string RecipeCatalogPath = "Assets/_Project/Resources/Crafting/RecipeCatalog.asset";
        private const string ProfessionDir = "Assets/_Project/Data/Catalogs/Crafting/Professions";

        private struct Spec
        {
            public string Key, Name, Description, Profession;
            public SkillCategory Category;
            public int Order;
            public Color Accent;
        }

        private static readonly Spec[] Roster =
        {
            new Spec { Key = "woodcutting", Name = "Tala", Category = SkillCategory.Gathering, Order = 0,
                Accent = new Color(0.45f, 0.66f, 0.38f),
                Description = "Talar árboles. Más skill: golpes más eficaces, maderas mejores y más troncos al derribar." },
            new Spec { Key = "mining", Name = "Minería", Category = SkillCategory.Gathering, Order = 1,
                Accent = new Color(0.55f, 0.60f, 0.70f), Profession = "mining",
                Description = "Picar vetas de mineral y gemas." },
            new Spec { Key = "fishing", Name = "Pesca", Category = SkillCategory.Gathering, Order = 2,
                Accent = new Color(0.35f, 0.62f, 0.85f),
                Description = "Pescar en ríos, lagos y costas." },
            new Spec { Key = "cooking", Name = "Cocina", Category = SkillCategory.Crafting, Order = 0,
                Accent = new Color(0.93f, 0.62f, 0.28f), Profession = "cooking",
                Description = "Cocinar platos con ingredientes. Más skill: recetas más elaboradas." },
            new Spec { Key = "blacksmith", Name = "Herrería", Category = SkillCategory.Crafting, Order = 1,
                Accent = new Color(0.72f, 0.45f, 0.30f), Profession = "blacksmith",
                Description = "Forjar armas y armaduras en una fragua." },
            new Spec { Key = "crafting", Name = "Artesanía", Category = SkillCategory.Crafting, Order = 2,
                Accent = new Color(0.62f, 0.55f, 0.85f), Profession = "crafting",
                Description = "Fabricar objetos en un banco de trabajo." },
            new Spec { Key = "athletics", Name = "Carrera", Category = SkillCategory.Physical, Order = 0,
                Accent = new Color(0.86f, 0.78f, 0.36f),
                Description = "Correr. Caminar un par de pasos te lanza a la carrera; más skill: arrancas antes, corres más rápido, aguantas más y giras sin perder el impulso." },
        };

        [MenuItem("Valkur/Skills/Seed Skill Content")]
        public static void SeedMenu() => Debug.Log(Seed());

        public static string Seed()
        {
            var report = new StringBuilder("[SkillContentSeeder]\n");
            EnsureFolder(SkillsDir);
            EnsureFolder(Path.GetDirectoryName(CatalogPath)?.Replace('\\', '/'));

            var catalog = LoadOrCreate<SkillCatalog>(CatalogPath, out _);
            var recipes = AssetDatabase.LoadAssetAtPath<RecipeCatalog>(RecipeCatalogPath);

            foreach (var spec in Roster)
            {
                var skill = LoadOrCreate<SkillDefinition>($"{SkillsDir}/GS_{spec.Key}.asset", out bool created);
                skill.skillKey = spec.Key;
                skill.category = spec.Category;
                skill.sortOrder = spec.Order;
                skill.accentColor = spec.Accent;

                if (created || string.IsNullOrEmpty(skill.displayName))
                    skill.displayName = spec.Name;
                if (created || string.IsNullOrEmpty(skill.description))
                    skill.description = spec.Description;

                // A crafting skill is trained per CRAFT, which costs ingredients, not per blow. The
                // gathering curve (0.1 % a gain) would ask for thousands of dishes; half a percent
                // a gain puts 100 % at roughly five hundred crafts. Written on creation only.
                if (created && spec.Category == SkillCategory.Crafting)
                {
                    skill.gainBaseChance = 0.55f;
                    skill.gainTenths = 5;
                }

                // Running is rolled per distance run (LocomotionTuning.gainEveryRunDistance) and
                // capped per minute, so a roll is rarer than a blow: 0.2 % a gain at a 0.9 chance
                // puts 50 % at about half an hour of running and 100 % at about five hours.
                // WoodcuttingDataTests' sibling, AthleticsDataTests, simulates it.
                if (created && spec.Category == SkillCategory.Physical)
                {
                    skill.gainBaseChance = 0.9f;
                    skill.gainTenths = 2;
                    skill.milestonePercent = 10;
                }

                Save(skill);
                if (!catalog.skills.Contains(skill)) catalog.skills.Add(skill);
                report.Append("  ").Append(spec.Key).Append(created ? " (created)" : "").Append('\n');

                if (!string.IsNullOrEmpty(spec.Profession))
                    WireProfession(spec.Profession, skill, report);
            }

            catalog.skills.RemoveAll(s => s == null);
            Save(catalog);

            RetireLumberjack(recipes, report);
            return report.ToString();
        }

        private static void WireProfession(string key, SkillDefinition skill, StringBuilder report)
        {
            var profession = AssetDatabase.LoadAssetAtPath<ProfessionDefinition>($"{ProfessionDir}/{key}.asset");
            if (profession == null)
            {
                report.Append("    no profession asset '").Append(key).Append("'\n");
                return;
            }
            if (profession.skill == skill) return;
            profession.skill = skill;
            Save(profession);
            report.Append("    profession ").Append(key).Append(" -> ").Append(skill.skillKey).Append('\n');
        }

        /// <summary>
        /// Felling trees is the woodcutting skill; a separate lumberjack TRADE with no recipes and
        /// no progression of its own is the duplicate the skills table would otherwise show twice.
        /// Saves that carried its level are migrated to woodcutting by
        /// <c>LegacyProfessionMigration</c>, so nothing depends on the asset any more.
        /// </summary>
        private static void RetireLumberjack(RecipeCatalog recipes, StringBuilder report)
        {
            if (recipes != null)
            {
                int removed = recipes.RetireProfessionsNotIn(new[] { "cooking", "blacksmith", "mining", "crafting" });
                if (removed > 0)
                {
                    Save(recipes);
                    report.Append("  retired ").Append(removed).Append(" profession(s) from the recipe catalog\n");
                }
            }

            string path = $"{ProfessionDir}/lumberjack.asset";
            if (AssetDatabase.LoadAssetAtPath<ProfessionDefinition>(path) != null && AssetDatabase.DeleteAsset(path))
                report.Append("  deleted ").Append(path).Append('\n');
        }

        private static T LoadOrCreate<T>(string path, out bool created) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            created = existing == null;
            if (existing != null) return existing;
            var asset = ScriptableObject.CreateInstance<T>();
            asset.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void Save(Object asset)
        {
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
