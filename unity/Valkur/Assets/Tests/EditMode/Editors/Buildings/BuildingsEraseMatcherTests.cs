using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Valkur.Data;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.Buildings
{
    /// <summary>
    /// Tests for <see cref="BuildingsEraseMatcher"/> — the pure-static helper
    /// behind the Buildings Editor Erase tool's scope filters.
    ///
    /// Two scopes:
    ///   - Zone:        match by (templateId, ZoneName) — OrdinalIgnoreCase.
    ///   - TilesArea:   match by templateId AND tilemap.WorldToCell(b.position) ∈ areaCells.
    ///
    /// Tests build minimal scenes with bare GameObject + BuildingObject components.
    /// _template is private/[SerializeField] so we set it via reflection. ZoneName has
    /// a public setter and is wired directly.
    /// </summary>
    [TestFixture]
    public class BuildingsEraseMatcherTests
    {
        private readonly List<GameObject>       _sceneObjects = new List<GameObject>();
        private readonly List<ScriptableObject> _assets       = new List<ScriptableObject>();

        private static readonly FieldInfo s_templateField =
            typeof(BuildingObject).GetField("_template",
                BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp()
        {
            // Suppress sprite-load warnings in case BuildingObject lifecycle hooks
            // try to render something during enable.
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _sceneObjects.Clear();

            foreach (var so in _assets)
                if (so != null) UnityEngine.Object.DestroyImmediate(so);
            _assets.Clear();
        }

        // ── Factories ─────────────────────────────────────────────────────────────

        private BuildingTemplateData CreateTemplate(int templateId)
        {
            var t = ScriptableObject.CreateInstance<BuildingTemplateData>();
            t.templateId    = templateId;
            t.originalScale = new Vector2Int(32, 32);
            t.splitRatio    = 0.5f;
            _assets.Add(t);
            return t;
        }

        private BuildingObject CreateBuilding(BuildingTemplateData template, string zone, Vector3 worldPos)
        {
            var go = new GameObject($"B_{template.templateId}_{zone}");
            go.transform.position = worldPos;
            _sceneObjects.Add(go);
            var b = go.AddComponent<BuildingObject>();
            s_templateField.SetValue(b, template);
            b.ZoneName  = zone;
            b.InstanceId = _sceneObjects.Count;
            return b;
        }

        private Tilemap CreateTilemap()
        {
            var gridGo = new GameObject("Grid");
            _sceneObjects.Add(gridGo);
            var grid = gridGo.AddComponent<Grid>();
            grid.cellSize = Vector3.one;
            var tmGo = new GameObject("Tilemap");
            tmGo.transform.SetParent(gridGo.transform, false);
            _sceneObjects.Add(tmGo);
            return tmGo.AddComponent<Tilemap>();
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  Zone scope
        // ═══════════════════════════════════════════════════════════════════════════

        [Test]
        public void MatchesByZone_ReturnsOnlySameTemplateAndZone()
        {
            var tA = CreateTemplate(1);
            var tB = CreateTemplate(2);
            var bAa1 = CreateBuilding(tA, "zoneA", Vector3.zero);
            var bAa2 = CreateBuilding(tA, "zoneA", new Vector3(2, 2, 0));
            var bAb  = CreateBuilding(tA, "zoneB", new Vector3(4, 4, 0));
            var bBa  = CreateBuilding(tB, "zoneA", new Vector3(6, 6, 0));

            var all = new List<BuildingObject> { bAa1, bAa2, bAb, bBa };
            var result = BuildingsEraseMatcher.MatchesByZone(all, templateId: 1, zoneId: "zoneA");

            Assert.AreEqual(2, result.Count);
            Assert.Contains(bAa1, result);
            Assert.Contains(bAa2, result);
        }

        [Test]
        public void MatchesByZone_DifferentZone_ReturnsEmpty()
        {
            var tA = CreateTemplate(1);
            var b1 = CreateBuilding(tA, "zoneA", Vector3.zero);
            var b2 = CreateBuilding(tA, "zoneA", new Vector3(2, 2, 0));

            var all = new List<BuildingObject> { b1, b2 };
            var result = BuildingsEraseMatcher.MatchesByZone(all, templateId: 1, zoneId: "zoneB");

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void MatchesByZone_DifferentTemplate_ReturnsEmpty()
        {
            var tA = CreateTemplate(1);
            var b1 = CreateBuilding(tA, "zoneA", Vector3.zero);
            var b2 = CreateBuilding(tA, "zoneA", new Vector3(2, 2, 0));

            var all = new List<BuildingObject> { b1, b2 };
            var result = BuildingsEraseMatcher.MatchesByZone(all, templateId: 99, zoneId: "zoneA");

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void MatchesByZone_OrdinalIgnoreCase_MatchesDifferentCasing()
        {
            var tA = CreateTemplate(1);
            var b1 = CreateBuilding(tA, "zone_A", Vector3.zero);

            var all = new List<BuildingObject> { b1 };
            var result = BuildingsEraseMatcher.MatchesByZone(all, templateId: 1, zoneId: "ZONE_A");

            Assert.AreEqual(1, result.Count, "Zone names must compare OrdinalIgnoreCase per CLAUDE.md.");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        //  TilesArea scope
        // ═══════════════════════════════════════════════════════════════════════════

        [Test]
        public void MatchesByTilesArea_ReturnsOnlyBuildingsInsideAreaWithSameTemplate()
        {
            var tilemap = CreateTilemap();
            var tA = CreateTemplate(1);

            // Cells (0,0) and (1,0) are inside the area; (5,5) is outside.
            // A building at world (0.5, 0.5, 0) → cell (0,0). At (1.5, 0.5) → (1,0). At (5.5, 5.5) → (5,5).
            var bIn1 = CreateBuilding(tA, "z", new Vector3(0.5f, 0.5f, 0f));
            var bIn2 = CreateBuilding(tA, "z", new Vector3(1.5f, 0.5f, 0f));
            var bOut = CreateBuilding(tA, "z", new Vector3(5.5f, 5.5f, 0f));

            var area = new HashSet<Vector3Int>
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 0, 0),
            };
            var all = new List<BuildingObject> { bIn1, bIn2, bOut };

            var result = BuildingsEraseMatcher.MatchesByTilesArea(all, templateId: 1, area, tilemap);

            Assert.AreEqual(2, result.Count);
            Assert.Contains(bIn1, result);
            Assert.Contains(bIn2, result);
        }

        [Test]
        public void MatchesByTilesArea_BuildingOutsideArea_NotMatched()
        {
            var tilemap = CreateTilemap();
            var tA = CreateTemplate(1);
            var bOut = CreateBuilding(tA, "z", new Vector3(5.5f, 5.5f, 0f));

            var area = new HashSet<Vector3Int> { new Vector3Int(0, 0, 0) };
            var all  = new List<BuildingObject> { bOut };

            var result = BuildingsEraseMatcher.MatchesByTilesArea(all, templateId: 1, area, tilemap);

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void MatchesByTilesArea_DifferentTemplate_NotMatched()
        {
            var tilemap = CreateTilemap();
            var tA = CreateTemplate(1);
            var bIn = CreateBuilding(tA, "z", new Vector3(0.5f, 0.5f, 0f));

            var area = new HashSet<Vector3Int> { new Vector3Int(0, 0, 0) };
            var all  = new List<BuildingObject> { bIn };

            var result = BuildingsEraseMatcher.MatchesByTilesArea(all, templateId: 99, area, tilemap);

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void MatchesByTilesArea_NullArea_ReturnsEmpty()
        {
            var tilemap = CreateTilemap();
            var tA = CreateTemplate(1);
            var b = CreateBuilding(tA, "z", Vector3.zero);

            var result = BuildingsEraseMatcher.MatchesByTilesArea(new List<BuildingObject> { b },
                templateId: 1, areaCells: null, tilemap);

            Assert.AreEqual(0, result.Count);
        }
    }
}
