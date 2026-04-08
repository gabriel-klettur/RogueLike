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

        // ── Runtime state ──────────────────────────────────────────────────
        private int _current;
        private int _best;
        private float _windowEndTime;
        private float _breakFlashEndTime;
        private readonly Dictionary<int, float> _lastHitTimeByTarget = new Dictionary<int, float>();
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
            if (attacker != gameObject) return;
            RegisterHit(victim, damage, "combat");
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
        public void RegisterHit(GameObject target, float damage, string source = "unknown")
        {
            if (damage <= 0f) return;
            if (target == null) return;

            float now = Time.time;
            int tid = target.GetInstanceID();

            // Same-target cooldown
            if (_lastHitTimeByTarget.TryGetValue(tid, out float lastT) &&
                (now - lastT) < sameTargetCooldown)
            {
                // Still refresh window if active
                if (IsActive)
                {
                    float eff = EffectiveWindow(_current > 0 ? _current : 1);
                    _windowEndTime = now + eff;
                }
                return;
            }

            _current++;
            if (_current > _best) _best = _current;

            float window = EffectiveWindow(_current);
            _windowEndTime = now + window;
            _lastHitTimeByTarget[tid] = now;

            OnComboChanged?.Invoke(_current);
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
