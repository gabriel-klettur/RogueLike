using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// EditMode robustness tests for <see cref="Projectile"/> (the runtime backing
    /// the fireball + every other projectile spell).
    /// Covers:
    ///   * Initialize wires speed / damage / lifetime / range correctly
    ///   * Initialize normalizes the direction vector
    ///   * Initialize sets sprite rotation from direction (atan2)
    ///   * SetExplosion / SetAcceleration / SetPoolKey / SetVFXColor / SetImpactPreset
    ///     setters round-trip through the private backing fields
    ///   * Multiple setter calls don't interfere with each other
    /// PlayMode-only behaviour (FixedUpdate sweep, OnTriggerEnter, AOE damage) is
    /// covered indirectly through the SpellCaster / ParticleProjectileVisual integration tests.
    /// </summary>
    public class ProjectileTests
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
            var go = new GameObject("ProjectileTest");
            _scene.Add(go);
            // Projectile has [RequireComponent(typeof(Rigidbody2D))] so it auto-adds RB.
            var p = go.AddComponent<Projectile>();
            // EditMode doesn't fire Awake on AddComponent — invoke it manually so
            // _rb is wired before Initialize is called.
            var awake = typeof(Projectile).GetMethod("Awake",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (awake != null) awake.Invoke(p, null);
            return p;
        }

        private static T GetField<T>(object instance, string name)
        {
            var f = instance.GetType().GetField(name,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, $"Field '{name}' not found on {instance.GetType().Name}");
            return (T)f.GetValue(instance);
        }

        // ── Initialize ─────────────────────────────────────────────────

        [Test]
        public void Initialize_WiresAllRuntimeValues()
        {
            var p = CreateProjectile();
            p.Initialize(Vector2.right, spd: 16f, dmg: 20f, life: 1.5f, rng: 12f, targets: 1 << 9);

            Assert.AreEqual(16f, GetField<float>(p, "speed"), 1e-4f);
            Assert.AreEqual(20f, GetField<float>(p, "damage"), 1e-4f);
            Assert.AreEqual(1.5f, GetField<float>(p, "lifetime"), 1e-4f);
            Assert.AreEqual(12f, GetField<float>(p, "range"), 1e-4f);
        }

        [Test]
        public void Initialize_NormalizesDirection()
        {
            var p = CreateProjectile();
            // Pass a non-unit direction; expect storage to be normalized so subsequent
            // velocity = dir * speed ends up at exactly `speed` magnitude.
            p.Initialize(new Vector2(3f, 4f), 10f, 1f, 1f, 1f, 0);

            var stored = GetField<Vector2>(p, "_direction");
            Assert.AreEqual(1f, stored.magnitude, 1e-4f, "_direction must be normalized");
            Assert.AreEqual(0.6f, stored.x, 1e-4f);
            Assert.AreEqual(0.8f, stored.y, 1e-4f);
        }

        [Test]
        public void Initialize_RotatesSpriteToFaceDirection()
        {
            var p = CreateProjectile();
            // Direction "up" → 90 degrees on Z.
            p.Initialize(Vector2.up, 10f, 1f, 1f, 1f, 0);
            Assert.AreEqual(90f, p.transform.eulerAngles.z, 1e-3f);

            // Direction "left" → 180 degrees.
            p.Initialize(Vector2.left, 10f, 1f, 1f, 1f, 0);
            Assert.AreEqual(180f, p.transform.eulerAngles.z, 1e-3f);
        }

        [Test]
        public void Initialize_StoresOriginAtCurrentTransformPosition()
        {
            var p = CreateProjectile();
            p.transform.position = new Vector3(5f, -3f, 0f);
            p.Initialize(Vector2.right, 10f, 1f, 1f, 1f, 0);

            var origin = GetField<Vector2>(p, "_origin");
            Assert.AreEqual(5f, origin.x, 1e-4f);
            Assert.AreEqual(-3f, origin.y, 1e-4f);
        }

        // ── Setters: round-trip via private fields ─────────────────────

        [Test]
        public void SetExplosion_StoresRadiusAndDamage()
        {
            var p = CreateProjectile();
            p.SetExplosion(radius: 1.5f, dmg: 30f);

            Assert.AreEqual(1.5f, GetField<float>(p, "_explosionRadius"), 1e-4f);
            Assert.AreEqual(30f, GetField<float>(p, "_explosionDamage"), 1e-4f);
        }

        [Test]
        public void SetAcceleration_StoresValue()
        {
            var p = CreateProjectile();
            p.SetAcceleration(5f);
            Assert.AreEqual(5f, GetField<float>(p, "_acceleration"), 1e-4f);
        }

        [Test]
        public void SetPoolKey_StoresKey()
        {
            var p = CreateProjectile();
            p.SetPoolKey("proj_fireball");
            Assert.AreEqual("proj_fireball", GetField<string>(p, "_poolKey"));
        }

        [Test]
        public void SetVFXColor_StoresColor()
        {
            var p = CreateProjectile();
            var c = new Color(0.2f, 0.4f, 0.6f, 0.8f);
            p.SetVFXColor(c);
            var stored = GetField<Color>(p, "_vfxColor");
            Assert.AreEqual(c, stored);
        }

        [Test]
        public void SetImpactPreset_StoresPresetName()
        {
            var p = CreateProjectile();
            p.SetImpactPreset("explosion_small");

            // The single-preset setter is now a convenience over the stack, so the stored
            // state is a list. An impact is built from several presets — flash, shockwave,
            // debris, smoke — and this setter is the one-layer case of that.
            CollectionAssert.AreEqual(
                new[] { "explosion_small" },
                GetField<System.Collections.Generic.List<string>>(p, "_impactPresets"));
        }

        [Test]
        public void SetImpactPresets_StoresTheWholeStackInOrder()
        {
            var p = CreateProjectile();
            p.SetImpactPresets(new System.Collections.Generic.List<string>
            {
                "fireball_impact_flash", "fireball_impact_shockwave", "fireball_impact_burst"
            });

            CollectionAssert.AreEqual(
                new[] { "fireball_impact_flash", "fireball_impact_shockwave", "fireball_impact_burst" },
                GetField<System.Collections.Generic.List<string>>(p, "_impactPresets"),
                "Order is draw order: the flash must land before the smoke that covers it.");
        }

        [Test]
        public void SetImpactPreset_AfterAStack_ReplacesItRatherThanAppending()
        {
            var p = CreateProjectile();
            p.SetImpactPresets(new System.Collections.Generic.List<string> { "a", "b", "c" });
            p.SetImpactPreset("solo");

            CollectionAssert.AreEqual(
                new[] { "solo" },
                GetField<System.Collections.Generic.List<string>>(p, "_impactPresets"),
                "Projectiles are pooled and reconfigured per shot; an appending setter would " +
                "accumulate every spell ever fired from that pool slot.");
        }

        [Test]
        public void SetImpactPreset_WithNullOrEmpty_LeavesNoStackBehind()
        {
            var p = CreateProjectile();
            p.SetImpactPresets(new System.Collections.Generic.List<string> { "leftover" });
            p.SetImpactPreset(null);

            Assert.IsEmpty(GetField<System.Collections.Generic.List<string>>(p, "_impactPresets"),
                "A spell with no impact preset must clear the previous shot's, not inherit it.");
        }

        // ── Multiple setters compose without interfering ───────────────

        [Test]
        public void Setters_DoNotInterfereWithEachOther()
        {
            // Mirrors how ProjectileExecutor wires up a fully-configured fireball:
            //   Initialize → SetImpactPreset → SetAcceleration → SetExplosion → SetPoolKey
            var p = CreateProjectile();
            p.Initialize(Vector2.right, 16f, 20f, 1f, 15f, 1 << 9);
            p.SetImpactPreset("explosion_small");
            p.SetAcceleration(2.5f);
            p.SetExplosion(1.5f, 30f);
            p.SetPoolKey("proj_fireball");

            Assert.AreEqual(16f, GetField<float>(p, "speed"), 1e-4f);
            Assert.AreEqual(20f, GetField<float>(p, "damage"), 1e-4f);
            Assert.AreEqual(1f,  GetField<float>(p, "lifetime"), 1e-4f);
            Assert.AreEqual(15f, GetField<float>(p, "range"), 1e-4f);
            CollectionAssert.AreEqual(
                new[] { "explosion_small" },
                GetField<System.Collections.Generic.List<string>>(p, "_impactPresets"));
            Assert.AreEqual(2.5f, GetField<float>(p, "_acceleration"), 1e-4f);
            Assert.AreEqual(1.5f, GetField<float>(p, "_explosionRadius"), 1e-4f);
            Assert.AreEqual(30f,  GetField<float>(p, "_explosionDamage"), 1e-4f);
            Assert.AreEqual("proj_fireball", GetField<string>(p, "_poolKey"));
        }

        // ── Defaults: fresh projectile has no AOE / acceleration ───────

        [Test]
        public void NewProjectile_HasZeroAccelerationAndExplosion()
        {
            // Defensive: a vanilla projectile must not accidentally accelerate or
            // explode if SetAcceleration / SetExplosion are never called.
            var p = CreateProjectile();
            Assert.AreEqual(0f, GetField<float>(p, "_acceleration"), 1e-4f);
            Assert.AreEqual(0f, GetField<float>(p, "_explosionRadius"), 1e-4f);
            Assert.AreEqual(0f, GetField<float>(p, "_explosionDamage"), 1e-4f);
        }

        // ── Rigidbody2D wiring (Awake) ─────────────────────────────────

        [Test]
        public void Awake_ConfiguresRigidbodyForGravitylessMovement()
        {
            var p = CreateProjectile();
            var rb = p.GetComponent<Rigidbody2D>();
            Assert.IsNotNull(rb, "Projectile requires a Rigidbody2D");
            Assert.AreEqual(0f, rb.gravityScale, "Projectiles must not be affected by gravity");
            Assert.IsTrue(rb.freezeRotation, "Projectiles control rotation manually via Initialize");
            Assert.AreEqual(CollisionDetectionMode2D.Continuous, rb.collisionDetectionMode,
                "Continuous CCD avoids tunneling at high speeds");
        }
    }
}
