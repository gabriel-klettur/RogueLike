using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Data.Feel;
using Valkur.Gameplay.Enemies;

namespace Valkur.Gameplay.Feel
{
    /// <summary>
    /// Where every camera beat comes from.
    ///
    /// All subscriptions live in <c>OnEnable</c> and all unsubscriptions in
    /// <c>OnDisable</c>, without exception: Domain Reload is off and
    /// <c>GameEvents.Clear()</c> runs at subsystem registration, so a handler registered
    /// anywhere else outlives the object that owns it.
    /// </summary>
    public sealed partial class CameraFeelDirector
    {
        /// <summary>
        /// Bosses register themselves, mirroring how they already register with the boss
        /// health bar. Static because a boss can spawn before this director exists.
        /// </summary>
        private static readonly HashSet<BossPhaseController> _bosses = new HashSet<BossPhaseController>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Declared here rather than inherited: DomainReloadStaticResetTests scans with
            // DeclaredOnly and would not see the base class's hook.
            _bosses.Clear();
        }

        private ComboCounter _subscribedCombo;

        private void OnEnable()
        {
            GameEvents.OnHitDealt += HandleHitDealt;
            GameEvents.OnEntityDamaged += HandleEntityDamaged;
            GameEvents.OnSpellCast += HandleSpellCast;
            GameEvents.OnLevelUp += HandleLevelUp;
            GameEvents.OnPlayerDied += HandlePlayerDied;
            GameEvents.OnPlayerRevived += HandlePlayerRevived;

            foreach (var boss in _bosses)
                if (boss != null) boss.OnPhaseChanged += HandleBossPhase;
        }

        private void OnDisable()
        {
            GameEvents.OnHitDealt -= HandleHitDealt;
            GameEvents.OnEntityDamaged -= HandleEntityDamaged;
            GameEvents.OnSpellCast -= HandleSpellCast;
            GameEvents.OnLevelUp -= HandleLevelUp;
            GameEvents.OnPlayerDied -= HandlePlayerDied;
            GameEvents.OnPlayerRevived -= HandlePlayerRevived;

            foreach (var boss in _bosses)
                if (boss != null) boss.OnPhaseChanged -= HandleBossPhase;

            if (_subscribedCombo != null)
            {
                _subscribedCombo.OnComboReset -= HandleComboReset;
                _subscribedCombo = null;
            }
        }

        /// <summary>
        /// Spells report a hand or muzzle child as the attacker, so comparing against the
        /// player object alone drops most of them.
        /// </summary>
        private bool IsPlayerActor(GameObject go)
            => go != null && _playerGo != null &&
               (go == _playerGo || go.transform.IsChildOf(_playerGo.transform));

        // ── Dealing damage ────────────────────────────────────────────────────

        private void HandleHitDealt(GameObject attacker, GameObject victim, int damage)
        {
            if (!IsPlayerActor(attacker) || victim == null) return;

            // A connect cancels the pending whiff: the swing that armed it landed.
            _pendingWhiffAt = -1f;

            Vector2 playerPos = _playerTransform != null ? (Vector2)_playerTransform.position : Vector2.zero;
            Vector2 fallback = _playerController != null ? _playerController.FacingDirection : Vector2.right;
            Vector2 toVictim = CameraFeelMath.SafeDirection(playerPos, victim.transform.position, fallback);

            float intensity = CameraFeelMath.ScaleByDamage(1f, damage, _profile.DamageReference);
            if (_playerCombo != null)
                intensity = CameraFeelMath.ScaleByCombo(intensity, _playerCombo.Current,
                                                        _profile.ComboCap, _profile.ComboGain);

            // Kick TOWARD what was hit — the frame follows through with the blow.
            FireCue(CameraFeelCue.AttackConnect, toVictim, intensity);
            SubscribeComboIfNeeded();
        }

        // ── Taking damage ─────────────────────────────────────────────────────

        private void HandleEntityDamaged(GameObject victim, GameObject attacker, int amount)
        {
            if (_playerGo == null || victim != _playerGo || amount <= 0) return;

            int maxHp = _playerHealth != null ? _playerHealth.MaxHp : 0;
            float severity = CameraFeelMath.SeverityFromDamage(amount, maxHp,
                                                               _profile.SevereDamageFraction);

            // Kick AWAY from whatever hit you. An unattributed source — a burn tick, a puddle
            // — has no direction to give, and gets the trauma and the lead freeze without the
            // punch rather than a punch in an invented direction.
            Vector2 direction = Vector2.zero;
            if (attacker != null && attacker != _playerGo && _playerTransform != null)
                direction = CameraFeelMath.SafeDirection(attacker.transform.position,
                                                         _playerTransform.position, Vector2.zero);

            FireCue(CameraFeelCue.Hurt, direction, 0.5f + 0.5f * severity);

            if (severity >= 0.6f) FireFreeze(0.05f);
        }

        // ── Casting ───────────────────────────────────────────────────────────

        private void HandleSpellCast(GameObject caster, string spellKey, string displayName,
                                     float cooldownDuration)
        {
            if (!IsPlayerActor(caster) || string.IsNullOrEmpty(spellKey)) return;

            SpellDefinition spell = ResolveSpell(caster, spellKey);
            if (spell == null) return;

            Vector2 facing = _playerController != null ? _playerController.FacingDirection : Vector2.right;

            if (CameraFeelMath.IsMeleeSwing(spell.range, spell.distance, spell.damage))
            {
                // Armed, not fired: a swing is only a whiff once the window closes with no
                // hit reported. There is no event for "missed".
                _pendingWhiffAt = Time.realtimeSinceStartup + _profile.WhiffWindowSeconds;
                _pendingWhiffDirection = facing;
                return;
            }

            // Light casts get nothing at all, and that is load-bearing. Reserving the camera
            // for weight is what makes the heavy casts land; a camera that reacts to every
            // fireball reads as noise.
            if (!CameraFeelMath.IsHeavyCast(spell.prepareDuration, cooldownDuration, spell.manaCost,
                                            _profile.HeavyPrepareSeconds,
                                            _profile.HeavyCooldownSeconds,
                                            _profile.HeavyManaCost)) return;

            // Recoil, not follow-through: the frame is pushed back by what was released.
            FireCue(CameraFeelCue.CastHeavy, -facing, 1f);
        }

        private SpellDefinition ResolveSpell(GameObject caster, string spellKey)
        {
            var spellCaster = caster.GetComponentInParent<Spells.SpellCaster>();
            return spellCaster != null ? spellCaster.GetSpellByKey(spellKey) : null;
        }

        // ── Rewards and states ────────────────────────────────────────────────

        private void HandleLevelUp(GameObject entity, int newLevel)
        {
            if (!IsPlayerActor(entity)) return;
            // Experience raises this inside a while loop, so one large pickup can fire it
            // several times in a single frame; the cue's own minimum interval swallows them.
            FireCue(CameraFeelCue.LevelUp, Vector2.zero, 1f);
        }

        private void HandleComboReset(int finalCount)
        {
            if (finalCount < 10) return;
            FireCue(CameraFeelCue.ComboPayoff, Vector2.zero, 1f);
        }

        private void HandlePlayerDied()
        {
            _deathFlowActive = true;
            ResetTransients();
            _state.LeadScale = _profile.SpiritLeadScale;

            // Fired past the death gate, which suppresses every other cue from here on.
            FeelCue tuning = _profile.GetCue(CameraFeelCue.Death);
            AddTraumaWithinBudget(tuning.traumaAdd * _profile.MasterIntensity01,
                                  tuning.traumaDecayPerSecond, tuning.shakeFrequencyHz,
                                  Time.realtimeSinceStartup);
            _state.LeadFreezeRemaining = tuning.leadFreezeSeconds;
        }

        private void HandlePlayerRevived()
        {
            _deathFlowActive = false;
            _state.LeadScale = 1f;
        }

        private void HandleBossPhase(int oldPhase, int newPhase)
        {
            // ForcePhase can move backwards; only an escalation is an event.
            if (newPhase <= oldPhase) return;
            FireCue(CameraFeelCue.BossPhase, Vector2.zero, 1f);
        }

        /// <summary>
        /// The combo counter is added to the player after this director may already exist, so
        /// it is picked up on the first hit rather than at startup.
        /// </summary>
        private void SubscribeComboIfNeeded()
        {
            if (_subscribedCombo != null || _playerCombo == null) return;
            _subscribedCombo = _playerCombo;
            _subscribedCombo.OnComboReset += HandleComboReset;
        }

        internal void RegisterBossInstance(BossPhaseController boss)
        {
            if (boss == null || !_bosses.Add(boss)) return;
            boss.OnPhaseChanged += HandleBossPhase;
        }

        internal void UnregisterBossInstance(BossPhaseController boss)
        {
            if (boss == null || !_bosses.Remove(boss)) return;
            boss.OnPhaseChanged -= HandleBossPhase;
        }

        /// <summary>Registration that works whether or not a director exists yet.</summary>
        internal static void TrackBoss(BossPhaseController boss)
        {
            if (boss == null) return;
            if (HasInstance) Instance.RegisterBossInstance(boss);
            else _bosses.Add(boss);
        }

        internal static void UntrackBoss(BossPhaseController boss)
        {
            if (boss == null) return;
            if (HasInstance) Instance.UnregisterBossInstance(boss);
            else _bosses.Remove(boss);
        }
    }
}
