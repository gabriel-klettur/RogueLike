using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Gameplay.Chat;

namespace Valkur.Tests.EditMode.Gameplay.Chat
{
    /// <summary>ChatUIBuilderTests: the canvas and corners tests. SetUp, TearDown and helpers live in ChatUIBuilderTests.cs.</summary>
    public partial class ChatUIBuilderTests
    {
        // -------------------------------------------------------------------------
        // Canvas
        // -------------------------------------------------------------------------

        [Test]
        public void BuildUI_Always_ParentsCanvasUnderChatUIGameObject()
        {
            // Guards a leak: an unparented canvas would survive ChatUI being destroyed
            // and keep drawing a dead chat panel over the game.
            Assert.AreEqual(1, _hostGo.transform.childCount,
                "BuildUI must create exactly one child (ChatCanvas) under the ChatUI GameObject.");
            Assert.AreSame(_hostGo.transform, CanvasGo.transform.parent,
                "ChatCanvas must be parented to the ChatUI transform so it is destroyed with it.");
            Assert.AreEqual("ChatCanvas", CanvasGo.name,
                "The canvas GameObject name is part of the hierarchy contract.");
        }

        [Test]
        public void BuildUI_Always_ConfiguresOverlayCanvasAboveGameplayHud()
        {
            var canvas = Field<Canvas>("_canvas");

            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode,
                "Chat must render as a screen-space overlay, not in world space.");
            Assert.AreEqual(200, canvas.sortingOrder,
                "sortingOrder 200 keeps chat above the HUD; lowering it hides the panel behind other UI.");
            Assert.IsNotNull(CanvasGo.GetComponent<GraphicRaycaster>(),
                "Without a GraphicRaycaster no chat button or input field can ever be clicked.");
        }

        [Test]
        public void BuildUI_Always_ScalesCanvasWithReferenceResolution()
        {
            var scaler = CanvasGo.GetComponent<CanvasScaler>();

            Assert.IsNotNull(scaler, "A CanvasScaler is required or the panel is unreadable at non-default resolutions.");
            Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode,
                "Constant-pixel scaling would make the chat panel tiny on high-DPI displays.");
            Assert.AreEqual(new Vector2(1600f, 800f), scaler.referenceResolution,
                "Reference resolution is the design baseline the panel size is authored against.");
        }

        // -------------------------------------------------------------------------
        // Top-level children: backdrop first, panel second
        // -------------------------------------------------------------------------

        [Test]
        public void BuildUI_SendButton_StaysSmallInsteadOfHalvingTheRow()
        {
            var inputRow = Child(Panel, "InputRow");
            var layout = inputRow.GetComponent<HorizontalLayoutGroup>();

            Assert.IsFalse(layout.childForceExpandWidth,
                "childForceExpandWidth defaults to TRUE and hands every child an equal share " +
                "of the leftover width regardless of what it asked for — so the Send button " +
                "ignored its preferred width and grew to half the row, dwarfing the message " +
                "field it belongs to.");

            var sendElement = Child(inputRow, "SendButton").GetComponent<LayoutElement>();
            Assert.AreEqual(0f, sendElement.flexibleWidth,
                "The button must never absorb slack; the message field's flexibleWidth takes it.");
            Assert.LessOrEqual(sendElement.preferredWidth, 80f,
                "Enter already sends. The button is the discoverable alternative, not the " +
                "main event, and it should not out-size the field it sits beside.");
        }

        [Test]
        public void BuildUI_InputField_TakesTheSlack()
        {
            var field = Child(Child(Panel, "InputRow"), "InputField").GetComponent<LayoutElement>();
            Assert.Greater(field.flexibleWidth, 0f,
                "With force-expand off, the field is the only thing that can grow — if it " +
                "does not, the row leaves empty space instead of a wider place to type.");
        }

        [Test]
        public void BuildUI_LangButton_IsExcludedFromThePanelLayout()
        {
            var lang = Child(Panel, "LangButton");
            var element = lang.GetComponent<LayoutElement>();

            Assert.IsNotNull(element,
                "LangButton is a CHILD of the panel, so the panel's VerticalLayoutGroup owns " +
                "it unless a LayoutElement opts out. Without one, the anchors that put it in " +
                "the top-right corner are overwritten.");
            Assert.IsTrue(element.ignoreLayout,
                "Measured live before this was set: the button came out 504x0 at the BOTTOM " +
                "of the stack instead of 42x22 in the top-right corner. A zero-height rect " +
                "raycasts nothing, so the language toggle was impossible to click at all, " +
                "while its 'ES' label overflowed the empty rect and rendered across the " +
                "Close button underneath.");
        }

        [Test]
        public void BuildUI_CloseX_IsClickableAndClosesTheWindow()
        {
            var closeX = Child(Panel, "CloseXButton");

            Assert.IsNotNull(closeX.GetComponent<Image>(), "The X needs an Image to be hit-tested.");
            Assert.IsNotNull(closeX.GetComponent<Button>(),
                "Escape and the backdrop also close this panel, and neither is the control " +
                "a player LOOKS for — a window with no X in its corner reads as one you are " +
                "stuck in. Since the full-width Cerrar strip was removed this is the only " +
                "close control that is VISIBLE, so it carries the whole affordance.");

            var element = closeX.GetComponent<LayoutElement>();
            Assert.IsTrue(element != null && element.ignoreLayout,
                "Without ignoreLayout the panel's VerticalLayoutGroup overwrites the corner " +
                "anchors and hands it the full width at zero height, which is what happened " +
                "to the LangButton: a control that exists, cannot be clicked, and draws its " +
                "label across its neighbours.");

            var label = closeX.transform.childCount > 0
                ? closeX.transform.GetChild(0).GetComponent<TMPro.TextMeshProUGUI>()
                : null;
            Assert.IsNotNull(label,
                "Image and TMP on the same GameObject is a NullReferenceException, so the " +
                "glyph has to be a child.");
            Assert.AreEqual("X", label.text);
        }

        [Test]
        public void BuildUI_CornerControls_DoNotOverlapEachOther()
        {
            var grip = Child(Panel, "ResizeGrip").GetComponent<RectTransform>();
            var closeX = Child(Panel, "CloseXButton").GetComponent<RectTransform>();
            var lang = Child(Panel, "LangButton").GetComponent<RectTransform>();

            // All three are pivoted on the panel's top-right, so x runs negative leftwards and
            // a control spans [anchoredPosition.x - width, anchoredPosition.x].
            float gripLeft = grip.anchoredPosition.x - grip.sizeDelta.x;
            float closeRight = closeX.anchoredPosition.x;
            float closeLeft = closeRight - closeX.sizeDelta.x;
            float langRight = lang.anchoredPosition.x;

            Assert.LessOrEqual(closeRight, gripLeft,
                "The grip owns the corner and the close button sits left of it. Overlapping " +
                "them puts a drag-to-resize on top of the button that closes the window, so " +
                "a click meant to close it stretches it instead.");
            Assert.LessOrEqual(langRight, closeLeft,
                "Nothing in uGUI arranges free-floating children of the same corner, so an " +
                "overlap is silent: the one drawn first simply stops receiving clicks. That " +
                "is exactly how the LangButton once ate the Cerrar strip's presses.");
        }

        [Test]
        public void BuildUI_ResizeGrip_IsTheSharedHandleWiredToThePanel()
        {
            var gripGo = Child(Panel, "ResizeGrip");
            var grip = gripGo.GetComponent<Valkur.UIKit.PanelResizeHandle>();

            Assert.IsNotNull(grip,
                "The chat panel must resize through the SAME PanelResizeHandle the four " +
                "resizable runtime editors use. MusicPlayerHUD already rolled its own and it " +
                "drifted — it resizes by localScale where this one uses sizeDelta.");
            Assert.AreSame(Panel.GetComponent<RectTransform>(), grip.Target,
                "A handle with no Target silently does nothing on drag.");

            Assert.AreEqual(Valkur.UIKit.ResizeGripCorner.TopRight, grip.Corner,
                "The panel is pivoted bottom-left and pinned near the bottom of the screen, " +
                "so its bottom edge cannot move. A bottom-right grip — the default, and what " +
                "every editor uses — could only ever change its width.");

            Assert.IsNotNull(gripGo.GetComponent<Valkur.UIKit.TriangleHandleGraphic>(),
                "Without a Graphic, uGUI raycasts nothing and the grip receives no pointer " +
                "events at all — so it would be invisible AND inert, for one reason.");

            var element = gripGo.GetComponent<LayoutElement>();
            Assert.IsTrue(element != null && element.ignoreLayout,
                "The panel's VerticalLayoutGroup would otherwise own the grip and hand it the " +
                "full panel width at zero height, the way it once did to the LangButton.");
        }

        [Test]
        public void BuildUI_ResizeGrip_SitsInsideThePanelItResizes()
        {
            var panelRt = Panel.GetComponent<RectTransform>();
            var gripRt = Child(Panel, "ResizeGrip").GetComponent<RectTransform>();

            var panelCorners = new Vector3[4];
            var gripCorners = new Vector3[4];
            panelRt.GetWorldCorners(panelCorners);
            gripRt.GetWorldCorners(gripCorners);

            // GetWorldCorners returns them rotated with the rect, so compare extents rather
            // than assuming corner[0] is the bottom-left.
            float panelRight = Mathf.Max(panelCorners[0].x, panelCorners[2].x);
            float panelTop = Mathf.Max(panelCorners[0].y, panelCorners[2].y);
            float gripRight = Mathf.Max(gripCorners[0].x, gripCorners[2].x);
            float gripTop = Mathf.Max(gripCorners[0].y, gripCorners[2].y);

            Assert.LessOrEqual(gripRight, panelRight + 0.01f,
                "Measured live before this was pinned: rotating the grip to point at its " +
                "corner turned it about a pivot that IS that corner, swinging the whole 16px " +
                "square outside the panel — x=[540..556] against a right edge of 540. The " +
                "glyph is mirrored in the mesh now, not by a transform.");
            Assert.LessOrEqual(gripTop, panelTop + 0.01f);

            Assert.AreEqual(Quaternion.identity, gripRt.localRotation,
                "A rotation here moves the rect, because the pivot is the corner.");
            Assert.AreEqual(Vector3.one, gripRt.localScale,
                "A negative scale would flip the triangle's winding as well as moving it.");

            var graphic = Child(Panel, "ResizeGrip").GetComponent<Valkur.UIKit.TriangleHandleGraphic>();
            Assert.AreEqual(Valkur.UIKit.ResizeGripCorner.TopRight, graphic.Corner,
                "The glyph and the drag it advertises must name the same corner, or the " +
                "triangle points one way and the panel grows the other.");
        }

        [Test]
        public void BuildUI_AtItsSmallest_TheRowsStillFitInThePanel()
        {
            AssertRowsFitAtMinimumSize(withTradeRows: false);
        }

        [Test]
        public void BuildUI_AtItsSmallest_TheRowsStillFitWithATradeOfferOnTheTable()
        {
            // The trade rows are the ones that appear without the player resizing, and the
            // confirmation appears at the exact moment they are reading something they must
            // not lose. Before SCROLL_MIN_H was lowered the message area had no give, so those
            // rows pushed the panel past its own rect — silently, because uGUI clips rather
            // than complaining, which reads as the conversation being cut off.
            AssertRowsFitAtMinimumSize(withTradeRows: true);
        }

        /// <summary>
        /// Asserts the rows' own declared heights sum to no more than <c>PANEL_MIN_H</c> holds.
        ///
        /// <para>Read off each <see cref="LayoutElement"/> rather than measured from the laid-out
        /// rects, because uGUI does not lay out in EditMode: <c>BuildUI</c> is invoked through
        /// reflection so the layout groups never get their enable-and-dirty cycle, and every
        /// row reports the RectTransform's default 100 px. A test that measured those would be
        /// asserting on nothing while looking like it asserted on everything — the same shape
        /// as the Awake/OnDestroy trap CLAUDE.md records.</para>
        ///
        /// <para>Summing the declarations still catches the drift that matters: adding a row,
        /// raising a preferred height or widening the spacing all move this total, and the
        /// hand-written constant does not.</para>
        /// </summary>
        private void AssertRowsFitAtMinimumSize(bool withTradeRows)
        {
            var vlg = Panel.GetComponent<VerticalLayoutGroup>();
            // TradeButton floats in the left gutter and is ignoreLayout, so it contributes
            // no height at all — which is the point of the column: the chrome costs the
            // conversation nothing vertically whether or not the character sells anything.
            // TradeConfirmRow is still a row, because an offer is content rather than chrome.
            Child(Panel, "TradeButton").SetActive(withTradeRows);
            Child(Panel, "TradeConfirmRow").SetActive(withTradeRows);

            float used = vlg.padding.top + vlg.padding.bottom;
            int rows = 0;
            var breakdown = new List<string>();

            foreach (Transform child in Panel.transform)
            {
                if (!child.gameObject.activeSelf) continue;

                var element = child.GetComponent<LayoutElement>();
                if (element == null || element.ignoreLayout) continue;   // the corner controls

                // A row that declares a MINIMUM cannot go below it; one that declares only a
                // preference is a fixed row and takes exactly that. This is what the VLG
                // reserves before it hands the slack to whatever is flexible.
                float needs = element.minHeight > 0f ? element.minHeight
                    : Mathf.Max(0f, element.preferredHeight);

                used += needs;
                rows++;
                breakdown.Add($"{child.name}={needs:F0}");
            }
            used += Mathf.Max(0, rows - 1) * vlg.spacing;

            var grip = Child(Panel, "ResizeGrip").GetComponent<Valkur.UIKit.PanelResizeHandle>();

            Assert.LessOrEqual(used, grip.MinSize.y,
                $"At the minimum height ({grip.MinSize.y}px) the {rows} rows need {used}px: " +
                string.Join(" + ", breakdown) + $" + {rows - 1} gaps + padding. Either raise " +
                "PANEL_MIN_H or give the message area more room to give up — a panel that " +
                "overflows does not report it, it just clips the conversation.");
        }

        [Test]
        public void BuildUI_ResizeGrip_CannotShrinkThePanelBelowItsFloorOrPastTheWindow()
        {
            var grip = Child(Panel, "ResizeGrip").GetComponent<Valkur.UIKit.PanelResizeHandle>();

            // 293 = the gutter column, which is the tallest thing in this panel and the only
            // thing that has to fit whatever the conversation gives up: 8 padding + 141 face
            // + 28 Comerciar + 28 Misiones + 28 Diario + 28 Reiniciar, with a 6px gap between
            // each and 8 padding at the foot. It is DERIVED in ChatUI from those same
            // constants, so a taller gutter control moves it — and this assertion is the
            // tripwire that makes that a deliberate change rather than a silent one.
            //
            // It rose from 259 when Misiones joined the column. The ceiling it must not
            // cross is PANEL_DEFAULT_H (312): a minimum above the default would drag every
            // player's saved panel size up through the restore clamp.
            Assert.AreEqual(new Vector2(470f, 293f), grip.MinSize,
                "PANEL_MIN_W/PANEL_MIN_H sat in ChatUI.cs unread since the Python port. They " +
                "are the floor the panel was always meant to refuse to shrink past, and this " +
                "is the call that finally makes them mean something.");

            Assert.LessOrEqual(grip.MaxSize.x, Screen.width,
                "The ceiling is the live viewport, not a constant. A constant is wrong in " +
                "both directions at once: unreachable on a small window, needlessly small " +
                "on a large one.");
            Assert.LessOrEqual(grip.MaxSize.y, Screen.height);
            Assert.GreaterOrEqual(grip.MaxSize.x, grip.MinSize.x,
                "A max below the min makes Mathf.Clamp return the MAX, silently shrinking " +
                "the panel below its own floor on a very small window.");
            Assert.GreaterOrEqual(grip.MaxSize.y, grip.MinSize.y);
        }

        [Test]
        public void BuildUI_Title_IsInsetClearOfTheCornerControls()
        {
            var title = Field<TMPro.TextMeshProUGUI>("_titleText");
            var closeX = Child(Panel, "CloseXButton").GetComponent<RectTransform>();
            var lang = Child(Panel, "LangButton").GetComponent<RectTransform>();

            // The strip the two buttons occupy, measured from the panel's right edge.
            float occupied = -(lang.anchoredPosition.x - lang.sizeDelta.x);

            Assert.GreaterOrEqual(title.margin.z, occupied,
                "A long persona name would otherwise run under the close button and the " +
                "player reads a name missing its last letters. The inset is on the TEXT's " +
                "margin because the row itself is owned by the VerticalLayoutGroup, which " +
                "overwrites any offset set on its RectTransform.");
            Assert.AreEqual(title.margin.x, title.margin.z, 0.001f,
                "The title is centred, so insetting only the right would shift the name off " +
                "centre by half the strip.");

            Assert.Greater(closeX.sizeDelta.x, 0f);
        }

        [Test]
        public void BuildUI_LangButton_KeepsItsCornerAnchors()
        {
            var rect = Child(Panel, "LangButton").GetComponent<RectTransform>();

            Assert.AreEqual(Vector2.one, rect.anchorMin, "Anchored to the panel's top-right.");
            Assert.AreEqual(Vector2.one, rect.anchorMax);
            Assert.AreEqual(Vector2.one, rect.pivot);
            Assert.Greater(rect.sizeDelta.y, 0f,
                "A zero-height button cannot be clicked, so the control exists and does " +
                "nothing — while its label, which does not clip, still draws over its " +
                "neighbours.");
        }

        [Test]
        public void BuildUI_ChatCanvas_DrawsBelowTheVendorShop()
        {
            // The shop is opened FROM the chat panel's Trade button, so it must draw over it.
            // Both canvases sat at 200, which left the winner to hierarchy order — that is,
            // to whichever bootstrap step happened to create its canvas first.
            Assert.Less(Field<Canvas>("_canvas").sortingOrder, 220,
                "ChatUI's canvas must sort below VendorShopUI's (220).");
        }

        [Test]
        public void BuildUI_Always_OrdersBackdropBeforePanel()
        {
            // Sibling order is draw order: the backdrop must sit behind the panel, otherwise
            // every click inside the panel also hits the backdrop and closes the chat.
            var names = new List<string>();
            foreach (Transform child in CanvasGo.transform) names.Add(child.name);

            CollectionAssert.AreEqual(new[] { "Backdrop", "ChatPanel" }, names,
                "Canvas children must be exactly [Backdrop, ChatPanel], in that order.");
        }

        [Test]
        public void BuildUI_Backdrop_IsFullScreenInvisibleAndRaycastable()
        {
            var rt = Backdrop.GetComponent<RectTransform>();
            var img = Backdrop.GetComponent<Image>();

            Assert.AreEqual(Vector2.zero, rt.anchorMin, "Backdrop must stretch from the bottom-left corner.");
            Assert.AreEqual(Vector2.one, rt.anchorMax, "Backdrop must stretch to the top-right corner.");
            Assert.AreEqual(Vector2.zero, rt.offsetMin, "Backdrop must have no bottom-left inset.");
            Assert.AreEqual(Vector2.zero, rt.offsetMax, "Backdrop must have no top-right inset.");

            Assert.IsNotNull(img, "The backdrop needs a Graphic to receive raycasts at all.");
            Assert.AreEqual(0f, img.color.a, 0.0001f,
                "The backdrop must stay fully transparent - any alpha dims the whole game behind the chat.");
            Assert.IsTrue(img.raycastTarget,
                "The backdrop must remain a raycast target or click-outside-to-close stops working.");
        }

        [Test]
        public void BuildUI_Backdrop_HasClickToCloseButtonWithNoTransition()
        {
            var btn = Backdrop.GetComponent<Button>();

            Assert.IsNotNull(btn, "The backdrop must carry the click-outside-to-close Button.");
            Assert.AreEqual(Selectable.Transition.None, btn.transition,
                "A colour transition on an invisible full-screen backdrop would flash the entire screen on hover.");
        }
    }
}
