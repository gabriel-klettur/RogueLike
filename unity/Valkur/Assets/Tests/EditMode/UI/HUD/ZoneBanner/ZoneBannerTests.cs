using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.UI.HUD.ZoneBanner
{
    /// <summary>
    /// The zone banner's two decisions and its timeline. A coordinate zone is never announced,
    /// a place is announced once and again only after a real absence, and the name is spelled
    /// the way the pixel face can draw it.
    /// </summary>
    [TestFixture]
    public class ZoneBannerTests
    {
        private readonly List<GameObject> _spawned = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        [Test]
        public void GeneratedZones_AreCoordinates_NotPlaces()
        {
            Assert.IsTrue(ZoneBannerRules.IsGeneratedName("zone_100_50"));
            Assert.IsTrue(ZoneBannerRules.IsGeneratedName("zone_-50_0"));
            Assert.IsFalse(ZoneBannerRules.IsGeneratedName("Lobby"));
            Assert.IsFalse(ZoneBannerRules.IsGeneratedName("Forest"));
        }

        [Test]
        public void Humanize_SpellsItForThePixelFace()
        {
            Assert.That(ZoneBannerRules.Humanize("Lobby"), Is.EqualTo("LOBBY"));
            Assert.That(ZoneBannerRules.Humanize("house_interior_small.overlay.json"), Is.EqualTo("HOUSE INTERIOR SMALL"));
            Assert.That(ZoneBannerRules.Humanize("  dark-forest "), Is.EqualTo("DARK FOREST"));
            Assert.That(ZoneBannerRules.Humanize(""), Is.EqualTo(""));
        }

        [Test]
        public void APlace_IsAnnouncedOnce_AndAgainAfterARealAbsence()
        {
            var shown = new Dictionary<string, float>();
            Assert.IsTrue(ZoneBannerRules.ShouldAnnounce("Forest", 10f, shown));
            Assert.IsFalse(ZoneBannerRules.ShouldAnnounce("Forest", 20f, shown), "Crossing the border twice in a fight is not two arrivals.");
            Assert.IsTrue(ZoneBannerRules.ShouldAnnounce("Forest", 10f + ZoneBannerRules.RevisitAfterSeconds + 1f, shown));
            Assert.IsFalse(ZoneBannerRules.ShouldAnnounce("zone_100_50", 0f, shown));
            Assert.IsFalse(ZoneBannerRules.ShouldAnnounce("", 0f, shown));
        }

        [Test]
        public void TheBanner_RevealsLetterByLetter_HoldsAndGoes()
        {
            var go = new GameObject("banner");
            _spawned.Add(go);
            var hud = go.AddComponent<ZoneBannerHUD>();
            hud.EnsureBuilt();

            hud.Play("LOBBY");
            Assert.IsTrue(hud.IsPlaying);
            hud.Tick(ZoneBannerHUD.RevealSeconds * 0.5f);
            Assert.That(hud.ShownText.Length, Is.GreaterThan(0).And.LessThan(5), "Half way through the reveal, some letters.");
            hud.Tick(ZoneBannerHUD.RevealSeconds * 0.6f);
            Assert.That(hud.ShownText, Is.EqualTo("LOBBY"));
            Assert.IsTrue(hud.IsPlaying, "Held.");
            hud.Tick(ZoneBannerHUD.HoldSeconds + ZoneBannerHUD.FadeSeconds + 0.1f);
            Assert.IsFalse(hud.IsPlaying, "And gone.");
        }
    }
}
