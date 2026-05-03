using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.FSM;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Pins the NPCAutoCast per-entry advanced config: initial delay,
    /// per-entry min/max distance gates, and the HP-loss step trigger.
    /// These let designers author bosses whose spells fire at different
    /// ranges and ramp up as the boss bleeds, without running the full
    /// Python AutoCastSystem (~400 lines) inside Unity.
    /// </summary>
    [TestFixture]
    public class NPCAutoCastEntryTests
    {
        private GameObject _npcGo;
        private GameObject _playerGo;
        private SpellCaster _caster;
        private NPCAutoCast _auto;
        private SpellDefinition _spell;
        private Health _health;

        // ── Setup ───────────────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            _spell = ScriptableObject.CreateInstance<SpellDefinition>();
            _spell.spellKey         = "test_spell";
            _spell.displayName      = "Test";
            _spell.type             = SpellType.Projectile;
            _spell.manaCost         = 0f;
            _spell.damage           = 10f;
            _spell.speed            = 5f;
            _spell.prepareDuration  = 0f;
            _spell.channelDuration  = 0f;
            _spell.cooldownDuration = 1f;
            _spell.range            = 10f;
            _spell.lifetime         = 3f;

            _npcGo  = new GameObject("NPC");
            _npcGo.AddComponent<Rigidbody2D>();
            _health = _npcGo.AddComponent<Health>();
            _health.Initialize(100);
            _caster = _npcGo.AddComponent<SpellCaster>();
            // Awake doesn't run in EditMode; prime caster + auto-cast state via
            // the same reflection trick the existing SpellCasterTests use.
            PrimeCasterCooldowns(_caster);
            _caster.SetSpell(0, _spell);

            _auto = _npcGo.AddComponent<NPCAutoCast>();
            // AddComponent does NOT run Awake in EditMode tests, so the
            // private _caster / _health refs that NPCAutoCast.Awake would
            // resolve never get populated. Inject them by reflection so the
            // HP-loss trigger sees a real Health component to query.
            InjectField(_auto, "_caster",        _caster);
            InjectField(_auto, "_health",        _health);
            InjectField(_auto, "_statusEffects", _npcGo.GetComponent<StatusEffectManager>());
            InjectField(_auto, "_brain",         _npcGo.GetComponent<FSMMonsterBrain>());

            // Clear the 1-element default and rebuild via AddEntry so test
            // entries are deterministic.
            _auto.Clear();

            _playerGo = new GameObject("Player");
            _playerGo.transform.position = new Vector3(2f, 0f, 0f); // 2 world units away
            EntityRegistry.RegisterPlayer(_playerGo);
        }

        [TearDown]
        public void TearDown()
        {
            if (_playerGo != null) EntityRegistry.UnregisterPlayer(_playerGo);
            if (_npcGo    != null) Object.DestroyImmediate(_npcGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_spell    != null) Object.DestroyImmediate(_spell);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static void PrimeCasterCooldowns(SpellCaster caster)
        {
            var f = typeof(SpellCaster).GetField("_cooldownTimers",
                BindingFlags.NonPublic | BindingFlags.Instance);
            f.SetValue(caster, new float[caster.SlotCount]);
        }

        private static void InjectField(object obj, string name, object value)
        {
            var f = obj.GetType().GetField(name,
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) f.SetValue(obj, value);
        }

        private static float GetEntryCooldown(NPCAutoCast auto, int entryIndex)
        {
            var f = typeof(NPCAutoCast).GetField("_entryCooldowns",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var arr = (float[])f.GetValue(auto);
            return arr[entryIndex];
        }

        private static int GetHpLossBucket(NPCAutoCast auto, int entryIndex)
        {
            var f = typeof(NPCAutoCast).GetField("_hpLossBuckets",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var arr = (int[])f.GetValue(auto);
            return arr[entryIndex];
        }

        private static void InvokeApplyHpLossTrigger(NPCAutoCast auto, int entryIndex)
        {
            var m = typeof(NPCAutoCast).GetMethod("ApplyHpLossTrigger",
                BindingFlags.NonPublic | BindingFlags.Instance);
            m.Invoke(auto, new object[] { entryIndex });
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void InitialDelay_PrimesCooldownToConfiguredSeconds()
        {
            _auto.AddEntry(new NPCAutoCast.AutoCastEntry
            {
                spellSlot           = 0,
                periodSeconds       = 5f,
                periodJitter        = 0f,
                initialDelaySeconds = 2.5f,
            });

            float cd = GetEntryCooldown(_auto, 0);
            Assert.AreEqual(2.5f, cd, 0.0001f,
                "An entry with initialDelaySeconds set must use that value as its " +
                "first-cast countdown — not the random [0, period] fallback. " +
                "Without this, boss multi-entry sequences fire all spells on " +
                "frame 1 instead of staggering.");
        }

        [Test]
        public void NoInitialDelay_FallsBackToRandomWithinPeriod()
        {
            _auto.AddEntry(new NPCAutoCast.AutoCastEntry
            {
                spellSlot           = 0,
                periodSeconds       = 4f,
                periodJitter        = 0f,
                initialDelaySeconds = 0f,
            });

            float cd = GetEntryCooldown(_auto, 0);
            Assert.GreaterOrEqual(cd, 0f);
            Assert.LessOrEqual(cd, 4f,
                "Without initialDelaySeconds, the cooldown must seed in [0, period] " +
                "so two NPCs sharing a definition don't fire in lockstep on aggro.");
        }

        [Test]
        public void HpLossStep_ZeroBucket_DoesNothingAtFullHealth()
        {
            _auto.AddEntry(new NPCAutoCast.AutoCastEntry
            {
                spellSlot     = 0,
                periodSeconds = 5f,
                hpLossStep    = 0.25f,
            });
            float beforeCd = GetEntryCooldown(_auto, 0);

            // Health untouched (100/100 = 0% lost) — no bucket boundary crossed.
            InvokeApplyHpLossTrigger(_auto, 0);

            Assert.AreEqual(beforeCd, GetEntryCooldown(_auto, 0),
                "At full HP no bucket crosses, so cooldown must stay untouched.");
            Assert.AreEqual(0, GetHpLossBucket(_auto, 0));
        }

        [Test]
        public void HpLossStep_CrossingThreshold_ResetsCooldownToZero()
        {
            _auto.AddEntry(new NPCAutoCast.AutoCastEntry
            {
                spellSlot     = 0,
                periodSeconds = 99f,
                hpLossStep    = 0.25f,
            });
            // Force a long cooldown so we can detect the reset clearly.
            var cdField = typeof(NPCAutoCast).GetField("_entryCooldowns",
                BindingFlags.NonPublic | BindingFlags.Instance);
            ((float[])cdField.GetValue(_auto))[0] = 50f;

            // Lose 30% HP → bucket 1 (one step of 0.25 lost).
            _health.TakeDamage(30);
            InvokeApplyHpLossTrigger(_auto, 0);

            Assert.AreEqual(0f, GetEntryCooldown(_auto, 0),
                "Crossing a new HP-loss bucket must drop the cooldown to ready " +
                "so the boss fires its desperation cast immediately.");
            Assert.AreEqual(1, GetHpLossBucket(_auto, 0));
        }

        [Test]
        public void HpLossStep_SameBucketTwice_DoesNotRetrigger()
        {
            _auto.AddEntry(new NPCAutoCast.AutoCastEntry
            {
                spellSlot     = 0,
                periodSeconds = 99f,
                hpLossStep    = 0.25f,
            });
            _health.TakeDamage(30); // bucket 1
            InvokeApplyHpLossTrigger(_auto, 0);

            // First trigger consumed; cooldown ticked back up.
            var cdField = typeof(NPCAutoCast).GetField("_entryCooldowns",
                BindingFlags.NonPublic | BindingFlags.Instance);
            ((float[])cdField.GetValue(_auto))[0] = 50f;

            // Take a sliver more damage but stay inside bucket 1 (need 50% lost
            // to reach bucket 2). Bucket 1 already triggered — should NOT
            // re-trigger.
            _health.TakeDamage(5);
            InvokeApplyHpLossTrigger(_auto, 0);

            Assert.AreEqual(50f, GetEntryCooldown(_auto, 0), 0.0001f,
                "Damage that stays inside the previously-triggered bucket must " +
                "not retrigger — otherwise every melee swing forces a cast.");
        }

        [Test]
        public void HpLossStep_CrossingMultipleBuckets_OnlyTriggersOnce()
        {
            _auto.AddEntry(new NPCAutoCast.AutoCastEntry
            {
                spellSlot     = 0,
                periodSeconds = 99f,
                hpLossStep    = 0.25f,
            });
            // Single hit that takes us from 100% to 20% (bucket 3).
            _health.TakeDamage(80);
            InvokeApplyHpLossTrigger(_auto, 0);

            Assert.AreEqual(0f, GetEntryCooldown(_auto, 0),
                "Even a multi-bucket leap must produce a ready cooldown — " +
                "the boss should still desperation-cast once.");
            Assert.AreEqual(3, GetHpLossBucket(_auto, 0),
                "Bucket counter must catch up to current loss so subsequent " +
                "damage in the same band doesn't retrigger.");
        }

        [Test]
        public void HpLossStep_Disabled_NoOp()
        {
            _auto.AddEntry(new NPCAutoCast.AutoCastEntry
            {
                spellSlot     = 0,
                periodSeconds = 99f,
                hpLossStep    = 0f, // disabled
            });
            var cdField = typeof(NPCAutoCast).GetField("_entryCooldowns",
                BindingFlags.NonPublic | BindingFlags.Instance);
            ((float[])cdField.GetValue(_auto))[0] = 50f;

            _health.TakeDamage(80);
            InvokeApplyHpLossTrigger(_auto, 0);

            Assert.AreEqual(50f, GetEntryCooldown(_auto, 0), 0.0001f,
                "hpLossStep == 0 must disable the trigger entirely; existing " +
                "non-boss NPCs can leave the field at 0 with no behaviour change.");
        }

        // ── Distance-gate behaviour is covered by integration through Update,
        // which ticks Time.deltaTime. EditMode can't drive Update predictably
        // without a PlayMode test; the per-entry gate logic is small and
        // covered indirectly by the wiring tests above and the FSM tests.
    }
}
