using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Guards the building templates that emit their own light.
    ///
    /// A fixture lights the world by naming a <c>LightPresetCatalog</c> key on its template;
    /// <c>WorldLightLoader.RegisterDerivedLight</c> reads it at placement time. Two ways that
    /// goes wrong, both silent:
    ///
    /// <list type="bullet">
    /// <item>A key the catalog does not define imports cleanly and then lights nothing —
    /// the brazier renders, the flame renders, and the ground stays dark.</item>
    /// <item>A re-import wipes a key that was authored by hand. The first wave's 32
    /// fixtures predate the manifest carrying light data, so <c>BuildingPropImporter</c>
    /// only WRITES the light fields when an entry actually names a preset. Relaxing that
    /// into an unconditional assignment would unlight every one of them, and nothing else
    /// in the suite would notice.</item>
    /// </list>
    ///
    /// The manifests are the contract, exactly as in <see cref="BuildingPropCatalogTests"/>.
    /// </summary>
    [TestFixture]
    public class BuildingLightFixtureTests
    {
        private const string MANIFEST_DIR_RELATIVE = "../../../tools/atlas/generated";
        private const string MANIFEST_SEARCH_PATTERN = "building_props_manifest*.json";
        private const string CATALOG_PATH = "Assets/_Project/Data/Catalogs/Buildings/BuildingCatalog.asset";
        private const string LIGHT_CATALOG_PATH = "Assets/_Project/Data/LightPresetCatalog.asset";

        [Serializable] private class Manifest { public List<Entry> entries = new List<Entry>(); }

        [Serializable]
        private class Entry
        {
            public string resourcePath;
            public string lightPresetKey;
            public float lightOffsetY;
        }

        private List<Entry> _entries;
        private Dictionary<string, BuildingTemplateData> _byPath;
        private string[] _validKeys;

        [OneTimeSetUp]
        public void LoadFixtures()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, MANIFEST_DIR_RELATIVE));
            Assert.That(Directory.Exists(dir), Is.True, $"Manifest folder missing at {dir}");

            string[] files = Directory.GetFiles(dir, MANIFEST_SEARCH_PATTERN);
            Array.Sort(files, StringComparer.Ordinal);
            Assert.That(files, Is.Not.Empty, $"No {MANIFEST_SEARCH_PATTERN} under {dir}");

            _entries = new List<Entry>();
            foreach (string file in files)
            {
                var wave = JsonUtility.FromJson<Manifest>(File.ReadAllText(file));
                if (wave?.entries != null) _entries.AddRange(wave.entries);
            }

            var catalog = AssetDatabase.LoadAssetAtPath<BuildingCatalog>(CATALOG_PATH);
            Assert.That(catalog, Is.Not.Null, $"BuildingCatalog missing at {CATALOG_PATH}");
            _byPath = new Dictionary<string, BuildingTemplateData>(StringComparer.Ordinal);
            foreach (BuildingTemplateData t in catalog.Templates)
            {
                if (t == null || string.IsNullOrEmpty(t.assetPath)) continue;
                if (!_byPath.ContainsKey(t.assetPath)) _byPath[t.assetPath] = t;
            }

            // Read the real keys rather than restating them: a preset renamed in the asset
            // must fail here, not be quietly re-blessed by a copy in the test.
            var lights = AssetDatabase.LoadAssetAtPath<LightPresetCatalog>(LIGHT_CATALOG_PATH);
            Assert.That(lights, Is.Not.Null, $"LightPresetCatalog missing at {LIGHT_CATALOG_PATH}");
            _validKeys = lights.presets.Where(p => p != null && !string.IsNullOrEmpty(p.presetKey))
                                       .Select(p => p.presetKey).ToArray();
            Assert.That(_validKeys, Is.Not.Empty, "LightPresetCatalog declares no presets");
        }

        [Test]
        public void EveryLitTemplateNamesAKeyTheCatalogDefines()
        {
            var unknown = new List<string>();
            foreach (BuildingTemplateData t in _byPath.Values)
            {
                if (string.IsNullOrEmpty(t.lightPresetKey)) continue;
                if (Array.IndexOf(_validKeys, t.lightPresetKey) < 0)
                    unknown.Add($"{t.assetPath}: '{t.lightPresetKey}'");
            }

            Assert.That(unknown, Is.Empty,
                $"Templates naming a preset outside [{string.Join(", ", _validKeys)}] — these " +
                "import cleanly and then light nothing: " + string.Join(", ", unknown.Take(10)));
        }

        [Test]
        public void EveryManifestEntryThatDeclaresALight_ProducedALitTemplate()
        {
            var missing = new List<string>();
            foreach (Entry e in _entries)
            {
                if (string.IsNullOrEmpty(e.lightPresetKey)) continue;
                if (!_byPath.TryGetValue(e.resourcePath, out BuildingTemplateData t)) continue;

                if (t.lightPresetKey != e.lightPresetKey)
                    missing.Add($"{e.resourcePath}: template '{t.lightPresetKey}' != manifest '{e.lightPresetKey}'");
            }

            Assert.That(missing, Is.Empty,
                "Fixtures that lost their light — re-run 'Valkur/Buildings/Import Prop Sprites (Apply)': " +
                string.Join(", ", missing.Take(10)));
        }

        [Test]
        public void HandAuthoredLightsSurviveAReImport()
        {
            // The first wave's manifest carries no light data at all. Those templates must
            // still be lit, which is only true because the importer skips the light fields
            // for an entry with an empty key instead of clearing them.
            var manifestKeys = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Entry e in _entries) manifestKeys[e.resourcePath ?? ""] = e.lightPresetKey ?? "";

            int handAuthored = 0;
            foreach (BuildingTemplateData t in _byPath.Values)
            {
                if (string.IsNullOrEmpty(t.lightPresetKey)) continue;
                if (manifestKeys.TryGetValue(t.assetPath, out string fromManifest) &&
                    string.IsNullOrEmpty(fromManifest))
                    handAuthored++;
            }

            Assert.That(handAuthored, Is.GreaterThan(0),
                "No template is lit without its manifest saying so, which means the " +
                "hand-authored fixtures were wiped by an import.");
        }

        [Test]
        public void EveryLitTemplatePutsItsFlameInsideItsOwnBounds()
        {
            var outside = new List<string>();
            foreach (BuildingTemplateData t in _byPath.Values)
            {
                if (string.IsNullOrEmpty(t.lightPresetKey)) continue;

                Vector2 o = t.lightOffsetNormalized;
                // Normalized to the building's bounds. A light at 0 sits on the ground line
                // and lights the floor rather than the lamp; outside [0,1] it detaches from
                // the fixture entirely.
                if (o.x < 0f || o.x > 1f || o.y < 0f || o.y > 1f)
                    outside.Add($"{t.assetPath}: {o}");
            }

            Assert.That(outside, Is.Empty,
                "Light offsets outside the building's own bounds: " + string.Join(", ", outside.Take(10)));
        }

        [Test]
        public void NoTemplateDeclaresALitSpriteThatDoesNotExist()
        {
            var broken = new List<string>();
            foreach (BuildingTemplateData t in _byPath.Values)
            {
                if (string.IsNullOrEmpty(t.litAssetPath)) continue;
                if (Resources.Load<Sprite>(t.litAssetPath) == null)
                    broken.Add($"{t.assetPath}: litAssetPath '{t.litAssetPath}'");
            }

            // The swap happens at dusk, so a missing lit sprite is invisible until then.
            Assert.That(broken, Is.Empty,
                "Templates whose night-time sprite does not resolve: " + string.Join(", ", broken.Take(10)));
        }
    }
}
