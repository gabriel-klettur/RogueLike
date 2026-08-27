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
    /// F12 Entities panel fixes (item 7): the by_archetype key is now a
    /// <see cref="MonsterCatalog"/> picker instead of unvalidated free text, the value
    /// (both the Add row and every existing assignment row) is a dropdown of the
    /// currently loaded FSM set ids instead of free text, and by_eid is labelled as
    /// unread by <c>FSMRuntimeFactory</c> instead of looking identical to the working half.
    /// </summary>
    [TestFixture]
    public class FSMEditorEntitiesPickerTests
    {
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

        private TempFsmEditor NewEditor()
        {
            var h = CreateEditorWithTempData();
            _handles.Add(h);
            return h;
        }

        private MonsterCatalog NewCatalog(params string[] monsterKeys)
        {
            var catalog = ScriptableObject.CreateInstance<MonsterCatalog>();
            _catalogs.Add(catalog);
            foreach (var key in monsterKeys)
            {
                var def = ScriptableObject.CreateInstance<MonsterDefinition>();
                def.monsterKey = key;
                _defs.Add(def);
                catalog.UpsertDefinition(def);
            }
            return catalog;
        }

        // ── MonsterCatalog picker data source ────────────────────────────────────

        [Test]
        public void CollectMonsterKeysForPicker_ReturnsSortedCatalogKeys()
        {
            var h = NewEditor();
            SetField(h.Editor, "_monsterCatalog", NewCatalog("zeta", "alpha", "mid"));

            var keys = Invoke(h.Editor, "CollectMonsterKeysForPicker") as List<string>;

            Assert.IsNotNull(keys);
            CollectionAssert.AreEqual(new[] { "alpha", "mid", "zeta" }, keys,
                "Picker options must be sorted for a designer to find anything in a real catalog.");
        }

        [Test]
        public void CollectMonsterKeysForPicker_SkipsEmptyKeys()
        {
            var h = NewEditor();
            var catalog = NewCatalog("real_key");
            var blank = ScriptableObject.CreateInstance<MonsterDefinition>();
            blank.monsterKey = "";
            _defs.Add(blank);
            catalog.UpsertDefinition(blank);
            SetField(h.Editor, "_monsterCatalog", catalog);

            var keys = Invoke(h.Editor, "CollectMonsterKeysForPicker") as List<string>;

            CollectionAssert.AreEqual(new[] { "real_key" }, keys);
        }

        [Test]
        public void CollectMonsterKeysForPicker_WithoutExplicitCatalog_SelfResolvesShippedCatalog()
        {
            var h = NewEditor();
            SetField(h.Editor, "_monsterCatalog", null);

            var keys = Invoke(h.Editor, "CollectMonsterKeysForPicker") as List<string>;

            Assert.IsNotNull(keys, "Must never return null even before any catalog is assigned.");
            // ResolveMonsterCatalogIfNeeded is Editor-only (AssetDatabase.FindAssets) and self
            // -resolves the project's shipped MonsterCatalog — same limitation as
            // EntitiesRuntimeEditor/F3's spawner catalog (empty in a standalone build until
            // there's a real injection seam). This proves the fallback actually finds it
            // rather than silently leaving the picker permanently empty in the Editor too.
            var resolved = GetField<MonsterCatalog>(h.Editor, "_monsterCatalog");
            Assert.IsNotNull(resolved,
                "ResolveMonsterCatalogIfNeeded must self-resolve the shipped catalog when the " +
                "Inspector field was left empty.");
        }

        // ── by_eid key-namespace note ─────────────────────────────────────────────

        [Test]
        public void RefreshEntities_ByEid_ShowsUnreadWarningBanner()
        {
            var h = NewEditor();
            h.Editor.LoadAssignmentsFromDisk();
            SetField(h.Editor, "_entitiesCategory", "by_eid");

            Invoke(h.Editor, "RefreshEntities");

            var uiRefs = GetField<FSMEditorUIBuilder.UIRefs>(h.Editor, "_uiRefs");
            Assert.IsNotNull(FindChildRecursive(uiRefs.EntitiesContent, "EntByEidWarning"),
                "by_eid is keyed by F5 PLACEMENT id, not by monster key — a designer who " +
                "types an archetype here gets silence, so the panel must say which " +
                "namespace it wants while that category is open.");
        }

        [Test]
        public void RefreshEntities_ByArchetype_HasNoUnreadWarningBanner()
        {
            var h = NewEditor();
            h.Editor.LoadAssignmentsFromDisk();
            SetField(h.Editor, "_entitiesCategory", "by_archetype");

            Invoke(h.Editor, "RefreshEntities");

            var uiRefs = GetField<FSMEditorUIBuilder.UIRefs>(h.Editor, "_uiRefs");
            Assert.IsNull(FindChildRecursive(uiRefs.EntitiesContent, "EntByEidWarning"),
                "by_archetype IS read by FSMRuntimeFactory — no warning belongs here.");
        }

        // ── Key/value pickers on the Add row ─────────────────────────────────────

        [Test]
        public void BuildEntityAddRow_Archetype_KeyIsMonsterCatalogDropdown()
        {
            var h = NewEditor();
            SetField(h.Editor, "_monsterCatalog", NewCatalog("barbol_test"));
            h.Editor.LoadAssignmentsFromDisk();
            InstallSet(h.Editor, MakeTestSet());
            SetField(h.Editor, "_entitiesCategory", "by_archetype");

            Invoke(h.Editor, "RefreshEntities");

            var uiRefs = GetField<FSMEditorUIBuilder.UIRefs>(h.Editor, "_uiRefs");
            var addRow = FindChildRecursive(uiRefs.EntitiesContent, "EntAddRow");
            Assert.IsNotNull(addRow, "Add row must exist.");
            var keyWrap = FindChildRecursive(addRow, "KeyDropdownWrap");
            Assert.IsNotNull(keyWrap, "by_archetype's key must be a dropdown wrap, not a free-text field.");
            var dropdown = keyWrap.GetComponentInChildren<TMP_Dropdown>();
            Assert.IsNotNull(dropdown);
            CollectionAssert.Contains(dropdown.options.Select(o => o.text).ToList(), "barbol_test",
                "Key picker options must come from MonsterCatalog.");
        }

        [Test]
        public void BuildEntityAddRow_ByEid_KeyIsFreeText()
        {
            var h = NewEditor();
            h.Editor.LoadAssignmentsFromDisk();
            InstallSet(h.Editor, MakeTestSet());
            SetField(h.Editor, "_entitiesCategory", "by_eid");

            Invoke(h.Editor, "RefreshEntities");

            var uiRefs = GetField<FSMEditorUIBuilder.UIRefs>(h.Editor, "_uiRefs");
            var addRow = FindChildRecursive(uiRefs.EntitiesContent, "EntAddRow");
            Assert.IsNull(FindChildRecursive(addRow, "KeyDropdownWrap"),
                "by_eid has no catalog of live entity ids to pick from — it must keep free text.");
            Assert.IsNotNull(addRow.GetComponentInChildren<TMP_InputField>(),
                "by_eid's key field must still be editable as text.");
        }

        [Test]
        public void BuildEntityAddRow_ValueDropdown_IsPopulatedFromLoadedSets()
        {
            var h = NewEditor();
            h.Editor.LoadAssignmentsFromDisk();
            InstallSet(h.Editor, MakeTestSet("Monster_Default"));
            SetField(h.Editor, "_entitiesCategory", "by_archetype");
            SetField(h.Editor, "_monsterCatalog", NewCatalog("barbol_test"));

            Invoke(h.Editor, "RefreshEntities");

            var uiRefs = GetField<FSMEditorUIBuilder.UIRefs>(h.Editor, "_uiRefs");
            var addRow = FindChildRecursive(uiRefs.EntitiesContent, "EntAddRow");
            var valueWrap = FindChildRecursive(addRow, "SetDropdownWrap");
            Assert.IsNotNull(valueWrap, "The value must be a dropdown of loaded sets, not free text.");
            var dropdown = valueWrap.GetComponentInChildren<TMP_Dropdown>();
            CollectionAssert.Contains(dropdown.options.Select(o => o.text).ToList(), "Monster_Default",
                "Value picker options must come from the currently loaded FSM sets — the check " +
                "that a typed set id actually exists.");
        }

        [Test]
        public void BuildEntityRow_PreservesStaleSetId_AsExtraOption()
        {
            var h = NewEditor();
            h.Editor.LoadAssignmentsFromDisk();
            InstallSet(h.Editor, MakeTestSet("Monster_Default"));
            SetField(h.Editor, "_entitiesCategory", "by_archetype");

            // Author an assignment that points at a set which no longer exists — e.g. one
            // that was since deleted. The row must not silently discard/replace it.
            Invoke(h.Editor, "CommitAssignment", "barbol_test", "Some_Deleted_Set");

            var uiRefs = GetField<FSMEditorUIBuilder.UIRefs>(h.Editor, "_uiRefs");
            var row = FindChildRecursive(uiRefs.EntitiesContent, "Ent_barbol_test");
            Assert.IsNotNull(row, "A row must be built for the existing assignment.");
            var dropdown = row.GetComponentInChildren<TMP_Dropdown>();
            Assert.IsNotNull(dropdown);
            Assert.AreEqual("Some_Deleted_Set", dropdown.options[dropdown.value].text,
                "A stale/unknown set id must be shown, not silently swapped for whatever sits at index 0.");
        }
    }
}
