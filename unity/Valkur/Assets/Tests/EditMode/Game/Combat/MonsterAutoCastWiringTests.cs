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
    /// Pins the EntitySetup.ConfigureMonster auto-cast wiring path: a
    /// MonsterDefinition with <c>autoCast = true</c> and a non-empty
    /// <c>autoCastList</c> must, by the time the monster GameObject finishes
    /// configuration, end up with:
    ///
    ///   1. A SpellCaster that has the named spells in its slots (capped at
    ///      SpellCaster.SlotCount = 4).
    ///   2. An NPCAutoCast with one entry per registered slot.
    ///   3. The spell book ('TryCastByKey' map) populated with every named
    ///      spell, even those past the slot cap.
    ///
    /// Without these, MonsterDefinition.autoCastList is dead data — the
    /// designer can wire it in the inspector but no monster ever fires.
    /// </summary>
    [TestFixture]
    public class MonsterAutoCastWiringTests
    {
        // ── Fixtures ────────────────────────────────────────────────────────────

        private static SpellDefinition MakeSpell(string key)
        {
            var s = ScriptableObject.CreateInstance<SpellDefinition>();
            s.spellKey         = key;
            s.displayName      = key;
            s.type             = SpellType.Projectile;
            s.manaCost         = 0f;
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
            // SpellCatalog has SetSpells (editor) + SetSpellsRuntime (runtime).
            cat.SetSpellsRuntime(spells);
            return cat;
        }

        private static MonsterDefinition MakeMonsterDef(string key, bool autoCast, string[] list)
        {
            var d = ScriptableObject.CreateInstance<MonsterDefinition>();
            d.monsterKey   = key;
            d.displayName  = key;
            d.autoCast     = autoCast;
            d.autoCastList = list ?? System.Array.Empty<string>();
            d.stats        = default;
            return d;
        }

        // SpellCaster.Awake doesn't run in EditMode, so the cooldown array must
        // be primed via reflection like SpellCasterTests does.
        private static void PrimeCasterCooldowns(SpellCaster caster)
        {
            var f = typeof(SpellCaster).GetField("_cooldownTimers",
                BindingFlags.NonPublic | BindingFlags.Instance);
            f.SetValue(caster, new float[caster.SlotCount]);
        }

        private static GameObject MakeMonsterGO()
        {
            // Bare GO — ConfigureMonsterAutoCast adds SpellCaster + NPCAutoCast itself.
            var go = new GameObject("MonsterUnderTest");
            return go;
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void AutoCastDisabled_NoSpellCasterAdded()
        {
            var cat  = MakeCatalog(MakeSpell("fireball"));
            EntitySetup.SetSpellCatalog(cat);

            var go   = MakeMonsterGO();
            var def  = MakeMonsterDef("dummy", autoCast: false, list: new[] { "fireball" });

            EntitySetup.ConfigureMonsterAutoCast(go, def);

            Assert.IsNull(go.GetComponent<SpellCaster>(),
                "Auto-cast disabled monsters must not get a SpellCaster appended.");
            Assert.IsNull(go.GetComponent<NPCAutoCast>(),
                "Auto-cast disabled monsters must not get an NPCAutoCast appended.");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(def);
            Object.DestroyImmediate(cat);
        }

        [Test]
        public void EmptyAutoCastList_NoOp()
        {
            var cat  = MakeCatalog();
            EntitySetup.SetSpellCatalog(cat);

            var go   = MakeMonsterGO();
            var def  = MakeMonsterDef("dummy", autoCast: true, list: System.Array.Empty<string>());

            EntitySetup.ConfigureMonsterAutoCast(go, def);

            Assert.IsNull(go.GetComponent<SpellCaster>(),
                "Empty autoCastList must not trigger any wiring.");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(def);
            Object.DestroyImmediate(cat);
        }

        [Test]
        public void NoSpellCatalogInjected_LogsWarningAndSkips()
        {
            // Drop any catalog the previous test left behind.
            EntitySetup.SetSpellCatalog(null);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                "no SpellCatalog was injected"));

            var go  = MakeMonsterGO();
            var def = MakeMonsterDef("dummy", autoCast: true, list: new[] { "fireball" });

            EntitySetup.ConfigureMonsterAutoCast(go, def);

            Assert.IsNull(go.GetComponent<SpellCaster>(),
                "Without a catalog the wiring must abort cleanly — no half-configured caster.");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(def);
        }

        [Test]
        public void TwoSpells_PopulatesSlotsAndEntries()
        {
            var fireball  = MakeSpell("fireball");
            var frostbolt = MakeSpell("frostbolt");
            var cat       = MakeCatalog(fireball, frostbolt);
            EntitySetup.SetSpellCatalog(cat);

            var go  = MakeMonsterGO();
            var def = MakeMonsterDef("ranged_npc", autoCast: true,
                                     list: new[] { "fireball", "frostbolt" });

            EntitySetup.ConfigureMonsterAutoCast(go, def);

            var caster = go.GetComponent<SpellCaster>();
            var auto   = go.GetComponent<NPCAutoCast>();
            Assert.IsNotNull(caster, "SpellCaster must be present after wiring.");
            Assert.IsNotNull(auto,   "NPCAutoCast must be present after wiring.");

            PrimeCasterCooldowns(caster);
            Assert.AreSame(fireball,  caster.GetSpellAtSlot(0));
            Assert.AreSame(frostbolt, caster.GetSpellAtSlot(1));
            Assert.IsNull(caster.GetSpellAtSlot(2),
                "Slots beyond the registered spells must remain empty.");

            Assert.AreEqual(2, auto.EntryCount,
                "NPCAutoCast must hold exactly one entry per registered slot.");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(def);
            Object.DestroyImmediate(cat);
            Object.DestroyImmediate(fireball);
            Object.DestroyImmediate(frostbolt);
        }

        [Test]
        public void UnknownSpellKey_Skipped_WithWarning()
        {
            var fireball = MakeSpell("fireball");
            var cat      = MakeCatalog(fireball);
            EntitySetup.SetSpellCatalog(cat);

            // 'meteor' isn't in the catalog → must warn but not abort.
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                "references unknown spell 'meteor'"));

            var go  = MakeMonsterGO();
            var def = MakeMonsterDef("npc", autoCast: true,
                                     list: new[] { "fireball", "meteor" });

            EntitySetup.ConfigureMonsterAutoCast(go, def);

            var auto = go.GetComponent<NPCAutoCast>();
            Assert.IsNotNull(auto);
            Assert.AreEqual(1, auto.EntryCount,
                "Only the resolvable spell ('fireball') must yield an entry.");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(def);
            Object.DestroyImmediate(cat);
            Object.DestroyImmediate(fireball);
        }

        [Test]
        public void ListBeyondSlotCount_RegistersInSpellBookEvenWhenNoSlotLeft()
        {
            // 5 spells; SpellCaster has 4 slots. The 5th must still land in the
            // spell book (TryCastByKey) but NOT in NPCAutoCast entries.
            var spells = new[]
            {
                MakeSpell("a"), MakeSpell("b"), MakeSpell("c"),
                MakeSpell("d"), MakeSpell("e"),
            };
            var cat = MakeCatalog(spells);
            EntitySetup.SetSpellCatalog(cat);

            var go  = MakeMonsterGO();
            var def = MakeMonsterDef("npc", autoCast: true,
                                     list: new[] { "a", "b", "c", "d", "e" });

            EntitySetup.ConfigureMonsterAutoCast(go, def);

            var caster = go.GetComponent<SpellCaster>();
            var auto   = go.GetComponent<NPCAutoCast>();
            PrimeCasterCooldowns(caster);

            Assert.AreEqual(caster.SlotCount, auto.EntryCount,
                "AutoCast entries must be capped at SpellCaster.SlotCount.");
            Assert.AreSame(spells[0], caster.GetSpellAtSlot(0));
            Assert.AreSame(spells[caster.SlotCount - 1], caster.GetSpellAtSlot(caster.SlotCount - 1));

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(def);
            Object.DestroyImmediate(cat);
            foreach (var s in spells) Object.DestroyImmediate(s);
        }

        [Test]
        public void ReWiring_ClearsPreviousEntries()
        {
            // A prefab that already has NPCAutoCast entries (e.g. authored in
            // the inspector) must not duplicate them on re-configuration.
            var fireball = MakeSpell("fireball");
            var cat      = MakeCatalog(fireball);
            EntitySetup.SetSpellCatalog(cat);

            var go = MakeMonsterGO();
            // Pre-existing inspector-style entry.
            var preAuto = go.AddComponent<NPCAutoCast>();
            preAuto.AddEntry(0, 5f, 0.1f);
            preAuto.AddEntry(1, 5f, 0.1f);
            Assert.AreEqual(2, preAuto.EntryCount, "Sanity: prefab seed accepted.");

            var def = MakeMonsterDef("npc", autoCast: true, list: new[] { "fireball" });
            EntitySetup.ConfigureMonsterAutoCast(go, def);

            Assert.AreEqual(1, preAuto.EntryCount,
                "Re-wiring must wipe inspector-authored entries before adding " +
                "the data-driven ones; otherwise prefab + def entries stack.");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(def);
            Object.DestroyImmediate(cat);
            Object.DestroyImmediate(fireball);
        }
    }
}
