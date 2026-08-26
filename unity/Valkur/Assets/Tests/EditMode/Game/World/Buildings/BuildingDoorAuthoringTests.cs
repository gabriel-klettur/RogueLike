using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.World;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Game.World.Buildings
{
    /// <summary>
    /// Pins the authoring seams the F10 Door panel and the <c>door</c> console command both
    /// call — <c>BuildingsRuntimeEditor.TrySetDoor</c> and friends.
    ///
    /// There is exactly one write path on purpose. A console command that serialized
    /// <c>overrides.door</c> itself would be a second writer to keep in step with the reader,
    /// which is the precise shape of the spawner coordinate-space drift. So these tests are
    /// the contract for BOTH surfaces, and the refusals matter as much as the successes: a
    /// doorway authored against an overlay that does not load is found by walking into it.
    ///
    /// The success cases write real JSON, so the whole fixture pins the map slot to a scratch
    /// directory under <c>Application.temporaryCachePath</c>. Without that they would write to
    /// StreamingAssets and edit the shipped world.
    /// </summary>
    [TestFixture]
    public class BuildingDoorAuthoringTests
    {
        private const string SHIPPED_INTERIOR = "Interiors/house_interior_small.overlay.json";

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<Object> _assets = new List<Object>();
        private string _scratchRoot;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            _scratchRoot = Path.Combine(Application.temporaryCachePath,
                                        "DoorAuthoringTests_" + Random.Range(100000, 999999));
            Directory.CreateDirectory(_scratchRoot);

            MapEditorActiveSlot.SetPersistentRootOverrideForTests(_scratchRoot);
            MapEditorActiveSlot.SetOverrideForTests("door_authoring_scratch");
        }

        [TearDown]
        public void TearDown()
        {
            MapEditorActiveSlot.SetOverrideForTests(null);
            MapEditorActiveSlot.SetPersistentRootOverrideForTests(null);
            MapEditorActiveSlot.SetStreamingRootOverrideForTests(null);

            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();

            foreach (var a in _assets)
                if (a != null) Object.DestroyImmediate(a);
            _assets.Clear();

            foreach (var b in Object.FindObjectsOfType<BuildingObject>())
                if (b != null) Object.DestroyImmediate(b.gameObject);
            foreach (var e in Object.FindObjectsOfType<BuildingsRuntimeEditor>())
                if (e != null) Object.DestroyImmediate(e.gameObject);
            ClearSingletonInstance<BuildingsRuntimeEditor>();

            try { if (Directory.Exists(_scratchRoot)) Directory.Delete(_scratchRoot, true); }
            catch (IOException) { /* a scratch dir left behind is harmless */ }

            LogAssert.ignoreFailingMessages = false;
        }

        // ── Fixtures ────────────────────────────────────────────────────────────

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var f = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }

        private BuildingsRuntimeEditor MakeEditor()
        {
            ClearSingletonInstance<BuildingsRuntimeEditor>();
            var go = new GameObject("TestBuildingsEditor");
            _spawned.Add(go);
            return go.AddComponent<BuildingsRuntimeEditor>();
        }

        private BuildingTemplateData MakeTemplate(bool hasDoor)
        {
            var t = ScriptableObject.CreateInstance<BuildingTemplateData>();
            t.name                 = "DoorAuthoringTemplate";
            t.templateId           = 4242;
            t.originalScale        = new Vector2Int(128, 192);
            t.hasDoor              = hasDoor;
            t.doorOffsetNormalized = new Vector2(0.5f, 0.05f);
            t.doorSizeNormalized   = new Vector2(0.2f, 0.15f);
            _assets.Add(t);
            return t;
        }

        private BuildingObject MakeBuilding(BuildingTemplateData template, int id, Vector3 pos)
        {
            var go = new GameObject($"TestBuilding_{id}");
            go.transform.position = pos;
            _spawned.Add(go);

            var b = go.AddComponent<BuildingObject>();
            typeof(BuildingObject)
                .GetField("_template", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(b, template);
            b.InstanceId = id;
            b.ZoneName   = "lobby";
            return b;
        }

        private static UndoStack UndoOf(BuildingsRuntimeEditor editor)
            => (UndoStack)typeof(BuildingsRuntimeEditor)
                .GetField("_undo", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(editor);

        // ── Refusals (never reach the disk) ─────────────────────────────────────

        [Test]
        public void NoBuildingSelected_IsRefused()
        {
            var editor = MakeEditor();

            Assert.IsFalse(editor.TrySetDoor(null, SHIPPED_INTERIOR, 1f, 2f, out string message));
            StringAssert.Contains("building", message.ToLowerInvariant());
        }

        [Test]
        public void BlankTarget_IsRefused()
        {
            var editor = MakeEditor();
            var b = MakeBuilding(MakeTemplate(hasDoor: true), 1, Vector3.zero);

            Assert.IsFalse(editor.TrySetDoor(b, "", 0f, 0f, out string a));
            Assert.IsFalse(editor.TrySetDoor(b, "   ", 0f, 0f, out string bMsg));
            Assert.IsFalse(editor.TrySetDoor(b, null, 0f, 0f, out string c));

            StringAssert.Contains("target overlay", a);
            StringAssert.Contains("target overlay", bMsg);
            StringAssert.Contains("target overlay", c);
            Assert.IsNull(b.DoorSpec);
        }

        [Test]
        public void TemplateWithoutADoorway_IsRefusedAndSaysWhichSwitchToFlip()
        {
            var editor = MakeEditor();
            var b = MakeBuilding(MakeTemplate(hasDoor: false), 1, Vector3.zero);

            Assert.IsFalse(editor.TrySetDoor(b, SHIPPED_INTERIOR, 0f, 0f, out string message));

            StringAssert.Contains("Has doorway", message,
                "The message has to name the control that fixes it — the author cannot see " +
                "hasDoor from the map.");
            Assert.IsNull(b.DoorSpec);
            Assert.IsNull(BuildingDoorFactory.Find(b));
        }

        [Test]
        public void UnloadableDestination_IsRefusedAtAuthorTime()
        {
            // The single most valuable refusal in the feature: a doorway pointing at a file
            // that does not load clears the world and strands the player, and it would be
            // discovered by walking into it rather than by reading a console.
            var editor = MakeEditor();
            var b = MakeBuilding(MakeTemplate(hasDoor: true), 1, Vector3.zero);

            Assert.IsFalse(editor.TrySetDoor(b, "no_such_room.overlay.json", 0f, 0f, out string message));

            StringAssert.Contains("not a loadable overlay", message);
            Assert.IsNull(b.DoorSpec);
        }

        // ── Success (writes to the scratch slot) ────────────────────────────────

        [Test]
        public void ValidCombination_AttachesALiveDoorwayAndRecordsTheDestination()
        {
            var editor = MakeEditor();
            var b = MakeBuilding(MakeTemplate(hasDoor: true), 1, new Vector3(10f, 5f, 0f));

            Assert.IsTrue(editor.TrySetDoor(b, SHIPPED_INTERIOR, 7.5f, 5.5f, out string message),
                $"Refused: {message}");

            Assert.IsNotNull(b.DoorSpec, "The destination was not recorded on the placement.");
            Assert.AreEqual(SHIPPED_INTERIOR, b.DoorSpec.target);
            Assert.AreEqual(7.5f, b.DoorSpec.spawnX, 1e-3f);
            Assert.AreEqual(5.5f, b.DoorSpec.spawnY, 1e-3f);

            var door = BuildingDoorFactory.Find(b);
            Assert.IsNotNull(door, "Writing the spec without attaching the live doorway would " +
                                   "produce correct JSON and a door that does nothing until reload.");
            Assert.AreEqual(SHIPPED_INTERIOR, door.TargetOverlay);
            Assert.IsTrue(b.HasUsableDoor);
        }

        [Test]
        public void AuthoringADoorway_IsUndoable()
        {
            var editor = MakeEditor();
            var b = MakeBuilding(MakeTemplate(hasDoor: true), 1, new Vector3(10f, 5f, 0f));

            Assert.IsTrue(editor.TrySetDoor(b, SHIPPED_INTERIOR, 1f, 2f, out _));
            Assume.That(b.DoorSpec != null);

            UndoOf(editor).Undo();

            Assert.IsNull(b.DoorSpec, "Ctrl+Z has to take the doorway back off, like every other " +
                                      "edit in this editor.");
            Assert.IsNull(BuildingDoorFactory.Find(b));
        }

        [Test]
        public void ClearingADoorway_RemovesTheLiveOneAndTheDestination()
        {
            var editor = MakeEditor();
            var b = MakeBuilding(MakeTemplate(hasDoor: true), 1, new Vector3(10f, 5f, 0f));
            Assume.That(editor.TrySetDoor(b, SHIPPED_INTERIOR, 1f, 2f, out _));

            Assert.IsTrue(editor.TryClearDoor(b, out string message), message);

            Assert.IsNull(b.DoorSpec);
            Assert.IsNull(BuildingDoorFactory.Find(b));
        }

        [Test]
        public void ClearingABuildingWithNoDoorway_IsRefusedRatherThanSilent()
        {
            var editor = MakeEditor();
            var b = MakeBuilding(MakeTemplate(hasDoor: true), 1, Vector3.zero);

            Assert.IsFalse(editor.TryClearDoor(b, out string message));
            StringAssert.Contains("no doorway", message);
        }

        // ── Template scope: every placement at once ─────────────────────────────

        [Test]
        public void TurningTheTemplateDoorwayOff_RemovesItFromEveryPlacement()
        {
            var editor = MakeEditor();
            var template = MakeTemplate(hasDoor: true);
            var a = MakeBuilding(template, 1, new Vector3(10f, 5f, 0f));
            var b = MakeBuilding(template, 2, new Vector3(30f, 5f, 0f));

            Assume.That(editor.TrySetDoor(a, SHIPPED_INTERIOR, 1f, 2f, out _));
            Assume.That(editor.TrySetDoor(b, SHIPPED_INTERIOR, 3f, 4f, out _));
            Assume.That(BuildingDoorFactory.Find(a) != null && BuildingDoorFactory.Find(b) != null);

            Assert.IsTrue(editor.TrySetTemplateHasDoor(template, false, out string message), message);

            Assert.IsFalse(template.hasDoor);
            Assert.IsNull(BuildingDoorFactory.Find(a),
                "The anchor is shared, so turning it off has to reach every placement — leaving " +
                "live doorways on an art that no longer has one is the split-brain state the " +
                "factory refuses to create in the first place.");
            Assert.IsNull(BuildingDoorFactory.Find(b));
        }

        [Test]
        public void TurningTheTemplateDoorwayBackOn_RestoresTheLivePlacements()
        {
            var editor = MakeEditor();
            var template = MakeTemplate(hasDoor: true);
            var a = MakeBuilding(template, 1, new Vector3(10f, 5f, 0f));
            Assume.That(editor.TrySetDoor(a, SHIPPED_INTERIOR, 1f, 2f, out _));

            Assume.That(editor.TrySetTemplateHasDoor(template, false, out _));
            Assume.That(BuildingDoorFactory.Find(a) == null);

            Assert.IsTrue(editor.TrySetTemplateHasDoor(template, true, out _));

            Assert.IsNotNull(BuildingDoorFactory.Find(a),
                "The destination was never cleared, so restoring the template's doorway must " +
                "bring the live one back rather than needing a reload.");
        }

        [Test]
        public void TogglingTheTemplateDoorway_IsUndoable()
        {
            var editor = MakeEditor();
            var template = MakeTemplate(hasDoor: false);
            MakeBuilding(template, 1, Vector3.zero);

            Assert.IsTrue(editor.TrySetTemplateHasDoor(template, true, out _));
            Assume.That(template.hasDoor);

            UndoOf(editor).Undo();

            Assert.IsFalse(template.hasDoor);
        }

        [Test]
        public void SettingTheSameTemplateValue_IsANoOpNotAnUndoEntry()
        {
            var editor = MakeEditor();
            var template = MakeTemplate(hasDoor: true);

            Assert.IsTrue(editor.TrySetTemplateHasDoor(template, true, out string message));
            StringAssert.Contains("already", message);
            Assert.IsFalse(UndoOf(editor).CanUndo,
                "A no-op must not consume an undo slot, or Ctrl+Z stops meaning 'take back my " +
                "last change'.");
        }

        [Test]
        public void MovingTheTemplateAnchor_MovesTheDoorwayOnEveryPlacement()
        {
            var editor = MakeEditor();
            var template = MakeTemplate(hasDoor: true);
            var a = MakeBuilding(template, 1, new Vector3(10f, 5f, 0f));
            var b = MakeBuilding(template, 2, new Vector3(30f, 5f, 0f));
            Assume.That(editor.TrySetDoor(a, SHIPPED_INTERIOR, 1f, 2f, out _));
            Assume.That(editor.TrySetDoor(b, SHIPPED_INTERIOR, 1f, 2f, out _));

            Assume.That(a.TryGetDoorWorldRect(out var aBefore));
            a.TryGetDoorWorldRect(out aBefore);
            b.TryGetDoorWorldRect(out var bBefore);

            Assert.IsTrue(editor.TrySetTemplateAnchor(template, new Vector2(0.1f, 0.5f), null, out string message),
                message);

            Assert.IsTrue(a.TryGetDoorWorldRect(out var aAfter));
            Assert.IsTrue(b.TryGetDoorWorldRect(out var bAfter));
            Assert.AreNotEqual(aBefore.center, aAfter.center, "Placement A did not move.");
            Assert.AreNotEqual(bBefore.center, bAfter.center,
                "Placement B did not move — the anchor is shared catalog data, so an edit that " +
                "only reaches the selected building is a lie about what was changed.");

            Assert.AreEqual(BuildingDoorFactory.Find(a).transform.position.x, aAfter.center.x, 1e-3f,
                "The live doorway object must follow the anchor, not just the reported rect.");
        }

        [Test]
        public void TemplateAnchorAndSize_AreClampedIntoRange()
        {
            var editor = MakeEditor();
            var template = MakeTemplate(hasDoor: true);

            Assert.IsTrue(editor.TrySetTemplateAnchor(template, new Vector2(-4f, 9f), 50f, out _));

            Assert.AreEqual(0f, template.doorOffsetNormalized.x, 1e-4f);
            Assert.AreEqual(1f, template.doorOffsetNormalized.y, 1e-4f);
            Assert.LessOrEqual(template.doorSizeNormalized.x, 1f);
            Assert.Greater(template.doorSizeNormalized.x, 0f);
        }

        [Test]
        public void TemplateAnchor_NullArgumentsLeaveThatHalfAlone()
        {
            var editor = MakeEditor();
            var template = MakeTemplate(hasDoor: true);
            Vector2 sizeBefore = template.doorSizeNormalized;

            Assert.IsTrue(editor.TrySetTemplateAnchor(template, new Vector2(0.25f, 0.25f), null, out _));

            Assert.AreEqual(0.25f, template.doorOffsetNormalized.x, 1e-4f);
            Assert.AreEqual(sizeBefore, template.doorSizeNormalized, "Size must not move when only the anchor was asked for.");
        }
    }
}
