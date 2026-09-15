using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Save;
using Valkur.UI.HUD;
using UnityEngine.TestTools;

namespace Valkur.Tests.EditMode.UI.Combat.Resources
{
    /// <summary>
    /// Pins the save-load round-trip for <see cref="Experience"/>:
    ///   • Initialize fires OnStateChanged so subscribers (HUD, telemetry)
    ///     can refresh after a Restore.
    ///   • The player panel bound BEFORE the restore still reflects the post-restore
    ///     state — this is the fix for the "XP doesn't persist visually"
    ///     bug where the bar stayed at 0/0 after loading a save.
    ///   • Full GameStateCollector → GameStateRestorer round-trip preserves
    ///     TotalXp + Level on a fresh Experience instance.
    /// </summary>
    [TestFixture]
    public class ExperiencePersistenceTests
    {
        [TearDown]
        public void TearDown()
        {
            GameEvents.Clear();
            EntityRegistry.UnregisterPlayer(EntityRegistry.Player);
        }

        [Test]
        public void Initialize_FiresOnStateChanged()
        {
            var go = new GameObject("Player");
            try
            {
                var xp = go.AddComponent<Experience>();
                int firedCount = 0;
                xp.OnStateChanged += () => firedCount++;

                xp.Initialize(150, 1);

                Assert.AreEqual(1, firedCount,
                    "Initialize must fire OnStateChanged exactly once so HUD/telemetry refresh.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void Initialize_PreservesXpAndLevel()
        {
            var go = new GameObject("Player");
            try
            {
                var xp = go.AddComponent<Experience>();
                xp.Initialize(150, 1);
                Assert.AreEqual(150, xp.TotalXp);
                Assert.AreEqual(1, xp.Level);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void HUDBoundBeforeRestore_RefreshesOnInitialize()
        {
            LogAssert.ignoreFailingMessages = true;

            var playerGo = new GameObject("Player");
            var canvasGo = new GameObject("HUDCanvas", typeof(RectTransform));
            PlayerHUD hud = null;
            try
            {
                var health = playerGo.AddComponent<Health>();
                health.Initialize(100);
                var xp = playerGo.AddComponent<Experience>();
                var canvas = canvasGo.AddComponent<Canvas>();

                // Bind BEFORE the simulated load — this is the real-world ordering
                // (the HUD binds as soon as the player spawns; Save.Load runs a few
                // frames later).
                hud = PlayerHUD.Create(canvas, health);
                Assert.AreEqual(0f, hud.XpBar.Target, 0.001f,
                    "Sanity: brand-new Experience must show an empty bar.");

                // Simulate the Restore: SaveService.Load -> GameStateRestorer
                // -> Experience.Initialize.
                xp.Initialize(50, 0);

                Assert.That(hud.XpBar.Target, Is.EqualTo(0.5f).Within(0.02f),
                    "After Initialize the HUD must reflect the loaded XP. " +
                    "If this fails, the OnStateChanged wiring regressed.");
                Assert.AreEqual("0", hud.Medallion.Label);
            }
            finally
            {
                if (hud != null) Object.DestroyImmediate(hud.gameObject);
                Object.DestroyImmediate(playerGo);
                Object.DestroyImmediate(canvasGo);
            }
        }

        [Test]
        public void HUDBoundBeforeRestore_ShowsTheRestoredLevel_NotTheBootOne()
        {
            // The "Lvl 0" defect: the old badge read the level once at bind time and afterwards
            // listened only for OnLevelUp, which Initialize does not raise. A save restored at
            // level 3 went on reading 0 until the next level-up.
            LogAssert.ignoreFailingMessages = true;

            var playerGo = new GameObject("Player");
            var canvasGo = new GameObject("HUDCanvas", typeof(RectTransform));
            PlayerHUD hud = null;
            try
            {
                var health = playerGo.AddComponent<Health>();
                health.Initialize(100);
                var xp = playerGo.AddComponent<Experience>();
                var canvas = canvasGo.AddComponent<Canvas>();
                hud = PlayerHUD.Create(canvas, health);

                xp.Initialize(xp.XpRequiredForLevel(3), 3);

                Assert.AreEqual(3, hud.Medallion.Level);
                Assert.AreEqual("3", hud.Medallion.Label);
            }
            finally
            {
                if (hud != null) Object.DestroyImmediate(hud.gameObject);
                Object.DestroyImmediate(playerGo);
                Object.DestroyImmediate(canvasGo);
            }
        }

        [Test]
        public void GameStateCollect_Then_Restore_PreservesXpAndLevel()
        {
            // Build the smallest viable player for the Collector contract:
            // Health (CurrentHp > 0) + Experience.
            var player = new GameObject("Player");
            player.tag = "Player";
            try
            {
                var hp = player.AddComponent<Health>();
                hp.Initialize(100);
                var xp = player.AddComponent<Experience>();
                xp.AddXp(150); // L0 + level-up cascade depending on default curve

                int savedXp = xp.TotalXp;
                int savedLv = xp.Level;

                EntityRegistry.RegisterPlayer(player);
                var data = GameStateCollector.Collect();
                Assert.IsNotNull(data, "Collect must produce data when Health is initialized.");
                Assert.AreEqual(savedXp, data.player.experience);
                Assert.AreEqual(savedLv, data.player.level);

                // Simulate a fresh play session: tear down Experience state
                // (level 0, total 0) then run Restore.
                xp.Initialize(0, 0);
                Assert.AreEqual(0, xp.TotalXp);

                GameStateRestorer.Restore(data);

                Assert.AreEqual(savedXp, xp.TotalXp,
                    "Restored TotalXp must match what was collected.");
                Assert.AreEqual(savedLv, xp.Level,
                    "Restored Level must match what was collected.");
            }
            finally
            {
                EntityRegistry.UnregisterPlayer(player);
                Object.DestroyImmediate(player);
            }
        }
    }
}
