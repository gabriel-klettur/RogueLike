using System.Collections.Generic;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Game.UI
{
    /// <summary>
    /// EditMode tests for the opt-in tab grouping in <see cref="PropertyForm"/>
    /// (PropertyForm.Tabs.cs).
    ///
    /// Two properties decide whether this feature is useful or infuriating, and both are
    /// pinned here:
    ///
    /// 1. IT IS OPT-IN. Ten runtime editors build this form — Spells, Items, Entities, FSM,
    ///    Inventory, Lighting, Buildings, Boss, Map, Camera — and none of them was touched
    ///    when tabs landed. Every one of them positions rows by insertion order inside the
    ///    form's VerticalLayoutGroup. A form that never calls BeginTab must therefore build
    ///    the same transforms, in the same order, with no strip and no page containers.
    ///    <see cref="FormWithoutBeginTab_BuildsNoStrip_SoTheTenUntouchedEditorsGainNoTransforms"/>
    ///    and <see cref="FormWithoutBeginTab_KeepsRowsAsDirectChildrenInInsertionOrder"/>
    ///    are the assertions that protect them.
    ///
    /// 2. THE SELECTED TAB SURVIVES A REBUILD. Editors Clear() and re-add every row on every
    ///    selection change, and the Particles preset panel goes further and rebuilds after
    ///    every accepted edit. A tab that snapped back to the first one each time would make
    ///    tabs strictly worse than the 56-row wall they replace, so the remembered choice
    ///    lives on the component, not in the hierarchy Clear() destroys.
    ///
    /// THE EDIT-MODE DEFECT THIS FILE FOUND. Clear() used to destroy rows with Object.Destroy
    /// unconditionally. Outside Play Mode Unity refuses that call — it logs "Destroy may not
    /// be called from edit mode" and the GameObjects simply stay — so a Clear() followed by a
    /// rebuild left the previous build's rows, pages and strip hanging beside the fresh ones.
    /// Writing these tests is what surfaced it; Clear() now routes through DestroyRow, which
    /// destroys immediately when not playing. <see cref="ClearAndReap"/> survives as a
    /// belt-and-braces sweep, and is expected to find nothing left to remove.
    /// <see cref="SelectedTab_SurvivesABareClear_ProvingTheMemoryIsNotInTheHierarchy"/>
    /// deliberately does NOT reap, to show the reap is a convenience of the test rig and not
    /// what makes the contract pass.
    /// </summary>
    public class PropertyFormTabsTests
    {
        private readonly List<GameObject> _sceneObjects = new List<GameObject>();

        private GameObject   _canvasGo;
        private PropertyForm _form;

        [SetUp]
        public void SetUp()
        {
            // Every row this form builds is a uGUI Graphic or a TMP text, and both complain
            // during EditMode construction about state a real canvas pass would have filled
            // in. Clear() also logs the edit-mode Destroy refusal described in the class
            // comment. None of that is the behaviour under test.
            LogAssert.ignoreFailingMessages = true;

            // A Canvas ancestor is mandatory, not cosmetic: PropertyForm.AddHeader and
            // TabStrip's own labels write tmp.fontStyle / tmp.color immediately after
            // AddComponent<TextMeshProUGUI>, which throws a NullReferenceException when
            // CanvasUpdateRegistry has no canvas to initialize against.
            _canvasGo = new GameObject("PropertyFormTestCanvas", typeof(RectTransform));
            _canvasGo.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            _sceneObjects.Add(_canvasGo);

            _form = PropertyForm.Create(_canvasGo.transform, "TestForm");
        }

        [TearDown]
        public void TearDown()
        {
            // Domain Reload is off in this project, so anything left in the scene leaks into
            // the next test rather than dying with the play session.
            foreach (var go in _sceneObjects)
                if (go != null) Object.DestroyImmediate(go);
            _sceneObjects.Clear();
            _canvasGo = null;
            _form     = null;

            LogAssert.ignoreFailingMessages = false;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// The shape the Particles preset panel is heading for: two identity rows pinned
        /// above the strip, then three tabs. Every row is AddText on purpose — AddInt and
        /// AddFloat parse with the ambient culture, and a decimal comma would read "1.5" as
        /// 15 on a Spanish machine and make an unrelated assertion flaky.
        /// </summary>
        private void BuildTabbedForm()
        {
            _form.AddText("id",   "Id",   "PP_ember");
            _form.AddText("kind", "Kind", "ambient");

            _form.BeginTab("EMISSION");
            _form.AddText("emission_rate", "Rate", "40");

            _form.BeginTab("MOTION");
            _form.AddText("motion_speed",   "Speed",   "one");
            _form.AddText("motion_gravity", "Gravity", "zero");

            _form.BeginTab("COLOR");
            _form.AddText("color_tint", "Tint", "#FF8800FF");
        }

        /// <summary>
        /// Clear(), then assert it actually cleared. Clear() is expected to empty the form in
        /// edit mode as well as in Play — it routes through DestroyRow, which switches to
        /// DestroyImmediate when not playing — so the sweep below is a guard, not a fixup: if
        /// it ever has something to remove, Clear() has regressed to the deferred
        /// Object.Destroy that edit mode refuses, and every "is there exactly one X"
        /// assertion in this file would start counting two.
        /// </summary>
        private static void ClearAndReap(PropertyForm form)
        {
            form.Clear();

            Assert.AreEqual(0, form.transform.childCount,
                "Clear() must leave the form empty immediately. Anything still here means it " +
                "is deferring destruction again, which edit mode refuses outright and which " +
                "would double every row of the next rebuild.");

            for (int i = form.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(form.transform.GetChild(i).gameObject);
        }

        private static List<string> ChildNames(Transform t)
        {
            var names = new List<string>(t.childCount);
            for (int i = 0; i < t.childCount; i++) names.Add(t.GetChild(i).name);
            return names;
        }

        /// <summary>The container holding one tab's rows, or null if that tab was not built.</summary>
        private GameObject Page(string tabLabel)
        {
            // Transform.Find sees inactive children, which matters here: every tab but the
            // selected one is deactivated, and GameObject.Find would miss them all.
            var t = _form.transform.Find("Tab_" + tabLabel);
            return t != null ? t.gameObject : null;
        }

        /// <summary>
        /// A row by its DISPLAY label (PropertyForm names rows "Row_" + label, not by key).
        /// Pass null for <paramref name="tabLabel"/> to look for a pinned row directly under
        /// the form; the two lookups are deliberately distinct so a test can assert a row is
        /// in one place and not in the other.
        /// </summary>
        private GameObject Row(string tabLabel, string rowLabel)
        {
            var parent = tabLabel == null ? _form.transform : _form.transform.Find("Tab_" + tabLabel);
            if (parent == null) return null;
            var row = parent.Find("Row_" + rowLabel);
            return row != null ? row.gameObject : null;
        }

        private TMP_InputField InputIn(string tabLabel, string rowLabel)
        {
            var row = Row(tabLabel, rowLabel);
            return row == null ? null : row.GetComponentInChildren<TMP_InputField>(true);
        }

        // ── 1. The opt-in guarantee ───────────────────────────────────────────────

        [Test]
        public void FormWithoutBeginTab_BuildsNoStrip_SoTheTenUntouchedEditorsGainNoTransforms()
        {
            _form.AddHeader("GENERAL");
            _form.AddText("name", "Name", "Ember");
            _form.AddBool("loop", "Loop", true);

            Assert.IsTrue(_form.GetComponentInChildren<TabStrip>(true) == null,
                "A form that never calls BeginTab must not build a TabStrip. Every editor " +
                "that has not adopted tabs — Spells, Items, Entities, FSM, Inventory, " +
                "Lighting, Buildings, Boss, Map, Camera — would otherwise grow a strip of " +
                "zero tabs eating 24 px in its properties panel.");

            Assert.IsTrue(_form.transform.Find("Tabs") == null,
                "No strip GameObject either. Adopting tabs has to be free for the forms that " +
                "do not ask for them, or the design is wrong.");

            Assert.IsTrue(_form.SelectedTab == null,
                "An untabbed form has no selected tab; reporting one would make callers " +
                "believe rows are being routed somewhere they are not.");
        }

        [Test]
        public void FormWithoutBeginTab_KeepsRowsAsDirectChildrenInInsertionOrder()
        {
            _form.AddHeader("GENERAL");
            _form.AddText("name",  "Name",  "Ember");
            _form.AddInt("count",  "Count", 3);
            _form.AddBool("loop",  "Loop",  true);
            _form.AddColor("tint", "Tint",  Color.red);

            CollectionAssert.AreEqual(
                new[] { "Header_GENERAL", "Row_Name", "Row_Count", "Row_Loop", "Row_Tint" },
                ChildNames(_form.transform),
                "Rows must stay DIRECT children of the form, in the order they were added. " +
                "The form's VerticalLayoutGroup lays out by sibling index, so an extra " +
                "wrapper transform or a reordering silently rearranges ten editors' " +
                "properties panels — a section header would end up over the wrong block of " +
                "fields, and nothing would throw to say so.");

            Assert.AreEqual(5, _form.transform.childCount,
                "Exactly the five rows that were added: no page container, no strip, no " +
                "spacer. Anything extra is a transform the untabbed editors did not ask for.");
        }

        // ── 2. Rows land in the open tab, and only that tab is visible ────────────

        [Test]
        public void Rows_LandInTheTabThatWasOpenWhenTheyWereAdded()
        {
            BuildTabbedForm();

            Assert.IsTrue(Row("EMISSION", "Rate")    != null, "Rate was added while EMISSION was open.");
            Assert.IsTrue(Row("MOTION",   "Speed")   != null, "Speed was added while MOTION was open.");
            Assert.IsTrue(Row("MOTION",   "Gravity") != null, "Gravity was added while MOTION was open.");
            Assert.IsTrue(Row("COLOR",    "Tint")    != null, "Tint was added while COLOR was open.");

            Assert.IsTrue(Row(null, "Speed") == null,
                "Speed must NOT also be a direct child of the form. A row that leaks out of " +
                "its page is visible under every tab, which is the wall tabs exist to remove.");
        }

        [Test]
        public void OnlyTheSelectedTabsRows_AreOnScreen()
        {
            BuildTabbedForm();

            Assert.AreEqual("EMISSION", _form.SelectedTab,
                "The first tab registered is the one on screen when nothing else was asked for.");

            Assert.IsTrue(Row("EMISSION", "Rate").activeInHierarchy,
                "The selected tab's rows must be visible — otherwise the panel opens blank.");
            Assert.IsFalse(Row("MOTION", "Speed").activeInHierarchy,
                "A non-selected tab's rows must be hidden. They are hidden with SetActive " +
                "rather than a CanvasGroup precisely so the ScrollRect's ContentSizeFitter " +
                "stops reserving their height; a visible row here means the scroll is also " +
                "as tall as every tab put together.");
            Assert.IsFalse(Row("COLOR", "Tint").activeInHierarchy,
                "Same for every further tab, not only the second one.");

            _form.SelectTab("MOTION");

            Assert.AreEqual("MOTION", _form.SelectedTab,
                "SelectTab must move the form to the named tab; if it does not, clicking a " +
                "tab in the strip appears to do nothing.");
            Assert.IsTrue(Row("MOTION", "Speed").activeInHierarchy,
                "Switching tabs has to reveal the new tab's rows.");
            Assert.IsFalse(Row("EMISSION", "Rate").activeInHierarchy,
                "…and hide the old tab's. Two tabs on screen at once is the 56-row wall back " +
                "again, with a strip on top of it.");
        }

        // ── 3. Pinned rows ────────────────────────────────────────────────────────

        [Test]
        public void PinnedRows_StayVisibleWhicheverTabIsSelected()
        {
            BuildTabbedForm();

            var id   = Row(null, "Id");
            var kind = Row(null, "Kind");

            Assert.IsTrue(id != null && kind != null,
                "Rows added before the first BeginTab stay direct children of the form. That " +
                "is where an editor puts the identity fields — the preset id and its kind — " +
                "it wants readable no matter which group of settings is open.");

            var strip = _form.transform.Find("Tabs");
            Assert.IsTrue(strip != null, "Three BeginTab calls must have built the strip.");
            Assert.Less(kind.transform.GetSiblingIndex(), strip.GetSiblingIndex(),
                "The strip is created on the first BeginTab and appended after whatever rows " +
                "already exist, so pinned rows sit ABOVE it. Below it they would read as " +
                "belonging to whichever tab is open.");

            foreach (var tab in new[] { "EMISSION", "MOTION", "COLOR" })
            {
                _form.SelectTab(tab);
                Assert.IsTrue(id.activeInHierarchy,
                    "Pinned row 'Id' vanished while '" + tab + "' was selected. Pinned rows " +
                    "are never members of a page, so nothing may deactivate them; if they " +
                    "blink out on a tab switch, the editor loses the field identifying what " +
                    "it is even editing.");
                Assert.IsTrue(kind.activeInHierarchy,
                    "Pinned row 'Kind' vanished while '" + tab + "' was selected.");
            }
        }

        // ── 4. Reaching a row on a hidden tab ─────────────────────────────────────

        [Test]
        public void SetValue_ReachesAnInputRowOnAHiddenTab_WithoutThrowing()
        {
            BuildTabbedForm();

            var hidden = InputIn("MOTION", "Speed");
            Assert.IsTrue(hidden != null, "MOTION/Speed must exist even while MOTION is hidden.");
            Assert.IsFalse(hidden.gameObject.activeInHierarchy,
                "Precondition: this row really is on a deactivated page, so the test is " +
                "exercising the hidden case and not accidentally the visible one.");

            Assert.DoesNotThrow(() => _form.SetValue("motion_speed", "twelve"),
                "Pushing a value into a hidden row is a normal event, not an error: an " +
                "editor clamps a field, or reloads the selection, and writes every key it " +
                "owns without caring which tab happens to be open. Throwing here would abort " +
                "the editor's entire refresh at whichever key first landed on a hidden tab, " +
                "leaving the rows after it showing the previous preset's values.");

            Assert.AreEqual("twelve", hidden.text,
                "The value must actually land. The key map is independent of the hierarchy, " +
                "and TMP resyncs from its stored text on OnEnable, so what is written now is " +
                "what the user sees the first time they open MOTION. If this is stale, the " +
                "tab shows the pre-edit value.");
        }

        [Test]
        public void SetValue_ReachesAToggleRowOnAHiddenTab()
        {
            _form.BeginTab("EMISSION");
            _form.AddText("emission_rate", "Rate", "40");

            _form.BeginTab("MOTION");
            _form.AddBool("motion_loop", "Loop", true);

            var loopRow = Row("MOTION", "Loop");
            Assert.IsTrue(loopRow != null, "AddBool must have built its row inside the MOTION page.");
            var toggle = loopRow.GetComponentInChildren<Toggle>(true);
            Assert.IsTrue(toggle != null && !toggle.gameObject.activeInHierarchy,
                "Precondition: the toggle is on the hidden MOTION page.");

            Assert.DoesNotThrow(() => _form.SetValue("motion_loop", false),
                "SetValue routes bools to Toggle.SetIsOnWithoutNotify. A hidden toggle must " +
                "take the write like any other; the bool branch is separate code from the " +
                "input-field branch and can regress on its own.");
            Assert.IsFalse(toggle.isOn,
                "The toggle must hold the new state so it draws correctly the first time " +
                "MOTION is revealed — a checkbox that lies about a preset's loop flag is a " +
                "bug the designer only finds by saving and reloading.");
        }

        [Test]
        public void ValueChanged_StillFiresForARowOnAHiddenTab()
        {
            BuildTabbedForm();

            string capturedKey   = null;
            object capturedValue = null;
            _form.ValueChanged = (k, v) => { capturedKey = k; capturedValue = v; };

            var hidden = InputIn("MOTION", "Gravity");
            Assert.IsTrue(hidden != null, "MOTION/Gravity must exist even while MOTION is hidden.");
            Assert.IsFalse(hidden.gameObject.activeInHierarchy,
                "Precondition: the row committing its edit is on a hidden page.");

            // The listener PropertyForm attached in AddText hangs off onEndEdit, and a
            // UnityEvent invokes its runtime listeners whether or not the object is active —
            // which is exactly why this happens for real. Hiding a page disables a focused
            // TMP_InputField, and TMP answers OnDisable with DeactivateInputField, which ends
            // in onEndEdit: the user's last keystroke is committed from a row that is already
            // off screen.
            hidden.onEndEdit.Invoke("cinders");

            Assert.AreEqual("motion_gravity", capturedKey,
                "ValueChanged must still reach the editor for a row on a hidden tab, under " +
                "the row's own key. Swallowing it loses the value the user typed immediately " +
                "before switching tabs — the edit looks accepted and is silently dropped.");
            Assert.AreEqual("cinders", capturedValue,
                "…and carry the committed value, not a stale or empty one.");
        }

        // ── 5. The rebuild guarantee ──────────────────────────────────────────────

        [Test]
        public void SelectedTab_SurvivesClearAndRebuild_OfTheSameTabSet()
        {
            BuildTabbedForm();
            _form.SelectTab("MOTION");
            Assert.AreEqual("MOTION", _form.SelectedTab, "Precondition: a non-first tab is open.");

            // Exactly what an editor does when its selection changes, and what the Particles
            // panel does after EVERY accepted edit.
            ClearAndReap(_form);
            BuildTabbedForm();

            Assert.AreEqual("MOTION", _form.SelectedTab,
                "This is the whole reason tabs are worth having here. The Particles panel " +
                "rebuilds the form on every value the user commits; if the tab reset to " +
                "EMISSION each time, editing three fields in MOTION would mean three trips " +
                "back across the strip, which is worse than the scroll tabs replaced.");

            Assert.IsTrue(Page("MOTION").activeSelf,
                "The remembered tab must be the one actually on screen after the rebuild, " +
                "not merely the one SelectedTab reports.");
            Assert.IsFalse(Page("EMISSION").activeSelf,
                "…and the first tab must not have been left showing underneath it.");
            Assert.IsTrue(Row("MOTION", "Speed").activeInHierarchy,
                "The rebuilt rows of the remembered tab are the ones the user sees.");
        }

        [Test]
        public void SelectedTab_SurvivesABareClear_ProvingTheMemoryIsNotInTheHierarchy()
        {
            BuildTabbedForm();
            _form.SelectTab("COLOR");

            // Clear() called exactly as production calls it, with no help from the fixture.
            // Whether the old strip and pages are gone or still hanging off the form is beside
            // the point here: the remembered tab has to come from the component's own field,
            // never from something that happened to survive in the hierarchy.
            _form.Clear();
            BuildTabbedForm();

            Assert.AreEqual("COLOR", _form.SelectedTab,
                "The remembered tab lives on the PropertyForm component. Parking it on the " +
                "strip or on a page would tie it to objects Clear() destroys, and the user's " +
                "choice would die with them on the first rebuild.");
        }

        [Test]
        public void SelectedTab_FallsBackToTheFirstTab_WhenTheRebuildDropsIt()
        {
            BuildTabbedForm();
            _form.SelectTab("MOTION");

            // A preset of a different kind: no MOTION section at all. This is the ordinary
            // case in the Particles panel, where the row set follows the preset's kind.
            ClearAndReap(_form);
            _form.AddText("id", "Id", "PP_glow");
            _form.BeginTab("EMISSION");
            _form.AddText("emission_rate", "Rate", "12");
            _form.BeginTab("COLOR");
            _form.AddText("color_tint", "Tint", "#3388FFFF");

            Assert.IsTrue(Page("MOTION") == null, "Precondition: MOTION was not rebuilt.");

            Assert.AreEqual("EMISSION", _form.SelectedTab,
                "When the remembered tab is missing from the new set the form must fall back " +
                "to the first tab — not throw, and not sit on a tab that no longer exists.");
            Assert.IsTrue(Row("EMISSION", "Rate").activeInHierarchy,
                "Something has to be on screen. A blank properties panel with a live strip " +
                "above it reads as the editor having crashed on the preset you just clicked.");
            Assert.IsFalse(Page("COLOR").activeSelf,
                "Falling back must still select exactly one tab, not reveal all of them.");
        }

        [Test]
        public void SelectedTab_ReturnsToTheRememberedTab_WhenThatTabComesBack()
        {
            BuildTabbedForm();
            _form.SelectTab("MOTION");

            // Step onto a preset with no MOTION section…
            ClearAndReap(_form);
            _form.BeginTab("EMISSION");
            _form.AddText("emission_rate", "Rate", "12");
            Assert.AreEqual("EMISSION", _form.SelectedTab, "Precondition: the form fell back.");

            // …and back onto one that has it.
            ClearAndReap(_form);
            BuildTabbedForm();

            Assert.AreEqual("MOTION", _form.SelectedTab,
                "A fallback must not be mistaken for the user changing their mind. The form " +
                "remembers what was ASKED FOR, not what it managed to show, so clicking down " +
                "a list of presets where only some carry a MOTION section keeps landing the " +
                "user back on MOTION instead of stranding them on EMISSION after the first " +
                "preset that lacked it.");
        }

        // ── 6. Degenerate calls ───────────────────────────────────────────────────

        [Test]
        public void BeginTab_WithTheSameLabelTwice_ResumesThePage_WithoutASecondStripOrContainer()
        {
            _form.BeginTab("EMISSION");
            _form.AddText("emission_rate", "Rate", "40");

            _form.BeginTab("MOTION");
            _form.AddText("motion_speed", "Speed", "one");

            // Returning to a group instead of having to emit all of its rows in one run —
            // which is how a builder that walks a field list section by section, or one
            // assembled from partials, ends up calling it.
            _form.BeginTab("EMISSION");
            _form.AddText("emission_burst", "Burst", "3");

            var strips = _form.GetComponentsInChildren<TabStrip>(true);
            Assert.AreEqual(1, strips.Length,
                "A second strip would stack a duplicate row of tab buttons under the first " +
                "and split the selection between two widgets, so clicking a tab in one would " +
                "leave the other still highlighting something else.");
            Assert.AreEqual(2, strips[0].Count,
                "EMISSION must be one tab, not two. A duplicate button reads as two " +
                "identical tabs the user has to guess between, and only one of them would " +
                "hold the rows added after the resume.");

            int emissionPages = 0;
            foreach (var name in ChildNames(_form.transform))
                if (name == "Tab_EMISSION") emissionPages++;
            Assert.AreEqual(1, emissionPages,
                "One container per label. A second container would leave the rows added " +
                "before the resume in a page nothing can ever show again.");

            Assert.IsTrue(Row("EMISSION", "Rate")  != null, "The row added on the first run stays.");
            Assert.IsTrue(Row("EMISSION", "Burst") != null,
                "The row added after the resume must join the SAME page. Landing anywhere " +
                "else scatters one section across the panel.");
        }

        [Test]
        public void BeginTab_WithAnEmptyOrNullLabel_DoesNotThrowAndBuildsNoStrip()
        {
            Assert.DoesNotThrow(() => _form.BeginTab(null),
                "A tab label comes from data — a section name off a preset, a catalog field — " +
                "so it can arrive empty. Throwing would take down the whole panel build for " +
                "one blank string in one asset.");
            Assert.DoesNotThrow(() => _form.BeginTab(string.Empty));

            _form.AddText("name", "Name", "Ember");

            Assert.IsTrue(_form.GetComponentInChildren<TabStrip>(true) == null,
                "An ignored BeginTab must not half-build the feature. A strip carrying a " +
                "nameless tab is a button the user can neither identify nor leave.");
            Assert.IsTrue(Row(null, "Name") != null,
                "The row after an ignored BeginTab stays where it would have been — pinned " +
                "under the form. Routing it into a page that was never created loses it.");
        }

        [Test]
        public void BeginTab_WithAnEmptyLabel_LeavesTheOpenTabAlone()
        {
            _form.BeginTab("EMISSION");
            _form.AddText("emission_rate", "Rate", "40");

            _form.BeginTab("");
            _form.AddText("emission_burst", "Burst", "3");

            Assert.IsTrue(Row("EMISSION", "Burst") != null,
                "Ignoring a bad label has to be a true no-op: the tab that was open stays " +
                "open. Quietly reverting to pinned would drop one row of a section above the " +
                "strip, where it then shows under every other tab as well.");
        }

        [Test]
        public void SelectTab_WithAnEmptyOrNullLabel_ReturnsFalseWithoutThrowing()
        {
            BuildTabbedForm();

            Assert.IsFalse(_form.SelectTab(null),
                "Nothing was selected, and the caller is told so rather than left to assume " +
                "the switch happened.");
            Assert.IsFalse(_form.SelectTab(string.Empty));
            Assert.AreEqual("EMISSION", _form.SelectedTab,
                "A rejected SelectTab must leave the form on the tab it was already showing, " +
                "and must not poison the remembered choice for the next rebuild.");
        }
    }
}
