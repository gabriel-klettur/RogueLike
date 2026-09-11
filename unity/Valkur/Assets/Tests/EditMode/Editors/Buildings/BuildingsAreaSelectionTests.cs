using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.Buildings
{
    /// <summary>
    /// The Select tool's Area scope: box a region, select every building that touches it.
    ///
    /// <para>The geometry is <see cref="BuildingAreaQuery"/> and is measured directly. The
    /// selection half is measured through <c>SelectFromArea</c>, which calls nothing that
    /// persists — same boundary as the other Buildings fixtures. The gesture itself (press,
    /// drag, release) reads the pointer and is not driven here.</para>
    /// </summary>
    [TestFixture]
    public class BuildingsAreaSelectionTests
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
        private static readonly MethodInfo s_selectFromArea =
            typeof(BuildingsRuntimeEditor).GetMethod("SelectFromArea", Any);
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

        private BuildingTemplateData NewTemplate()
        {
            var t = ScriptableObject.CreateInstance<BuildingTemplateData>();
            t.templateId    = 7;
            t.name          = "test_house";
            t.originalScale = new Vector2Int(64, 64);   // 2 x 2 world units at PPU 32
            _assets.Add(t);
            return t;
        }

        /// <summary>A building whose ground line is at <paramref name="feet"/>; its rect is
        /// centred on x and grows 2 units up from y (the fallback rect path).</summary>
        private BuildingObject NewBuilding(BuildingTemplateData t, int id, Vector2 feet)
        {
            var go = new GameObject("B_" + id);
            go.transform.position = new Vector3(feet.x, feet.y, 0f);
            _sceneObjects.Add(go);
            var b = go.AddComponent<BuildingObject>();
            s_templateField.SetValue(b, t);
            b.InstanceId = id;
            b.ZoneName   = "Lobby";
            return b;
        }

        private BuildingsRuntimeEditor NewAreaEditor()
        {
            var go = new GameObject("BuildingsEditorUnderTest");
            _sceneObjects.Add(go);
            var ed = go.AddComponent<BuildingsRuntimeEditor>();
            s_scopeField.SetValue(ed, Enum.Parse(s_scopeType, "Area"));
            return ed;
        }

        private static BuildingSelectionSet SelectionOf(BuildingsRuntimeEditor ed) =>
            (BuildingSelectionSet)s_selectionField.GetValue(ed);

        private static int Box(BuildingsRuntimeEditor ed, Rect area, bool additive) =>
            (int)s_selectFromArea.Invoke(ed, new object[] { area, additive });

        // ── Geometry ─────────────────────────────────────────────────────────

        /// <summary>A drag up-and-left hands the second corner below and left of the first.
        /// A Rect built naively from that has a negative size and Overlaps answers wrongly.</summary>
        [Test]
        public void FromCorners_NormalisesADragInAnyDirection()
        {
            var r1 = BuildingAreaQuery.FromCorners(new Vector2(1, 1), new Vector2(5, 4));
            var r2 = BuildingAreaQuery.FromCorners(new Vector2(5, 4), new Vector2(1, 1));
            var r3 = BuildingAreaQuery.FromCorners(new Vector2(5, 1), new Vector2(1, 4));

            Assert.AreEqual(r1, r2);
            Assert.AreEqual(r1, r3);
            Assert.AreEqual(1f, r1.xMin); Assert.AreEqual(5f, r1.xMax);
            Assert.AreEqual(1f, r1.yMin); Assert.AreEqual(4f, r1.yMax);
        }

        /// <summary>Touching is enough — a box across the fronts of a row must take the row.
        /// Containment would select nothing until the box swallowed every canopy.</summary>
        [Test]
        public void Overlapping_TakesPartialOverlap_AndSkipsDisjointAndDeleted()
        {
            var t = NewTemplate();
            var inside   = NewBuilding(t, 1, new Vector2(10f, 10f));   // rect x 9..11, y 10..12
            var clipped  = NewBuilding(t, 2, new Vector2(13f, 10f));   // rect x 12..14 — box reaches 12.5
            var outside  = NewBuilding(t, 3, new Vector2(20f, 10f));
            var deleted  = NewBuilding(t, 4, new Vector2(10f, 10f));
            deleted.gameObject.SetActive(false);

            var box  = Rect.MinMaxRect(8f, 9f, 12.5f, 11f);
            var hits = BuildingAreaQuery.Overlapping(new[] { inside, clipped, outside, deleted, null }, box);

            CollectionAssert.AreEquivalent(new[] { inside, clipped }, hits);
        }

        [Test]
        public void Overlapping_ATouchOnTheEdgeCounts_ButAGapDoesNot()
        {
            var t = NewTemplate();
            var b = NewBuilding(t, 1, new Vector2(10f, 10f));   // rect x 9..11

            Assert.AreEqual(1, BuildingAreaQuery.Overlapping(new[] { b }, Rect.MinMaxRect(0f, 0f, 9.01f, 20f)).Count,
                "A box that reaches into the rect by a hair touches it.");
            Assert.AreEqual(0, BuildingAreaQuery.Overlapping(new[] { b }, Rect.MinMaxRect(0f, 0f, 8.99f, 20f)).Count,
                "A box that stops short does not.");
        }

        // ── Selection ────────────────────────────────────────────────────────

        [Test]
        public void Box_ReplacesTheSelection_AndTheLastHitIsPrimary()
        {
            var ed = NewAreaEditor();
            var t  = NewTemplate();
            var a  = NewBuilding(t, 1, new Vector2(10f, 10f));
            var b  = NewBuilding(t, 2, new Vector2(13f, 10f));
            var far = NewBuilding(t, 3, new Vector2(40f, 40f));
            SelectionOf(ed).Add(far);
            s_activeBuildingField.SetValue(ed, far);

            int hits = Box(ed, Rect.MinMaxRect(8f, 9f, 15f, 11f), additive: false);

            Assert.AreEqual(2, hits);
            Assert.AreEqual(2, SelectionOf(ed).Count, "The previous selection is replaced, not kept.");
            Assert.IsFalse(SelectionOf(ed).Contains(far));
            Assert.IsNotNull(s_activeBuildingField.GetValue(ed), "A box with hits leaves a primary.");
        }

        [Test]
        public void ShiftBox_AddsToTheSelection()
        {
            var ed = NewAreaEditor();
            var t  = NewTemplate();
            var a  = NewBuilding(t, 1, new Vector2(10f, 10f));
            var far = NewBuilding(t, 3, new Vector2(40f, 40f));
            SelectionOf(ed).Add(far);
            s_activeBuildingField.SetValue(ed, far);

            Box(ed, Rect.MinMaxRect(8f, 9f, 12f, 11f), additive: true);

            Assert.AreEqual(2, SelectionOf(ed).Count);
            Assert.IsTrue(SelectionOf(ed).Contains(far), "Additive keeps what was there.");
            Assert.AreSame(a, SelectionOf(ed).Primary);
        }

        [Test]
        public void AnEmptyBox_ClearsTheSelection_UnlessAdditive()
        {
            var ed = NewAreaEditor();
            var t  = NewTemplate();
            var far = NewBuilding(t, 3, new Vector2(40f, 40f));
            SelectionOf(ed).Add(far);
            s_activeBuildingField.SetValue(ed, far);

            Assert.AreEqual(0, Box(ed, Rect.MinMaxRect(0f, 0f, 1f, 1f), additive: true));
            Assert.AreEqual(1, SelectionOf(ed).Count, "Shift-boxing empty ground changes nothing.");

            Assert.AreEqual(0, Box(ed, Rect.MinMaxRect(0f, 0f, 1f, 1f), additive: false));
            Assert.AreEqual(0, SelectionOf(ed).Count, "A plain empty box is how you deselect with the mouse.");
            Assert.IsNull(s_activeBuildingField.GetValue(ed));
        }

        // ── The flyout ───────────────────────────────────────────────────────

        [Test]
        public void TheScopeFlyout_HasAnAreaRow_AndIsTallEnoughForThree()
        {
            var canvasGo = new GameObject("TestCanvas");
            _sceneObjects.Add(canvasGo);
            canvasGo.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var root = new GameObject("Root", typeof(RectTransform));
            root.transform.SetParent(canvasGo.transform, false);

            int area = 0;
            var refs = BuildingsEditorUIBuilder.BuildAll(
                root.transform,
                onDropdownToggle: _ => { },
                onUndo: () => { }, onRedo: () => { },
                onSave: () => { }, onReload: () => { },
                onModeSelect: () => { }, onModePlace: () => { },
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
                onSelectArea: () => area++);

            Assert.IsNotNull(refs.SelectAreaBtnImg, "The Area row was not built.");
            refs.SelectAreaBtnImg.GetComponent<Button>().onClick.Invoke();
            Assert.AreEqual(1, area);

            // Three BTN_H rows must fit the flyout's declared height, or the last one is
            // clipped off the bottom with no other symptom — the Tools panel's own trap.
            var builder = typeof(BuildingsEditorUIBuilder);
            float btnH = (float)builder.GetField("BTN_H", Any).GetValue(null);
            float subH = (float)builder.GetField("SELECT_SUB_H", Any).GetValue(null);
            float hdrH = Valkur.Gameplay.TileEditor.TileEditorUIHelpers.PANEL_HDR_H;
            int rows = refs.SelectSubPanel.GetComponentsInChildren<LayoutElement>(true)
                .Count(le => Mathf.Approximately(le.preferredHeight, btnH));
            Assert.AreEqual(3, rows);
            Assert.GreaterOrEqual(subH, hdrH + btnH * rows, "SELECT_SUB_H does not cover its rows.");
        }
    }
}
