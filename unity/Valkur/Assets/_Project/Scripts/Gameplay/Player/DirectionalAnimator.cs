using System;
using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay
{
    /// <summary>
    /// 8-directional sprite animator matching Python's Animator component.
    /// Handles direction resolution from vectors, frame cycling with interval,
    /// idle hold behavior, and walk skip-first-frame behavior.
    /// </summary>
    public class DirectionalAnimator : MonoBehaviour
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

        public AnimState CurrentState => _currentState;
        public Direction CurrentDirection => _currentDirection;

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
            _currentState = state;
            _currentDirection = direction;

            if (stateChanged || direction != _prevDirection)
            {
                if (stateChanged)
                {
                    _frameIndex = 0;
                    _stateStartTime = Time.time;
                }
                _prevState = _currentState;
                _prevDirection = _currentDirection;
            }
        }

        /// <summary>
        /// Set direction from a movement/facing vector.
        /// Maps to Python's get_direction_name(dx, dy) using atan2.
        /// </summary>
        public void SetDirectionFromVector(Vector2 dir)
        {
            if (dir.sqrMagnitude < 0.01f) return;
            _currentDirection = VectorToDirection(dir);
        }

        /// <summary>
        /// Convert a 2D vector to one of 8 directions.
        /// Matches Python's angle-based direction resolution.
        /// </summary>
        public static Direction VectorToDirection(Vector2 dir)
        {
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            if (angle < 0) angle += 360f;

            // Map angle to 8 sectors (0° = East, 90° = North, etc.)
            if (angle < 22.5f || angle >= 337.5f) return Direction.East;
            if (angle < 67.5f) return Direction.NorthEast;
            if (angle < 112.5f) return Direction.North;
            if (angle < 157.5f) return Direction.NorthWest;
            if (angle < 202.5f) return Direction.West;
            if (angle < 247.5f) return Direction.SouthWest;
            if (angle < 292.5f) return Direction.South;
            return Direction.SouthEast;
        }

        private void AdvanceFrame()
        {
            var spriteSet = GetSpriteSet(_currentState);
            Sprite[] frames = spriteSet.GetFrames(_currentDirection);

            if (frames == null || frames.Length == 0)
            {
                // Fallback: try idle set for this direction
                frames = idleSprites.GetFrames(_currentDirection);
                if (frames == null || frames.Length == 0) return;
            }

            // Single frame — no animation needed
            if (frames.Length == 1)
            {
                ApplyFrame(frames[0]);
                return;
            }

            // Idle behavior: hold first frame for idleHoldTime, then loop 1..end
            // Matches Python's Animator.next_frame() idle logic
            if (_currentState == AnimState.Idle)
            {
                if (_frameIndex == 0)
                {
                    if (Time.time - _stateStartTime < idleHoldTime)
                    {
                        ApplyFrame(frames[0]);
                        return;
                    }
                    _frameIndex = 1;
                    _stateStartTime = Time.time;
                }
                ApplyFrame(frames[_frameIndex]);
                _frameIndex++;
                if (_frameIndex >= frames.Length)
                    _frameIndex = 1;
                return;
            }

            // Walk behavior: skip first frame, loop 1..end
            // Matches Python's Animator.next_frame() walk logic
            if (_currentState == AnimState.Walk || _currentState == AnimState.Chase)
            {
                if (_frameIndex < 1) _frameIndex = 1;
                ApplyFrame(frames[_frameIndex]);
                _frameIndex++;
                if (_frameIndex >= frames.Length)
                    _frameIndex = 1;
                return;
            }

            // Default: loop all frames
            _frameIndex %= frames.Length;
            ApplyFrame(frames[_frameIndex]);
            _frameIndex = (_frameIndex + 1) % frames.Length;
        }

        private DirectionalSpriteSet GetSpriteSet(AnimState state)
        {
            return state switch
            {
                AnimState.Idle => idleSprites,
                AnimState.Walk => walkSprites,
                AnimState.Chase => chaseSprites,
                AnimState.Cast => castSprites,
                AnimState.Attack => attackSprites,
                AnimState.Damage => damageSprites,
                AnimState.Death => deathSprites,
                _ => idleSprites
            };
        }

        private void ApplyFrame(Sprite sprite)
        {
            if (targetRenderer != null && sprite != null)
                targetRenderer.sprite = sprite;
        }
    }
}
