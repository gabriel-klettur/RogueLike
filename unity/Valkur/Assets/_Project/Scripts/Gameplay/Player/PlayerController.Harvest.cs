using UnityEngine;

namespace Valkur.Gameplay
{
    /// <summary>
    /// Working animation: the swing a harvest session drives, once per blow.
    /// </summary>
    public partial class PlayerController
    {
        /// <summary>
        /// Play one work swing — a chop, a pick strike, a pull — facing
        /// <paramref name="towards"/>.
        ///
        /// <para>It goes through the controller rather than the harvest node writing the
        /// animator directly, because locomotion reverts any state it does not recognise on
        /// the very next frame. A pose set from outside would be overwritten before it
        /// rendered, which is invisible in code and reads on screen as the character standing
        /// perfectly still while the rock loses charges.</para>
        ///
        /// <para>The direction is taken from the NODE, not from the cursor. A shift is
        /// stationary and the player is free to look wherever they like during it; swinging
        /// away from the thing being worked is the one reading that is definitely wrong.
        /// <see cref="_facingDirection"/> itself is deliberately left alone, so the cursor
        /// still owns facing the moment the session ends.</para>
        ///
        /// <para><c>RestartCurrentState</c> is not optional. <c>SetState</c> early-returns
        /// when neither state, direction nor variant changed, so a character with a single
        /// attack animation would swing once and then stand in the last frame of it for the
        /// rest of the shift — the same trap a same-direction re-swing hits in combat.</para>
        /// </summary>
        /// <summary>
        /// How far the swing's playback speed may be bent to fit the blow interval. A
        /// pathologically small <c>secondsPerBlow</c> would otherwise drive the multiplier
        /// somewhere silly, and an animation running eight times too fast reads as a glitch
        /// rather than as a fast worker.
        /// </summary>
        private const float MIN_WORK_SWING_SPEED = 0.35f;
        private const float MAX_WORK_SWING_SPEED = 4f;

        public void PlayWorkSwing(Vector2 towards, string animationKey = null,
                                  float blowSeconds = 0f)
        {
            if (_animator == null) return;

            var aim = towards.sqrMagnitude > 0.0001f ? towards : _facingDirection;
            var dir = _animator.ResolveDirectionFromVector(aim);
            const DirectionalAnimator.AnimState state = DirectionalAnimator.AnimState.Attack;

            // The animation is DATA: DestructionProfile.swingAnimationKey names it, so a tree
            // asks for "harvest_chop" and a seam for "harvest_mine" without this method
            // knowing which is which, and a third kind of node needs no code at all.
            //
            // The fallback is load-bearing in BOTH directions. An empty key, or a key this
            // character has no art for, must land on the ordinary rotation rather than on
            // nothing: only the dwarf has chop and mine sheets, and the elf and barbarian have
            // to keep swinging at the tree instead of standing still while it loses hit
            // points. And a reserved variant leaves NextVariant's pool, so a character that
            // DOES have the art can never have it borrowed by an unrelated action -- two
            // different guarantees, both needed, and the second is what the reservation in
            // build_player_frames buys.
            int variant = -1;
            if (!string.IsNullOrEmpty(animationKey))
                variant = _animator.VariantForSpell(state, animationKey);

            // Fit ONE full cycle into ONE blow. Without this the animation is simply cut:
            // measured live on the dwarf's eight-frame chop, a 1.2 s cycle restarted every
            // 0.6 s played frames 1 2 3 4 5, 1 2 3 4 5, and never once reached 6 or 7 — the
            // two deepest strike frames, which is exactly the half of the swing that reads as
            // hitting something. It is the third sighting of this shape: AttackState cut an
            // eight-frame swing at frame four, and TriggerCastAnimation's flat 0.35 s window
            // did it to casts.
            //
            // DERIVED rather than authored, and that is the point. Writing 2.0 into the chop
            // variant's animationSpeedMultiplier would fix today's numbers and silently break
            // the moment anyone retunes secondsPerBlow — a constant overriding the field a
            // designer would reach for, which is the trap this feature has already recorded
            // twice. Deriving it means any tool, any character and any retune is right for
            // free, including characters whose art has a different frame count.
            //
            // Only for a variant the key RESOLVED to. A fallback variant came out of
            // NextVariant's pool and is shared, so re-pacing it would leave an unrelated punch
            // playing at whatever speed the last chop needed.
            if (variant >= 0 && blowSeconds > 0f)
            {
                float cycle = _animator.GetStateLength(state, variant);
                float paced = _animator.PacingOf(state, variant).SpeedMultiplier;
                if (cycle > 0f && paced > 0f)
                {
                    float wanted = Mathf.Clamp(paced * cycle / blowSeconds,
                        MIN_WORK_SWING_SPEED, MAX_WORK_SWING_SPEED);
                    _animator.TrySetVariantSpeed(state, variant, wanted);
                }
            }

            // Rotating rather than randomising, for the reason PlayerController.NextVariant
            // already records: a random pick repeats the same swing back to back about one
            // time in N and reads as the animation having failed to change.
            if (variant < 0) variant = NextVariant(state);

            _animator.SetState(state, dir, variant);
            _animator.RestartCurrentState();

            _castAnimSpellKey = null;
            _castAnimState = state;

            // Shares the cast timer on purpose: it is the one deadline TickCastAnimRevert
            // already checks every frame, so a swing cannot outlive its own animation even if
            // the session that started it is cancelled mid-blow. Measured AFTER SetState has
            // turned the animator, because GetStateLength reports the frame count of the
            // CURRENT direction.
            _castAnimEndTime = Time.time + Mathf.Max(0.05f, _animator.GetStateLength(state, variant));
        }
    }
}
