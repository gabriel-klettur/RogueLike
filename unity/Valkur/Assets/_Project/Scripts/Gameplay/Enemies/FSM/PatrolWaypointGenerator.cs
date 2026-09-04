using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.FSM
{
    /// <summary>
    /// Generates patrol waypoints from a patrol type string.
    /// Mirrors Python's build_patrol_route() in behaviour_loader.py.
    /// Supports: stroll, line, ping_pong, circle, square, zigzag, figure_eight.
    /// </summary>
    public static class PatrolWaypointGenerator
    {
        /// <summary>
        /// Generate waypoints for the given patrol type around a spawn position.
        /// </summary>
        /// <param name="origin">World-space spawn position.</param>
        /// <param name="patrolType">Patrol pattern ID (stroll, line, circle, square, zigzag, figure_eight).</param>
        /// <returns>Array of world-space waypoints.</returns>
        public static Vector2[] Generate(Vector2 origin, string patrolType)
        {
            if (string.IsNullOrEmpty(patrolType))
                return DefaultLine(origin);

            switch (patrolType.ToLowerInvariant())
            {
                case "stroll":
                    return GenerateStroll(origin, STROLL_HALF_WIDTH);

                case "line":
                case "ping_pong":
                    return GenerateLine(origin, 5f);

                case "circle":
                    return GenerateCircle(origin, 4f, 16);

                case "square":
                    return GenerateSquare(origin, 6f, 6f, 4);

                case "zigzag":
                    return GenerateZigzag(origin, 6, 3f, 2f);

                case "figure_eight":
                    return GenerateFigureEight(origin, 3f, 2f, 12);

                default:
                    return DefaultLine(origin);
            }
        }

        private static Vector2[] DefaultLine(Vector2 origin)
        {
            return new[] { origin, origin + new Vector2(5f, 0f) };
        }

        /// <summary>
        /// How far either side of the spawn a <c>stroll</c> reaches, in world units.
        ///
        /// Sized against the pacing window rather than picked: a shopkeeper walks at 0.8 u/s
        /// and the authored Idle-to-Patrol cycle gives her five seconds, so 1.25 either way
        /// is a 2.5-unit crossing she completes in about three seconds — she arrives, pauses,
        /// and is sent back to Idle, instead of being interrupted mid-stride every time.
        /// </summary>
        private const float STROLL_HALF_WIDTH = 1.25f;

        /// <summary>
        /// A short pace either side of where the entity stands, for someone who belongs at a
        /// spot rather than covering ground.
        ///
        /// Centred on the origin, unlike every other pattern here, which starts AT the spawn
        /// and extends away from it. That difference is the whole point: a vendor patrolling
        /// a 5-unit <c>line</c> spends most of the session an average of 2.5 units east of
        /// their own stall, so the player looks for them where they were placed and finds
        /// empty ground. Centred, the stall stays the middle of the walk.
        ///
        /// HORIZONTAL on purpose too. Art drawn as a single front-facing view — Gatita's is —
        /// has no back to show, and <c>DirectionalAnimator</c> never flips, so a character
        /// walking north reads as moon-walking towards the camera. Strafing left and right in
        /// front of a counter is exactly what that art can portray honestly.
        /// </summary>
        private static Vector2[] GenerateStroll(Vector2 origin, float halfWidth)
        {
            return new[]
            {
                origin + new Vector2(-halfWidth, 0f),
                origin + new Vector2(halfWidth, 0f),
            };
        }

        private static Vector2[] GenerateLine(Vector2 origin, float lengthTiles)
        {
            return new[] { origin, origin + new Vector2(lengthTiles, 0f) };
        }

        private static Vector2[] GenerateCircle(Vector2 origin, float radiusTiles, int points)
        {
            var result = new Vector2[points];
            for (int i = points - 1; i >= 0; i--)
            {
                float th = (2f * Mathf.PI * i) / points;
                result[points - 1 - i] = origin + new Vector2(
                    radiusTiles * Mathf.Cos(th),
                    radiusTiles * Mathf.Sin(th)
                );
            }
            return result;
        }

        private static Vector2[] GenerateSquare(Vector2 origin, float width, float height, int pointsPerEdge)
        {
            var result = new List<Vector2>();
            float x0 = origin.x - width / 2f;
            float y0 = origin.y - height / 2f;
            float x1 = origin.x + width / 2f;
            float y1 = origin.y + height / 2f;

            int ppe = Mathf.Max(1, pointsPerEdge);
            // Top edge
            for (int i = 0; i < ppe; i++)
            {
                float t = ppe > 1 ? (float)i / (ppe - 1) : 0f;
                result.Add(new Vector2(x0 + (x1 - x0) * t, y0));
            }
            // Right edge
            for (int i = 1; i < ppe; i++)
            {
                float t = (float)i / (ppe - 1);
                result.Add(new Vector2(x1, y0 + (y1 - y0) * t));
            }
            // Bottom edge
            for (int i = 1; i < ppe; i++)
            {
                float t = (float)i / (ppe - 1);
                result.Add(new Vector2(x1 - (x1 - x0) * t, y1));
            }
            // Left edge
            for (int i = 1; i < ppe - 1; i++)
            {
                float t = (float)i / (ppe - 1);
                result.Add(new Vector2(x0, y1 - (y1 - y0) * t));
            }
            return result.ToArray();
        }

        private static Vector2[] GenerateZigzag(Vector2 origin, int segments, float step, float amplitude)
        {
            var result = new Vector2[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float x = origin.x + step * i;
                float y = origin.y + (i % 2 == 0 ? amplitude : -amplitude);
                result[i] = new Vector2(x, y);
            }
            return result;
        }

        private static Vector2[] GenerateFigureEight(Vector2 origin, float radiusTiles, float gapTiles, int pointsPerLoop)
        {
            var result = new List<Vector2>();
            float cxLeft = origin.x - (radiusTiles + gapTiles / 2f);
            float cxRight = origin.x + (radiusTiles + gapTiles / 2f);
            float cy = origin.y;

            // Left loop clockwise
            for (int i = 0; i < pointsPerLoop; i++)
            {
                float th = (2f * Mathf.PI * (pointsPerLoop - 1 - i)) / pointsPerLoop;
                result.Add(new Vector2(cxLeft + radiusTiles * Mathf.Cos(th), cy + radiusTiles * Mathf.Sin(th)));
            }
            // Right loop counter-clockwise
            for (int i = 0; i < pointsPerLoop; i++)
            {
                float th = (2f * Mathf.PI * i) / pointsPerLoop;
                result.Add(new Vector2(cxRight + radiusTiles * Mathf.Cos(th), cy + radiusTiles * Mathf.Sin(th)));
            }
            return result.ToArray();
        }
    }
}
