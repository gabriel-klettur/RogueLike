using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Editor.FSM;
using Valkur.Gameplay.FSM;

namespace Valkur.Tests.EditMode.Editors.FSM
{
    /// <summary>
    /// Unit tests for the pure (no-I/O) builders + merger of
    /// <see cref="FSMSeedGenerator"/>. These pin the JSON shape consumed by
    /// <see cref="FSMRuntimeEditor"/> and the runtime <c>FSMRuntimeFactory</c>:
    /// schema regressions or accidental loss of user-edited sets would
    /// silently break the editor and runtime AI loading. The tests do NOT
    /// hit the disk — the I/O orchestration in <c>FSMSeedGenerator.Run</c>
    /// is exercised end-to-end at integration time, here we lock the logic.
    /// </summary>
    [TestFixture]
    public class FSMSeedGeneratorTests
    {
        // ── Schema: BuildDefaultSetsRoot ─────────────────────────────────────────

        [Test]
        public void BuildDefaultSetsRoot_Produces_SingleSet_WithExpectedTopLevelKeys()
        {
            var root = FSMSeedGenerator.BuildDefaultSetsRoot(
                FSMSeedGenerator.DefaultStates,
                FSMSeedGenerator.INITIAL_STATE);

            Assert.IsTrue(root.ContainsKey("sets"), "Root must have a 'sets' array.");
            var sets = root["sets"] as List<object>;
            Assert.AreEqual(1, sets.Count, "Default seed emits exactly one set.");

            var first = sets[0] as Dictionary<string, object>;
            Assert.AreEqual(FSMSeedGenerator.DEFAULT_SET_ID,    first["id"]);
            Assert.AreEqual(FSMSeedGenerator.DEFAULT_SET_LABEL, first["label"]);
            Assert.AreEqual(FSMSeedGenerator.INITIAL_STATE,     first["initial"]);
            Assert.IsTrue((bool)first[FSMSeedGenerator.AUTO_FLAG_KEY],
                "Auto-generated flag must be true so re-runs can refresh this set.");
            Assert.IsInstanceOf<List<object>>(first["states"]);
            Assert.IsInstanceOf<List<object>>(first["transitions"]);
            Assert.IsInstanceOf<Dictionary<string, object>>(first["blackboard"]);
        }

        [Test]
        public void BuildDefaultSetsRoot_EmitsOneStatePerInputId()
        {
            var ids = new[] { nameof(IdleState), nameof(ChaseState), nameof(DeathState) };
            var root  = FSMSeedGenerator.BuildDefaultSetsRoot(ids, nameof(IdleState));
            var first = ((List<object>)root["sets"])[0] as Dictionary<string, object>;
            var states = (List<object>)first["states"];

            Assert.AreEqual(ids.Length, states.Count);
            for (int i = 0; i < ids.Length; i++)
            {
                var s = states[i] as Dictionary<string, object>;
                Assert.AreEqual(ids[i], s["id"], $"State #{i} id must round-trip.");
                Assert.IsTrue(s.ContainsKey("label"),       "Every state needs a label.");
                Assert.IsTrue(s.ContainsKey("x"),           "Every state needs an x coordinate.");
                Assert.IsTrue(s.ContainsKey("y"),           "Every state needs a y coordinate.");
                Assert.IsTrue(s.ContainsKey("is_initial"),  "Every state needs is_initial.");
                Assert.IsTrue(s.ContainsKey("is_terminal"), "Every state needs is_terminal.");
                Assert.IsInstanceOf<Dictionary<string, object>>(s["props"]);
            }
        }

        [Test]
        public void BuildDefaultSetsRoot_FlagsInitialAndTerminalStatesCorrectly()
        {
            var root  = FSMSeedGenerator.BuildDefaultSetsRoot(
                FSMSeedGenerator.DefaultStates, FSMSeedGenerator.INITIAL_STATE);
            var first = ((List<object>)root["sets"])[0] as Dictionary<string, object>;
            var states = (List<object>)first["states"];

            int initialCount  = states.Cast<Dictionary<string, object>>()
                .Count(s => (bool)s["is_initial"]);
            int terminalCount = states.Cast<Dictionary<string, object>>()
                .Count(s => (bool)s["is_terminal"]);

            Assert.AreEqual(1, initialCount, "Exactly one state must be the initial state.");
            Assert.AreEqual(2, terminalCount,
                "Death + Unconscious are the two terminal states for the default set.");

            var initial = states.Cast<Dictionary<string, object>>()
                .First(s => (bool)s["is_initial"]);
            Assert.AreEqual(FSMSeedGenerator.INITIAL_STATE, initial["id"]);
        }

        [Test]
        public void DefaultStates_DoNotIncludeDamageState()
        {
            // DamageState is transient — pushed by StateMachine.HandleHitEvent,
            // never user-selectable. Including it would let designers wire it
            // as an initial state, which the runtime cannot honor.
            CollectionAssert.DoesNotContain(FSMSeedGenerator.DefaultStates, nameof(DamageState),
                "DamageState must remain transient and not appear in the default set.");
        }

        // ── Schema: BuildAssignmentsRoot ─────────────────────────────────────────

        [Test]
        public void BuildAssignmentsRoot_SkipsMonstersWithEmptyFsmSet()
        {
            var hostile = ScriptableObject_Of("barbol_test", "Monster_Default");
            var vendor  = ScriptableObject_Of("vendor_test", "");

            var root = FSMSeedGenerator.BuildAssignmentsRoot(new List<MonsterDefinition>
            {
                hostile, vendor
            });

            var byArch = root["by_archetype"] as Dictionary<string, object>;
            Assert.AreEqual(1, byArch.Count, "Vendor (empty fsmSet) must be skipped.");
            Assert.AreEqual("Monster_Default", byArch["barbol_test"]);
            Assert.IsFalse(byArch.ContainsKey("vendor_test"));
            Assert.IsInstanceOf<Dictionary<string, object>>(root["by_eid"]);
        }

        [Test]
        public void BuildAssignmentsRoot_TolertesNullEntries()
        {
            var root = FSMSeedGenerator.BuildAssignmentsRoot(new List<MonsterDefinition>
            {
                null,
                ScriptableObject_Of("barbol_a", "Monster_Default"),
                null,
            });

            var byArch = root["by_archetype"] as Dictionary<string, object>;
            Assert.AreEqual(1, byArch.Count, "Null entries must be silently skipped.");
        }

        // ── Schema: BuildAnimationMapRoot ────────────────────────────────────────

        [Test]
        public void BuildAnimationMapRoot_PreservesEveryDefaultMapping()
        {
            var root = FSMSeedGenerator.BuildAnimationMapRoot(
                FSMSeedGenerator.DefaultAnimationMap);

            var def = root["default"] as Dictionary<string, object>;
            foreach (var kv in FSMSeedGenerator.DefaultAnimationMap)
                Assert.AreEqual(kv.Value, def[kv.Key],
                    $"Animation slot for '{kv.Key}' must round-trip into the default map.");
            Assert.IsInstanceOf<Dictionary<string, object>>(root["per_set"]);
        }

        // ── Idempotency: MergeSetsIdempotent ─────────────────────────────────────

        [Test]
        public void MergeSetsIdempotent_RegeneratesAutoSet_WithoutDuplicating()
        {
            var fresh    = FSMSeedGenerator.BuildDefaultSetsRoot(
                FSMSeedGenerator.DefaultStates, FSMSeedGenerator.INITIAL_STATE);
            var existing = FSMSeedGenerator.BuildDefaultSetsRoot(
                new[] { nameof(IdleState) }, nameof(IdleState));

            var merged = FSMSeedGenerator.MergeSetsIdempotent(existing, fresh);
            var sets   = merged["sets"] as List<object>;

            Assert.AreEqual(1, sets.Count,
                "An auto-generated set with the same id must be replaced, not duplicated.");
            var set = sets[0] as Dictionary<string, object>;
            var states = set["states"] as List<object>;
            Assert.AreEqual(FSMSeedGenerator.DefaultStates.Length, states.Count,
                "Replacement must use the fresh state list, not the existing one.");
        }

        [Test]
        public void MergeSetsIdempotent_PreservesUserAuthoredSet()
        {
            var fresh = FSMSeedGenerator.BuildDefaultSetsRoot(
                FSMSeedGenerator.DefaultStates, FSMSeedGenerator.INITIAL_STATE);

            // Hand-authored set (no auto_generated flag).
            var customSet = new Dictionary<string, object>
            {
                ["id"]         = "Custom_Boss",
                ["label"]      = "Custom Boss FSM",
                ["initial"]    = nameof(IdleState),
                ["states"]     = new List<object>(),
                ["transitions"]= new List<object>(),
            };
            var existing = new Dictionary<string, object>
            {
                ["sets"] = new List<object> { customSet },
            };

            var merged = FSMSeedGenerator.MergeSetsIdempotent(existing, fresh);
            var sets   = merged["sets"] as List<object>;

            Assert.AreEqual(2, sets.Count,
                "Custom set must coexist with the regenerated default set.");
            CollectionAssert.Contains(
                sets.Cast<Dictionary<string, object>>().Select(s => s["id"] as string).ToList(),
                "Custom_Boss",
                "The user's hand-authored set must survive a regen.");
        }

        // ── Idempotency: MergeAssignmentsIdempotent ──────────────────────────────

        [Test]
        public void MergeAssignmentsIdempotent_NeverOverwritesExistingAssignment()
        {
            var existing = new Dictionary<string, object>
            {
                ["by_archetype"] = new Dictionary<string, object>
                {
                    ["barbol"] = "Custom_Set_User_Pinned",
                },
                ["by_eid"] = new Dictionary<string, object>
                {
                    ["npc_007"] = "Custom_Set",
                },
            };
            var generated = new Dictionary<string, object>
            {
                ["by_archetype"] = new Dictionary<string, object>
                {
                    ["barbol"]   = "Monster_Default",   // would clobber the user's pin
                    ["barbol_2"] = "Monster_Default",   // genuinely new — should be added
                },
                ["by_eid"] = new Dictionary<string, object>(),
            };

            var merged = FSMSeedGenerator.MergeAssignmentsIdempotent(existing, generated);

            var byArch = merged["by_archetype"] as Dictionary<string, object>;
            Assert.AreEqual("Custom_Set_User_Pinned", byArch["barbol"],
                "User-pinned assignment must NOT be overwritten by regen.");
            Assert.AreEqual("Monster_Default", byArch["barbol_2"],
                "New archetype must be added to assignments on regen.");

            var byEid = merged["by_eid"] as Dictionary<string, object>;
            Assert.AreEqual("Custom_Set", byEid["npc_007"],
                "by_eid entries must round-trip without being touched.");
        }

        // ── Idempotency: MergeAnimationMapIdempotent ─────────────────────────────

        [Test]
        public void MergeAnimationMapIdempotent_PreservesUserOverrides()
        {
            var existing = new Dictionary<string, object>
            {
                ["default"] = new Dictionary<string, object>
                {
                    [nameof(IdleState)] = "custom_idle_clip",   // user override
                },
                ["per_set"] = new Dictionary<string, object>
                {
                    ["Monster_Boss"] = new Dictionary<string, object>
                    {
                        [nameof(AttackState)] = "boss_special_attack",
                    },
                },
            };
            var generated = FSMSeedGenerator.BuildAnimationMapRoot(
                FSMSeedGenerator.DefaultAnimationMap);

            var merged = FSMSeedGenerator.MergeAnimationMapIdempotent(existing, generated);

            var def = merged["default"] as Dictionary<string, object>;
            Assert.AreEqual("custom_idle_clip", def[nameof(IdleState)],
                "User-set animation slot for IdleState must survive a regen.");
            Assert.AreEqual("chase", def[nameof(ChaseState)],
                "Generator must fill in keys the user has not overridden.");

            var perSet = merged["per_set"] as Dictionary<string, object>;
            Assert.IsTrue(perSet.ContainsKey("Monster_Boss"),
                "per_set overrides must round-trip without being touched.");
        }

        // ── Reflection: ValidateStatesExist ──────────────────────────────────────

        [Test]
        public void ValidateStatesExist_ReturnsEmpty_ForRealStateClasses()
        {
            var missing = FSMSeedGenerator.ValidateStatesExist(FSMSeedGenerator.DefaultStates);
            Assert.AreEqual(0, missing.Count,
                "Every state in DefaultStates must resolve to a concrete IState in Valkur.Gameplay. " +
                "Missing: " + string.Join(", ", missing));
        }

        [Test]
        public void ValidateStatesExist_ReportsBogusStateNames()
        {
            var missing = FSMSeedGenerator.ValidateStatesExist(new[]
            {
                nameof(IdleState),
                "DefinitelyNotARealState",
            });
            Assert.AreEqual(1, missing.Count);
            Assert.AreEqual("DefinitelyNotARealState", missing[0]);
        }

        // ── Humanization ─────────────────────────────────────────────────────────

        [Test]
        public void HumanizeStateName_DropsStateSuffix_AndSplitsCamelCase()
        {
            Assert.AreEqual("Idle",            FSMSeedGenerator.HumanizeStateName("IdleState"));
            Assert.AreEqual("Alert Chase",     FSMSeedGenerator.HumanizeStateName("AlertChaseState"));
            Assert.AreEqual("Death",           FSMSeedGenerator.HumanizeStateName("DeathState"));
        }

        [Test]
        public void HumanizeStateName_HandlesAcronymsCorrectly()
        {
            // Acronym followed by a regular word: "NPCCast" → "NPC Cast", not "N P C Cast".
            Assert.AreEqual("NPC Cast", FSMSeedGenerator.HumanizeStateName("NPCCastState"));
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static MonsterDefinition ScriptableObject_Of(string monsterKey, string fsmSet)
        {
            var def = ScriptableObject.CreateInstance<MonsterDefinition>();
            def.monsterKey = monsterKey;
            def.fsmSet     = fsmSet;
            return def;
        }
    }
}
