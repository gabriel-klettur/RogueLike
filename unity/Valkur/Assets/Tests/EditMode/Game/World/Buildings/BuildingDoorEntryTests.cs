using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World.Buildings
{
    /// <summary>
    /// Pins what <see cref="BuildingDoor.Enter"/> does when the transition does NOT happen.
    ///
    /// The door records where to come back to BEFORE swapping the world, because once the
    /// overlay changes the building it belongs to no longer exists to be measured. That
    /// ordering creates a failure mode of its own: if the swap is then refused, an armed
    /// return point describes a trip the player never took, and the next exit teleports them
    /// somewhere they have never been. The compensating clear is asserted here.
    ///
    /// The success path needs a live WorldGridBuilder and a real overlay file, so it belongs
    /// to PlayMode; what EditMode can pin is that a failed entry leaves NO residue.
    /// </summary>
    [TestFixture]
    public class BuildingDoorEntryTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<Object> _assets = new List<Object>();

        private static void ResetTransitionStatics()
        {
            typeof(WorldTransitionService)
                .GetMethod("ResetStaticsOnPlayModeEnter", BindingFlags.NonPublic | BindingFlags.Static)
                ?.Invoke(null, null);
        }

        [SetUp]
        public void SetUp() => ResetTransitionStatics();

        [TearDown]
        public void TearDown()
        {
            ResetTransitionStatics();
            LogAssert.ignoreFailingMessages = false;

            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();

            foreach (var a in _assets)
                if (a != null) Object.DestroyImmediate(a);
            _assets.Clear();
        }

        private BuildingDoor MakeDoor(BuildingDoorSpec spec)
        {
            var template = ScriptableObject.CreateInstance<BuildingTemplateData>();
            template.name                 = "DoorTemplate";
            template.templateId           = 1;
            template.originalScale        = new Vector2Int(128, 192);
            template.hasDoor              = true;
            template.doorOffsetNormalized = new Vector2(0.5f, 0.05f);
            template.doorSizeNormalized   = new Vector2(0.25f, 0.2f);
            _assets.Add(template);

            var go = new GameObject("DoorBuilding");
            go.transform.position = new Vector3(10f, 5f, 0f);
            _spawned.Add(go);

            var b = go.AddComponent<BuildingObject>();
            typeof(BuildingObject)
                .GetField("_template", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(b, template);
            b.InstanceId = 7;

            var door = BuildingDoorFactory.TryAttach(b, spec);
            Assert.IsNotNull(door, "Fixture failed to attach a doorway.");
            return door;
        }

        [Test]
        public void DoorWithNoDestination_RefusesToEnterAndSaysSo()
        {
            // Attach a real door, then strip its destination the way an author clearing the
            // field would. Configure(null) is the same state Enter has to survive.
            var door = MakeDoor(new BuildingDoorSpec { target = "x.overlay.json" });
            door.Configure(door.Owner, null);

            LogAssert.Expect(LogType.Warning, new Regex("no destination"));

            Assert.IsFalse(door.Enter(player: null));
            Assert.IsFalse(WorldTransitionService.HasReturnPoint,
                "A door that never fired must not arm a way back.");
        }

        [Test]
        public void RefusedTransition_LeavesNoArmedReturnPoint()
        {
            Assume.That(Object.FindObjectOfType<WorldGridBuilder>() == null,
                "Another fixture left a WorldGridBuilder in the scene; this case needs none.");

            var door = MakeDoor(new BuildingDoorSpec
            {
                target = "house_a_int.overlay.json",
                spawnX = 25f,
                spawnY = 4f,
            });

            LogAssert.Expect(LogType.Error, new Regex("No WorldGridBuilder"));

            bool ok = door.Enter(player: null);

            Assert.IsFalse(ok, "With no grid builder the world cannot be swapped.");
            Assert.IsFalse(WorldTransitionService.HasReturnPoint,
                "The return point was recorded before the swap was attempted. A refused swap has " +
                "to take it back, or the next exit teleports the player to a trip they never took.");
            Assert.AreEqual(string.Empty, WorldTransitionService.CurrentOverlay);
        }

        [Test]
        public void TheRecordedExitPoint_SitsOutsideTheDoorwayItCameThrough()
        {
            // Same derivation the door uses, asserted against the doorway it belongs to: the
            // returning player must not land back inside the trigger and bounce straight in.
            var door = MakeDoor(new BuildingDoorSpec { target = "x.overlay.json" });

            Assert.IsTrue(door.TryGetWorldRect(out var doorRect));
            var exit = BuildingDoorGeometry.ResolveExitPoint(doorRect);

            Assert.IsFalse(doorRect.Contains(exit),
                $"Exit {exit} is inside the doorway rect {doorRect}.");
            Assert.Less(exit.y, doorRect.yMin,
                "Buildings are anchored at their bottom-centre and their footprint is solid, so " +
                "below the doorway is the one direction guaranteed to be outdoors.");
        }

        [Test]
        public void DoorwayFollowsItsBuilding_WhenTheBuildingIsMoved()
        {
            var door = MakeDoor(new BuildingDoorSpec { target = "x.overlay.json" });
            Assert.IsTrue(door.TryGetWorldRect(out var before));

            door.Owner.transform.position += new Vector3(20f, -7f, 0f);
            door.RefreshGeometry();

            Assert.IsTrue(door.TryGetWorldRect(out var after));
            Assert.AreEqual(before.center.x + 20f, after.center.x, 1e-3f);
            Assert.AreEqual(before.center.y - 7f,  after.center.y, 1e-3f);
            Assert.AreEqual(before.center.x, door.transform.position.x - 20f, 1e-3f,
                "The trigger transform must track the doorway, not stay where it was authored.");
        }
    }
}
