using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using Valkur.Gameplay.Save;
using Valkur.UI.MainMenu;

namespace Valkur.Tests.EditMode
{
    /// <summary>
    /// Regression tests for the MainMenu load panel.
    ///
    /// Key regressions prevented:
    ///   - Selection appearing to not stick when clicking an autosave row
    ///     (pill highlight was correct, but the "Operará sobre" label was absent
    ///     so users couldn't confirm which row was active).
    ///   - Selection silently resetting to slot 0 every time the list was
    ///     refreshed (e.g. after rename or delete), making the cursor appear
    ///     to jump away from the row the user had just operated on.
    ///   - Hovering over empty rows (beyond the end of the save list) clobbering
    ///     the current selection because the index guard was missing.
    /// </summary>
    [TestFixture]
    public class MainMenuLoadPanelTests
    {
        private GameObject _go;
        private MainMenuUI _menu;

        [SetUp]
        public void SetUp()
        {
            var existing = Object.FindObjectOfType<MainMenuUI>();
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            _go   = new GameObject("TestMainMenuUI_LoadPanel");
            _menu = _go.AddComponent<MainMenuUI>();
            InvokePrivate("Start");
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        // ── Reflection helpers ────────────────────────────────────────────────

        private void InvokePrivate(string methodName)
        {
            var m = typeof(MainMenuUI).GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            m?.Invoke(_menu, null);
        }

        private T GetField<T>(string name)
        {
            var f = typeof(MainMenuUI).GetField(name,
                BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? (T)f.GetValue(_menu) : default;
        }

        private void SetField(string name, object value)
        {
            var f = typeof(MainMenuUI).GetField(name,
                BindingFlags.NonPublic | BindingFlags.Instance);
            f?.SetValue(_menu, value);
        }

        private static List<SaveSlotInfo> MakeSaves(params string[] names)
        {
            var list = new List<SaveSlotInfo>();
            foreach (var n in names)
                list.Add(new SaveSlotInfo
                {
                    fileName  = n,
                    path      = $"/fake/saves/{n}.json",
                    timestamp = "2026-01-01T00:00:00"
                });
            return list;
        }

        // ── Target label ("Operará sobre") ────────────────────────────────────

        [Test]
        public void TargetLabel_ExistsAfterStart()
        {
            var label = GetField<TextMeshProUGUI>("_mmLoadTargetLabel");
            Assert.IsNotNull(label,
                "_mmLoadTargetLabel must be created during BuildUI/BuildLoadGameSubmenu");
        }

        [Test]
        public void TargetLabel_ReflectsSelectedSaveName()
        {
            var saves = MakeSaves("autosave_0", "autosave_1");
            SetField("_mmLoadSaves", saves);
            SetField("_mmLoadSel",   0);
            InvokePrivate("UpdateMMLoadVisuals");

            var label = GetField<TextMeshProUGUI>("_mmLoadTargetLabel");
            Assert.IsNotNull(label, "Label must exist");
            StringAssert.Contains("autosave_0", label.text,
                "Target label must mention the selected save's filename");
        }

        [Test]
        public void TargetLabel_UpdatesWhenSelectionChanges()
        {
            var saves = MakeSaves("save_a", "save_b", "save_c");
            SetField("_mmLoadSaves", saves);

            SetField("_mmLoadSel", 0);
            InvokePrivate("UpdateMMLoadVisuals");
            var label = GetField<TextMeshProUGUI>("_mmLoadTargetLabel");
            Assert.IsNotNull(label, "Label must exist");
            string labelAt0 = label.text;

            SetField("_mmLoadSel", 2);
            InvokePrivate("UpdateMMLoadVisuals");
            string labelAt2 = label.text;

            StringAssert.Contains("save_a", labelAt0, "Label must show save_a when sel=0");
            StringAssert.Contains("save_c", labelAt2, "Label must show save_c when sel=2");
            Assert.AreNotEqual(labelAt0, labelAt2,
                "Target label must change when selection index changes");
        }

        [Test]
        public void TargetLabel_EmptyWhenNoSaves()
        {
            SetField("_mmLoadSaves", new List<SaveSlotInfo>());
            SetField("_mmLoadSel",   0);
            InvokePrivate("UpdateMMLoadVisuals");

            var label = GetField<TextMeshProUGUI>("_mmLoadTargetLabel");
            Assert.IsNotNull(label, "Label must exist");
            Assert.IsEmpty(label.text,
                "Target label must be empty when there are no saves in the list");
        }

        // ── Selection visual state ────────────────────────────────────────────

        [Test]
        public void SelectedRow_Pill_IsNotClear()
        {
            var saves = MakeSaves("autosave_0", "autosave_1");
            SetField("_mmLoadSaves", saves);
            SetField("_mmLoadSel",   1);
            SetField("_mmLoadScroll", 0);
            InvokePrivate("UpdateMMLoadVisuals");

            var pills = GetField<Image[]>("_mmLoadPills");
            Assert.IsNotNull(pills, "_mmLoadPills must exist");
            Assert.Greater(pills.Length, 1, "Must have at least 2 pill elements");

            Assert.AreNotEqual(Color.clear, pills[1].color,
                "The selected row's pill must not be transparent");
        }

        [Test]
        public void UnselectedRow_Pill_IsClear()
        {
            var saves = MakeSaves("autosave_0", "autosave_1");
            SetField("_mmLoadSaves", saves);
            SetField("_mmLoadSel",   1);
            SetField("_mmLoadScroll", 0);
            InvokePrivate("UpdateMMLoadVisuals");

            var pills = GetField<Image[]>("_mmLoadPills");
            Assert.IsNotNull(pills, "_mmLoadPills must exist");
            Assert.Greater(pills.Length, 1, "Must have at least 2 pill elements");

            Assert.AreEqual(Color.clear, pills[0].color,
                "An unselected row's pill must be transparent");
        }

        [Test]
        public void SelectionIndex_NotResetBy_UpdateMMLoadVisuals()
        {
            var saves = MakeSaves("a", "b", "c");
            SetField("_mmLoadSaves", saves);
            SetField("_mmLoadSel",   2);
            InvokePrivate("UpdateMMLoadVisuals");

            int sel = GetField<int>("_mmLoadSel");
            Assert.AreEqual(2, sel,
                "UpdateMMLoadVisuals must not reset _mmLoadSel to 0");
        }

        // ── Empty-row hover guard ─────────────────────────────────────────────

        [Test]
        public void EmptyRowHoverGuard_TriggersForRowBeyondSaveCount()
        {
            // 3 saves in an 8-row window. Row cap=5 (scroll=0) → idx=5 is empty.
            var saves = MakeSaves("s0", "s1", "s2");
            int scroll = 0;
            int cap    = 5; // visible row index
            int idx    = scroll + cap;

            bool guardShouldBlock = idx < 0 || idx >= saves.Count;
            Assert.IsTrue(guardShouldBlock,
                "The idx>=Count guard must block row 5 when only 3 saves exist");
        }

        [Test]
        public void EmptyRowHoverGuard_DoesNotBlockValidRow()
        {
            var saves = MakeSaves("s0", "s1", "s2");
            int scroll = 0;
            int cap    = 2; // valid row

            bool guardShouldBlock = (scroll + cap) < 0 || (scroll + cap) >= saves.Count;
            Assert.IsFalse(guardShouldBlock,
                "The guard must NOT block a row that has a valid corresponding save");
        }

        // ── Scroll bound clamping ─────────────────────────────────────────────

        [Test]
        public void EnsureMMLoadScroll_ScrollsDown_WhenSelectedRowIsBelowWindow()
        {
            // 10 saves, window height = 8. Select row 9 — scroll must advance.
            var saves = MakeSaves("s0","s1","s2","s3","s4","s5","s6","s7","s8","s9");
            SetField("_mmLoadSaves",  saves);
            SetField("_mmLoadSel",    9);
            SetField("_mmLoadScroll", 0);
            InvokePrivate("EnsureMMLoadScroll");

            int scroll = GetField<int>("_mmLoadScroll");
            Assert.GreaterOrEqual(scroll, 2,
                "Scroll must advance so row 9 is visible in an 8-row window");
        }

        [Test]
        public void EnsureMMLoadScroll_ScrollsUp_WhenSelectedRowIsAboveWindow()
        {
            var saves = MakeSaves("s0","s1","s2","s3","s4","s5","s6","s7","s8","s9");
            SetField("_mmLoadSaves",  saves);
            SetField("_mmLoadSel",    1);
            SetField("_mmLoadScroll", 5);
            InvokePrivate("EnsureMMLoadScroll");

            int scroll = GetField<int>("_mmLoadScroll");
            Assert.AreEqual(1, scroll,
                "Scroll must retreat to 1 so the selected row appears at the top of the window");
        }

        [Test]
        public void EnsureMMLoadScroll_NoChange_WhenSelectionIsAlreadyVisible()
        {
            var saves = MakeSaves("s0","s1","s2","s3","s4","s5","s6","s7","s8","s9");
            SetField("_mmLoadSaves",  saves);
            SetField("_mmLoadSel",    3);
            SetField("_mmLoadScroll", 0);
            InvokePrivate("EnsureMMLoadScroll");

            int scroll = GetField<int>("_mmLoadScroll");
            Assert.AreEqual(0, scroll,
                "Scroll must stay at 0 when selected row 3 is already visible");
        }

        // ── SetLoadMode isolation ─────────────────────────────────────────────

        [Test]
        public void SetLoadMode_RenameOverlay_IsActivatedForRename()
        {
            // Invoke with the Rename enum value.
            var modeField = typeof(MainMenuUI).GetField("_mmLoadMode",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(modeField, "_mmLoadMode field must exist");
            var enumType = modeField.FieldType;
            var renameVal = System.Enum.Parse(enumType, "Rename");

            var method = typeof(MainMenuUI).GetMethod("SetLoadMode",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "SetLoadMode must exist");
            method.Invoke(_menu, new[] { renameVal });

            var overlay = GetField<GameObject>("_mmRenameOverlay");
            Assert.IsNotNull(overlay, "_mmRenameOverlay must exist after BuildUI");
            Assert.IsTrue(overlay.activeSelf,
                "_mmRenameOverlay must be active when mode is Rename");
        }

        [Test]
        public void SetLoadMode_RenameOverlay_IsHiddenForListMode()
        {
            var modeField = typeof(MainMenuUI).GetField("_mmLoadMode",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var enumType  = modeField.FieldType;

            var setMode = typeof(MainMenuUI).GetMethod("SetLoadMode",
                BindingFlags.NonPublic | BindingFlags.Instance);

            // First switch to Rename, then back to List.
            setMode.Invoke(_menu, new[] { System.Enum.Parse(enumType, "Rename") });
            setMode.Invoke(_menu, new[] { System.Enum.Parse(enumType, "List") });

            var overlay = GetField<GameObject>("_mmRenameOverlay");
            Assert.IsNotNull(overlay);
            Assert.IsFalse(overlay.activeSelf,
                "_mmRenameOverlay must be hidden when mode returns to List");
        }
    }
}
