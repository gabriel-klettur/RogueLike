using NUnit.Framework;
using UnityEngine;
using Valkur.UI.HUD;

namespace Valkur.Tests.EditMode.UI.HUD.Music
{
    /// <summary>The resonance's analyser, fed synthetic signals.</summary>
    public class MusicSpectrumTests
    {
        private static float[] Sine(float hz, float amplitude, int n, int rate)
        {
            var s = new float[n];
            for (int i = 0; i < n; i++) s[i] = amplitude * Mathf.Sin(2f * Mathf.PI * hz * i / rate);
            return s;
        }

        private static int Tallest(MusicSpectrum sp)
        {
            int best = 0;
            for (int b = 1; b < sp.Bands; b++) if (sp.Levels[b] > sp.Levels[best]) best = b;
            return best;
        }

        [TestCase(110f)]
        [TestCase(440f)]
        [TestCase(3000f)]
        public void APureTone_LightsTheBandThatContainsIt(float hz)
        {
            var sp = new MusicSpectrum(28);
            sp.SetSampleRate(48000);
            var s = Sine(hz, 0.5f, sp.Size, 48000);
            for (int i = 0; i < 10; i++) sp.Analyse(s, s.Length, 1f / 30f);
            int band = Tallest(sp);
            float centre = sp.BandCentreHz(band);
            // Log bands: the tone's band centre is within a band's width (about 22 % here).
            Assert.That(Mathf.Abs(Mathf.Log(centre / hz)), Is.LessThan(0.35f),
                $"{hz} Hz lit band {band} centred at {centre:0} Hz.");
            Assert.Greater(sp.Levels[band], 0.8f);
        }

        [Test]
        public void AQuietSong_FillsTheBars_AsWellAsALoudOne()
        {
            // The automatic gain is what makes a ballad as readable as a battle theme.
            var loud = new MusicSpectrum(28);
            var quiet = new MusicSpectrum(28);
            var a = Sine(440f, 0.5f, loud.Size, 48000);
            var b = Sine(440f, 0.01f, quiet.Size, 48000);
            for (int i = 0; i < 20; i++)
            {
                loud.Analyse(a, a.Length, 1f / 30f);
                quiet.Analyse(b, b.Length, 1f / 30f);
            }
            Assert.AreEqual(Tallest(loud), Tallest(quiet));
            Assert.That(quiet.Levels[Tallest(quiet)], Is.EqualTo(loud.Levels[Tallest(loud)]).Within(0.05f));
        }

        [Test]
        public void Silence_LetsTheBarsFall_ToNothing()
        {
            var sp = new MusicSpectrum(16);
            var s = Sine(440f, 0.5f, sp.Size, 48000);
            for (int i = 0; i < 5; i++) sp.Analyse(s, s.Length, 1f / 30f);
            Assert.Greater(sp.Levels[Tallest(sp)], 0.5f);
            for (int i = 0; i < 60; i++) sp.Analyse(null, 0, 1f / 30f);
            for (int b = 0; b < sp.Bands; b++) Assert.AreEqual(0f, sp.Levels[b], 1e-4f);
        }

        [Test]
        public void TheFftSize_MustBeAPowerOfTwo()
        {
            Assert.Throws<System.ArgumentException>(() => new MusicSpectrum(8, 1000));
        }
    }
}
