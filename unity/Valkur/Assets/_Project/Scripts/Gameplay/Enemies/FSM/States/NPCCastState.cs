using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Spells;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// The frames an NPC spends committed to a spell: rooted, facing its target, playing the
    /// cast pose. It ends when the SPELL ACTION ends — not when the ability comes off
    /// cooldown, which is a different thing entirely and used to be what this state waited for.
    ///
    /// <para><b>THE STATE USED TO LAST THE WHOLE COOLDOWN, AND THAT MADE EVERY CASTER A
    /// STATUE.</b> The exit test was <c>CurrentPhase == Ready</c>, and <see cref="SpellCaster"/>
    /// reaches <c>Ready</c> only after <c>prepare + channel + cooldownDuration</c> — so the
    /// immobility was the spell's entire rate limit. Measured on the shipped catalogue that is
    /// 4 s for <c>void_lance</c>, 11 s for <c>thunderclap</c>, 12 s for <c>leap_slam</c> and
    /// <b>20 s for <c>war_cry</c></b>; measured live with 31 monsters on the field, 9 of them
    /// were in this state at velocity 0.00, the worst 10.3 s and still counting, three of them
    /// frozen simultaneously mid-<c>leap_slam</c> — a spell whose entire point is that it
    /// moves the caster.</para>
    ///
    /// <para>It survived for the life of the project because it was latent in the DATA rather
    /// than in the code: <c>barbol_cyan</c> was the only autocasting monster ever shipped and
    /// it casts one spell, <c>iceball</c>, whose whole chain is 1.7 s. The defect lived in the
    /// interaction between this state and a cooldown range no shipped asset occupied — the
    /// same shape as the spawner coordinate drift, where both halves were internally
    /// consistent and only the composition was wrong.</para>
    ///
    /// <para><b>A COOLDOWN IS A RATE LIMIT ON THE ABILITY, NOT A DURATION FOR THE CASTER.</b>
    /// The action is over when the caster leaves <c>Prepare</c> and <c>Channel</c>; everything
    /// after that is the slot recharging, and the monster should be walking during it.
    /// <see cref="NPCAutoCast"/> owns the re-arm and now derives its period from the spell's own
    /// cooldown, so the two clocks finally agree.</para>
    ///
    /// <para><b>The pose has a FLOOR, and that floor is the whole reason the state still
    /// exists for an instant spell.</b> Half the shipped hostile spells author
    /// <c>prepare: 0, channel: 0</c>, so the action is over one or two frames after it starts —
    /// exiting on that would mean a monster whose spells appear out of nowhere while it walks,
    /// which is the "a cast pose is not a cast" problem this project already records for the
    /// player. The floor is taken from the entity's own cast ANIMATION where it has one
    /// (<see cref="DirectionalAnimator.GetStateLength"/>, the same source
    /// <c>AttackState</c> sizes its swing from), clamped so a long sheet cannot re-introduce a
    /// multi-second freeze.</para>
    ///
    /// <para>Death / damage interrupts are handled by the standard FSM event queue in
    /// <see cref="StateMachine"/>; <see cref="DamageState"/> and
    /// <see cref="UnconsciousState"/> always preempt.</para>
    /// </summary>
    public class NPCCastState : IState
    {
        private SpellCaster _caster;

        /// <summary>
        /// Hard cap, for a caster whose phase machine never returns — a channelled spell whose
        /// controller was destroyed, say. Deliberately far above any legitimate cast pose:
        /// this is a deadlock breaker, not a duration.
        /// </summary>
        private const float MaxStateDuration = 8f;

        /// <summary>
        /// Shortest a cast pose may be. Below this the spell reads as having happened while the
        /// monster walked — no wind-up, no commitment, no frame the player could have reacted to.
        /// </summary>
        private const float MinPoseSeconds = 0.30f;

        /// <summary>
        /// Longest the ANIMATION alone may hold the monster still. A creature with an eight-frame
        /// cast sheet would otherwise be rooted for 1.2 s per instant spell, which is the old
        /// defect back in a smaller size. The spell's own prepare/channel is not capped by
        /// this — an authored channel is a deliberate commitment.
        /// </summary>
        private const float MaxPoseSeconds = 0.85f;

        private float _stateTimer;
        private float _poseSeconds;
        private int _variant = -1;

        public void Enter(StateMachine fsm)
        {
            _caster = fsm.Owner.GetComponent<SpellCaster>();
            _stateTimer = 0f;

            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);

            // Rooted for the pose. Python applied a StunComponent for the channel; zeroing the
            // velocity here and writing none in Execute is the same thing with no status effect
            // for another system to trip over.
            c?.StopMovement();

            _variant = ResolveCastVariant(c);
            _poseSeconds = ResolvePoseSeconds(c);

            FaceTarget(c, fsm);
        }

        public void Execute(StateMachine fsm, float dt)
        {
            var c = fsm.GetContext<FSMComponents>(FSMComponents.KEY);
            if (c?.Health != null && c.Health.IsDead)
            {
                fsm.ChangeState(new UnconsciousState());
                return;
            }

            _stateTimer += dt;

            // Hold velocity at zero in case some other system nudged it.
            c?.StopMovement();

            if (_stateTimer >= MaxStateDuration || IsDone())
            {
                ReturnToHostileState(fsm);
            }
        }

        public void Exit(StateMachine fsm) { }

        /// <summary>
        /// True when the monster has nothing left to commit to: the spell's own action has
        /// finished AND the pose has been on screen long enough to read.
        ///
        /// <para>A null caster is done immediately — the state is only ever entered by
        /// <see cref="NPCAutoCast"/>, which requires one, so this is the defensive branch and
        /// standing still would be the wrong answer to it.</para>
        /// </summary>
        private bool IsDone()
        {
            if (_caster == null) return true;
            if (IsActing(_caster.CurrentPhase)) return false;
            return _stateTimer >= _poseSeconds;
        }

        /// <summary>
        /// The two phases in which the spell is still HAPPENING. <c>Cooldown</c> is
        /// deliberately absent: it is the slot recharging, and a monster standing through it is
        /// the defect this class's remarks are about.
        /// </summary>
        private static bool IsActing(SpellCaster.CastPhase phase)
            => phase == SpellCaster.CastPhase.Prepare || phase == SpellCaster.CastPhase.Channel;

        /// <summary>
        /// How long the pose is held when the spell itself is instant. Read off the entity's
        /// own cast animation so a creature with a real cast sheet gets the wind-up its art
        /// draws, and clamped at both ends so neither a missing sheet nor a long one decides
        /// how long a monster is helpless.
        /// </summary>
        private float ResolvePoseSeconds(FSMComponents c)
        {
            if (c?.Animator == null) return MinPoseSeconds;

            float animLength = c.Animator.GetStateLength(DirectionalAnimator.AnimState.Cast, _variant);
            return Mathf.Clamp(animLength, MinPoseSeconds, MaxPoseSeconds);
        }

        /// <summary>
        /// The animation this SPELL reserves, if the entity draws one.
        ///
        /// <para>Monsters never used to pick a cast variant at all: both this state and
        /// <c>FSMMonsterBrain.OnFSMStateChanged</c> called the two-argument
        /// <c>SetState(state, direction)</c>, which REUSES the active variant index — and on a
        /// monster that index is never set, so it is -1 forever. The Dark roster inherited 3 to
        /// 9 cast animations each from the player classes it wears, and every one of them was
        /// unreachable: authored, round-tripped and inert, which is the shape this project
        /// keeps finding. Resolving it here mirrors
        /// <c>PlayerController.ResolveCastVariant</c> exactly.</para>
        /// </summary>
        private int ResolveCastVariant(FSMComponents c)
        {
            if (c?.Animator == null || _caster == null) return -1;

            var spell = _caster.GetSpellAtSlot(_caster.ActiveSlot);
            if (spell == null || string.IsNullOrEmpty(spell.spellKey)) return -1;

            return c.Animator.VariantForSpell(DirectionalAnimator.AnimState.Cast, spell.spellKey);
        }

        private void FaceTarget(FSMComponents c, StateMachine fsm)
        {
            if (c?.Animator == null) return;

            var target = FactionTargeting.EnemyOf(fsm.Owner);
            if (target == null) return;

            Vector2 toTarget = (Vector2)target.transform.position - (Vector2)fsm.Owner.transform.position;
            if (toTarget.sqrMagnitude < 0.0001f) return;

            var dir = c.Animator.ResolveDirectionFromVector(toTarget);
            c.Animator.SetState(DirectionalAnimator.AnimState.Cast, dir, _variant);
        }

        /// <summary>
        /// Back to fighting: Attack when the target is already inside reach, Chase otherwise.
        /// Idle and Patrol are not options — the monster was aggressive enough to cast, and
        /// dropping to a resting state would throw that away.
        /// </summary>
        private static void ReturnToHostileState(StateMachine fsm)
        {
            var target = FactionTargeting.EnemyOf(fsm.Owner);
            if (target == null)
            {
                fsm.ChangeState(new ChaseState());
                return;
            }

            float meleeRange = fsm.GetContextFloat("melee_range", 1.5f);
            float distSq = ((Vector2)target.transform.position -
                            (Vector2)fsm.Owner.transform.position).sqrMagnitude;

            if (distSq <= meleeRange * meleeRange)
                fsm.ChangeState(new AttackState());
            else
                fsm.ChangeState(new ChaseState());
        }
    }
}
