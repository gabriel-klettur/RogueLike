using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Gameplay.Editors;

namespace Valkur.Tests.EditMode
{
    /// <summary>
    /// Tests for <see cref="EditorUIHelpers.AddVerticalScrollbar"/> — the gold-themed
    /// scrollbar used by the Buildings Editor (and other editor panels) on grid pickers.
    ///
    /// Coverage:
    ///   • Returns a non-null Scrollbar and wires it onto the ScrollRect.
    ///   • Visibility is set to Permanent (always visible — Tiles editor parity).
    ///   • Direction is BottomToTop (vertical scroll).
    ///   • A "VScrollbar" GameObject is added under the ScrollRect.
    ///   • Handle has the gold color matching the Tile editor theme.
    ///   • Viewport offsetMax is shifted left so content does not overlap the scrollbar.
    ///   • Default sbWidth = 12 px is honored; custom widths apply correctly.
    /// </summary>
    [TestFixture]
    public class EditorUIHelpersScrollbarTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();

        // ── helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a minimal ScrollRect (root + Viewport + Content) sufficient to
        /// satisfy <see cref="EditorUIHelpers.AddVerticalScrollbar"/>.
        /// </summary>
        private ScrollRect MakeScrollRect()
        {
            var rootGo  = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
            var rootRt  = rootGo.GetComponent<RectTransform>();
            rootRt.sizeDelta = new Vector2(200f, 300f);
            _scene.Add(rootGo);

            var sr = rootGo.GetComponent<ScrollRect>();

            var viewportGo  = new GameObject("Viewport", typeof(RectTransform));
            viewportGo.transform.SetParent(rootGo.transform, false);
            var vpRt        = viewportGo.GetComponent<RectTransform>();
            vpRt.anchorMin  = Vector2.zero;
            vpRt.anchorMax  = Vector2.one;
            vpRt.offsetMin  = Vector2.zero;
            vpRt.offsetMax  = Vector2.zero;
            viewportGo.AddComponent<RectMask2D>();
            sr.viewport = vpRt;

            var contentGo  = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewportGo.transform, false);
            sr.content = contentGo.GetComponent<RectTransform>();

            return sr;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene) if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // ──────────────────────────────────────────────────────────────────────────

        [Test]
        public void AddVerticalScrollbar_ReturnsNonNullScrollbar()
        {
            var sr  = MakeScrollRect();
            var bar = EditorUIHelpers.AddVerticalScrollbar(sr);
            Assert.IsNotNull(bar, "AddVerticalScrollbar must return the created Scrollbar.");
        }

        [Test]
        public void AddVerticalScrollbar_AssignsToScrollRect_VerticalScrollbar()
        {
            var sr  = MakeScrollRect();
            var bar = EditorUIHelpers.AddVerticalScrollbar(sr);
            Assert.AreSame(bar, sr.verticalScrollbar,
                "ScrollRect.verticalScrollbar must point to the returned Scrollbar.");
        }

        [Test]
        public void AddVerticalScrollbar_VisibilityIsPermanent()
        {
            var sr = MakeScrollRect();
            EditorUIHelpers.AddVerticalScrollbar(sr);
            Assert.AreEqual(ScrollRect.ScrollbarVisibility.Permanent,
                sr.verticalScrollbarVisibility,
                "Scrollbar must always be visible (Permanent) — matches the Tiles editor style.");
        }

        [Test]
        public void AddVerticalScrollbar_DirectionIsBottomToTop()
        {
            var sr  = MakeScrollRect();
            var bar = EditorUIHelpers.AddVerticalScrollbar(sr);
            Assert.AreEqual(Scrollbar.Direction.BottomToTop, bar.direction,
                "Vertical scrollbar must scroll BottomToTop (Unity convention for Y-up).");
        }

        [Test]
        public void AddVerticalScrollbar_CreatesVScrollbarChild()
        {
            var sr = MakeScrollRect();
            EditorUIHelpers.AddVerticalScrollbar(sr);
            var child = sr.transform.Find("VScrollbar");
            Assert.IsNotNull(child, "AddVerticalScrollbar must create a 'VScrollbar' child GameObject.");
        }

        [Test]
        public void AddVerticalScrollbar_HandleIsGoldColored()
        {
            var sr  = MakeScrollRect();
            var bar = EditorUIHelpers.AddVerticalScrollbar(sr);
            Assert.IsNotNull(bar.handleRect, "Scrollbar.handleRect must be wired.");
            var img = bar.handleRect.GetComponent<Image>();
            Assert.IsNotNull(img, "Handle must have an Image component.");
            // RGB ≈ (0.55, 0.45, 0.22) — warm gold matching the Tile editor handle.
            Assert.AreEqual(0.55f, img.color.r, 0.02f, "Handle red channel mismatch (gold theme).");
            Assert.AreEqual(0.45f, img.color.g, 0.02f, "Handle green channel mismatch (gold theme).");
            Assert.AreEqual(0.22f, img.color.b, 0.02f, "Handle blue channel mismatch (gold theme).");
        }

        [Test]
        public void AddVerticalScrollbar_HandleRectIsTargetGraphic()
        {
            var sr  = MakeScrollRect();
            var bar = EditorUIHelpers.AddVerticalScrollbar(sr);
            Assert.AreSame(bar.handleRect.GetComponent<Image>(), bar.targetGraphic,
                "targetGraphic must be the handle's Image so colors animate on hover/press.");
        }

        [Test]
        public void AddVerticalScrollbar_DefaultWidth_OffsetsViewport_Minus12()
        {
            var sr            = MakeScrollRect();
            float origOffsetX = sr.viewport.offsetMax.x; // 0 in our setup
            EditorUIHelpers.AddVerticalScrollbar(sr);
            Assert.AreEqual(origOffsetX - 12f, sr.viewport.offsetMax.x, 0.001f,
                "Default sbWidth=12 must shift viewport.offsetMax.x left by 12 px.");
        }

        [Test]
        public void AddVerticalScrollbar_CustomWidth_OffsetsViewport_Accordingly()
        {
            var sr            = MakeScrollRect();
            float origOffsetX = sr.viewport.offsetMax.x;
            EditorUIHelpers.AddVerticalScrollbar(sr, sbWidth: 20f);
            Assert.AreEqual(origOffsetX - 20f, sr.viewport.offsetMax.x, 0.001f,
                "Custom sbWidth must shift viewport.offsetMax.x left by that amount.");
        }

        [Test]
        public void AddVerticalScrollbar_VScrollbar_AnchoredToRightEdge()
        {
            var sr = MakeScrollRect();
            EditorUIHelpers.AddVerticalScrollbar(sr);
            var rt = sr.transform.Find("VScrollbar").GetComponent<RectTransform>();
            // anchor to the right edge: anchorMin = (1,0), anchorMax = (1,1)
            Assert.AreEqual(1f, rt.anchorMin.x, 0.001f, "VScrollbar.anchorMin.x must be 1 (right edge).");
            Assert.AreEqual(0f, rt.anchorMin.y, 0.001f, "VScrollbar.anchorMin.y must be 0 (full height).");
            Assert.AreEqual(1f, rt.anchorMax.x, 0.001f, "VScrollbar.anchorMax.x must be 1 (right edge).");
            Assert.AreEqual(1f, rt.anchorMax.y, 0.001f, "VScrollbar.anchorMax.y must be 1 (full height).");
        }

        [Test]
        public void AddVerticalScrollbar_VScrollbar_HasBackgroundImage()
        {
            var sr = MakeScrollRect();
            EditorUIHelpers.AddVerticalScrollbar(sr);
            var bg = sr.transform.Find("VScrollbar").GetComponent<Image>();
            Assert.IsNotNull(bg, "VScrollbar must have a dark background Image.");
            // Dark background ≈ (0.08, 0.08, 0.10), alpha ≈ 0.85
            Assert.Less(bg.color.r, 0.20f, "Background red channel should be dark.");
            Assert.Less(bg.color.g, 0.20f, "Background green channel should be dark.");
            Assert.Less(bg.color.b, 0.20f, "Background blue channel should be dark.");
            Assert.Greater(bg.color.a, 0.5f, "Background alpha should be mostly opaque.");
        }

        [Test]
        public void AddVerticalScrollbar_HoverColor_IsBrighterThanNormal()
        {
            var sr  = MakeScrollRect();
            var bar = EditorUIHelpers.AddVerticalScrollbar(sr);
            Assert.Greater(bar.colors.highlightedColor.r, bar.colors.normalColor.r,
                "Highlighted color must be brighter than normal (visual feedback on hover).");
        }
    }
}
