using UnityEngine;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// The dome's frame loop, and the ARCS that are the whole spell.
    ///
    /// <para>The shell itself is deliberately almost featureless — a faint silhouette, a dim
    /// fill and twenty-two slowly turning points. Everything the player is meant to look at is
    /// an EVENT: a discharge that appears between two points on the surface and is gone in about
    /// a tenth of a second. An effect made only of continuous motion stops being read after
    /// roughly a second, which is why the old rig — a rune spinning at a constant rate under a
    /// pulse ring firing twice a second — measured 170 % duty and read as one steady texture.
    /// Overlapping events are not events.</para>
    ///
    /// <para>0.11 s held against a 0.25–0.62 s interval is about 25 %. The first version of the
    /// vortex's discharge layer measured 78 % of frames lit, which is not lightning but a lamp
    /// with a flicker, and it forfeits the one thing the layer exists for.</para>
    ///
    /// <para>AN ARC PREFERS A VICTIM. When the sweep has just hurt something, the next arc
    /// terminates ON that body instead of on a shell node — so a decorative layer becomes a
    /// damage indicator at no extra cost, and the player can see WHO the field is reaching.</para>
    /// </summary>
    internal sealed partial class StaticDomeFX
    {
        private const float ARC_SECONDS = 0.11f;
        private const float ARC_INTERVAL_MIN = 0.25f;
        private const float ARC_INTERVAL_MAX = 0.62f;

        /// <summary>How long a reported victim stays "fresh" enough to be arced at. About one
        /// damage tick, so the indication tracks the mechanic rather than lagging it.</summary>
        private const float TARGET_MEMORY_SECONDS = 0.60f;

        /// <summary>Thickness of a drawn arc, world units.</summary>
        private const float ARC_THICKNESS = 0.42f;

        private bool[] _nodeFront;

        public void Tick(float deltaTime, float fade, int casterOrder)
        {
            if (_destroyed) return;

            float dt = Mathf.Max(0f, deltaTime);
            _age += dt;
            _fade = Mathf.Clamp01(fade);
            _spin += SPIN_DEG_PER_SECOND * dt * Mathf.Deg2Rad;
            _targetAge += dt;

            if (_arcFlash > 0f) _arcFlash = Mathf.Max(0f, _arcFlash - dt / ARC_SECONDS);

            RebaseSortingOrder(casterOrder);
            AdvanceNodes();
            AdvanceArcs(dt);
            AdvanceShell();

            // The light flickers WITH the arcs. A field held for eight seconds at constant
            // brightness is the failure this whole layer exists to avoid, restated in light.
            SetLightIntensity((0.45f + 1.30f * _arcFlash) * _fade);
        }

        /// <summary>
        /// The field just hurt something. Remember where, so the next arc lands on it.
        /// </summary>
        public void NoteTarget(Vector3 worldPosition)
        {
            _targetWorld = worldPosition;
            _targetAge = 0f;
        }

        private void AdvanceNodes()
        {
            if (_nodeFront == null) _nodeFront = new bool[NODE_COUNT];

            for (int i = 0; i < NODE_COUNT; i++)
            {
                float azimuth = _nodeAzimuth[i] + _spin;
                float elevation = _nodeElevation[i]
                                  // A slow individual wobble, or twenty-two points turning in
                                  // perfect lockstep read as one rigid object rather than as a
                                  // field of charge.
                                  + 0.06f * Mathf.Sin(_age * 1.3f + _nodePhase[i]);

                float cosE = Mathf.Cos(elevation);
                float x = _radius * cosE * Mathf.Cos(azimuth);
                float y = _radius * Mathf.Sin(elevation);
                float z = _radius * cosE * Mathf.Sin(azimuth);   // depth: + is toward the camera

                _nodeLocal[i] = new Vector3(x, y, 0f);
                _nodeDepth[i] = z;
                _nodes[i].localPosition = _nodeLocal[i];

                // Size, brightness and sort order all read off the SAME depth. If they disagree
                // the motion reads as sliding across a disc instead of turning on a sphere.
                float near01 = (z / _radius + 1f) * 0.5f;
                float size = NODE_SIZE * Mathf.Lerp(0.62f, 1.20f, near01);
                _nodes[i].localScale = Vector3.one * size;

                float twinkle = 0.60f + 0.40f * Mathf.Sin(_age * 5.5f + _nodePhase[i] * 3f);
                SetAlpha(_nodeRenderers[i], Mathf.Lerp(0.22f, 0.75f, near01) * twinkle * _fade);

                bool front = z >= 0f;
                if (front == _nodeFront[i]) continue;
                _nodeFront[i] = front;
                _nodeRenderers[i].sortingOrder = _baseOrder + (front ? FRONT_ORDER : BACK_ORDER);
            }
        }

        private void AdvanceArcs(float dt)
        {
            _arcTimer -= dt;
            if (_arcTimer <= 0f)
            {
                _arcTimer = Random.Range(ARC_INTERVAL_MIN, ARC_INTERVAL_MAX);
                FireArc();
            }

            for (int i = 0; i < ARCS; i++)
            {
                if (_arcAge[i] >= ARC_SECONDS) continue;
                _arcAge[i] += dt;

                float t = Mathf.Clamp01(_arcAge[i] / ARC_SECONDS);
                // Full brightness immediately, then out. A discharge has no attack: it is
                // already there when you notice it.
                SetAlpha(_arcRenderers[i], (1f - t * t) * 0.95f * _fade);
            }
        }

        private void FireArc()
        {
            if (_arcs == null || _shell == null) return;

            int a = Random.Range(0, NODE_COUNT);
            Vector3 from = _nodeLocal[a];
            Vector3 to;

            if (_targetAge < TARGET_MEMORY_SECONDS)
            {
                // Terminate on the body the sweep just hurt. The point is converted into the
                // shell's own space, so an arc drawn to it is drawn to where that body actually
                // is even while the caster is moving.
                Vector3 local = _shell.InverseTransformPoint(_targetWorld);
                to = new Vector3(local.x, local.y + 0.45f, 0f);

                // Start from whichever node is nearest the victim, so the discharge leaves the
                // side of the dome facing them rather than crossing the whole shell.
                float best = float.MaxValue;
                for (int i = 0; i < NODE_COUNT; i++)
                {
                    float d = (_nodeLocal[i] - to).sqrMagnitude;
                    if (d >= best) continue;
                    best = d;
                    from = _nodeLocal[i];
                }
            }
            else
            {
                int b = (a + Random.Range(2, NODE_COUNT - 1)) % NODE_COUNT;
                to = _nodeLocal[b];
            }

            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 0.05f) return;

            int slot = _arcCursor;
            _arcCursor = (_arcCursor + 1) % ARCS;

            // The Arc sprite runs along +X from a LEFT-CENTRE pivot, so placing it at `from`,
            // turning it to the bearing and scaling x by the distance is the whole placement.
            _arcs[slot].localPosition = from;
            _arcs[slot].localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            _arcs[slot].localScale = new Vector3(length, ARC_THICKNESS / FieldSprites.ArcUnitHeight, 1f);
            _arcAge[slot] = 0f;
            _arcFlash = 1f;
        }

        private void AdvanceShell()
        {
            float breathe = 0.72f + 0.28f * Mathf.Sin(_age * 1.9f);

            // Very faint. The shell is a container, not a subject: everything bright here is an
            // arc, and raising the shell's own alpha is what would turn the spell back into a
            // steady glowing bubble.
            SetAlpha(_silhouette, (0.16f + 0.06f * breathe + 0.30f * _arcFlash) * _fade);
            SetAlpha(_fill, (0.055f + 0.02f * breathe + 0.09f * _arcFlash) * _fade);

            // Brightness only. A circle that breathes in SIZE is a promise that moves, and this
            // one is pinned to the circle the sweep actually queries.
            SetAlpha(_groundRing, (0.30f + 0.14f * breathe + 0.40f * _arcFlash) * _fade);
        }
    }
}
