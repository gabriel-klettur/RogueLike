using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Gameplay.FSM;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Drives boss phase transitions based on HP thresholds. Designers
    /// author phases as ordered breakpoints (HP fraction → phase index);
    /// the controller listens to <see cref="Health.OnHpChanged"/> and
    /// fires <see cref="OnPhaseChanged"/> when the boss crosses into a
    /// new phase. Listeners (NPCAutoCast for new spell rotations,
    /// FSMMonsterBrain for animation swaps, audio for enrage stings) read
    /// <see cref="CurrentPhase"/> to react.
    ///
    /// Why a dedicated controller and not just a state inside the FSM:
    /// the FSM tracks "what is the boss doing right now" (Chase, Attack,
    /// Cast). Phases are orthogonal — a boss can be in Phase 1 Chase,
    /// then Phase 2 Chase, with nothing about the FSM transitioning. The
    /// phase controller layers on top.
    ///
    /// Phase transitions are one-way (HP only decreases meaningfully —
    /// healing back over a threshold doesn't drop the phase). This keeps
    /// listener logic simple: each phase change is a permanent escalation.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class BossPhaseController : MonoBehaviour
    {
        [Serializable]
        public class PhaseBreakpoint
        {
            [Tooltip("HP fraction at which this phase activates. 1.0 = full HP, " +
                     "0.5 = half HP, 0.0 = death. Phases ordered descending.")]
            [Range(0f, 1f)] public float hpFraction = 1f;

            [Tooltip("Optional designer-readable label for logs / debugger.")]
            public string label;
        }

        [Header("Phases")]
        [Tooltip("Phase breakpoints in DESCENDING HP-fraction order. Phase 0 is " +
                 "the entry phase (HP fraction 1.0); subsequent entries trigger " +
                 "as HP falls below their thresholds. List is normalised on Awake " +
                 "so out-of-order entries are forgiven.")]
        [SerializeField] private List<PhaseBreakpoint> phases = new List<PhaseBreakpoint>
        {
            new PhaseBreakpoint { hpFraction = 1.00f, label = "Phase 1" },
            new PhaseBreakpoint { hpFraction = 0.50f, label = "Phase 2" },
            new PhaseBreakpoint { hpFraction = 0.20f, label = "Phase 3" },
        };

        public int    CurrentPhase   { get; private set; }
        public string CurrentLabel   => CurrentPhase >= 0 && CurrentPhase < phases.Count
                                         ? phases[CurrentPhase].label : string.Empty;
        public int    PhaseCount     => phases != null ? phases.Count : 0;

        /// <summary>Fires (oldPhase, newPhase) on every transition.</summary>
        public event Action<int, int> OnPhaseChanged;

        private Health _health;

        private void Awake()
        {
            _health = GetComponent<Health>();
            NormalisePhases();
            CurrentPhase = 0;
        }

        private void OnEnable()
        {
            if (_health != null) _health.OnHpChanged += OnHpChanged;
            // Publish presence so BossHealthBarHUD can claim the top-centre slot
            // when the player closes in. Registration is the only coupling —
            // the controller never talks to the HUD directly.
            HUD.BossHealthBarHUD.RegisterBoss(this);
            Feel.CameraFeel.RegisterBoss(this);
        }

        private void OnDisable()
        {
            if (_health != null) _health.OnHpChanged -= OnHpChanged;
            HUD.BossHealthBarHUD.UnregisterBoss(this);
            Feel.CameraFeel.UnregisterBoss(this);
        }

        // Internal seam used by tests in EditMode where Awake doesn't fire.
        public void InitForTest(Health health)
        {
            _health = health;
            NormalisePhases();
            CurrentPhase = 0;
        }

        // Public so tests can drive transitions without spinning a real
        // Health component each time.
        public void EvaluateAt(float hpFraction)
        {
            int target = ResolvePhaseAt(hpFraction);
            if (target > CurrentPhase)
            {
                int old = CurrentPhase;
                CurrentPhase = target;
                OnPhaseChanged?.Invoke(old, target);
            }
        }

        private void OnHpChanged(int current, int max)
        {
            float frac = max > 0 ? (float)current / max : 0f;
            EvaluateAt(frac);
        }

        // Sort phases by descending HP fraction so the threshold ladder
        // reads top-to-bottom. Designers don't have to author them in
        // order — the inspector view ends up tidy regardless.
        private void NormalisePhases()
        {
            if (phases == null || phases.Count == 0) return;
            phases.Sort((a, b) => b.hpFraction.CompareTo(a.hpFraction));
        }

        // Returns the index of the deepest breakpoint whose threshold is
        // at or above the given hp fraction. With phases sorted descending
        // (1.00, 0.50, 0.20), at frac 0.4 → phase 1 (the 0.50 entry); at
        // frac 0.1 → phase 2 (the 0.20 entry).
        public int ResolvePhaseAt(float hpFraction)
        {
            int phase = 0;
            for (int i = 0; i < phases.Count; i++)
            {
                if (hpFraction <= phases[i].hpFraction) phase = i;
                else break; // sorted descending — once we exceed, no later breakpoint can match
            }
            return phase;
        }

        /// <summary>
        /// Forces an immediate phase transition to the breakpoint whose label
        /// matches <paramref name="label"/> (case-insensitive). Idempotent — if
        /// the requested phase equals the current one nothing happens. Honors
        /// the one-way escalation rule: cannot move to a lower index.
        /// Returns true on a successful change. Used by chart cues with
        /// <c>BossCueType.SwitchPhase</c>.
        /// </summary>
        public bool ForcePhase(string label)
        {
            if (string.IsNullOrEmpty(label) || phases == null) return false;
            for (int i = 0; i < phases.Count; i++)
            {
                if (string.Equals(phases[i].label, label, StringComparison.OrdinalIgnoreCase))
                {
                    if (i <= CurrentPhase) return false;
                    int old = CurrentPhase;
                    CurrentPhase = i;
                    OnPhaseChanged?.Invoke(old, i);
                    return true;
                }
            }
            return false;
        }
    }
}
