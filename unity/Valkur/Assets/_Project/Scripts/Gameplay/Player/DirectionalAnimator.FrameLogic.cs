using System;
using UnityEngine;

namespace Valkur.Gameplay
{
    public partial class DirectionalAnimator
    {
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
