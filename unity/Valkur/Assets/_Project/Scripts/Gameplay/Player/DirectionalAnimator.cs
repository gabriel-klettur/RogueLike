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

        public Sprite PeekFirstFrame(DirectionalSpriteSet set)
        {
            Sprite[] frames = set.GetFrames(Direction.South);
            if (frames != null && frames.Length > 0 && frames[0] != null) return frames[0];

            frames = set.GetFrames(Direction.East);
            if (frames != null && frames.Length > 0 && frames[0] != null) return frames[0];

            frames = set.GetFrames(Direction.North);
            if (frames != null && frames.Length > 0 && frames[0] != null) return frames[0];

            frames = set.GetFrames(Direction.West);
            if (frames != null && frames.Length > 0 && frames[0] != null) return frames[0];

            return null;
        }

        public static DirectionalSpriteSet CreateSetFromDirectional(DirectionalSprites directional)
        {
            return new DirectionalSpriteSet
            {
                south = ToSingleFrameArray(directional.south),
                southEast = ToSingleFrameArray(directional.southEast),
                east = ToSingleFrameArray(directional.east),
                northEast = ToSingleFrameArray(directional.northEast),
                north = ToSingleFrameArray(directional.north),
                northWest = ToSingleFrameArray(directional.northWest),
                west = ToSingleFrameArray(directional.west),
                southWest = ToSingleFrameArray(directional.southWest)
            };
        }

        public static DirectionalSpriteSet CreateSetFromLinearFrames(IReadOnlyList<Sprite> frames, bool assumeFourDirectionalLayout = false)
        {
            if (frames == null || frames.Count == 0)
                return default;

            var clean = new List<Sprite>(frames.Count);
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i] != null)
                    clean.Add(frames[i]);
            }

            if (clean.Count == 0)
                return default;

            if (assumeFourDirectionalLayout && TryBuildFourDirectionalSet(clean, out var fourDirectionalSet))
                return fourDirectionalSet;

            return BuildEightDirectionalSet(clean);
        }

        private static bool TryBuildFourDirectionalSet(IReadOnlyList<Sprite> clean, out DirectionalSpriteSet set)
        {
            set = default;
            if (clean == null || clean.Count < 4 || clean.Count % 4 != 0)
                return false;

            int framesPerDirection = clean.Count / 4;
            if (framesPerDirection <= 0)
                return false;

            // Expected 4-dir sheet order used by imported player sheets: South, West, East, North.
            var south = SliceFrames(clean, 0 * framesPerDirection, framesPerDirection);
            var west = SliceFrames(clean, 1 * framesPerDirection, framesPerDirection);
            var east = SliceFrames(clean, 2 * framesPerDirection, framesPerDirection);
            var north = SliceFrames(clean, 3 * framesPerDirection, framesPerDirection);

            set = new DirectionalSpriteSet
            {
                south = south,
                southEast = east,
                east = east,
                northEast = east,
                north = north,
                northWest = west,
                west = west,
                southWest = west
            };

            return true;
        }

        private static DirectionalSpriteSet BuildEightDirectionalSet(List<Sprite> clean)
        {
            var buckets = new List<Sprite>[8];
            for (int i = 0; i < buckets.Length; i++)
                buckets[i] = new List<Sprite>();

            int perDirection = clean.Count / 8;
            if (perDirection > 0)
            {
                // Python parity: directional strips are contiguous by direction.
                // If an extra frame exists (e.g., 41 total), ignore trailing remainder frames.
                for (int dir = 0; dir < 8; dir++)
                {
                    int start = dir * perDirection;
                    for (int i = 0; i < perDirection; i++)
                        buckets[dir].Add(clean[start + i]);
                }
            }
            else
            {
                for (int i = 0; i < clean.Count; i++)
                {
                    int dirIndex = (int)Mathf.Floor((i * 8f) / clean.Count);
                    if (dirIndex < 0) dirIndex = 0;
                    if (dirIndex > 7) dirIndex = 7;
                    buckets[dirIndex].Add(clean[i]);
                }
            }

            var fallback = clean[0];
            for (int i = 0; i < buckets.Length; i++)
            {
                if (buckets[i].Count == 0)
                    buckets[i].Add(fallback);
            }

            return new DirectionalSpriteSet
            {
                south = buckets[0].ToArray(),
                southEast = buckets[1].ToArray(),
                east = buckets[2].ToArray(),
                northEast = buckets[3].ToArray(),
                north = buckets[4].ToArray(),
                northWest = buckets[5].ToArray(),
                west = buckets[6].ToArray(),
                southWest = buckets[7].ToArray()
            };
        }

        private static Sprite[] SliceFrames(IReadOnlyList<Sprite> frames, int startIndex, int count)
        {
            if (frames == null || count <= 0 || startIndex < 0 || startIndex >= frames.Count)
                return Array.Empty<Sprite>();

            int maxCount = Mathf.Min(count, frames.Count - startIndex);
            var result = new Sprite[maxCount];
            for (int i = 0; i < maxCount; i++)
                result[i] = frames[startIndex + i];
            return result;
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

        /// <summary>
        /// Convert a 2D vector to a primary cardinal direction using dominant axis.
        /// Mirrors Python's primary_direction_from_vector fallback when only 4-dir assets exist.
        /// </summary>
        public static Direction VectorToPrimaryDirection(Vector2 dir)
        {
            if (Mathf.Abs(dir.x) > Mathf.Abs(dir.y))
                return dir.x < 0f ? Direction.West : Direction.East;

            return dir.y < 0f ? Direction.South : Direction.North;
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

        private static Sprite[] ToSingleFrameArray(Sprite sprite)
        {
            return sprite == null ? Array.Empty<Sprite>() : new[] { sprite };
        }

        private void ApplyFrame(Sprite sprite)
        {
            if (targetRenderer != null && sprite != null)
                targetRenderer.sprite = sprite;
        }
    }
}
