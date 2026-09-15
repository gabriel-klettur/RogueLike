using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Gameplay.World.Buildings
{
    /// <summary>
    /// Pins the trunk box: the pure conversion from the normalized rect drawn on the art to world
    /// space, and that a tree's interaction and impact bounds are the TRUNK when one is drawn,
    /// the footprint when not, and never the trunk while the stump is showing.
    /// </summary>
    [TestFixture]
    public class BuildingTrunkGeometryTests
    {
        private readonly List<Object> _made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _made) if (o != null) Object.DestroyImmediate(o);
            _made.Clear();
        }

        [Test]
        public void TheBox_IsAFractionOfTheBuilding_FromItsBottomLeft()
        {
            var building = new Rect(10f, 5f, 4f, 8f);
            Assert.IsTrue(BuildingTrunkGeometry.TryGetTrunkRect(building,
                Rect.MinMaxRect(0.25f, 0.1f, 0.75f, 0.5f), out var trunk));

            Assert.AreEqual(11f, trunk.xMin, 1e-4f);
            Assert.AreEqual(13f, trunk.xMax, 1e-4f);
            Assert.AreEqual(5.8f, trunk.yMin, 1e-4f);
            Assert.AreEqual(9f, trunk.yMax, 1e-4f);
        }

        [Test]
        public void ASliver_IsWidenedToSomethingABlowCanLandOn_AroundItsOwnCentre()
        {
            var building = new Rect(0f, 0f, 4f, 4f);
            Assert.IsTrue(BuildingTrunkGeometry.TryGetTrunkRect(building,
                Rect.MinMaxRect(0.49f, 0.1f, 0.51f, 0.5f), out var trunk));

            Assert.AreEqual(BuildingTrunkGeometry.MIN_TRUNK_EXTENT_WORLD, trunk.width, 1e-4f);
            Assert.AreEqual(2f, trunk.center.x, 1e-4f);
        }

        [Test]
        public void TheBox_NeverLeavesTheSprite()
        {
            var building = new Rect(0f, 0f, 2f, 2f);
            Assert.IsTrue(BuildingTrunkGeometry.TryGetTrunkRect(building,
                Rect.MinMaxRect(-0.3f, -0.2f, 0.05f, 1.4f), out var trunk));

            Assert.GreaterOrEqual(trunk.xMin, building.xMin - 1e-4f);
            Assert.GreaterOrEqual(trunk.yMin, building.yMin - 1e-4f);
            Assert.LessOrEqual(trunk.yMax, building.yMax + 1e-4f);
        }

        [Test]
        public void NoArea_OrNoBuilding_IsNoTrunk()
        {
            Assert.IsFalse(BuildingTrunkGeometry.TryGetTrunkRect(new Rect(0, 0, 2, 2), new Rect(0.4f, 0.1f, 0f, 0.3f), out _));
            Assert.IsFalse(BuildingTrunkGeometry.TryGetTrunkRect(new Rect(0, 0, 0, 2), Rect.MinMaxRect(0.4f, 0.1f, 0.6f, 0.4f), out _));
        }

        // ── Wired into the node ──────────────────────────────────────────────────────

        private (BuildingObject building, HarvestNode node, BuildingTemplateData template) Tree(Rect trunk)
        {
            var template = ScriptableObject.CreateInstance<BuildingTemplateData>();
            template.originalScale = new Vector2Int(160, 320);   // 5 x 10 world units at 32 PPU
            template.trunkNormalized = trunk;
            _made.Add(template);

            var go = new GameObject("Tree");
            go.transform.position = new Vector3(20f, 3f, 0f);
            _made.Add(go);

            var building = go.AddComponent<BuildingObject>();
            typeof(BuildingObject).GetField("_template", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(building, template);

            // A null profile stops Initialize before it registers anywhere, which is all a
            // bounds test needs and keeps the interactable registry untouched.
            var node = go.AddComponent<HarvestNode>();
            node.Initialize(null, building, null);
            return (building, node, template);
        }

        [Test]
        public void ANodesInteractionBounds_AreTheDrawnTrunk()
        {
            var (_, node, _) = Tree(Rect.MinMaxRect(0.4f, 0.05f, 0.6f, 0.4f));
            var b = node.InteractionBounds;

            // The building rect is x 17.5..22.5, y 3..13.
            Assert.AreEqual(19.5f, b.min.x, 1e-3f);
            Assert.AreEqual(20.5f, b.max.x, 1e-3f);
            Assert.AreEqual(3.5f, b.min.y, 1e-3f);
            Assert.AreEqual(7f, b.max.y, 1e-3f);
        }

        [Test]
        public void ArtWithNoTrunkDrawn_ReportsNone_SoTheFootprintStillAnswers()
        {
            var (building, _, _) = Tree(new Rect(0f, 0f, 0f, 0f));
            Assert.IsFalse(building.TryGetTrunkBounds(out _));
        }

        [Test]
        public void WhileTheStumpShows_TheTreesTrunkBoxIsNotUsed()
        {
            var (building, _, _) = Tree(Rect.MinMaxRect(0.4f, 0.05f, 0.6f, 0.4f));
            Assert.IsTrue(building.TryGetTrunkBounds(out _));

            typeof(BuildingObject).GetField("_hasPristineSnapshot", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(building, true);
            Assert.IsFalse(building.TryGetTrunkBounds(out _),
                "the box was drawn on the tree, and a stump is a different sprite");
        }
    }
}
