using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World.Buildings
{
    /// <summary>
    /// Pins <see cref="BuildingDoorGeometry"/>, the single owner of "where is the doorway".
    ///
    /// The properties asserted here are the reason the anchor is normalized rather than a
    /// glyph in the collision matrix: a fraction of the bounds survives a scale override by
    /// construction, where a grid cell is erased by
    /// <c>BuildingCollisionLoader.ResampleGrid</c> the moment the instance is resized.
    /// <see cref="ScaleInvariance_SameAnchorOnADoubledBuilding_LandsAtTheSameRelativeSpot"/>
    /// is that claim, made falsifiable.
    /// </summary>
    [TestFixture]
    public class BuildingDoorGeometryTests
    {
        private static readonly Rect Building = new Rect(10f, 5f, 4f, 6f); // 4 x 6 world units

        // ── Guards ──────────────────────────────────────────────────────────────

        [Test]
        public void DegenerateBuildingRect_IsRefused()
        {
            Assert.IsFalse(BuildingDoorGeometry.TryGetDoorRect(
                new Rect(0f, 0f, 0f, 5f), new Vector2(0.5f, 0f), new Vector2(0.2f, 0.2f), out _),
                "Zero width must be refused — that is the state a building is in before its " +
                "renderers exist, and it means 'not ready', not 'no door'.");

            Assert.IsFalse(BuildingDoorGeometry.TryGetDoorRect(
                new Rect(0f, 0f, 5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.2f, 0.2f), out _),
                "Zero height must be refused for the same reason.");
        }

        // ── Placement ───────────────────────────────────────────────────────────

        [Test]
        public void CentredAnchor_PutsTheDoorwayInTheMiddleOfTheBuilding()
        {
            Assert.IsTrue(BuildingDoorGeometry.TryGetDoorRect(
                Building, new Vector2(0.5f, 0.5f), new Vector2(0.25f, 0.25f), out var door));

            Assert.AreEqual(Building.center.x, door.center.x, 1e-4f, "Doorway centre X.");
            Assert.AreEqual(Building.center.y, door.center.y, 1e-4f, "Doorway centre Y.");
            Assert.AreEqual(1.0f, door.width,  1e-4f, "0.25 of a 4-unit width.");
            Assert.AreEqual(1.5f, door.height, 1e-4f, "0.25 of a 6-unit height.");
        }

        [Test]
        public void GroundLineAnchor_KeepsTheWholeRectInsideTheSprite()
        {
            // (0.5, 0) means "centred on the ground line". Half the doorway must NOT hang
            // below the sprite: the trigger would then sit outside the building the player
            // is supposed to be entering.
            Assert.IsTrue(BuildingDoorGeometry.TryGetDoorRect(
                Building, new Vector2(0.5f, 0f), new Vector2(0.2f, 0.2f), out var door));

            Assert.GreaterOrEqual(door.yMin, Building.yMin - 1e-4f, "Doorway hangs below the sprite.");
            Assert.AreEqual(Building.yMin, door.yMin, 1e-4f,
                "A ground-line anchor should sit flush with the bottom edge.");
        }

        [Test]
        public void ExtremeAnchors_AreClampedInsideTheBuilding()
        {
            foreach (var offset in new[]
                     {
                         new Vector2(0f, 0f), new Vector2(1f, 1f),
                         new Vector2(-5f, -5f), new Vector2(5f, 5f),
                     })
            {
                Assert.IsTrue(BuildingDoorGeometry.TryGetDoorRect(
                    Building, offset, new Vector2(0.3f, 0.3f), out var door),
                    $"Offset {offset} should still resolve.");

                Assert.GreaterOrEqual(door.xMin, Building.xMin - 1e-4f, $"xMin escaped at {offset}.");
                Assert.GreaterOrEqual(door.yMin, Building.yMin - 1e-4f, $"yMin escaped at {offset}.");
                Assert.LessOrEqual(door.xMax, Building.xMax + 1e-4f, $"xMax escaped at {offset}.");
                Assert.LessOrEqual(door.yMax, Building.yMax + 1e-4f, $"yMax escaped at {offset}.");
            }
        }

        // ── Size ────────────────────────────────────────────────────────────────

        [Test]
        public void TinyNormalizedSize_IsRaisedToTheMinimumExtent()
        {
            // 0.001 of 4 units is 4 mm — the player brushes past without ever touching the
            // trigger, and the door reads as broken rather than as small.
            Assert.IsTrue(BuildingDoorGeometry.TryGetDoorRect(
                Building, new Vector2(0.5f, 0.5f), new Vector2(0.001f, 0.001f), out var door));

            Assert.AreEqual(BuildingDoorGeometry.MIN_DOOR_EXTENT_WORLD, door.width,  1e-4f);
            Assert.AreEqual(BuildingDoorGeometry.MIN_DOOR_EXTENT_WORLD, door.height, 1e-4f);
        }

        [Test]
        public void MinimumExtent_NeverExceedsTheBuildingItself()
        {
            // A prop smaller than the minimum must not have its doorway inflated past its
            // own bounds, or the clamp above would have nothing valid to clamp to.
            var tiny = new Rect(0f, 0f, 0.1f, 0.1f);

            Assert.IsTrue(BuildingDoorGeometry.TryGetDoorRect(
                tiny, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), out var door));

            Assert.AreEqual(tiny.width,  door.width,  1e-4f);
            Assert.AreEqual(tiny.height, door.height, 1e-4f);
        }

        [Test]
        public void OversizedNormalizedSize_IsCappedAtTheWholeBuilding()
        {
            Assert.IsTrue(BuildingDoorGeometry.TryGetDoorRect(
                Building, new Vector2(0.5f, 0.5f), new Vector2(4f, 4f), out var door));

            Assert.AreEqual(Building.width,  door.width,  1e-4f);
            Assert.AreEqual(Building.height, door.height, 1e-4f);
        }

        // ── The property the whole design rests on ──────────────────────────────

        [Test]
        public void ScaleInvariance_SameAnchorOnADoubledBuilding_LandsAtTheSameRelativeSpot()
        {
            var offset = new Vector2(0.35f, 0.1f);
            var size   = new Vector2(0.2f, 0.15f);

            var small = new Rect(0f, 0f, 4f, 6f);
            var big   = new Rect(0f, 0f, 8f, 12f); // the same building with a 2x scale override

            Assert.IsTrue(BuildingDoorGeometry.TryGetDoorRect(small, offset, size, out var doorSmall));
            Assert.IsTrue(BuildingDoorGeometry.TryGetDoorRect(big,   offset, size, out var doorBig));

            float uSmall = (doorSmall.center.x - small.xMin) / small.width;
            float uBig   = (doorBig.center.x   - big.xMin)   / big.width;
            float vSmall = (doorSmall.center.y - small.yMin) / small.height;
            float vBig   = (doorBig.center.y   - big.yMin)   / big.height;

            Assert.AreEqual(uSmall, uBig, 1e-4f,
                "The doorway must sit on the same fraction of the width at any scale. If this " +
                "fails the anchor has stopped being scale-free and resized buildings lose their doors.");
            Assert.AreEqual(vSmall, vBig, 1e-4f, "Same, up the height.");
            Assert.AreEqual(2f, doorBig.width / doorSmall.width, 1e-4f, "Width should scale with the building.");
        }

        // ── Exit point ──────────────────────────────────────────────────────────

        [Test]
        public void ExitPoint_SitsBelowTheDoorwayAndOnItsCentreLine()
        {
            var door = new Rect(2f, 3f, 1f, 0.5f);

            var exit = BuildingDoorGeometry.ResolveExitPoint(door, margin: 0.75f);

            Assert.AreEqual(door.center.x, exit.x, 1e-4f, "Exit stays on the doorway's centre line.");
            Assert.AreEqual(door.yMin - 0.75f, exit.y, 1e-4f,
                "Exit must be outside the trigger, or the returning player re-enters it on the " +
                "same frame and bounces straight back inside.");
        }

        [Test]
        public void ExitPoint_NegativeMargin_IsTreatedAsZero()
        {
            var door = new Rect(2f, 3f, 1f, 0.5f);

            var exit = BuildingDoorGeometry.ResolveExitPoint(door, margin: -4f);

            Assert.AreEqual(door.yMin, exit.y, 1e-4f,
                "A negative margin would push the exit INTO the building.");
        }

        // ── Grid cell resolution (drives the blocked-doorway warning) ────────────

        [Test]
        public void DoorCell_BottomCentre_ResolvesToTheBottomRow()
        {
            Assert.IsTrue(BuildingDoorGeometry.TryGetDoorRect(
                Building, new Vector2(0.5f, 0f), new Vector2(0.2f, 0.2f), out var door));

            Assert.IsTrue(BuildingDoorGeometry.TryGetDoorCell(
                Building, door, rows: 4, cols: 4, out int row, out int col));

            Assert.AreEqual(3, row, "Row 0 is the TOP of the grid, matching the authored JSON order.");
            Assert.AreEqual(2, col, "A centred doorway on 4 columns lands on column 2.");
        }

        [Test]
        public void DoorCell_TopLeft_ResolvesToRowZeroColumnZero()
        {
            Assert.IsTrue(BuildingDoorGeometry.TryGetDoorRect(
                Building, new Vector2(0f, 1f), new Vector2(0.05f, 0.05f), out var door));

            Assert.IsTrue(BuildingDoorGeometry.TryGetDoorCell(
                Building, door, rows: 4, cols: 4, out int row, out int col));

            Assert.AreEqual(0, row);
            Assert.AreEqual(0, col);
        }

        [Test]
        public void DoorCell_DegenerateInputs_AreRefused()
        {
            var door = new Rect(11f, 6f, 1f, 1f);

            Assert.IsFalse(BuildingDoorGeometry.TryGetDoorCell(Building, door, 0, 4, out _, out _));
            Assert.IsFalse(BuildingDoorGeometry.TryGetDoorCell(Building, door, 4, 0, out _, out _));
            Assert.IsFalse(BuildingDoorGeometry.TryGetDoorCell(new Rect(0, 0, 0, 0), door, 4, 4, out _, out _));
        }
    }
}
