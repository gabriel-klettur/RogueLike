using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Editor.FSM;
using Valkur.Gameplay.Enemies.FSM;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.World;
using static Valkur.Tests.EditMode.Editors.FSM.FSMEditorTestSupport;

namespace Valkur.Tests.EditMode.Editors.FSM
{
    /// <summary>
    /// "Add one test that seeds, loads through the editor, saves, and asserts the factory
    /// still builds the same StateMachine." — before this file, nothing in the repository
    /// called <c>PersistSets</c>, <c>SyncSetToRaw</c> or <c>BuildTypedSetsFromRaw</c> at all.
    ///
    /// It cannot literally hand the round-tripped file to the production
    /// <see cref="FSMRuntimeFactory"/>: the factory reads a HARD-CODED path
    /// (<c>Application.streamingAssetsPath/FSM/</c>), <c>Gameplay/Enemies/FSM/**</c> is
    /// off-limits for this change set (owned by a concurrent edit to the runtime), and this
    /// test is required to never write to the real <c>StreamingAssets/FSM/</c>. Instead it
    /// reconstructs the exact resolution rule the factory's own XML docs state — "a node
    /// names its class in `class`, falling back to its id" — against the round-tripped
    /// temp-dir JSON, then feeds the result through the REAL, unmodified, PUBLIC
    /// <see cref="StateMachine"/>/<see cref="FSMTransition"/>/<see cref="FSMCondition"/>
    /// classes to prove the hand-authored edge actually FIRES post-round-trip, not merely
    /// that its JSON shape looks right. A second, closing test calls the real factory
    /// read-only against the untouched shipped files (exactly like the existing
    /// <c>FSMRuntimeFactoryTests</c>) to catch any static-state pollution from everything else
    /// in this batch.
    /// </summary>
    [TestFixture]
    public class FSMEditorFactoryRoundTripTests
    {
        private readonly List<TempFsmEditor> _handles = new List<TempFsmEditor>();
        private readonly List<GameObject> _scene = new List<GameObject>();
        private MonsterDefinition _fakeMonster;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene) if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            foreach (var h in _handles) h.Dispose();
            _handles.Clear();
            if (_fakeMonster != null) Object.DestroyImmediate(_fakeMonster);
            FSMRuntimeFactory.InvalidateCache();
        }

        private TempFsmEditor NewEditor()
        {
            var h = CreateEditorWithTempData();
            _handles.Add(h);
            return h;
        }

        [Test]
        public void SeedLoadEditSave_RoundTrips_ToAWorkingStateMachine()
        {
            var h = NewEditor();

            // ── Seed — the real generator's pure (no-I/O) builder, so this is genuine seed
            //    shape (is_initial/is_terminal, no "class" key, empty "transitions"), not a
            //    hand-rolled approximation of it. ──
            var seedRoot = FSMSeedGenerator.BuildDefaultSetsRoot(
                FSMSeedGenerator.DefaultStates, FSMSeedGenerator.INITIAL_STATE);
            File.WriteAllText(Path.Combine(h.TempDir, "sets.json"),
                MiniJsonRuntime.Serialize(seedRoot, pretty: true));

            _fakeMonster = ScriptableObject.CreateInstance<MonsterDefinition>();
            _fakeMonster.monsterKey = "roundtrip_test_monster";
            _fakeMonster.fsmSet = FSMSeedGenerator.DEFAULT_SET_ID;
            var assignRoot = FSMSeedGenerator.BuildAssignmentsRoot(new List<MonsterDefinition> { _fakeMonster });
            File.WriteAllText(Path.Combine(h.TempDir, "assignments.json"),
                MiniJsonRuntime.Serialize(assignRoot, pretty: true));

            // ── Load through the editor — exercises the is_terminal/terminal and
            //    "Damage"/"DamageState" NormalizeSets migrations on genuine seed output. ──
            h.Editor.LoadSetsFromDisk();
            Assert.IsFalse(GetField<bool>(h.Editor, "_setsLoadFailed"));

            var fsmSets = GetField<List<FSMRuntimeEditor.FSMSetData>>(h.Editor, "_fsmSets");
            var set = fsmSets.First(s => s.id == FSMSeedGenerator.DEFAULT_SET_ID);
            SetField(h.Editor, "_selectedSet", set);

            foreach (var st in set.states)
                Assert.IsFalse(st.raw.ContainsKey("is_terminal"),
                    $"'{st.id}' must have migrated is_terminal → terminal on load.");
            Assert.AreEqual(2, set.states.Count(s => s.isTerminal),
                "Death + Unconscious must still read as terminal after the migration.");

            // ── Edit — hand-author a global transition through the REAL Connect-tool method,
            //    exactly what a designer does in F12. Its second call is also the SAVE half:
            //    HandleConnectClickFrom persists internally via PersistSets → SaveSets. ──
            Invoke(h.Editor, "HandleConnectClickFrom", "*", true);
            Invoke(h.Editor, "HandleConnectClickFrom", nameof(ChaseState), true);
            Assert.AreEqual(1, set.transitions.Count);

            // ── Reload fresh from disk — proves the save didn't corrupt the file. ──
            var reloadedText = File.ReadAllText(Path.Combine(h.TempDir, "sets.json"));
            var reloadedRoot = MiniJsonRuntime.Deserialize(reloadedText) as Dictionary<string, object>;
            Assert.IsNotNull(reloadedRoot);
            var reloadedSet = ((List<object>)reloadedRoot["sets"])
                .Cast<Dictionary<string, object>>()
                .First(s => (string)s["id"] == FSMSeedGenerator.DEFAULT_SET_ID);

            // ── "…asserts the factory still builds the same StateMachine." ──
            var classMap = ExtractStateClassMap((List<object>)reloadedSet["states"]);
            string initialClass = ResolveClassFor((string)reloadedSet["initial"], classMap);
            Assert.AreEqual(nameof(IdleState), initialClass,
                "The seed's initial state must still resolve to IdleState post round-trip.");

            var initialState = FSMRuntimeFactory.CreateState(initialClass);
            Assert.IsNotNull(initialState,
                $"'{initialClass}' must resolve to a real IState via the factory's own public CreateState.");

            var owner = new GameObject("RoundTripOwner");
            _scene.Add(owner);
            var fsm = new StateMachine(owner, initialState);
            fsm.SetAllowedStates(new HashSet<string>(classMap.Values));

            var transitions = new List<FSMTransition>();
            foreach (var tObj in (List<object>)reloadedSet["transitions"])
            {
                var t = (Dictionary<string, object>)tObj;
                string fromId = (string)t["from"];
                string toId = (string)t["to"];
                string from = fromId == "*" ? "*" : ResolveClassFor(fromId, classMap);
                string to = ResolveClassFor(toId, classMap);
                string rawGuard = t.ContainsKey("guard") ? t["guard"] as string
                                 : t.ContainsKey("when") ? t["when"] as string
                                 : t.ContainsKey("condition") ? t["condition"] as string : null;
                var cond = FSMCondition.Parse(rawGuard, out var err);
                Assert.IsNull(err, $"Guard '{rawGuard}' must parse cleanly.");
                int priority = t.ContainsKey("priority") ? System.Convert.ToInt32(t["priority"]) : 0;
                transitions.Add(new FSMTransition(from, to, cond, priority, 0f, rawGuard));
            }
            Assert.AreEqual(1, transitions.Count);
            Assert.IsTrue(transitions[0].IsGlobal, "'*' must still resolve to a global edge post round-trip.");
            fsm.SetTransitions(transitions.ToArray());

            Assert.AreEqual(nameof(IdleState), fsm.CurrentState.GetType().Name);
            Assert.IsTrue(fsm.HasAuthoredTransitions);

            // The proof that matters: the hand-authored edge actually FIRES, not merely that
            // its JSON shape looks right.
            fsm.Update(0.016f);
            Assert.AreEqual(nameof(ChaseState), fsm.CurrentState.GetType().Name,
                "The global edge authored through the Connect tool must survive seed → load → " +
                "edit → save → reload and still drive the real StateMachine.");
        }

        /// <summary>
        /// Closing smoke test: the untouched, shipped StreamingAssets/FSM files (read-only,
        /// never written by this fixture) must still build correctly through the real,
        /// unmodified <see cref="FSMRuntimeFactory"/> — catching any accidental pollution of
        /// its static caches from the InvalidateCache() calls elsewhere in this batch.
        /// </summary>
        [Test]
        public void ShippedMonsterDefault_StillBuilds_ThroughTheRealFactory()
        {
            FSMRuntimeFactory.InvalidateCache();
            if (!FSMRuntimeFactory.HasSetForArchetype("barbol"))
                Assert.Ignore("StreamingAssets/FSM/assignments.json missing 'barbol'.");

            var owner = new GameObject("RealFactorySmokeTest");
            _scene.Add(owner);
            bool ok = FSMRuntimeFactory.TryBuildForArchetype("barbol", owner, out var fsm);

            Assert.IsTrue(ok);
            Assert.AreEqual(nameof(IdleState), fsm.CurrentState.GetType().Name);
        }

        // ── Replica of FSMRuntimeFactory's documented, id-falls-back-to-class resolution
        //    (ExtractStateClassMap / ResolveClassFor). Not a second implementation to keep in
        //    sync — it is the one rule the runtime's own XML docs state: "a node names its
        //    class in `class`, falling back to its id." ──

        private static Dictionary<string, string> ExtractStateClassMap(List<object> states)
        {
            var map = new Dictionary<string, string>();
            foreach (var sObj in states)
            {
                var s = (Dictionary<string, object>)sObj;
                string id = (string)s["id"];
                string cls = s.TryGetValue("class", out var c) ? c as string : null;
                map[id] = string.IsNullOrEmpty(cls) ? id : cls;
            }
            return map;
        }

        private static string ResolveClassFor(string nodeId, Dictionary<string, string> classMap)
            => classMap.TryGetValue(nodeId, out var cls) ? cls : nodeId;
    }
}
