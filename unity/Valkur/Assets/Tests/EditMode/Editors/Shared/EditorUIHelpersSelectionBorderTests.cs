using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Editors.Shared
{
    /// <summary>
    /// Pins the geometry and the click-through contract of
    /// <see cref="EditorUIHelpers.MakeSelectionBorder"/> plus the theme token it
    /// defaults to, <see cref="UITheme.SELECTION_BORDER"/>.
    ///
    /// Why this matters: picker grids (Particles F1 today, and every grid that
    /// copies the pattern) mark the selected cell with this frame because the
    /// usual translucent <see cref="UITheme.SLOT_SELECTED"/> background tint is
    /// invisible behind a slot whose icon or live preview fills the whole cell.
    /// The frame is drawn as four <see cref="Image"/> strips laid ON TOP of the
    /// slot button, which makes it a natural way to break the picker: if any
    /// strip ever ships with <c>raycastTarget = true</c>, the edges of every
    /// selected slot stop responding to clicks — a bug that looks like "the
    /// picker sometimes ignores me" and is very hard to trace back here.
    ///
    /// The fixture therefore asserts, per edge:
    ///   • four strips exist, one per edge, each stretched along that edge;
    ///   • no <see cref="Graphic"/> in the subtree takes raycasts;
    ///   • the container is the LAST sibling (draws above the slot content) and
    ///     stretches 0,0 → 1,1 with zero offsets (tracks slot resizes);
    ///   • thickness lands on the correct axis per strip;
    ///   • colour falls back to the theme token and an explicit colour wins.
    ///
    /// All geometry is asserted twice where it is cheap to do so: on the
    /// anchor/sizeDelta setup AND on the resulting <c>RectTransform.rect</c>,
    /// so a "correct-looking" anchor set that produces the wrong rect still fails.
    /// </summary>
    [TestFixture]
    public class EditorUIHelpersSelectionBorderTests
    {
        private const float TargetWidth  = 100f;
        private const float TargetHeight = 80f;
        private const float Tol          = 0.001f;

        private readonly List<GameObject> _scene = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            // Creating Image/Graphic components outside a Canvas logs renderer
            // init noise in EditMode that would fail otherwise-passing tests.
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene)
                if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a stand-in for a picker slot: a non-stretched RectTransform of a
        /// known size (so <c>rect</c> reads deterministically without a Canvas or a
        /// layout pass) carrying <paramref name="contentChildren"/> content children,
        /// which is what the border has to draw on top of.
        /// </summary>
        private RectTransform BuildSlot(int contentChildren = 2,
            float width = TargetWidth, float height = TargetHeight)
        {
            var go = new GameObject("Slot", typeof(RectTransform));
            _scene.Add(go);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot     = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(width, height);

            for (int i = 0; i < contentChildren; i++)
            {
                var child = new GameObject("Content" + i, typeof(RectTransform));
                child.transform.SetParent(rt, false);
            }
            return rt;
        }

        private static RectTransform Strip(GameObject border, string edge)
        {
            var t = border.transform.Find(edge);
            Assert.IsTrue(t != null, $"Selection border is missing its '{edge}' strip.");
            return (RectTransform)t;
        }

        /// <summary>
        /// World-space corners of a RectTransform, in Unity's documented order:
        /// [0] bottom-left, [1] top-left, [2] top-right, [3] bottom-right.
        /// The slot is built at the origin with pivot (0,0), so these read as
        /// plain pixel coordinates from the slot's bottom-left corner — which
        /// makes "is the strip actually flush with that edge?" directly assertable
        /// instead of merely re-reading back the anchors that were just set.
        /// </summary>
        private static Vector3[] Corners(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return c;
        }

        private static void AssertColorEquals(Color expected, Color actual, string message)
        {
            Assert.AreEqual(expected.r, actual.r, Tol, message + " (r)");
            Assert.AreEqual(expected.g, actual.g, Tol, message + " (g)");
            Assert.AreEqual(expected.b, actual.b, Tol, message + " (b)");
            Assert.AreEqual(expected.a, actual.a, Tol, message + " (a)");
        }

        // ── Null / defensive input ────────────────────────────────────────────

        [Test]
        public void MakeSelectionBorder_NullTarget_ReturnsNullInsteadOfThrowing()
        {
            // Picker rebuilds routinely run against slots that were destroyed
            // earlier in the same frame; the helper must degrade, not throw,
            // because a throw here aborts the whole grid rebuild mid-way.
            GameObject result = null;
            Assert.DoesNotThrow(() => result = EditorUIHelpers.MakeSelectionBorder(null),
                "A null target must not throw — it aborts an in-progress picker rebuild.");
            Assert.IsNull(result, "Null target must yield a null container, not an orphan GameObject.");
        }

        [Test]
        public void MakeSelectionBorder_DestroyedTarget_ReturnsNullInsteadOfThrowing()
        {
            // Unity fake-null: a destroyed RectTransform is not C# null, so the
            // guard has to be a Unity == comparison, not `is null`.
            var slot = BuildSlot();
            Object.DestroyImmediate(slot.gameObject);

            GameObject result = null;
            Assert.DoesNotThrow(() => result = EditorUIHelpers.MakeSelectionBorder(slot),
                "A destroyed target must hit the Unity-null guard, not throw a MissingReference.");
            Assert.IsTrue(result == null,
                "A destroyed target must yield no container (Unity-null aware assert).");
        }

        // ── Structure: four strips, one per edge ──────────────────────────────

        [Test]
        public void MakeSelectionBorder_Default_CreatesExactlyFourStripsOnePerEdge()
        {
            var slot = BuildSlot();

            var border = EditorUIHelpers.MakeSelectionBorder(slot);

            Assert.IsTrue(border != null, "A valid target must produce a border container.");
            Assert.AreEqual(4, border.transform.childCount,
                "The frame is exactly four strips — extra children mean duplicated or leaked edges.");
            foreach (var edge in new[] { "Top", "Bottom", "Left", "Right" })
                Assert.IsTrue(border.transform.Find(edge) != null,
                    $"Missing the '{edge}' strip — the frame would be open on that side.");
        }

        [Test]
        public void MakeSelectionBorder_Container_IsParentedToTarget()
        {
            var slot = BuildSlot();

            var border = EditorUIHelpers.MakeSelectionBorder(slot);

            Assert.AreSame(slot, border.transform.parent,
                "The border must live under the slot so destroying the slot disposes of it too.");
        }

        [Test]
        public void MakeSelectionBorder_EveryStrip_HasARenderableImage()
        {
            var slot = BuildSlot();

            var border = EditorUIHelpers.MakeSelectionBorder(slot);

            foreach (var edge in new[] { "Top", "Bottom", "Left", "Right" })
            {
                var img = Strip(border, edge).GetComponent<Image>();
                Assert.IsTrue(img != null, $"'{edge}' strip must carry an Image or it draws nothing.");
                Assert.IsTrue(img.enabled, $"'{edge}' strip Image must be enabled.");
            }
        }

        // ── The click-through contract (the important one) ────────────────────

        [Test]
        public void MakeSelectionBorder_EveryGraphicInSubtree_HasRaycastTargetDisabled()
        {
            // THE regression guard for this helper. The frame is drawn over the
            // slot button; a single raycastTarget=true strip makes the outer
            // ~4 px of the selected slot swallow clicks.
            var slot = BuildSlot();

            var border = EditorUIHelpers.MakeSelectionBorder(slot);

            var graphics = border.GetComponentsInChildren<Graphic>(includeInactive: true);
            Assert.AreEqual(4, graphics.Length,
                "Only the four strips should be renderable — an extra Graphic (e.g. a background " +
                "on the container) would cover the whole slot, not just its edges.");
            foreach (var g in graphics)
                Assert.IsFalse(g.raycastTarget,
                    $"'{g.name}' takes raycasts — the selected slot's button would stop " +
                    "receiving clicks along that edge.");
        }

        [Test]
        public void MakeSelectionBorder_Container_HasNoGraphicOfItsOwn()
        {
            var slot = BuildSlot();

            var border = EditorUIHelpers.MakeSelectionBorder(slot);

            Assert.IsTrue(border.GetComponent<Graphic>() == null,
                "The container must stay a bare RectTransform — a Graphic on it would " +
                "paint over (and potentially block) the entire slot instead of its border.");
        }

        // ── Draw order + fill ─────────────────────────────────────────────────

        [Test]
        public void MakeSelectionBorder_Container_IsLastSiblingSoItDrawsAboveSlotContent()
        {
            var slot = BuildSlot(contentChildren: 3);

            var border = EditorUIHelpers.MakeSelectionBorder(slot);

            Assert.AreEqual(slot.childCount - 1, border.transform.GetSiblingIndex(),
                "uGUI draws siblings in order; anything but the last index puts the frame " +
                "behind the slot's icon/preview, which is precisely the case this widget exists for.");
        }

        [Test]
        public void MakeSelectionBorder_Container_StretchesToFillTargetWithZeroOffsets()
        {
            var slot = BuildSlot();

            var border = EditorUIHelpers.MakeSelectionBorder(slot);
            var rt = (RectTransform)border.transform;

            Assert.AreEqual(Vector2.zero, rt.anchorMin, "Container anchorMin must be (0,0).");
            Assert.AreEqual(Vector2.one,  rt.anchorMax, "Container anchorMax must be (1,1).");
            Assert.AreEqual(Vector2.zero, rt.offsetMin,
                "Non-zero offsetMin would inset the frame from the slot's real bounds.");
            Assert.AreEqual(Vector2.zero, rt.offsetMax,
                "Non-zero offsetMax would inset the frame from the slot's real bounds.");

            Assert.AreEqual(TargetWidth,  rt.rect.width,  Tol,
                "Stretched container must measure exactly the slot width.");
            Assert.AreEqual(TargetHeight, rt.rect.height, Tol,
                "Stretched container must measure exactly the slot height.");
        }

        [Test]
        public void MakeSelectionBorder_WhenTargetIsResizedAfterwards_FrameFollows()
        {
            // Responsive picker grids (GridAutoSize) resize cells at runtime.
            // Anchor-based stretch means the frame tracks that for free; a
            // regression to fixed sizeDelta would leave the frame at the old size.
            var slot = BuildSlot();
            var border = EditorUIHelpers.MakeSelectionBorder(slot);

            slot.sizeDelta = new Vector2(240f, 160f);

            Assert.AreEqual(240f, ((RectTransform)border.transform).rect.width, Tol,
                "Container must follow the slot when the grid reflows.");
            Assert.AreEqual(240f, Strip(border, "Top").rect.width, Tol,
                "The top strip must re-stretch to the new slot width.");
            Assert.AreEqual(160f, Strip(border, "Left").rect.height, Tol,
                "The left strip must re-stretch to the new slot height.");
        }

        // ── Per-edge geometry ─────────────────────────────────────────────────

        [Test]
        public void MakeSelectionBorder_TopStrip_SpansFullWidthAndSitsOnTopEdge()
        {
            var slot = BuildSlot();

            var top = Strip(EditorUIHelpers.MakeSelectionBorder(slot, thickness: 6f), "Top");

            Assert.AreEqual(new Vector2(0f, 1f), top.anchorMin, "Top strip anchorMin must be (0,1).");
            Assert.AreEqual(Vector2.one,         top.anchorMax, "Top strip anchorMax must be (1,1).");
            Assert.AreEqual(new Vector2(0.5f, 1f), top.pivot,
                "Pivot y must be 1 so the strip hangs inside the slot instead of straddling the edge.");
            Assert.AreEqual(TargetWidth, top.rect.width, Tol,
                "Top strip must span the full slot width — a short strip leaves gaps at the corners.");
            Assert.AreEqual(6f, top.rect.height, Tol, "Top strip height must equal the thickness.");

            var c = Corners(top);
            Assert.AreEqual(TargetHeight, c[1].y, Tol,
                "Top strip must be flush with the slot's top edge (y = slot height).");
            Assert.AreEqual(TargetHeight - 6f, c[0].y, Tol,
                "Top strip must hang INWARDS from the top edge, not straddle it.");
            Assert.AreEqual(0f, c[0].x, Tol, "Top strip must start at the slot's left edge.");
            Assert.AreEqual(TargetWidth, c[2].x, Tol, "Top strip must end at the slot's right edge.");
        }

        [Test]
        public void MakeSelectionBorder_BottomStrip_SpansFullWidthAndSitsOnBottomEdge()
        {
            var slot = BuildSlot();

            var bottom = Strip(EditorUIHelpers.MakeSelectionBorder(slot, thickness: 6f), "Bottom");

            Assert.AreEqual(Vector2.zero,        bottom.anchorMin, "Bottom strip anchorMin must be (0,0).");
            Assert.AreEqual(new Vector2(1f, 0f), bottom.anchorMax, "Bottom strip anchorMax must be (1,0).");
            Assert.AreEqual(new Vector2(0.5f, 0f), bottom.pivot,
                "Pivot y must be 0 so the strip sits inside the slot.");
            Assert.AreEqual(TargetWidth, bottom.rect.width, Tol,
                "Bottom strip must span the full slot width.");
            Assert.AreEqual(6f, bottom.rect.height, Tol, "Bottom strip height must equal the thickness.");

            var c = Corners(bottom);
            Assert.AreEqual(0f, c[0].y, Tol,
                "Bottom strip must be flush with the slot's bottom edge (y = 0).");
            Assert.AreEqual(6f, c[1].y, Tol,
                "Bottom strip must grow upwards into the slot, not below it.");
            Assert.AreEqual(0f, c[0].x, Tol, "Bottom strip must start at the slot's left edge.");
            Assert.AreEqual(TargetWidth, c[2].x, Tol, "Bottom strip must end at the slot's right edge.");
        }

        [Test]
        public void MakeSelectionBorder_LeftStrip_SpansFullHeightAndSitsOnLeftEdge()
        {
            var slot = BuildSlot();

            var left = Strip(EditorUIHelpers.MakeSelectionBorder(slot, thickness: 6f), "Left");

            Assert.AreEqual(Vector2.zero,        left.anchorMin, "Left strip anchorMin must be (0,0).");
            Assert.AreEqual(new Vector2(0f, 1f), left.anchorMax, "Left strip anchorMax must be (0,1).");
            Assert.AreEqual(new Vector2(0f, 0.5f), left.pivot,
                "Pivot x must be 0 so the strip sits inside the slot.");
            Assert.AreEqual(TargetHeight, left.rect.height, Tol,
                "Left strip must span the full slot height — a short strip leaves corner gaps.");
            Assert.AreEqual(6f, left.rect.width, Tol, "Left strip width must equal the thickness.");

            var c = Corners(left);
            Assert.AreEqual(0f, c[0].x, Tol,
                "Left strip must be flush with the slot's left edge (x = 0).");
            Assert.AreEqual(6f, c[3].x, Tol,
                "Left strip must grow inwards to the right, not off the slot.");
            Assert.AreEqual(0f, c[0].y, Tol, "Left strip must start at the slot's bottom edge.");
            Assert.AreEqual(TargetHeight, c[1].y, Tol, "Left strip must end at the slot's top edge.");
        }

        [Test]
        public void MakeSelectionBorder_RightStrip_SpansFullHeightAndSitsOnRightEdge()
        {
            var slot = BuildSlot();

            var right = Strip(EditorUIHelpers.MakeSelectionBorder(slot, thickness: 6f), "Right");

            Assert.AreEqual(new Vector2(1f, 0f), right.anchorMin, "Right strip anchorMin must be (1,0).");
            Assert.AreEqual(Vector2.one,         right.anchorMax, "Right strip anchorMax must be (1,1).");
            Assert.AreEqual(new Vector2(1f, 0.5f), right.pivot,
                "Pivot x must be 1 so the strip sits inside the slot.");
            Assert.AreEqual(TargetHeight, right.rect.height, Tol,
                "Right strip must span the full slot height.");
            Assert.AreEqual(6f, right.rect.width, Tol, "Right strip width must equal the thickness.");

            var c = Corners(right);
            Assert.AreEqual(TargetWidth, c[3].x, Tol,
                "Right strip must be flush with the slot's right edge (x = slot width).");
            Assert.AreEqual(TargetWidth - 6f, c[0].x, Tol,
                "Right strip must grow inwards to the left, not off the slot.");
            Assert.AreEqual(0f, c[0].y, Tol, "Right strip must start at the slot's bottom edge.");
            Assert.AreEqual(TargetHeight, c[1].y, Tol, "Right strip must end at the slot's top edge.");
        }

        // ── Thickness ─────────────────────────────────────────────────────────

        [Test]
        public void MakeSelectionBorder_Thickness_LandsOnThePerpendicularAxisOnly()
        {
            const float t = 11f;
            var slot = BuildSlot();

            var border = EditorUIHelpers.MakeSelectionBorder(slot, thickness: t);

            // Horizontal strips: free width (sizeDelta.x = 0 → stretched), fixed height.
            foreach (var edge in new[] { "Top", "Bottom" })
            {
                var rt = Strip(border, edge);
                Assert.AreEqual(0f, rt.sizeDelta.x, Tol,
                    $"'{edge}' must keep sizeDelta.x = 0 so it stretches with the slot width.");
                Assert.AreEqual(t, rt.sizeDelta.y, Tol,
                    $"'{edge}' thickness belongs on the Y axis.");
            }

            // Vertical strips: fixed width, free height.
            foreach (var edge in new[] { "Left", "Right" })
            {
                var rt = Strip(border, edge);
                Assert.AreEqual(t, rt.sizeDelta.x, Tol,
                    $"'{edge}' thickness belongs on the X axis.");
                Assert.AreEqual(0f, rt.sizeDelta.y, Tol,
                    $"'{edge}' must keep sizeDelta.y = 0 so it stretches with the slot height.");
            }
        }

        [Test]
        public void MakeSelectionBorder_NoThicknessArgument_UsesThePublishedDefaultConstant()
        {
            // Callers read EditorUIHelpers.SELECTION_BORDER_THICKNESS to size
            // their own padding; the default argument must not drift from it.
            var slot = BuildSlot();

            var border = EditorUIHelpers.MakeSelectionBorder(slot);

            Assert.Greater(EditorUIHelpers.SELECTION_BORDER_THICKNESS, 0f,
                "A zero/negative default thickness would render an invisible frame.");
            Assert.AreEqual(EditorUIHelpers.SELECTION_BORDER_THICKNESS,
                Strip(border, "Top").sizeDelta.y, Tol,
                "The default thickness argument must match SELECTION_BORDER_THICKNESS.");
            Assert.AreEqual(EditorUIHelpers.SELECTION_BORDER_THICKNESS,
                Strip(border, "Left").sizeDelta.x, Tol,
                "The default thickness must apply to the vertical strips too.");
        }

        [Test]
        public void MakeSelectionBorder_ZeroThickness_StillBuildsFourNonBlockingStrips()
        {
            // Degenerate but reachable (a caller computing thickness from cell
            // size can hit 0). It must not throw and must not start blocking clicks.
            var slot = BuildSlot();

            GameObject border = null;
            Assert.DoesNotThrow(() => border = EditorUIHelpers.MakeSelectionBorder(slot, thickness: 0f));

            Assert.AreEqual(4, border.transform.childCount,
                "Zero thickness must still produce a well-formed (if invisible) frame.");
            foreach (var g in border.GetComponentsInChildren<Graphic>(true))
                Assert.IsFalse(g.raycastTarget,
                    "Even zero-thickness strips must stay click-through.");
        }

        [Test]
        public void MakeSelectionBorder_ThicknessLargerThanSlot_DoesNotThrowOrDropStrips()
        {
            // Thickness > slot size makes the strips overlap. Ugly, but it must
            // not corrupt the hierarchy or resurrect raycast blocking.
            var slot = BuildSlot(contentChildren: 1, width: 8f, height: 8f);

            GameObject border = null;
            Assert.DoesNotThrow(() => border = EditorUIHelpers.MakeSelectionBorder(slot, thickness: 200f));

            Assert.AreEqual(4, border.transform.childCount,
                "Oversized thickness must not drop or merge strips.");
            Assert.AreEqual(200f, Strip(border, "Top").rect.height, Tol,
                "The strip honours the requested thickness even when it overflows the slot.");
            foreach (var g in border.GetComponentsInChildren<Graphic>(true))
                Assert.IsFalse(g.raycastTarget,
                    "An oversized frame covers the whole slot — it MUST stay click-through.");
        }

        // ── Colour ────────────────────────────────────────────────────────────

        [Test]
        public void MakeSelectionBorder_NoColourArgument_UsesUIThemeSelectionBorder()
        {
            var slot = BuildSlot();

            var border = EditorUIHelpers.MakeSelectionBorder(slot);

            foreach (var edge in new[] { "Top", "Bottom", "Left", "Right" })
                AssertColorEquals(UITheme.SELECTION_BORDER,
                    Strip(border, edge).GetComponent<Image>().color,
                    $"'{edge}' strip must default to the theme token so every picker " +
                    "grid highlights identically");
        }

        [Test]
        public void MakeSelectionBorder_ExplicitColour_OverridesTheThemeDefaultOnAllFourStrips()
        {
            var custom = new Color(0.1f, 0.7f, 0.9f, 0.5f);
            var slot = BuildSlot();

            var border = EditorUIHelpers.MakeSelectionBorder(slot, color: custom);

            foreach (var edge in new[] { "Top", "Bottom", "Left", "Right" })
                AssertColorEquals(custom, Strip(border, edge).GetComponent<Image>().color,
                    $"'{edge}' strip ignored the explicit colour — a partially tinted frame " +
                    "is worse than none");
            Assert.AreNotEqual(UITheme.SELECTION_BORDER, custom,
                "Test fixture sanity: the override colour must differ from the theme default.");
        }

        // ── The theme token itself ────────────────────────────────────────────

        [Test]
        public void UIThemeSelectionBorder_IsFullyOpaque()
        {
            // The whole reason this token exists (see its XML doc): SLOT_SELECTED
            // is translucent and vanishes behind a full-cell preview. If someone
            // "harmonises" SELECTION_BORDER by giving it alpha, the frame becomes
            // unreadable over bright previews and the widget loses its purpose.
            Assert.AreEqual(1f, UITheme.SELECTION_BORDER.a, Tol,
                "SELECTION_BORDER must stay fully opaque — a translucent frame is " +
                "invisible over a full-cell icon/RenderTexture preview.");
        }

        [Test]
        public void UIThemeSelectionBorder_IsLouderThanSlotSelected()
        {
            Assert.Greater(UITheme.SELECTION_BORDER.a, UITheme.SLOT_SELECTED.a,
                "SELECTION_BORDER is documented as deliberately louder than the " +
                "SLOT_SELECTED background tint; equal or lower alpha breaks that contract.");
        }

        // ── Naming, repeat calls, removal ─────────────────────────────────────

        [Test]
        public void MakeSelectionBorder_CustomName_IsAppliedToTheContainerOnly()
        {
            // Some callers locate/remove the frame with Transform.Find(name),
            // so the name has to survive verbatim — including unicode and length.
            const string weird = "Selección_Borde_★_" +
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            var slot = BuildSlot();

            var border = EditorUIHelpers.MakeSelectionBorder(slot, name: weird);

            Assert.AreEqual(weird, border.name,
                "The container name must be used verbatim — callers Find() by it.");
            Assert.IsTrue(slot.Find(weird) != null,
                "The custom-named container must be findable under the slot.");
            Assert.IsTrue(border.transform.Find("Top") != null,
                "Renaming the container must not rename the edge strips.");
        }

        [Test]
        public void MakeSelectionBorder_EmptyName_StillProducesAUsableFrame()
        {
            var slot = BuildSlot();

            GameObject border = null;
            Assert.DoesNotThrow(() => border = EditorUIHelpers.MakeSelectionBorder(slot, name: string.Empty));

            Assert.IsTrue(border != null, "An empty name must not prevent creation.");
            Assert.AreEqual(4, border.transform.childCount,
                "An empty name must not affect the frame's structure.");
        }

        [Test]
        public void MakeSelectionBorder_CalledTwice_CreatesASecondContainerOnTop()
        {
            // Documents that the helper does NOT de-duplicate: the caller owns
            // the returned container and must destroy it before re-adding, which
            // is exactly why the method returns it.
            var slot = BuildSlot(contentChildren: 1);
            int contentBefore = slot.childCount;

            var first  = EditorUIHelpers.MakeSelectionBorder(slot);
            var second = EditorUIHelpers.MakeSelectionBorder(slot);

            Assert.AreNotSame(first, second,
                "Each call returns its own container — callers must destroy the previous one.");
            Assert.AreEqual(contentBefore + 2, slot.childCount,
                "Two calls leave two frames; a picker that forgets to destroy leaks them.");
            Assert.AreEqual(slot.childCount - 1, second.transform.GetSiblingIndex(),
                "The most recent frame must still be pushed to the last sibling slot.");
        }

        [Test]
        public void MakeSelectionBorder_DestroyingReturnedContainer_RemovesTheWholeFrame()
        {
            // Deselection path: pickers drop the highlight by destroying the
            // returned GameObject. Nothing may survive under the slot.
            var slot = BuildSlot(contentChildren: 2);
            var border = EditorUIHelpers.MakeSelectionBorder(slot);

            Object.DestroyImmediate(border);

            Assert.AreEqual(2, slot.childCount,
                "Destroying the container must restore the slot to its content children only.");
            Assert.AreEqual(0, slot.GetComponentsInChildren<Graphic>(true).Length,
                "No strip may outlive its container — a leftover strip would keep " +
                "painting a highlight on a deselected slot.");
        }
    }
}
