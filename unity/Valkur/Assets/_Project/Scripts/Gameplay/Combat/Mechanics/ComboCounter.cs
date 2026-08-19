using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Tracks hit-streak combos per entity (normally the player).
    /// Mirrors Python ComboCounterComponent + ComboSystem:
    ///   - Window shrinks as combo grows (difficulty_increase_per_hit).
    ///   - Same-target cooldown prevents spam counting.
    ///   - Subscribes to GameEvents.OnHitDealt (only counts attacker == owner).
    ///   - Events allow UI to react without polling.
    /// </summary>
    public class ComboCounter : MonoBehaviour
    {
        [Header("Window")]
        [Tooltip("Base combo window duration in seconds (Python: 2.0).")]
        [SerializeField] private float windowSeconds = 2f;

        [Tooltip("Minimum window after difficulty reduction (Python: 0.3).")]
        [SerializeField] private float minWindowSeconds = 0.3f;

        [Tooltip("Window shrink factor per hit: window *= (1 - value)^(n-1) (Python: 0.05).")]
        [SerializeField] private float difficultyIncreasePerHit = 0.05f;

        [Header("Anti-spam")]
        [Tooltip("Seconds before the same target can count again (Python: 0.5).")]
        [SerializeField] private float sameTargetCooldown = 0.5f;

        [Header("Flash")]
        [Tooltip("Duration the break-flash UI plays (Python: 0.3).")]
        [SerializeField] private float breakFlashDuration = 0.3f;

        [Header("Rules (mirror Python combo_rules.json)")]
        [Tooltip("Source tags whose hits count toward the combo. " +
                 "Hits with a source NOT in this list are ignored (Python allowed_sources). " +
                 "Empty array disables the filter.")]
        [SerializeField] private string[] allowedSources = { "combat", "melee", "hitbox", "fireball" };

        [Tooltip("Minimum damage for a hit to count (Python min_damage). Hits below the threshold " +
                 "refresh the active window but do not increment the combo.")]
        [SerializeField] private float minDamage = 1f;

        [Tooltip("If true, only hits where the victim is on an enemy layer count " +
                 "(Python require_enemy). Default: NPC layer.")]
        [SerializeField] private bool requireEnemy = true;

        [Tooltip("Layer(s) treated as enemies when require_enemy is on.")]
        [SerializeField] private LayerMask enemyLayers = 1 << 9; // NPC

        [Tooltip("If true, two consecutive hits on the same target do not count " +
                 "(Python require_unique_target). The same-target cooldown is a complementary " +
                 "time-based filter.")]
        [SerializeField] private bool requireUniqueTarget = true;

        // ── Runtime state ──────────────────────────────────────────────────
        private int _current;
        private int _best;
        private float _windowEndTime;
        private float _breakFlashEndTime;
        private readonly Dictionary<int, float> _lastHitTimeByTarget = new Dictionary<int, float>();
        private int _lastTargetInstanceId = 0;
        private int _totalCompleted;
        private int _lastCompletedCount;

        // ── Public read ────────────────────────────────────────────────────
        public int Current        => _current;
        public int Best           => _best;
        public bool IsActive      => _current > 0 && Time.time < _windowEndTime;
        public float WindowEnd    => _windowEndTime;
        public float BreakFlashEndTime => _breakFlashEndTime;
        public bool IsBreakFlashing    => Time.time < _breakFlashEndTime;
        public int TotalCompleted      => _totalCompleted;
        public int LastCompletedCount  => _lastCompletedCount;

        /// <summary>
        /// Length in seconds of the window the CURRENT combo is running on.
        /// The window shrinks as the streak grows, so UI that wants a truthful
        /// drain bar must divide by this value rather than by the base window.
        /// </summary>
        public float CurrentWindowDuration => EffectiveWindow(Mathf.Max(1, _current));

        /// <summary>
        /// Fraction of the current combo window still left: 1 right after a hit
        /// lands, 0 the instant it expires. Returns 0 when no combo is running.
        /// </summary>
        public float WindowRemaining01
        {
            get
            {
                if (_current <= 0) return 0f;
                float window = CurrentWindowDuration;
                if (window <= 0f) return 0f;
                return Mathf.Clamp01((_windowEndTime - Time.time) / window);
            }
        }

        // ── Events ─────────────────────────────────────────────────────────
        /// <summary>Fired when the combo count changes. Arg = new count.</summary>
        public event Action<int> OnComboChanged;

        /// <summary>Fired when the combo resets after the window expires.</summary>
        public event Action<int> OnComboReset;  // arg = final count

        // ── Update ─────────────────────────────────────────────────────────
        private void OnEnable()
        {
            GameEvents.OnHitDealt += HandleHitDealt;
        }

        private void OnDisable()
        {
            GameEvents.OnHitDealt -= HandleHitDealt;
        }

        private void HandleHitDealt(GameObject attacker, GameObject victim, int damage)
        {
            if (!IsOwnHit(attacker)) return;
            RegisterHit(victim, damage, "combat");
        }

        /// <summary>
        /// True when <paramref name="attacker"/> is this entity, or something in
        /// its hierarchy. Spells report the transform they were cast from, which
        /// is often a child of the caster (a hand, a muzzle) rather than the
        /// entity itself, so those hits have to count too.
        /// </summary>
        public bool IsOwnHit(GameObject attacker)
        {
            if (attacker == null) return false;
            if (attacker == gameObject) return true;
            return attacker.transform.IsChildOf(transform);
        }

        private void Update()
        {
            if (_current > 0 && Time.time >= _windowEndTime)
                BreakCombo();
        }

        // ── Public API ─────────────────────────────────────────────────────

        /// <summary>
        /// Register a hit event. Increments the combo if the hit passes anti-spam filters.
        /// </summary>
        /// <param name="target">The entity that was hit.</param>
        /// <param name="damage">Damage dealt (must be > 0).</param>
        /// <param name="source">Source tag: "slash", "projectile", etc.</param>
        public void RegisterHit(GameObject target, float damage, string source = "combat")
        {
            if (target == null) return;

            float now = Time.time;
            int tid = target.GetInstanceID();

            // Rule: min damage threshold (Python min_damage)
            if (damage < minDamage) { RefreshWindowIfActive(now); return; }

            // Rule: allowed_sources filter (Python allowed_sources)
            if (allowedSources != null && allowedSources.Length > 0 &&
                Array.IndexOf(allowedSources, source) < 0)
            {
                RefreshWindowIfActive(now);
                return;
            }

            // Rule: require_enemy — reject if target isn't on an enemy layer
            if (requireEnemy && (enemyLayers.value & (1 << target.layer)) == 0)
            {
                RefreshWindowIfActive(now);
                return;
            }

            // Rule: require_unique_target — reject consecutive hits on same target while in combo
            if (requireUniqueTarget && _current > 0 && _lastTargetInstanceId == tid)
            {
                RefreshWindowIfActive(now);
                return;
            }

            // Anti-spam: same-target cooldown (time-based, complements require_unique_target)
            if (_lastHitTimeByTarget.TryGetValue(tid, out float lastT) &&
                (now - lastT) < sameTargetCooldown)
            {
                RefreshWindowIfActive(now);
                return;
            }

            // Valid hit — increment and refresh window
            _current++;
            if (_current > _best) _best = _current;

            float window = EffectiveWindow(_current);
            _windowEndTime = now + window;
            _lastHitTimeByTarget[tid] = now;
            _lastTargetInstanceId = tid;

            OnComboChanged?.Invoke(_current);
        }

        private void RefreshWindowIfActive(float now)
        {
            if (!IsActive) return;
            float eff = EffectiveWindow(_current > 0 ? _current : 1);
            _windowEndTime = now + eff;
        }

        /// <summary>
        /// Explicitly break the combo (e.g. player takes damage).
        /// </summary>
        public void ForceBreak()
        {
            BreakCombo();
        }

        // ── Internal ───────────────────────────────────────────────────────

        private void BreakCombo()
        {
            if (_current == 0) return;

            _lastCompletedCount = _current;
            _totalCompleted++;
            int final = _current;
            _current = 0;
            _windowEndTime = 0f;
            _lastHitTimeByTarget.Clear();
            _lastTargetInstanceId = 0;
            _breakFlashEndTime = Time.time + breakFlashDuration;

            OnComboReset?.Invoke(final);
        }

        private float EffectiveWindow(int n)
        {
            if (n <= 1) return windowSeconds;
            float diff = Mathf.Clamp(difficultyIncreasePerHit, 0f, 0.95f);
            float effective = windowSeconds * Mathf.Pow(1f - diff, n - 1);
            return Mathf.Max(minWindowSeconds, effective);
        }
    }
}
