using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay
{
    /// <summary>
    /// 8-directional sprite animator matching Python's Animator component.
    /// Handles direction resolution from vectors, frame cycling with interval,
    /// idle hold behavior, and walk skip-first-frame behavior.
    /// </summary>
    public partial class DirectionalAnimator : MonoBehaviour
    {
        /// <summary>
        /// The 8 cardinal/intercardinal directions matching Python's naming.
        /// </summary>
        public enum Direction
        {
            South,      // "down" / "s"
            SouthEast,  // "down_right" / "se"
            East,       // "right" / "e"
            NorthEast,  // "up_right" / "ne"
            North,      // "up" / "n"
            NorthWest,  // "up_left" / "nw"
            West,       // "left" / "w"
            SouthWest   // "down_left" / "sw"
        }

        /// <summary>
        /// Animation state matching Python's FSM-driven animation states.
        /// </summary>
        public enum AnimState
        {
            Idle,
            Walk,
            Chase,
            Cast,
            Attack,
            Damage,
            Death,

            /// <summary>
            /// Getting back up. Entered ONLY by <c>DeathSequenceController.ReviveRoutine</c>,
            /// which owns both the entry and the exit.
            ///
            /// This is the eighth value, and CLAUDE.md warns at length that adding one is how
            /// you get a state the player enters and never leaves — <c>PlayerController
            /// .Movement</c> overrides locomotion on an Idle/Walk/Chase whitelist and reverts
            /// on a Cast/Attack one, so a value missing from the second list is a soft lock.
            /// Recover is in that revert list, and unlike Cast/Attack it also carries a hard
            /// deadline in <c>TickCastAnimRevert</c>, because the system that entered it is a
            /// coroutine that a scene change can kill mid-flight.
            ///
            /// It exists rather than being folded into Damage because the two read as opposite
            /// things: Damage is a flinch that interrupts, Recover is a rise that resolves.
            /// </summary>
            Recover
        }

        [Serializable]
        public struct DirectionalSpriteSet
        {
            public Sprite[] south;
            public Sprite[] southEast;
            public Sprite[] east;
            public Sprite[] northEast;
            public Sprite[] north;
            public Sprite[] northWest;
            public Sprite[] west;
            public Sprite[] southWest;

            public Sprite[] GetFrames(Direction dir)
            {
                return dir switch
                {
                    Direction.South => south,
                    Direction.SouthEast => southEast,
                    Direction.East => east,
                    Direction.NorthEast => northEast,
                    Direction.North => north,
                    Direction.NorthWest => northWest,
                    Direction.West => west,
                    Direction.SouthWest => southWest,
                    _ => south
                };
            }
        }

        [Header("Animation Sets")]
        [SerializeField] private DirectionalSpriteSet idleSprites;
        [SerializeField] private DirectionalSpriteSet walkSprites;
        [SerializeField] private DirectionalSpriteSet chaseSprites;
        [SerializeField] private DirectionalSpriteSet castSprites;
        [SerializeField] private DirectionalSpriteSet attackSprites;
        [SerializeField] private DirectionalSpriteSet damageSprites;
        [SerializeField] private DirectionalSpriteSet deathSprites;
        // Installed by SetRecoverSprites rather than by the seven-argument SetSpriteSets,
        // so every existing caller of that method keeps compiling untouched.
        [SerializeField] private DirectionalSpriteSet recoverSprites;

        [Header("Timing")]
        [SerializeField] private float frameInterval = 0.15f;
        [SerializeField] private float idleHoldTime = 1.0f;

        [Header("References")]
        [SerializeField] private SpriteRenderer targetRenderer;

        private AnimState _currentState = AnimState.Idle;
        private Direction _currentDirection = Direction.South;
        private int _frameIndex;
        private float _frameTimer;
        private float _stateStartTime;
        private AnimState _prevState;
        private Direction _prevDirection;
        private bool _preferCardinalDirectionSampling;

        // Per-entity animation speed. Kept OFF the serialized frameInterval field so its
        // authored 0.15s value (identical across the whole bestiary today) stays the
        // single source of truth; this is a runtime multiplier applied on top of it. See
        // EntityAssetConfig.AnimationScaleConfig.animationSpeedMultiplier for the authoring
        // side and SetAnimationSpeedMultiplier below for the <=0 "unset" sentinel.
        private const float MinAnimationSpeedMultiplier = 0.01f;
        private float _animationSpeedMultiplier = 1f;

        // Alternative animations, per state. Deliberately NOT a serialized field per state
        // per variant: see EntityAssetConfig.AttackVariant for why the vocabulary lives in
        // data rather than in the enum.
        //
        // This started as an Attack-only array. It is indexed by AnimState now because the
        // elven character ships three spellcasting animations, and a second parallel
        // cast-only array would have paid the same positional tax a second time — the tax
        // AttackVariant's own doc-comment exists to complain about. A state with no entry
        // resolves to its single set exactly as before.
        private DirectionalSpriteSet[][] _variantsByState;
        private int _activeVariant = -1;

        public AnimState CurrentState => _currentState;
        public Direction CurrentDirection => _currentDirection;

        // Read-only accessors — used by the Spells Editor View panel to clone the
        // player's sprite sets onto the synthetic preview character so the user
        // sees the actual cast pose for the selected direction.
        public DirectionalSpriteSet IdleSprites   => idleSprites;
        public DirectionalSpriteSet WalkSprites   => walkSprites;
        public DirectionalSpriteSet ChaseSprites  => chaseSprites;
        public DirectionalSpriteSet CastSprites   => castSprites;
        public DirectionalSpriteSet AttackSprites => attackSprites;
        public DirectionalSpriteSet DamageSprites => damageSprites;
        public DirectionalSpriteSet DeathSprites  => deathSprites;
        public DirectionalSpriteSet RecoverSprites => recoverSprites;
        public bool PrefersCardinalDirectionSampling => _preferCardinalDirectionSampling;

        /// <summary>How many alternative animations this entity carries for one state. 0 = one.</summary>
        public int VariantCount(AnimState state)
        {
            DirectionalSpriteSet[] variants = VariantsFor(state);
            return variants?.Length ?? 0;
        }

        /// <summary>How many alternative attack animations this entity carries. 0 = one attack.</summary>
        public int AttackVariantCount => VariantCount(AnimState.Attack);

        /// <summary>Variant currently selected for the active state; -1 = the base set.</summary>
        public int ActiveVariant => _activeVariant;

        /// <summary>Kept for the monster path, which only ever varies its attack.</summary>
        public int ActiveAttackVariant => _activeVariant;

        /// <summary>
        /// One variant's sprite set, or the empty set when the index is out of range.
        /// Read-only, in the same spirit as <see cref="AttackSprites"/>: it lets a caller
        /// name the frames a variant should be showing without reaching into the array.
        /// </summary>
        public DirectionalSpriteSet VariantSet(AnimState state, int index)
        {
            DirectionalSpriteSet[] variants = VariantsFor(state);
            return variants != null && index >= 0 && index < variants.Length
                ? variants[index]
                : default;
        }

        public DirectionalSpriteSet AttackVariantSet(int index)
            => VariantSet(AnimState.Attack, index);

        private DirectionalSpriteSet[] VariantsFor(AnimState state)
        {
            int i = (int)state;
            return _variantsByState != null && i >= 0 && i < _variantsByState.Length
                ? _variantsByState[i]
                : null;
        }

        /// <summary>
        /// Runtime assignment API for data-driven character definitions.
        /// </summary>
        public void SetSpriteSets(
            DirectionalSpriteSet idle,
            DirectionalSpriteSet walk,
            DirectionalSpriteSet chase,
            DirectionalSpriteSet cast,
            DirectionalSpriteSet attack,
            DirectionalSpriteSet damage,
            DirectionalSpriteSet death,
            bool preferCardinalDirectionSampling = false)
        {
            idleSprites = idle;
            walkSprites = walk;
            chaseSprites = chase;
            castSprites = cast;
            attackSprites = attack;
            damageSprites = damage;
            deathSprites = death;
            _preferCardinalDirectionSampling = preferCardinalDirectionSampling;

            _frameIndex = 0;
            _frameTimer = 0f;
            _stateStartTime = Time.time;
        }

        /// <summary>
        /// Installs the alternative attack animations. Additive to
        /// <see cref="SetSpriteSets"/> on purpose — every existing caller of that
        /// seven-argument method keeps compiling and behaving identically, and an entity
        /// that never calls this one resolves Attack to its single attack set as before.
        /// </summary>
        public void SetAttackVariants(IReadOnlyList<DirectionalSpriteSet> variants)
            => SetVariants(AnimState.Attack, variants);

        /// <summary>
        /// Installs the alternative animations for one state. Additive to
        /// <see cref="SetSpriteSets"/> on purpose — every existing caller of that
        /// seven-argument method keeps compiling and behaving identically, and a state that
        /// never gets variants resolves to its single set as before.
        /// </summary>
        public void SetVariants(AnimState state, IReadOnlyList<DirectionalSpriteSet> variants)
        {
            int stateCount = Enum.GetValues(typeof(AnimState)).Length;
            if (_variantsByState == null || _variantsByState.Length != stateCount)
                _variantsByState = new DirectionalSpriteSet[stateCount][];

            int index = (int)state;
            if (index < 0 || index >= stateCount)
                return;

            if (variants == null || variants.Count == 0)
            {
                _variantsByState[index] = null;
            }
            else
            {
                var copy = new DirectionalSpriteSet[variants.Count];
                for (int i = 0; i < variants.Count; i++)
                    copy[i] = variants[i];
                _variantsByState[index] = copy;
            }

            _activeVariant = -1;
        }

        /// <summary>
        /// Installs the "getting back up" set. Separate from <see cref="SetSpriteSets"/> for
        /// the same reason <see cref="SetAttackVariants"/> is: widening that method to eight
        /// arguments would break every existing caller for a state most entities do not have.
        /// </summary>
        public void SetRecoverSprites(DirectionalSpriteSet recover)
        {
            recoverSprites = recover;
        }

        /// <summary>
        /// Per-entity playback speed set from <c>EntityAssetConfig.AnimationScaleConfig
        /// .animationSpeedMultiplier</c> via <c>EntityAnimationBinder.ApplyVisuals</c>.
        ///
        /// A value &lt;= 0 collapses to 1 (identity, i.e. today's flat 0.15s/frame for
        /// every monster). That is deliberate, not just a clamp: a struct field with no
        /// matching key in an asset serialized before this field existed deserializes to
        /// its CLR default, 0 — not the C# line's absent initializer — so every shipped
        /// monster keeps its exact current timing until an author sets this explicitly.
        /// </summary>
        public void SetAnimationSpeedMultiplier(float multiplier)
        {
            _animationSpeedMultiplier = multiplier <= 0f
                ? 1f
                : Mathf.Max(MinAnimationSpeedMultiplier, multiplier);
        }

        /// <summary>Read-only, for tests and inspection — the value <see cref="Update"/> and <see cref="GetStateLength"/> actually run at.</summary>
        public float AnimationSpeedMultiplier => _animationSpeedMultiplier;

        /// <summary>
        /// The authored <see cref="frameInterval"/> divided by <see cref="_animationSpeedMultiplier"/>.
        /// Every reader of the per-frame timing (the Update loop and GetStateLength) goes
        /// through this single accessor so the two can never disagree about how fast a
        /// state is actually playing.
        /// </summary>
        private float EffectiveFrameInterval => frameInterval / _animationSpeedMultiplier;

        /// <summary>
        /// How long one full pass of a state's animation takes, in seconds, AT THIS
        /// ENTITY'S current animation speed.
        ///
        /// Exists because <c>AttackState</c> hardcodes its swing at windup + 0.3 s while
        /// every frame runs for <see cref="EffectiveFrameInterval"/> — an eight-frame swing
        /// needs 1.2 s at the default speed and was being cut at frame four, mid-arc.
        /// Returns 0 when the state has no frames, so a caller can take the larger of the
        /// two and never SHORTEN a swing that the rest of the bestiary depends on.
        /// </summary>
        public float GetStateLength(AnimState state, int attackVariant = -1)
        {
            Sprite[] frames = ResolveFrames(state, _currentDirection, attackVariant);
            return frames == null || frames.Length == 0 ? 0f : frames.Length * EffectiveFrameInterval;
        }

        /// <summary>
        /// Replays the current state from frame 0 without changing state or direction.
        ///
        /// <see cref="SetState"/> early-returns when neither changed, and
        /// <c>AttackState</c>'s re-swing path resets only its own timers — so back-to-back
        /// swings at a player who never leaves melee range ride one free-running sprite
        /// loop, and the second swing starts wherever the first happened to be.
        /// </summary>
        public void RestartCurrentState()
        {
            _frameIndex = 0;
            _frameTimer = 0f;
            _stateStartTime = Time.time;
            AdvanceFrame();
        }

        private void Awake()
        {
            if (targetRenderer == null)
                targetRenderer = GetComponentInChildren<SpriteRenderer>();
            _prevState = _currentState;
            _prevDirection = _currentDirection;
            _stateStartTime = Time.time;
        }

        private void Update()
        {
            _frameTimer += Time.deltaTime;
            float interval = EffectiveFrameInterval;
            if (_frameTimer < interval) return;
            _frameTimer -= interval;

            AdvanceFrame();
        }

        /// <summary>
        /// Set animation state and direction.
        /// Resets frame counter only on state change — direction changes preserve the
        /// current frame index so walk/idle animations don't stutter when the mouse
        /// crosses an 8-direction sector boundary.
        /// Maps to Python's set_mapped_anim / Animator.current_state assignment.
        /// </summary>
        public void SetState(AnimState state, Direction direction)
            => SetState(state, direction, _activeVariant);

        /// <summary>
        /// Same, choosing which attack animation plays. <paramref name="attackVariant"/> is
        /// an index into <see cref="SetAttackVariants"/>; -1 (or an out-of-range value)
        /// falls back to the single attack set.
        ///
        /// A CHANGED VARIANT counts as a state change. Without that the guard below sees
        /// Attack-to-Attack in the same direction, returns early, and the second kick
        /// silently keeps playing the first one's frames.
        /// </summary>
        public void SetState(AnimState state, Direction direction, int attackVariant)
        {
            bool stateChanged = state != _currentState;
            bool directionChanged = direction != _currentDirection;
            // Any state that carries variants, not just Attack: the elven character casts
            // three different ways, and without this a second cast in the same direction
            // with a different animation returns early and keeps playing the first one.
            bool variantChanged = VariantsFor(state) != null && attackVariant != _activeVariant;

            _activeVariant = attackVariant;

            if (!stateChanged && !directionChanged && !variantChanged)
                return;

            if (variantChanged && !stateChanged)
            {
                // Same state, different animation: the frame cursor has to go back to 0 or
                // the new variant starts mid-cycle. Handled here because the block below
                // only resets on a state change.
                _currentDirection = direction;
                _prevDirection = direction;
                _frameIndex = 0;
                _frameTimer = 0f;
                _stateStartTime = Time.time;
                AdvanceFrame();
                return;
            }

            _currentDirection = direction;
            _prevDirection = direction;

            if (stateChanged)
            {
                _currentState = state;
                _prevState = state;
                _frameIndex = 0;
                _frameTimer = 0f;
                _stateStartTime = Time.time;
                // Apply immediately so the new state is visible without frame-interval lag.
                AdvanceFrame();
            }
            else
            {
                // Direction-only change: show same frame index in new direction without
                // resetting the counter, preventing walk animation stutter.
                RefreshCurrentFrame();
            }
        }

        /// <summary>
        /// Applies the current frame from the new direction's sprite set without
        /// advancing <see cref="_frameIndex"/>. Called on direction-only changes.
        /// </summary>
        private void RefreshCurrentFrame()
        {
            // The active variant, not the default -1: this runs on a direction-only change,
            // which during an attack means the player is strafing. Resolving the BASE attack
            // set here flashes one frame of the sword swing into the middle of a kick every
            // time the facing sector changes, and the next AdvanceFrame tick hides it again.
            var spriteSet = GetSpriteSet(_currentState, _activeVariant);
            Sprite[] frames = spriteSet.GetFrames(_currentDirection);

            if (frames == null || frames.Length == 0)
            {
                // Mirror AdvanceFrame's death fallback so a direction-only change while
                // dead renders the corpse pose, not the idle pose.
                if (_currentState == AnimState.Death)
                    frames = FindFirstNonEmptyDirection(spriteSet);

                if (frames == null || frames.Length == 0)
                {
                    frames = idleSprites.GetFrames(_currentDirection);
                    if (frames == null || frames.Length == 0) return;
                }
            }

            if (frames.Length == 1)
            {
                ApplyFrame(frames[0]);
                return;
            }

            int idx = Mathf.Clamp(_frameIndex, 0, frames.Length - 1);
            // Walk/Chase skip frame 0 (standing pose).
            if ((_currentState == AnimState.Walk || _currentState == AnimState.Chase) && idx < 1)
                idx = 1;
            ApplyFrame(frames[idx]);
        }

        /// <summary>
        /// Set direction from a movement/facing vector.
        /// Maps to Python's get_direction_name(dx, dy) using atan2.
        /// </summary>
        public void SetDirectionFromVector(Vector2 dir)
        {
            if (dir.sqrMagnitude < 0.01f) return;
            _currentDirection = ResolveDirectionFromVector(dir);
        }

        public Direction ResolveDirectionFromVector(Vector2 dir)
        {
            if (dir.sqrMagnitude < 0.01f)
                return _currentDirection;

            return _preferCardinalDirectionSampling
                ? VectorToPrimaryDirection(dir)
                : VectorToDirection(dir);
        }
    }
}
