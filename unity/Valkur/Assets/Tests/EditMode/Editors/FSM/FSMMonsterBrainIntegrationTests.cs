using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Enemies.FSM;
using Valkur.Gameplay.FSM;

namespace Valkur.Tests.EditMode.Editors.FSM
{
    /// <summary>
    /// Locks the Phase 3 contract: <see cref="FSMMonsterBrain"/> consumes the
    /// JSON model via <see cref="FSMRuntimeFactory"/> when an archetype is
    /// seeded, and falls back to the hard-coded boot otherwise.
    ///
    /// The factory itself is unit-tested in
    /// <see cref="FSMRuntimeFactoryTests"/>; what these tests pin is the
    /// *integration* — that the boot path inside <c>FSMMonsterBrain.Initialize</c>
    /// actually calls the factory and reuses its <see cref="StateMachine"/>.
    /// </summary>
    [TestFixture]
    public class FSMMonsterBrainIntegrationTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene)
                if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            FSMRuntimeFactory.InvalidateCache();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private FSMMonsterBrain SpawnBrain(string monsterKey, int hp = 10)
        {
            var go = new GameObject($"TestMonster_{monsterKey}");
            _scene.Add(go);
            go.AddComponent<Rigidbody2D>();
            go.AddComponent<Health>();

            var brain = go.AddComponent<FSMMonsterBrain>();
            // EditMode does not auto-fire Awake on AddComponent timing — invoke
            // it manually so internal RequireComponent caches are populated
            // (otherwise Initialize NREs on _health.Initialize).
            var awake = typeof(FSMMonsterBrain).GetMethod(
                "Awake",
                BindingFlags.NonPublic | BindingFlags.Instance);
            awake?.Invoke(brain, null);

            var def = ScriptableObject.CreateInstance<MonsterDefinition>();
            def.monsterKey  = monsterKey;
            def.displayName = monsterKey;
            def.stats       = new EntityStats { hp = hp };
            brain.Initialize(def);
            return brain;
        }

        // ── Phase 3 contract ─────────────────────────────────────────────────────

        [Test]
        public void Initialize_UsesFactory_WhenArchetypeIsSeeded()
        {
            // The Phase 1 seed maps "barbol" → Monster_Default (initial = IdleState).
            // Skip if a fresh checkout deleted the seed JSON — the factory's
            // own tests assert that scenario separately.
            FSMRuntimeFactory.InvalidateCache();
            if (!FSMRuntimeFactory.HasSetForArchetype("barbol"))
                Assert.Ignore("StreamingAssets/FSM seed missing — run Valkur > FSM > Generate Seed.");

            var brain = SpawnBrain("barbol");

            Assert.IsNotNull(brain.FSM,
                "FSMMonsterBrain.Initialize must always end up with a non-null StateMachine.");
            Assert.AreEqual("IdleState", brain.FSM.CurrentState.GetType().Name,
                "Monster_Default declares IdleState as the initial state — the brain must honor it.");

            // The factory wires SetAllowedStates from the set's vocabulary.
            // The hard-coded fallback never calls SetAllowedStates, so a non-null
            // _allowedStates field is the unambiguous signal that the factory ran.
            var allowed = typeof(StateMachine).GetField(
                "_allowedStates",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var set = allowed?.GetValue(brain.FSM) as HashSet<string>;
            Assert.IsNotNull(set,
                "Factory path must have called SetAllowedStates — otherwise the fallback ran.");
            CollectionAssert.Contains(set, "IdleState",
                "Allowed-states set must include the seeded vocabulary.");
        }

        [Test]
        public void Initialize_FallsBackToHardcoded_WhenArchetypeIsUnseeded()
        {
            FSMRuntimeFactory.InvalidateCache();
            // An archetype that doesn't exist in assignments.json — the brain
            // must boot via the legacy hard-coded path. No exceptions, FSM
            // is still IdleState, but no allowed-states guard.
            var brain = SpawnBrain("__unseeded_archetype_xyz__");

            Assert.IsNotNull(brain.FSM, "Fallback must produce a working StateMachine.");
            Assert.AreEqual("IdleState", brain.FSM.CurrentState.GetType().Name,
                "Hard-coded fallback uses IdleState (parity with pre-Phase-3 behaviour).");

            var allowed = typeof(StateMachine).GetField(
                "_allowedStates",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNull(allowed?.GetValue(brain.FSM),
                "Hard-coded fallback must NOT install an allowed-states guard " +
                "(unseeded archetypes need the loose pre-migration semantics).");
        }

        [Test]
        public void Initialize_FallsBackToHardcoded_WhenFactoryReportsLoadFailure()
        {
            // Force the factory to report "no data" for the archetype lookup
            // by invalidating + querying a known-missing archetype. This is
            // the same code path that fires when StreamingAssets/FSM is empty.
            FSMRuntimeFactory.InvalidateCache();

            var brain = SpawnBrain("__definitely_missing_xyz__");

            Assert.IsNotNull(brain.FSM,
                "FSMMonsterBrain.Initialize must never return without an FSM, even if " +
                "the entire JSON model is unavailable.");
            Assert.AreEqual("IdleState", brain.FSM.CurrentState.GetType().Name);
        }
    }
}
