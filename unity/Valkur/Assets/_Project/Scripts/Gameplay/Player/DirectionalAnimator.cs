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

        public AnimState CurrentState => _currentState;
        public Direction CurrentDirection => _currentDirection;

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
        /// Set animation state and direction. Resets frame on state change.
        /// Maps to Python's set_mapped_anim / Animator.current_state assignment.
        /// </summary>
        public void SetState(AnimState state, Direction direction)
        {
            bool stateChanged = state != _currentState;
            bool directionChanged = direction != _currentDirection;

            if (!stateChanged && !directionChanged)
                return;

            _currentState = state;
            _currentDirection = direction;

            _frameIndex = 0;
            _frameTimer = 0f;
            _stateStartTime = Time.time;
            _prevState = _currentState;
            _prevDirection = _currentDirection;

            // Apply immediately so idle/walk direction follows mouse without frame-interval lag.
            AdvanceFrame();
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
