using NUnit.Framework;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// EditMode tests for ScriptableObject catalogs — validates the Dictionary-cached
    /// O(1) lookups in <see cref="BuildingCatalog"/> and <see cref="ParticlePresetCatalog"/>.
    ///
    /// Migrated from <c>PlayMode/Data/CatalogLookupPlayTests.cs</c>. The original used
    /// <c>[UnityTest]</c> + gratuitous <c>yield return null</c> between synchronous
    /// SO instantiation and lookup calls. Neither needs the frame loop, real time,
    /// or <c>MonoBehaviour</c> lifecycle — moving to EditMode cuts ~1 second off
    /// every PlayMode suite run with zero coverage loss.
    /// </summary>
    [TestFixture]
    public class CatalogLookupTests
    {
        // ── BuildingCatalog ─────────────────────────────────────────────────────

        [Test]
        public void BuildingCatalog_GetById_ReturnsCorrectTemplate()
        {
            var catalog = ScriptableObject.CreateInstance<BuildingCatalog>();
            var template1 = ScriptableObject.CreateInstance<BuildingTemplateData>();
            template1.templateId = 42;

            var template2 = ScriptableObject.CreateInstance<BuildingTemplateData>();
            template2.templateId = 99;

            catalog.AddTemplate(template1);
            catalog.AddTemplate(template2);

            try
            {
                var result = catalog.GetById(42);
                Assert.IsNotNull(result);
                Assert.AreEqual(42, result.templateId);

                var result2 = catalog.GetById(99);
                Assert.IsNotNull(result2);
                Assert.AreEqual(99, result2.templateId);

                var result3 = catalog.GetById(999);
                Assert.IsNull(result3);
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(template1);
                Object.DestroyImmediate(template2);
            }
        }

        [Test]
        public void BuildingCatalog_AddTemplate_NoDuplicates()
        {
            var catalog = ScriptableObject.CreateInstance<BuildingCatalog>();
            var t1 = ScriptableObject.CreateInstance<BuildingTemplateData>();
            t1.templateId = 1;

            var t2 = ScriptableObject.CreateInstance<BuildingTemplateData>();
            t2.templateId = 1;

            try
            {
                Assert.IsTrue(catalog.AddTemplate(t1));
                Assert.IsFalse(catalog.AddTemplate(t2), "Should reject duplicate templateId.");
                Assert.AreEqual(1, catalog.Templates.Count);
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(t1);
                Object.DestroyImmediate(t2);
            }
        }

        [Test]
        public void BuildingCatalog_UpsertTemplate_ReplacesExisting()
        {
            var catalog = ScriptableObject.CreateInstance<BuildingCatalog>();
            var t1 = ScriptableObject.CreateInstance<BuildingTemplateData>();
            t1.templateId = 5;

            var t2 = ScriptableObject.CreateInstance<BuildingTemplateData>();
            t2.templateId = 5;

            try
            {
                catalog.AddTemplate(t1);
                catalog.UpsertTemplate(t2);

                var result = catalog.GetById(5);
                Assert.AreSame(t2, result, "Upsert must replace the original template.");
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(t1);
                Object.DestroyImmediate(t2);
            }
        }

        // ── ParticlePresetCatalog ───────────────────────────────────────────────

        [Test]
        public void ParticlePresetCatalog_GetById_ReturnsCorrectPreset()
        {
            var catalog = ScriptableObject.CreateInstance<ParticlePresetCatalog>();

            var p1 = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            p1.id = "fireball_trail";

            var p2 = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            p2.id = "healing_aura";

            catalog.SetPresets(new[] { p1, p2 });

            try
            {
                var result = catalog.GetById("fireball_trail");
                Assert.IsNotNull(result);
                Assert.AreEqual("fireball_trail", result.id);

                var result2 = catalog.GetById("healing_aura");
                Assert.IsNotNull(result2);

                Assert.IsNull(catalog.GetById("nonexistent"));
                Assert.IsNull(catalog.GetById(null));
                Assert.IsNull(catalog.GetById(""));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(p1);
                Object.DestroyImmediate(p2);
            }
        }

        [Test]
        public void ParticlePresetCatalog_SetPresets_InvalidatesCacheProperly()
        {
            var catalog = ScriptableObject.CreateInstance<ParticlePresetCatalog>();

            var p1 = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            p1.id = "effect_a";

            var p2 = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            p2.id = "effect_b";

            try
            {
                catalog.SetPresets(new[] { p1 });
                Assert.IsNotNull(catalog.GetById("effect_a"));

                catalog.SetPresets(new[] { p2 });

                Assert.IsNull(catalog.GetById("effect_a"),
                    "Old preset must not be found after SetPresets replaces the set.");
                Assert.IsNotNull(catalog.GetById("effect_b"),
                    "New preset must be found after SetPresets replaces the set.");
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(p1);
                Object.DestroyImmediate(p2);
            }
        }
    }
}
