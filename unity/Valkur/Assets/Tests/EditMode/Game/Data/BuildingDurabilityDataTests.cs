using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Pins the SHIPPED building-durability data: the resistance matrix, the common tree
    /// profile, and the templates wired to it.
    ///
    /// <para>These assert characteristics rather than literals wherever a designer is meant
    /// to keep tuning — "an axe beats a sword against wood" survives a balance pass, "an axe
    /// scores exactly 1.00" does not. The exceptions are the invariants that stop the system
    /// being quietly wrong: a material with no row, a remains sprite that does not load, a
    /// stump wired as choppable.</para>
    /// </summary>
    [TestFixture]
    public class BuildingDurabilityDataTests
    {
        private const string TablePath =
            "Assets/_Project/Resources/DestructionResistanceTable.asset";
        private const string TreeProfilePath =
            "Assets/_Project/Data/Catalogs/Destruction/DP_tree_common.asset";

        private static DestructionResistanceTable LoadTable()
        {
            var table = AssetDatabase.LoadAssetAtPath<DestructionResistanceTable>(TablePath);
            Assert.That(table, Is.Not.Null, $"Missing shipped matrix at {TablePath}.");
            return table;
        }

        // ── The matrix ─────────────────────────────────────────────────────────────

        [Test]
        public void Matrix_HasARowForEveryMaterial()
        {
            var table = LoadTable();
            foreach (MaterialClass material in System.Enum.GetValues(typeof(MaterialClass)))
            {
                bool found = false;
                for (int i = 0; i < table.rows.Count; i++)
                    if (table.rows[i] != null && table.rows[i].material == material) found = true;

                Assert.That(found, Is.True,
                    $"MaterialClass.{material} has no row. A missing row falls back to 1.0 for " +
                    "every damage class, so the material silently stops resisting anything.");
            }
        }

        [Test]
        public void Matrix_TheRightToolAlwaysBeatsTheWrongOne()
        {
            var table = LoadTable();

            // The whole point of the axis: what a tool is FOR must win against its material.
            Assert.That(table.Multiplier(MaterialClass.Wood, DamageClass.Axe),
                Is.GreaterThan(table.Multiplier(MaterialClass.Wood, DamageClass.Pick)),
                "An axe must beat a pick against wood.");
            Assert.That(table.Multiplier(MaterialClass.Stone, DamageClass.Pick),
                Is.GreaterThan(table.Multiplier(MaterialClass.Stone, DamageClass.Axe)),
                "A pick must beat an axe against stone.");
            Assert.That(table.Multiplier(MaterialClass.Metal, DamageClass.Blunt),
                Is.GreaterThan(table.Multiplier(MaterialClass.Metal, DamageClass.Blade)),
                "Metal is dented, not cut.");
        }

        [Test]
        public void Matrix_TheBestToolAlwaysBeatsBareHands()
        {
            var table = LoadTable();
            var tools = new[]
            {
                DamageClass.Axe, DamageClass.Pick, DamageClass.Blade, DamageClass.Blunt,
            };

            // The claim is that SOME tool is worth carrying for every material, not that EVERY
            // tool is. An earlier version asserted the latter and failed on the shipped data,
            // correctly: a pick scores 0.10 against foliage and bare hands score 0.25, because
            // pulling a bush apart by hand really is easier than mining it. Demanding that
            // every tool beat bare hands everywhere would force a matrix where the tool axis
            // says nothing — the whole point is that the wrong tool is WORSE than no tool.
            foreach (MaterialClass material in System.Enum.GetValues(typeof(MaterialClass)))
            {
                float bare = table.Multiplier(material, DamageClass.None);
                float best = 0f;
                DamageClass bestTool = DamageClass.None;
                foreach (var tool in tools)
                {
                    float multiplier = table.Multiplier(material, tool);
                    if (multiplier <= best) continue;
                    best = multiplier;
                    bestTool = tool;
                }

                Assert.That(best, Is.GreaterThan(bare),
                    $"Nothing beats bare hands against {material} (best is {bestTool} at {best} " +
                    $"against {bare}), so there is no reason to carry a tool for it.");
            }
        }

        [Test]
        public void Matrix_FireBurnsWoodAndFoliageAndNotStone()
        {
            var table = LoadTable();
            Assert.That(table.Multiplier(MaterialClass.Wood, DamageClass.Fire), Is.GreaterThan(1f));
            Assert.That(table.Multiplier(MaterialClass.Foliage, DamageClass.Fire), Is.GreaterThan(1f));
            Assert.That(table.Multiplier(MaterialClass.Stone, DamageClass.Fire), Is.LessThan(0.2f),
                "A stone house must not burn down, or the material axis says nothing.");
        }

        [Test]
        public void Matrix_NoCellIsNegative()
        {
            var table = LoadTable();
            foreach (MaterialClass material in System.Enum.GetValues(typeof(MaterialClass)))
            foreach (DamageClass damageClass in System.Enum.GetValues(typeof(DamageClass)))
            {
                Assert.That(table.Multiplier(material, damageClass), Is.GreaterThanOrEqualTo(0f),
                    $"{material}/{damageClass} is negative — a blow that heals a building.");
            }
        }

        [Test]
        public void Matrix_SeedingIsIdempotentAndMatchesTheShippedAsset()
        {
            // The seed is the reference balance a designer can return to, so it has to still
            // describe what actually ships. A fresh instance is used rather than reseeding
            // the live asset, which would overwrite whatever tuning is in flight.
            var fresh = ScriptableObject.CreateInstance<DestructionResistanceTable>();
            try
            {
                fresh.SeedShippedMatrix();
                var shipped = LoadTable();

                foreach (MaterialClass material in System.Enum.GetValues(typeof(MaterialClass)))
                foreach (DamageClass damageClass in System.Enum.GetValues(typeof(DamageClass)))
                {
                    Assert.That(shipped.Multiplier(material, damageClass),
                        Is.EqualTo(fresh.Multiplier(material, damageClass)).Within(0.0001f),
                        $"{material}/{damageClass} has drifted from SeedShippedMatrix(). Move " +
                        "the seed to match the tuning, so the reference stays a real fallback.");
                }
            }
            finally
            {
                Object.DestroyImmediate(fresh);
            }
        }

        // ── The shipped tree profile ───────────────────────────────────────────────

        [Test]
        public void TreeProfile_IsWoodAndFellsAndDropsSomething()
        {
            var profile = AssetDatabase.LoadAssetAtPath<DestructionProfile>(TreeProfilePath);
            Assert.That(profile, Is.Not.Null, $"Missing {TreeProfilePath}.");

            Assert.That(profile.material, Is.EqualTo(MaterialClass.Wood));
            Assert.That(profile.kind, Is.EqualTo(DestructionKind.Fell));
            Assert.That(profile.durability, Is.GreaterThan(0));
            Assert.That(profile.drops, Is.Not.Null, "A felled tree that drops nothing is scenery.");
            Assert.That(profile.drops.entries, Is.Not.Empty);
        }

        [Test]
        public void TreeProfile_RemainsSpriteActuallyLoads()
        {
            var profile = AssetDatabase.LoadAssetAtPath<DestructionProfile>(TreeProfilePath);
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.remainsAssetPath, Is.Not.Empty);

            // The path is a string resolved at destruction time, so a typo is invisible until
            // a player fells a tree and gets an empty patch of ground plus a console warning.
            Assert.That(Resources.Load<Sprite>(profile.remainsAssetPath), Is.Not.Null,
                $"Remains sprite '{profile.remainsAssetPath}' does not load from Resources.");
        }

        [Test]
        public void DropTables_OnlyNameItemsTheCatalogHolds()
        {
            var catalogs = AssetDatabase.FindAssets("t:ItemCatalog");
            Assert.That(catalogs, Is.Not.Empty, "No ItemCatalog in the project.");
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(
                AssetDatabase.GUIDToAssetPath(catalogs[0]));

            foreach (var guid in AssetDatabase.FindAssets("t:HarvestDropTable"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var table = AssetDatabase.LoadAssetAtPath<HarvestDropTable>(path);
                if (table == null || table.entries == null) continue;

                foreach (var entry in table.entries)
                {
                    Assert.That(entry.itemId, Is.Not.Empty, $"Empty item id in {path}.");
                    Assert.That(catalog.GetById(entry.itemId), Is.Not.Null,
                        $"'{entry.itemId}' in {path} is not in the ItemCatalog.");
                    Assert.That(entry.maxQuantity, Is.GreaterThanOrEqualTo(entry.minQuantity),
                        $"'{entry.itemId}' in {path} has an inverted quantity range.");
                }
            }
        }

        // ── The wiring ─────────────────────────────────────────────────────────────

        [Test]
        public void WiredTemplates_AreTreesAndNeverTheirOwnRemains()
        {
            var wired = new List<BuildingTemplateData>();
            foreach (var guid in AssetDatabase.FindAssets("t:BuildingTemplateData"))
            {
                var template = AssetDatabase.LoadAssetAtPath<BuildingTemplateData>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (template != null && template.destruction != null) wired.Add(template);
            }

            Assert.That(wired, Is.Not.Empty,
                "No template declares a DestructionProfile, so nothing in the world can be broken.");

            foreach (var template in wired)
            {
                string path = template.assetPath == null ? "" : template.assetPath.ToLowerInvariant();

                // A stump IS what a felled tree leaves. Wiring one as choppable would let a
                // player chop a stump into a stump, forever.
                Assert.That(path.Contains("stump"), Is.False,
                    $"'{template.assetPath}' is a stump and must not be destructible.");
                Assert.That(path.Contains("log_"), Is.False,
                    $"'{template.assetPath}' is fallen timber and must not be destructible.");
            }
        }

        [Test]
        public void ToolItems_DeclareATierWhenTheyDeclareAClass()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:ItemDefinition"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (item == null || item.toolClass == DamageClass.None) continue;

                // A tool at tier 0 can never satisfy a profile that requires one, so it reads
                // in game as an item that looks like a tool and behaves like bare hands.
                Assert.That(item.toolTier, Is.GreaterThan(0),
                    $"'{item.itemId}' declares toolClass {item.toolClass} but tier 0.");

                // The magical classes are resolved from a spell's element, never from an item.
                Assert.That(DamageClassIsPhysical(item.toolClass), Is.True,
                    $"'{item.itemId}' declares the magical class {item.toolClass}; an item's " +
                    "toolClass is how it is SWUNG.");
            }
        }

        private static bool DamageClassIsPhysical(DamageClass damageClass)
        {
            return damageClass == DamageClass.None
                || damageClass == DamageClass.Axe
                || damageClass == DamageClass.Pick
                || damageClass == DamageClass.Blade
                || damageClass == DamageClass.Blunt;
        }
    }
}
