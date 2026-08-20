using System.IO;
using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Pins the wiring that makes the combo counter work at all.
    ///
    /// <see cref="ComboCounter"/> listens on <c>GameEvents.OnHitDealt</c>, but for
    /// a long time only <c>MeleeCombat</c> and explosions ever raised it — and the
    /// player attacks exclusively with spells. The result was a combo that could
    /// never start and a HUD that could never appear. These tests fail the moment
    /// a player-facing damage path stops reporting its hits.
    ///
    /// The scan is deliberately source-level: the damage paths need physics,
    /// pooling and a live projectile to exercise for real, which is PlayMode
    /// territory. What has to hold here is that the call exists at all.
    /// </summary>
    [TestFixture]
    public class SpellHitReportingTests
    {
        // Every damage path the player can drive directly. Continuous ground
        // effects (puddle, arcane flame) are left out on purpose: they tick on
        // their own and would keep a combo alive without the player attacking.
        private static readonly string[] PlayerDamagePaths =
        {
            "Gameplay/Spells/Projectiles/Projectile.cs",
            "Gameplay/Spells/Projectiles/BoomerangProjectile.cs",
            "Gameplay/Spells/Executors/SlashAttack.Damage.cs",
            "Gameplay/Spells/Executors/RegularSlashAttack.cs",
            "Gameplay/Spells/Executors/AreaExecutor.cs",
            "Gameplay/Spells/Executors/LightningExecutor.cs",
            "Gameplay/Spells/Executors/DashExecutor.cs",
            "Gameplay/Spells/Controllers/ConeBreathController.cs",
            "Gameplay/Spells/Controllers/LaserBeamController.cs",
        };

        private static string ScriptsRoot =>
            Path.Combine(Application.dataPath, "_Project", "Scripts");

        [Test]
        public void EveryPlayerDamagePathReportsItsHits()
        {
            foreach (var relative in PlayerDamagePaths)
            {
                string full = Path.Combine(ScriptsRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                Assert.IsTrue(File.Exists(full), $"Expected damage path missing: {relative}");

                string src = File.ReadAllText(full);
                Assert.IsTrue(src.Contains("FireHitDealt"),
                    $"{relative} deals damage the player caused but never raises " +
                    "GameEvents.FireHitDealt. ComboCounter listens on that event, so the " +
                    "combo (and the HUD that shows it) stays dead for this spell.");
            }
        }

        [Test]
        public void ComboCounterAcceptsHitsFromItsOwnHierarchy()
        {
            var player = new GameObject("Player");
            var combo = player.AddComponent<ComboCounter>();

            var hand = new GameObject("Hand");
            hand.transform.SetParent(player.transform, false);

            var stranger = new GameObject("Stranger");

            try
            {
                Assert.IsTrue(combo.IsOwnHit(player),
                    "A hit reported by the entity itself is obviously its own.");
                Assert.IsTrue(combo.IsOwnHit(hand),
                    "Spells are cast from child transforms (hands, muzzles) — those hits are " +
                    "still the player's, and rejecting them is what kept the combo at zero.");
                Assert.IsFalse(combo.IsOwnHit(stranger),
                    "Another entity's damage must never feed this combo.");
                Assert.IsFalse(combo.IsOwnHit(null));
            }
            finally
            {
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(stranger);
            }
        }

        [Test]
        public void TwoEnemiesInARowBuildAComboTheHudWillShow()
        {
            var player = new GameObject("Player");
            var combo = player.AddComponent<ComboCounter>();

            var first = new GameObject("Enemy1") { layer = 9 };   // NPC
            var second = new GameObject("Enemy2") { layer = 9 };

            try
            {
                combo.RegisterHit(first, 10f);
                Assert.AreEqual(1, combo.Current);

                combo.RegisterHit(second, 10f);
                Assert.AreEqual(2, combo.Current,
                    "Hitting a second enemy is the minimum streak the badge shows — if this " +
                    "ever drops back to 1 the HUD becomes invisible in normal play.");
                Assert.IsTrue(combo.IsActive);
                Assert.Greater(combo.WindowRemaining01, 0f,
                    "The drain bar reads this; at 0 the badge would show an empty timer.");
            }
            finally
            {
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }
    }
}
