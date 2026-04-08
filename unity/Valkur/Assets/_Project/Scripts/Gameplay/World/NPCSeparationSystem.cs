using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Scene-level system that resolves overlaps between NPC colliders each physics frame.
    /// Mirrors Python NpcSeparationSystem: uses broad-phase (Physics2D.OverlapCircleAll)
    /// and applies gentle separation impulses so NPCs never share the same space.
    ///
    /// Attach to any persistent scene GameObject (e.g. GameDirector child).
    /// Works only on GameObjects in the NPC layer (9).
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

        private static readonly int NpcLayerMask = 1 << 9; // NPC layer

        private void FixedUpdate()
        {
            // Use EntityRegistry instead of FindObjectsOfType (O(1) vs O(n) scene scan)
            var monsterList = EntityRegistry.Monsters;
            if (monsterList == null || monsterList.Count < 2) return;

            for (int iter = 0; iter < solverIterations; iter++)
            {
                bool movedAny = false;

                for (int i = 0; i < monsterList.Count; i++)
                {
                    var npcGo = monsterList[i];
                    if (npcGo == null || !npcGo.activeInHierarchy) continue;

                    var rbA = npcGo.GetComponent<Rigidbody2D>();
                    var colA = npcGo.GetComponent<Collider2D>();
                    if (rbA == null || colA == null) continue;
                    if (rbA.isKinematic) continue;

                    // Broad-phase: find nearby NPCs
                    var hits = Physics2D.OverlapCircleAll(
                        (Vector2)npcGo.transform.position, searchRadius, NpcLayerMask);

                    foreach (var hit in hits)
                    {
                        if (hit.gameObject == npcGo) continue;

                        // Verify the hit object is on NPC layer
                        if (hit.gameObject.layer != 9) continue;

                        var rbB = hit.GetComponent<Rigidbody2D>();
                        if (rbB == null || rbB.isKinematic) continue;

                        // Compute separation vector between feet positions
                        Vector2 posA = rbA.position;
                        Vector2 posB = rbB.position;
                        Vector2 delta = posA - posB;
                        float dist = delta.magnitude;

                        // Estimate combined half-extents from colliders
                        float radA = GetColliderRadius(colA);
                        float radB = GetColliderRadius(hit);
                        float minDist = radA + radB;

                        float overlap = minDist - dist;
                        if (overlap < minOverlap) continue;

                        // Push apart proportionally (lighter entity moves more)
                        Vector2 sep = dist > 0.001f
                            ? delta.normalized * overlap * separationStrength * Time.fixedDeltaTime
                            : new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)).normalized
                              * overlap * separationStrength * Time.fixedDeltaTime;

                        rbA.MovePosition(posA + sep * 0.5f);
                        rbB.MovePosition(posB - sep * 0.5f);
                        movedAny = true;
                    }
                }

                if (!movedAny) break;
            }
        }

        private static float GetColliderRadius(Collider2D col)
        {
            if (col is CircleCollider2D cc) return cc.radius;
            if (col is BoxCollider2D bc)
            {
                Vector2 s = bc.size * 0.5f;
                return Mathf.Min(s.x, s.y);
            }
            return 0.4f; // fallback
        }
    }
}
