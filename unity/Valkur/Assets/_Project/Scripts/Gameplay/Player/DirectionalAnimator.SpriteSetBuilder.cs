using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Gameplay
{
    public partial class DirectionalAnimator
    {
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
    }
}
