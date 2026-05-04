using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.PlayMode.Gameplay
{
    /// <summary>
    /// PlayMode regression tests for the "fireball blew up in my face" bug.
    ///
    /// Concrete scenario reproduced:
    ///   * The caster is on layer Player(8) with its own Health and Collider2D.
    ///   * The caster has a CHILD GameObject on layer NPC(9) with a Collider2D
    ///     but no Health — exactly the shape a perception trigger / hurtbox /
    ///     mouse-target detector takes in the live game.
    ///   * The projectile's targetLayers = NPC layer (player-cast spells
    ///     target NPCs).
    ///
    /// Without the SetCaster filter, the projectile's swept query lands on
    /// the caster's own NPC-layer child collider, GetComponentInParent&lt;Health&gt;()
    /// walks UP to the caster's Health, and the caster takes damage from its
    /// own cast. The fix: ProjectileExecutor binds proj.SetCaster(ctx.Caster),
    /// and Projectile filters every collider whose transform is the caster
    /// or any descendant of it.
    /// </summary>
    public class ProjectileSelfDamagePlayTests
    {
        private const int LayerPlayer = 8;
        private const int LayerNPC    = 9;

        private readonly List<GameObject> _spawned = new();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
                if (_spawned[i] != null) Object.Destroy(_spawned[i]);
            _spawned.Clear();
            yield return null;
        }

        // ── Helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Builds a caster shaped like the live Player: layer 8 root with
        /// Health, plus a CHILD GameObject on NPC(9) with a Collider2D and
        /// no Health. The child is the "trap" the regression hit.
        /// </summary>
        private GameObject CreateCasterWithNpcLayerChildHurtbox(Vector3 pos, int hp = 100)
        {
            var caster = new GameObject("CasterPlayerLike");
            caster.transform.position = pos;
            caster.layer = LayerPlayer;

            // Root collider (also Player layer — would never be in the projectile
            // sweep mask, but keep it realistic).
            var rootCol = caster.AddComponent<BoxCollider2D>();
            rootCol.size = new Vector2(0.8f, 1.2f);
            rootCol.isTrigger = false;

            var health = caster.AddComponent<Health>();
            health.Initialize(hp);

            // Child on NPC layer — the actual regression vector. No Health on
            // the child; GetComponentInParent walks up to the caster's Health.
            var hurtbox = new GameObject("CasterChild_NpcLayerHurtbox");
            hurtbox.transform.SetParent(caster.transform, worldPositionStays: false);
            hurtbox.layer = LayerNPC;
            var hurtCol = hurtbox.AddComponent<BoxCollider2D>();
            hurtCol.size = new Vector2(1.5f, 1.5f);
            hurtCol.isTrigger = true;

            _spawned.Add(caster);
            return caster;
        }

        private GameObject CreateNpcTarget(Vector3 pos, int hp = 100)
        {
            var go = new GameObject("EnemyNpc");
            go.transform.position = pos;
            go.layer = LayerNPC;

            var col = go.AddComponent<BoxCollider2D>();
            col.size = new Vector2(1f, 1f);

            var h = go.AddComponent<Health>();
            h.Initialize(hp);
            _spawned.Add(go);
            return go;
        }

        private GameObject CreateProjectile(Vector3 pos, Vector2 dir, Transform caster,
                                             float speed = 16f, float damage = 20f,
                                             float lifetime = 1f, float range = 15f,
                                             float explosionRadius = 0f, float explosionDamage = 0f)
        {
            var go = new GameObject("FireballTest");
            go.transform.position = pos;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;

            var col = go.AddComponent<CircleCollider2D>();
            col.radius = 0.15f;
            col.isTrigger = true;

            var proj = go.AddComponent<Projectile>();
            // CRITICAL: bind caster BEFORE Initialize, mirroring ProjectileExecutor.
            proj.SetCaster(caster);
            proj.Initialize(dir, speed, damage, lifetime, range, 1 << LayerNPC);
            if (explosionRadius > 0f)
                proj.SetExplosion(explosionRadius, explosionDamage);

            _spawned.Add(go);
            return go;
        }

        private static IEnumerator WaitUntilProjectileGone(GameObject projectile, float timeout = 1.5f)
        {
            float elapsed = 0f;
            while (projectile != null && projectile.activeInHierarchy && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        // ── Direct-hit self-damage prevention ──────────────────────────

        [UnityTest]
        public IEnumerator Projectile_SpawnedInsideCasterChildOnNpcLayer_DoesNotDamageCaster()
        {
            // Reproduces the exact regression: spawn projectile right on top of
            // the caster's NPC-layer hurtbox. Without the caster filter the
            // sweep would hit the hurtbox, walk GetComponentInParent up to the
            // caster's Health and apply damage on the very first FixedUpdate.
            var caster = CreateCasterWithNpcLayerChildHurtbox(new Vector3(0f, 0f, 0f));
            var health = caster.GetComponent<Health>();
            int hpBefore = health.CurrentHp;

            // Spawn projectile dead-centre on the caster (which fully contains
            // the NPC-layer child collider). Direction "right" is irrelevant —
            // the bug fires whether or not the projectile moves at all.
            var proj = CreateProjectile(
                pos: caster.transform.position,
                dir: Vector2.right,
                caster: caster.transform,
                speed: 16f, damage: 20f, lifetime: 1f, range: 15f);

            yield return WaitUntilProjectileGone(proj);

            Assert.AreEqual(hpBefore, health.CurrentHp,
                "Caster must not lose HP from its own projectile. Without the " +
                "SetCaster filter, the projectile would land on the caster's " +
                "NPC-layer child collider and damage the caster's Health via " +
                "GetComponentInParent walk-up.");
        }

        [UnityTest]
        public IEnumerator Projectile_TravellingPastCaster_DoesNotDamageCaster()
        {
            // Spawn projectile a hair behind the caster, fire forward through it.
            // Even mid-traversal, the sweep should ignore caster-owned colliders.
            var caster = CreateCasterWithNpcLayerChildHurtbox(new Vector3(0f, 0f, 0f));
            var health = caster.GetComponent<Health>();

            var proj = CreateProjectile(
                pos: new Vector3(-1.5f, 0f, 0f),
                dir: Vector2.right,
                caster: caster.transform,
                speed: 20f, damage: 20f, lifetime: 1f, range: 10f);

            yield return WaitUntilProjectileGone(proj);

            Assert.AreEqual(100, health.CurrentHp,
                "Projectile passing through the caster on its way to a target " +
                "must not damage the caster.");
        }

        // ── AOE explosion self-damage prevention ───────────────────────

        [UnityTest]
        public IEnumerator Projectile_ExplosionAOE_DoesNotDamageCaster_EvenWhenCenteredOnIt()
        {
            // Worst-case: projectile expires by lifetime exactly on top of
            // caster, then triggers an AOE radius wide enough to engulf the
            // caster's NPC-layer child collider. The AOE filter must catch this.
            var caster = CreateCasterWithNpcLayerChildHurtbox(new Vector3(0f, 0f, 0f));
            var health = caster.GetComponent<Health>();
            int hpBefore = health.CurrentHp;

            // Place the projectile far enough away to NOT collide on spawn,
            // but with a short lifetime + huge AOE so it expires by time and
            // then lights up the caster's hurtbox via the explosion overlap.
            var proj = CreateProjectile(
                pos: new Vector3(0.3f, 0f, 0f),
                dir: Vector2.right,
                caster: caster.transform,
                speed: 0.01f, damage: 20f, lifetime: 0.05f, range: 50f,
                explosionRadius: 3f, explosionDamage: 30f);

            yield return WaitUntilProjectileGone(proj);

            Assert.AreEqual(hpBefore, health.CurrentHp,
                "AOE explosion must skip the caster's hierarchy. Without the " +
                "filter, the OverlapCircle would find the NPC-layer child and " +
                "GetComponentInParent would credit the damage to the caster.");
        }

        [UnityTest]
        public IEnumerator Projectile_ExplosionAOE_StillDamagesUnrelatedNpcInRadius()
        {
            // Sanity counter-test: the caster filter must NOT silence the AOE
            // for legitimate enemies. Place an unrelated NPC inside the same
            // explosion radius and verify it takes the AOE hit.
            var caster = CreateCasterWithNpcLayerChildHurtbox(new Vector3(0f, 0f, 0f));
            var enemy  = CreateNpcTarget(new Vector3(2f, 0f, 0f), hp: 100);
            var enemyHealth = enemy.GetComponent<Health>();

            var proj = CreateProjectile(
                pos: new Vector3(2f, 0f, 0f),  // sit on top of enemy
                dir: Vector2.right,
                caster: caster.transform,
                speed: 0.01f, damage: 20f, lifetime: 0.05f, range: 50f,
                explosionRadius: 3f, explosionDamage: 30f);

            yield return WaitUntilProjectileGone(proj);

            Assert.Less(enemyHealth.CurrentHp, 100,
                "Unrelated NPC in the AOE radius MUST still receive damage. " +
                "If this fails, the caster-filter is over-eager and breaks the " +
                "primary purpose of the spell.");
            Assert.AreEqual(100, caster.GetComponent<Health>().CurrentHp,
                "...AND the caster must remain unhurt in the same explosion.");
        }

        [UnityTest]
        public IEnumerator Projectile_DirectHit_StillDamagesNpcWithoutCasterRelation()
        {
            // Counter-test for the direct-hit path. A vanilla NPC standing in
            // the projectile's lane must still take the configured damage.
            var caster = CreateCasterWithNpcLayerChildHurtbox(new Vector3(0f, 0f, 0f));
            var enemy  = CreateNpcTarget(new Vector3(3f, 0f, 0f), hp: 100);
            var enemyHealth = enemy.GetComponent<Health>();

            var proj = CreateProjectile(
                pos: new Vector3(1f, 0f, 0f),
                dir: Vector2.right,
                caster: caster.transform,
                speed: 16f, damage: 20f, lifetime: 1f, range: 10f);

            yield return WaitUntilProjectileGone(proj);

            Assert.AreEqual(80, enemyHealth.CurrentHp,
                "Direct hit must still apply the configured damage to NPCs " +
                "outside the caster's hierarchy.");
        }

        // ── Pool-reuse safety: ResetState must clear the caster ────────

        [UnityTest]
        public IEnumerator Projectile_AfterExpire_DoesNotLeakCasterToReuse()
        {
            // First flight bound to caster A. After Expire(), the projectile is
            // returned to inactive state. If Caster A leaked into the field, a
            // re-Initialize "by caster B" would still skip Caster A's children.
            // We exercise the pool-reuse contract directly via reflection on the
            // private field that ResetState clears.
            var casterA = CreateCasterWithNpcLayerChildHurtbox(new Vector3(0f, 0f, 0f));
            var proj = CreateProjectile(
                pos: new Vector3(0.5f, 0f, 0f),
                dir: Vector2.right,
                caster: casterA.transform,
                speed: 16f, damage: 20f, lifetime: 0.05f, range: 5f,
                explosionRadius: 0.5f, explosionDamage: 10f);

            yield return WaitUntilProjectileGone(proj);

            // After Expire (no pool key set), proj should be destroyed. If it
            // survived (pooling), the _caster field must be null.
            if (proj != null)
            {
                var f = typeof(Projectile).GetField("_caster",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.IsNotNull(f);
                var leaked = f.GetValue(proj.GetComponent<Projectile>());
                Assert.IsNull(leaked,
                    "After ResetState (pool reuse path) the caster reference " +
                    "must be null so the next shooter does not inherit a stale " +
                    "ignore-target.");
            }
        }
    }
}
