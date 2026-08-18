using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Game.VFX
{
    /// <summary>
    /// Covers <see cref="BeamMaterialCache"/>, which fixed two things about the laser at once.
    ///
    /// The beam was alpha-blended: LaserBeamController built its lines with
    /// Sprite-Unlit-Default and never touched the blend factors, so it occluded the world
    /// instead of adding light to it. A laser that cannot be brighter than its background is
    /// a coloured bar.
    ///
    /// And it allocated a Material per beam per line. Sharing them is what makes the scroll
    /// interesting: the offset has to live in a MaterialPropertyBlock instead, or two
    /// simultaneous beams would scroll as one.
    /// </summary>
    [TestFixture]
    public class BeamMaterialCacheTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();

        [SetUp]
        public void SetUp() => LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created) if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        private static Texture2D Tex(BeamTextureKind kind) => BeamTextureLibrary.Get(kind, 0.4f);

        private LineRenderer MakeLine()
        {
            var go = new GameObject("BeamCacheTestLine");
            _created.Add(go);
            return go.AddComponent<LineRenderer>();
        }

        // ── Sharing ──────────────────────────────────────────────────────────────

        [Test]
        public void Get_SameTexture_ReturnsTheSharedMaterial()
        {
            var a = BeamMaterialCache.Get(Tex(BeamTextureKind.Core));
            var b = BeamMaterialCache.Get(Tex(BeamTextureKind.Core));

            Assert.IsNotNull(a);
            Assert.AreSame(a, b, "One material per texture, not one per beam.");
        }

        [Test]
        public void Get_DifferentTextures_ReturnDifferentMaterials()
        {
            Assert.AreNotSame(
                BeamMaterialCache.Get(Tex(BeamTextureKind.Core)),
                BeamMaterialCache.Get(Tex(BeamTextureKind.Glow)));
        }

        [Test]
        public void Get_NullTexture_StillProducesAUsableMaterial()
        {
            Assert.IsNotNull(BeamMaterialCache.Get(null));
        }

        [Test]
        public void Get_MaterialIsDontSave()
        {
            Assert.AreEqual(HideFlags.DontSave, BeamMaterialCache.Get(Tex(BeamTextureKind.Glow)).hideFlags);
        }

        // ── The blend mode that makes it a laser ─────────────────────────────────

        [Test]
        public void Get_UsesAdditiveBlending()
        {
            var mat = BeamMaterialCache.Get(Tex(BeamTextureKind.Core));

            // No Assert.Ignore escape hatch here on purpose. The first implementation used a
            // shader with no blend properties, so every SetFloat was a silent no-op and the
            // beam stayed alpha-blended — and an Ignore hid exactly that. If the property is
            // missing, the material cannot be additive, and that is a failure.
            Assert.IsTrue(mat.HasProperty("_SrcBlend"),
                "The shader must expose blend factors, or additive cannot be requested at all.");

            Assert.AreEqual((float)BlendMode.SrcAlpha, mat.GetFloat("_SrcBlend"), 1e-4f);
            Assert.AreEqual((float)BlendMode.One, mat.GetFloat("_DstBlend"), 1e-4f,
                "A destination factor of OneMinusSrcAlpha is alpha blending, which is what made " +
                "the beam occlude the world rather than glow over it.");
        }

        [Test]
        public void Get_IsTransparentAndDoesNotWriteDepth()
        {
            var mat = BeamMaterialCache.Get(Tex(BeamTextureKind.Core));

            Assert.AreEqual(3000, mat.renderQueue, "The beam must sort in the transparent queue.");
            if (mat.HasProperty("_ZWrite")) Assert.AreEqual(0f, mat.GetFloat("_ZWrite"), 1e-4f);
        }

        [Test]
        public void Get_BindsTheTexture()
        {
            var tex = Tex(BeamTextureKind.Energy);
            var mat = BeamMaterialCache.Get(tex);

            bool bound = (mat.HasProperty("_MainTex") && mat.GetTexture("_MainTex") == tex)
                      || (mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") == tex);

            Assert.IsTrue(bound,
                "An unbound texture leaves the LineRenderer drawing a hard-edged rectangle — " +
                "exactly the look this whole change exists to remove.");
        }

        // ── Per-renderer scroll ──────────────────────────────────────────────────

        [Test]
        public void ApplyScroll_WritesTilingAndOffsetOntoTheRenderer()
        {
            var line = MakeLine();
            var block = new MaterialPropertyBlock();

            BeamMaterialCache.ApplyScroll(line, block, tiling: 3.5f, offset: -0.25f);

            var read = new MaterialPropertyBlock();
            line.GetPropertyBlock(read);
            var st = read.GetVector("_MainTex_ST");

            Assert.AreEqual(3.5f, st.x, 1e-4f, "x is tiling along the beam.");
            Assert.AreEqual(-0.25f, st.z, 1e-4f, "z is the scroll offset.");
            Assert.AreEqual(1f, st.y, 1e-4f, "The across-beam axis must never tile.");
        }

        [Test]
        public void ApplyScroll_KeepsTwoRenderersIndependent()
        {
            var a = MakeLine();
            var b = MakeLine();
            var blockA = new MaterialPropertyBlock();
            var blockB = new MaterialPropertyBlock();

            BeamMaterialCache.ApplyScroll(a, blockA, 2f, 0.1f);
            BeamMaterialCache.ApplyScroll(b, blockB, 2f, 0.9f);

            var readA = new MaterialPropertyBlock();
            a.GetPropertyBlock(readA);

            Assert.AreEqual(0.1f, readA.GetVector("_MainTex_ST").z, 1e-4f,
                "The material is shared, so scroll must live per-renderer. Writing it on the " +
                "material would make every beam in the scene scroll as one.");
        }

        [Test]
        public void ApplyScroll_NullArguments_AreSafe()
        {
            Assert.DoesNotThrow(() => BeamMaterialCache.ApplyScroll(null, new MaterialPropertyBlock(), 1f, 0f));
            Assert.DoesNotThrow(() => BeamMaterialCache.ApplyScroll(MakeLine(), null, 1f, 0f));
        }
    }
}
