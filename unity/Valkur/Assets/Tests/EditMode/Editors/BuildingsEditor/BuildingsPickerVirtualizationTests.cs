using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay.Buildings;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Editors.BuildingsEditor
{
    /// <summary>
    /// The picker grid is virtualised AND reflowing: the content rect is sized for every
    /// template, only the rows in view (plus overscan) exist as GameObjects, and the column
    /// count comes from the live width rather than a hardcoded three.
    ///
    /// Both halves shipped broken. The rebuild of 1474 slots cost 772 ms and 53 MB and ran
    /// on every slot click; and three fixed 80 px columns in a 384 px panel left a 104 px
    /// dead strip down the right-hand side, which is what the panel looked like in the bug
    /// report that started this.
    ///
    /// The two constraints under the virtualisation are pinned here because both are the
    /// obvious thing to "add back": a layout group would stack whatever slots exist from the
    /// top and put row 150 where row 0 belongs, and a size fitter would shrink the content to
    /// the realised window and make the rest unreachable.
    /// </summary>
    [TestFixture]
    public class BuildingsPickerVirtualizationTests
    {
        private const int   TEMPLATE_COUNT = 300;
        private const float CANVAS_W       = 400f;
        private const float CANVAS_H       = 600f;

        private readonly List<Object> _cleanup = new List<Object>();
        private BuildingsRuntimeEditor _editor;
        private RectTransform          _canvasRt;
        private RectTransform          _content;
        private ScrollRect             _scroll;
        private TextMeshProUGUI        _status;
        private BuildingCatalog        _catalog;

        private static FieldInfo Field(string name)
            => typeof(BuildingsRuntimeEditor).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);

        private static MethodInfo Method(string name)
            => typeof(BuildingsRuntimeEditor).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);

        private void Invoke(string name, params object[] args) => Method(name).Invoke(_editor, args);

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            _cleanup.Add(canvasGo);
            // An explicit size so rect.width resolves without a layout pass — uGUI runs none
            // in Edit Mode, and the grid's whole job here is to answer to a real width.
            _canvasRt = canvasGo.GetComponent<RectTransform>();
            _canvasRt.sizeDelta = new Vector2(CANVAS_W, CANVAS_H);

            var (scroll, content) = EditorUIHelpers.MakeGridPicker(canvasGo.transform, "BuildingGrid", 3, 80f, 4f);
            EditorUIHelpers.AddVerticalScrollbar(scroll);
            _scroll  = scroll;
            _content = content;

            var statusGo = new GameObject("Status", typeof(RectTransform));
            statusGo.transform.SetParent(canvasGo.transform, false);
            _status = statusGo.AddComponent<TextMeshProUGUI>();

            _catalog = ScriptableObject.CreateInstance<BuildingCatalog>();
            _cleanup.Add(_catalog);
            for (int i = 0; i < TEMPLATE_COUNT; i++)
            {
                var tpl = ScriptableObject.CreateInstance<BuildingTemplateData>();
                tpl.templateId = i + 1;
                tpl.assetPath  = $"Buildings/structures/house_{i + 1}";
                _cleanup.Add(tpl);
                _catalog.AddTemplate(tpl);
            }

            var editorGo = new GameObject("BuildingsEditorUnderTest");
            _cleanup.Add(editorGo);
            _editor = editorGo.AddComponent<BuildingsRuntimeEditor>();
            Field("_pickerContent").SetValue(_editor, _content);
            Field("_pickerScroll").SetValue(_editor, _scroll);
            Field("_statusTmp").SetValue(_editor, _status);
            Field("_catalog").SetValue(_editor, _catalog);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _cleanup) if (o != null) Object.DestroyImmediate(o);
            _cleanup.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>Width the grid lays out against: the viewport, i.e. the canvas minus the scrollbar.</summary>
        private float ContentWidth => _scroll.viewport.rect.width;

        private int MaxWindowSlots =>
            (BuildingsRuntimeEditor.PICKER_FALLBACK_VISIBLE_ROWS + BuildingsRuntimeEditor.PICKER_OVERSCAN_ROWS * 2 + 2)
            * _editor.PickerColumns
            + _editor.PickerColumns * 4;   // slack: the viewport-derived row count is >= the fallback

        // ── The maths ────────────────────────────────────────────────────────────

        [TestCase(356f)]   // the shipped 384 px panel, less its padding and scrollbar
        [TestCase(244f)]   // the panel as it was before the width grew
        [TestCase(500f)]
        [TestCase(812f)]
        public void ResolvePickerMetrics_FillsTheRowExactly(float width)
        {
            BuildingsRuntimeEditor.ResolvePickerMetrics(width, out int columns, out float cell);

            Assert.GreaterOrEqual(columns, 1);
            Assert.GreaterOrEqual(cell, BuildingsRuntimeEditor.PICKER_MIN_CELL - 0.01f,
                "A cell below the minimum stops reading as building art.");
            Assert.LessOrEqual(cell, BuildingsRuntimeEditor.PICKER_MAX_CELL + 0.01f);

            float used = BuildingsRuntimeEditor.PICKER_PAD * 2f
                       + columns * cell
                       + (columns - 1) * BuildingsRuntimeEditor.PICKER_SPACING;
            Assert.AreEqual(width, used, 0.01f,
                $"{columns} columns of {cell:0.##} must fill {width:0.##} exactly — the slack IS the dead strip.");
        }

        [Test]
        public void ResolvePickerMetrics_OnTheShippedPanel_GivesFourColumns_NotThree()
        {
            // 384 px panel - 8 px of content padding each side - 12 px scrollbar = 356.
            BuildingsRuntimeEditor.ResolvePickerMetrics(356f, out int columns, out float cell);

            Assert.AreEqual(4, columns,
                "Three 80 px columns reach 252 px and left 104 px of nothing down the right.");
            Assert.AreEqual(84f, cell, 0.01f);
        }

        [Test]
        public void ResolvePickerMetrics_ZeroWidth_FallsBackInsteadOfCollapsing()
        {
            BuildingsRuntimeEditor.ResolvePickerMetrics(0f, out int columns, out float cell);

            Assert.Greater(columns, 1, "uGUI lays out nothing in Edit Mode; one column is not a sane default.");
            Assert.Greater(cell, 1f);
        }

        // ── The window ───────────────────────────────────────────────────────────

        [Test]
        public void RefreshPicker_RealisesAWindow_NotTheCatalog()
        {
            Invoke("RefreshPicker");

            Assert.AreEqual(TEMPLATE_COUNT, _editor.PickerVisibleCount, "Every template passes an empty filter.");
            Assert.LessOrEqual(_editor.PickerRealizedSlotCount, MaxWindowSlots,
                "Only the viewport window plus overscan may exist as GameObjects.");
            Assert.Less(_editor.PickerRealizedSlotCount, TEMPLATE_COUNT,
                "Realising every template is the 772 ms rebuild this replaces.");
            Assert.IsNotNull(_editor.PickerSlotObjectAt(0), "Row 0 is in the window.");
            Assert.IsNull(_editor.PickerSlotObjectAt(TEMPLATE_COUNT - 1), "The last row is not.");
        }

        [Test]
        public void TheGrid_LeavesNoDeadStrip_DownTheRight()
        {
            Invoke("RefreshPicker");

            float used = BuildingsRuntimeEditor.PICKER_PAD * 2f
                       + _editor.PickerColumns * _editor.PickerCell
                       + (_editor.PickerColumns - 1) * BuildingsRuntimeEditor.PICKER_SPACING;
            Assert.AreEqual(ContentWidth, used, 0.5f,
                "The row must span the content width; whatever it leaves over is visible as empty panel.");

            // And the right-most realised slot really does reach the edge.
            var last = _editor.PickerSlotObjectAt(_editor.PickerColumns - 1);
            Assert.IsNotNull(last, "The first row must be fully realised.");
            var rt = (RectTransform)last.transform;
            Assert.AreEqual(ContentWidth - BuildingsRuntimeEditor.PICKER_PAD,
                rt.anchoredPosition.x + rt.rect.width, 0.5f);
        }

        [Test]
        public void ContentRect_IsSizedForEveryRow_NotForTheWindow()
        {
            Invoke("RefreshPicker");

            int rows = (TEMPLATE_COUNT + _editor.PickerColumns - 1) / _editor.PickerColumns;
            float expected = BuildingsRuntimeEditor.PICKER_PAD * 2f
                           + rows * _editor.PickerCell
                           + (rows - 1) * BuildingsRuntimeEditor.PICKER_SPACING;

            Assert.AreEqual(expected, _content.sizeDelta.y, 0.01f,
                "The scrollbar range comes from the content height; it must cover rows that do not exist yet.");
        }

        [Test]
        public void Content_CarriesNoLayoutGroup_AndNoSizeFitter()
        {
            Invoke("RefreshPicker");

            Assert.IsNull(_content.GetComponent<LayoutGroup>(),
                "A layout group stacks whatever rows exist from the top and puts row 150 where row 0 belongs.");
            Assert.IsNull(_content.GetComponent<ContentSizeFitter>(),
                "A size fitter shrinks the content to the realised window and makes the rest unreachable.");
        }

        [Test]
        public void Slots_ArePlacedByIndex_AtAbsolutePositions()
        {
            Invoke("RefreshPicker");

            float pitch = _editor.PickerRowPitch;
            int   cols  = _editor.PickerColumns;
            int   index = cols + 1;                       // row 1, column 1
            var slot = (RectTransform)_editor.PickerSlotObjectAt(index).transform;

            Assert.AreEqual(BuildingsRuntimeEditor.PICKER_PAD + pitch, slot.anchoredPosition.x, 0.01f);
            Assert.AreEqual(-(BuildingsRuntimeEditor.PICKER_PAD + pitch), slot.anchoredPosition.y, 0.01f);
            Assert.AreEqual($"B{index + 1}", slot.name, "A slot is named after the template it shows.");
        }

        [Test]
        public void Scrolling_MovesTheWindow_AndRecyclesSlots()
        {
            Invoke("RefreshPicker");
            Assert.IsNotNull(_editor.PickerSlotObjectAt(0));

            const int scrollRows = 40;
            _content.anchoredPosition = new Vector2(0f, scrollRows * _editor.PickerRowPitch);
            Invoke("UpdatePickerVisibleSlots");

            Assert.IsNull(_editor.PickerSlotObjectAt(0), "Row 0 left the window and was recycled.");
            Assert.IsNotNull(_editor.PickerSlotObjectAt(scrollRows * _editor.PickerColumns),
                "Row 40 entered the window.");
            Assert.LessOrEqual(_editor.PickerRealizedSlotCount, MaxWindowSlots);

            int active = 0;
            foreach (Transform c in _content) if (c.gameObject.activeSelf) active++;
            Assert.AreEqual(_editor.PickerRealizedSlotCount, active,
                "Recycled slots go inactive into the pool; the active count IS the window.");
            Assert.LessOrEqual(_content.childCount, MaxWindowSlots * 2,
                "Pooled: scrolling must reuse slots, not accumulate them.");
        }

        // ── Reflow ───────────────────────────────────────────────────────────────

        [Test]
        public void WideningThePanel_AddsColumns_AndResizesTheSlotsThatExist()
        {
            Invoke("RefreshPicker");
            int   colsBefore = _editor.PickerColumns;
            float cellBefore = _editor.PickerCell;

            _canvasRt.sizeDelta = new Vector2(CANVAS_W * 2f, CANVAS_H);
            Invoke("UpdatePickerLayoutIfResized");

            Assert.Greater(_editor.PickerColumns, colsBefore,
                "Twice the width must show more buildings, not the same three columns and more emptiness.");

            float used = BuildingsRuntimeEditor.PICKER_PAD * 2f
                       + _editor.PickerColumns * _editor.PickerCell
                       + (_editor.PickerColumns - 1) * BuildingsRuntimeEditor.PICKER_SPACING;
            Assert.AreEqual(ContentWidth, used, 0.5f, "The wider row must fill the wider panel.");

            var slot = (RectTransform)_editor.PickerSlotObjectAt(1).transform;
            Assert.AreEqual(_editor.PickerCell, slot.rect.width, 0.01f,
                "A pooled slot outlives the cell size it was built at; realise must re-size it.");
            Assert.AreEqual(BuildingsRuntimeEditor.PICKER_PAD + _editor.PickerRowPitch,
                slot.anchoredPosition.x, 0.01f, "…and re-place it.");
            Assert.AreNotEqual(cellBefore, _editor.PickerCell, "Sanity: the cell really did change.");
        }

        [Test]
        public void NarrowingThePanel_DropsColumns_AndKeepsEveryTemplateReachable()
        {
            Invoke("RefreshPicker");
            int colsBefore = _editor.PickerColumns;

            _canvasRt.sizeDelta = new Vector2(200f, CANVAS_H);
            Invoke("UpdatePickerLayoutIfResized");

            Assert.Less(_editor.PickerColumns, colsBefore);
            Assert.GreaterOrEqual(_editor.PickerColumns, 1);

            int rows = (TEMPLATE_COUNT + _editor.PickerColumns - 1) / _editor.PickerColumns;
            float expected = BuildingsRuntimeEditor.PICKER_PAD * 2f
                           + rows * _editor.PickerCell
                           + (rows - 1) * BuildingsRuntimeEditor.PICKER_SPACING;
            Assert.AreEqual(expected, _content.sizeDelta.y, 0.01f,
                "Fewer columns means more rows; a content height left at the old row count " +
                "makes the tail of the catalog unscrollable.");
        }

        [Test]
        public void AnUnchangedWidth_CostsNothing()
        {
            Invoke("RefreshPicker");
            var slotBefore = _editor.PickerSlotObjectAt(0);

            Invoke("UpdatePickerLayoutIfResized");

            Assert.AreSame(slotBefore, _editor.PickerSlotObjectAt(0),
                "The per-frame check must early-out on an unchanged width, not rebuild the window.");
        }

        [Test]
        public void WheelNotch_TracksTheLiveRowPitch()
        {
            Invoke("RefreshPicker");
            Assert.AreEqual(_editor.PickerRowPitch, _scroll.scrollSensitivity, 0.01f,
                "The default 20 px on a 41,000 px list was two thousand notches from top to bottom.");

            _canvasRt.sizeDelta = new Vector2(CANVAS_W * 2f, CANVAS_H);
            Invoke("UpdatePickerLayoutIfResized");
            Assert.AreEqual(_editor.PickerRowPitch, _scroll.scrollSensitivity, 0.01f,
                "A reflow changes the row pitch, and the wheel has to follow it.");
        }

        // ── Selection ────────────────────────────────────────────────────────────

        [Test]
        public void Selection_RepaintsTint_WithoutRebuildingSlots()
        {
            Invoke("RefreshPicker");
            var slot1 = _editor.PickerSlotObjectAt(1);

            Field("_selectedTemplateId").SetValue(_editor, 2);   // index 1 shows template 2
            Invoke("RefreshPickerSelection");

            Assert.AreSame(slot1, _editor.PickerSlotObjectAt(1),
                "Selecting must not destroy and recreate the slot the author just clicked.");
            var selected = slot1.GetComponent<Button>();
            Assert.AreEqual(EditorUIHelpers.SLOT_SELECTED, selected.colors.normalColor,
                "The tint lives in the ColorBlock, or Unity's next transition wipes it.");
            var other = _editor.PickerSlotObjectAt(0).GetComponent<Button>();
            Assert.AreEqual(EditorUIHelpers.SLOT_BG, other.colors.normalColor);
        }

        [Test]
        public void SelectTemplate_DoesNotCallRefreshPicker()
        {
            string src = File.ReadAllText(Path.Combine(Application.dataPath,
                "_Project/Scripts/Gameplay/Editors/Buildings/BuildingsRuntimeEditor.Picker.cs"));
            var body = Regex.Match(src, @"private void SelectTemplate\(int id\)(.*?)\n        \}", RegexOptions.Singleline);
            Assert.IsTrue(body.Success, "SelectTemplate must exist in Picker.cs.");
            StringAssert.DoesNotContain("RefreshPicker()", body.Groups[1].Value,
                "A click on a slot must repaint the selection, never rebuild the grid — that was the 772 ms per click.");
            StringAssert.Contains("RefreshPickerSelection()", body.Groups[1].Value);
        }

        [Test]
        public void PickerCode_NeverWritesSlotImageColourDirectly()
        {
            foreach (var file in new[] { "BuildingsRuntimeEditor.Picker.cs", "BuildingsRuntimeEditor.Picker.Virtual.cs" })
            {
                string src = File.ReadAllText(Path.Combine(Application.dataPath,
                    "_Project/Scripts/Gameplay/Editors/Buildings", file));
                Assert.IsFalse(Regex.IsMatch(src, @"GetComponent<Image>\(\)\.color\s*="),
                    $"{file}: a slot colour written onto the Image survives until the Button's next transition. Use SetSlotTint.");
            }
        }

        // ── Filters ──────────────────────────────────────────────────────────────

        [Test]
        public void Search_NarrowsTheList_AndTheStatusLine()
        {
            Field("_searchFilter").SetValue(_editor, "12");
            Invoke("RefreshPicker");

            int expected = Enumerable.Range(1, TEMPLATE_COUNT).Count(id => id.ToString().Contains("12"));
            Assert.AreEqual(expected, _editor.PickerVisibleCount);
            Assert.AreEqual($"{expected} match '12'", _status.text);
        }

        [Test]
        public void EmptyResult_LeavesNoSlots_AndNoHeight()
        {
            Field("_searchFilter").SetValue(_editor, "zzz-nothing");
            Invoke("RefreshPicker");

            Assert.AreEqual(0, _editor.PickerVisibleCount);
            Assert.AreEqual(0, _editor.PickerRealizedSlotCount);
            Assert.AreEqual(0f, _content.sizeDelta.y, 0.01f);
            Assert.AreEqual("0 match 'zzz-nothing'", _status.text);
        }

        [Test]
        public void StatusLine_KeepsTheShippedFormat_WithoutAFilter()
        {
            Invoke("RefreshPicker");
            Assert.AreEqual($"{TEMPLATE_COUNT} templates", _status.text);
        }
    }
}
