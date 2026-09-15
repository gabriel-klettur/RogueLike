using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.UI.HUD.Music
{
    /// <summary>
    /// What the music panel SAYS about a track: zone, position in the zone's list, sigil, time,
    /// key in Spanish, and a title cut to fit. All pure.
    /// </summary>
    public class MusicTrackInfoTests
    {
        [TestCase("Pepitoria Main Theme", "Pepitoria")]
        [TestCase("Pepitoria Theme 13", "Pepitoria")]
        [TestCase("Desert Theme 2", "Desert")]
        [TestCase("Menu Intro", "Menu")]
        [TestCase("Solo", "Solo")]
        [TestCase("", "")]
        public void GroupOf_TakesTheZoneFromTheTitle(string title, string expected)
        {
            Assert.AreEqual(expected, MusicTrackInfo.GroupOf(title));
        }

        [Test]
        public void PositionInGroup_CountsOnlyTheSameZone_InCatalogOrder()
        {
            var tracks = new[]
            {
                new MusicTrackEntry { id = "menu", title = "Menu Intro" },
                new MusicTrackEntry { id = "a", title = "Pepitoria Main Theme" },
                new MusicTrackEntry { id = "d", title = "Desert Theme 2" },
                new MusicTrackEntry { id = "b", title = "Pepitoria Theme 2" },
                new MusicTrackEntry { id = "c", title = "Pepitoria Theme 3" },
            };
            Assert.AreEqual(new Vector2Int(2, 3), MusicTrackInfo.PositionInGroup(tracks, "b"));
            Assert.AreEqual(new Vector2Int(1, 1), MusicTrackInfo.PositionInGroup(tracks, "d"));
            Assert.AreEqual(Vector2Int.zero, MusicTrackInfo.PositionInGroup(tracks, "missing"));
        }

        [Test]
        public void SigilOf_GivesEachShippedZoneItsOwnPicture()
        {
            Assert.AreEqual(MusicSigil.Town, MusicTrackInfo.SigilOf("Pepitoria"));
            Assert.AreEqual(MusicSigil.Forest, MusicTrackInfo.SigilOf("forest"));
            Assert.AreEqual(MusicSigil.Desert, MusicTrackInfo.SigilOf("Desert"));
            Assert.AreEqual(MusicSigil.Crypt, MusicTrackInfo.SigilOf("Covetus"));
            Assert.AreEqual(MusicSigil.Note, MusicTrackInfo.SigilOf("Menu"));
        }

        [TestCase(0f, "0:00")]
        [TestCase(106.9f, "1:46")]
        [TestCase(-3f, "0:00")]
        [TestCase(3725f, "1:02:05")]
        public void FormatTime(float seconds, string expected)
        {
            Assert.AreEqual(expected, MusicTrackInfo.FormatTime(seconds));
        }

        [TestCase("A minor", "La menor")]
        [TestCase("F# major", "Fa# mayor")]
        [TestCase("C major", "Do mayor")]
        [TestCase("", "")]
        [TestCase("weird", "weird")]
        public void KeyInSpanish(string key, string expected)
        {
            Assert.AreEqual(expected, MusicTrackInfo.KeyInSpanish(key));
        }

        [Test]
        public void AKeyTheAnalysisIsUnsureOf_IsNotShown()
        {
            Assert.AreEqual(string.Empty, MusicTrackInfo.TrustedKey(new MusicTrackEntry { key = "C major", keyConfidence = 0.03f }));
            Assert.AreEqual("A minor", MusicTrackInfo.TrustedKey(new MusicTrackEntry { key = "A minor", keyConfidence = 0.25f }));
            Assert.AreEqual(string.Empty, MusicTrackInfo.TrustedKey(null));
        }

        [Test]
        public void FitToWidth_CutsWithAnEllipsis_OnlyWhenItMust()
        {
            System.Func<string, int> measure = s => s.Length * 4;
            Assert.AreEqual("ABCD", MusicTrackInfo.FitToWidth("ABCD", 16, measure));
            string cut = MusicTrackInfo.FitToWidth("ABCDEFGH", 24, measure);
            StringAssert.EndsWith("...", cut);
            Assert.LessOrEqual(measure(cut), 24);
        }

        [Test]
        public void PanelLabels_AreSpanish_AndSpellableInThePixelFace()
        {
            var glyphs = HudPixelFont.Glyphs(HudFontFace.Small);
            foreach (var label in new[] { MusicHudText.Idle, MusicHudText.Paused, MusicHudText.Muted })
                Assert.IsTrue(HudPixelFont.CanSpell(label, glyphs), $"'{label}' has a letter the pixel face cannot draw.");
            // The old panel said these in English in a Spanish game.
            foreach (var english in new[] { "No music", "no tempo", "paused", "Bar ", "Beat " })
            {
                Assert.AreNotEqual(english, MusicHudText.Idle);
                Assert.AreNotEqual(english, MusicHudText.Paused);
            }
        }
    }
}
