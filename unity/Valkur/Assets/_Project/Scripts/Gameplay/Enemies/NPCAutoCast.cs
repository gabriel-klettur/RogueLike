using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Combat.Death;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// NPC periodic spell-casting behaviour.
    /// Mirrors Python AutoCastComponent + AutoCastSystem.
    ///
    /// Each entry defines a spell slot index, a cast period (seconds), an
    /// optional initial-delay stagger, optional per-entry min/max distance
    /// gates that override the global <see cref="castRange"/>, and an
    /// optional HP-loss step trigger that forces the cooldown ready when
    /// the NPC's health crosses a multiple of the configured fraction.
    ///
    /// Casting is suppressed while the NPC is in DamageState, DeathState,
    /// UnconsciousState, NPCCastState, or is stunned.
    ///
    /// Attach alongside <see cref="FSMMonsterBrain"/> and <see cref="SpellCaster"/>.
    /// Wire spell slots on the SpellCaster, then populate <see cref="entries"/>.
    /// </summary>
    [RequireComponent(typeof(SpellCaster))]
    public class NPCAutoCast : MonoBehaviour
    {
        [Serializable]
        public class AutoCastEntry
        {
            [Tooltip("Spell slot index on the attached SpellCaster (0-3).")]
            public int spellSlot = 0;
            [Tooltip("Seconds between consecutive casts of this spell. Python: period_s")]
            public float periodSeconds = 3f;
            [Tooltip("Random variance added to period each cast (jitter to avoid synchronisation).")]
            public float periodJitter = 0.5f;

            [Header("Initial delay")]
            [Tooltip("Stagger the very first cast of this entry by this many seconds. " +
                     "Useful for boss multi-entry sequences where two spells share a slot " +
                     "rotation but should not fire simultaneously on aggro. Python: initial_delay_s")]
            public float initialDelaySeconds = 0f;

            [Header("Per-entry distance gate")]
            [Tooltip("Minimum distance to player to fire this spell (world units). " +
                     "Cone breath / melee-range spells use this to keep the boss from " +
                     "phasing through the player. 0 = no minimum.")]
            public float minDistance = 0f;
            [Tooltip("Maximum distance to player to fire this spell (world units). " +
                     "0 = use the NPCAutoCast.castRange global. Long-range bosses raise " +
                     "this; melee-range NPCs leave it at 0.")]
            public float maxDistance = 0f;

            [Header("HP-loss trigger")]
            [Tooltip("Force this entry's cooldown to zero whenever the NPC crosses a " +
                     "multiple of this fraction of max HP. 0.25 means trigger on every " +
                     "25% lost (75% HP, 50% HP, 25% HP). 0 disables the trigger entirely. " +
                     "Python: on_hp_loss_step")]
            [Range(0f, 1f)] public float hpLossStep = 0f;
        }

        [Header("Auto Cast Entries")]
        [SerializeField] private List<AutoCastEntry> entries = new List<AutoCastEntry>();

        [Header("Cast Range (global fallback)")]
        [Tooltip("Maximum distance to player to attempt a cast when an entry's " +
                 "maxDistance is 0. Per-entry maxDistance overrides this.")]
        [SerializeField] private float castRange = 8f;

        [Header("State")]
        [SerializeField] private bool castingEnabled = true;

        private SpellCaster _caster;
        private StatusEffectManager _statusEffects;
        private FSMMonsterBrain _brain;
        private Health _health;

        // Cooldown timer per entry. Negative values mean "ready" so we can
        // also use them as the initial-delay countdown without an extra flag.
        private float[] _entryCooldowns;

        // Last-seen HP-loss bucket per entry. We trigger when the bucket
        // number rises (i.e. NPC has lost another step of HP since last
        // check). -1 = uninitialised.
        private int[] _hpLossBuckets;

        /// <summary>Number of configured spell entries — used by tests and bootstrap.</summary>
        public int EntryCount => entries != null ? entries.Count : 0;

        private void Awake()
        {
            _caster        = GetComponent<SpellCaster>();
            _statusEffects = GetComponent<StatusEffectManager>();
            _brain         = GetComponent<FSMMonsterBrain>();
            _health        = GetComponent<Health>();
            ResetCooldowns();
        }

        private void ResetCooldowns()
        {
            _entryCooldowns = new float[entries.Count];
            _hpLossBuckets  = new int[entries.Count];
            for (int i = 0; i < _entryCooldowns.Length; i++)
            {
                // initialDelaySeconds takes priority on the very first cast.
                // If unset, fall back to a uniform [0, period] random offset
                // so multiple entries don't fire on the same frame.
                _entryCooldowns[i] = entries[i].initialDelaySeconds > 0f
                    ? entries[i].initialDelaySeconds
                    : UnityEngine.Random.Range(0f, entries[i].periodSeconds);
                _hpLossBuckets[i] = 0;
            }
        }

        private void Update()
        {
            if (!castingEnabled) return;
            if (_caster == null) return;

            // Suppress while stunned or brain is in non-hostile states
            if (IsSupressed()) return;

            // Find player
            var player = FactionTargeting.EnemyTransformOf(gameObject);
            if (player == null) return;

            // Spirit-form players are intangible: don't burn cooldowns on a target
            // that can't be hit and won't aggro back.
            var playerGo = player != null ? player.gameObject : null;
            if (playerGo != null)
            {
                var playerSpirit = playerGo.GetComponent<PlayerSpiritState>();
                if (playerSpirit != null && playerSpirit.IsSpirit) return;
            }

            float distSq = ((Vector2)transform.position - (Vector2)player.position).sqrMagnitude;

            Vector2 dir = ((Vector2)player.position - (Vector2)transform.position).normalized;

            for (int i = 0; i < entries.Count; i++)
            {
                if (i >= _entryCooldowns.Length) break;

                ApplyHpLossTrigger(i);

                _entryCooldowns[i] -= Time.deltaTime;
                if (_entryCooldowns[i] > 0f) continue;

                // Per-entry distance gate. maxDistance == 0 falls back to the
                // global castRange. minDistance bails out only if the player
                // is closer than the floor — useful for kited bosses.
                float maxDist = entries[i].maxDistance > 0f ? entries[i].maxDistance : castRange;
                float minDist = Mathf.Max(0f, entries[i].minDistance);
                if (distSq > maxDist * maxDist) continue;
                if (minDist > 0f && distSq < minDist * minDist) continue;

                // Attempt cast
                if (_caster.TryCast(entries[i].spellSlot, dir))
                {
                    float jitter = UnityEngine.Random.Range(-entries[i].periodJitter, entries[i].periodJitter);
                    _entryCooldowns[i] = Mathf.Max(0.5f, entries[i].periodSeconds + jitter);

                    // Push the brain into NPCCastState so movement is blocked
                    // for the full prepare/channel/cooldown chain. The state
                    // polls SpellCaster.CurrentPhase and pops back to Chase /
                    // Attack when the caster returns to Ready. Without this
                    // hand-off the NPC would chase the player while casting.
                    if (_brain != null && _brain.FSM != null)
                        _brain.FSM.ChangeState(new NPCCastState());

                    // Fire only one spell per Update — multiple slots ready in
                    // the same frame would otherwise stack casts (mana / cooldown
                    // suppress them on the caster, but the NPCCastState push
                    // would still flicker).
                    break;
                }
            }
        }

        // HP-loss trigger: when the NPC has lost another step of max HP since
        // we last checked, force the entry's cooldown to ready. Useful for
        // boss "desperation casts" — when fireball stops being scary, the
        // boss starts dropping meteors at 50%/25%.
        private void ApplyHpLossTrigger(int entryIndex)
        {
            if (_health == null) return;
            float step = entries[entryIndex].hpLossStep;
            if (step <= 0f) return;
            if (_health.MaxHp <= 0) return;

            float lost = 1f - ((float)_health.CurrentHp / _health.MaxHp);
            // Clamp tiny negative values that can appear from rounding.
            if (lost < 0f) lost = 0f;
            int bucket = Mathf.FloorToInt(lost / step);
            if (bucket > _hpLossBuckets[entryIndex])
            {
                _entryCooldowns[entryIndex] = 0f;
                _hpLossBuckets[entryIndex]  = bucket;
            }
        }

        private bool IsSupressed()
        {
            // Stun
            if (_statusEffects != null && _statusEffects.IsStunned) return true;

            // FSM — don't cast while taking damage, dead, or already casting.
            // The "NPCCast" suppression matters for prepare-heavy spells where
            // SpellCaster.CurrentPhase != Ready blocks TryCast anyway, but
            // skipping the whole loop avoids the per-frame allocation of
            // direction vectors and player-distance checks.
            if (_brain != null)
            {
                string state = _brain.CurrentStateName;
                if (state == "Damage" || state == "Death" || state == "Unconscious" || state == "NPCCast")
                    return true;
            }

            return false;
        }

        /// <summary>Add an entry at runtime (e.g. during EntitySetup monster configuration).</summary>
        public void AddEntry(int spellSlot, float periodSeconds, float jitter = 0.5f)
        {
            AddEntry(new AutoCastEntry
            {
                spellSlot           = spellSlot,
                periodSeconds       = periodSeconds,
                periodJitter        = jitter,
                initialDelaySeconds = 0f,
                minDistance         = 0f,
                maxDistance         = 0f,
                hpLossStep          = 0f,
            });
        }

        /// <summary>
        /// Add a fully-configured entry at runtime. Used by tests and by any
        /// data-driven path that wants to thread per-entry advanced config
        /// (initial delay, distance gates, HP-loss trigger) into the NPC.
        /// </summary>
        public void AddEntry(AutoCastEntry entry)
        {
            if (entry == null) return;
            entries.Add(entry);

            Array.Resize(ref _entryCooldowns, entries.Count);
            Array.Resize(ref _hpLossBuckets,  entries.Count);
            _entryCooldowns[entries.Count - 1] = entry.initialDelaySeconds > 0f
                ? entry.initialDelaySeconds
                : UnityEngine.Random.Range(0f, entry.periodSeconds);
            _hpLossBuckets[entries.Count - 1] = 0;
        }

        /// <summary>
        /// Wipe all entries (and their cooldowns). Used by EntitySetup to overwrite
        /// any inspector-authored prefab defaults with the data-driven list from
        /// <see cref="MonsterDefinition.autoCastList"/>.
        /// </summary>
        public void Clear()
        {
            entries.Clear();
            _entryCooldowns = Array.Empty<float>();
            _hpLossBuckets  = Array.Empty<int>();
        }

        /// <summary>Toggle casting on/off without losing the configured entries.</summary>
        public void SetCastingEnabled(bool enabled) => castingEnabled = enabled;
    }
}
