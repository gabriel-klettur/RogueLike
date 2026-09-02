using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Discharges running up the funnel wall.
    ///
    /// <para>WHY THE EFFECT NEEDS THEM. Everything else in this rig is CONTINUOUS — bands turn,
    /// debris circles, streaks run — and continuous motion at a steady rate stops being read
    /// after about a second. An arc is an EVENT: it appears, it is gone, and it resets the eye.
    /// The ki charge earns its top end the same way.</para>
    ///
    /// <para>They climb the wall rather than crossing the middle, because a bolt through the
    /// centre of the cone contradicts the silhouette the bands spent nine layers establishing —
    /// it says the column is solid when the whole shape says it is hollow.</para>
    ///
    /// <para>Each arc is one <c>LineRenderer</c> reused forever, dark for most of its life.
    /// Spawning one per strike would allocate through a two-second cast and leave the pooling
    /// to the garbage collector.</para>
    /// </summary>
    internal sealed partial class VortexFunnelFX
    {
        private const int ARC_COUNT = 3;

        /// <summary>How long one discharge is on screen. Below about three frames it reads as a
        /// dropped frame rather than as lightning.</summary>
        private const float ARC_VISIBLE_SECONDS = 0.085f;

        /// <summary>
        /// Gap between one arc's strikes. Measured at 0.16-0.52 the three arcs together were lit
        /// on 78 % of frames — which is not lightning, it is a lamp with a flicker, and it
        /// forfeits the whole reason the layer exists. At these numbers the duty is about a
        /// quarter and a two-second cast carries roughly five discharges.
        /// </summary>
        private const float ARC_INTERVAL_MIN = 0.45f;
        private const float ARC_INTERVAL_MAX = 1.30f;

        /// <summary>How far the path is allowed to wander off the straight line, as a fraction
        /// of its own length.</summary>
        private const float ARC_JAGGEDNESS = 0.22f;

        private LineRenderer[] _arcs;
        private float[] _arcNextStrike;
        private float[] _arcAge;
        private Vector3[] _arcPoints;

        private void BuildArcs()
        {
            _arcs = new LineRenderer[ARC_COUNT];
            _arcNextStrike = new float[ARC_COUNT];
            _arcAge = new float[ARC_COUNT];
            _arcPoints = new Vector3[LightningPath.POINT_COUNT];

            for (int i = 0; i < ARC_COUNT; i++)
            {
                var go = new GameObject("Arc" + i);
                go.transform.SetParent(_root, false);

                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.positionCount = LightningPath.POINT_COUNT;
                line.numCapVertices = 2;
                line.widthMultiplier = Mathf.Max(0.03f, _radius * 0.035f);
                // sharedMaterial, never material: assigning `material` clones the shared asset
                // once per arc, which is the bug LightningBoltFX carried for months.
                line.sharedMaterial = ElementalSprites.SharedAdditiveMaterial;
                line.sortingLayerName = SortingConfig.LAYER_VFX;
                line.sortingOrder = ORDER_DUST + 2;
                line.textureMode = LineTextureMode.Stretch;
                line.alignment = LineAlignment.View;
                line.enabled = false;

                _arcs[i] = line;
                // Staggered, or all three strike together on the first frame and the effect
                // opens with one flash and then nothing.
                _arcNextStrike[i] = Random.Range(0.12f, ARC_INTERVAL_MIN) + i * 0.19f;
                _arcAge[i] = float.MaxValue;
            }
        }

        private void TickArcs(float deltaTime, float grown, float fade, float dissipate)
        {
            if (_arcs == null) return;

            // Nothing discharges out of a funnel that has not finished climbing, and nothing
            // discharges out of one that is coming apart.
            bool canStrike = grown > 0.75f && dissipate <= 0f && fade > 0.5f;

            for (int i = 0; i < _arcs.Length; i++)
            {
                _arcAge[i] += deltaTime;
                _arcNextStrike[i] -= deltaTime;

                if (canStrike && _arcNextStrike[i] <= 0f)
                {
                    Strike(i);
                    _arcNextStrike[i] = Random.Range(ARC_INTERVAL_MIN, ARC_INTERVAL_MAX);
                }

                float t = _arcAge[i] / ARC_VISIBLE_SECONDS;
                if (t >= 1f) { if (_arcs[i].enabled) _arcs[i].enabled = false; continue; }

                // Bright the instant it appears and gone on a curve, which is what separates a
                // discharge from a light being switched on and off.
                float alpha = Mathf.Pow(1f - t, 1.9f) * fade;
                var head = WithAlpha(_palette.Core, alpha);
                var tail = WithAlpha(_palette.Mid, alpha * 0.35f);
                _arcs[i].startColor = head;
                _arcs[i].endColor = tail;
            }
        }

        /// <summary>
        /// Lay one discharge along the wall. Both ends sit ON the cone at their own height, so
        /// the bolt follows the surface instead of cutting the hollow middle.
        /// </summary>
        private void Strike(int index)
        {
            float fromHeight01 = Random.Range(0.05f, 0.35f);
            float toHeight01 = Random.Range(fromHeight01 + 0.30f, 1f);

            // The two ends are at different angles, so the bolt WRAPS as it climbs rather than
            // standing in one vertical plane — a straight-up bolt reads as a pillar, not a spiral.
            float baseAngle = Random.Range(0f, Mathf.PI * 2f);
            float sweep = Random.Range(0.5f, 1.5f) * _spinSign;

            Vector3 from = WallPoint(baseAngle, fromHeight01);
            Vector3 to = WallPoint(baseAngle + sweep, toHeight01);

            LightningPath.Generate(_arcPoints, from, to, (to - from).magnitude * ARC_JAGGEDNESS);
            _arcs[index].SetPositions(_arcPoints);
            _arcs[index].enabled = true;
            _arcAge[index] = 0f;
        }

        /// <summary>A point on the funnel's surface, in the rig's own local space. Shares the
        /// cone equation with the bands, so an arc can never leave the shape they draw.</summary>
        private Vector3 WallPoint(float angle, float height01)
        {
            float radius = _radius * Mathf.Lerp(NECK_FRAC, FLARE_FRAC, Mathf.Pow(height01, 0.75f));
            Vector2 offset = WallOffset(height01);
            return new Vector3(
                offset.x + Mathf.Cos(angle) * radius,
                Height * height01 + offset.y + Mathf.Sin(angle) * radius * GROUND_SQUASH,
                0f);
        }
    }
}
