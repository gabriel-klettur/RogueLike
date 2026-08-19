using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// Top-of-screen boss bar. It occupies the same slot as the ordinary
    /// <c>TargetHUD</c> and outranks it: while a boss bar is up,
    /// <see cref="IsShowing"/> is true and the target panel keeps itself hidden,
    /// so the two never stack on top of each other.
    ///
    /// Engagement is automatic. Every <see cref="BossPhaseController"/> registers
    /// itself while enabled; the HUD polls that registry a few times a second and
    /// binds to the nearest living boss inside <see cref="engageRadius"/> of the
    /// player, releasing it when the boss dies, despawns or is left behind.
    /// <see cref="BindToBoss"/> stays public for scripted encounters and tests.
    ///
    /// The bar shows the boss name, its current phase label, one pip per phase,
    /// and an HP bar with a trailing "damage ghost" so a big hit reads as a chunk
    /// torn out rather than a number that quietly moved.
    /// </summary>
    public sealed partial class BossHealthBarHUD : SingletonMonoBehaviour<BossHealthBarHUD>
    {
        [Header("Auto-engage")]
        [SerializeField, Tooltip("Bind automatically to the nearest living boss in range. " +
                                 "Turn off for fully scripted encounters that call BindToBoss themselves.")]
        private bool autoEngage = true;

        [SerializeField, Tooltip("World-space distance at which a boss claims the bar.")]
        private float engageRadius = 16f;

        [SerializeField, Tooltip("Extra distance the player must travel beyond the engage radius " +
                                 "before the bar lets go. Stops it flickering at the boundary.")]
        private float disengageHysteresis = 4f;

        [Header("Damage ghost")]
        [SerializeField, Tooltip("Seconds the ghost bar holds before it starts catching up.")]
        private float ghostDelay = 0.35f;

        [SerializeField, Tooltip("How fast the ghost bar drains toward the real value, in bar fractions per second.")]
        private float ghostSpeed = 0.55f;

        // ── Registry ──────────────────────────────────────────────────────
        private static readonly List<BossPhaseController> Registered = new List<BossPhaseController>();

        /// <summary>True while a boss bar is on screen. TargetHUD yields to this.</summary>
        public static bool IsShowing { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayModeEnter()
        {
            Registered.Clear();
            IsShowing = false;
        }

        /// <summary>Called by <see cref="BossPhaseController"/> while it is enabled.</summary>
        public static void RegisterBoss(BossPhaseController boss)
        {
            if (boss == null || Registered.Contains(boss)) return;
            Registered.Add(boss);
        }

        /// <summary>Called by <see cref="BossPhaseController"/> when it is disabled or destroyed.</summary>
        public static void UnregisterBoss(BossPhaseController boss)
        {
            if (boss == null) return;
            Registered.Remove(boss);
        }

        // ── Runtime state ─────────────────────────────────────────────────
        private const float EngagePollInterval = 0.25f;

        private BossPhaseController _boundBoss;
        private Health _boundHealth;
        private float _pollTimer;
        private float _targetFill;
        private float _ghostFill;
        private float _ghostHoldTimer;

        protected override bool Persist => false;

        // ── Public API ────────────────────────────────────────────────────

        /// <summary>
        /// Bind the bar to a boss. Passing null hides it. Re-binding always
        /// releases the previous boss's events first, so a second encounter
        /// never leaves the bar reacting to the first boss's HP.
        /// </summary>
        public void BindToBoss(BossPhaseController boss)
        {
            UnbindCurrent();

            _boundBoss = boss;
            if (_boundBoss == null)
            {
                SetVisible(false);
                return;
            }

            _boundHealth = boss.GetComponent<Health>();
            EnsureBuilt();

            _boundBoss.OnPhaseChanged += OnPhaseChanged;
            if (_boundHealth != null) _boundHealth.OnHpChanged += OnHpChanged;

            SetVisible(true);
            RebuildPhasePips();
            Refresh();

            // A fresh encounter starts with a full ghost — no phantom chunk.
            _ghostFill = _targetFill;
            _ghostHoldTimer = 0f;
        }

        /// <summary>True when the bar is bound to a boss that is still alive.</summary>
        public bool IsActive => _boundBoss != null && _boundHealth != null && !_boundHealth.IsDead;

        /// <summary>The boss currently owning the bar, or null.</summary>
        public BossPhaseController BoundBoss => _boundBoss;

        // ── Lifecycle ─────────────────────────────────────────────────────

        protected override void OnSingletonAwake() => EnsureBuilt();

        protected override void OnDestroy()
        {
            UnbindCurrent();
            SetVisible(false);
            base.OnDestroy();
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Frame driver, public so PlayMode tests can step it deterministically.</summary>
        public void Tick(float deltaTime)
        {
            if (autoEngage)
            {
                _pollTimer -= deltaTime;
                if (_pollTimer <= 0f)
                {
                    _pollTimer = EngagePollInterval;
                    PollEngagement();
                }
            }

            if (!IsShowing) return;
            TickGhost(deltaTime);
        }

        // ── Engagement ────────────────────────────────────────────────────

        private void PollEngagement()
        {
            var player = EntityRegistry.Player;
            if (player == null) return;

            Vector3 playerPos = player.transform.position;

            // Keeping the bound boss needs a slightly larger radius than claiming
            // one, so walking the boundary doesn't strobe the bar on and off.
            if (_boundBoss != null && IsEngageable(_boundBoss, playerPos, engageRadius + disengageHysteresis))
                return;

            BossPhaseController nearest = null;
            float nearestSqr = float.MaxValue;

            for (int i = Registered.Count - 1; i >= 0; i--)
            {
                var candidate = Registered[i];
                if (candidate == null) { Registered.RemoveAt(i); continue; }
                if (!IsEngageable(candidate, playerPos, engageRadius)) continue;

                float sqr = (candidate.transform.position - playerPos).sqrMagnitude;
                if (sqr >= nearestSqr) continue;
                nearestSqr = sqr;
                nearest = candidate;
            }

            if (nearest != _boundBoss) BindToBoss(nearest);
        }

        private static bool IsEngageable(BossPhaseController boss, Vector3 playerPos, float radius)
        {
            if (boss == null || !boss.isActiveAndEnabled) return false;

            var health = boss.GetComponent<Health>();
            if (health == null || health.IsDead) return false;

            return (boss.transform.position - playerPos).sqrMagnitude <= radius * radius;
        }

        // ── Binding internals ─────────────────────────────────────────────

        private void UnbindCurrent()
        {
            if (_boundBoss != null) _boundBoss.OnPhaseChanged -= OnPhaseChanged;
            if (_boundHealth != null) _boundHealth.OnHpChanged -= OnHpChanged;
            _boundBoss = null;
            _boundHealth = null;
        }

        private void OnHpChanged(int current, int max)
        {
            // Auto-release on death so the bar never lingers over a corpse.
            if (current <= 0)
            {
                BindToBoss(null);
                return;
            }
            Refresh();
        }

        private void OnPhaseChanged(int oldPhase, int newPhase) => Refresh();

        private void TickGhost(float deltaTime)
        {
            if (_ghostImage == null) return;

            if (_ghostFill <= _targetFill)
            {
                _ghostFill = _targetFill;
                _ghostImage.fillAmount = _ghostFill;
                return;
            }

            if (_ghostHoldTimer > 0f)
            {
                _ghostHoldTimer -= deltaTime;
                return;
            }

            _ghostFill = Mathf.MoveTowards(_ghostFill, _targetFill, ghostSpeed * deltaTime);
            _ghostImage.fillAmount = _ghostFill;
        }

        private void Refresh()
        {
            if (_boundBoss == null || _boundHealth == null)
            {
                SetVisible(false);
                return;
            }

            float previous = _targetFill;
            _targetFill = _boundHealth.MaxHp > 0
                ? Mathf.Clamp01((float)_boundHealth.CurrentHp / _boundHealth.MaxHp)
                : 0f;

            if (_targetFill < previous) _ghostHoldTimer = ghostDelay;

            ApplyVisualState(_boundBoss, _boundHealth, _targetFill);
        }
    }
}
