using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Combat;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// NPC periodic spell-casting behaviour.
    /// Mirrors Python AutoCastComponent + AutoCastSystem.
    ///
    /// Each entry defines a spell slot index and a cast period (seconds).
    /// The system targets the player. Casting is suppressed while the NPC
    /// is in DamageState, DeathState, UnconsiousState or is stunned.
    ///
    /// Attach alongside <see cref="FSMMonsterBrain"/> and <see cref="SpellCaster"/>.
    /// wire spell slots on the SpellCaster, then populate <see cref="entries"/>.
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
        }

        [Header("Auto Cast Entries")]
        [SerializeField] private List<AutoCastEntry> entries = new List<AutoCastEntry>();

        [Header("Cast Range")]
        [Tooltip("Maximum distance to player to attempt a cast (world units). Python: TILE_SIZE * n / 16")]
        [SerializeField] private float castRange = 8f;

        [Header("State")]
        [SerializeField] private bool castingEnabled = true;

        private SpellCaster _caster;
        private StatusEffectManager _statusEffects;
        private FSMMonsterBrain _brain;

        // Maintain cooldown array sized per entries — backing array allocated in Awake
        private float[] _entryCooldowns;

        /// <summary>Number of configured spell entries — used by tests and bootstrap.</summary>
        public int EntryCount => entries != null ? entries.Count : 0;

        private void Awake()
        {
            _caster       = GetComponent<SpellCaster>();
            _statusEffects = GetComponent<StatusEffectManager>();
            _brain         = GetComponent<FSMMonsterBrain>();
            ResetCooldowns();
        }

        private void ResetCooldowns()
        {
            _entryCooldowns = new float[entries.Count];
            for (int i = 0; i < _entryCooldowns.Length; i++)
                _entryCooldowns[i] = UnityEngine.Random.Range(0f, entries[i].periodSeconds);
        }

        private void Update()
        {
            if (!castingEnabled) return;
            if (_caster == null) return;

            // Suppress while stunned or brain is in non-hostile states
            if (IsSupressed()) return;

            // Find player
            var player = Valkur.Core.EntityRegistry.PlayerTransform;
            if (player == null) return;

            float distSq = ((Vector2)transform.position - (Vector2)player.position).sqrMagnitude;
            if (distSq > castRange * castRange) return;

            Vector2 dir = ((Vector2)player.position - (Vector2)transform.position).normalized;

            for (int i = 0; i < entries.Count; i++)
            {
                if (i >= _entryCooldowns.Length) break;
                _entryCooldowns[i] -= Time.deltaTime;
                if (_entryCooldowns[i] > 0f) continue;

                // Attempt cast
                if (_caster.TryCast(entries[i].spellSlot, dir))
                {
                    float jitter = UnityEngine.Random.Range(-entries[i].periodJitter, entries[i].periodJitter);
                    _entryCooldowns[i] = Mathf.Max(0.5f, entries[i].periodSeconds + jitter);
                }
            }
        }

        private bool IsSupressed()
        {
            // Stun
            if (_statusEffects != null && _statusEffects.IsStunned) return true;

            // FSM — don't cast while taking damage or already dead/unconscious
            if (_brain != null)
            {
                string state = _brain.CurrentStateName;
                if (state == "Damage" || state == "Death" || state == "Unconscious")
                    return true;
            }

            return false;
        }

        /// <summary>Add an entry at runtime (e.g. during EntitySetup monster configuration).</summary>
        public void AddEntry(int spellSlot, float periodSeconds, float jitter = 0.5f)
        {
            entries.Add(new AutoCastEntry
            {
                spellSlot     = spellSlot,
                periodSeconds = periodSeconds,
                periodJitter  = jitter
            });

            // Resize cooldown array
            Array.Resize(ref _entryCooldowns, entries.Count);
            _entryCooldowns[entries.Count - 1] = UnityEngine.Random.Range(0f, periodSeconds);
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
        }

        /// <summary>Toggle casting on/off without losing the configured entries.</summary>
        public void SetCastingEnabled(bool enabled) => castingEnabled = enabled;
    }
}
