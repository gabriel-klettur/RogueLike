using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;
using Valkur.Gameplay.Save;
using Valkur.UI.HUD;
using TMPro;
using UnityEngine.UI;
using UnityEngine.TestTools;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Pins the save-load round-trip for <see cref="Experience"/>:
    ///   • Initialize fires OnStateChanged so subscribers (HUD, telemetry)
    ///     can refresh after a Restore.
    ///   • XpBarHUD bound BEFORE the restore still reflects the post-restore
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
            var hudGo    = new GameObject("XpBarHUD");
            try
            {
                var xp = playerGo.AddComponent<Experience>();

                // Build a minimal HUD wired with the canonical UI references.
                var fillGo = new GameObject("Fill", typeof(RectTransform));
                fillGo.transform.SetParent(hudGo.transform, false);
                var fill = fillGo.AddComponent<Image>();
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Horizontal;

                var bgGo = new GameObject("BG", typeof(RectTransform));
                bgGo.transform.SetParent(hudGo.transform, false);
                var bg = bgGo.AddComponent<Image>();

                var labelGo = new GameObject("Label", typeof(RectTransform));
                labelGo.transform.SetParent(hudGo.transform, false);
                var label = labelGo.AddComponent<TextMeshProUGUI>();

                var hud = hudGo.AddComponent<XpBarHUD>();
                hud.SetUIReferences(fill, bg, label);

                // Bind BEFORE the simulated load — this is the real-world ordering
                // (HUDBootstrap binds as soon as the player spawns; Save.Load runs
                // a few frames later).
                hud.Bind(xp);
                Assert.AreEqual(0f, hud.TargetFill,
                    "Sanity: brand-new Experience must show empty bar.");

                // Simulate the Restore: SaveService.Load → GameStateRestorer
                // → Experience.Initialize(150, 0).
                xp.Initialize(50, 0);

                Assert.That(hud.TargetFill, Is.EqualTo(0.5f).Within(0.02f),
                    "After Initialize the HUD must reflect the loaded XP. " +
                    "If this fails, OnStateChanged → RefreshAll wiring regressed.");
                StringAssert.Contains("Lvl 0", label.text);
            }
            finally
            {
                Object.DestroyImmediate(playerGo);
                Object.DestroyImmediate(hudGo);
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
