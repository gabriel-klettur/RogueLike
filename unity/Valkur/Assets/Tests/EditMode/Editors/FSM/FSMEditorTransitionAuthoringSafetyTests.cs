using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Gameplay.Enemies.FSM;
using static Valkur.Tests.EditMode.Editors.FSM.FSMEditorTestSupport;

namespace Valkur.Tests.EditMode.Editors.FSM
{
    /// <summary>
    /// F12 transition-authoring safety: the panel now says out loud what the runtime was
    /// already doing silently.
    ///
    ///   • Connect commits a guard-less edge (FSMCondition.Parse("") == null, and
    ///     StateMachine treats null as PASS) — the status line must name the edge and say
    ///     it is UNCONDITIONAL, without blocking creation.
    ///   • Committing the condition field runs author-time diagnostics: a parse error
    ///     (which makes FSMRuntimeFactory drop the WHOLE edge at load) and the
    ///     misspelled-signal trap (an unknown left term falls to GetContextFloat(term, 0f)
    ///     and evaluates as 0 forever) both reach the status line. Advice only — the save
    ///     is never rejected.
    ///   • The cooldown row is relabeled from the raw 'cooldown_frames' key and carries a
    ///     computed seconds hint (load divides by a hardcoded 60; the clock advances only
    ///     while in the from-state) that updates in place on commit.
    ///   • Dead fields are marked: the 'when' row (the runtime reads guard first and this
    ///     editor always writes one), and the Actions / Blackboard / per-state props data
    ///     (round-trips through sets.json, but no runtime code executes it yet) each get
    ///     an inert banner in the Entities panel's existing warning hue (ENT_GAP_COLOR).
    ///
    /// Every test redirects persistence to a temp directory via
    /// <see cref="FSMEditorTestSupport.CreateEditorWithTempData"/> — never the real
    /// <c>StreamingAssets/FSM/</c>.
    /// </summary>
    [TestFixture]
    public class FSMEditorTransitionAuthoringSafetyTests
    {
        private readonly List<TempFsmEditor> _handles = new List<TempFsmEditor>();

        [TearDown]
        public void TearDown()
        {
            foreach (var h in _handles) h.Dispose();
            _handles.Clear();
        }

        private TempFsmEditor NewEditor()
        {
            var h = CreateEditorWithTempData();
            _handles.Add(h);
            return h;
        }

        // ── Fixture helpers ──────────────────────────────────────────────────────

        /// <summary>Editor with a two-state set and one Idle→Chase transition installed.</summary>
        private static FSMRuntimeEditor.FSMTransitionData InstallTwoStateSetWithEdge(TempFsmEditor h)
        {
            var set = MakeTestSet();
            AddState(set, "IdleState");
            AddState(set, "ChaseState");
            var tr = AddTransition(set, "IdleState", "ChaseState");
            InstallSet(h.Editor, set);
            return tr;
        }

        /// <summary>Selects the props tab by name — the enum is private, so parse it.</summary>
        private static void SetPropsTab(TempFsmEditor h, string tabName)
        {
            var f = Field(h.Editor, "_propsTab");
            f.SetValue(h.Editor, System.Enum.Parse(f.FieldType, tabName));
        }

        private static void ShowTransitionTab(TempFsmEditor h, FSMRuntimeEditor.FSMTransitionData tr)
        {
            SetField(h.Editor, "_selectedTransition", tr);
            SetField(h.Editor, "_selectedState", null);
            SetPropsTab(h, "Transition");
            Invoke(h.Editor, "RefreshProperties");
        }

        private static string Status(TempFsmEditor h)
            => GetField<TextMeshProUGUI>(h.Editor, "_statusTmp").text;

        private static Transform FindRow(TempFsmEditor h, string name)
            => FindChildRecursive(h.GameObject.transform, name);

        /// <summary>Drives a property row's input field exactly like a designer's Enter.</summary>
        private static void CommitRow(TempFsmEditor h, string rowName, string value)
        {
            var row = FindRow(h, rowName);
            Assert.IsNotNull(row, $"Row '{rowName}' must exist before it can be committed.");
            var input = row.GetComponentInChildren<TMP_InputField>(true);
            Assert.IsNotNull(input, $"Row '{rowName}' must carry an input field.");
            input.onEndEdit.Invoke(value);
        }

        // ── (1) Empty guard at creation ──────────────────────────────────────────

        [Test]
        public void Connect_NewEdge_StatusNamesEdgeAndWarnsUnconditional()
        {
            var h = NewEditor();
            var set = MakeTestSet();
            AddState(set, "IdleState");
            AddState(set, "ChaseState");
            InstallSet(h.Editor, set);

            Invoke(h.Editor, "HandleConnectClickFrom", "IdleState", true);
            Invoke(h.Editor, "HandleConnectClickFrom", "ChaseState", true);

            StringAssert.Contains("UNCONDITIONAL", Status(h),
                "A guard-less edge fires on its first eligible frame — the status line must " +
                "say so the moment the arrow is drawn, not leave it for a play-test.");
            StringAssert.Contains("IdleState", Status(h), "The warning must name the edge.");
            StringAssert.Contains("ChaseState", Status(h), "The warning must name the edge.");

            Assert.AreEqual(1, set.transitions.Count,
                "The warning is advice — creation must NOT be blocked (an unconditional " +
                "edge is legitimate).");
        }

        // ── (2) Signal-name validation at author time ────────────────────────────

        [Test]
        public void ConditionCommit_ParseError_StatusCarriesTheFactoryDropWarning()
        {
            var h = NewEditor();
            var tr = InstallTwoStateSetWithEdge(h);
            ShowTransitionTab(h, tr);

            const string bad = "hp_pct <";
            Valkur.Gameplay.FSM.FSMCondition.Parse(bad, out string expectedError);
            Assert.IsNotNull(expectedError, "Fixture text must actually be a parse error.");

            CommitRow(h, "__FSMPropsRow_condition", bad);

            StringAssert.Contains(expectedError, Status(h),
                "The status line must surface FSMCondition.Parse's own error, because " +
                "FSMRuntimeFactory drops the whole edge at load on it.");
            Assert.AreEqual(bad, tr.raw["guard"],
                "Author-time advice only — the text must still be saved, never rejected.");
        }

        [Test]
        public void ConditionCommit_MisspelledSignal_WarnsItReadsAsContextKeyZero()
        {
            var h = NewEditor();
            var tr = InstallTwoStateSetWithEdge(h);
            ShowTransitionTab(h, tr);

            CommitRow(h, "__FSMPropsRow_condition", "hp_pctt < 0.25");

            StringAssert.Contains("hp_pctt", Status(h),
                "The warning must name the unrecognised term.");
            StringAssert.Contains("context key", Status(h),
                "The trap: an unknown term falls to GetContextFloat(term, 0f), so " +
                "'hp_pctt < 0.25' is permanently true. The status line must explain the " +
                "mechanism, not just say 'unknown'.");
            StringAssert.Contains("0", Status(h));
            Assert.AreEqual("hp_pctt < 0.25", tr.raw["guard"],
                "Never rejected — a genuine context key (aggro_range, …) is legitimate.");
        }

        [Test]
        public void ConditionCommit_BuiltInSignalsOnly_DoesNotWarnAboutContextKeys()
        {
            var h = NewEditor();
            var tr = InstallTwoStateSetWithEdge(h);
            ShowTransitionTab(h, tr);

            CommitRow(h, "__FSMPropsRow_condition", "hp_pct < 0.25 && state_time > 2");

            StringAssert.DoesNotContain("context key", Status(h),
                "Both left terms are FSMCondition consts — a false positive here would " +
                "train designers to ignore the real misspelled-signal warning.");
        }

        [Test]
        public void ConditionCommit_Cleared_ReportsUnconditional()
        {
            var h = NewEditor();
            var tr = InstallTwoStateSetWithEdge(h);
            ShowTransitionTab(h, tr);

            CommitRow(h, "__FSMPropsRow_condition", "");

            StringAssert.Contains("UNCONDITIONAL", Status(h),
                "Clearing the guard re-arms the fires-immediately behavior — the status " +
                "line must restate it at the moment it happens.");
        }

        // ── (3) Cooldown row ─────────────────────────────────────────────────────

        [Test]
        public void TransitionTab_CooldownRow_RelabeledWithComputedSecondsHint()
        {
            var h = NewEditor();
            var tr = InstallTwoStateSetWithEdge(h);
            tr.cooldownFrames = 180;
            ShowTransitionTab(h, tr);

            Assert.IsNull(FindRow(h, "__FSMPropsRow_cooldown_frames"),
                "The raw key name is the lie — FSMRuntimeFactory divides it by a hardcoded " +
                "60 at load, so it is seconds at a reference rate, not frames.");
            Assert.IsNotNull(FindRow(h, "__FSMPropsRow_cooldown (frames)"),
                "The relabeled cooldown row must exist.");

            var hint = FindRow(h, "__FSMPropsRow_hint_cooldown");
            Assert.IsNotNull(hint, "The computed hint row must exist under the cooldown row.");
            string expected = (string)Invoke(h.Editor, "ComposeCooldownHint", 180);
            Assert.AreEqual(expected, hint.GetComponent<TextMeshProUGUI>().text);
            StringAssert.Contains("180 = ", expected);
            StringAssert.Contains(" s, counted only while in the from-state", expected,
                "StateMachine tests AppliesTo before the cooldown, so the clock advances " +
                "only on ticks spent in the from-state — the hint must say so.");
        }

        [Test]
        public void CooldownCommit_UpdatesHintInPlace_AndRoundTripsTheRawKey()
        {
            var h = NewEditor();
            var tr = InstallTwoStateSetWithEdge(h);
            tr.cooldownFrames = 180;
            ShowTransitionTab(h, tr);

            CommitRow(h, "__FSMPropsRow_cooldown (frames)", "90");

            Assert.AreEqual(90, tr.cooldownFrames);
            Assert.AreEqual(90L, tr.raw["cooldown_frames"],
                "The relabel is display-only — the persisted key must stay 'cooldown_frames' " +
                "or the runtime factory stops seeing it.");
            string expected = (string)Invoke(h.Editor, "ComposeCooldownHint", 90);
            Assert.AreEqual(expected,
                FindRow(h, "__FSMPropsRow_hint_cooldown").GetComponent<TextMeshProUGUI>().text,
                "The hint must recompute with the committed value without a full tab " +
                "rebuild (rebuilding inside onEndEdit destroys the field mid-callback).");
        }

        // ── (4) Dead-field markers ───────────────────────────────────────────────

        [Test]
        public void TransitionTab_WhenRow_IsMarkedInert_ButStillRoundTrips()
        {
            var h = NewEditor();
            var tr = InstallTwoStateSetWithEdge(h);
            ShowTransitionTab(h, tr);

            Assert.IsNotNull(FindRow(h, "__FSMPropsRow_when"),
                "The row must keep existing — seed-generated data round-trips through it.");
            Assert.IsNotNull(FindRow(h, "__FSMPropsRow_inert_when"),
                "SyncSetToRaw always writes a 'guard' key and the runtime reads " +
                "guard ?? when ?? condition, so 'when' can never win — typing a guard here " +
                "must not look identical to typing one that works.");
        }

        [Test]
        public void ActionsTab_ShowsInertBanner()
        {
            var h = NewEditor();
            var tr = InstallTwoStateSetWithEdge(h);
            SetField(h.Editor, "_selectedTransition", tr);
            SetPropsTab(h, "Actions");
            Invoke(h.Editor, "RefreshProperties");

            Assert.IsNotNull(FindRow(h, "__FSMPropsRow_inert_actions"),
                "'actions' reaches no runtime code (verified by grep) — a designer " +
                "authoring rows here must be told nothing executes them yet.");
        }

        [Test]
        public void BlackboardTab_ShowsInertBanner()
        {
            var h = NewEditor();
            var tr = InstallTwoStateSetWithEdge(h);
            SetField(h.Editor, "_selectedTransition", tr);
            SetPropsTab(h, "Blackboard");
            Invoke(h.Editor, "RefreshProperties");

            Assert.IsNotNull(FindRow(h, "__FSMPropsRow_inert_blackboard"),
                "'blackboard' reaches no runtime code (verified by grep) — same trap, " +
                "same banner.");
        }

        [Test]
        public void StateTab_PropsSection_ShowsInertBanner_AndOtherBannersDoNotLeak()
        {
            var h = NewEditor();
            var tr = InstallTwoStateSetWithEdge(h);

            // Visit the Actions tab first so a leaked banner would be caught below —
            // BuildPropertiesRows must clear every PROPS_ROW_TAG child between refreshes.
            SetField(h.Editor, "_selectedTransition", tr);
            SetPropsTab(h, "Actions");
            Invoke(h.Editor, "RefreshProperties");

            var set = GetField<FSMRuntimeEditor.FSMSetData>(h.Editor, "_selectedSet");
            var node = set.states[0];
            SetField(h.Editor, "_selectedState", node);
            SetField(h.Editor, "_selectedTransition", null);
            SetPropsTab(h, "State");
            Invoke(h.Editor, "RefreshProperties");

            Assert.IsNotNull(FindRow(h, "__FSMPropsRow_inert_props"),
                "Per-state props round-trip but no runtime code executes them — the props " +
                "sub-section must carry the same inert banner as actions/blackboard.");
            Assert.IsNull(FindRow(h, "__FSMPropsRow_inert_actions"),
                "Tab switches rebuild the rows — the Actions banner must not leak into " +
                "the State tab.");
        }

        [Test]
        public void InertBanners_ReuseEntitiesGapColour_AndCarryNoImage()
        {
            var h = NewEditor();
            var tr = InstallTwoStateSetWithEdge(h);
            ShowTransitionTab(h, tr);

            var banner = FindRow(h, "__FSMPropsRow_inert_when");
            Assert.IsNotNull(banner);

            var gapColorField = typeof(FSMRuntimeEditor).GetField(
                "ENT_GAP_COLOR", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(gapColorField,
                "ENT_GAP_COLOR is the panel's one warning hue (FSMRuntimeEditor.Entities.cs).");
            Assert.AreEqual((Color)gapColorField.GetValue(null),
                banner.GetComponent<TextMeshProUGUI>().color,
                "One warning colour across the whole editor — a second hue would make " +
                "designers learn two colour languages for the same 'silently ignored' fact.");

            Assert.IsNull(banner.GetComponent<Image>(),
                "Image + TMP on the same GameObject throws — banner labels must carry " +
                "no Image.");
        }
    }
}
