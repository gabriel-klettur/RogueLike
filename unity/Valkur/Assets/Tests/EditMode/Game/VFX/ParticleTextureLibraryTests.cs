using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Game.VFX
{
    /// <summary>
    /// Covers <see cref="ParticleTextureLibrary"/> — the procedural billboard textures that
    /// replaced the untextured quads every Valkur particle used to render as.
    ///
    /// Generated textures are uploaded with <c>makeNoLongerReadable: true</c>, so the shape
    /// itself is asserted through the pure <see cref="ParticleTextureLibrary.EvaluateAlpha"/>
    /// function rather than a GPU read-back.
    /// </summary>
    [TestFixture]
    public class ParticleTextureLibraryTests
    {
        // ── ResolveShape ─────────────────────────────────────────────────────────

        [Test]
        public void ResolveShape_NonAuto_PassesThrough()
        {
            Assert.AreEqual(ParticleTextureShape.Ring,
                ParticleTextureLibrary.ResolveShape(ParticleTextureShape.Ring, "explosion", additive: true));
            Assert.AreEqual(ParticleTextureShape.None,
                ParticleTextureLibrary.ResolveShape(ParticleTextureShape.None, "aura", additive: false));
        }

        [Test]
        public void ResolveShape_Auto_SmokeKinds_ReturnSmoke()
        {
            foreach (string kind in new[] { "smoke", "smoke_emitter", "smoke_burst" })
            {
                Assert.AreEqual(ParticleTextureShape.Smoke,
                    ParticleTextureLibrary.ResolveShape(ParticleTextureShape.Auto, kind, additive: false),
                    $"kind '{kind}' should resolve to Smoke");
            }
        }

        [Test]
        public void ResolveShape_Auto_SparkKinds_ReturnSpark()
        {
            foreach (string kind in new[] { "slash", "dash", "firework" })
            {
                Assert.AreEqual(ParticleTextureShape.Spark,
                    ParticleTextureLibrary.ResolveShape(ParticleTextureShape.Auto, kind, additive: true),
                    $"kind '{kind}' should resolve to Spark");
            }
        }

        [Test]
        public void ResolveShape_Auto_UnknownKind_FollowsBlendMode()
        {
            Assert.AreEqual(ParticleTextureShape.Glow,
                ParticleTextureLibrary.ResolveShape(ParticleTextureShape.Auto, "not_a_real_kind", additive: true));
            Assert.AreEqual(ParticleTextureShape.SoftDot,
                ParticleTextureLibrary.ResolveShape(ParticleTextureShape.Auto, "not_a_real_kind", additive: false));
        }

        [Test]
        public void ResolveShape_Auto_NullKind_DoesNotThrow()
        {
            Assert.AreEqual(ParticleTextureShape.SoftDot,
                ParticleTextureLibrary.ResolveShape(ParticleTextureShape.Auto, null, additive: false));
        }

        // ── Get ──────────────────────────────────────────────────────────────────

        [Test]
        public void Get_None_ReturnsNull()
        {
            Assert.IsNull(ParticleTextureLibrary.Get(ParticleTextureShape.None, 0.5f));
        }

        [Test]
        public void Get_Auto_ReturnsNull_BecauseCallerMustResolveFirst()
        {
            Assert.IsNull(ParticleTextureLibrary.Get(ParticleTextureShape.Auto, 0.5f));
        }

        [Test]
        public void Get_SameShapeAndSoftness_ReturnsCachedInstance()
        {
            var first = ParticleTextureLibrary.Get(ParticleTextureShape.SoftDot, 0.5f);
            var second = ParticleTextureLibrary.Get(ParticleTextureShape.SoftDot, 0.5f);

            Assert.IsNotNull(first);
            Assert.AreSame(first, second, "the library must cache, not regenerate");
        }

        [Test]
        public void Get_DifferentShapes_ReturnDistinctTextures()
        {
            var dot = ParticleTextureLibrary.Get(ParticleTextureShape.SoftDot, 0.5f);
            var ring = ParticleTextureLibrary.Get(ParticleTextureShape.Ring, 0.5f);

            Assert.AreNotSame(dot, ring);
        }

        [Test]
        public void Get_NearbySoftness_QuantisesToSameTexture()
        {
            // 16 steps => anything inside half a step rounds to the same key.
            var a = ParticleTextureLibrary.Get(ParticleTextureShape.Glow, 0.500f);
            var b = ParticleTextureLibrary.Get(ParticleTextureShape.Glow, 0.510f);

            Assert.AreSame(a, b, "softness must be quantised so the cache stays bounded");
        }

        [Test]
        public void Get_MarksTextureDontSave_SoItNeverLeaksIntoAScene()
        {
            var tex = ParticleTextureLibrary.Get(ParticleTextureShape.Spark, 0.25f);

            Assert.IsNotNull(tex);
            Assert.AreEqual(HideFlags.DontSave, tex.hideFlags);
            Assert.AreEqual(TextureWrapMode.Clamp, tex.wrapMode);
            Assert.AreEqual(FilterMode.Bilinear, tex.filterMode,
                "the VFX layer is deliberately not point-filtered like the pixel-art world");
        }

        // ── EvaluateAlpha — shape correctness ────────────────────────────────────

        [Test]
        public void EvaluateAlpha_SoftDot_IsOpaqueAtCentreAndFadesOutward()
        {
            float centre = ParticleTextureLibrary.EvaluateAlpha(ParticleTextureShape.SoftDot, 0f, 0f, 0.5f);
            float mid = ParticleTextureLibrary.EvaluateAlpha(ParticleTextureShape.SoftDot, 0.5f, 0f, 0.5f);
            float edge = ParticleTextureLibrary.EvaluateAlpha(ParticleTextureShape.SoftDot, 0.99f, 0f, 0.5f);

            Assert.AreEqual(1f, centre, 1e-4f);
            Assert.Greater(centre, mid);
            Assert.Greater(mid, edge);
        }

        [Test]
        public void EvaluateAlpha_OutsideUnitCircle_IsZero()
        {
            foreach (ParticleTextureShape shape in new[]
            {
                ParticleTextureShape.SoftDot, ParticleTextureShape.Glow,
                ParticleTextureShape.Spark, ParticleTextureShape.Smoke, ParticleTextureShape.Ring,
            })
            {
                Assert.AreEqual(0f, ParticleTextureLibrary.EvaluateAlpha(shape, 0.99f, 0.99f, 0.5f), 1e-6f,
                    $"{shape} must not paint the quad corners — that is what made particles look square");
            }
        }

        [Test]
        public void EvaluateAlpha_Softness_WidensTheFalloff()
        {
            float hard = ParticleTextureLibrary.EvaluateAlpha(ParticleTextureShape.SoftDot, 0.6f, 0f, 0f);
            float soft = ParticleTextureLibrary.EvaluateAlpha(ParticleTextureShape.SoftDot, 0.6f, 0f, 1f);

            Assert.Greater(soft, hard, "higher softness must carry more alpha further out");
        }

        [Test]
        public void EvaluateAlpha_Spark_FallsOffFasterThanSoftDot()
        {
            float spark = ParticleTextureLibrary.EvaluateAlpha(ParticleTextureShape.Spark, 0.5f, 0f, 0.5f);
            float dot = ParticleTextureLibrary.EvaluateAlpha(ParticleTextureShape.SoftDot, 0.5f, 0f, 0.5f);

            Assert.Less(spark, dot, "a spark is a tight hot core, not a soft blob");
        }

        [Test]
        public void EvaluateAlpha_Ring_PeaksOffCentre()
        {
            float centre = ParticleTextureLibrary.EvaluateAlpha(ParticleTextureShape.Ring, 0f, 0f, 0.5f);
            float band = ParticleTextureLibrary.EvaluateAlpha(ParticleTextureShape.Ring, 0.72f, 0f, 0.5f);

            Assert.Greater(band, centre, "a ring must be hollow");
            Assert.AreEqual(1f, band, 1e-3f);
        }

        [Test]
        public void EvaluateAlpha_Star_HasBrighterArmsThanDiagonals()
        {
            float arm = ParticleTextureLibrary.EvaluateAlpha(ParticleTextureShape.Star, 0.5f, 0f, 0.5f);
            float diagonal = ParticleTextureLibrary.EvaluateAlpha(ParticleTextureShape.Star, 0.35f, 0.35f, 0.5f);

            Assert.Greater(arm, diagonal, "the flare arms are the whole point of the Star shape");
        }

        [Test]
        public void EvaluateAlpha_Smoke_IsDeterministic()
        {
            float a = ParticleTextureLibrary.EvaluateAlpha(ParticleTextureShape.Smoke, 0.3f, -0.2f, 0.5f);
            float b = ParticleTextureLibrary.EvaluateAlpha(ParticleTextureShape.Smoke, 0.3f, -0.2f, 0.5f);

            Assert.AreEqual(a, b, 0f, "noise must be hash-based, never Random — textures have to be reproducible");
        }

        [Test]
        public void EvaluateAlpha_Smoke_IsNotUniform()
        {
            float a = ParticleTextureLibrary.EvaluateAlpha(ParticleTextureShape.Smoke, 0.10f, 0.10f, 0.5f);
            float b = ParticleTextureLibrary.EvaluateAlpha(ParticleTextureShape.Smoke, -0.25f, 0.30f, 0.5f);

            Assert.AreNotEqual(a, b, "smoke needs visible internal structure, not a flat disc");
        }
    }
}
