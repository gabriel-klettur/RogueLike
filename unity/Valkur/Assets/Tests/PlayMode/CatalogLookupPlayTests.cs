using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Tests.PlayMode
{
    /// <summary>
    /// PlayMode tests for ScriptableObject catalogs.
    /// Validates Dictionary-cached O(1) lookups in BuildingCatalog and ParticlePresetCatalog.
    /// </summary>
    public class CatalogLookupPlayTests
    {
        // ── BuildingCatalog ──

        [UnityTest]
        public IEnumerator BuildingCatalog_GetById_ReturnsCorrectTemplate()
        {
            var catalog = ScriptableObject.CreateInstance<BuildingCatalog>();
            var template1 = ScriptableObject.CreateInstance<BuildingTemplateData>();
            template1.templateId = 42;

            var template2 = ScriptableObject.CreateInstance<BuildingTemplateData>();
            template2.templateId = 99;

            catalog.AddTemplate(template1);
            catalog.AddTemplate(template2);

            yield return null;

            var result = catalog.GetById(42);
            Assert.IsNotNull(result);
            Assert.AreEqual(42, result.templateId);

            var result2 = catalog.GetById(99);
            Assert.IsNotNull(result2);
            Assert.AreEqual(99, result2.templateId);

            var result3 = catalog.GetById(999);
            Assert.IsNull(result3);

            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(template1);
            Object.DestroyImmediate(template2);
        }

        [UnityTest]
        public IEnumerator BuildingCatalog_AddTemplate_NoDuplicates()
        {
            var catalog = ScriptableObject.CreateInstance<BuildingCatalog>();
            var t1 = ScriptableObject.CreateInstance<BuildingTemplateData>();
            t1.templateId = 1;

            var t2 = ScriptableObject.CreateInstance<BuildingTemplateData>();
            t2.templateId = 1;

            yield return null;

            Assert.IsTrue(catalog.AddTemplate(t1));
            Assert.IsFalse(catalog.AddTemplate(t2), "Should reject duplicate templateId");
            Assert.AreEqual(1, catalog.Templates.Count);

            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(t1);
            Object.DestroyImmediate(t2);
        }

        [UnityTest]
        public IEnumerator BuildingCatalog_UpsertTemplate_ReplacesExisting()
        {
            var catalog = ScriptableObject.CreateInstance<BuildingCatalog>();
            var t1 = ScriptableObject.CreateInstance<BuildingTemplateData>();
            t1.templateId = 5;

            var t2 = ScriptableObject.CreateInstance<BuildingTemplateData>();
            t2.templateId = 5;

            yield return null;

            catalog.AddTemplate(t1);
            catalog.UpsertTemplate(t2);

            var result = catalog.GetById(5);
            Assert.AreSame(t2, result, "Upsert should replace the original template");

            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(t1);
            Object.DestroyImmediate(t2);
        }

        // ── ParticlePresetCatalog ──

        [UnityTest]
        public IEnumerator ParticlePresetCatalog_GetById_ReturnsCorrectPreset()
        {
            var catalog = ScriptableObject.CreateInstance<ParticlePresetCatalog>();

            var p1 = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            p1.id = "fireball_trail";

            var p2 = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            p2.id = "healing_aura";

            catalog.SetPresets(new[] { p1, p2 });

            yield return null;

            var result = catalog.GetById("fireball_trail");
            Assert.IsNotNull(result);
            Assert.AreEqual("fireball_trail", result.id);

            var result2 = catalog.GetById("healing_aura");
            Assert.IsNotNull(result2);

            var result3 = catalog.GetById("nonexistent");
            Assert.IsNull(result3);

            var result4 = catalog.GetById(null);
            Assert.IsNull(result4);

            var result5 = catalog.GetById("");
            Assert.IsNull(result5);

            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(p1);
            Object.DestroyImmediate(p2);
        }

        [UnityTest]
        public IEnumerator ParticlePresetCatalog_SetPresets_InvalidatesCacheProperly()
        {
            var catalog = ScriptableObject.CreateInstance<ParticlePresetCatalog>();

            var p1 = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            p1.id = "effect_a";

            catalog.SetPresets(new[] { p1 });

            yield return null;

            Assert.IsNotNull(catalog.GetById("effect_a"));

            // Replace with different set
            var p2 = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            p2.id = "effect_b";
            catalog.SetPresets(new[] { p2 });

            Assert.IsNull(catalog.GetById("effect_a"), "Old preset should not be found after SetPresets");
            Assert.IsNotNull(catalog.GetById("effect_b"), "New preset should be found after SetPresets");

            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(p1);
            Object.DestroyImmediate(p2);
        }
    }
}
