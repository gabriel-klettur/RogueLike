using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Pins the SHIPPED harvesting data: the mine profile, the tools that can work it, and
    /// the building templates wired to it.
    ///
    /// <para>Kept beside <see cref="BuildingDurabilityDataTests"/> rather than inside it
    /// because the two assert different things about different assets — that fixture owns the
    /// resistance matrix and the tree, this one owns the mine, the tools and the harvest
    /// fields. They share the rule that a characteristic is pinned wherever a designer is
    /// meant to keep tuning ("a pick beats an axe on stone") and a literal only where the
    /// literal is the invariant (a drop table naming an item the catalog does not hold).</para>
    /// </summary>
    [TestFixture]
    public class HarvestingDataTests
    {
        private const string MineProfilePath =
            "Assets/_Project/Data/Catalogs/Destruction/DP_mine_iron.asset";
        private const string TreeProfilePath =
            "Assets/_Project/Data/Catalogs/Destruction/DP_tree_common.asset";
        private const string CatalogPath =
            "Assets/_Project/Data/Catalogs/Items/ItemCatalog.asset";
        private const string TablePath =
            "Assets/_Project/Resources/DestructionResistanceTable.asset";

        /// <summary>Every shipped placement of <c>Buildings/mine</c>.</summary>
        private static readonly int[] MineTemplateIds = { 68, 91, 210, 211 };

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, $"Missing shipped asset at {path}.");
            return asset;
        }

        // The mine profile -------------------------------------------------------------

        [Test]
        public void MineProfile_IsAHarvestableDepleteNode()
        {
            var mine = Load<DestructionProfile>(MineProfilePath);

            Assert.That(mine.harvestable, Is.True);
            Assert.That(mine.harvestMode, Is.EqualTo(HarvestMode.Deplete),
                "A mine is exhausted, not destroyed. Destroy mode would let the player delete " +
                "a hillside, and would put the seam in the obstacle registry where a stray " +
                "fireball could empty it from across the room.");
            Assert.That(mine.material, Is.EqualTo(MaterialClass.Stone));
            Assert.That(mine.charges, Is.GreaterThan(1));
        }

        [Test]
        public void MineProfile_RequiresARealToolAndSaysSo()
        {
            var mine = Load<DestructionProfile>(MineProfilePath);

            Assert.That(mine.requiredToolTier, Is.GreaterThan(0),
                "Bare hands are not a mining tool.");
            Assert.That(mine.harvestVerb, Is.Not.Empty,
                "The verb is what the interaction prompt reads; an empty one falls back to " +
                "'Harvest', which tells the player nothing about what they are standing at.");
        }

        [Test]
        public void MineProfile_PaysOutPerBlowRatherThanOnDeath()
        {
            var mine = Load<DestructionProfile>(MineProfilePath);

            Assert.That(mine.yieldPerBlow, Is.Not.Null,
                "A Deplete node is never destroyed, so a drop table on `drops` would never be " +
                "rolled and the mine would produce nothing at all.");
            Assert.That(mine.drops, Is.Null,
                "`drops` is what a Destroy-mode building leaves when it falls. A seam that " +
                "authored one would be describing an event that cannot happen to it.");
        }

        [Test]
        public void MineProfile_IsStillWorkableWithoutAPickJustSlower()
        {
            var mine = Load<DestructionProfile>(MineProfilePath);

            // A required tool tier is a statement about SPEED, never about permission. Setting
            // chipDamageFraction to 0 here would make the node refuse a player with no pick
            // outright, and a mine you are simply not allowed to touch is a different game
            // from one that is hard work without the right kit. Chopping has always worked
            // this way and mining matches it.
            Assert.That(mine.chipDamageFraction, Is.GreaterThan(0f),
                "Mining without a pick has to remain possible, only slow.");

            var table = Load<DestructionResistanceTable>(TablePath);
            float bareHanded = table.Multiplier(mine.material, DamageClass.None)
                               * mine.chipDamageFraction;

            Assert.That(bareHanded, Is.GreaterThan(0f),
                "The matrix and the chip fraction multiply, so either one at zero refuses the " +
                "blow. Both have to stay positive for a bare-handed shift to end.");
        }

        [Test]
        public void MineProfile_RegrowsSoTheSeamIsNotConsumedForever()
        {
            var mine = Load<DestructionProfile>(MineProfilePath);
            Assert.That(mine.regrowSeconds, Is.GreaterThan(0f));
        }

        [Test]
        public void MineYieldTable_OnlyNamesItemsTheCatalogHolds()
        {
            var mine = Load<DestructionProfile>(MineProfilePath);
            var catalog = Load<ItemCatalog>(CatalogPath);

            Assert.That(mine.yieldPerBlow.entries, Is.Not.Empty);
            foreach (var entry in mine.yieldPerBlow.entries)
            {
                Assert.That(entry.itemId, Is.Not.Empty);
                Assert.That(catalog.GetById(entry.itemId), Is.Not.Null,
                    $"Yield '{entry.itemId}' is not in the ItemCatalog, so every blow that " +
                    "rolls it logs a warning and drops nothing.");
                Assert.That(entry.maxQuantity, Is.GreaterThanOrEqualTo(entry.minQuantity));
                Assert.That(entry.chance, Is.GreaterThan(0f));
            }
        }

        [Test]
        public void MineYieldTable_HasACommonYieldSoAShiftIsNeverEmpty()
        {
            var mine = Load<DestructionProfile>(MineProfilePath);

            float best = 0f;
            foreach (var entry in mine.yieldPerBlow.entries)
                if (entry.chance > best) best = entry.chance;

            // Entries are rolled INDEPENDENTLY, so this is not a distribution that has to sum
            // to anything. What it has to have is one line the player can rely on: a table of
            // nothing but rare strikes reads as a broken mine for most of a shift.
            Assert.That(best, Is.GreaterThanOrEqualTo(0.4f));
        }

        // The templates ----------------------------------------------------------------

        [Test]
        public void EveryMineTemplate_IsWiredToTheMineProfile()
        {
            var mine = Load<DestructionProfile>(MineProfilePath);

            foreach (var id in MineTemplateIds)
            {
                var path = $"Assets/_Project/Data/Catalogs/Buildings/BuildingTemplate_{id}.asset";
                var template = Load<BuildingTemplateData>(path);

                Assert.That(template.assetPath, Does.Contain("mine"),
                    $"Template {id} is no longer the mine art; this list needs updating.");
                Assert.That(template.destruction, Is.SameAs(mine),
                    $"Template {id} draws a mine and cannot be mined.");
            }
        }

        // The tools --------------------------------------------------------------------

        [Test]
        public void APickaxeExistsAndIsTheOnlyWayIntoStone()
        {
            var catalog = Load<ItemCatalog>(CatalogPath);
            var table = Load<DestructionResistanceTable>(TablePath);
            var mine = Load<DestructionProfile>(MineProfilePath);

            var pick = catalog.GetById("pickaxe_iron");
            Assert.That(pick, Is.Not.Null, "Nothing in the game can mine without a pick.");
            Assert.That(pick.toolClass, Is.EqualTo(DamageClass.Pick));
            Assert.That(pick.toolTier, Is.GreaterThanOrEqualTo(mine.requiredToolTier),
                "The only shipped pick must clear the only shipped mine, or the tool exists " +
                "and still cannot open the thing it was made for.");

            float pickOnStone = table.Multiplier(MaterialClass.Stone, DamageClass.Pick);
            float axeOnStone = table.Multiplier(MaterialClass.Stone, DamageClass.Axe);
            float bladeOnStone = table.Multiplier(MaterialClass.Stone, DamageClass.Blade);

            Assert.That(pickOnStone, Is.GreaterThan(axeOnStone));
            Assert.That(pickOnStone, Is.GreaterThan(bladeOnStone));
        }

        [Test]
        public void AnAxeExistsAndIsTheBestThingToChopWith()
        {
            var catalog = Load<ItemCatalog>(CatalogPath);
            var table = Load<DestructionResistanceTable>(TablePath);

            var axe = catalog.GetById("axe_iron");
            Assert.That(axe, Is.Not.Null);
            Assert.That(axe.toolClass, Is.EqualTo(DamageClass.Axe));

            float axeOnWood = table.Multiplier(MaterialClass.Wood, DamageClass.Axe);
            Assert.That(axeOnWood, Is.GreaterThan(table.Multiplier(MaterialClass.Wood, DamageClass.Blade)));
            Assert.That(axeOnWood, Is.GreaterThan(table.Multiplier(MaterialClass.Wood, DamageClass.Pick)));
        }

        [Test]
        public void EveryToolItemDeclaresATier()
        {
            // toolTier 0 means "bare hands are enough", which is a real answer for a material
            // with no requirement and a meaningless one for a tool. A tier-0 pick would be
            // refused by every profile that asks for a tool at all.
            var catalog = Load<ItemCatalog>(CatalogPath);
            foreach (var item in catalog.Items)
            {
                if (item == null || item.toolClass == DamageClass.None) continue;
                Assert.That(item.toolTier, Is.GreaterThan(0),
                    $"'{item.itemId}' declares a tool class but no tier.");
            }
        }

        /// <summary>
        /// The dwarf's animation bindings.
        ///
        /// <para>Reached through the <see cref="PlayerDefinition"/> that owns it:
        /// <see cref="EntityAssetConfig"/> is a plain [Serializable] class embedded as a
        /// field, not a ScriptableObject, so it has no asset of its own and
        /// <c>FindAssets("t:EntityAssetConfig")</c> returns nothing at all.</para>
        /// </summary>
        private static EntityAssetConfig LoadDwarfConfig()
        {
            var def = Load<PlayerDefinition>("Assets/_Project/Data/Catalogs/Players/dwarf.asset");
            Assert.That(def.assetConfig, Is.Not.Null, "The dwarf ships no asset config.");
            return def.assetConfig;
        }

        // The swing animation ------------------------------------------------------------

        [Test]
        public void EveryHarvestableProfile_NamesASwingAnimation()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:DestructionProfile"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<DestructionProfile>(path);
                if (profile == null || !profile.harvestable) continue;

                // An empty key is legal and falls back to the ordinary attack rotation, but
                // on a SHIPPED harvest profile it means the character mimes the wrong action:
                // chopping a tree with a kick, or a spellcast. It is the one field that turns
                // a working system into a convincing one.
                Assert.That(profile.swingAnimationKey, Is.Not.Empty,
                    $"{path} is harvestable and names no swing animation.");
            }
        }

        [Test]
        public void TheDwarfReservesAVariantForEverySwingKeyItShips()
        {
            var dwarf = LoadDwarfConfig();

            foreach (var guid in AssetDatabase.FindAssets("t:DestructionProfile"))
            {
                var profile = AssetDatabase.LoadAssetAtPath<DestructionProfile>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (profile == null || !profile.harvestable) continue;
                if (string.IsNullOrEmpty(profile.swingAnimationKey)) continue;

                var variant = dwarf.attackVariants
                    .Find(v => v != null && v.ClaimsSpell(profile.swingAnimationKey));

                Assert.That(variant, Is.Not.Null,
                    $"No dwarf attack variant claims '{profile.swingAnimationKey}'. " +
                    "PlayWorkSwing would fall back to the rotation and the character would " +
                    "punch, kick or charge at the node instead of working it.");

                // A reservation with no art is worse than none: VariantForSpell answers with
                // an index, the animator has nothing to draw for it, and the swing renders
                // nothing at all rather than falling back to a pose that exists.
                // `sheets` is a FLAT list of framesPerDirection * 8 sprites -- 64 for an
                // eight-frame animation -- not a per-direction structure to walk. Its count
                // is the honest "did the binding land" check.
                Assert.That(variant.sheets, Is.Not.Null.And.Not.Empty,
                    $"Variant '{variant.key}' claims '{profile.swingAnimationKey}' with no sprites.");
                Assert.That(variant.sheets, Has.None.Null,
                    $"Variant '{variant.key}' has null sprite slots.");
            }
        }

        [Test]
        public void AReservedSwingVariant_IsNeverAlsoAPlainRotationStep()
        {
            // Leaving the rotation is half the guarantee and is separate from being claimable:
            // the claim makes the pose always play for its own key, and the reservation is
            // what stops an unrelated punch borrowing the pickaxe swing.
            var dwarf = LoadDwarfConfig();

            foreach (var variant in dwarf.attackVariants)
            {
                if (variant == null) continue;
                bool isHarvest = variant.ClaimsSpell("harvest_mine")
                                 || variant.ClaimsSpell("harvest_chop");
                if (!isHarvest) continue;

                Assert.That(variant.IsReservedForSpell, Is.True,
                    $"Variant '{variant.key}' is a harvest swing and must stay out of " +
                    "NextVariant's pool.");
            }
        }

        // The tree ---------------------------------------------------------------------

        [Test]
        public void TreeProfile_IsChoppableByHandAndStillDestroyMode()
        {
            var tree = Load<DestructionProfile>(TreeProfilePath);

            Assert.That(tree.harvestable, Is.True, "E must chop a tree.");
            Assert.That(tree.harvestMode, Is.EqualTo(HarvestMode.Destroy),
                "A tree is consumed by chopping; Deplete would leave it standing forever.");
            Assert.That(tree.harvestVerb, Is.Not.Empty);
            Assert.That(tree.drops, Is.Not.Null,
                "A Destroy node pays out when it falls, so it needs the death table.");
        }

        [Test]
        public void EveryHarvestableProfile_HasAWorkableClock()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:DestructionProfile"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<DestructionProfile>(path);
                if (profile == null || !profile.harvestable) continue;

                Assert.That(profile.secondsPerBlow, Is.GreaterThan(0f), path);
                Assert.That(profile.blowDamage, Is.GreaterThan(0), path);
                Assert.That(profile.interactionRadius, Is.GreaterThan(0f), path);

                // A shift the player cannot see the end of is a shift they abandon. Twenty
                // blows at the authored rate is already most of a minute.
                float blows = profile.harvestMode == HarvestMode.Deplete
                    ? profile.charges
                    : Mathf.Ceil(profile.durability / (float)profile.blowDamage);
                Assert.That(blows * profile.secondsPerBlow, Is.LessThan(45f),
                    $"{path} takes {blows} blows at {profile.secondsPerBlow}s each.");
            }
        }
    }
}
