using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World
{
    [TestFixture]
    public class BuildingColliderDebugOverlayTests
    {
        [Test]
        public void SetVisible_WithRootCollider_CreatesOneVisual()
        {
            var buildingGo = new GameObject("Building");
            buildingGo.AddComponent<BoxCollider2D>().size = new Vector2(2f, 1f);
            var overlay = buildingGo.AddComponent<BuildingColliderDebugOverlay>();

            overlay.SetVisible(true);

            Assert.AreEqual(1, overlay.CurrentVisualCount,
                "One enabled BoxCollider2D should produce one debug visual.");

            Object.DestroyImmediate(buildingGo);
        }

        [Test]
        public void SetVisible_WithChildTileCollider_CreatesVisualForTile()
        {
            var buildingGo = new GameObject("Building");
            var rootCollider = buildingGo.AddComponent<BoxCollider2D>();
            rootCollider.enabled = false;

            var tileGo = new GameObject("CollTile_0_0");
            tileGo.transform.SetParent(buildingGo.transform, worldPositionStays: false);
            tileGo.AddComponent<BoxCollider2D>().size = new Vector2(1f, 1f);

            var overlay = buildingGo.AddComponent<BuildingColliderDebugOverlay>();
            overlay.SetVisible(true);

            Assert.AreEqual(1, overlay.CurrentVisualCount,
                "A tiled building collider should be visualized even when the root collider is disabled.");

            Object.DestroyImmediate(buildingGo);
        }
    }
}
