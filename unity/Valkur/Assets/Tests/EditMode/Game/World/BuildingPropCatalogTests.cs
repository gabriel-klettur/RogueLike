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
    /// Guards the prop sprites imported from the sliced prop sheets
    /// (<c>tools/atlas/slice_prop_sheet.py</c> → <c>build_building_props.py</c> →
    /// <c>BuildingPropImporter</c>).
    ///
    /// The manifest is the contract: it is versioned in the repo, the source sheets are
    /// not. So the invariant these tests defend is "the catalog still says exactly what
    /// the manifest says", which is what breaks when a PNG is renamed by hand, a template
    /// asset is deleted, or the importer is re-run against stale data.
    ///
    /// Scope is deliberately limited to the five prop categories. The ~313 legacy
    /// templates carry known drift from the Python port (three without a preview sprite,
    /// one with a zero originalScale, several sharing one image on purpose) and are not
    /// this suite's business.
    /// </summary>
    [TestFixture]
    public class BuildingPropCatalogTests
    {
        private const string MANIFEST_RELATIVE_PATH = "../../../tools/atlas/generated/building_props_manifest.json";
        private const string CATALOG_PATH = "Assets/_Project/Data/Catalogs/Buildings/BuildingCatalog.asset";
        private const float BUILDING_PPU = 32f;

        /// <summary>Tallest a prop may stand, in tiles. The player is two tiles.</summary>
        private const float MAX_PROP_TILES = 12f;

        private static readonly string[] PropCategories = { "lights", "signs", "market", "props", "nature" };

        [Serializable] private class Manifest { public List<Entry> entries = new List<Entry>(); }

        [Serializable]
        private class Entry
        {
            public string name;
            public string category;
            public string resourcePath;
            public string sourceImagePath;
            public bool solid;
            public float splitRatio;
            public string colliderScope;
            public int width;
            public int height;
        }

        private Manifest _manifest;
        private BuildingCatalog _catalog;
        private List<BuildingTemplateData> _propTemplates;

        [OneTimeSetUp]
        public void LoadFixtures()
        {
            string manifestPath = Path.GetFullPath(Path.Combine(Application.dataPath, MANIFEST_RELATIVE_PATH));
            Assert.That(File.Exists(manifestPath), Is.True,
                $"Prop manifest missing at {manifestPath}. Run tools/atlas/build_building_props.py.");
            _manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));

            _catalog = AssetDatabase.LoadAssetAtPath<BuildingCatalog>(CATALOG_PATH);
            Assert.That(_catalog, Is.Not.Null, $"BuildingCatalog missing at {CATALOG_PATH}");

            _propTemplates = _catalog.Templates
                .Where(t => t != null && IsPropTemplate(t.assetPath))
                .ToList();
        }

        private static bool IsPropTemplate(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            return PropCategories.Any(c => assetPath.StartsWith("Buildings/" + c + "/", StringComparison.Ordinal));
        }

        [Test]
        public void Manifest_HasEntries()
        {
            Assert.That(_manifest, Is.Not.Null, "Manifest did not deserialize");
            Assert.That(_manifest.entries.Count, Is.GreaterThan(0), "Manifest lists no sprites");
        }

        [Test]
        public void EveryManifestEntry_HasACatalogTemplate()
        {
            var byPath = _propTemplates.ToLookup(t => t.assetPath, StringComparer.Ordinal);
            var missing = _manifest.entries
                .Where(e => !byPath.Contains(e.resourcePath))
                .Select(e => e.resourcePath)
                .ToList();

            Assert.That(missing, Is.Empty,
                "Manifest entries with no template in the catalog — re-run " +
                "'Valkur/Buildings/Import Prop Sprites (Apply)': " + string.Join(", ", missing.Take(10)));
        }

        [Test]
        public void EveryPropTemplate_MatchesItsManifestEntry()
        {
            var byPath = _manifest.entries.ToDictionary(e => e.resourcePath, StringComparer.Ordinal);
            var drift = new List<string>();

            foreach (BuildingTemplateData tpl in _propTemplates)
            {
                if (!byPath.TryGetValue(tpl.assetPath, out Entry e))
                {
                    drift.Add($"{tpl.assetPath}: in catalog but not in the manifest");
                    continue;
                }

                if (tpl.solid != e.solid)
                    drift.Add($"{tpl.assetPath}: solid {tpl.solid} != manifest {e.solid}");
                if (Mathf.Abs(tpl.splitRatio - e.splitRatio) > 0.001f)
                    drift.Add($"{tpl.assetPath}: splitRatio {tpl.splitRatio} != manifest {e.splitRatio}");
                if (tpl.originalScale.x != e.width || tpl.originalScale.y != e.height)
                    drift.Add($"{tpl.assetPath}: originalScale {tpl.originalScale} != manifest {e.width}x{e.height}");
                if (tpl.sourceImagePath != e.sourceImagePath)
                    drift.Add($"{tpl.assetPath}: sourceImagePath '{tpl.sourceImagePath}' != '{e.sourceImagePath}'");
            }

            Assert.That(drift, Is.Empty, string.Join("\n", drift.Take(15)));
        }

        [Test]
        public void EveryPropTemplate_ResolvesItsSprite()
        {
            var broken = _propTemplates
                .Where(t => Resources.Load<Sprite>(t.assetPath) == null)
                .Select(t => $"#{t.templateId} {t.assetPath}")
                .ToList();

            Assert.That(broken, Is.Empty,
                "Templates whose Resources path loads nothing: " + string.Join(", ", broken.Take(10)));
        }

        [Test]
        public void EveryPropTemplate_HasAPreviewSprite()
        {
            var broken = _propTemplates
                .Where(t => t.previewSprite == null)
                .Select(t => $"#{t.templateId} {t.assetPath}")
                .ToList();

            Assert.That(broken, Is.Empty,
                "Templates with no preview sprite (the F10 palette renders them blank): " +
                string.Join(", ", broken.Take(10)));
        }

        [Test]
        public void EveryPropTemplate_OriginalScaleMatchesTheSprite()
        {
            var drift = new List<string>();
            foreach (BuildingTemplateData tpl in _propTemplates)
            {
                Sprite sprite = Resources.Load<Sprite>(tpl.assetPath);
                if (sprite == null) continue;

                int w = Mathf.RoundToInt(sprite.rect.width);
                int h = Mathf.RoundToInt(sprite.rect.height);
                if (tpl.originalScale.x != w || tpl.originalScale.y != h)
                    drift.Add($"#{tpl.templateId} {tpl.assetPath}: template {tpl.originalScale} vs PNG {w}x{h}");
            }

            Assert.That(drift, Is.Empty, string.Join("\n", drift.Take(15)));
        }

        [Test]
        public void EveryPropTemplate_HasSaneAuthoringData()
        {
            var bad = new List<string>();
            foreach (BuildingTemplateData tpl in _propTemplates)
            {
                if (tpl.splitRatio < 0f || tpl.splitRatio > 1f)
                    bad.Add($"#{tpl.templateId} {tpl.assetPath}: splitRatio {tpl.splitRatio}");
                if (tpl.colliderScope != "CG" && tpl.colliderScope != "CU")
                    bad.Add($"#{tpl.templateId} {tpl.assetPath}: colliderScope '{tpl.colliderScope}'");
                if (tpl.originalScale.x <= 0 || tpl.originalScale.y <= 0)
                    bad.Add($"#{tpl.templateId} {tpl.assetPath}: originalScale {tpl.originalScale}");
                else if (tpl.originalScale.y / BUILDING_PPU > MAX_PROP_TILES)
                    bad.Add($"#{tpl.templateId} {tpl.assetPath}: {tpl.originalScale.y / BUILDING_PPU:0.0} tiles tall, " +
                            $"over the {MAX_PROP_TILES}-tile cap");
            }

            Assert.That(bad, Is.Empty, string.Join("\n", bad.Take(15)));
        }

        [Test]
        public void PropTemplates_HaveUniqueIdsAndPaths()
        {
            var dupIds = _propTemplates.GroupBy(t => t.templateId).Where(g => g.Count() > 1)
                .Select(g => $"id {g.Key} x{g.Count()}").ToList();
            var dupPaths = _propTemplates.GroupBy(t => t.assetPath, StringComparer.Ordinal)
                .Where(g => g.Count() > 1).Select(g => $"{g.Key} x{g.Count()}").ToList();

            Assert.That(dupIds, Is.Empty, "Duplicate template ids: " + string.Join(", ", dupIds));
            Assert.That(dupPaths, Is.Empty, "Duplicate asset paths: " + string.Join(", ", dupPaths));
        }

        /// <summary>
        /// End-to-end smoke test: the data is only worth anything if
        /// <see cref="BuildingObject.Apply"/> can actually build a placeable object out of
        /// it. One representative template per category is assembled and checked for a
        /// footprint and a canopy whose heights add back up to the sprite.
        /// </summary>
        [Test]
        public void OnePropPerCategory_AssemblesIntoAPlaceableBuilding()
        {
            foreach (string category in PropCategories)
            {
                BuildingTemplateData tpl = _propTemplates
                    .FirstOrDefault(t => t.assetPath.StartsWith("Buildings/" + category + "/", StringComparison.Ordinal));
                Assert.That(tpl, Is.Not.Null, $"No template found for category '{category}'");

                var go = new GameObject("PropSmokeTest_" + category);
                try
                {
                    var building = go.AddComponent<Valkur.Gameplay.World.BuildingObject>();
                    building.Apply(tpl, Vector2Int.zero, -1f);

                    SpriteRenderer[] renderers = go.GetComponentsInChildren<SpriteRenderer>(true);
                    SpriteRenderer footprint = renderers.FirstOrDefault(r => r.name == "Footprint");
                    SpriteRenderer canopy = renderers.FirstOrDefault(r => r.name == "Canopy");

                    Assert.That(footprint, Is.Not.Null, $"{tpl.assetPath}: no Footprint renderer");
                    Assert.That(canopy, Is.Not.Null, $"{tpl.assetPath}: no Canopy renderer");
                    Assert.That(footprint.sprite, Is.Not.Null, $"{tpl.assetPath}: Footprint has no sprite");
                    Assert.That(canopy.sprite, Is.Not.Null, $"{tpl.assetPath}: Canopy has no sprite");

                    float total = footprint.sprite.rect.height + canopy.sprite.rect.height;
                    float source = Resources.Load<Sprite>(tpl.assetPath).rect.height;
                    Assert.That(total, Is.EqualTo(source).Within(1f),
                        $"{tpl.assetPath}: split halves ({total}px) do not add back up to the sprite ({source}px)");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
        }

        [Test]
        public void EveryPropSpritePath_FollowsTheNamingConvention()
        {
            var offenders = new List<string>();
            foreach (BuildingTemplateData tpl in _propTemplates)
            {
                string file = tpl.assetPath.Substring(tpl.assetPath.LastIndexOf('/') + 1);
                if (file.Any(c => !(char.IsLower(c) || char.IsDigit(c) || c == '_')))
                    offenders.Add($"#{tpl.templateId} {tpl.assetPath}");
            }

            Assert.That(offenders, Is.Empty,
                "Prop sprite names must be lowercase snake_case ASCII: " + string.Join(", ", offenders.Take(10)));
        }
    }
}
