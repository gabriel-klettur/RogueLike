using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Game.VFX
{
    /// <summary>
    /// Covers <see cref="BeamTextureLibrary"/>, the band textures that turned the laser from
    /// a coloured bar into light.
    ///
    /// A <see cref="LineRenderer"/> with no texture draws a hard-edged rectangle: constant
    /// alpha right up to the edge, where it stops. There is no falloff across the width and
    /// nothing to scroll along the length, which is why the beam read as flat.
    ///
    /// Textures are uploaded with <c>makeNoLongerReadable</c>, so the shape is asserted
    /// through the pure <see cref="BeamTextureLibrary.EvaluateAlpha"/> rather than a GPU
    /// read-back — the same approach as the particle texture library.
    /// </summary>
    [TestFixture]
    public class BeamTextureLibraryTests
    {
        // ── Caching and setup ────────────────────────────────────────────────────

        [Test]
        public void Get_SameKindAndSoftness_ReturnsTheCachedTexture()
        {
            var a = BeamTextureLibrary.Get(BeamTextureKind.Core, 0.25f);
            var b = BeamTextureLibrary.Get(BeamTextureKind.Core, 0.25f);

            Assert.IsNotNull(a);
            Assert.AreSame(a, b, "The library must cache, not regenerate per beam.");
        }

        [Test]
        public void Get_DifferentKinds_ReturnDistinctTextures()
        {
            Assert.AreNotSame(
                BeamTextureLibrary.Get(BeamTextureKind.Core, 0.25f),
                BeamTextureLibrary.Get(BeamTextureKind.Glow, 0.25f));
        }

        [Test]
        public void Get_WrapsRepeat_SoTheTextureCanTileAndScroll()
        {
            var tex = BeamTextureLibrary.Get(BeamTextureKind.Energy, 0.3f);

            Assert.AreEqual(TextureWrapMode.Repeat, tex.wrapMode,
                "Clamp would stretch one copy across the whole beam and make scrolling " +
                "impossible — repeat is the whole mechanism behind energy flow.");
            Assert.AreEqual(FilterMode.Bilinear, tex.filterMode,
                "The VFX layer is deliberately not point-filtered like the pixel-art world.");
        }

        [Test]
        public void Get_MarksTextureDontSave_SoItNeverLeaksIntoAScene()
        {
            Assert.AreEqual(HideFlags.DontSave,
                BeamTextureLibrary.Get(BeamTextureKind.Glow, 0.8f).hideFlags);
        }

        // ── Shape across the width ───────────────────────────────────────────────

        [Test]
        public void EvaluateAlpha_IsBrightestOnTheCentreLineAndFadesToTheEdges()
        {
            foreach (BeamTextureKind kind in new[] { BeamTextureKind.Core, BeamTextureKind.Glow, BeamTextureKind.Energy })
            {
                float centre = BeamTextureLibrary.EvaluateAlpha(kind, 0f, 0.5f, 0.4f);
                float mid    = BeamTextureLibrary.EvaluateAlpha(kind, 0.5f, 0.5f, 0.4f);
                float edge   = BeamTextureLibrary.EvaluateAlpha(kind, 0.98f, 0.5f, 0.4f);

                Assert.Greater(centre, mid, $"{kind}: the centre line must be the brightest part.");
                Assert.Greater(mid, edge, $"{kind}: alpha must keep falling toward the edge.");
            }
        }

        [Test]
        public void EvaluateAlpha_IsZeroBeyondTheEdge()
        {
            foreach (BeamTextureKind kind in new[] { BeamTextureKind.Core, BeamTextureKind.Glow, BeamTextureKind.Energy })
                Assert.AreEqual(0f, BeamTextureLibrary.EvaluateAlpha(kind, 1.2f, 0.5f, 0.4f), 1e-6f,
                    $"{kind} must not paint outside the band.");
        }

        [Test]
        public void EvaluateAlpha_CoreIsTighterThanGlow()
        {
            float core = BeamTextureLibrary.EvaluateAlpha(BeamTextureKind.Core, 0.6f, 0.5f, 0.4f);
            float glow = BeamTextureLibrary.EvaluateAlpha(BeamTextureKind.Glow, 0.6f, 0.5f, 0.4f);

            Assert.Less(core, glow,
                "The core is a hard bright line and the glow is the halo around it. If the core " +
                "spreads as wide as the glow the beam has no readable centre.");
        }

        [Test]
        public void EvaluateAlpha_GlowNeverReachesFullOpacity()
        {
            Assert.Less(BeamTextureLibrary.EvaluateAlpha(BeamTextureKind.Glow, 0f, 0.5f, 0.4f), 1f,
                "A glow that peaks at 1 is a second core, and the beam loses its layering.");
        }

        [Test]
        public void EvaluateAlpha_SofterMeansWider()
        {
            float hard = BeamTextureLibrary.EvaluateAlpha(BeamTextureKind.Core, 0.6f, 0.5f, 0f);
            float soft = BeamTextureLibrary.EvaluateAlpha(BeamTextureKind.Core, 0.6f, 0.5f, 1f);

            Assert.Greater(soft, hard, "Higher softness must carry more alpha further out.");
        }

        // ── Variation along the length ───────────────────────────────────────────

        [Test]
        public void EvaluateAlpha_EnergyVariesAlongTheBeam()
        {
            float a = BeamTextureLibrary.EvaluateAlpha(BeamTextureKind.Energy, 0f, 0.10f, 0.4f);
            float b = BeamTextureLibrary.EvaluateAlpha(BeamTextureKind.Energy, 0f, 0.35f, 0.4f);

            Assert.AreNotEqual(a, b,
                "Energy is the kind that scrolls. With no variation along the length, scrolling " +
                "it is invisible and the beam looks static however fast it moves.");
        }

        [Test]
        public void EvaluateAlpha_CoreAndGlowAreConstantAlongTheBeam()
        {
            foreach (BeamTextureKind kind in new[] { BeamTextureKind.Core, BeamTextureKind.Glow })
                Assert.AreEqual(
                    BeamTextureLibrary.EvaluateAlpha(kind, 0.2f, 0.10f, 0.4f),
                    BeamTextureLibrary.EvaluateAlpha(kind, 0.2f, 0.80f, 0.4f), 1e-6f,
                    $"{kind} must be uniform along its length, or tiling it would show seams " +
                    "as the beam grows and the tile count changes.");
        }

        [Test]
        public void EvaluateAlpha_EnergyTilesSeamlessly()
        {
            // The pattern is built from whole numbers of cycles, so t=0 and t=1 must match
            // or every tile boundary shows a visible step travelling along the beam.
            Assert.AreEqual(
                BeamTextureLibrary.EvaluateAlpha(BeamTextureKind.Energy, 0f, 0f, 0.4f),
                BeamTextureLibrary.EvaluateAlpha(BeamTextureKind.Energy, 0f, 1f, 0.4f), 1e-4f,
                "A seam at the wrap point is the classic tell of a scrolling texture.");
        }

        [Test]
        public void EvaluateAlpha_EnergyStaysPositiveAcrossItsLength()
        {
            // A pattern that dips to zero would break the beam into visibly separate dashes.
            for (int i = 0; i <= 20; i++)
            {
                float t = i / 20f;
                Assert.Greater(BeamTextureLibrary.EvaluateAlpha(BeamTextureKind.Energy, 0f, t, 0.4f), 0.2f,
                    $"Energy modulation must stay well above zero at t={t:0.00}; the beam is a " +
                    "continuous line with brightness travelling through it, not a dashed one.");
            }
        }
    }
}
