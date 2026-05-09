using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data.Dungeon.Udemy;
using Valkur.Gameplay.World.Dungeon.Udemy.Builder;

namespace Valkur.Tests.EditMode.Game.World.Dungeon.Udemy.Builder
{
    public class DoorwayMatcherTests
    {
        [Test]
        public void GetOppositeDoorway_NorthFindsSouth()
        {
            var parent = new Doorway { orientation = Orientation.North };
            var list = new List<Doorway>
            {
                new Doorway { orientation = Orientation.East },
                new Doorway { orientation = Orientation.South },
                new Doorway { orientation = Orientation.West },
            };
            Assert.AreSame(list[1], DoorwayMatcher.GetOppositeDoorway(parent, list));
        }

        [Test]
        public void GetOppositeDoorway_NoMatch_ReturnsNull()
        {
            var parent = new Doorway { orientation = Orientation.North };
            var list = new List<Doorway>
            {
                new Doorway { orientation = Orientation.North },
                new Doorway { orientation = Orientation.East },
            };
            Assert.IsNull(DoorwayMatcher.GetOppositeDoorway(parent, list));
        }

        [Test]
        public void GetOppositeDoorway_NullArgs_ReturnNull()
        {
            Assert.IsNull(DoorwayMatcher.GetOppositeDoorway(null, new List<Doorway>()));
            Assert.IsNull(DoorwayMatcher.GetOppositeDoorway(
                new Doorway { orientation = Orientation.North }, null));
        }

        [TestCase(Orientation.North, 0, -1)]
        [TestCase(Orientation.East, -1, 0)]
        [TestCase(Orientation.South, 0, 1)]
        [TestCase(Orientation.West, 1, 0)]
        [TestCase(Orientation.None, 0, 0)]
        public void GetAdjacencyAdjustment_MatchesUdemyTable(Orientation o, int expectedX, int expectedY)
        {
            var adj = DoorwayMatcher.GetAdjacencyAdjustment(o);
            Assert.AreEqual(new Vector2Int(expectedX, expectedY), adj);
        }

        [Test]
        public void ComputeChildLowerBounds_NorthSouthAlignment_PlacesChildBelowParent()
        {
            // Parent: world (10..15, 10..15), template (0..5, 0..5).
            // Parent's south doorway at template (3, 0). Parent world position of doorway = (13, 10).
            // Child's north doorway at template (3, 5), template extents (0..5, 0..5).
            // Adjustment for north (child entering from above) = (0, -1).
            // Expected child.lowerBounds = (13, 10) + (0, -1) + (0, 0) - (3, 5) = (10, 4).
            var parentLower = new Vector2Int(10, 10);
            var parentTplLower = Vector2Int.zero;
            var parentDoorway = new Doorway { orientation = Orientation.South, position = new Vector2Int(3, 0) };
            var childTplLower = Vector2Int.zero;
            var childDoorway = new Doorway { orientation = Orientation.North, position = new Vector2Int(3, 5) };

            var lb = DoorwayMatcher.ComputeChildLowerBounds(
                parentLower, parentTplLower, parentDoorway, childTplLower, childDoorway);

            Assert.AreEqual(new Vector2Int(10, 4), lb);
        }

        [Test]
        public void RoomsOverlap_TouchingByOneTile_CountsAsOverlap()
        {
            // Udemy uses inclusive AABB (Mathf.Max <= Mathf.Min). Two rooms whose
            // upper edge of A == lower edge of B share one tile and count as overlap.
            Assert.IsTrue(DoorwayMatcher.RoomsOverlap(
                new Vector2Int(0, 0), new Vector2Int(5, 5),
                new Vector2Int(5, 5), new Vector2Int(10, 10)));
        }

        [Test]
        public void RoomsOverlap_DisjointReturnsFalse()
        {
            Assert.IsFalse(DoorwayMatcher.RoomsOverlap(
                new Vector2Int(0, 0), new Vector2Int(4, 4),
                new Vector2Int(5, 5), new Vector2Int(10, 10)));
        }
    }
}
