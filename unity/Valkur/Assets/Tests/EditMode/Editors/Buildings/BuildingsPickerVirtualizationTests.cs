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

namespace Valkur.Tests.EditMode.Editors.Buildings
{
    /// <summary>
    /// The picker grid is virtualised: the content rect is sized for every template, and
    /// only the rows in view (plus overscan) exist as GameObjects. Measured before: the
    /// full rebuild of 1474 slots cost 772 ms and 53 MB, and ran on every slot click.
    ///
    /// The two constraints that hold it up are pinned here because both are the obvious
    /// thing to "add back": a layout group would stack whatever slots exist from the top
    /// and put row 150 where row 0 belongs, and a size fitter would shrink the content to
    /// the realised window and make the rest unreachable.
    /// </summary>
    [TestFixture]
    public class BuildingsPickerVirtualizationTests
    {
        private const int TEMPLATE_COUNT = 300;

        private readonly List<Object> _cleanup = new List<Object>();
        private BuildingsRuntimeEditor _editor;
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
            var (scroll, content) = EditorUIHelpers.MakeGridPicker(canvasGo.transform, "BuildingGrid", 3, 80f, 4f);
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

        private static int MaxWindowSlots =>
            (BuildingsRuntimeEditor.PICKER_FALLBACK_VISIBLE_ROWS + BuildingsRuntimeEditor.PICKER_OVERSCAN_ROWS * 2 + 1)
            * BuildingsRuntimeEditor.PICKER_COLUMNS;

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
        public void ContentRect_IsSizedForEveryRow_NotForTheWindow()
        {
            Invoke("RefreshPicker");

            float expected = BuildingsRuntimeEditor.PickerContentHeight(TEMPLATE_COUNT);
            Assert.AreEqual(expected, _content.sizeDelta.y, 0.01f,
                "The scrollbar range comes from the content height; it must cover rows that do not exist yet.");

            int rows = (TEMPLATE_COUNT + BuildingsRuntimeEditor.PICKER_COLUMNS - 1) / BuildingsRuntimeEditor.PICKER_COLUMNS;
            Assert.AreEqual(
                BuildingsRuntimeEditor.PICKER_PAD * 2f + rows * BuildingsRuntimeEditor.PICKER_CELL
                    + (rows - 1) * BuildingsRuntimeEditor.PICKER_SPACING,
                expected, 0.01f);
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

            float pitch = BuildingsRuntimeEditor.PickerRowPitch;
            var slot4 = (RectTransform)_editor.PickerSlotObjectAt(4).transform;   // row 1, col 1
            Assert.AreEqual(BuildingsRuntimeEditor.PICKER_PAD + 1 * pitch, slot4.anchoredPosition.x, 0.01f);
            Assert.AreEqual(-(BuildingsRuntimeEditor.PICKER_PAD + 1 * pitch), slot4.anchoredPosition.y, 0.01f);
            Assert.AreEqual("B5", slot4.name, "A slot is named after the template it shows.");
        }

        [Test]
        public void Scrolling_MovesTheWindow_AndRecyclesSlots()
        {
            Invoke("RefreshPicker");
            var firstBefore = _editor.PickerSlotObjectAt(0);
            Assert.IsNotNull(firstBefore);

            // Scroll 40 rows down (top-anchored content: y grows positive).
            _content.anchoredPosition = new Vector2(0f, 40 * BuildingsRuntimeEditor.PickerRowPitch);
            Invoke("UpdatePickerVisibleSlots");

            Assert.IsNull(_editor.PickerSlotObjectAt(0), "Row 0 left the window and was recycled.");
            Assert.IsNotNull(_editor.PickerSlotObjectAt(40 * 3), "Row 40 entered the window.");
            Assert.LessOrEqual(_editor.PickerRealizedSlotCount, MaxWindowSlots);

            int active = 0;
            foreach (Transform c in _content) if (c.gameObject.activeSelf) active++;
            Assert.AreEqual(_editor.PickerRealizedSlotCount, active,
                "Recycled slots go inactive into the pool; the active count IS the window.");
            Assert.LessOrEqual(_content.childCount, MaxWindowSlots * 2,
                "Pooled: scrolling must reuse slots, not accumulate them.");
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

        [Test]
        public void WheelNotch_MovesOneRow()
        {
            Invoke("RefreshPicker");
            Assert.AreEqual(BuildingsRuntimeEditor.PickerRowPitch, _scroll.scrollSensitivity, 0.01f,
                "The default 20 px on a 41,000 px list was two thousand notches from top to bottom.");
        }
    }
}
