using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.World;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Editors.Lighting
{
    /// <summary>
    /// Pins the Lighting Editor's undo/redo against the three corruption paths the audit measured
    /// in Play Mode, plus the class of bug that produced all three.
    ///
    /// The root cause was that every command captured a <see cref="GameObject"/>. A captured
    /// reference dies with its object and cannot be revived, so:
    ///
    ///   * the redo of a delete was written as an empty lambda, because there was no live object
    ///     to delete again — measured: 10 lights -> 9 -> 10 -> <b>10</b>;
    ///   * the redo of a spawn re-created the light but had nowhere to write the new reference,
    ///     so the next undo looked at a corpse and left an orphan no further undo could reach —
    ///     measured across a frame boundary: 10 -> 11 -> 11 -> <b>12</b>;
    ///   * the undo of a delete rebuilt the light from a preset key parsed out of the object's
    ///     NAME, minting a fresh id and dropping every per-instance override — measured: a light
    ///     with id=1 and an authored colour came back as id=15 with none.
    ///
    /// The fix is that commands address lights by their stable id and carry a
    /// <see cref="WorldLightLoader.LightSnapshot"/>, which holds no GameObject and therefore
    /// cannot go stale. These tests assert that property directly, so a future refactor that
    /// reintroduces a captured reference fails here rather than in someone's map.
    /// </summary>
    [TestFixture]
    public class LightingEditorUndoTests
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic
                                       | BindingFlags.Instance | BindingFlags.Static;

        private static readonly Type LoaderType = typeof(WorldLightLoader);
        private static Type SnapshotType => LoaderType.GetNestedType("LightSnapshot", Any);
        private static Type InstanceType => LoaderType.GetNestedType("LightInstance", Any);

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private WorldLightLoader _loader;

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject("UndoTestLoader");
            _spawned.Add(go);
            _loader = go.AddComponent<WorldLightLoader>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _spawned.Clear();
            _loader = null;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  The snapshot carries a light whole
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A snapshot must carry every per-instance override. If it carries only the preset key,
        /// undo hands back a stock light of the right family — which is what shipped, and which
        /// reads as "my tuning was thrown away" rather than as a bug.
        /// </summary>
        [Test]
        public void LightSnapshot_CarriesIdPositionAndEveryOverride()
        {
            Assert.IsNotNull(SnapshotType, "LightSnapshot is gone — undo cannot restore a light whole.");

            foreach (string field in new[] { "Id", "PresetId", "Zone", "RelX", "RelY", "WorldPosition",
                                             "OverrideColor", "OverrideIntensity", "OverrideRadius",
                                             "OverrideFlickerAmp", "OverrideFlickerSpeed" })
                Assert.IsNotNull(SnapshotType.GetField(field),
                    $"LightSnapshot lost '{field}'. Anything it does not carry is silently dropped " +
                    "the first time an author undoes a delete.");

            // The whole point is that it holds no live reference.
            foreach (var f in SnapshotType.GetFields())
                Assert.IsFalse(typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType),
                    $"LightSnapshot.{f.Name} is a {f.FieldType.Name}. A snapshot that holds a Unity " +
                    "object goes stale exactly when it is needed — it is captured to survive the " +
                    "destruction of the thing it describes.");
        }

        /// <summary>
        /// Capture must round-trip through the in-memory record. Asserted against the record
        /// rather than through a spawn, because spawning needs a catalog and a scene.
        /// </summary>
        [Test]
        public void CaptureLight_ReadsTheRecordNotTheGameObjectName()
        {
            var lightGo = MakeSceneLight("Light_7_Torch", new Vector3(12f, 34f, 0f));
            AddInstance(id: 7, presetId: "preset_with_underscores", zone: "zone_100_50",
                        relX: 1323f, relY: 457f, go: lightGo, persistent: true,
                        color: new Color(0.1f, 0.9f, 0.3f), intensity: 2.75f, radius: 9.5f);

            var snap = _loader.CaptureLight(lightGo);
            Assert.IsNotNull(snap, "CaptureLight refused a light this loader owns.");
            Assert.AreEqual(7, Get<int>(snap, "Id"));
            Assert.AreEqual("preset_with_underscores", Get<string>(snap, "PresetId"),
                "The preset came from somewhere other than the record. The editor used to parse " +
                "it out of the GameObject's name, which any key containing an underscore breaks.");
            Assert.AreEqual("zone_100_50", Get<string>(snap, "Zone"));
            Assert.AreEqual(1323f, Get<float>(snap, "RelX"), 0.001f);
            Assert.AreEqual(457f,  Get<float>(snap, "RelY"), 0.001f);
            Assert.AreEqual(new Vector3(12f, 34f, 0f), Get<Vector3>(snap, "WorldPosition"));

            Assert.AreEqual(new Color(0.1f, 0.9f, 0.3f), Get<Color?>(snap, "OverrideColor").Value);
            Assert.AreEqual(2.75f, Get<float?>(snap, "OverrideIntensity").Value, 0.001f);
            Assert.AreEqual(9.5f,  Get<float?>(snap, "OverrideRadius").Value,    0.001f);
        }

        /// <summary>
        /// A derived light belongs to its lamp-post building, not to the light file. Capturing one
        /// would let an undo re-create it independently — and then the next load would build the
        /// building's copy too, so the world would gain a light every time.
        /// </summary>
        [Test]
        public void CaptureLight_RefusesADerivedLight()
        {
            var lightGo = MakeSceneLight("DerivedLight_Torch", Vector3.zero);
            AddInstance(id: 0, presetId: "Torch", zone: "", relX: 0f, relY: 0f,
                        go: lightGo, persistent: false);

            Assert.IsNull(_loader.CaptureLight(lightGo),
                "A derived light was captured. Restoring one duplicates it against the copy its " +
                "building rebuilds on the next load.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Id-addressed lookup
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Id lookup is what lets a command outlive the object it was recorded against. Without
        /// it, a command recorded before a delete+undo cycle silently targets nothing.
        /// </summary>
        [Test]
        public void FindLightById_ResolvesAcrossAReplacedGameObject()
        {
            var first = MakeSceneLight("Light_4_Torch", Vector3.zero);
            AddInstance(id: 4, presetId: "Torch", zone: "z", relX: 0f, relY: 0f, go: first, persistent: true);
            Assert.AreSame(first, _loader.FindLightById(4));

            // Simulate the delete+undo: the record is replaced by an equivalent one carrying a
            // brand new GameObject under the same id.
            ClearInstances();
            var second = MakeSceneLight("Light_4_Torch", Vector3.zero);
            AddInstance(id: 4, presetId: "Torch", zone: "z", relX: 0f, relY: 0f, go: second, persistent: true);

            Assert.AreSame(second, _loader.FindLightById(4),
                "Id lookup did not follow the light across its replacement. This is the property " +
                "that lets a move command recorded before a delete still undo afterwards.");
            Assert.IsNull(_loader.FindLightById(999), "An unknown id must resolve to null, not to a light.");
            Assert.IsNull(_loader.FindLightById(0),   "Id 0 is the derived-light sentinel and must never resolve.");
        }

        /// <summary>The preset key comes from the record, for derived lights too.</summary>
        [Test]
        public void GetLightPresetKey_ReadsTheRecord()
        {
            var lightGo = MakeSceneLight("anything at all", Vector3.zero);
            AddInstance(id: 0, presetId: "magic_blue_soft", zone: "", relX: 0f, relY: 0f,
                        go: lightGo, persistent: false);

            Assert.AreEqual("magic_blue_soft", _loader.GetLightPresetKey(lightGo),
                "The preset key was not read from the record. Note the GameObject's name here is " +
                "deliberately nothing like the key — the old name-parsing path returned garbage.");
            Assert.IsNull(_loader.GetLightPresetKey(null));
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  The world-generation guard
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Ids are unique within one loaded world and re-minted by the next. A history that
        /// survives a map-slot switch does not fail loudly — it succeeds on whichever light now
        /// wears that number. The generation counter is what makes that detectable.
        /// </summary>
        [Test]
        public void WorldGeneration_AdvancesWhenTheWorldIsTornDown()
        {
            int before = _loader.WorldGeneration;
            _loader.ClearSpawnedLights();
            int after = _loader.WorldGeneration;

            Assert.AreEqual(before + 1, after,
                "WorldGeneration did not advance. Nothing downstream can then tell that the ids " +
                "in an undo history now name different lights.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  UndoStack no longer fails in silence
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A throwing command must be reported. Both catches used to be empty, so a broken undo
        /// looked exactly like a working one while the stack went on claiming edits the world had
        /// never seen. The step is still consumed on purpose: five runtime editors share this
        /// class and wedging the history is worse for the author than losing one step.
        /// </summary>
        [Test]
        public void UndoStack_ReportsACommandThatThrowsInsteadOfSwallowingIt()
        {
            var stack = new UndoStack(8);
            stack.Record(new UndoStack.LambdaCommand("exploding step",
                doAction:   () => throw new InvalidOperationException("redo boom"),
                undoAction: () => throw new InvalidOperationException("undo boom")));

            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                @"\[UndoStack\] Undo of 'exploding step' threw InvalidOperationException"));
            Assert.IsTrue(stack.Undo(), "Undo must still consume the step rather than wedging the history.");
            Assert.AreEqual(1, stack.RedoCount, "The failed step must still be redoable.");

            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                @"\[UndoStack\] Redo of 'exploding step' threw InvalidOperationException"));
            Assert.IsTrue(stack.Redo());
            Assert.AreEqual(1, stack.UndoCount);
        }

        /// <summary>
        /// Recording a new step must drop the redo branch. Without it, redo after a fresh edit
        /// replays a command from an abandoned timeline against the current world.
        /// </summary>
        [Test]
        public void UndoStack_RecordingANewStepDropsTheRedoBranch()
        {
            var stack = new UndoStack(8);
            stack.Record(new UndoStack.LambdaCommand("a", () => { }, () => { }));
            stack.Undo();
            Assert.AreEqual(1, stack.RedoCount, "Precondition.");

            stack.Record(new UndoStack.LambdaCommand("b", () => { }, () => { }));
            Assert.AreEqual(0, stack.RedoCount,
                "The abandoned branch survived. Redo would replay a command from a timeline that " +
                "no longer describes the world.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  No command may capture a Unity object
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The design assertion: the editor performs its undo work through helpers addressed by
        /// stable id and by snapshot, never by GameObject.
        ///
        /// A blanket "no lambda captures a Unity object" check was tried first and is wrong for
        /// this file — the instance-list rows legitimately close over their own GameObject in a
        /// button listener, and every lambda calling an instance method captures <c>this</c>.
        /// Pinning the SIGNATURES is the honest version of the same intent: as long as the three
        /// commands can only express themselves as an id plus a snapshot, they have nothing
        /// stale to hold.
        /// </summary>
        [Test]
        public void LightingEditorUndoWork_IsAddressedByIdAndSnapshot()
        {
            var editorType = typeof(LightingRuntimeEditor);

            var removeById = editorType.GetMethod("RemoveById", Any);
            Assert.IsNotNull(removeById, "RemoveById is gone — deletion is being redone some other way.");
            Assert.AreEqual(typeof(int), removeById.GetParameters()[0].ParameterType,
                "RemoveById must take an id. Taking a GameObject reopens the stale-reference bug.");

            var moveById = editorType.GetMethod("MoveById", Any);
            Assert.IsNotNull(moveById, "MoveById is gone.");
            Assert.AreEqual(typeof(int),     moveById.GetParameters()[0].ParameterType);
            Assert.AreEqual(typeof(Vector3), moveById.GetParameters()[1].ParameterType);

            var restore = editorType.GetMethod("RestoreSnapshot", Any);
            Assert.IsNotNull(restore, "RestoreSnapshot is gone — undo of a delete cannot carry overrides.");
            Assert.AreEqual(SnapshotType, restore.GetParameters()[0].ParameterType,
                "RestoreSnapshot must take a LightSnapshot. A preset key alone brings back a stock " +
                "light of the right family, which is the bug that shipped.");

            Assert.IsNull(editorType.GetMethod("ExtractPresetFromName", Any),
                "The name-parsing helper is back. The preset key must come from the loader's " +
                "record — see WorldLightLoader.GetLightPresetKey.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────────────────

        private GameObject MakeSceneLight(string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            _spawned.Add(go);
            return go;
        }

        private System.Collections.IList Instances()
            => (System.Collections.IList)LoaderType.GetField("_activeLights", Any).GetValue(_loader);

        private void ClearInstances() => Instances().Clear();

        private void AddInstance(int id, string presetId, string zone, float relX, float relY,
                                 GameObject go, bool persistent,
                                 Color? color = null, float? intensity = null, float? radius = null)
        {
            object inst = Activator.CreateInstance(InstanceType);
            InstanceType.GetField("id").SetValue(inst, id);
            InstanceType.GetField("presetId").SetValue(inst, presetId);
            InstanceType.GetField("zone").SetValue(inst, zone);
            InstanceType.GetField("relX").SetValue(inst, relX);
            InstanceType.GetField("relY").SetValue(inst, relY);
            InstanceType.GetField("go").SetValue(inst, go);
            InstanceType.GetField("persistent").SetValue(inst, persistent);
            InstanceType.GetField("overrideColor").SetValue(inst, color);
            InstanceType.GetField("overrideIntensity").SetValue(inst, intensity);
            InstanceType.GetField("overrideRadius").SetValue(inst, radius);
            Instances().Add(inst);
        }

        private static T Get<T>(object snapshot, string field)
            => (T)SnapshotType.GetField(field).GetValue(snapshot);
    }
}
