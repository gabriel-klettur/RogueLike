using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Infrastructure;
using Valkur.UI.HUD;
using Valkur.Tests.Support;

namespace Valkur.Tests.EditMode.UI.HUD.Music
{
    /// <summary>
    /// The shipped music catalog carries what the music panel and the beat clock read. For the
    /// life of the project all 24 tracks shipped with <c>bpm: 0</c>, so the metronome, the bar
    /// counter and the beat grid of the old panel never showed a real value — and nothing failed.
    /// </summary>
    [Category(TestCategories.ShippedData)]
    public class ShippedMusicCatalogTests
    {
        private static MusicTrackEntry[] Tracks()
        {
            var catalog = Resources.Load<AudioCatalogSO>("AudioCatalog");
            Assert.IsNotNull(catalog, "Resources/AudioCatalog.asset is missing.");
            Assert.That(catalog.Tracks.Length, Is.GreaterThan(0));
            return catalog.Tracks;
        }

        [Test]
        public void EveryTrack_HasAnAnalysedTempo()
        {
            foreach (var t in Tracks())
            {
                Assert.That(t.bpm, Is.InRange(50f, 220f), $"{t.id}: bpm {t.bpm}. Run tools/audio/analyze_music.py.");
                Assert.That(t.firstBeatOffsetSec, Is.GreaterThanOrEqualTo(0f), t.id);
            }
        }

        [Test]
        public void EveryTrack_HasItsBeatOnsets_InOrder()
        {
            foreach (var t in Tracks())
            {
                Assert.IsNotNull(t.beatTimes, t.id);
                Assert.That(t.beatTimes.Length, Is.GreaterThan(32), t.id);
                for (int i = 1; i < t.beatTimes.Length; i++)
                    Assert.Greater(t.beatTimes[i], t.beatTimes[i - 1], $"{t.id}: beat {i} is not after beat {i - 1}.");
            }
        }

        [Test]
        public void EveryTrack_HasABakedEnvelope_WithShape()
        {
            foreach (var t in Tracks())
            {
                var env = t.DecodeEnvelope();
                Assert.AreEqual(128, env.Length, $"{t.id}: envelope '{t.envelope}'.");
                float min = 1f, max = 0f;
                foreach (var v in env) { min = Mathf.Min(min, v); max = Mathf.Max(max, v); }
                // A flat envelope draws a bar, not a song.
                Assert.Greater(max - min, 0.2f, $"{t.id}: envelope is flat.");
            }
        }

        [Test]
        public void EveryTrack_ReadsAsAZone_ThePanelCanName()
        {
            var tracks = Tracks();
            foreach (var t in tracks)
            {
                Assert.IsNotEmpty(MusicTrackInfo.GroupOf(t.title), t.id);
                var pos = MusicTrackInfo.PositionInGroup(tracks, t.id);
                Assert.That(pos.x, Is.InRange(1, pos.y), t.id);
                if (!string.IsNullOrEmpty(t.key))
                    Assert.AreNotEqual(t.key, MusicTrackInfo.KeyInSpanish(t.key), $"{t.id}: key '{t.key}' has no Spanish name.");
            }
        }

        [Test]
        public void DecodeEnvelope_RefusesMalformedHex()
        {
            Assert.AreEqual(0, new MusicTrackEntry { envelope = "abc" }.DecodeEnvelope().Length);
            Assert.AreEqual(0, new MusicTrackEntry { envelope = "zz" }.DecodeEnvelope().Length);
            Assert.AreEqual(1f, new MusicTrackEntry { envelope = "ff" }.DecodeEnvelope()[0], 1e-6f);
        }

        [Test]
        public void MutedMusic_StillCarriesASignal_ForTheResonance()
        {
            // Measured on Unity 2022.3: a music source at volume 1e-5 is virtualised and its
            // filter receives exact zeros; at 1e-4 (-80 dBFS, below a 16-bit LSB for music at
            // 0.3 peak) the filter still sees the signal and the panel divides the volume back out.
            Assert.GreaterOrEqual(AudioManager.MusicSilenceFloor, 1e-4f);
            Assert.LessOrEqual(AudioManager.MusicSilenceFloor, 1e-3f, "Louder than -60 dBFS would be audible.");
        }
    }
}
