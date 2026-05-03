using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Player
{
    /// <summary>
    /// Pins <see cref="SkillEffectApplicator"/>: StatBoost on maxHp/maxMana
    /// flows to Health/Mana, UnlockSpell registers in SpellCaster's book,
    /// unknown stat keys log a warning (not crash), and ReapplyAll
    /// replays effects after a save load.
    /// </summary>
    [TestFixture]
    public class SkillEffectApplicatorTests
    {
        // ── Helpers ─────────────────────────────────────────────────────────────

        private static SkillNode MakeStatBoostNode(string id, string statKey, float value)
        {
            var n = ScriptableObject.CreateInstance<SkillNode>();
            n.skillId = id;
            n.displayName = id;
            n.effects = new[]
            {
                new SkillEffect
                {
                    kind = SkillEffectKind.StatBoost,
                    key  = statKey,
                    value = value,
                }
            };
            return n;
        }

        private static SkillNode MakeUnlockSpellNode(string id, string spellKey)
        {
            var n = ScriptableObject.CreateInstance<SkillNode>();
            n.skillId = id;
            n.displayName = id;
            n.effects = new[]
            {
                new SkillEffect
                {
                    kind = SkillEffectKind.UnlockSpell,
                    key  = spellKey,
                    value = 0f,
                }
            };
            return n;
        }

        private static SkillTree MakeTree(params SkillNode[] nodes)
        {
            var t = ScriptableObject.CreateInstance<SkillTree>();
            t.EditorSetNodes(nodes);
            return t;
        }

        private static SpellDefinition MakeSpell(string key)
        {
            var s = ScriptableObject.CreateInstance<SpellDefinition>();
            s.spellKey = key;
            s.displayName = key;
            s.type = SpellType.Projectile;
            s.cooldownDuration = 1f;
            return s;
        }

        private static SpellCatalog MakeCatalog(params SpellDefinition[] spells)
        {
            var c = ScriptableObject.CreateInstance<SpellCatalog>();
            c.SetSpellsRuntime(spells);
            return c;
        }

        private static (GameObject go, LearnedSkills skills, SkillEffectApplicator app) MakePlayer(
            SkillTree tree,
            int hp = 100,
            int mana = 50,
            SpellCaster caster = null,
            SpellCatalog catalog = null)
        {
            var go = new GameObject("Player");
            var health = go.AddComponent<Health>();
            health.Initialize(hp);
            var manaC = go.AddComponent<Mana>();
            manaC.Initialize(mana, regen: 0f);
            if (caster != null)
            {
                // already on the GO if user passed one; otherwise add fresh
            }
            else
            {
                caster = go.AddComponent<SpellCaster>();
                // Awake doesn't run reliably in EditMode — prime cooldown array.
                var f = typeof(SpellCaster).GetField("_cooldownTimers",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                f.SetValue(caster, new float[caster.SlotCount]);
            }

            var skills = go.AddComponent<LearnedSkills>();
            skills.SetTree(tree);
            skills.AddPoints(99);

            var app = go.AddComponent<SkillEffectApplicator>();
            if (catalog != null) app.SetSpellCatalog(catalog);

            // OnEnable doesn't reliably fire in EditMode AddComponent.
            var onEnable = typeof(SkillEffectApplicator).GetMethod("OnEnable",
                BindingFlags.NonPublic | BindingFlags.Instance);
            onEnable.Invoke(app, null);

            return (go, skills, app);
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void StatBoost_MaxHp_IncreasesHealth()
        {
            var node = MakeStatBoostNode("plus_hp", "maxHp", 25);
            var tree = MakeTree(node);
            var (go, skills, _) = MakePlayer(tree, hp: 100);
            try
            {
                skills.TryLearn(node, 1, out _);
                var health = go.GetComponent<Health>();
                Assert.AreEqual(125, health.MaxHp,
                    "StatBoost maxHp must permanently increase Health.MaxHp.");
                Assert.AreEqual(125, health.CurrentHp,
                    "Stat boost must also grant the matching current HP — full-heal " +
                    "convention for permanent upgrades.");
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(node); Object.DestroyImmediate(tree); }
        }

        [Test]
        public void StatBoost_MaxMana_IncreasesMana()
        {
            var node = MakeStatBoostNode("plus_mp", "maxMana", 30);
            var tree = MakeTree(node);
            var (go, skills, _) = MakePlayer(tree, mana: 50);
            try
            {
                skills.TryLearn(node, 1, out _);
                var mana = go.GetComponent<Mana>();
                Assert.AreEqual(80, mana.MaxMana);
                Assert.AreEqual(80, mana.CurrentMana);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(node); Object.DestroyImmediate(tree); }
        }

        [Test]
        public void StatBoost_UnknownKey_LogsWarning_DoesNotCrash()
        {
            var node = MakeStatBoostNode("typo", "speeed", 10); // typo of 'speed'
            var tree = MakeTree(node);
            var (go, skills, _) = MakePlayer(tree);
            try
            {
                LogAssert.Expect(LogType.Warning,
                    new System.Text.RegularExpressions.Regex("Unknown stat key 'speeed'"));
                Assert.IsTrue(skills.TryLearn(node, 1, out _),
                    "TryLearn must still succeed — the data is misconfigured but that's not the " +
                    "applicator's failure mode.");
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(node); Object.DestroyImmediate(tree); }
        }

        [Test]
        public void UnlockSpell_RegistersInSpellCaster()
        {
            var spell = MakeSpell("frostbolt");
            var catalog = MakeCatalog(spell);
            var node = MakeUnlockSpellNode("learn_frost", "frostbolt");
            var tree = MakeTree(node);
            var (go, skills, _) = MakePlayer(tree, catalog: catalog);
            try
            {
                skills.TryLearn(node, 1, out _);
                var caster = go.GetComponent<SpellCaster>();

                // SpellCaster doesn't expose the spell book contents directly,
                // but TryCastByKey returns false if the key isn't registered.
                // We verify registration by attempting a cast — without a
                // projectile prefab the cast will fail at execute, but the
                // initial "is the spell known?" check passes (we expect a
                // different failure path).
                bool canCast = caster.TryCastByKey("frostbolt", Vector2.right);
                // The cast may succeed (cooldown 1s, no mana req) or fail at
                // execute, but it must NOT bail at the "unknown key" check.
                // We use a more direct approach: reflection on _spellBook.
                var bookField = typeof(SpellCaster).GetField("_spellBook",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var book = bookField.GetValue(caster) as System.Collections.IDictionary;
                Assert.IsTrue(book.Contains("frostbolt"),
                    "UnlockSpell must register the spell in SpellCaster's spell book.");
            }
            finally
            {
                Object.DestroyImmediate(go); Object.DestroyImmediate(node);
                Object.DestroyImmediate(tree); Object.DestroyImmediate(spell);
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void UnlockSpell_UnknownKey_LogsWarning_DoesNotCrash()
        {
            var catalog = MakeCatalog(); // empty
            var node = MakeUnlockSpellNode("learn_ghost", "ghost_spell");
            var tree = MakeTree(node);
            var (go, skills, _) = MakePlayer(tree, catalog: catalog);
            try
            {
                LogAssert.Expect(LogType.Warning,
                    new System.Text.RegularExpressions.Regex("not found in SpellCatalog"));
                skills.TryLearn(node, 1, out _);
            }
            finally
            {
                Object.DestroyImmediate(go); Object.DestroyImmediate(node);
                Object.DestroyImmediate(tree); Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void ReapplyAll_AfterSnapshotRestore_RepliesStatBoosts()
        {
            // Simulate a save/load: learn the skill, snapshot, wipe the
            // applicator's side-effects (manually reset Health.MaxHp), restore
            // the snapshot, then ReapplyAll. The snapshot path doesn't fire
            // OnSkillLearned, so without ReapplyAll the player would be left
            // with the learned-id flag but no stat boost — a save bug.
            var node = MakeStatBoostNode("plus_hp", "maxHp", 50);
            var tree = MakeTree(node);
            var (go, skills, app) = MakePlayer(tree, hp: 100);
            try
            {
                skills.TryLearn(node, 1, out _);
                var health = go.GetComponent<Health>();
                Assert.AreEqual(150, health.MaxHp);

                // "Save → load to a fresh entity": rebuild the player and
                // restore the snapshot.
                var snap = skills.ToSnapshot();
                Object.DestroyImmediate(go);

                var (go2, skills2, app2) = MakePlayer(tree, hp: 100);
                skills2.FromSnapshot(snap);

                Assert.IsTrue(skills2.IsLearned("plus_hp"),
                    "Snapshot restore must repopulate the learned set.");

                // Without ReapplyAll the new player has the flag but no boost.
                var health2 = go2.GetComponent<Health>();
                Assert.AreEqual(100, health2.MaxHp,
                    "Sanity: pre-ReapplyAll the new player has not received the stat boost yet.");

                app2.ReapplyAll();

                Assert.AreEqual(150, health2.MaxHp,
                    "ReapplyAll must replay every effect on every learned skill so " +
                    "loaded saves end up at the cumulative stats they had pre-save.");

                Object.DestroyImmediate(go2);
            }
            finally { Object.DestroyImmediate(node); Object.DestroyImmediate(tree); }
        }
    }
}
