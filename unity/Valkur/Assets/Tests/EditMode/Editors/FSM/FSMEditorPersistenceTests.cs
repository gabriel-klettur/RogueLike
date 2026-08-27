using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
using Valkur.Gameplay.Enemies.FSM;
using static Valkur.Tests.EditMode.Editors.FSM.FSMEditorTestSupport;

namespace Valkur.Tests.EditMode.Editors.FSM
{
    /// <summary>
    /// F12 persistence-layer fixes:
    ///   • three writer/reader key mismatches (is_terminal/terminal, per_set/overrides/by_set,
    ///     the "Damage" vs "DamageState" sentinel id)
    ///   • the anti-wipe guard (a parse failure must block the next save, not silently persist
    ///     an emptiness over the file that failed to parse)
    ///   • atomic writes (AtomicJsonFile, no leftover temp artefacts)
    ///   • FSMRuntimeFactory.InvalidateCache being called after every sets.json / assignments.json
    ///     save, so the iteration loop is edit → fight, not edit → `reloadfsm` → fight
    ///
    /// Everything that touches disk redirects to a temp directory via
    /// <see cref="FSMEditorTestSupport.CreateEditorWithTempData"/>.
    /// </summary>
    [TestFixture]
    public class FSMEditorPersistenceTests
    {
        private readonly List<TempFsmEditor> _handles = new List<TempFsmEditor>();

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            foreach (var h in _handles) h.Dispose();
            _handles.Clear();
        }

        private TempFsmEditor NewEditor()
        {
            var h = CreateEditorWithTempData();
            _handles.Add(h);
            return h;
        }

        // ── is_terminal / terminal ───────────────────────────────────────────────

        [Test]
        public void NormalizeSets_MigratesIsTerminal_ToTerminal_AndDropsLegacyKey()
        {
            var root = RootWithStates(
                new Dictionary<string, object> { ["id"] = "A", ["is_terminal"] = true },
                new Dictionary<string, object> { ["id"] = "B", ["is_terminal"] = false });

            FSMRuntimeEditor.NormalizeSets(root);

            var states = StatesOf(root);
            Assert.IsTrue((bool)states[0]["terminal"], "is_terminal:true must migrate to terminal:true.");
            Assert.IsFalse(states[0].ContainsKey("is_terminal"), "The legacy key must be dropped.");
            Assert.IsFalse((bool)states[1]["terminal"]);
            Assert.IsFalse(states[1].ContainsKey("is_terminal"));
        }

        [Test]
        public void NormalizeSets_PrefersExplicitTerminal_OverStaleIsTerminal()
        {
            var root = RootWithStates(
                new Dictionary<string, object> { ["id"] = "A", ["is_terminal"] = true, ["terminal"] = false });

            FSMRuntimeEditor.NormalizeSets(root);

            var states = StatesOf(root);
            Assert.IsFalse((bool)states[0]["terminal"],
                "A hand-edit via the Properties panel ('terminal': false) must win over a stale " +
                "seed-generator value in 'is_terminal' — otherwise reopening F12 could silently " +
                "revert an author's explicit change.");
        }

        [Test]
        public void NormalizeSets_StripsRedundantIsInitial()
        {
            var root = RootWithStates(new Dictionary<string, object> { ["id"] = "A", ["is_initial"] = true });

            FSMRuntimeEditor.NormalizeSets(root);

            Assert.IsFalse(StatesOf(root)[0].ContainsKey("is_initial"),
                "is_initial is a second source of truth for the set-level 'initial' field, and " +
                "nothing keeps it in sync with Mark-Initial — it must be stripped so a stale copy " +
                "can't linger.");
        }

        // ── "Damage" vs "DamageState" ────────────────────────────────────────────

        [Test]
        public void NormalizeSets_MigratesLegacyDamageId_ToDamageState_WithoutDuplicating()
        {
            var root = RootWithStates(
                new Dictionary<string, object> { ["id"] = "A" },
                new Dictionary<string, object> { ["id"] = "Damage", ["class"] = "DamageState" });

            FSMRuntimeEditor.NormalizeSets(root);

            var ids = StatesOf(root).Select(s => (string)s["id"]).ToList();
            CollectionAssert.DoesNotContain(ids, "Damage");
            Assert.AreEqual(1, ids.Count(id => id == "DamageState"),
                "Exactly one DamageState node — the legacy 'Damage' id must rename in place, not " +
                "coexist with a second, freshly auto-included 'DamageState'.");
        }

        [Test]
        public void NormalizeSets_AutoIncludesDamageState_WhenAbsent()
        {
            var root = RootWithStates(new Dictionary<string, object> { ["id"] = "A" });

            FSMRuntimeEditor.NormalizeSets(root);

            var ids = StatesOf(root).Select(s => (string)s["id"]).ToList();
            Assert.AreEqual(1, ids.Count(id => id == "DamageState"));
        }

        // ── animation_map.json: per_set / overrides / by_set ─────────────────────

        [Test]
        public void LoadAnimationMapFromDisk_MigratesLegacyOverridesKey_ToPerSet()
        {
            var h = NewEditor();
            File.WriteAllText(Path.Combine(h.TempDir, "animation_map.json"),
                "{\"default\":{},\"overrides\":{\"Monster_Boss\":{\"AttackState\":\"boss_attack\"}}}");

            h.Editor.LoadAnimationMapFromDisk();

            var animRoot = GetField<Dictionary<string, object>>(h.Editor, "_animationMapRoot");
            Assert.IsTrue(animRoot.ContainsKey("per_set"),
                "per_set is the name FSMSeedGenerator.BuildAnimationMapRoot and the Animations " +
                "panel (the only real reader/writer) agree on.");
            Assert.IsFalse(animRoot.ContainsKey("overrides"), "The legacy key nothing ever read must be dropped.");
            var perSet = (Dictionary<string, object>)animRoot["per_set"];
            Assert.IsTrue(perSet.ContainsKey("Monster_Boss"), "Data under the old key must survive the migration.");
        }

        [Test]
        public void LoadAnimationMapFromDisk_MigratesLegacyBySetKey_ToPerSet()
        {
            var h = NewEditor();
            File.WriteAllText(Path.Combine(h.TempDir, "animation_map.json"),
                "{\"default\":{},\"by_set\":{\"Monster_Boss\":{\"AttackState\":\"boss_attack\"}}}");

            h.Editor.LoadAnimationMapFromDisk();

            var animRoot = GetField<Dictionary<string, object>>(h.Editor, "_animationMapRoot");
            Assert.IsTrue(animRoot.ContainsKey("per_set"));
            Assert.IsFalse(animRoot.ContainsKey("by_set"));
        }

        [Test]
        public void AnimationsPanel_ReadsAndWrites_PerSetKey()
        {
            var h = NewEditor();
            h.Editor.LoadAnimationMapFromDisk();
            SetField(h.Editor, "_animTarget", "Monster_Boss");

            Invoke(h.Editor, "CommitAnim", "AttackState", "boss_special_attack");

            var animRoot = GetField<Dictionary<string, object>>(h.Editor, "_animationMapRoot");
            var perSet = (Dictionary<string, object>)animRoot["per_set"];
            var bossMap = (Dictionary<string, object>)perSet["Monster_Boss"];
            Assert.AreEqual("boss_special_attack", bossMap["AttackState"]);
        }

        // ── Anti-wipe guard ───────────────────────────────────────────────────────

        [Test]
        public void SaveSets_RefusesWrite_AfterMalformedSetsJson()
        {
            LogAssert.ignoreFailingMessages = true;
            var h = NewEditor();
            string path = Path.Combine(h.TempDir, "sets.json");
            const string malformed = "{ this is not valid json";
            File.WriteAllText(path, malformed);

            h.Editor.LoadSetsFromDisk();
            Assert.IsTrue(GetField<bool>(h.Editor, "_setsLoadFailed"),
                "A parse failure (file exists, content invalid) must set the anti-wipe flag.");

            h.Editor.SaveSets();

            Assert.AreEqual(malformed, File.ReadAllText(path),
                "SaveSets must refuse to write while the load-failure flag is set — otherwise the " +
                "very next graph-tool click after a corrupted read would persist an empty " +
                "'sets: []' over a file that was still recoverable by hand.");
        }

        [Test]
        public void SaveSets_DoesNotFlagFailure_WhenFileIsSimplyMissing()
        {
            var h = NewEditor(); // temp dir has no sets.json yet — legitimate first run

            h.Editor.LoadSetsFromDisk();
            Assert.IsFalse(GetField<bool>(h.Editor, "_setsLoadFailed"),
                "A missing file is the legitimate first-run state, not a parse failure.");

            h.Editor.SaveSets();
            Assert.IsTrue(File.Exists(Path.Combine(h.TempDir, "sets.json")));
        }

        [Test]
        public void SaveAssignments_RefusesWrite_AfterMalformedAssignmentsJson()
        {
            LogAssert.ignoreFailingMessages = true;
            var h = NewEditor();
            string path = Path.Combine(h.TempDir, "assignments.json");
            const string malformed = "not json at all";
            File.WriteAllText(path, malformed);

            h.Editor.LoadAssignmentsFromDisk();
            Assert.IsTrue(GetField<bool>(h.Editor, "_assignmentsLoadFailed"));

            h.Editor.SaveAssignments();

            Assert.AreEqual(malformed, File.ReadAllText(path));
        }

        // ── Atomic write ──────────────────────────────────────────────────────────

        [Test]
        public void SaveSets_WritesThroughAtomicJsonFile_NoLeftoverTempArtefacts()
        {
            var h = NewEditor();
            var set = MakeTestSet();
            AddState(set, "ChaseState");
            InstallSet(h.Editor, set);

            h.Editor.SaveSets();

            var files = Directory.GetFiles(h.TempDir);
            Assert.IsTrue(files.Any(f => f.EndsWith("sets.json")), "sets.json must exist after a save.");
            Assert.IsFalse(files.Any(f => f.Contains(".tmp")),
                "AtomicJsonFile.Write must not leave a temp file behind after a successful write.");
        }

        // ── InvalidateCache ───────────────────────────────────────────────────────

        [Test]
        public void SaveSets_InvalidatesRuntimeFsmCache()
        {
            var h = NewEditor();
            var set = MakeTestSet();
            AddState(set, "ChaseState");
            InstallSet(h.Editor, set);

            // Force the factory's cache to a known "loaded" state first.
            FSMRuntimeFactory.HasSetForArchetype("anything");
            Assert.IsTrue(FSMRuntimeFactory.IsLoaded, "Pre-condition: factory cache primed.");

            h.Editor.SaveSets();

            Assert.IsFalse(FSMRuntimeFactory.IsLoaded,
                "SaveSets must call FSMRuntimeFactory.InvalidateCache() — otherwise a monster " +
                "spawned right after an F12 save keeps the FSM parsed at the editor's first open, " +
                "and the only way to see the edit live was to type 'reloadfsm'.");

            FSMRuntimeFactory.InvalidateCache(); // leave the static cache clean for later fixtures
        }

        [Test]
        public void SaveAssignments_InvalidatesRuntimeFsmCache()
        {
            var h = NewEditor();
            h.Editor.LoadAssignmentsFromDisk();

            FSMRuntimeFactory.HasSetForArchetype("anything");
            Assert.IsTrue(FSMRuntimeFactory.IsLoaded);

            h.Editor.SaveAssignments();

            Assert.IsFalse(FSMRuntimeFactory.IsLoaded,
                "assignments.json is the other file FSMRuntimeFactory reads (by_archetype) — " +
                "re-pointing an archetype at a different set needs the same invalidation sets.json gets.");

            FSMRuntimeFactory.InvalidateCache();
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static Dictionary<string, object> RootWithStates(params Dictionary<string, object>[] states)
        {
            return new Dictionary<string, object>
            {
                ["sets"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["id"] = "S1", ["initial"] = "A",
                        ["states"] = states.Cast<object>().ToList(),
                        ["transitions"] = new List<object>(),
                    },
                },
            };
        }

        private static List<Dictionary<string, object>> StatesOf(Dictionary<string, object> root)
        {
            var set = (Dictionary<string, object>)((List<object>)root["sets"])[0];
            return ((List<object>)set["states"]).Cast<Dictionary<string, object>>().ToList();
        }
    }
}
