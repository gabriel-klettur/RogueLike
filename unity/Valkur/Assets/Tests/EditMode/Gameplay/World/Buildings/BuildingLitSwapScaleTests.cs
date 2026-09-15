using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Gameplay.World.Buildings
{
    /// <summary>
    /// A light fixture must be the SAME SIZE lit and unlit.
    ///
    /// The lit art is the same drawing with a glow painted around it, and the sprites are
    /// trimmed to their alpha — so <c>lamp_post_classic</c> is 26x115 dark and 51x115 lit, and
    /// ten of the eleven shipped lit/unlit pairs differ in width. <c>BuildingObject.Apply</c>
    /// measured its size budget against whichever sprite happened to be loaded, so at dusk the
    /// lit sprite's aspect (51/115) missed the template's (26/115) by more than the tolerance,
    /// took the aspect-DRIFT branch meant for re-exported PNGs, and fitted the glow into the
    /// dark art's budget: fit = min(26/51, 115/115) = 0.51. Every lamp post in the world halved
    /// in size at nightfall and grew back at dawn.
    ///
    /// The invariant that fixes it is the one asserted here: the pixels-to-world mapping
    /// (<c>localScale</c>) is a property of the BASE art and the instance's own override, and a
    /// presentation-only sprite swap may not move it. Asserting the scale rather than the
    /// rendered width is deliberate — the lit sprite SHOULD cover more world, because the glow
    /// is wider than the lamp; what must not change is how big a sprite pixel is.
    /// </summary>
    [TestFixture]
    public class BuildingLitSwapScaleTests
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic
                                       | BindingFlags.Instance | BindingFlags.Static;

        // The shipped pair the bug was reported on, and the widest mismatch in the family.
        private const string BASE_PATH = "Buildings/lights/lamp_post_classic";
        private const string LIT_PATH  = "Buildings/lights/lamp_post_classic_lit";

        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _go = new GameObject("LitSwapProbe");
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            if (_go != null) Object.DestroyImmediate(_go);
        }

        private static (int w, int h) SpriteSize(string resourcePath)
        {
            var s = Resources.Load<Sprite>(resourcePath);
            Assert.IsNotNull(s, $"Shipped sprite missing at Resources/{resourcePath}.");
            return (Mathf.RoundToInt(s.textureRect.width), Mathf.RoundToInt(s.textureRect.height));
        }

        private static BuildingTemplateData MakeTemplate(int origW, int origH)
        {
            var t = ScriptableObject.CreateInstance<BuildingTemplateData>();
            t.templateId    = 325;
            t.assetPath     = BASE_PATH;
            t.litAssetPath  = LIT_PATH;
            t.originalScale = new Vector2Int(origW, origH);
            t.splitRatio    = 0f;
            return t;
        }

        /// <summary>The premise. If the art is ever re-exported to matching sizes this test
        /// stops meaning anything, and it should say so rather than pass vacuously.</summary>
        [Test]
        public void ThePairThisGuards_ReallyDoesDifferInWidth()
        {
            var b = SpriteSize(BASE_PATH);
            var l = SpriteSize(LIT_PATH);
            Assert.AreEqual(b.h, l.h, "Heights are expected to match across the pair.");
            Assert.AreNotEqual(b.w, l.w,
                "The lit and unlit art are now the same width, so this fixture no longer " +
                "exercises the swap it was written for. Re-point it at a pair that differs.");
        }

        [Test]
        public void LitSwap_DoesNotChangeTheBuildingScale()
        {
            var b = SpriteSize(BASE_PATH);
            var t = MakeTemplate(b.w, b.h);
            try
            {
                var bo = _go.AddComponent<BuildingObject>();

                bo.Apply(t, Vector2Int.zero, -1f);
                Vector3 dark = _go.transform.localScale;

                bo.Apply(t, Vector2Int.zero, -1f, assetPathOverride: LIT_PATH);
                Vector3 lit = _go.transform.localScale;

                Assert.AreEqual(dark.x, lit.x, 0.0001f,
                    $"The fixture changed width at nightfall ({dark.x} -> {lit.x}). The glow is " +
                    "wider than the lamp, so fitting the lit sprite into the dark sprite's " +
                    "budget squashes the lamp itself.");
                Assert.AreEqual(dark.y, lit.y, 0.0001f,
                    $"The fixture changed height at nightfall ({dark.y} -> {lit.y}).");

                // And back again, because the swap runs in both directions every day.
                bo.Apply(t, Vector2Int.zero, -1f);
                Assert.AreEqual(dark.x, _go.transform.localScale.x, 0.0001f, "Dawn did not restore the width.");
                Assert.AreEqual(dark.y, _go.transform.localScale.y, 0.0001f, "Dawn did not restore the height.");
            }
            finally { Object.DestroyImmediate(t); }
        }

        /// <summary>
        /// A resized instance keeps its own size across the swap too. The author's override is
        /// the size they chose; a nightfall that renegotiates it is the same bug wearing a
        /// different number.
        /// </summary>
        [Test]
        public void LitSwap_PreservesAPerInstanceScaleOverride()
        {
            var b = SpriteSize(BASE_PATH);
            var t = MakeTemplate(b.w, b.h);
            try
            {
                var bo = _go.AddComponent<BuildingObject>();
                var over = new Vector2Int(b.w * 2, b.h * 2);

                bo.Apply(t, over, -1f);
                Vector3 dark = _go.transform.localScale;
                bo.Apply(t, over, -1f, assetPathOverride: LIT_PATH);

                Assert.AreEqual(dark.x, _go.transform.localScale.x, 0.0001f,
                    "A resized fixture changed width at nightfall.");
                Assert.AreEqual(dark.y, _go.transform.localScale.y, 0.0001f,
                    "A resized fixture changed height at nightfall.");
                Assert.AreEqual(2f, dark.x, 0.0001f, "The 2x override did not resolve to a 2x scale.");
            }
            finally { Object.DestroyImmediate(t); }
        }

        /// <summary>
        /// The fix must be invisible to every building that does NOT swap — which is 1165 of
        /// the 1176 shipped templates. With no override the base sprite IS the loaded sprite,
        /// so the scale stays exactly 1.
        /// </summary>
        [Test]
        public void NonSwappingBuilding_IsUnaffected()
        {
            var b = SpriteSize(BASE_PATH);
            var t = MakeTemplate(b.w, b.h);
            try
            {
                var bo = _go.AddComponent<BuildingObject>();
                bo.Apply(t, Vector2Int.zero, -1f);
                Assert.AreEqual(1f, _go.transform.localScale.x, 0.0001f);
                Assert.AreEqual(1f, _go.transform.localScale.y, 0.0001f);
            }
            finally { Object.DestroyImmediate(t); }
        }
    }
}
