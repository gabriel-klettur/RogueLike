using System.Collections.Generic;
using UnityEngine;

namespace Valkur.Gameplay.Spells.Debugging
{
    /// <summary>
    /// The world-space outline of a <see cref="Collider2D"/>, as closed loops of points.
    ///
    /// <para>Built from the collider's OWN fields through its transform, never from
    /// <c>Collider2D.bounds</c>: bounds is an axis-aligned box, so a rotated or scaled collider
    /// would be drawn larger than the shape Physics2D actually tests, and the overlay would
    /// report a contact the query never made. Bounds is only the fallback for types with no
    /// shape of their own to read (a composite).</para>
    /// </summary>
    public static class ColliderOutline
    {
        private const int CIRCLE_SEGMENTS = 32;
        private const int CAPSULE_CAP_SEGMENTS = 10;

        /// <summary>Appends one freshly-allocated loop per shape. For snapshots, not per frame.</summary>
        public static void AppendLoops(Collider2D collider, List<Vector2[]> loops)
        {
            var points = new List<Vector2>(40);
            var lengths = new List<int>(2);
            Build(collider, points, lengths);

            int cursor = 0;
            for (int i = 0; i < lengths.Count; i++)
            {
                var loop = new Vector2[lengths[i]];
                points.CopyTo(cursor, loop, 0, lengths[i]);
                cursor += lengths[i];
                loops.Add(loop);
            }
        }

        /// <summary>
        /// Non-allocating form: appends points into <paramref name="points"/> and the length of
        /// each loop into <paramref name="loopLengths"/>. Loops are implicitly closed.
        /// </summary>
        public static void Build(Collider2D collider, List<Vector2> points, List<int> loopLengths)
        {
            if (collider == null) return;
            Transform t = collider.transform;

            switch (collider)
            {
                case BoxCollider2D box:
                {
                    Vector2 h = box.size * 0.5f;
                    Vector2 o = box.offset;
                    points.Add(t.TransformPoint(o + new Vector2(-h.x, -h.y)));
                    points.Add(t.TransformPoint(o + new Vector2(h.x, -h.y)));
                    points.Add(t.TransformPoint(o + new Vector2(h.x, h.y)));
                    points.Add(t.TransformPoint(o + new Vector2(-h.x, h.y)));
                    loopLengths.Add(4);
                    return;
                }

                case CircleCollider2D circle:
                {
                    // Physics2D scales a circle by the LARGER axis of the lossy scale.
                    Vector3 s = t.lossyScale;
                    float r = circle.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y));
                    Vector2 c = t.TransformPoint(circle.offset);
                    for (int i = 0; i < CIRCLE_SEGMENTS; i++)
                    {
                        float a = i * Mathf.PI * 2f / CIRCLE_SEGMENTS;
                        points.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                    }
                    loopLengths.Add(CIRCLE_SEGMENTS);
                    return;
                }

                case CapsuleCollider2D capsule:
                {
                    // Built in LOCAL space then transformed, so rotation and non-uniform scale
                    // land where Physics2D puts them.
                    Vector2 size = capsule.size;
                    bool vertical = capsule.direction == CapsuleDirection2D.Vertical;
                    float radius = (vertical ? size.x : size.y) * 0.5f;
                    float half = Mathf.Max(0f, (vertical ? size.y : size.x) * 0.5f - radius);
                    Vector2 axis = vertical ? Vector2.up : Vector2.right;
                    // The axis turned -90 degrees: each cap sweeps half a turn from here, so the
                    // first cap bulges along +axis and the second along -axis, one closed loop.
                    Vector2 side = new Vector2(axis.y, -axis.x);
                    int count = 0;
                    for (int cap = 0; cap < 2; cap++)
                    {
                        Vector2 centre = capsule.offset + axis * (cap == 0 ? half : -half);
                        float start = Mathf.Atan2(side.y, side.x) + (cap == 0 ? 0f : Mathf.PI);
                        for (int i = 0; i <= CAPSULE_CAP_SEGMENTS; i++)
                        {
                            float a = start + Mathf.PI * i / CAPSULE_CAP_SEGMENTS;
                            Vector2 local = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                            points.Add(t.TransformPoint(local));
                            count++;
                        }
                    }
                    loopLengths.Add(count);
                    return;
                }

                case PolygonCollider2D polygon:
                {
                    for (int p = 0; p < polygon.pathCount; p++)
                    {
                        Vector2[] path = polygon.GetPath(p);
                        if (path == null || path.Length < 2) continue;
                        for (int i = 0; i < path.Length; i++)
                            points.Add(t.TransformPoint(path[i] + polygon.offset));
                        loopLengths.Add(path.Length);
                    }
                    return;
                }

                case EdgeCollider2D edge:
                {
                    Vector2[] path = edge.points;
                    if (path == null || path.Length < 2) return;
                    for (int i = 0; i < path.Length; i++)
                        points.Add(t.TransformPoint(path[i] + edge.offset));
                    loopLengths.Add(path.Length);
                    return;
                }

                default:
                {
                    Bounds b = collider.bounds;
                    points.Add(new Vector2(b.min.x, b.min.y));
                    points.Add(new Vector2(b.max.x, b.min.y));
                    points.Add(new Vector2(b.max.x, b.max.y));
                    points.Add(new Vector2(b.min.x, b.max.y));
                    loopLengths.Add(4);
                    return;
                }
            }
        }
    }
}
