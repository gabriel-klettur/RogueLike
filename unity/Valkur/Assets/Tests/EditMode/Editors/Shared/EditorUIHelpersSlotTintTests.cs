using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Editors.Shared
{
    /// <summary>
    /// A picker slot's colour has an owner, and it is not the caller.
    ///
    /// <para>A slot is a <see cref="Button"/>, so it is a <c>Selectable</c> in ColorTint
    /// mode with the slot background as its <c>targetGraphic</c>. Unity therefore drives
    /// that graphic's RENDERED colour through <c>CrossFadeColor</c>, which writes the
    /// CanvasRenderer directly and ignores whatever <c>Image.color</c> happens to say. So
    /// <c>slot.GetComponent&lt;Image&gt;().color = tint</c> reads back perfectly while
    /// rendering something else entirely.</para>
    ///
    /// <para>That is what made the Spells picker's colour-coded catalog appear correct when
    /// the grid was built and then revert to one flat <c>SLOT_BG</c> a few seconds later:
    /// a canvas rebuild pushes <c>Image.color</c> through, and the next state transition
    /// Unity runs — a pointer crossing the panel, an enable cycle, one
    /// <c>OnCanvasGroupChanged</c> anywhere above the grid — takes it straight back.</para>
    /// </summary>
    public class EditorUIHelpersSlotTintTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        /// <summary>Projectile orange at the alpha the Spells grid uses.</summary>
        private static readonly Color Tint = new Color(0.95f, 0.55f, 0.20f, 0.18f);

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private Button NewSlot()
        {
            var canvas = new GameObject("slot_tint_canvas", typeof(RectTransform), typeof(Canvas));
            _spawned.Add(canvas);
            return UIButton.MakeSlot(canvas.transform, "slot", 64f, null).Item1;
        }

        private static Color Rendered(Button slot)
            => slot.GetComponent<Image>().canvasRenderer.GetColor();

        /// <summary>
        /// Everything Unity answers with a state transition: an enable cycle, and a
        /// CanvasGroup change anywhere above the slot — the one that repaints a whole grid
        /// at once rather than the tile under the pointer.
        /// </summary>
        private static void ProvokeStateTransitions(Button slot)
        {
            var group = slot.transform.parent.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.interactable = true;
            slot.enabled = false;
            slot.enabled = true;
        }

        [Test]
        public void SetSlotTint_SurvivesEveryStateTransition()
        {
            var slot = NewSlot();
            EditorUIHelpers.SetSlotTint(slot, Tint);

            Assert.AreEqual(Tint, Rendered(slot), "the tint did not reach the CanvasRenderer");

            ProvokeStateTransitions(slot);

            Assert.AreEqual(Tint, Rendered(slot),
                "Unity repainted the slot over its category tint");
        }

        [Test]
        public void WritingTheImageDirectly_DoesNotSurvive_WhichIsWhyTheHelperExists()
        {
            // Not a test of Unity — a guard on the FIX. Someone reading SetSlotTint and
            // thinking it is a long way to say `img.color = tint` needs this to fail.
            var slot = NewSlot();
            slot.GetComponent<Image>().color = Tint;

            Assert.AreNotEqual(Tint, Rendered(slot),
                "writing Image.color now survives; SetSlotTint may no longer be needed");
            Assert.AreEqual(UITheme.SLOT_BG, Rendered(slot),
                "the slot rendered something other than the Button's own normalColor");
        }

        [Test]
        public void SelectedSlot_KeepsItsTint_BecauseClickingLeavesItFocused()
        {
            // A click leaves the slot selected in the EventSystem. With selectedColor at the
            // theme default, the one tile the author is looking at is the one that loses its
            // colour — the worst possible tile to lose it on.
            var slot = NewSlot();
            EditorUIHelpers.SetSlotTint(slot, Tint);

            Assert.AreEqual(Tint, slot.colors.normalColor);
            Assert.AreEqual(Tint, slot.colors.selectedColor);
        }

        [Test]
        public void HoverIsDerivedFromTheTint_BrighterAndMoreOpaque()
        {
            // Both halves matter: a category tint can be dark (the vortex is nearly black),
            // so brightness alone barely moves it, and it is nearly transparent, so opacity
            // alone barely shows.
            var slot = NewSlot();
            EditorUIHelpers.SetSlotTint(slot, Tint);

            Color hover = slot.colors.highlightedColor;
            Assert.Greater(hover.a, Tint.a, "hover is no more opaque than rest");
            Assert.Greater(hover.r + hover.g + hover.b, Tint.r + Tint.g + Tint.b,
                "hover is no brighter than rest");

            Color.RGBToHSV(Tint, out float restHue, out _, out _);
            Color.RGBToHSV(hover, out float hoverHue, out _, out _);
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(restHue * 360f, hoverHue * 360f)), 8f,
                "hover changed the hue, so hovering reads as a different category");
        }

        [Test]
        public void PressedDefaultsToTheSharedSelectionColour()
        {
            var slot = NewSlot();
            EditorUIHelpers.SetSlotTint(slot, Tint);
            Assert.AreEqual(EditorUIHelpers.SLOT_SELECTED, slot.colors.pressedColor);
        }
    }
}
