using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Enemies.FSM;
using static Valkur.Tests.EditMode.Editors.FSM.FSMEditorTestSupport;

namespace Valkur.Tests.EditMode.Editors.FSM
{
    /// <summary>
    /// The F12 Entities panel used to render only the archetypes that already had an entry
    /// in <c>assignments.json</c>. A monster with NO assignment was therefore invisible
    /// there — it existed only as one more identical line inside the <c>+ Add</c> dropdown —
    /// so eight shipped monsters booted the hard-coded <c>IdleState</c> fallback for months
    /// with nothing in the editor saying so.
    ///
    /// These tests pin the gap being visible: an UNASSIGNED section built from the same
    /// <see cref="MonsterCatalog"/> the key picker reads, a separate lighter treatment for a
    /// monster that resolves through <c>MonsterDefinition.fsmSet</c> (which works, and must
    /// not be reported as broken), and a header that states the gap as a number. Every
    /// expectation here is derived from the fixture the test itself builds — no test asserts
    /// how many monsters, sets or assignments the project happens to ship.
    /// </summary>
    [TestFixture]
    public class FSMEditorEntitiesCoverageTests
    {
        private const string LOADED_SET = "Monster_Default";

        private readonly List<TempFsmEditor> _handles = new List<TempFsmEditor>();
        private readonly List<MonsterCatalog> _catalogs = new List<MonsterCatalog>();
        private readonly List<MonsterDefinition> _defs = new List<MonsterDefinition>();

        [TearDown]
        public void TearDown()
        {
            foreach (var h in _handles) h.Dispose();
            _handles.Clear();
            foreach (var d in _defs) if (d != null) Object.DestroyImmediate(d);
            _defs.Clear();
            foreach (var c in _catalogs) if (c != null) Object.DestroyImmediate(c);
            _catalogs.Clear();
        }

        // ── Fixture ──────────────────────────────────────────────────────────────

        /// <summary>Builds a catalog of (monsterKey, MonsterDefinition.fsmSet) rows.</summary>
        private MonsterCatalog NewCatalog(params (string key, string fsmSet)[] rows)
        {
            var catalog = ScriptableObject.CreateInstance<MonsterCatalog>();
            _catalogs.Add(catalog);
            foreach (var row in rows)
            {
                var def = ScriptableObject.CreateInstance<MonsterDefinition>();
                def.monsterKey = row.key;
                def.fsmSet = row.fsmSet;
                _defs.Add(def);
                catalog.UpsertDefinition(def);
            }
            return catalog;
        }

        /// <summary>
        /// A by_archetype editor with exactly one loaded set (<see cref="LOADED_SET"/>) and
        /// the given catalog. Assignments start empty — every monster's coverage in these
        /// tests comes from what the test itself commits.
        /// </summary>
        private TempFsmEditor NewArchetypeEditor(MonsterCatalog catalog)
        {
            var h = CreateEditorWithTempData();
            _handles.Add(h);
            SetField(h.Editor, "_monsterCatalog", catalog);
            h.Editor.LoadAssignmentsFromDisk();
            InstallSet(h.Editor, MakeTestSet(LOADED_SET));
            SetField(h.Editor, "_entitiesCategory", "by_archetype");
            Invoke(h.Editor, "RefreshEntities");
            return h;
        }

        private static List<FSMRuntimeEditor.MonsterFSMCoverage> Coverage(FSMRuntimeEditor ed)
        {
            var list = Invoke(ed, "CollectMonsterCoverage") as List<FSMRuntimeEditor.MonsterFSMCoverage>;
            Assert.IsNotNull(list, "CollectMonsterCoverage must always return a list.");
            return list;
        }

        private static FSMRuntimeEditor.MonsterFSMCoverage For(
            List<FSMRuntimeEditor.MonsterFSMCoverage> coverage, string monsterKey)
        {
            var row = coverage.FirstOrDefault(c => c.monsterKey == monsterKey);
            Assert.IsNotNull(row, $"'{monsterKey}' is in the catalog, so it must appear in the audit.");
            return row;
        }

        private static Transform Content(FSMRuntimeEditor ed)
            => GetField<FSMEditorUIBuilder.UIRefs>(ed, "_uiRefs").EntitiesContent;

        // ── Classification ───────────────────────────────────────────────────────

        [Test]
        public void Coverage_NoAssignmentAndNoFsmSet_IsUnassigned()
        {
            var h = NewArchetypeEditor(NewCatalog(("gap_mon", "")));

            var row = For(Coverage(h.Editor), "gap_mon");

            Assert.AreEqual(FSMRuntimeEditor.FSMSetSource.Unassigned, row.source,
                "A monster that neither an assignment nor an fsmSet reaches boots the " +
                "hard-coded IdleState — that is the gap this panel has to show.");
            Assert.IsEmpty(row.setId ?? "", "Nothing resolves, so no set id may be claimed.");
        }

        [Test]
        public void Coverage_FsmSetNamingALoadedSet_IsDefinitionFallback_NotUnassigned()
        {
            var h = NewArchetypeEditor(NewCatalog(("hinted_mon", LOADED_SET)));

            var row = For(Coverage(h.Editor), "hinted_mon");

            Assert.AreEqual(FSMRuntimeEditor.FSMSetSource.DefinitionFallback, row.source,
                "FSMRuntimeFactory.TryBuildForEntity takes MonsterDefinition.fsmSet as its " +
                "last-resort hint, so this monster does get a real brain — flagging it as " +
                "broken would send an author chasing a non-problem.");
            Assert.AreEqual(LOADED_SET, row.setId, "The row must say WHICH set it inherits.");
            StringAssert.Contains("fsmSet", row.note,
                "The row has to name where the set comes from, or 'not in the file' reads as 'nowhere'.");
        }

        [Test]
        public void Coverage_FsmSetNamingAnUnloadedSet_IsUnassigned()
        {
            var h = NewArchetypeEditor(NewCatalog(("stale_mon", "Some_Deleted_Set")));

            var row = For(Coverage(h.Editor), "stale_mon");

            Assert.AreEqual(FSMRuntimeEditor.FSMSetSource.Unassigned, row.source,
                "A hint that names no loaded set resolves nothing at runtime. Counting it as " +
                "covered would rebuild the original blind spot with one extra step: the panel " +
                "would claim the monster was fine while it booted the IdleState fallback.");
            StringAssert.Contains("Some_Deleted_Set", row.note,
                "The reason must name the dangling set id — that is the thing to fix.");
        }

        [Test]
        public void Coverage_Assignment_WinsOverFsmSet()
        {
            var h = NewArchetypeEditor(NewCatalog(("both_mon", "Some_Other_Set")));
            Invoke(h.Editor, "CommitAssignment", "both_mon", LOADED_SET);

            var row = For(Coverage(h.Editor), "both_mon");

            Assert.AreEqual(FSMRuntimeEditor.FSMSetSource.Assignment, row.source,
                "assignments.json is what F12 edits and what FSMRuntimeFactory resolves " +
                "first — the audit must report the same precedence the runtime uses.");
            Assert.AreEqual(LOADED_SET, row.setId);
        }

        // ── The UNASSIGNED section ───────────────────────────────────────────────

        [Test]
        public void RefreshEntities_ByArchetype_UnassignedMonsterGetsItsOwnRow()
        {
            var h = NewArchetypeEditor(NewCatalog(("gap_mon", ""), ("covered_mon", "")));
            Invoke(h.Editor, "CommitAssignment", "covered_mon", LOADED_SET);

            var content = Content(h.Editor);
            Assert.IsNotNull(FindChildRecursive(content, "EntUnassignedHeader"),
                "The gap needs a section banner, not just a differently coloured line.");
            Assert.IsNotNull(FindChildRecursive(content, "EntUnassigned_gap_mon"),
                "A catalog monster with no coverage was previously visible ONLY inside the " +
                "'+ Add' dropdown, identical to every assigned monster beside it.");
            Assert.IsNull(FindChildRecursive(content, "EntUnassigned_covered_mon"),
                "An assigned monster must not be listed as a gap.");
            Assert.IsNotNull(FindChildRecursive(content, "Ent_covered_mon"),
                "…and must keep its normal editable row.");
        }

        [Test]
        public void UnassignedRow_ReusesTheByEidBannerWarningColour()
        {
            var h = NewArchetypeEditor(NewCatalog(("gap_mon", "")));
            var content = Content(h.Editor);

            // Read the panel's established warning colour off the banner that already owns it.
            SetField(h.Editor, "_entitiesCategory", "by_eid");
            Invoke(h.Editor, "RefreshEntities");
            var banner = FindChildRecursive(content, "EntByEidWarning");
            Assert.IsNotNull(banner);
            Color warningColour = banner.GetComponent<TextMeshProUGUI>().color;

            SetField(h.Editor, "_entitiesCategory", "by_archetype");
            Invoke(h.Editor, "RefreshEntities");
            var keyLabel = FindChildRecursive(FindChildRecursive(content, "EntUnassigned_gap_mon"),
                "CoverageKeyLabel").GetComponent<TextMeshProUGUI>();

            Assert.AreEqual(warningColour, keyLabel.color,
                "A second warning hue would make an author learn two colour languages to " +
                "tell an actionable gap from decoration.");
        }

        [Test]
        public void FallbackRow_IsLighter_AndIsNotPaintedAsAGap()
        {
            var h = NewArchetypeEditor(NewCatalog(("hinted_mon", LOADED_SET), ("gap_mon", "")));
            var content = Content(h.Editor);

            var fallbackRow = FindChildRecursive(content, "EntFallback_hinted_mon");
            Assert.IsNotNull(fallbackRow,
                "A monster covered only by MonsterDefinition.fsmSet is in neither the " +
                "assignment rows nor the gap — without its own row it stays invisible.");
            Assert.IsNull(FindChildRecursive(content, "EntUnassigned_hinted_mon"),
                "It resolves a real set; listing it as unassigned would blunt the real warning.");

            var fallbackColour = FindChildRecursive(fallbackRow, "CoverageKeyLabel")
                .GetComponent<TextMeshProUGUI>().color;
            var gapColour = FindChildRecursive(FindChildRecursive(content, "EntUnassigned_gap_mon"),
                "CoverageKeyLabel").GetComponent<TextMeshProUGUI>().color;
            Assert.AreNotEqual(gapColour, fallbackColour,
                "'Working, just not from this file' and 'nothing resolves' must not look the same.");

            var note = FindChildRecursive(fallbackRow, "CoverageNoteLabel")
                .GetComponent<TextMeshProUGUI>().text;
            StringAssert.Contains(LOADED_SET, note, "The row must say which set it inherits…");
            StringAssert.Contains("fsmSet", note, "…and where that set comes from.");
        }

        // ── The one-click assign control ─────────────────────────────────────────

        [Test]
        public void UnassignedRow_AssignDropdown_OffersTheLoadedSetsBehindAPlaceholder()
        {
            var h = NewArchetypeEditor(NewCatalog(("gap_mon", "")));

            var wrap = FindChildRecursive(FindChildRecursive(Content(h.Editor), "EntUnassigned_gap_mon"),
                "AssignDropdownWrap");
            Assert.IsNotNull(wrap, "The gap has to be closable from where it is reported.");
            var dropdown = wrap.GetComponentInChildren<TMP_Dropdown>();
            Assert.IsNotNull(dropdown);

            var options = dropdown.options.Select(o => o.text).ToList();
            CollectionAssert.Contains(options, LOADED_SET,
                "The control must be the SAME set-id picker the assigned rows use, not a " +
                "second list that can disagree with it.");
            Assert.AreNotEqual(LOADED_SET, options[0],
                "Index 0 must be a placeholder: with a real set already selected there, " +
                "picking it raises no change event and the row silently does nothing.");
        }

        [Test]
        public void UnassignedRow_PickingASet_WritesTheAssignmentAndClosesTheGap()
        {
            var h = NewArchetypeEditor(NewCatalog(("gap_mon", "")));
            var content = Content(h.Editor);

            var dropdown = FindChildRecursive(FindChildRecursive(content, "EntUnassigned_gap_mon"),
                "AssignDropdownWrap").GetComponentInChildren<TMP_Dropdown>();
            int pick = dropdown.options.FindIndex(o => o.text == LOADED_SET);
            Assert.Greater(pick, 0, "The set must sit after the placeholder.");

            dropdown.value = pick; // one click — the pick IS the commit

            var byArchetype = GetField<Dictionary<string, object>>(h.Editor, "_assignmentsRoot")["by_archetype"]
                as Dictionary<string, object>;
            Assert.IsNotNull(byArchetype);
            Assert.IsTrue(byArchetype.ContainsKey("gap_mon"),
                "Choosing a set must write the assignment — a report with no cure is still a gap.");
            Assert.AreEqual(LOADED_SET, byArchetype["gap_mon"] as string);

            Assert.IsNull(FindChildRecursive(content, "EntUnassigned_gap_mon"),
                "The refresh that follows the commit must move the monster out of the gap…");
            Assert.IsNotNull(FindChildRecursive(content, "Ent_gap_mon"),
                "…and into the normal assignment rows.");
        }

        // ── Header counts ────────────────────────────────────────────────────────

        [Test]
        public void Header_StatesTheGapAsANumber()
        {
            var h = NewArchetypeEditor(NewCatalog(
                ("gap_a", ""), ("gap_b", "Some_Deleted_Set"),
                ("hinted_mon", LOADED_SET), ("covered_mon", "")));
            Invoke(h.Editor, "CommitAssignment", "covered_mon", LOADED_SET);

            var coverage = Coverage(h.Editor);
            int expectedGap = coverage.Count(c => c.source == FSMRuntimeEditor.FSMSetSource.Unassigned);
            int expectedViaFsmSet = coverage.Count(c => c.source == FSMRuntimeEditor.FSMSetSource.DefinitionFallback);

            var header = FindChildRecursive(Content(h.Editor), "EntHeaderLabel")
                .GetComponent<TextMeshProUGUI>().text;

            StringAssert.Contains($"{expectedGap} unassigned", header,
                "A count in the header is the difference between a gap someone can act on " +
                "and one they have to notice by scrolling.");
            StringAssert.Contains($"{expectedViaFsmSet} via fsmSet", header);
            StringAssert.Contains("1 assigned", header,
                "One key was committed, so exactly one assignment row exists.");
        }

        [Test]
        public void RefreshEntities_ByEid_BuildsNoCoverageSections()
        {
            var h = NewArchetypeEditor(NewCatalog(("gap_mon", "")));
            SetField(h.Editor, "_entitiesCategory", "by_eid");

            Invoke(h.Editor, "RefreshEntities");

            var content = Content(h.Editor);
            Assert.IsNull(FindChildRecursive(content, "EntUnassignedHeader"),
                "by_eid is keyed by F5 PLACEMENT id, not by monster key — diffing it against " +
                "the MonsterCatalog would report every monster in the game as unassigned.");
            Assert.IsNull(FindChildRecursive(content, "EntUnassigned_gap_mon"));
        }

        [Test]
        public void RefreshEntities_WithAnEmptyCatalog_BuildsNoCoverageSections()
        {
            var h = NewArchetypeEditor(NewCatalog());

            var content = Content(h.Editor);
            Assert.IsNull(FindChildRecursive(content, "EntUnassignedHeader"),
                "An empty catalog has no gap to report — an empty UNASSIGNED banner would be " +
                "one more warning that fires for a steady state.");
            Assert.IsNull(FindChildRecursive(content, "EntFallbackHeader"));
            Assert.IsNotNull(FindChildRecursive(content, "EntAddRow"),
                "The rest of the panel must be untouched by having nothing to audit.");
        }
    }
}
