using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Scene-level system that resolves overlaps between NPC bodies each physics frame,
    /// so a pack converging on the player spreads instead of stacking into one sprite.
    /// Mirrors Python NpcSeparationSystem.
    ///
    /// Attach to any persistent scene GameObject (created by
    /// <c>GameplaySceneSetup.EnsureNPCSeparation</c>).
    ///
    /// <para><b>Why this is written the way it is.</b> The original solved each pair
    /// TWICE per iteration — once while visiting A with B as a neighbour and again while
    /// visiting B with A as one — and EACH visit moved BOTH bodies, so with three
    /// iterations every overlap was corrected six times against an authored strength of
    /// 2.5. It also applied each correction through <c>Rigidbody2D.MovePosition</c>
    /// inside the neighbour loop, and a body honours only the LAST MovePosition issued
    /// before a physics step — so with K overlapping neighbours, K-1 of the corrections
    /// were computed and thrown away. Over-correcting and then discarding most of it is
    /// what made a clump jitter instead of spreading.</para>
    ///
    /// <para>Now: neighbours come from <see cref="EntityRegistry.Monsters"/> and cached
    /// positions rather than a per-NPC <c>Physics2D.OverlapCircleAll</c> (which allocated
    /// a fresh array 3xN times per FixedUpdate — 3,000/s for a pack of twenty), each
    /// unordered pair is visited exactly once, every correction accumulates into a
    /// per-entity displacement, and the total is written to each body ONCE at the end.</para>
    ///
    /// <para>The final write is to <c>Rigidbody2D.position</c>, not <c>MovePosition</c>.
    /// MovePosition is a swept move that OWNS the body's motion for that step, which
    /// would fight the FSM states — they write <c>velocity</c> from <c>Update</c>, at a
    /// different rate, with no execution order defined between them. A direct position
    /// nudge composes with the velocity integration instead of replacing it, so a monster
    /// being separated still advances. The corrections are bounded by
    /// <c>overlap * separationStrength * fixedDeltaTime</c>, far too small to tunnel.</para>
    /// </summary>
    public class NPCSeparationSystem : MonoBehaviour
    {
        [Header("Separation Settings")]
        [Tooltip("Radius to query nearby NPCs around each NPC (world units). Python: 48px / 16PPU = 3")]
        [SerializeField] private float searchRadius = 3f;

        [Tooltip("Separation force scale. Higher = faster push-apart.")]
        [SerializeField] private float separationStrength = 2.5f;

        [Tooltip("Minimum overlap distance to trigger separation (world units).")]
        [SerializeField] private float minOverlap = 0.05f;

        [Tooltip("Number of solver iterations per frame. Python: max_iters = 3")]
        [SerializeField] private int solverIterations = 3;

        // Per-FixedUpdate working set. Instance fields, not statics, so Domain Reload
        // being OFF cannot carry a destroyed Rigidbody2D into the next Play session.
        private readonly List<Rigidbody2D> _bodies = new List<Rigidbody2D>(64);
        private readonly List<Vector2> _positions = new List<Vector2>(64);
        private readonly List<float> _radii = new List<float>(64);
        private readonly List<Vector2> _displacements = new List<Vector2>(64);

        /// <summary>
        /// Body-collider radius per entity, keyed by instance id.
        /// <c>EntityColliderConfigurator.GetBodyCollider</c> allocates internally via
        /// <c>GetComponents&lt;Collider2D&gt;()</c>, and a collider's size does not change
        /// between frames, so resolving it once per entity instead of once per neighbour
        /// per iteration removes the system's last per-frame allocation.
        /// </summary>
        private readonly Dictionary<int, float> _radiusCache = new Dictionary<int, float>(128);

        /// <summary>Prune threshold for <see cref="_radiusCache"/> — monsters die and respawn.</summary>
        private const int RadiusCacheMax = 512;

        private void FixedUpdate()
        {
            var monsters = EntityRegistry.Monsters;
            if (monsters == null || monsters.Count < 2) return;

            GatherActiveBodies(monsters);
            int n = _bodies.Count;
            if (n < 2) return;

            float searchRadiusSq = searchRadius * searchRadius;
            float dt = Time.fixedDeltaTime;

            for (int iter = 0; iter < solverIterations; iter++)
            {
                for (int i = 0; i < n; i++) _displacements[i] = Vector2.zero;
                bool movedAny = false;

                // j > i: every unordered pair exactly once. The old loop visited each
                // pair twice and moved both bodies on each visit.
                for (int i = 0; i < n; i++)
                {
                    Vector2 posA = _positions[i];
                    float radA = _radii[i];

                    for (int j = i + 1; j < n; j++)
                    {
                        Vector2 delta = posA - _positions[j];
                        float distSq = delta.sqrMagnitude;
                        if (distSq > searchRadiusSq) continue;

                        float minDist = radA + _radii[j];
                        if (distSq >= minDist * minDist) continue;

                        float dist = Mathf.Sqrt(distSq);
                        float overlap = minDist - dist;
                        if (overlap < minOverlap) continue;

                        Vector2 dir = dist > 0.001f
                            ? delta / dist
                            // Perfectly coincident: any direction will do, but it must be
                            // deterministic per pair or two stacked monsters jitter forever
                            // on fresh random values every physics step.
                            : DeterministicNudge(i, j);

                        Vector2 sep = dir * (overlap * separationStrength * dt * 0.5f);
                        _displacements[i] += sep;
                        _displacements[j] -= sep;
                        movedAny = true;
                    }
                }

                if (!movedAny) break;

                // Integrate this iteration into the working positions so the next one
                // solves against the partially-resolved layout, exactly as before.
                for (int i = 0; i < n; i++) _positions[i] += _displacements[i];
            }

            // One write per body per physics step.
            for (int i = 0; i < n; i++)
            {
                var rb = _bodies[i];
                if (rb == null) continue;
                Vector2 target = _positions[i];
                if ((target - rb.position).sqrMagnitude > 0f)
                    rb.position = target;
            }
        }

        private void GatherActiveBodies(IReadOnlyList<GameObject> monsters)
        {
            _bodies.Clear();
            _positions.Clear();
            _radii.Clear();
            _displacements.Clear();

            for (int i = 0; i < monsters.Count; i++)
            {
                var go = monsters[i];
                if (go == null || !go.activeInHierarchy) continue;

                var rb = go.GetComponent<Rigidbody2D>();
                if (rb == null || rb.isKinematic) continue;

                float radius = ResolveRadius(go);
                if (radius <= 0f) continue;

                _bodies.Add(rb);
                _positions.Add(rb.position);
                _radii.Add(radius);
                _displacements.Add(Vector2.zero);
            }
        }

        private float ResolveRadius(GameObject go)
        {
            int id = go.GetInstanceID();
            if (_radiusCache.TryGetValue(id, out float cached)) return cached;

            var col = EntityColliderConfigurator.GetBodyCollider(go);
            float radius = col != null ? GetColliderRadius(col) : 0f;

            if (_radiusCache.Count >= RadiusCacheMax) _radiusCache.Clear();
            _radiusCache[id] = radius;
            return radius;
        }

        /// <summary>
        /// A stable pseudo-direction for two exactly-coincident bodies. Derived from the
        /// pair's indices rather than <c>Random</c> so the push does not change direction
        /// every physics step, which is its own source of jitter.
        /// </summary>
        private static Vector2 DeterministicNudge(int i, int j)
        {
            float angle = ((i * 73856093) ^ (j * 19349663)) * 0.0001f;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        private static float GetColliderRadius(Collider2D col)
        {
            if (col is CircleCollider2D cc)
                return cc.radius * Mathf.Max(Mathf.Abs(col.transform.lossyScale.x), Mathf.Abs(col.transform.lossyScale.y));

            if (col is BoxCollider2D bc)
            {
                Vector2 s = Vector2.Scale(bc.size, Abs(col.transform.lossyScale)) * 0.5f;
                return Mathf.Min(s.x, s.y);
            }

            return 0.4f; // fallback
        }

        private static Vector2 Abs(Vector3 value)
        {
            return new Vector2(Mathf.Abs(value.x), Mathf.Abs(value.y));
        }
    }
}
