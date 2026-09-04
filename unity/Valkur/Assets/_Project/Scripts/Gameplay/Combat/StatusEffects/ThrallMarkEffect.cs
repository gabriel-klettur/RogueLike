using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay.Combat
{
    /// <summary>
    /// A claim on a LIVING enemy's death. While the mark holds, killing the bearer raises it
    /// as an ally; if the mark expires first, the cast was spent for nothing.
    ///
    /// <para>WHY THE LIVING TARGET AND NOT A CORPSE. A corpse-raising spell has to hook
    /// <c>Health.OnDeath</c> or <c>DeathSequenceController</c>, then find a body that
    /// <c>deathDisappearTime</c> may already have despawned, and reconstruct which
    /// <c>MonsterDefinition</c> it was from something that no longer exists — which means a
    /// death registry, kept in sync, holding data about entities that are gone. Marking the
    /// living target inverts all of it: the mark is carried BY the creature, so at the moment
    /// of death every fact needed is still on a live GameObject. The registry is not
    /// implemented cheaply here; it is not implemented at all.</para>
    ///
    /// <para>AND IT IS THE BETTER SPELL. The player spends mana and a cast on a bet placed
    /// BEFORE the kill, on a target they believe they can finish inside the window. A target
    /// that walks away is the cost, and a spell that can be wasted is a spell worth aiming.
    /// It also telegraphs to the other side: a marked monster is visibly marked, which makes
    /// the seconds before it dies tense rather than administrative.</para>
    ///
    /// <para>Being a <see cref="StatusEffect"/> is what makes the mark survive everything
    /// else happening to the target — frozen, stunned, rooted, burning — because
    /// <c>StatusEffectManager.Apply</c> only replaces an effect of the SAME type.</para>
    /// </summary>
    public sealed class ThrallMarkEffect : StatusEffect
    {
        /// <summary>Seconds the raised ally serves before it collapses for good.</summary>
        private readonly float _thrallDuration;

        /// <summary>
        /// Fraction of the raised creature's own max HP it keeps. Well under 1 on purpose: a
        /// thrall is a borrowed body, not a second player, and a monster raised at full
        /// strength makes killing it the wrong move.
        /// </summary>
        private const float THRALL_HEALTH_SCALE = 0.6f;

        private Health _health;
        private StatusEffectManager _owner;
        private System.Action _deathHandler;
        private bool _consumed;

        // Captured while the bearer is ALIVE, because by the time the raising happens the
        // body is on its way out and cannot be asked anything.
        //
        // The corpse is deliberately NOT reused. DeathState.Enter calls Object.Destroy on
        // the owner the instant the FSM reaches it, and a Destroy cannot be cancelled -- the
        // object survives the rest of the frame and is gone at the end of it, so anything
        // built on top of that body would vanish a few milliseconds later with nothing in
        // the console to say why. Spawning a FRESH creature from the same definition sidesteps
        // the whole race, and it is also the honest reading of the spell: what rises is a
        // copy of what died, not the same meat animated.
        private MonsterDefinition _capturedDefinition;
        private Vector3 _capturedPosition;

        public override StatusEffectKind Kind => StatusEffectKind.Marked;

        public ThrallMarkEffect(float duration, float thrallDuration = 18f, GameObject applier = null)
            : base(duration, applier)
        {
            _thrallDuration = Mathf.Max(1f, thrallDuration);
        }

        public override void OnApply(StatusEffectManager target)
        {
            _owner = target;
            _health = target.GetComponent<Health>();
            if (_health == null) return;

            // Refuse a boss outright, and say so. Handing the player a boss as a pet is not a
            // tuning problem — the phase choreography, the music and the health pool are all
            // balanced against the whole fight.
            if (target.GetComponent<BossPhaseController>() != null)
            {
                Debug.Log($"[ThrallMark] '{target.name}' is a boss and cannot be raised.");
                return;
            }

            // Subscribing to the death event is the entire mechanism. C# snapshots an event's
            // invocation list when it is raised, so this handler still runs even if something
            // else clears the target's status effects from inside its own OnDeath handler.
            var brain = target.GetComponent<Valkur.Gameplay.FSM.FSMMonsterBrain>();
            _capturedDefinition = brain != null ? brain.Definition : null;
            if (_capturedDefinition == null)
            {
                // Nothing to raise: the target is not a definition-driven monster (a
                // hand-placed prop, a test double). Say so rather than marking it and letting
                // the player discover on the kill that the cast did nothing.
                Debug.Log($"[ThrallMark] '{target.name}' has no MonsterDefinition, so it " +
                          "cannot be raised. The mark was not applied.");
                return;
            }

            _deathHandler = OnBearerDied;
            _health.OnDeath += _deathHandler;

            ThrallMarkFX.Attach(target.gameObject, this);
        }

        public override void Tick(StatusEffectManager target)
        {
            // Nothing per frame. The mark is a subscription, not a poll — and the rig that
            // draws it reads the bearer's HP fraction itself.
        }

        public override void OnRemove(StatusEffectManager target)
        {
            if (_health != null && _deathHandler != null)
                _health.OnDeath -= _deathHandler;
            _deathHandler = null;
        }

        /// <summary>Bearer's remaining health as a fraction. Read by the rig, which TIGHTENS
        /// as this falls — the one coupling that turns the mark from decoration into
        /// information about whether the bet is about to pay.</summary>
        public float BearerHealthFraction
        {
            get
            {
                if (_health == null || _health.MaxHp <= 0) return 1f;
                return Mathf.Clamp01(_health.CurrentHp / (float)_health.MaxHp);
            }
        }

        /// <summary>True once the mark has cashed in. The rig stops drawing the sigil then:
        /// the raising has its own, much bigger, beat.</summary>
        public bool Consumed => _consumed;

        private void OnBearerDied()
        {
            if (_consumed || _capturedDefinition == null) return;
            _consumed = true;

            if (_owner != null) _capturedPosition = _owner.transform.position;

            ThrallRaiseFX.Play(_capturedDefinition, _capturedPosition,
                               _thrallDuration, THRALL_HEALTH_SCALE);
        }
    }
}
