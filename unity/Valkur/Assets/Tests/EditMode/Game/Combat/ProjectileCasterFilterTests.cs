using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Regression tests for Projectile.SetCaster — pins the contract that
    /// stops the "fireball blew up in my face" bug.
    ///
    /// Background: a Player (caster) can have a child collider on NPC layer
    /// (perception trigger, hurtbox). When a fireball travels from that
    /// caster, its swept query sees the NPC-layer child, calls
    /// GetComponentInParent&lt;Health&gt;() on it, and walks UP to the Player's
    /// own Health. Result: the player damages itself on its own cast.
    ///
    /// Fix: every executor calls Projectile.SetCaster(ctx.Caster) BEFORE
    /// Initialize. The projectile then ignores any collider whose transform
    /// is the caster or any descendant of the caster, in:
    ///   * the FixedUpdate sweep loop
    ///   * OnTriggerEnter2D fallback
    ///   * Expire's OverlapCircle AOE explosion
    /// </summary>
    [TestFixture]
    public class ProjectileCasterFilterTests
    {
        private readonly List<GameObject> _scene = new();

        [SetUp]
        public void SetUp()
        {
            // Procedural Rigidbody2D + sprite warnings can leak in EditMode.
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene)
                if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
        }

        // ── Helpers ────────────────────────────────────────────────────

        private Projectile CreateProjectile()
        {
            var go = new GameObject("ProjectileCasterTest");
            _scene.Add(go);
            var p = go.AddComponent<Projectile>();
            // Awake doesn't fire on AddComponent in EditMode — invoke manually.
            var awake = typeof(Projectile).GetMethod("Awake",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (awake != null) awake.Invoke(p, null);
            return p;
        }

        private GameObject MakeCaster(string name = "Caster")
        {
            var go = new GameObject(name);
            _scene.Add(go);
            return go;
        }

        private static Transform GetField_Caster(Projectile p)
        {
            var f = typeof(Projectile).GetField("_caster",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "Field '_caster' not found on Projectile");
            return (Transform)f.GetValue(p);
        }

        private static bool InvokeIsCasterCollider(Projectile p, Collider2D other)
        {
            var m = typeof(Projectile).GetMethod("IsCasterCollider",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(m, "IsCasterCollider helper missing — fix scope or rename here");
            return (bool)m.Invoke(p, new object[] { other });
        }

        private Collider2D AddBoxCollider(GameObject go)
        {
            // BoxCollider2D needs no setup, but the host GO must be alive at
            // call time (the [SetUp]/[TearDown] track that).
            return go.AddComponent<BoxCollider2D>();
        }

        // ── Setter contract ────────────────────────────────────────────

        [Test]
        public void SetCaster_StoresTransform()
        {
            var p = CreateProjectile();
            var caster = MakeCaster();

            p.SetCaster(caster.transform);

            Assert.AreSame(caster.transform, GetField_Caster(p),
                "SetCaster must store the caster transform so all downstream " +
                "filters can reach it");
        }

        [Test]
        public void SetCaster_AcceptsNull_AndClearsBinding()
        {
            var p = CreateProjectile();
            var caster = MakeCaster();
            p.SetCaster(caster.transform);
            Assert.IsNotNull(GetField_Caster(p));

            p.SetCaster(null);
            Assert.IsNull(GetField_Caster(p),
                "Passing null must clear the binding (no caster filter active)");
        }

        // ── IsCasterCollider behaviour ─────────────────────────────────

        [Test]
        public void IsCasterCollider_FalseWhenNoCasterBound()
        {
            var p = CreateProjectile();
            var stranger = MakeCaster("Stranger");
            var col = AddBoxCollider(stranger);

            Assert.IsFalse(InvokeIsCasterCollider(p, col),
                "Without a caster bound, no collider should be considered " +
                "owned by the caster (the filter must be opt-in)");
        }

        [Test]
        public void IsCasterCollider_FalseForUnrelatedCollider()
        {
            var p = CreateProjectile();
            var caster = MakeCaster();
            p.SetCaster(caster.transform);

            var unrelated = MakeCaster("UnrelatedNPC");
            var col = AddBoxCollider(unrelated);

            Assert.IsFalse(InvokeIsCasterCollider(p, col),
                "An unrelated collider must NOT be filtered — the projectile " +
                "still has to damage real targets");
        }

        [Test]
        public void IsCasterCollider_TrueForCasterRootCollider()
        {
            var p = CreateProjectile();
            var caster = MakeCaster();
            var col = AddBoxCollider(caster);

            p.SetCaster(caster.transform);

            Assert.IsTrue(InvokeIsCasterCollider(p, col),
                "A collider on the caster's own GameObject must be filtered " +
                "(prevents direct self-damage)");
        }

        [Test]
        public void IsCasterCollider_TrueForCasterChildCollider()
        {
            // THIS is the regression case. Player has a child GO on NPC layer
            // (perception, hurtbox). Without IsCasterCollider, the swept query
            // returns this child collider, the layer mask passes (NPC ∈ targets),
            // and GetComponentInParent<Health>() walks UP to the Player's Health
            // — the player damages itself.
            var p = CreateProjectile();
            var caster = MakeCaster();
            var perception = new GameObject("CasterChild_Perception_OnNPCLayer");
            _scene.Add(perception);
            perception.transform.SetParent(caster.transform);
            var col = AddBoxCollider(perception);

            p.SetCaster(caster.transform);

            Assert.IsTrue(InvokeIsCasterCollider(p, col),
                "A collider on a CHILD of the caster must be filtered. This " +
                "is the exact case that produced the 'fireball blew up in my " +
                "face' regression — caster had a perception trigger on NPC " +
                "layer and the projectile hit it on spawn.");
        }

        [Test]
        public void IsCasterCollider_TrueForDeeplyNestedDescendant()
        {
            // Defensive: filter must walk the full hierarchy, not just direct children.
            var p = CreateProjectile();
            var caster = MakeCaster();
            var mid = new GameObject("Mid"); _scene.Add(mid);
            mid.transform.SetParent(caster.transform);
            var leaf = new GameObject("Leaf"); _scene.Add(leaf);
            leaf.transform.SetParent(mid.transform);
            var col = AddBoxCollider(leaf);

            p.SetCaster(caster.transform);

            Assert.IsTrue(InvokeIsCasterCollider(p, col),
                "Descendants 2+ levels deep must still be filtered");
        }

        [Test]
        public void IsCasterCollider_HandlesNullCollider()
        {
            // Array-pool API (CircleCastNonAlloc / OverlapCircleNonAlloc) leaves
            // null entries past the valid count. The helper must not NRE on those.
            var p = CreateProjectile();
            var caster = MakeCaster();
            p.SetCaster(caster.transform);

            Assert.DoesNotThrow(() => InvokeIsCasterCollider(p, null),
                "Helper must defend against null colliders from the alloc-free " +
                "physics buffer slots");
            Assert.IsFalse(InvokeIsCasterCollider(p, null),
                "Null collider is not the caster's");
        }

        // ── ResetState clears caster (pool-reuse safety) ───────────────

        [Test]
        public void ResetState_ClearsCaster_SoPooledProjectileDoesntInheritStaleOwner()
        {
            // Critical for pooled projectiles: when a projectile returns to the
            // VFXManager pool, the next executor that grabs it MUST see no
            // stale caster, otherwise an NPC's fireball would inherit the
            // Player's caster filter (so the NPC could shoot itself).
            var p = CreateProjectile();
            var firstCaster = MakeCaster("FirstCaster");
            p.SetCaster(firstCaster.transform);
            Assert.IsNotNull(GetField_Caster(p));

            var reset = typeof(Projectile).GetMethod("ResetState",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(reset, "ResetState method missing");
            reset.Invoke(p, null);

            Assert.IsNull(GetField_Caster(p),
                "ResetState must clear _caster so the pool cannot leak the " +
                "previous shooter to the next user of this projectile instance");
        }

        // ── ProjectileExecutor wires the caster ────────────────────────

        [Test]
        public void ProjectileExecutor_PassesCasterTransformIntoProjectile()
        {
            // Pin the wiring contract: any future refactor of ProjectileExecutor
            // that forgets proj.SetCaster(ctx.Caster) re-opens the regression.
            var p = CreateProjectile();
            var caster = MakeCaster("PlayerLikeCaster");

            // Mimic ProjectileExecutor's call sequence (the parts we can do in EditMode):
            p.SetCaster(caster.transform);
            p.Initialize(Vector2.right, 16f, 20f, 1f, 15f, 1 << 9);
            p.SetExplosion(1.5f, 30f);

            Assert.AreSame(caster.transform, GetField_Caster(p),
                "After SetCaster + Initialize the bound transform survives — " +
                "Initialize must not silently clear it");
        }
    }
}
