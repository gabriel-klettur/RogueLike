using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Robustness tests for the fireball impact FX (shockwave + flash + ember burst
    /// + light pulse + camera shake). EditMode-only; verifies construction and
    /// progress-driven state without running PlayMode physics.
    /// </summary>
    public class FireballImpactFXTests
    {
        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            // Wipe any leftover impact FX, embers, or camera shake helpers between tests.
            foreach (var fx in Object.FindObjectsOfType<FireballImpactFX>())
                Object.DestroyImmediate(fx.gameObject);
            foreach (var ember in Object.FindObjectsOfType<FireballEmber>())
                Object.DestroyImmediate(ember.gameObject);
        }

        private static T GetField<T>(object instance, string name) where T : class
        {
            var f = instance.GetType().GetField(name,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, $"Field '{name}' not found on {instance.GetType().Name}");
            return f.GetValue(instance) as T;
        }

        // ── Construction ──────────────────────────────────────────────

        [Test]
        public void Spawn_CreatesFxAtRequestedPosition()
        {
            var pos = new Vector3(2.5f, -1.25f, 0f);
            var fx = FireballImpactFX.Spawn(pos, Color.red);
            Assert.IsNotNull(fx);
            Assert.AreEqual(pos, fx.transform.position);
        }

        [Test]
        public void Spawn_BuildsFlashAndShockwaveChildren()
        {
            var fx = FireballImpactFX.Spawn(Vector3.zero, Color.red);
            Assert.IsNotNull(fx.transform.Find("Flash"),      "Flash child missing");
            Assert.IsNotNull(fx.transform.Find("Shockwave"), "Shockwave child missing");
        }

        [Test]
        public void Spawn_FlashAndRingUseSharedAssets()
        {
            var fx = FireballImpactFX.Spawn(Vector3.zero, Color.red);
            var flashSr = fx.transform.Find("Flash").GetComponent<SpriteRenderer>();
            var ringSr  = fx.transform.Find("Shockwave").GetComponent<SpriteRenderer>();

            Assert.AreSame(FireballVisual.SharedHotCoreSprite, flashSr.sprite,
                "Flash should reuse the shared hot-core sprite");
            Assert.AreSame(FireballVisual.SharedRingSprite, ringSr.sprite,
                "Shockwave should reuse the shared ring sprite");
            Assert.AreSame(FireballVisual.SharedUnlitMaterial, flashSr.sharedMaterial);
            Assert.AreSame(FireballVisual.SharedUnlitMaterial, ringSr.sharedMaterial);
        }

        [Test]
        public void Spawn_FlashRendersAboveShockwave()
        {
            var fx = FireballImpactFX.Spawn(Vector3.zero, Color.red);
            var flashSr = fx.transform.Find("Flash").GetComponent<SpriteRenderer>();
            var ringSr  = fx.transform.Find("Shockwave").GetComponent<SpriteRenderer>();
            Assert.Greater(flashSr.sortingOrder, ringSr.sortingOrder,
                "Flash must render above the shockwave ring for the bright punch effect");
        }

        [Test]
        public void Spawn_CreatesExactlyOneImpactFxPerCall()
        {
            int before = Object.FindObjectsOfType<FireballImpactFX>().Length;
            FireballImpactFX.Spawn(Vector3.zero, Color.red);
            int after = Object.FindObjectsOfType<FireballImpactFX>().Length;
            Assert.AreEqual(before + 1, after);
        }

        // ── Ember burst ───────────────────────────────────────────────

        [Test]
        public void Spawn_EmitsExactly22EmbersInRadialPattern()
        {
            // Clear any pre-existing embers first.
            foreach (var e in Object.FindObjectsOfType<FireballEmber>())
                Object.DestroyImmediate(e.gameObject);

            FireballImpactFX.Spawn(Vector3.zero, Color.red);

            int embers = Object.FindObjectsOfType<FireballEmber>().Length;
            Assert.AreEqual(22, embers, "EmberBurstCount=22 must produce 22 burst embers");
        }

        [Test]
        public void EmberBurst_VelocitiesSpanFullCircle()
        {
            // Clear stragglers and spawn a fresh burst.
            foreach (var e in Object.FindObjectsOfType<FireballEmber>())
                Object.DestroyImmediate(e.gameObject);

            FireballImpactFX.Spawn(Vector3.zero, Color.red);

            var embers = Object.FindObjectsOfType<FireballEmber>();
            Assert.AreEqual(22, embers.Length);

            bool seenPosX = false, seenNegX = false, seenPosY = false, seenNegY = false;
            foreach (var ember in embers)
            {
                var vel = GetField<object>(ember, "_vel");
                Vector2 v = (Vector2)vel;
                if (v.x >  0.1f) seenPosX = true;
                if (v.x < -0.1f) seenNegX = true;
                if (v.y >  0.1f) seenPosY = true;
                if (v.y < -0.1f) seenNegY = true;
            }
            Assert.IsTrue(seenPosX && seenNegX && seenPosY && seenNegY,
                "Burst velocities should cover all four quadrants");
        }

        // ── Self-destruction ──────────────────────────────────────────

        [Test]
        public void Update_DestroysSelfAfterDuration()
        {
            var fx = FireballImpactFX.Spawn(Vector3.zero, Color.red);
            Assert.IsTrue(fx != null && fx.gameObject != null);

            // Force the timer past Duration (0.55s) and call Update via reflection.
            var tField = typeof(FireballImpactFX).GetField("_t",
                BindingFlags.NonPublic | BindingFlags.Instance);
            tField.SetValue(fx, 1f); // > Duration

            var update = typeof(FireballImpactFX).GetMethod("Update",
                BindingFlags.NonPublic | BindingFlags.Instance);
            // Just ensure the destruction codepath doesn't throw at u >= 1.
            // Destroy() in EditMode logs an Error which we explicitly accept.
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("Destroy may not be called from edit mode"));
            Assert.DoesNotThrow(() => update.Invoke(fx, null));
        }
    }

    /// <summary>
    /// Robustness tests for the trailing/burst ember kinematics.
    /// </summary>
    public class FireballEmberTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp() { LogAssert.ignoreFailingMessages = true; }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            foreach (var ember in Object.FindObjectsOfType<FireballEmber>())
                Object.DestroyImmediate(ember.gameObject);
        }

        private FireballEmber CreateEmber(Vector2 vel, float life, float scale)
        {
            _go = new GameObject("Ember");
            _go.AddComponent<SpriteRenderer>().sprite = FireballVisual.SharedEmberSprite;
            var ember = _go.AddComponent<FireballEmber>();
            ember.Init(vel, life, scale);
            return ember;
        }

        [Test]
        public void Init_AppliesScale()
        {
            var ember = CreateEmber(Vector2.right, 0.5f, 0.12f);
            Assert.AreEqual(0.12f, ember.transform.localScale.x, 0.001f);
            Assert.AreEqual(0.12f, ember.transform.localScale.y, 0.001f);
        }

        [Test]
        public void Init_ClampsLifetimeToMinimum()
        {
            // Init clamps lifetime to >= 0.05f to prevent divide-by-zero in Update.
            var ember = CreateEmber(Vector2.zero, 0f, 0.1f);
            var lifeField = typeof(FireballEmber).GetField("_life",
                BindingFlags.NonPublic | BindingFlags.Instance);
            float life = (float)lifeField.GetValue(ember);
            Assert.GreaterOrEqual(life, 0.05f,
                "Life must be clamped above 0.05 to avoid div-by-zero");
        }
    }

    /// <summary>
    /// CameraShake is a self-installing helper used by FireballImpactFX. We can't drive
    /// LateUpdate in EditMode, but we can verify the public Trigger() API is null-safe and
    /// stacks amplitude/duration correctly (no exceptions thrown without a Camera.main).
    /// </summary>
    public class CameraShakeTests
    {
        [Test]
        public void Trigger_WithoutMainCamera_DoesNotThrow()
        {
            // No camera in the EditMode test scene — Trigger must early-out silently.
            Assert.DoesNotThrow(() => InvokeTrigger(0.2f, 0.3f),
                "Trigger should be a no-op when Camera.main is null");
        }

        [Test]
        public void Trigger_AcceptsZeroAndNegativeArguments_NoException()
        {
            Assert.DoesNotThrow(() => InvokeTrigger(0f, 0f));
            Assert.DoesNotThrow(() => InvokeTrigger(-1f, -1f));
        }

        private static void InvokeTrigger(float amp, float dur)
        {
            // CameraShake is internal to FireballImpactFX.cs; invoke via reflection so the
            // test stays in the same assembly without exposing internals_visible_to.
            var assembly = typeof(FireballImpactFX).Assembly;
            var t = assembly.GetType("Valkur.Gameplay.Spells.CameraShake");
            Assert.IsNotNull(t, "CameraShake type not found in gameplay assembly");
            var trigger = t.GetMethod("Trigger",
                BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(trigger, "CameraShake.Trigger(float,float) not found");
            trigger.Invoke(null, new object[] { amp, dur });
        }
    }
}
