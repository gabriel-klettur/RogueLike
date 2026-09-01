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
            var spriteSet = GetSpriteSet(_currentState, _activeVariant);
            Sprite[] frames = spriteSet.GetFrames(_currentDirection);

            if (frames == null || frames.Length == 0)
            {
                // For Death, prefer ANY non-empty death direction (the corpse pose)
                // before falling back to idle. Many monsters wire only `death.south`
                // for an omnidirectional corpse sprite — without this, an entity that
                // dies facing N/E/W would render the idle pose and look alive.
                if (_currentState == AnimState.Death)
                    frames = FindFirstNonEmptyDirection(spriteSet);

                if (frames == null || frames.Length == 0)
                {
                    frames = idleSprites.GetFrames(_currentDirection);
                    if (frames == null || frames.Length == 0) return;
                }
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

            // Death: play once, then hold the final corpse frame.
            if (_currentState == AnimState.Death)
            {
                int idx = Mathf.Min(_frameIndex, frames.Length - 1);
                ApplyFrame(frames[idx]);
                if (_frameIndex < frames.Length - 1)
                    _frameIndex++;
                return;
            }

            // A variant that ENDS in a pose plays once and stays there, exactly as Death
            // does above. The dash is why: its body teleports in one physics step and its
            // wake is gone in 0.14 s, so the charge frames are compressed to fit and the
            // landing pose holds the remainder of the window rather than the lunge starting
            // over. Looping a move that finishes somewhere reads as a stutter, not a cycle.
            if (PacingOf(_currentState, _activeVariant).HoldLastFrame)
            {
                int held = Mathf.Min(_frameIndex, frames.Length - 1);
                ApplyFrame(frames[FrameAt(held, frames.Length)]);
                if (_frameIndex < frames.Length - 1)
                    _frameIndex++;
                return;
            }

            // Default: loop all frames. The cursor always counts UP; FrameAt is what turns it
            // into a back-to-front read, so reversed playback inherits the loop, the hold and
            // the frame clock rather than reimplementing all three.
            _frameIndex %= frames.Length;
            ApplyFrame(frames[FrameAt(_frameIndex, frames.Length)]);
            _frameIndex = (_frameIndex + 1) % frames.Length;
        }

        /// <summary>
        /// Finds the first non-empty direction within a sprite set. Used to render
        /// the canonical corpse pose when the current direction's death frames are
        /// missing (e.g. monsters that only wire `death.south`).
        /// </summary>
        private static Sprite[] FindFirstNonEmptyDirection(DirectionalSpriteSet set)
        {
            // Probe in order of design likelihood for a single-direction corpse sprite.
            Sprite[] frames = set.south;
            if (frames != null && frames.Length > 0) return frames;
            frames = set.east;
            if (frames != null && frames.Length > 0) return frames;
            frames = set.west;
            if (frames != null && frames.Length > 0) return frames;
            frames = set.north;
            if (frames != null && frames.Length > 0) return frames;
            frames = set.southEast;
            if (frames != null && frames.Length > 0) return frames;
            frames = set.southWest;
            if (frames != null && frames.Length > 0) return frames;
            frames = set.northEast;
            if (frames != null && frames.Length > 0) return frames;
            frames = set.northWest;
            if (frames != null && frames.Length > 0) return frames;
            return null;
        }

        /// <summary>
        /// Frames for a state, direction and attack variant, WITHOUT touching the
        /// animation cursor. <see cref="GetStateLength"/> needs to measure a variant it is
        /// not currently playing, and every other path here mutates <c>_frameIndex</c>.
        /// Returns null when nothing is wired, so a caller can treat that as zero length
        /// rather than as one frame.
        /// </summary>
        private Sprite[] ResolveFrames(AnimState state, Direction direction, int attackVariant)
        {
            Sprite[] frames = GetSpriteSet(state, attackVariant).GetFrames(direction);
            return frames != null && frames.Length > 0 ? frames : null;
        }

        private DirectionalSpriteSet GetSpriteSet(AnimState state, int attackVariant = -1)
        {
            // A selected variant REPLACES that state's single set. Bounds are re-checked
            // here rather than trusted from the caller: the variant array is rebuilt on
            // every ApplyVisuals, and an index cached across a shorter rebuild would
            // throw out of the render path, where it is hardest to trace.
            DirectionalSpriteSet[] variants = VariantsFor(state);
            if (variants != null && attackVariant >= 0 && attackVariant < variants.Length)
                return variants[attackVariant];

            return state switch
            {
                AnimState.Idle => idleSprites,
                AnimState.Walk => walkSprites,
                AnimState.Chase => chaseSprites,
                AnimState.Cast => castSprites,
                AnimState.Attack => attackSprites,
                AnimState.Damage => damageSprites,
                AnimState.Death => deathSprites,
                // An entity with no recover art falls back to idle rather than to an empty
                // set, so a revive that plays on a character without the animation still
                // shows the character standing instead of nothing at all.
                AnimState.Recover => HasFrames(recoverSprites) ? recoverSprites : idleSprites,
                _ => idleSprites
            };
        }

        /// <summary>True when any direction of the set carries at least one frame.</summary>
        private static bool HasFrames(DirectionalSpriteSet set)
        {
            return (set.south != null && set.south.Length > 0) ||
                   (set.southEast != null && set.southEast.Length > 0) ||
                   (set.east != null && set.east.Length > 0) ||
                   (set.northEast != null && set.northEast.Length > 0) ||
                   (set.north != null && set.north.Length > 0) ||
                   (set.northWest != null && set.northWest.Length > 0) ||
                   (set.west != null && set.west.Length > 0) ||
                   (set.southWest != null && set.southWest.Length > 0);
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
