using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Editor.Gathering
{
    /// <summary>
    /// <c>Valkur &gt; Gathering &gt; Seed Woodcutting Content</c>: the woodcutting skill, its wood
    /// table, one destruction profile per tree family, the wiring of every tree template to its
    /// family, and the names, rarities and prices of the 64 wood items.
    ///
    /// <para><b>THE TABLE BELOW IS THE BALANCE.</b> Everything a designer would retune — which
    /// wood unlocks when, how hard each family is, how much each log sells for — is a literal in
    /// this file, in one place, so a balance pass is one diff that can be read. The importer
    /// writes DERIVED data every run (the same rule the crafting importer uses for balance) and
    /// is idempotent: running it twice changes nothing the second time.</para>
    ///
    /// <para><b>No <c>Undo.RecordObject</c> and no global <c>SaveAssets</c>.</b> The first is the
    /// 193-reverted-templates trap; the second writes every dirty asset in the project, including
    /// other sessions' work. Each asset this touches is saved with <c>SaveAssetIfDirty</c>.</para>
    /// </summary>
    public static class WoodcuttingContentSeeder
    {
        private const string CatalogDir = "Assets/_Project/Data/Catalogs/Gathering";
        private const string DestructionDir = "Assets/_Project/Data/Catalogs/Destruction";
        private const string ItemsDir = "Assets/_Project/Data/Catalogs/Items/Material";
        private const string ResourcesDir = "Assets/_Project/Resources/Skills";
        private const string SkillsDir = "Assets/_Project/Data/Catalogs/Skills";
        private const string CommonProfilePath = DestructionDir + "/DP_tree_common.asset";

        // ── The wood table ───────────────────────────────────────────────────────

        private struct TierSpec
        {
            public string Key, Name, Tag, Description, Where;
            public float Min, Ramp, FadeOut, Weight;
            public ItemRarity Rarity;
            public int Price;
            public int[] Woods;
        }

        private static readonly TierSpec[] Tiers =
        {
            T("ramas",       "Ramas secas",        "",         0f,  0f, 30f, 100f, ItemRarity.Common,     1, "Ramas finas y secas. Arden rápido y valen poco.", 4, 8, 18, 21, 27, 31, 43, 52),
            T("lena",        "Leña",               "",         0f,  0f, 45f,  90f, ItemRarity.Common,     2, "Un haz de leña atado a mano.", 3, 7, 13, 20, 22, 26, 35),
            T("comun",       "Madera común",       "",         5f, 10f, 70f, 100f, ItemRarity.Common,     3, "Troncos rectos de madera corriente.", 1, 2, 5, 10, 33, 46, 48, 53, 61),
            T("abedul",      "Madera de abedul",   "",        18f, 12f, 85f,  60f, ItemRarity.Uncommon,   6, "Corteza blanca y veta clara. Buscada por los carpinteros.", 14, 25, 51),
            T("bambu",       "Bambú",              "tropical",20f, 10f,  0f,  70f, ItemRarity.Uncommon,   6, "Cañas huecas y resistentes de la selva.", 40),
            T("musgosa",     "Madera musgosa",     "moss",    25f, 12f,  0f,  45f, ItemRarity.Uncommon,   7, "Troncos húmedos cubiertos de musgo vivo.", 9, 34, 39, 44, 59),
            T("roble",       "Madera de roble",    "",        35f, 15f,  0f,  55f, ItemRarity.Uncommon,   9, "Madera densa y noble, de anillos apretados.", 17, 29, 38, 60),
            T("mangle",      "Madera de mangle",   "swamp",   35f, 15f,  0f,  60f, ItemRarity.Uncommon,   9, "Madera oscura que no se pudre en el agua.", 23, 30, 42),
            T("duramen",     "Madera de duramen",  "",        50f, 15f,  0f,  45f, ItemRarity.Rare,      15, "El corazón del tronco, recto y sin nudos.", 6, 11, 12, 16, 19, 24, 36, 41, 45, 54, 58, 63),
            T("glacial",     "Madera glacial",     "frost",   55f, 15f,  0f,  40f, ItemRarity.Rare,      20, "Fría al tacto incluso junto al fuego.", 47),
            T("carbonizada", "Madera carbonizada", "charred", 55f, 15f,  0f,  45f, ItemRarity.Rare,      18, "Ennegrecida por fuera y dura como la piedra.", 15, 32, 57, 64),
            T("secuoya",     "Secuoya roja",       "ancient", 65f, 15f,  0f,  35f, ItemRarity.Epic,      32, "Madera rojiza de un árbol más viejo que el reino.", 28, 37, 50),
            T("ascua",       "Madera de ascua",    "volcanic",75f, 15f,  0f,  25f, ItemRarity.Epic,      45, "Sigue ardiendo por dentro sin consumirse.", 49, 56),
            T("arcana",      "Madera arcana",      "arcane",  85f, 10f,  0f,  20f, ItemRarity.Legendary, 80, "Late con una luz azul que nadie ha sabido explicar.", 62),
            T("vacio",       "Madera del vacío",   "void",    85f, 10f,  0f,  20f, ItemRarity.Legendary, 80, "Absorbe la luz a su alrededor.", 55),
        };

        private static TierSpec T(string key, string name, string tag, float min, float ramp, float fadeOut,
            float weight, ItemRarity rarity, int price, string description, params int[] woods) =>
            new TierSpec
            {
                Key = key, Name = name, Tag = tag, Min = min, Ramp = ramp, FadeOut = fadeOut, Weight = weight,
                Rarity = rarity, Price = price, Description = description, Woods = woods,
                Where = WhereFor(tag),
            };

        private static string WhereFor(string tag)
        {
            switch (tag)
            {
                case "tropical": return "árboles tropicales";
                case "moss":     return "comunes, pantano, ancestrales";
                case "swamp":    return "árboles de pantano";
                case "frost":    return "árboles nevados";
                case "charred":  return "volcánicos o corruptos";
                case "ancient":  return "árboles ancestrales";
                case "volcanic": return "árboles volcánicos";
                case "arcane":   return "árboles encantados";
                case "void":     return "árboles corruptos";
                default:         return "";
            }
        }

        // ── The tree families ────────────────────────────────────────────────────

        private struct FamilySpec
        {
            public TreeFamily Family;
            public int Difficulty, Durability, WorkPerYield, FellBonus;
            public float RegrowSeconds;
            public string[] Tags;
            public Color Leaf, RemainsTint;
        }

        private static readonly FamilySpec[] Families =
        {
            F(TreeFamily.Small,      0,  25, 10, 0,  480f, new[] { "" },                   new Color(0.45f, 0.66f, 0.30f), Color.white),
            F(TreeFamily.Common,    15,  40, 10, 1,  900f, new[] { "moss" },               new Color(0.36f, 0.58f, 0.25f), Color.white),
            F(TreeFamily.Autumn,    20,  45, 10, 1,  900f, new string[0],                  new Color(0.86f, 0.46f, 0.18f), Color.white),
            F(TreeFamily.Tropical,  25,  45, 10, 1,  900f, new[] { "tropical" },           new Color(0.30f, 0.62f, 0.28f), Color.white),
            F(TreeFamily.Swamp,     35,  55, 11, 1, 1200f, new[] { "swamp", "moss" },      new Color(0.42f, 0.50f, 0.24f), new Color(0.85f, 0.88f, 0.80f)),
            F(TreeFamily.Winter,    45,  60, 12, 1, 1200f, new[] { "frost" },              new Color(0.88f, 0.92f, 0.96f), new Color(0.92f, 0.95f, 1.00f)),
            F(TreeFamily.Volcanic,  55,  70, 14, 1, 1500f, new[] { "volcanic", "charred" }, new Color(0.45f, 0.35f, 0.30f), new Color(0.45f, 0.40f, 0.38f)),
            F(TreeFamily.Ancient,   70, 140, 20, 2, 2400f, new[] { "ancient", "moss" },    new Color(0.33f, 0.52f, 0.22f), Color.white),
            F(TreeFamily.Enchanted, 80, 110, 18, 2, 1800f, new[] { "arcane" },             new Color(0.45f, 0.85f, 0.90f), new Color(0.85f, 0.95f, 1.00f)),
            F(TreeFamily.Corrupted, 75, 100, 17, 2, 1800f, new[] { "void", "charred" },    new Color(0.50f, 0.30f, 0.55f), new Color(0.55f, 0.45f, 0.60f)),
        };

        private static FamilySpec F(TreeFamily family, int difficulty, int durability, int workPerYield, int fellBonus,
            float regrow, string[] tags, Color leaf, Color remainsTint) =>
            new FamilySpec
            {
                Family = family, Difficulty = difficulty, Durability = durability, WorkPerYield = workPerYield,
                FellBonus = fellBonus, RegrowSeconds = regrow,
                Tags = tags.Where(t => !string.IsNullOrEmpty(t)).ToArray(), Leaf = leaf, RemainsTint = remainsTint,
            };

        /// <summary>Exposed so a test can read the table without running the seeder.</summary>
        public static int FamilyDifficulty(TreeFamily family)
        {
            foreach (var f in Families) if (f.Family == family) return f.Difficulty;
            return -1;
        }

        // ── Menu ─────────────────────────────────────────────────────────────────

        [MenuItem("Valkur/Gathering/Seed Woodcutting Content")]
        public static void SeedMenu() => Debug.Log(Seed());

        public static string Seed()
        {
            var report = new StringBuilder("[WoodcuttingContentSeeder]\n");
            ValidateTable();

            EnsureFolder(CatalogDir);
            EnsureFolder(ResourcesDir);
            EnsureFolder(SkillsDir);

            var common = AssetDatabase.LoadAssetAtPath<DestructionProfile>(CommonProfilePath);
            if (common == null) return report.Append("ABORT: no ").Append(CommonProfilePath).ToString();

            var items = LoadWoods(report);
            if (items == null) return report.ToString();

            var table = LoadOrCreate<GatheringYieldTable>(CatalogDir + "/GYT_woodcutting.asset");
            WriteTable(table, items);
            Save(table);

            var skill = LoadOrCreate<SkillDefinition>(SkillsDir + "/GS_woodcutting.asset");
            skill.skillKey = "woodcutting";
            skill.displayName = "Tala";
            skill.yieldTable = table;
            Save(skill);

            var catalog = LoadOrCreate<SkillCatalog>(ResourcesDir + "/SkillCatalog.asset");
            if (!catalog.skills.Contains(skill)) catalog.skills.Add(skill);
            catalog.skills.RemoveAll(s => s == null);
            Save(catalog);

            WriteItems(items, report);

            var profiles = new Dictionary<TreeFamily, DestructionProfile>();
            foreach (var spec in Families)
            {
                string path = DestructionDir + "/" + TreeFamilyClassifier.ProfileName(spec.Family) + ".asset";
                var profile = spec.Family == TreeFamily.Common ? common : LoadOrCreate<DestructionProfile>(path);
                WriteProfile(profile, common, spec, skill);
                Save(profile);
                profiles[spec.Family] = profile;
            }

            skill.trainingNodes = Families.Select(f => profiles[f.Family]).ToList();
            Save(skill);

            int wired = WireTemplates(profiles, report);
            report.Append("  templates wired: ").Append(wired).Append('\n');
            return report.ToString();
        }

        private static void ValidateTable()
        {
            var seen = new HashSet<int>();
            foreach (var t in Tiers)
                foreach (int w in t.Woods)
                    if (!seen.Add(w)) throw new InvalidOperationException($"wood_{w:00} is in two tiers");
            for (int i = 1; i <= 64; i++)
                if (!seen.Contains(i)) throw new InvalidOperationException($"wood_{i:00} is in no tier");
        }

        private static Dictionary<int, ItemDefinition> LoadWoods(StringBuilder report)
        {
            var map = new Dictionary<int, ItemDefinition>();
            for (int i = 1; i <= 64; i++)
            {
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemsDir}/wood_{i:00}.asset");
                if (item == null) { report.Append("ABORT: missing wood_").Append(i.ToString("00")).Append('\n'); return null; }
                map[i] = item;
            }
            return map;
        }

        private static void WriteTable(GatheringYieldTable table, Dictionary<int, ItemDefinition> items)
        {
            table.tiers = Tiers.Select(spec => new GatheringYieldTable.Tier
            {
                key = spec.Key,
                displayName = spec.Name,
                minSkill = spec.Min,
                rampSkill = spec.Ramp,
                fadeOutSkill = spec.FadeOut,
                fadedWeightFactor = 0.2f,
                weight = spec.Weight,
                requiredTag = spec.Tag,
                whereHint = spec.Where,
                items = spec.Woods.Select(w => items[w]).ToArray(),
            }).ToList();
        }

        private static void WriteItems(Dictionary<int, ItemDefinition> items, StringBuilder report)
        {
            int changed = 0;
            foreach (var spec in Tiers)
            {
                foreach (int w in spec.Woods)
                {
                    var item = items[w];
                    bool dirty = item.displayName != spec.Name || item.rarity != spec.Rarity ||
                                 item.buyPrice != spec.Price || item.sellPrice != Mathf.Max(1, spec.Price / 2) ||
                                 item.description != spec.Description;
                    if (!dirty) continue;

                    item.displayName = spec.Name;
                    item.rarity = spec.Rarity;
                    item.buyPrice = spec.Price;
                    item.sellPrice = Mathf.Max(1, spec.Price / 2);
                    item.description = spec.Description;
                    Save(item);
                    changed++;
                }
            }
            report.Append("  wood items updated: ").Append(changed).Append('\n');
        }

        private static void WriteProfile(DestructionProfile p, DestructionProfile common, FamilySpec spec,
            SkillDefinition skill)
        {
            p.material = MaterialClass.Wood;
            p.durability = spec.Durability;
            p.requiredToolTier = 1;
            p.chipDamageFraction = common.chipDamageFraction;
            p.kind = DestructionKind.Fell;
            p.remainsAssetPath = "Buildings/nature/tree_stump_cut_rings";
            p.drops = null;
            p.swingAnimationKey = "harvest_chop";
            p.regrowSeconds = spec.RegrowSeconds;
            p.noiseRadius = 12f;
            p.remainsWalkable = true;
            p.harvestable = true;
            p.harvestMode = HarvestMode.Destroy;
            p.harvestVerb = "Talar";
            p.interactionRadius = 1.7f;
            p.secondsPerBlow = 0.6f;
            p.blowDamage = 10;
            p.yieldPerBlow = null;
            p.yieldPool = null;

            p.gatheringSkill = skill;
            p.skillDifficulty = spec.Difficulty;
            p.nodeDisplayName = TreeFamilyClassifier.DisplayName(spec.Family);
            p.yieldTags = spec.Tags;
            p.workPerYield = spec.WorkPerYield;
            p.fellBonusYields = spec.FellBonus;
            p.blowNoiseRadius = 5f;
            p.leafColor = spec.Leaf;
            p.remainsTint = spec.RemainsTint;
            p.fallSeconds = spec.Durability >= 100 ? 1.25f : 0.9f;
        }

        private static int WireTemplates(Dictionary<TreeFamily, DestructionProfile> profiles, StringBuilder report)
        {
            var treeProfiles = new HashSet<DestructionProfile>(profiles.Values);
            int wired = 0;
            var counts = new Dictionary<TreeFamily, int>();

            foreach (string guid in AssetDatabase.FindAssets("t:BuildingTemplateData"))
            {
                var template = AssetDatabase.LoadAssetAtPath<BuildingTemplateData>(AssetDatabase.GUIDToAssetPath(guid));
                if (template == null || template.destruction == null) continue;
                if (!treeProfiles.Contains(template.destruction)) continue;

                var family = TreeFamilyClassifier.Classify(template.assetPath);
                if (family == TreeFamily.None || !profiles.TryGetValue(family, out var wanted)) continue;

                counts[family] = counts.TryGetValue(family, out int c) ? c + 1 : 1;
                if (template.destruction == wanted) continue;

                template.destruction = wanted;
                Save(template);
                wired++;
            }

            foreach (var kv in counts.OrderBy(k => k.Key))
                report.Append("    ").Append(kv.Key).Append(": ").Append(kv.Value).Append('\n');
            return wired;
        }

        // ── Asset helpers ────────────────────────────────────────────────────────

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var created = ScriptableObject.CreateInstance<T>();
            created.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        private static void Save(UnityEngine.Object asset)
        {
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
