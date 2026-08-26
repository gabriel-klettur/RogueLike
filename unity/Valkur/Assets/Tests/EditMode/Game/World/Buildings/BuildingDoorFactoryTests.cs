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
    /// Pins <see cref="BuildingDoorFactory"/> — the rule that a doorway needs BOTH halves.
    ///
    /// The template says the ART has a doorway (true for every placement of house_a); the
    /// instance says where THIS house leads (different for every one of them). Attaching on
    /// either half alone produces one of the two failures this fixture forbids: a trigger
    /// with no destination, which reads as a broken door, or a destination with nowhere on
    /// the sprite to put it, which is an authoring mistake that must be reported rather than
    /// swallowed.
    /// </summary>
    [TestFixture]
    public class BuildingDoorFactoryTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<Object> _assets = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;

            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();

            foreach (var a in _assets)
                if (a != null) Object.DestroyImmediate(a);
            _assets.Clear();
        }

        // ── Fixtures ────────────────────────────────────────────────────────────

        private BuildingTemplateData MakeTemplate(bool hasDoor,
                                                  Vector2Int originalScale,
                                                  Vector2 offset,
                                                  Vector2 size)
        {
            var t = ScriptableObject.CreateInstance<BuildingTemplateData>();
            t.name                 = "TestTemplate";
            t.templateId           = 1;
            t.originalScale        = originalScale;
            t.hasDoor              = hasDoor;
            t.doorOffsetNormalized = offset;
            t.doorSizeNormalized   = size;
            _assets.Add(t);
            return t;
        }

        /// <summary>
        /// A BuildingObject whose bounds come from the template fallback in
        /// TryGetWorldRect — no sprites, so no Resources loads and no renderer-material
        /// leak warnings in EditMode.
        /// </summary>
        private BuildingObject MakeBuilding(BuildingTemplateData template, Vector3 position)
        {
            var go = new GameObject("TestBuilding");
            go.transform.position = position;
            _spawned.Add(go);

            var b = go.AddComponent<BuildingObject>();
            typeof(BuildingObject)
                .GetField("_template", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(b, template);
            b.InstanceId = 42;
            return b;
        }

        private static BuildingDoorSpec ValidSpec() => new BuildingDoorSpec
        {
            target = "house_a_int.overlay.json",
            spawnX = 25f,
            spawnY = 4f,
        };

        // ── Attachment rules ────────────────────────────────────────────────────

        [Test]
        public void TemplateWithoutDoorway_PlusADestination_IsRefusedAndReported()
        {
            var b = MakeBuilding(MakeTemplate(false, new Vector2Int(128, 192),
                                              new Vector2(0.5f, 0.05f), new Vector2(0.25f, 0.2f)),
                                 Vector3.zero);

            LogAssert.Expect(LogType.Warning, new Regex("does not.*declare a doorway"));

            var door = BuildingDoorFactory.TryAttach(b, ValidSpec());

            Assert.IsNull(door, "There is nowhere on the art to put the trigger.");
            Assert.IsNull(BuildingDoorFactory.Find(b));
        }

        [Test]
        public void DoorwayWithoutADestination_AttachesNothingAndStaysSilent()
        {
            var b = MakeBuilding(MakeTemplate(true, new Vector2Int(128, 192),
                                              new Vector2(0.5f, 0.05f), new Vector2(0.25f, 0.2f)),
                                 Vector3.zero);

            // The resting state of every un-assigned house. Not a warning.
            Assert.IsNull(BuildingDoorFactory.TryAttach(b, null));
            Assert.IsNull(BuildingDoorFactory.TryAttach(b, new BuildingDoorSpec { target = "  " }));
            Assert.IsNull(BuildingDoorFactory.Find(b));
            Assert.IsNull(b.DoorSpec);
            Assert.IsFalse(b.HasUsableDoor);
        }

        [Test]
        public void BothHalvesPresent_AttachesATriggerChildUnderTheBuilding()
        {
            var b = MakeBuilding(MakeTemplate(true, new Vector2Int(128, 192),
                                              new Vector2(0.5f, 0.05f), new Vector2(0.25f, 0.2f)),
                                 new Vector3(10f, 5f, 0f));

            var door = BuildingDoorFactory.TryAttach(b, ValidSpec());

            Assert.IsNotNull(door);
            Assert.AreEqual(BuildingDoor.CHILD_NAME, door.gameObject.name);
            Assert.AreSame(b.transform, door.transform.parent,
                "The doorway must be a CHILD so it follows the building when F10 drags it.");
            Assert.AreSame(b, door.Owner);
            Assert.IsTrue(b.HasUsableDoor);

            Assert.IsNull(door.GetComponent<Collider2D>(),
                "Detection is a poll, not a trigger — buildings carry no Rigidbody2D and a player " +
                "body that has gone to sleep starts no new contacts. A stray collider here would " +
                "either block the doorway or add a second, racing detection path.");
        }

        [Test]
        public void AttachedSpec_IsACopy_NotTheCallersInstance()
        {
            var b = MakeBuilding(MakeTemplate(true, new Vector2Int(128, 192),
                                              new Vector2(0.5f, 0.05f), new Vector2(0.25f, 0.2f)),
                                 Vector3.zero);

            var input = ValidSpec();
            BuildingDoorFactory.TryAttach(b, input);
            input.target = "somewhere_else.overlay.json";

            Assert.AreEqual("house_a_int.overlay.json", b.DoorSpec.target,
                "Mutating the parsed record afterwards must not re-aim a live door.");
        }

        [Test]
        public void AttachingTwice_ReusesTheSameChild()
        {
            var b = MakeBuilding(MakeTemplate(true, new Vector2Int(128, 192),
                                              new Vector2(0.5f, 0.05f), new Vector2(0.25f, 0.2f)),
                                 Vector3.zero);

            var first  = BuildingDoorFactory.TryAttach(b, ValidSpec());
            var second = BuildingDoorFactory.TryAttach(b, new BuildingDoorSpec { target = "b.overlay.json" });

            Assert.AreSame(first, second, "Re-applying must re-configure, not duplicate — the F10 " +
                                          "live re-apply path calls this on every edit.");
            Assert.AreEqual(1, b.GetComponentsInChildren<BuildingDoor>(true).Length);
            Assert.AreEqual("b.overlay.json", second.TargetOverlay);
        }

        [Test]
        public void ClearingTheDestination_RemovesAnAlreadyAttachedDoorway()
        {
            var b = MakeBuilding(MakeTemplate(true, new Vector2Int(128, 192),
                                              new Vector2(0.5f, 0.05f), new Vector2(0.25f, 0.2f)),
                                 Vector3.zero);
            BuildingDoorFactory.TryAttach(b, ValidSpec());

            BuildingDoorFactory.TryAttach(b, null);

            Assert.IsNull(BuildingDoorFactory.Find(b), "A door that no longer leads anywhere must go.");
            Assert.IsNull(b.DoorSpec);
        }

        [Test]
        public void Remove_IsSafeOnABuildingWithNoDoorway()
        {
            var b = MakeBuilding(MakeTemplate(true, new Vector2Int(128, 192),
                                              new Vector2(0.5f, 0.05f), new Vector2(0.25f, 0.2f)),
                                 Vector3.zero);

            Assert.DoesNotThrow(() => BuildingDoorFactory.Remove(b));
            Assert.DoesNotThrow(() => BuildingDoorFactory.Remove(null));
            Assert.DoesNotThrow(() => BuildingDoorFactory.RefreshGeometry(b));
        }

        [Test]
        public void NullOwner_IsRefusedWithoutThrowing()
        {
            Assert.IsNull(BuildingDoorFactory.TryAttach(null, ValidSpec()));
        }

        // ── Geometry handed to the trigger ──────────────────────────────────────

        [Test]
        public void DoorwayObject_SitsOnTheCentreOfTheGeometryTheBuildingReports()
        {
            // 128x192 px at 32 PPU = 4 x 6 world units, anchored bottom-centre at (10, 5),
            // so the building rect is (8, 5, 4, 6).
            var b = MakeBuilding(MakeTemplate(true, new Vector2Int(128, 192),
                                              new Vector2(0.5f, 0.05f), new Vector2(0.25f, 0.2f)),
                                 new Vector3(10f, 5f, 0f));

            var door = BuildingDoorFactory.TryAttach(b, ValidSpec());

            Assert.IsTrue(b.TryGetDoorWorldRect(out var expected));
            Assert.AreEqual(expected.center.x, door.transform.position.x, 1e-3f, "Doorway centre X.");
            Assert.AreEqual(expected.center.y, door.transform.position.y, 1e-3f, "Doorway centre Y.");
        }

        [Test]
        public void EntryRect_IsTheDoorwayPlusSlack_OnEverySide()
        {
            var b = MakeBuilding(MakeTemplate(true, new Vector2Int(128, 192),
                                              new Vector2(0.5f, 0.05f), new Vector2(0.25f, 0.2f)),
                                 new Vector3(10f, 5f, 0f));

            var door = BuildingDoorFactory.TryAttach(b, ValidSpec());

            Assert.IsTrue(b.TryGetDoorWorldRect(out var drawn));
            Assert.IsTrue(door.TryGetEntryRect(out var entry));

            float p = BuildingDoor.ENTRY_PADDING_WORLD;
            Assert.AreEqual(drawn.xMin - p, entry.xMin, 1e-3f);
            Assert.AreEqual(drawn.yMin - p, entry.yMin, 1e-3f);
            Assert.AreEqual(drawn.width  + 2f * p, entry.width,  1e-3f);
            Assert.AreEqual(drawn.height + 2f * p, entry.height, 1e-3f);
            Assert.AreEqual(drawn.center, entry.center,
                "The slack must be symmetric — an off-centre entry rect makes the doorway " +
                "fire from a place the author never drew.");
        }

        [Test]
        public void NonUniformParentScale_DoesNotMoveTheDoorwayOffItsGeometry()
        {
            var b = MakeBuilding(MakeTemplate(true, new Vector2Int(128, 192),
                                              new Vector2(0.5f, 0.05f), new Vector2(0.25f, 0.2f)),
                                 new Vector3(10f, 5f, 0f));
            b.transform.localScale = new Vector3(2f, 0.5f, 1f);

            var door = BuildingDoorFactory.TryAttach(b, ValidSpec());

            Assert.IsTrue(b.TryGetDoorWorldRect(out var expected));
            Assert.AreEqual(expected.center.x, door.transform.position.x, 1e-3f,
                "The doorway object is placed in WORLD space, so a scaled parent must not drag it off.");
            Assert.AreEqual(expected.center.y, door.transform.position.y, 1e-3f);
        }

        [Test]
        public void BuildingWithNoResolvableBounds_ReportsInsteadOfAttachingASizelessTrigger()
        {
            // originalScale (0,0) and no renderers: TryGetWorldRect has nothing to work from.
            var b = MakeBuilding(MakeTemplate(true, Vector2Int.zero,
                                              new Vector2(0.5f, 0.05f), new Vector2(0.25f, 0.2f)),
                                 Vector3.zero);

            LogAssert.Expect(LogType.Warning, new Regex("world bounds are not resolvable"));

            var door = BuildingDoorFactory.TryAttach(b, ValidSpec());

            Assert.IsNotNull(door, "The doorway is still attached — it just has nowhere to sit yet.");
            Assert.IsFalse(door.TryGetWorldRect(out _));
            Assert.IsFalse(door.TryGetEntryRect(out _));
        }
    }
}
