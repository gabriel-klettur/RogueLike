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
            Death
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

        // Alternative attack animations. Deliberately NOT seven more serialized fields:
        // see EntityAssetConfig.AttackVariant for why the vocabulary lives in data.
        private DirectionalSpriteSet[] _attackVariants;
        private int _activeAttackVariant = -1;

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
        public bool PrefersCardinalDirectionSampling => _preferCardinalDirectionSampling;

        /// <summary>How many alternative attack animations this entity carries. 0 = one attack.</summary>
        public int AttackVariantCount => _attackVariants?.Length ?? 0;

        /// <summary>Variant currently selected under <see cref="AnimState.Attack"/>; -1 = the base set.</summary>
        public int ActiveAttackVariant => _activeAttackVariant;

        /// <summary>
        /// One variant's sprite set, or the empty set when the index is out of range.
        /// Read-only, in the same spirit as <see cref="AttackSprites"/>: it lets a caller
        /// name the frames a variant should be showing without reaching into the array.
        /// </summary>
        public DirectionalSpriteSet AttackVariantSet(int index)
            => _attackVariants != null && index >= 0 && index < _attackVariants.Length
                ? _attackVariants[index]
                : default;

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
        {
            if (variants == null || variants.Count == 0)
            {
                _attackVariants = null;
                _activeAttackVariant = -1;
                return;
            }

            _attackVariants = new DirectionalSpriteSet[variants.Count];
            for (int i = 0; i < variants.Count; i++)
                _attackVariants[i] = variants[i];
            _activeAttackVariant = -1;
        }

        /// <summary>
        /// How long one full pass of a state's animation takes, in seconds.
        ///
        /// Exists because <c>AttackState</c> hardcodes its swing at windup + 0.3 s while
        /// every frame runs for <see cref="frameInterval"/> — an eight-frame swing needs
        /// 1.2 s and was being cut at frame four, mid-arc. Returns 0 when the state has
        /// no frames, so a caller can take the larger of the two and never SHORTEN a
        /// swing that the rest of the bestiary depends on.
        /// </summary>
        public float GetStateLength(AnimState state, int attackVariant = -1)
        {
            Sprite[] frames = ResolveFrames(state, _currentDirection, attackVariant);
            return frames == null || frames.Length == 0 ? 0f : frames.Length * frameInterval;
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
            if (_frameTimer < frameInterval) return;
            _frameTimer -= frameInterval;

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
            => SetState(state, direction, _activeAttackVariant);

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
            bool variantChanged = state == AnimState.Attack && attackVariant != _activeAttackVariant;

            _activeAttackVariant = attackVariant;

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
            var spriteSet = GetSpriteSet(_currentState, _activeAttackVariant);
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
