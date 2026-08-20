using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using Valkur.Data;
using Valkur.Gameplay.VFX;
using Cat = Valkur.Gameplay.VFX.ParticlePresetCategory.Category;

namespace Valkur.Tests.EditMode.Editors.Particles
{
    /// <summary>
    /// Guards the Particles Editor (F1) preset category tabs.
    ///
    /// The classification is prefix-based, so it rots quietly: rename a preset or add a
    /// family and presets slide into the SpellFx fallback without any error. These tests
    /// make that visible.
    /// </summary>
    [TestFixture]
    public class ParticlePresetCategoryTests
    {
        private const string CATALOG_PATH =
            "Assets/_Project/Data/Catalogs/Particles/ParticlePresetCatalog.asset";

        private static ParticlePresetCatalog LoadCatalog()
        {
            var cat = AssetDatabase.LoadAssetAtPath<ParticlePresetCatalog>(CATALOG_PATH);
            Assert.IsNotNull(cat, $"ParticlePresetCatalog not found at {CATALOG_PATH}.");
            return cat;
        }

        [Test]
        public void Of_NullOrEmpty_FallsBackToSpellFx_WithoutThrowing()
        {
            Assert.AreEqual(Cat.SpellFx, ParticlePresetCategory.Of((string)null));
            Assert.AreEqual(Cat.SpellFx, ParticlePresetCategory.Of(""));
            Assert.AreEqual(Cat.SpellFx, ParticlePresetCategory.Of((ParticlePresetDefinition)null));
        }

        [TestCase("torch_flame",       Cat.Ambient)]
        [TestCase("torch_embers",      Cat.Ambient)]
        [TestCase("torch_smoke",       Cat.Ambient)]
        [TestCase("forge_coals",       Cat.Ambient)]
        [TestCase("forge_flame",       Cat.Ambient)]
        [TestCase("forge_embers",      Cat.Ambient)]
        [TestCase("forge_glow",        Cat.Ambient)]
        [TestCase("chimney_smoke",     Cat.Ambient)]
        [TestCase("rain_mist_soft",    Cat.Ambient)]
        [TestCase("falling_petal_30s", Cat.Vegetation)]
        [TestCase("flowers_pollen_soft", Cat.Vegetation)]
        [TestCase("flowers_petal_pink_60s", Cat.Vegetation)]
        [TestCase("autumn_leaves_gradient", Cat.Vegetation)]
        [TestCase("falling_leaf_30s",  Cat.Vegetation)]
        [TestCase("falling_leaf_canopy", Cat.Vegetation)]
        [TestCase("water_flow_h",      Cat.Water)]
        [TestCase("water_fountain_small", Cat.Water)]
        [TestCase("fountain_sparkle",  Cat.Water)]
        [TestCase("fountain_jet_arc",  Cat.Water)]
        [TestCase("fountain_fall",     Cat.Water)]
        [TestCase("fountain_splash",   Cat.Water)]
        [TestCase("fountain_mist",     Cat.Water)]
        [TestCase("explosion_small",   Cat.Fire)]
        [TestCase("ember_plume",       Cat.Fire)]
        [TestCase("smoke_emitter",     Cat.Fire)]
        [TestCase("aura_ring_additive", Cat.Magic)]
        [TestCase("mana_regen_aura",   Cat.Magic)]
        [TestCase("portal_red_full",   Cat.Portals)]
        [TestCase("portal_oval_auto",  Cat.Portals)]
        public void Of_KnownPresets_LandInTheirCategory(string id, Cat expected)
        {
            Assert.AreEqual(expected, ParticlePresetCategory.Of(id), $"'{id}' misclassified.");
        }

        /// <summary>
        /// The four projectile stacks carry elemental words in their names — "fireball",
        /// "iceball", "lightball" — that the Fire and Magic prefixes would otherwise capture.
        /// They are spell internals and must stay out of the decoration tabs.
        /// </summary>
        [TestCase("fireball_core")]
        [TestCase("fireball_impact_smoke")]
        [TestCase("iceball_trail")]
        [TestCase("darkball_wake")]
        [TestCase("lightball_motes")]
        public void Of_ProjectileStacks_AreSpellFx_NotElementalCategories(string id)
        {
            Assert.AreEqual(Cat.SpellFx, ParticlePresetCategory.Of(id),
                $"'{id}' belongs to a projectile stack and must not appear in a decoration tab.");
        }

        [Test]
        public void EveryCatalogPreset_Classifies_WithoutThrowing()
        {
            foreach (var p in LoadCatalog().Presets)
            {
                if (p == null) continue;
                Assert.DoesNotThrow(() => ParticlePresetCategory.Of(p),
                    $"Classifying '{p.id}' threw.");
            }
        }

        /// <summary>
        /// A tab that shows nothing is worse than no tab — it reads as a broken filter.
        /// </summary>
        [Test]
        public void EveryTab_HasAtLeastOnePreset()
        {
            var counts = new Dictionary<Cat, int>();
            foreach (Cat c in ParticlePresetCategory.TabOrder) counts[c] = 0;

            foreach (var p in LoadCatalog().Presets)
            {
                if (p == null) continue;
                var c = ParticlePresetCategory.Of(p);
                if (counts.ContainsKey(c)) counts[c]++;
            }

            foreach (var kv in counts)
                Assert.Greater(kv.Value, 0,
                    $"Category tab '{ParticlePresetCategory.Label(kv.Key)}' would render empty.");
        }

        /// <summary>
        /// Every tab needs a label, and the strip shares one panel width between them — a
        /// long label truncates to nothing useful.
        /// </summary>
        [Test]
        public void EveryTab_HasAShortLabel()
        {
            foreach (Cat c in ParticlePresetCategory.TabOrder)
            {
                string label = ParticlePresetCategory.Label(c);
                Assert.IsFalse(string.IsNullOrWhiteSpace(label), $"{c} has no label.");
                Assert.LessOrEqual(label.Length, 8,
                    $"Label '{label}' is too long for the shared tab strip, which now " +
                    "splits one panel width between eight tabs.");
            }
        }

        [Test]
        public void TabOrder_CoversEveryCategory_ExactlyOnce()
        {
            var seen = new HashSet<Cat>();
            foreach (Cat c in ParticlePresetCategory.TabOrder)
                Assert.IsTrue(seen.Add(c), $"{c} appears twice in TabOrder.");

            foreach (Cat c in (Cat[])Enum.GetValues(typeof(Cat)))
                Assert.IsTrue(seen.Contains(c),
                    $"{c} exists but has no tab, so its presets are unreachable.");
        }
    }
}
