using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Pins the two defects fixed for audit item 8
    /// ("NPC spellcasting + boss phases are unreachable at runtime" —
    /// .github/ENTITIES_FSM_PVM_AUDIT.md, dimension 12):
    ///
    ///   1. <see cref="EntitySetup.ConfigureBoss"/> attaches
    ///      <see cref="BossPhaseController"/> + <see cref="BossConfigurator"/>
    ///      (plus their SpellCaster/NPCAutoCast dependencies) when
    ///      <c>MonsterDefinition.bossDefinition</c> is set, and leaves a plain
    ///      monster (no boss definition) completely untouched. Before this
    ///      fix those two components were constructed in exactly one place in
    ///      the whole project — the Boss Editor's preview sandbox — so no
    ///      spawned monster ever got phases, a phase-driven spell rotation,
    ///      phase music, or the boss health bar.
    ///   2. <see cref="SpellCaster.SetFreeCastWithoutMana"/> — called on
    ///      every NPC/boss SpellCaster EntitySetup builds — lets a costed
    ///      spell (manaCost > 0) actually fire with no Mana component
    ///      present, instead of the historical "requires mana ... Cast
    ///      cancelled" refusal that silenced 30 of the 47 catalog spells on
    ///      every monster in the game (Mana was only ever added on the
    ///      player path, in EntitySetup.Visuals.cs).
    /// </summary>
    [TestFixture]
    public class BossAndAutoCastManaGateTests
    {
        // ── Fixtures ─────────────────────────────────────────────────────────

        private static SpellDefinition MakeSpell(string key, float manaCost = 0f)
        {
            var s = ScriptableObject.CreateInstance<SpellDefinition>();
            s.spellKey         = key;
            s.displayName      = key;
            s.type             = SpellType.Projectile;
            s.manaCost         = manaCost;
            s.damage           = 10f;
            s.speed            = 5f;
            s.prepareDuration  = 0f;
            s.channelDuration  = 0f;
            s.cooldownDuration = 1f;
            s.range            = 10f;
            s.lifetime         = 3f;
            return s;
        }

        private static SpellCatalog MakeCatalog(params SpellDefinition[] spells)
        {
            var cat = ScriptableObject.CreateInstance<SpellCatalog>();
            cat.SetSpellsRuntime(spells);
            return cat;
        }

        private static MonsterDefinition MakeMonsterDef(string key, BossDefinition boss = null)
        {
            var d = ScriptableObject.CreateInstance<MonsterDefinition>();
            d.monsterKey     = key;
            d.displayName    = key;
            d.stats          = default;
            d.stats.hp       = 100;
            d.bossDefinition = boss;
            return d;
        }

        private static BossDefinition MakeBossDef(params (float hp, string label, string[] spells)[] phases)
        {
            var d = ScriptableObject.CreateInstance<BossDefinition>();
            d.phases = new BossDefinition.Phase[phases.Length];
            for (int i = 0; i < phases.Length; i++)
            {
                d.phases[i] = new BossDefinition.Phase
                {
                    hpThreshold  = phases[i].hp,
                    label        = phases[i].label,
                    autoCastList = phases[i].spells ?? System.Array.Empty<string>(),
                };
            }
            return d;
        }

        // SpellCaster.Awake doesn't run reliably from AddComponent in EditMode —
        // prime the cooldown array by hand, matching MonsterAutoCastWiringTests.
        private static void PrimeCasterCooldowns(SpellCaster caster)
        {
            var f = typeof(SpellCaster).GetField("_cooldownTimers",
                BindingFlags.NonPublic | BindingFlags.Instance);
            f.SetValue(caster, new float[caster.SlotCount]);
        }

        // ── Boss attachment ─────────────────────────────────────────────────

        [Test]
        public void MonsterWithBossDefinition_GetsPhaseControllerAndConfiguratorAttached()
        {
            var fireball = MakeSpell("fireball");
            var cat = MakeCatalog(fireball);
            EntitySetup.SetSpellCatalog(cat);

            var bossDef = MakeBossDef((1f, "Opening", new[] { "fireball" }));
            var def = MakeMonsterDef("test_boss", boss: bossDef);

            var go = new GameObject("BossUnderTest");
            var health = go.AddComponent<Health>();
            health.Initialize(def.stats.hp);

            EntitySetup.ConfigureBoss(go, def);

            Assert.IsNotNull(go.GetComponent<BossPhaseController>(),
                "A monster whose MonsterDefinition.bossDefinition is set must get a BossPhaseController.");
            Assert.IsNotNull(go.GetComponent<BossConfigurator>(),
                "A monster whose MonsterDefinition.bossDefinition is set must get a BossConfigurator.");
            Assert.IsNotNull(go.GetComponent<SpellCaster>(),
                "BossConfigurator.Awake resolves SpellCaster via GetComponent — without one, " +
                "ConfigureRotation permanently no-ops (BossConfigurator's own null guard).");
            Assert.IsNotNull(go.GetComponent<NPCAutoCast>(),
                "BossConfigurator.Awake resolves NPCAutoCast via GetComponent — same no-op risk.");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(def);
            Object.DestroyImmediate(bossDef);
            Object.DestroyImmediate(cat);
            Object.DestroyImmediate(fireball);
        }

        [Test]
        public void MonsterWithoutBossDefinition_GetsNoBossComponents()
        {
            var def = MakeMonsterDef("plain_monster", boss: null);

            var go = new GameObject("PlainMonsterUnderTest");
            var health = go.AddComponent<Health>();
            health.Initialize(def.stats.hp);

            EntitySetup.ConfigureBoss(go, def);

            Assert.IsNull(go.GetComponent<BossPhaseController>(),
                "A plain monster (bossDefinition == null) must not get a BossPhaseController.");
            Assert.IsNull(go.GetComponent<BossConfigurator>(),
                "A plain monster (bossDefinition == null) must not get a BossConfigurator.");
            Assert.IsNull(go.GetComponent<SpellCaster>(),
                "ConfigureBoss must add nothing at all when there is no boss definition — it must " +
                "not be the thing that gives an ordinary monster a SpellCaster.");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(def);
        }

        // ── NPC mana gate ────────────────────────────────────────────────────

        [Test]
        public void CastingNpc_WithManaCostSpellAndNoManaComponent_CanStillCast()
        {
            // 18 mana matches the shipped meteor_shower.asset — one of the 30
            // catalog spells (of 47) that used to silently refuse on every NPC
            // because EntitySetup never attached a Mana component to monsters.
            var costly = MakeSpell("meteor_shower", manaCost: 18f);
            var cat = MakeCatalog(costly);
            EntitySetup.SetSpellCatalog(cat);

            var go = new GameObject("CastingNpcUnderTest");
            var def = MakeMonsterDef("caster_npc");
            def.autoCast = true;
            def.autoCastList = new[] { "meteor_shower" };

            EntitySetup.ConfigureMonsterAutoCast(go, def);

            var caster = go.GetComponent<SpellCaster>();
            Assert.IsNotNull(caster, "Sanity: auto-cast wiring must add the SpellCaster.");
            Assert.IsNull(go.GetComponent<Mana>(),
                "Sanity: NPCs still get no Mana component — that half of the design is unchanged, " +
                "only the gate that used to block them on a missing one has.");
            PrimeCasterCooldowns(caster);

            bool cast = caster.TryCast(0, Vector2.right);

            Assert.IsTrue(cast,
                "An NPC caster with a manaCost > 0 spell and no Mana component must still be able " +
                "to cast: SpellCaster.SetFreeCastWithoutMana(true) is set on every NPC/boss caster " +
                "EntitySetup builds. Before this fix TryCast logged 'requires mana ... Cast " +
                "cancelled' and returned false for every non-zero-cost spell on every monster.");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(def);
            Object.DestroyImmediate(cat);
            Object.DestroyImmediate(costly);
        }

        [Test]
        public void CasterWithoutOptOut_WithManaCostSpellAndNoManaComponent_StillRefusedAndWarns()
        {
            // Guards the other half of the contract: a caster nobody has opted
            // out via SetFreeCastWithoutMana (the historical/player path) must
            // keep refusing + warning. If this regresses, the NPC fix has
            // silently become a global bypass instead of an NPC-only one.
            var spell = MakeSpell("fireball", manaCost: 2f);
            var caster = new GameObject("BareCasterUnderTest").AddComponent<SpellCaster>();
            PrimeCasterCooldowns(caster);
            caster.SetSpell(0, spell);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("requires mana"));
            bool cast = caster.TryCast(0, Vector2.right);

            Assert.IsFalse(cast,
                "A caster that was never opted out via SetFreeCastWithoutMana(true) must keep " +
                "refusing a costed spell when it has no Mana component — this is the player's " +
                "contract and must not silently change.");

            Object.DestroyImmediate(caster.gameObject);
            Object.DestroyImmediate(spell);
        }
    }
}
