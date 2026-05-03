using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;
using Valkur.Gameplay.HUD;

namespace Valkur.Tests.EditMode.Game.HUD
{
    /// <summary>
    /// Pins <see cref="BossHealthBarHUD"/>: BindToBoss attaches the HUD,
    /// Bind(null) hides it, IsActive reflects bound + alive state, and
    /// the bar auto-unbinds when the bound boss dies.
    /// </summary>
    [TestFixture]
    public class BossHealthBarHUDTests
    {
        private GameObject _hudGo;
        private BossHealthBarHUD _hud;
        private GameObject _bossGo;
        private BossPhaseController _phases;
        private Health _bossHealth;

        [SetUp]
        public void SetUp()
        {
            _hudGo = new GameObject("BossHealthBarHUD");
            _hud = _hudGo.AddComponent<BossHealthBarHUD>();
            _hud.EnsureBuilt(); // create the canvas in EditMode where Awake doesn't fire reliably.

            _bossGo = new GameObject("Boss");
            _bossGo.AddComponent<Rigidbody2D>();
            _bossHealth = _bossGo.AddComponent<Health>();
            _bossHealth.Initialize(200);
            _phases = _bossGo.AddComponent<BossPhaseController>();
            _phases.InitForTest(_bossHealth);
        }

        [TearDown]
        public void TearDown()
        {
            if (_hudGo != null) Object.DestroyImmediate(_hudGo);
            if (_bossGo != null) Object.DestroyImmediate(_bossGo);
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void BindToBoss_ActivatesHUD()
        {
            Assert.IsFalse(_hud.IsActive, "Sanity: HUD inactive before bind.");
            _hud.BindToBoss(_phases);
            Assert.IsTrue(_hud.IsActive, "After BindToBoss the HUD must report active.");
        }

        [Test]
        public void BindToNull_DeactivatesHUD()
        {
            _hud.BindToBoss(_phases);
            Assert.IsTrue(_hud.IsActive);

            _hud.BindToBoss(null);
            Assert.IsFalse(_hud.IsActive,
                "Bind(null) is the standard 'hide' path — HUD must release the bond.");
        }

        [Test]
        public void BossDeath_AutoUnbinds()
        {
            _hud.BindToBoss(_phases);
            Assert.IsTrue(_hud.IsActive);

            _bossHealth.TakeDamage(9999);
            Assert.IsFalse(_hud.IsActive,
                "When the bound boss dies, the HUD must auto-unbind so it doesn't " +
                "linger pointing at a corpse.");
        }

        [Test]
        public void RebindToDifferentBoss_ReleasesPreviousBond()
        {
            // Setup: two bosses, bind to first.
            _hud.BindToBoss(_phases);
            Assert.AreEqual(true, _hud.IsActive);

            var secondGo = new GameObject("Boss2");
            secondGo.AddComponent<Rigidbody2D>();
            var secondHealth = secondGo.AddComponent<Health>();
            secondHealth.Initialize(100);
            var secondPhases = secondGo.AddComponent<BossPhaseController>();
            secondPhases.InitForTest(secondHealth);

            try
            {
                _hud.BindToBoss(secondPhases);
                Assert.IsTrue(_hud.IsActive);

                // Killing the FIRST boss must NOT trigger an OnHpChanged
                // dispatch on the HUD — it should already be unbound.
                _bossHealth.TakeDamage(9999);
                Assert.IsTrue(_hud.IsActive,
                    "Re-binding must release the previous boss's events; first " +
                    "boss's death should not affect the HUD bound to the second.");
            }
            finally { Object.DestroyImmediate(secondGo); }
        }

        [Test]
        public void EnsureBuilt_CreatesCanvasOnce()
        {
            // Already built in SetUp. Verify the Canvas exists and a second
            // EnsureBuilt is a no-op (no extra Canvas).
            var canvases = _hudGo.GetComponentsInChildren<Canvas>(true);
            Assert.AreEqual(1, canvases.Length);

            _hud.EnsureBuilt();
            var canvasesAfter = _hudGo.GetComponentsInChildren<Canvas>(true);
            Assert.AreEqual(1, canvasesAfter.Length,
                "EnsureBuilt must be idempotent — calling it twice must not " +
                "spawn a second Canvas.");
        }
    }
}
