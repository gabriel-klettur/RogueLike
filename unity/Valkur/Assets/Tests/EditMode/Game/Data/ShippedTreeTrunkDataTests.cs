using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Editor.Buildings;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Pins the hand-drawn trunk boxes: the manifest is well formed, every tree the woodcutting
    /// layer can fell has one, and the shipped templates carry exactly what the manifest says.
    ///
    /// <para>A tree without a box does not fail anywhere — it silently falls back to its
    /// footprint and takes blows on its roots again — which is why the coverage is asserted
    /// rather than trusted.</para>
    /// </summary>
    [TestFixture]
    public class ShippedTreeTrunkDataTests
    {
        private static Dictionary<string, Rect> Boxes()
        {
            var boxes = TreeTrunkBoxApplier.LoadByAssetPath();
            Assert.That(boxes.Count, Is.GreaterThanOrEqualTo(428), "tree_trunks.json lost boxes.");
            return boxes;
        }

        [Test]
        public void EveryBox_IsInsideTheSprite_AndStandsOnTheLowerHalf()
        {
            foreach (var kv in Boxes())
            {
                var r = kv.Value;
                Assert.That(r.xMin, Is.InRange(0f, 1f), kv.Key);
                Assert.That(r.xMax, Is.InRange(0f, 1f), kv.Key);
                Assert.That(r.yMin, Is.InRange(0f, 1f), kv.Key);
                Assert.That(r.yMax, Is.InRange(0f, 1f), kv.Key);
                Assert.That(r.width, Is.GreaterThan(0.05f), $"{kv.Key}: a trunk narrower than 5 % is a slip of the pen.");
                Assert.That(r.height, Is.GreaterThan(0.1f), kv.Key);
                // A trunk starts near the ground, and the ground can be a ROCK: the coastal bonsais
                // grow out of boulders, and bonsai_coastal_windswept_5's trunk starts at half height.
                Assert.That(r.yMin, Is.LessThanOrEqualTo(0.6f), $"{kv.Key}: a trunk starts near the ground.");
            }
        }

        [Test]
        public void EveryFellableTree_HasItsTrunkDrawn_AndTheTemplateCarriesIt()
        {
            var boxes = Boxes();
            int checkedCount = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:BuildingTemplateData"))
            {
                var t = AssetDatabase.LoadAssetAtPath<BuildingTemplateData>(AssetDatabase.GUIDToAssetPath(guid));
                if (t == null || t.destruction == null || !t.destruction.name.StartsWith("DP_tree_")) continue;

                Assert.That(boxes.TryGetValue(t.assetPath, out var box), Is.True,
                    $"'{t.assetPath}' can be felled and has no trunk drawn in tree_trunks.json.");
                Assert.That(t.HasTrunk, Is.True,
                    $"'{t.assetPath}' (id {t.templateId}) has no trunk: run Valkur > Buildings > Apply Tree Trunk Boxes.");
                Assert.That(t.trunkNormalized.xMin, Is.EqualTo(box.xMin).Within(0.001f), t.assetPath);
                Assert.That(t.trunkNormalized.yMax, Is.EqualTo(box.yMax).Within(0.001f), t.assetPath);
                checkedCount++;
            }
            Assert.That(checkedCount, Is.GreaterThan(500), "The tree templates vanished.");
        }
    }
}
