using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.Buildings
{
    /// <summary>
    /// Ctrl+C / Ctrl+V on a placed building.
    ///
    /// <para>WHAT THIS FIXTURE DELIBERATELY DOES NOT DO IS PASTE. A paste goes through
    /// <c>ExecutePersistedEdit</c>, which force-flushes <c>SaveInstancesToJson</c>, and that
    /// path carries no Play-Mode guard — so an EditMode paste would write the SHIPPED
    /// <c>StreamingAssets/Buildings/buildings_instances.json</c> from a scene holding three
    /// synthetic buildings. That is the twin-save incident's shape exactly. The copy half is
    /// pure and is measured directly; the paste half is covered by asserting the COMPOSITION —
    /// that the snapshot carries every override the serializer writes — which is the half that
    /// can be false while both ends read correctly.</para>
    /// </summary>
    [TestFixture]
    public class BuildingsClipboardTests
    {
        private readonly List<GameObject>       _sceneObjects = new List<GameObject>();
        private readonly List<ScriptableObject> _assets       = new List<ScriptableObject>();

        private static readonly FieldInfo s_templateField =
            typeof(BuildingObject).GetField("_template", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo s_activeBuildingField =
            typeof(BuildingsRuntimeEditor).GetField("_activeBuilding", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo s_clipboardField =
            typeof(BuildingsRuntimeEditor).GetField("_clipboard", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo s_copy =
            typeof(BuildingsRuntimeEditor).GetMethod("CopyActiveBuilding",
                BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp] public void SetUp() => LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _sceneObjects.Clear();
            foreach (var so in _assets) if (so != null) UnityEngine.Object.DestroyImmediate(so);
            _assets.Clear();
        }

        // ── Fixtures ─────────────────────────────────────────────────────────

        private BuildingsRuntimeEditor NewEditor()
        {
            var go = new GameObject("BuildingsEditorUnderTest");
            _sceneObjects.Add(go);
            // Awake never runs on a component added in Edit Mode, so every field here is its
            // declared default — which is what makes the copy path measurable without the
            // rest of the editor being built.
            return go.AddComponent<BuildingsRuntimeEditor>();
        }

        private BuildingTemplateData NewTemplate(int id = 7)
        {
            var t = ScriptableObject.CreateInstance<BuildingTemplateData>();
            t.templateId    = id;
            t.name          = "test_house";
            t.originalScale = new Vector2Int(64, 96);
            t.splitRatio    = 0.5f;
            t.colliderScope = "CG";
            _assets.Add(t);
            return t;
        }

        private BuildingObject NewBuilding(BuildingTemplateData t, int instanceId = 3)
        {
            var go = new GameObject("B_" + instanceId);
            _sceneObjects.Add(go);
            var b = go.AddComponent<BuildingObject>();
            s_templateField.SetValue(b, t);
            b.InstanceId = instanceId;
            b.ZoneName   = "Lobby";
            return b;
        }

        /// <summary>The ANCHOR entry - last in the list, the one the cursor lands on. For a
        /// single-building copy it is the only entry.</summary>
        private static object ClipboardOf(BuildingsRuntimeEditor ed)
        {
            var list = (System.Collections.IList)s_clipboardField.GetValue(ed);
            return list.Count == 0 ? null : list[list.Count - 1];
        }

        private static System.Collections.IList ClipboardListOf(BuildingsRuntimeEditor ed) =>
            (System.Collections.IList)s_clipboardField.GetValue(ed);

        private static object Field(object entry, string name) =>
            entry.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance).GetValue(entry);

        // ── Copy ─────────────────────────────────────────────────────────────

        /// <summary>Copying nothing must leave the clipboard alone rather than storing an
        /// empty entry — a paste that produced a templateless building would be worse than a
        /// refusal, and <c>HasClipboard</c> is the single answer both the guard and this test
        /// read.</summary>
        [Test]
        public void CopyWithNoSelection_RefusesAndStoresNothing()
        {
            var ed = NewEditor();
            Assert.IsFalse(ed.HasClipboard, "A fresh editor has copied nothing.");
            Assert.IsFalse((bool)s_copy.Invoke(ed, null), "Copy with no active building must refuse.");
            Assert.IsFalse(ed.HasClipboard);
            Assert.AreEqual(0, ClipboardListOf(ed).Count);
        }

        [Test]
        public void CopyOfATemplatelessBuilding_Refuses()
        {
            var ed = NewEditor();
            var go = new GameObject("B_untemplated");
            _sceneObjects.Add(go);
            s_activeBuildingField.SetValue(ed, go.AddComponent<BuildingObject>());

            Assert.IsFalse((bool)s_copy.Invoke(ed, null));
            Assert.IsFalse(ed.HasClipboard);
        }

        /// <summary>
        /// The snapshot carries every per-instance override, not just the template. A copy
        /// that dropped one produces a duplicate that LOOKS identical and BEHAVES differently,
        /// which is the failure nobody reports.
        /// </summary>
        [Test]
        public void Copy_SnapshotsEveryPerInstanceOverride()
        {
            var ed = NewEditor();
            var t  = NewTemplate();
            var b  = NewBuilding(t);
            b.ScaleOverride         = new Vector2Int(48, 72);
            b.SplitRatioOverride    = 0.31f;
            b.ZBottom               = 2;
            b.ZTop                  = 7;
            b.ColliderScopeOverride = "CU";
            b.InteractableOverride  = 1;
            b.DoorSpec              = new BuildingDoorSpec { target = "house_interior_small.overlay.json", spawnX = 4f, spawnY = 5f };
            s_activeBuildingField.SetValue(ed, b);

            Assert.IsTrue((bool)s_copy.Invoke(ed, null));
            var entry = ClipboardOf(ed);
            Assert.IsNotNull(entry);

            Assert.AreSame(t, Field(entry, "Template"));
            Assert.AreEqual(new Vector2Int(48, 72), Field(entry, "ScaleOverride"));
            Assert.AreEqual(0.31f, (float)Field(entry, "SplitRatioOverride"), 1e-4f);
            Assert.AreEqual(2, Field(entry, "ZBottom"));
            Assert.AreEqual(7, Field(entry, "ZTop"));
            Assert.AreEqual(Vector3.zero, Field(entry, "Offset"), "A lone copy is its own anchor.");
            Assert.AreEqual("CU", Field(entry, "ColliderScopeOverride"));
            Assert.AreEqual(1, Field(entry, "InteractableOverride"));

            var door = (BuildingDoorSpec)Field(entry, "Door");
            Assert.IsNotNull(door, "A doorway travels with the copy — BuildingDoorSpec's own summary says so.");
            Assert.AreEqual("house_interior_small.overlay.json", door.target);
            Assert.AreEqual(4f, door.spawnX, 1e-4f);
        }

        /// <summary>
        /// The door is CLONED. Sharing the spec would let an edit to the source's destination
        /// silently rewrite what the clipboard is holding — the reason
        /// <see cref="BuildingDoorSpec.Clone"/> exists at all.
        /// </summary>
        [Test]
        public void Copy_ClonesTheDoorRatherThanSharingIt()
        {
            var ed = NewEditor();
            var b  = NewBuilding(NewTemplate());
            var original = new BuildingDoorSpec { target = "a.overlay.json" };
            b.DoorSpec = original;
            s_activeBuildingField.SetValue(ed, b);

            s_copy.Invoke(ed, null);
            original.target = "b.overlay.json";

            var door = (BuildingDoorSpec)Field(ClipboardOf(ed), "Door");
            Assert.AreNotSame(original, door);
            Assert.AreEqual("a.overlay.json", door.target, "The clipboard must hold the value it was given.");
        }

        /// <summary>
        /// The source is a SNAPSHOT, so deleting or re-editing the original leaves the
        /// clipboard intact. A live reference would paste whatever the source has become — or
        /// throw once it is gone.
        /// </summary>
        [Test]
        public void Copy_SurvivesTheSourceBeingDestroyed()
        {
            var ed = NewEditor();
            var t  = NewTemplate();
            var b  = NewBuilding(t);
            b.ZTop = 8;
            s_activeBuildingField.SetValue(ed, b);
            s_copy.Invoke(ed, null);

            UnityEngine.Object.DestroyImmediate(b.gameObject);

            Assert.IsTrue(ed.HasClipboard);
            Assert.AreEqual(8, Field(ClipboardOf(ed), "ZTop"));
            Assert.AreSame(t, Field(ClipboardOf(ed), "Template"));
        }

        /// <summary>
        /// The measured height is what lands the duplicate's VISUAL CENTRE on the cursor, and
        /// it must come from the building's own drawn rect rather than from the template — a
        /// copy of a half-size house offset by half the FULL size lands visibly high, and
        /// nothing about the code would look wrong.
        /// </summary>
        [Test]
        public void Copy_MeasuresTheSourcesOwnHeight_NotTheTemplates()
        {
            var ed = NewEditor();
            var t  = NewTemplate();                      // originalScale.y = 96
            var b  = NewBuilding(t);
            b.ScaleOverride = new Vector2Int(32, 48);    // half height
            s_activeBuildingField.SetValue(ed, b);

            Assert.IsTrue(b.TryGetWorldRect(out var rect), "The fallback rect path must answer without renderers.");
            s_copy.Invoke(ed, null);

            Assert.AreEqual(rect.height, (float)Field(ClipboardOf(ed), "SourceWorldHeight"), 1e-4f);
            Assert.Less((float)Field(ClipboardOf(ed), "SourceWorldHeight"), 96f / 32f,
                "A scaled-down building must not report the template's height.");
        }

        /// <summary>
        /// A CG collision grid belongs to the IMAGE and is inherited by sharing the template.
        /// Snapshotting it per instance would fork one shared edit into two private ones that
        /// drift apart on the next paint.
        /// </summary>
        [Test]
        public void Copy_TakesNoPrivateGrid_ForABuildingOnSharedScope()
        {
            var ed = NewEditor();
            var b  = NewBuilding(NewTemplate());
            b.ColliderScopeOverride = "";                // → template's "CG"
            s_activeBuildingField.SetValue(ed, b);

            s_copy.Invoke(ed, null);
            Assert.IsNull(Field(ClipboardOf(ed), "CollisionOverride"));
        }

        // ── Composition: the snapshot vs what the serializer writes ──────────

        /// <summary>
        /// EVERY OVERRIDE THE SERIALIZER WRITES MUST BE ON THE CLIPBOARD. This reads
        /// <c>BuildingsRuntimeEditor.Persistence.cs</c> for the <c>BuildingObject</c>
        /// properties it consults when building an instance's <c>overrides</c> block, and
        /// requires each to appear in the clipboard's own source. It is the half that cannot
        /// be checked from either end alone: a new override added to the save path is
        /// perfectly correct there and silently absent from every paste, and the duplicate
        /// then differs from its source in a field neither file looks wrong about.
        /// </summary>
        [Test]
        public void TheClipboard_CarriesEveryOverrideTheSerializerWrites()
        {
            string dir = Path.Combine(Application.dataPath,
                "_Project/Scripts/Gameplay/Editors/Buildings");
            string persistence = File.ReadAllText(Path.Combine(dir, "BuildingsRuntimeEditor.Persistence.cs"));
            string clipboard   = File.ReadAllText(Path.Combine(dir, "BuildingsRuntimeEditor.Clipboard.cs"));

            // The properties the save path reads off a building to decide its overrides block.
            // Position, zone and instance id are excluded on purpose: they are the identity of
            // a PLACEMENT, and a paste is a new placement somewhere else.
            var expected = new[]
            {
                "ScaleOverride", "SplitRatioOverride", "ColliderScopeOverride",
                "ZBottom", "ZTop", "InteractableOverride", "DoorSpec",
            };

            var missingFromSerializer = expected.Where(p => !persistence.Contains("b." + p)).ToList();
            CollectionAssert.IsEmpty(missingFromSerializer,
                "This list is derived from the save path; a name that no longer appears there means " +
                "the serializer changed and this test is now asserting about the wrong fields.");

            // DoorSpec is read through the clipboard's own `Door` field, so map it.
            var missingFromClipboard = expected
                .Where(p => !clipboard.Contains(p == "DoorSpec" ? "DoorSpec?.Clone()" : p))
                .ToList();
            CollectionAssert.IsEmpty(missingFromClipboard,
                "An override the save path writes but the clipboard does not copy makes a paste " +
                "silently differ from its source.");

            StringAssert.Contains("_colliderInstanceStore", clipboard,
                "The per-instance collision grid is written per record too, so it must travel with a copy.");
        }

        // ── The input half ───────────────────────────────────────────────────

        /// <summary>
        /// Both clipboard verbs are this editor's OWN Ctrl tools. The owner string is the
        /// editor's exact <c>EditorName</c> — a mismatch there is silent and kills the tool.
        /// </summary>
        [TestCase("Copy")]
        [TestCase("Paste")]
        public void TheClipboardVerbs_AreDeclaredAsBuildingsCtrlTools(string action)
        {
            var d = InputActionCatalog.Find(InputActionCatalog.MapBuildingsEditor, action);
            Assert.IsNotNull(d, action + " is not in the catalog, so EditorInput cannot answer it.");
            Assert.AreEqual("Buildings Editor", d.OwnerEditor);
            Assert.IsTrue(d.RequiresCtrl,
                "Declared Ctrl in the catalog is what makes EditorInput answer it while Ctrl is held " +
                "AND what tells the conflict scanner it does not clash with a bare key.");
        }

        /// <summary>
        /// THE REGRESSION THAT MADE THE TILE EDITOR'S OWN CLIPBOARD DEAD.
        /// <c>EditorInput.Tool</c> used to open with an unconditional
        /// <c>if (IsCtrlHeld()) return false;</c>, which separates a Ctrl shortcut from a bare
        /// tool on the same key AND makes a Ctrl tool unreachable at every moment. Three
        /// buttons in the Tile editor's Select panel do the same job, so the dead keys read as
        /// a preference rather than as a defect. The modifier is MATCHED against the
        /// descriptor now; this reads the source because the gate itself needs a live editor
        /// context and a real keyboard, neither of which exists in EditMode.
        /// </summary>
        [Test]
        public void EditorInput_MatchesTheCtrlModifier_RatherThanRefusingIt()
        {
            string src = File.ReadAllText(Path.Combine(Application.dataPath,
                "_Project/Scripts/Core/Input/EditorInput.cs"));
            src = Regex.Replace(src, @"^\s*///.*$", "", RegexOptions.Multiline);

            StringAssert.DoesNotContain("if (KeyboardInputManager.IsCtrlHeld()) return false;", src,
                "A blanket refusal makes every Ctrl tool unreachable — the Tile editor shipped that way.");
            StringAssert.Contains("descriptor.RequiresCtrl == KeyboardInputManager.IsCtrlHeld()", src,
                "The tool gate must read the same flag the shared verbs and the conflict scanner read.");
        }

        /// <summary>The Tile editor's clipboard is the other half of that fix: its three verbs
        /// only ever fire with Ctrl held, so the catalog has to say so or they go back to being
        /// refused.</summary>
        [TestCase("Copy")]
        [TestCase("Cut")]
        [TestCase("Paste")]
        public void TheTileClipboardVerbs_AreDeclaredCtrlToo(string action)
        {
            var d = InputActionCatalog.Find(InputActionCatalog.MapTileEditor, action);
            Assert.IsNotNull(d);
            Assert.IsTrue(d.RequiresCtrl);
        }
    }
}
