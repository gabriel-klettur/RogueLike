using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.PlayMode.Gameplay
{
    /// <summary>
    /// PlayMode tests verifying that the fireball Projectile collides reliably with:
    ///  - Map tile colliders (layer World = 11, non-trigger)
    ///  - Building colliders (layer World = 11, non-trigger BoxCollider2D)
    ///  - Damageable targets on the configured target layer (layer NPC = 9)
    /// And that:
    ///  - The projectile uses Continuous CCD so it does not tunnel at high speed
    ///  - The projectile expires (becomes inactive) on impact instead of passing through
    ///  - Damage is only applied to targets in targetLayers, never to obstacle layers
    /// </summary>
    public class ProjectileCollisionPlayTests
    {
        private const int LayerWorld      = 11; // Tiles + Buildings live here
        private const int LayerProjectile = 10;
        private const int LayerNPC        = 9;

        private GameObject _projectile;
        private GameObject _obstacle;
        private GameObject _target;

        [TearDown]
        public void TearDown()
        {
            if (_projectile != null) Object.Destroy(_projectile);
            if (_obstacle   != null) Object.Destroy(_obstacle);
            if (_target     != null) Object.Destroy(_target);
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private GameObject CreateProjectile(Vector3 pos, Vector2 dir, float speed = 56.25f, float lifetime = 2f, float range = 20f, LayerMask targets = default)
        {
            var go = new GameObject("FireballTest");
            go.transform.position = pos;
            go.layer = LayerProjectile;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.15f;
            col.isTrigger = true;

            var proj = go.AddComponent<Projectile>();
            proj.Initialize(dir, speed, 20f, lifetime, range, targets);
            return go;
        }

        private GameObject CreateObstacle(Vector3 pos, int layer, Vector2 size, bool isTrigger = false)
        {
            var go = new GameObject("Obstacle");
            go.transform.position = pos;
            go.layer = layer;

            var box = go.AddComponent<BoxCollider2D>();
            box.size = size;
            box.isTrigger = isTrigger;
            return go;
        }

        private GameObject CreateDamageableTarget(Vector3 pos, int hp = 100)
        {
            var go = new GameObject("Target");
            go.transform.position = pos;
            go.layer = LayerNPC;

            var box = go.AddComponent<BoxCollider2D>();
            box.size = new Vector2(1f, 1f);
            box.isTrigger = false;

            var h = go.AddComponent<Health>();
            h.Initialize(hp);
            return go;
        }

        // ── Configuration / Setup Tests ─────────────────────────────────────

        [UnityTest]
        public IEnumerator Projectile_UsesContinuousCCD_AfterAwake()
        {
            _projectile = CreateProjectile(Vector3.zero, Vector2.right);
            yield return null;

            var rb = _projectile.GetComponent<Rigidbody2D>();
            Assert.AreEqual(
                CollisionDetectionMode2D.Continuous, rb.collisionDetectionMode,
                "Projectile must use Continuous CCD to avoid tunneling at high speed.");
        }

        [UnityTest]
        public IEnumerator Projectile_ColliderIsTrigger()
        {
            _projectile = CreateProjectile(Vector3.zero, Vector2.right);
            yield return null;

            var col = _projectile.GetComponent<CircleCollider2D>();
            Assert.IsTrue(col.isTrigger,
                "Projectile must be a trigger so OnTriggerEnter2D fires on overlap.");
        }

        // ── Obstacle Collision Tests ────────────────────────────────────────

        [UnityTest]
        public IEnumerator Projectile_ExpiresOnTileCollider_Layer11()
        {
            // Tile collider 2 units to the right of the projectile (non-trigger, World layer)
            _obstacle   = CreateObstacle(new Vector3(2f, 0f, 0f), LayerWorld, new Vector2(1f, 4f));
            _projectile = CreateProjectile(Vector3.zero, Vector2.right);

            // Wait long enough for the projectile to traverse the gap (2 units / 56.25 u/s ≈ 0.036s)
            // Add buffer for FixedUpdate cadence.
            float timeout = 0.5f;
            float elapsed = 0f;
            while (_projectile != null && _projectile.activeInHierarchy && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.IsTrue(_projectile == null || !_projectile.activeInHierarchy,
                "Projectile must expire when colliding with a World-layer tile collider " +
                "(it stayed active for " + elapsed + "s).");
        }

        [UnityTest]
        public IEnumerator Projectile_ExpiresOnBuildingCollider_Layer11()
        {
            // Simulate a building's BoxCollider2D (non-trigger, layer World)
            _obstacle   = CreateObstacle(new Vector3(2f, 0f, 0f), LayerWorld, new Vector2(2f, 2f));
            _projectile = CreateProjectile(Vector3.zero, Vector2.right);

            float timeout = 0.5f;
            float elapsed = 0f;
            while (_projectile != null && _projectile.activeInHierarchy && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.IsTrue(_projectile == null || !_projectile.activeInHierarchy,
                "Projectile must expire on building collider impact.");
        }

        [UnityTest]
        public IEnumerator Projectile_DoesNotTunnel_ThroughThinObstacle_AtHighSpeed()
        {
            // Thin 0.2-unit-wide obstacle in a fast-moving projectile's path.
            // Without Continuous CCD, the projectile would skip past it in one FixedUpdate step.
            _obstacle   = CreateObstacle(new Vector3(3f, 0f, 0f), LayerWorld, new Vector2(0.2f, 4f));
            _projectile = CreateProjectile(Vector3.zero, Vector2.right, speed: 80f);

            float timeout = 0.5f;
            float elapsed = 0f;
            while (_projectile != null && _projectile.activeInHierarchy && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.IsTrue(_projectile == null || !_projectile.activeInHierarchy,
                "Continuous CCD must prevent tunneling even through thin colliders at high speed.");
        }

        [UnityTest]
        public IEnumerator Projectile_PassesThrough_NonObstacle_NonTargetLayer()
        {
            // Pickup layer (12) is neither target nor obstacle — projectile should ignore it.
            _obstacle   = CreateObstacle(new Vector3(2f, 0f, 0f), 12, new Vector2(1f, 4f));
            _projectile = CreateProjectile(Vector3.zero, Vector2.right, speed: 5f);

            // Give the projectile time to reach and overlap the pickup
            yield return new WaitForSeconds(0.6f);

            Assert.IsTrue(_projectile != null && _projectile.activeInHierarchy,
                "Projectile should NOT expire when overlapping a non-target, non-obstacle layer.");
        }

        // ── Damage Tests ────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Projectile_AppliesDamage_ToTarget_OnLayerNPC()
        {
            _target     = CreateDamageableTarget(new Vector3(2f, 0f, 0f), hp: 100);
            _projectile = CreateProjectile(Vector3.zero, Vector2.right, targets: 1 << LayerNPC);

            float timeout = 0.5f;
            float elapsed = 0f;
            while (_projectile != null && _projectile.activeInHierarchy && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            var h = _target.GetComponent<Health>();
            Assert.Less(h.CurrentHp, 100, "Target should have taken damage (HP decreased).");
            Assert.IsTrue(_projectile == null || !_projectile.activeInHierarchy,
                "Projectile must expire after dealing damage.");
        }

        [UnityTest]
        public IEnumerator Projectile_DoesNotApplyDamage_ToObstacleLayer()
        {
            // World-layer obstacle that *also* has a Health component (edge case).
            _obstacle = CreateObstacle(new Vector3(2f, 0f, 0f), LayerWorld, new Vector2(1f, 4f));
            var h = _obstacle.AddComponent<Health>();
            h.Initialize(100);

            _projectile = CreateProjectile(Vector3.zero, Vector2.right, targets: 1 << LayerNPC);

            float timeout = 0.5f;
            float elapsed = 0f;
            while (_projectile != null && _projectile.activeInHierarchy && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.AreEqual(100, h.CurrentHp,
                "Projectile must NOT damage obstacle-layer objects (only targetLayers).");
            Assert.IsTrue(_projectile == null || !_projectile.activeInHierarchy,
                "Projectile must still expire on obstacle impact.");
        }

        // ── Lifetime / Range Tests ──────────────────────────────────────────

        [UnityTest]
        public IEnumerator Projectile_Expires_AfterLifetimeElapses()
        {
            _projectile = CreateProjectile(Vector3.zero, Vector2.right, speed: 1f, lifetime: 0.2f, range: 100f);

            yield return new WaitForSeconds(0.5f);

            Assert.IsTrue(_projectile == null || !_projectile.activeInHierarchy,
                "Projectile must expire after its lifetime elapses with no impact.");
        }

        [UnityTest]
        public IEnumerator Projectile_Expires_AfterReachingMaxRange()
        {
            _projectile = CreateProjectile(Vector3.zero, Vector2.right, speed: 20f, lifetime: 10f, range: 1.5f);

            // 1.5 units / 20 u/s = 0.075s — wait a bit more
            yield return new WaitForSeconds(0.4f);

            Assert.IsTrue(_projectile == null || !_projectile.activeInHierarchy,
                "Projectile must expire after travelling its max range.");
        }

        // ── Anti-tunneling stress tests ────────────────────────────────────

        [UnityTest]
        public IEnumerator Projectile_DoesNotTunnel_AtFireballProductionSpeed_56u()
        {
            // The actual fireball speed in fireball.asset (after halving): 56.25 u/s
            // At fixedDeltaTime=0.02s that's 1.125 units/step — easy tunneling territory
            // for naive velocity-based movement.
            _obstacle   = CreateObstacle(new Vector3(2f, 0f, 0f), LayerWorld, new Vector2(0.5f, 4f));
            _projectile = CreateProjectile(Vector3.zero, Vector2.right, speed: 56.25f);

            float timeout = 0.5f;
            float elapsed = 0f;
            while (_projectile != null && _projectile.activeInHierarchy && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.IsTrue(_projectile == null || !_projectile.activeInHierarchy,
                "Projectile at fireball production speed must NOT tunnel through normal-width obstacles.");
        }

        [UnityTest]
        public IEnumerator Projectile_DoesNotTunnel_AtExtremeSpeed_500u()
        {
            // Extreme speed stress test — anything sweep-based should still work.
            _obstacle   = CreateObstacle(new Vector3(5f, 0f, 0f), LayerWorld, new Vector2(0.5f, 4f));
            _projectile = CreateProjectile(Vector3.zero, Vector2.right, speed: 500f, lifetime: 1f, range: 100f);

            float timeout = 0.5f;
            float elapsed = 0f;
            while (_projectile != null && _projectile.activeInHierarchy && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.IsTrue(_projectile == null || !_projectile.activeInHierarchy,
                "Sweep-based collision must hold even at extreme speeds (500 u/s).");
        }

        [UnityTest]
        public IEnumerator Projectile_StopsAtImpactPoint_DoesNotPassThrough()
        {
            // Verify the projectile is moved to the impact surface and not past it.
            _obstacle   = CreateObstacle(new Vector3(3f, 0f, 0f), LayerWorld, new Vector2(1f, 4f));
            _projectile = CreateProjectile(Vector3.zero, Vector2.right, speed: 30f);

            // Cache the projectile's position right after expiration.
            float timeout = 0.5f;
            float elapsed = 0f;
            Vector3 finalPos = Vector3.zero;
            while (_projectile != null && elapsed < timeout)
            {
                if (!_projectile.activeInHierarchy)
                {
                    finalPos = _projectile.transform.position;
                    break;
                }
                elapsed += Time.deltaTime;
                yield return null;
            }

            // The obstacle's left edge sits at x=2.5 (centered at 3 with size.x=1).
            // The projectile (radius 0.15) should stop near x ≈ 2.35.
            Assert.LessOrEqual(finalPos.x, 2.5f + 0.05f,
                "Projectile must stop at/before the obstacle surface, never past it. Final x=" + finalPos.x);
        }

        // ── Multi-frame / direction tests ──────────────────────────────────

        [UnityTest]
        public IEnumerator Projectile_HitsObstacle_InAllCardinalDirections()
        {
            Vector2[] dirs = { Vector2.right, Vector2.up, Vector2.left, Vector2.down };
            foreach (var dir in dirs)
            {
                Vector3 obstaclePos = new Vector3(dir.x, dir.y, 0f) * 2f;
                _obstacle   = CreateObstacle(obstaclePos, LayerWorld, new Vector2(2f, 2f));
                _projectile = CreateProjectile(Vector3.zero, dir, speed: 30f);

                float timeout = 0.5f;
                float elapsed = 0f;
                while (_projectile != null && _projectile.activeInHierarchy && elapsed < timeout)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                Assert.IsTrue(_projectile == null || !_projectile.activeInHierarchy,
                    $"Projectile must collide in direction {dir}.");

                if (_projectile != null) Object.Destroy(_projectile);
                if (_obstacle != null) Object.Destroy(_obstacle);
                _projectile = null;
                _obstacle = null;
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Projectile_PicksClosestObstacle_WhenMultipleInPath()
        {
            // Two obstacles in path: near (x=2) and far (x=5). Projectile must hit the near one
            // and never reach the far one (i.e. final position < 4.5).
            var nearObs = CreateObstacle(new Vector3(2f, 0f, 0f), LayerWorld, new Vector2(0.5f, 4f));
            var farObs  = CreateObstacle(new Vector3(5f, 0f, 0f), LayerWorld, new Vector2(0.5f, 4f));
            _projectile = CreateProjectile(Vector3.zero, Vector2.right, speed: 50f);

            float timeout = 0.5f;
            float elapsed = 0f;
            Vector3 finalPos = Vector3.zero;
            while (_projectile != null && elapsed < timeout)
            {
                if (!_projectile.activeInHierarchy)
                {
                    finalPos = _projectile.transform.position;
                    break;
                }
                elapsed += Time.deltaTime;
                yield return null;
            }

            Assert.Less(finalPos.x, 3f,
                "Projectile must impact the nearest obstacle (x=2), not pass through to x=5. Final x=" + finalPos.x);

            Object.Destroy(nearObs);
            Object.Destroy(farObs);
        }

        [UnityTest]
        public IEnumerator Projectile_DoesNotHitItself_DespiteOwnCollider()
        {
            // Spawn projectile with its own trigger collider — sweep must filter it out
            // and not immediately self-expire.
            _projectile = CreateProjectile(Vector3.zero, Vector2.right, speed: 1f, lifetime: 5f, range: 100f);

            // Wait several FixedUpdate steps
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.IsTrue(_projectile != null && _projectile.activeInHierarchy,
                "Projectile must not self-expire from detecting its own collider.");
        }

        // ── Start-inside-collider regression tests ─────────────────────────
        // These guard against the bug where the projectile spawns overlapping
        // a collider (caster body, adjacent wall, etc.) and immediately detonates
        // because Physics2D.queriesStartInColliders returns a distance==0 hit.

        [UnityTest]
        public IEnumerator Projectile_DoesNotDetonate_WhenSpawnedOverlappingObstacle()
        {
            // Force queriesStartInColliders ON to mirror project settings.
            bool prev = Physics2D.queriesStartInColliders;
            Physics2D.queriesStartInColliders = true;
            try
            {
                // Big obstacle whose centre overlaps the projectile spawn point.
                _obstacle   = CreateObstacle(Vector3.zero, LayerWorld, new Vector2(2f, 2f));
                _projectile = CreateProjectile(Vector3.zero, Vector2.right, speed: 30f, lifetime: 1f, range: 10f);

                // Give the projectile a few FixedUpdates. If the start-inside guard works,
                // it must keep moving (and eventually leave the obstacle), not detonate at t≈0.
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();

                Assert.IsTrue(_projectile != null && _projectile.activeInHierarchy,
                    "Projectile must NOT detonate on its first FixedUpdate when the sweep " +
                    "starts overlapping a collider (queriesStartInColliders edge case).");
                // It should also have moved forward — not be stuck at origin.
                Assert.Greater(_projectile.transform.position.x, 0.05f,
                    "Projectile must continue advancing despite the start-inside overlap.");
            }
            finally
            {
                Physics2D.queriesStartInColliders = prev;
            }
        }

        [UnityTest]
        public IEnumerator Projectile_DoesNotDetonate_WhenSpawnedInsideCasterCollider()
        {
            // Simulates the production case: player has a collider on the Player layer,
            // projectile spawns at the player's position. The projectile's own layer is
            // Projectile (10), the player's is Player (8). Player is NOT in targetLayers
            // nor ObstacleLayers, so it must be ignored entirely — even with start-inside.
            bool prev = Physics2D.queriesStartInColliders;
            Physics2D.queriesStartInColliders = true;
            try
            {
                const int LayerPlayer = 8;
                _obstacle = CreateObstacle(Vector3.zero, LayerPlayer, new Vector2(1f, 1f));
                _projectile = CreateProjectile(Vector3.zero, Vector2.right, speed: 30f,
                    lifetime: 1f, range: 10f, targets: 1 << LayerNPC);

                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();

                Assert.IsTrue(_projectile != null && _projectile.activeInHierarchy,
                    "Projectile must ignore the caster's own collider on spawn.");
            }
            finally
            {
                Physics2D.queriesStartInColliders = prev;
            }
        }

        [UnityTest]
        public IEnumerator Projectile_StillHitsObstacle_Normally_AfterStartInsideGuard()
        {
            // Sanity: the start-inside guard must not break normal collisions.
            // Projectile spawns clear of any collider, then encounters one mid-flight.
            bool prev = Physics2D.queriesStartInColliders;
            Physics2D.queriesStartInColliders = true;
            try
            {
                _obstacle   = CreateObstacle(new Vector3(2f, 0f, 0f), LayerWorld, new Vector2(1f, 4f));
                _projectile = CreateProjectile(Vector3.zero, Vector2.right, speed: 30f);

                float timeout = 0.5f;
                float elapsed = 0f;
                while (_projectile != null && _projectile.activeInHierarchy && elapsed < timeout)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                Assert.IsTrue(_projectile == null || !_projectile.activeInHierarchy,
                    "Start-inside guard must not prevent normal mid-flight collisions.");
            }
            finally
            {
                Physics2D.queriesStartInColliders = prev;
            }
        }

        [UnityTest]
        public IEnumerator Projectile_DetonatesOnObstacle_AfterExitingSpawnOverlap()
        {
            // Projectile spawns overlapping obstacle A, must clear it without exploding,
            // then must explode on obstacle B which is downstream.
            bool prev = Physics2D.queriesStartInColliders;
            Physics2D.queriesStartInColliders = true;
            try
            {
                // A: overlaps spawn
                var overlapping = CreateObstacle(Vector3.zero, LayerWorld, new Vector2(0.8f, 0.8f));
                // B: downstream
                var downstream  = CreateObstacle(new Vector3(3f, 0f, 0f), LayerWorld, new Vector2(1f, 4f));

                _projectile = CreateProjectile(Vector3.zero, Vector2.right, speed: 30f, lifetime: 1f, range: 10f);

                float timeout = 0.6f;
                float elapsed = 0f;
                Vector3 finalPos = Vector3.zero;
                while (_projectile != null && elapsed < timeout)
                {
                    if (!_projectile.activeInHierarchy)
                    {
                        finalPos = _projectile.transform.position;
                        break;
                    }
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                Assert.IsTrue(_projectile == null || !_projectile.activeInHierarchy,
                    "Projectile must detonate on the downstream obstacle B.");
                // It must have travelled past the overlapping obstacle (centre x=0, half-width 0.4)
                Assert.Greater(finalPos.x, 1.0f,
                    "Detonation point must be downstream of the spawn-overlap obstacle. " +
                    "Final x=" + finalPos.x);

                Object.Destroy(overlapping);
                Object.Destroy(downstream);
            }
            finally
            {
                Physics2D.queriesStartInColliders = prev;
            }
        }

        [UnityTest]
        public IEnumerator Projectile_AppliesDamage_OnlyOnce_PerHit()
        {
            // Verify a single overlap doesn't double-deduct HP across frames.
            _target     = CreateDamageableTarget(new Vector3(2f, 0f, 0f), hp: 1000);
            _projectile = CreateProjectile(Vector3.zero, Vector2.right, speed: 30f, targets: 1 << LayerNPC);

            yield return new WaitForSeconds(0.5f);

            var h = _target.GetComponent<Health>();
            int dmg = 1000 - h.CurrentHp;
            // Default damage is 20 (passed in CreateProjectile/Initialize)
            Assert.AreEqual(20, dmg,
                "Projectile must apply exactly its damage value once, not multiple times. Dealt: " + dmg);
        }
    }
}
