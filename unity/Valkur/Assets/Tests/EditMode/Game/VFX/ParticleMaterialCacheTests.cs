using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Game.VFX
{
    /// <summary>
    /// Covers <see cref="ParticleMaterialCache"/>, which replaced the old
    /// "new Material() per emitter" path in <c>ParticleEmitter.ConfigureRenderer</c>.
    ///
    /// Two regressions are locked down here: materials must be shared (SRP batching +
    /// no EditMode instance leak), and the surface must be set to Transparent — URP's
    /// particle shader ships as Opaque, which is why every non-additive preset used to
    /// render as a solid quad.
    /// </summary>
    [TestFixture]
    public class ParticleMaterialCacheTests
    {
        private static Texture2D Tex(ParticleTextureShape shape) =>
            ParticleTextureLibrary.Get(shape, 0.5f);

        [Test]
        public void Get_SameTextureAndBlend_ReturnsSharedInstance()
        {
            var tex = Tex(ParticleTextureShape.SoftDot);

            var first = ParticleMaterialCache.Get(tex, additive: false);
            var second = ParticleMaterialCache.Get(tex, additive: false);

            Assert.IsNotNull(first);
            Assert.AreSame(first, second, "emitters must share one material or SRP batching is lost");
        }

        [Test]
        public void Get_DifferentBlendMode_ReturnsDifferentMaterial()
        {
            var tex = Tex(ParticleTextureShape.SoftDot);

            var alpha = ParticleMaterialCache.Get(tex, additive: false);
            var additive = ParticleMaterialCache.Get(tex, additive: true);

            Assert.AreNotSame(alpha, additive);
        }

        [Test]
        public void Get_DifferentTexture_ReturnsDifferentMaterial()
        {
            var dot = ParticleMaterialCache.Get(Tex(ParticleTextureShape.SoftDot), additive: true);
            var spark = ParticleMaterialCache.Get(Tex(ParticleTextureShape.Spark), additive: true);

            Assert.AreNotSame(dot, spark);
        }

        [Test]
        public void Get_NullTexture_IsSupportedForTheLegacyQuad()
        {
            var mat = ParticleMaterialCache.Get(null, additive: false);

            Assert.IsNotNull(mat, "textureShape = None must still produce a usable material");
        }

        [Test]
        public void Get_AssignsTheTextureToTheShader()
        {
            var tex = Tex(ParticleTextureShape.Glow);
            var mat = ParticleMaterialCache.Get(tex, additive: true);

            bool assigned =
                (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") == tex) ||
                (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") == tex);

            Assert.IsTrue(assigned, "an unassigned _BaseMap is what made every particle a white square");
        }

        [Test]
        public void Get_MaterialIsTransparentAndDepthWriteIsOff()
        {
            var mat = ParticleMaterialCache.Get(Tex(ParticleTextureShape.SoftDot), additive: false);

            Assert.AreEqual(3000, mat.renderQueue, "particles must sort in the transparent queue");
            if (mat.HasProperty("_ZWrite"))
                Assert.AreEqual(0f, mat.GetFloat("_ZWrite"), 1e-4f);
            if (mat.HasProperty("_Surface"))
                Assert.AreEqual(1f, mat.GetFloat("_Surface"), 1e-4f, "1 = Transparent; URP defaults to Opaque");
        }

        [Test]
        public void Get_AdditiveMaterial_UsesSrcAlphaOneBlend()
        {
            var mat = ParticleMaterialCache.Get(Tex(ParticleTextureShape.Glow), additive: true);

            if (!mat.HasProperty("_SrcBlend")) Assert.Ignore("Fallback shader has no blend properties.");

            Assert.AreEqual((float)BlendMode.SrcAlpha, mat.GetFloat("_SrcBlend"), 1e-4f);
            Assert.AreEqual((float)BlendMode.One, mat.GetFloat("_DstBlend"), 1e-4f);
        }

        [Test]
        public void Get_AlphaMaterial_UsesStandardAlphaBlend()
        {
            var mat = ParticleMaterialCache.Get(Tex(ParticleTextureShape.Smoke), additive: false);

            if (!mat.HasProperty("_SrcBlend")) Assert.Ignore("Fallback shader has no blend properties.");

            Assert.AreEqual((float)BlendMode.SrcAlpha, mat.GetFloat("_SrcBlend"), 1e-4f);
            Assert.AreEqual((float)BlendMode.OneMinusSrcAlpha, mat.GetFloat("_DstBlend"), 1e-4f);
        }

        [Test]
        public void Get_MaterialIsDontSave_SoItNeverLeaksIntoAScene()
        {
            var mat = ParticleMaterialCache.Get(Tex(ParticleTextureShape.Ring), additive: true);

            Assert.AreEqual(HideFlags.DontSave, mat.hideFlags);
        }
    }
}
