using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.Buildings
{
    /// <summary>
    /// The Select tool: its button and scope flyout in the Tools panel, the
    /// <see cref="BuildingSelectionSet"/> behind Multiple scope, and the group half of the
    /// clipboard.
    ///
    /// <para>Same boundary as <c>BuildingsClipboardTests</c>: nothing here PASTES, MOVES or
    /// DELETES through the editor, because every one of those runs
    /// <c>ExecutePersistedEdit</c>, which force-flushes the shipped
    /// <c>buildings_instances.json</c> with no Play-Mode guard. The set is pure and measured
    /// directly; the click logic is measured through the editor with no UI built (Awake never
    /// runs in Edit Mode, so every field is its declared default and the null guards do the
    /// rest); the group copy is measured because copy writes nothing.</para>
    /// </summary>
    [TestFixture]
    public class BuildingsSelectionToolTests
    {
        private readonly List<GameObject>       _sceneObjects = new List<GameObject>();
        private readonly List<ScriptableObject> _assets       = new List<ScriptableObject>();

        private static readonly BindingFlags Any =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private static readonly FieldInfo s_templateField =
            typeof(BuildingObject).GetField("_template", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo s_activeBuildingField =
            typeof(BuildingsRuntimeEditor).GetField("_activeBuilding", Any);
        private static readonly FieldInfo s_selectionField =
            typeof(BuildingsRuntimeEditor).GetField("_selection", Any);
        private static readonly FieldInfo s_scopeField =
            typeof(BuildingsRuntimeEditor).GetField("_selectScope", Any);
        private static readonly FieldInfo s_clipboardField =
            typeof(BuildingsRuntimeEditor).GetField("_clipboard", Any);
        private static readonly MethodInfo s_handleClick =
            typeof(BuildingsRuntimeEditor).GetMethod("HandleSelectClick", Any);
        private static readonly MethodInfo s_reconcile =
            typeof(BuildingsRuntimeEditor).GetMethod("ReconcileSelection", Any);
        private static readonly MethodInfo s_copy =
            typeof(BuildingsRuntimeEditor).GetMethod("CopyActiveBuilding", Any);
        private static readonly Type s_scopeType =
            typeof(BuildingsRuntimeEditor).GetNestedType("SelectScope", BindingFlags.NonPublic);

        [SetUp] public void SetUp() => LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _sceneObjects.Clear();
            foreach (var so in _assets) if (so != null) UnityEngine.Object.DestroyImmediate(so);
            _assets.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Fixtures ─────────────────────────────────────────────────────────

        private BuildingTemplateData NewTemplate(int id = 7)
        {
            var t = ScriptableObject.CreateInstance<BuildingTemplateData>();
            t.templateId    = id;
            t.name          = "test_house";
            t.originalScale = new Vector2Int(64, 96);
            _assets.Add(t);
            return t;
        }

        private BuildingObject NewBuilding(BuildingTemplateData t, int instanceId, Vector3 pos = default)
        {
            var go = new GameObject("B_" + instanceId);
            go.transform.position = pos;
            _sceneObjects.Add(go);
            var b = go.AddComponent<BuildingObject>();
            s_templateField.SetValue(b, t);
            b.InstanceId = instanceId;
            b.ZoneName   = "Lobby";
            return b;
        }

        private BuildingsRuntimeEditor NewEditor(string scope)
        {
            var go = new GameObject("BuildingsEditorUnderTest");
            _sceneObjects.Add(go);
            var ed = go.AddComponent<BuildingsRuntimeEditor>();
            s_scopeField.SetValue(ed, Enum.Parse(s_scopeType, scope));
            return ed;
        }

        private static BuildingSelectionSet SelectionOf(BuildingsRuntimeEditor ed) =>
            (BuildingSelectionSet)s_selectionField.GetValue(ed);

        private static BuildingObject ActiveOf(BuildingsRuntimeEditor ed) =>
            (BuildingObject)s_activeBuildingField.GetValue(ed);

        private static void Click(BuildingsRuntimeEditor ed, BuildingObject b) =>
            s_handleClick.Invoke(ed, new object[] { b });

        // ── The set ──────────────────────────────────────────────────────────

        [Test]
        public void Set_ToggleAddsThenRemoves_AndTheLastAddedIsPrimary()
        {
            var t = NewTemplate();
            var a = NewBuilding(t, 1); var b = NewBuilding(t, 2);
            var set = new BuildingSelectionSet();

            Assert.IsTrue(set.Toggle(a));
            Assert.IsTrue(set.Toggle(b));
            Assert.AreSame(b, set.Primary);
            Assert.AreEqual(2, set.Count);

            Assert.IsFalse(set.Toggle(b), "Toggling a member removes it.");
            Assert.AreSame(a, set.Primary, "The remaining member becomes primary.");
        }

        [Test]
        public void Set_AddOfAnExistingMember_PromotesItToPrimary_WithoutDuplicating()
        {
            var t = NewTemplate();
            var a = NewBuilding(t, 1); var b = NewBuilding(t, 2);
            var set = new BuildingSelectionSet();
            set.Add(a); set.Add(b);

            Assert.IsFalse(set.Add(a), "Already a member.");
            Assert.AreEqual(2, set.Count);
            Assert.AreSame(a, set.Primary);
        }

        [Test]
        public void Set_CollapseTo_KeepsOnlyThatBuilding()
        {
            var t = NewTemplate();
            var a = NewBuilding(t, 1); var b = NewBuilding(t, 2); var c = NewBuilding(t, 3);
            var set = new BuildingSelectionSet();
            set.Add(a); set.Add(b); set.Add(c);

            set.CollapseTo(b);
            Assert.AreEqual(1, set.Count);
            Assert.AreSame(b, set.Primary);

            set.CollapseTo(null);
            Assert.AreEqual(0, set.Count);
        }

        /// <summary>The editor deletes by SetActive(false) and an erase-undo destroys, and
        /// neither tells the set. Both must fall out on Prune, or a group delete walks a
        /// building the author can no longer see.</summary>
        [Test]
        public void Set_Prune_DropsDestroyedAndDeactivatedMembers()
        {
            var t = NewTemplate();
            var a = NewBuilding(t, 1); var b = NewBuilding(t, 2); var c = NewBuilding(t, 3);
            var set = new BuildingSelectionSet();
            set.Add(a); set.Add(b); set.Add(c);

            b.gameObject.SetActive(false);
            UnityEngine.Object.DestroyImmediate(c.gameObject);

            Assert.AreEqual(2, set.Prune());
            Assert.AreEqual(1, set.Count);
            Assert.AreSame(a, set.Primary);
        }

        [Test]
        public void Set_Items_IsASnapshot_NotTheBackingList()
        {
            var t = NewTemplate();
            var a = NewBuilding(t, 1);
            var set = new BuildingSelectionSet();
            set.Add(a);
            var items = set.Items;
            set.Clear();
            Assert.AreEqual(1, items.Count, "A snapshot handed out must not shrink under its reader.");
        }

        // ── The click, per scope ─────────────────────────────────────────────

        [Test]
        public void SimpleScope_ClickReplacesTheSelection_AndGroundClearsIt()
        {
            var ed = NewEditor("Simple");
            var t  = NewTemplate();
            var a  = NewBuilding(t, 1); var b = NewBuilding(t, 2);

            Click(ed, a);
            Click(ed, b);
            s_reconcile.Invoke(ed, null);
            Assert.AreSame(b, ActiveOf(ed));
            Assert.AreEqual(1, SelectionOf(ed).Count, "Simple scope never holds more than one.");

            Click(ed, null);
            Assert.IsNull(ActiveOf(ed), "Clicking something that is not a building deselects.");
            Assert.AreEqual(0, SelectionOf(ed).Count);
        }

        [Test]
        public void MultipleScope_ClickTogglesMembership_LastClickedIsPrimary()
        {
            var ed = NewEditor("Multiple");
            var t  = NewTemplate();
            var a  = NewBuilding(t, 1); var b = NewBuilding(t, 2); var c = NewBuilding(t, 3);

            Click(ed, a); Click(ed, b); Click(ed, c);
            Assert.AreEqual(3, SelectionOf(ed).Count);
            Assert.AreSame(c, ActiveOf(ed));

            Click(ed, c);   // toggle the primary off
            Assert.AreEqual(2, SelectionOf(ed).Count);
            Assert.AreSame(b, ActiveOf(ed), "Removing the primary hands the role to the previous member.");
        }

        [Test]
        public void MultipleScope_ClickOnGround_ClearsTheGroup()
        {
            var ed = NewEditor("Multiple");
            var t  = NewTemplate();
            Click(ed, NewBuilding(t, 1)); Click(ed, NewBuilding(t, 2));

            Click(ed, null);
            Assert.AreEqual(0, SelectionOf(ed).Count);
            Assert.IsNull(ActiveOf(ed));
        }

        /// <summary>Eleven places write <c>_activeBuilding</c> and none of them know the set
        /// exists. The per-frame reconcile is what keeps a stale group from outliving them.</summary>
        [Test]
        public void Reconcile_FollowsWhoeverWroteTheActiveBuilding()
        {
            var ed = NewEditor("Multiple");
            var t  = NewTemplate();
            var a  = NewBuilding(t, 1); var b = NewBuilding(t, 2); var c = NewBuilding(t, 3);
            Click(ed, a); Click(ed, b);

            // Something else (a paste, a restore) made c active without touching the set.
            s_activeBuildingField.SetValue(ed, c);
            s_reconcile.Invoke(ed, null);
            Assert.AreSame(c, SelectionOf(ed).Primary);
            Assert.AreEqual(3, SelectionOf(ed).Count);

            // Something else (the picker) nulled it: the whole group goes.
            s_activeBuildingField.SetValue(ed, null);
            s_reconcile.Invoke(ed, null);
            Assert.AreEqual(0, SelectionOf(ed).Count);
        }

        [Test]
        public void NarrowingToSimple_KeepsThePrimaryAndDropsTheRest()
        {
            var ed = NewEditor("Multiple");
            var t  = NewTemplate();
            var a  = NewBuilding(t, 1); var b = NewBuilding(t, 2);
            Click(ed, a); Click(ed, b);

            typeof(BuildingsRuntimeEditor).GetMethod("SetSelectScope", Any)
                .Invoke(ed, new object[] { Enum.Parse(s_scopeType, "Simple") });
            Assert.AreEqual(1, SelectionOf(ed).Count);
            Assert.AreSame(b, SelectionOf(ed).Primary);
            Assert.AreSame(b, ActiveOf(ed));
        }

        // ── Group copy ───────────────────────────────────────────────────────

        /// <summary>A group is stored as OFFSETS from its anchor (the primary, last). Absolute
        /// positions would paste the group on top of the originals.</summary>
        [Test]
        public void GroupCopy_StoresEveryMember_AsOffsetsFromThePrimary()
        {
            var ed = NewEditor("Multiple");
            var t  = NewTemplate();
            var a  = NewBuilding(t, 1, new Vector3(10f, 5f, 0f));
            var b  = NewBuilding(t, 2, new Vector3(13f, 5f, 0f));
            var c  = NewBuilding(t, 3, new Vector3(10f, 9f, 0f));
            Click(ed, a); Click(ed, b); Click(ed, c);   // c is primary → anchor

            Assert.IsTrue((bool)s_copy.Invoke(ed, null));
            Assert.AreEqual(3, ed.ClipboardCount);

            var list = (IList)s_clipboardField.GetValue(ed);
            Vector3 OffsetOf(int i) => (Vector3)list[i].GetType().GetField("Offset").GetValue(list[i]);
            int      IdOf(int i)    => ((BuildingTemplateData)list[i].GetType().GetField("Template").GetValue(list[i])).templateId;

            Assert.AreEqual(Vector3.zero,           OffsetOf(2), "The anchor is last and carries no offset.");
            Assert.AreEqual(new Vector3(0f, -4f, 0f), OffsetOf(0), "a sits four units below c.");
            Assert.AreEqual(new Vector3(3f, -4f, 0f), OffsetOf(1), "b sits three right and four below c.");
            Assert.AreEqual(7, IdOf(0));
        }

        // ── The Tools panel ──────────────────────────────────────────────────

        private static BuildingsEditorUIBuilder.UIRefs BuildUI(Transform root, Action onSelect, Action onSimple, Action onMultiple)
        {
            return BuildingsEditorUIBuilder.BuildAll(
                root,
                onDropdownToggle: _ => { },
                onUndo: () => { }, onRedo: () => { },
                onSave: () => { }, onReload: () => { },
                onModeSelect: onSelect, onModePlace: () => { },
                onModeResize: () => { }, onModeDelete: () => { },
                onAddBuilding: () => { }, onRemoveBuilding: () => { }, onAddOnSystem: () => { },
                onToggleTutorial: () => { },
                onSearchChanged: _ => { },
                onSplitChanged: _ => { },
                onZBottomMinus: () => { }, onZBottomPlus: () => { },
                onZTopMinus: () => { }, onZTopPlus: () => { },
                onGridColsMinus: () => { }, onGridColsPlus: () => { },
                onGridRowsMinus: () => { }, onGridRowsPlus: () => { },
                onColliderScope: () => { },
                onInteractable: () => { },
                onPaintSolid: () => { }, onPaintWalk: () => { }, onSaveCU: () => { },
                onDeleteBuilding: () => { },
                onResetBuilding: () => { },
                onToggleCollidersVisible: () => { },
                onCollScopeToggle: () => { },
                onBrushPaint: () => { },
                onBrushErase: () => { },
                onCollBrushSizeChanged: _ => { },
                onCollBrushSizeStepDown: () => { },
                onCollBrushSizeStepUp: () => { },
                onToggleBuildingsVisible: () => { },
                onSelectSimple: onSimple, onSelectMultiple: onMultiple);
        }

        private Transform NewCanvasRoot()
        {
            var canvasGo = new GameObject("TestCanvas");
            _sceneObjects.Add(canvasGo);
            canvasGo.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var root = new GameObject("Root", typeof(RectTransform));
            root.transform.SetParent(canvasGo.transform, false);
            return root.transform;
        }

        [Test]
        public void ToolsPanel_HasASelectButton_WithTheScopeAsItsSubLabel_AndAHiddenFlyout()
        {
            int selectClicks = 0, simple = 0, multiple = 0;
            var refs = BuildUI(NewCanvasRoot(), () => selectClicks++, () => simple++, () => multiple++);

            Assert.IsNotNull(refs.SelectBtnImg, "The Select tool button was not built.");
            Assert.IsNotNull(refs.SelectBtnSubText, "The scope sub-label was not built.");
            Assert.AreEqual("Simple", refs.SelectBtnSubText.text, "Simple is the historical default.");

            refs.SelectBtnImg.GetComponent<Button>().onClick.Invoke();
            Assert.AreEqual(1, selectClicks);

            Assert.IsNotNull(refs.SelectSubPanel);
            Assert.IsFalse(refs.SelectSubPanel.activeSelf, "The flyout is a menu: closed until the tool is clicked.");
            refs.SelectSimpleBtnImg.GetComponent<Button>().onClick.Invoke();
            refs.SelectMultipleBtnImg.GetComponent<Button>().onClick.Invoke();
            Assert.AreEqual(1, simple);
            Assert.AreEqual(1, multiple);
        }

        /// <summary>
        /// The Tools panel's height is a hand-maintained formula over its button count, and
        /// its own comment says what happens when the two disagree: the last button is
        /// clipped off the bottom with no other symptom. Count the BTN_H-tall children the
        /// panel really builds against the constant.
        /// </summary>
        [Test]
        public void ToolsPanel_Height_CoversEveryButtonItBuilds()
        {
            var refs = BuildUI(NewCanvasRoot(), () => { }, () => { }, () => { });
            var builder = typeof(BuildingsEditorUIBuilder);
            float btnH   = (float)builder.GetField("BTN_H", Any).GetValue(null);
            float modesH = (float)builder.GetField("MODES_H", Any).GetValue(null);
            // The header height is the Tile editor helpers' constant, reached through
            // `using static`, so it is not a member of the Buildings builder.
            float hdrH   = Valkur.Gameplay.TileEditor.TileEditorUIHelpers.PANEL_HDR_H;

            var buttons = refs.ModesDropdown.GetComponentsInChildren<LayoutElement>(true)
                .Where(le => Mathf.Approximately(le.preferredHeight, btnH))
                .ToList();
            Assert.GreaterOrEqual(buttons.Count, 6, "Select, Undo, Redo, Fill, Erase, Door.");

            // The formula the constant is written as: 88 covers two buttons, the multiplier
            // pays for the rest, plus the header.
            float needed = 88f + btnH * (buttons.Count - 2) + hdrH;
            Assert.GreaterOrEqual(modesH, needed - 0.5f,
                $"MODES_H ({modesH}) does not cover {buttons.Count} buttons ({needed}); the last one is clipped.");
        }
    }
}
