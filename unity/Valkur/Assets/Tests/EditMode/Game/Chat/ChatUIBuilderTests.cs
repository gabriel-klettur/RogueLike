using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Gameplay.Chat;

namespace Valkur.Tests.EditMode.Game.Chat
{
    /// <summary>
    /// Exercises <c>ChatUI.BuildUI</c> (ChatUI.Builder.cs) and the private references that the
    /// rest of <see cref="ChatUI"/> (ChatUI.cs) dereferences without any null guard.
    ///
    /// Why this matters: BuildUI runs exactly once, from Start(), and every later code path
    /// (OnChatOpened, OnChatClosed, OnMessageReceived, SubmitInput, ToggleLang, Update) assumes
    /// the hierarchy it produced is complete and correctly wired. A silent regression in the
    /// builder - a missing child, a ScrollRect whose viewport/content was never assigned, an
    /// Image and a TextMeshProUGUI landing on the same GameObject - does not surface until the
    /// chat panel is opened in play mode, where it shows up as a NullReferenceException or as
    /// an invisible / unscrollable panel.
    ///
    /// Start() does not run in EditMode, so BuildUI is invoked directly through reflection.
    /// The whole hierarchy hangs off the ChatUI GameObject, which TearDown destroys.
    /// </summary>
    [TestFixture]
    public class ChatUIBuilderTests
    {
        private GameObject _hostGo;
        private ChatUI _ui;

        /// <summary>
        /// Keys the panel remembers its size under. Cleared around every test, because
        /// PlayerPrefs is MACHINE state, not fixture state: it survives the run, the Editor
        /// and the reboot. Without this, resizing the chat window once by hand — in the
        /// Editor, in a build, on any machine that ever ran this suite — would leave the
        /// default-size assertion below failing forever, and it would fail for a reason
        /// nothing in the test names.
        /// </summary>
        private static readonly string[] PanelSizePrefKeys =
        {
            "valkur.chat.panel.width",
            "valkur.chat.panel.height",
        };

        private static void ClearPanelSizePrefs()
        {
            for (int i = 0; i < PanelSizePrefKeys.Length; i++)
                PlayerPrefs.DeleteKey(PanelSizePrefKeys[i]);
        }

        [SetUp]
        public void SetUp()
        {
            // Building UGUI/TMP objects in EditMode emits assorted initialisation noise.
            LogAssert.ignoreFailingMessages = true;

            ClearPanelSizePrefs();

            // Defensive: a leaked ChatUI from another fixture would make the singleton
            // duplicate-guard call Destroy() (illegal in EditMode) from inside our Awake.
            if (ChatUI.HasInstance && ChatUI.Instance != null)
                UnityEngine.Object.DestroyImmediate(ChatUI.Instance.gameObject);
            ClearSingleton<ChatUI>();

            // The three button-callback tests below require ChatSystem.Instance to be
            // absent. HasInstance is a Unity fake-null check, so a *destroyed* leaked
            // instance reports false while ChatSystem.Instance?.CloseChat() still
            // dereferences it and throws MissingReferenceException. Null the static
            // outright so the precondition is real rather than probabilistic.
            ClearSingleton<ChatSystem>();

            _hostGo = new GameObject("ChatUIHost");
            _ui = _hostGo.AddComponent<ChatUI>();

            BuildUI();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hostGo != null) UnityEngine.Object.DestroyImmediate(_hostGo);
            _hostGo = null;
            _ui = null;
            // Don't leak our ChatUI into the next fixture: OnDestroy only nulls the
            // static when Unity actually delivers the message, which EditMode does
            // not guarantee for components added via AddComponent.
            ClearSingleton<ChatUI>();

            // Leaving a size behind would hand it to the NEXT fixture that builds a panel.
            ClearPanelSizePrefs();
            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>Nulls SingletonMonoBehaviour&lt;T&gt;'s private static _instance slot.</summary>
        private static void ClearSingleton<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var fi = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (fi != null) { fi.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }

        // -------------------------------------------------------------------------
        // Reflection helpers - BuildUI and every reference it fills are private.
        // -------------------------------------------------------------------------

        private void BuildUI()
        {
            var mi = typeof(ChatUI).GetMethod("BuildUI", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mi, "ChatUI.BuildUI() was renamed or removed - the builder contract changed.");
            Invoke(mi, _ui, null);
        }

        /// <summary>
        /// Throws away the panel SetUp built and builds a fresh one, for the tests that need
        /// the builder to read state written after SetUp ran.
        ///
        /// A second <c>BuildUI()</c> would do nothing — it opens with <c>if (_isBuilt) return;</c>
        /// — so the host has to go with it. Without this the remembered-size tests would set a
        /// preference, assert against the panel built before it existed, and pass or fail on
        /// the default every time.
        /// </summary>
        private void Rebuild()
        {
            if (_hostGo != null) UnityEngine.Object.DestroyImmediate(_hostGo);
            ClearSingleton<ChatUI>();

            _hostGo = new GameObject("ChatUIHost");
            _ui = _hostGo.AddComponent<ChatUI>();
            BuildUI();
        }

        private void AppendMessageRow(string sender, string text)
        {
            var mi = typeof(ChatUI).GetMethod("AppendMessageRow", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(mi, "ChatUI.AppendMessageRow(string,string) was renamed or removed.");
            Invoke(mi, _ui, new object[] { sender, text });
        }

        /// <summary>Invokes and unwraps TargetInvocationException so failures report the real error.</summary>
        private static void Invoke(MethodBase method, object target, object[] args)
        {
            try
            {
                method.Invoke(target, args);
            }
            catch (TargetInvocationException tie)
            {
                throw tie.InnerException ?? tie;
            }
        }

        private T Field<T>(string name)
        {
            var fi = typeof(ChatUI).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(fi, "ChatUI field '" + name + "' is missing - ChatUI.cs and ChatUI.Builder.cs are out of sync.");
            return (T)fi.GetValue(_ui);
        }

        private GameObject CanvasGo => Field<Canvas>("_canvas").gameObject;
        private GameObject Panel => Field<GameObject>("_panel");
        private GameObject Backdrop => Field<GameObject>("_backdrop");

        private static GameObject Child(GameObject parent, string path)
        {
            var t = parent.transform.Find(path);
            Assert.IsTrue(t != null, "Expected child '" + path + "' under '" + parent.name + "' - hierarchy changed.");
            return t.gameObject;
        }

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
                "Reference resolution is the design baseline the 520x250 panel size was authored against.");
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
                "Escape, the backdrop and the Cerrar strip all close this panel, and none of " +
                "them is the control a player LOOKS for — a window with no X in its corner " +
                "reads as one you are stuck in.");

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

            Assert.AreEqual(new Vector2(320f, 244f), grip.MinSize,
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

        // -------------------------------------------------------------------------
        // Panel
        // -------------------------------------------------------------------------

        [Test]
        public void BuildUI_Panel_AnchorsBottomLeftAtDefaultSize()
        {
            var rt = Panel.GetComponent<RectTransform>();

            Assert.AreEqual(Vector2.zero, rt.anchorMin, "The panel anchors to the bottom-left corner.");
            Assert.AreEqual(Vector2.zero, rt.anchorMax, "The panel anchors to the bottom-left corner.");
            Assert.AreEqual(Vector2.zero, rt.pivot,
                "The pivot must match the anchor, otherwise the panel drifts half off-screen.");
            Assert.AreEqual(new Vector2(20f, 20f), rt.anchoredPosition,
                "The panel sits 20px in from the bottom-left corner.");
            Assert.AreEqual(new Vector2(520f, 250f), rt.sizeDelta,
                "With nothing remembered, the panel must open at the documented default " +
                "PANEL_DEFAULT_W x PANEL_DEFAULT_H (520x250). The fixture clears the size " +
                "prefs, so this is the DEFAULT path, not the restore path.");
        }

        [Test]
        public void BuildUI_Panel_OpensAtTheSizeThePlayerLeftIt()
        {
            PlayerPrefs.SetFloat("valkur.chat.panel.width", 640f);
            PlayerPrefs.SetFloat("valkur.chat.panel.height", 300f);

            Rebuild();

            Assert.AreEqual(new Vector2(640f, 300f),
                Panel.GetComponent<RectTransform>().sizeDelta,
                "A remembered size must survive the session. Persisting on drag-end and " +
                "never reading it back is the same as not persisting at all.");
        }

        [Test]
        public void BuildUI_Panel_RefusesARememberedSizeBelowItsOwnFloor()
        {
            PlayerPrefs.SetFloat("valkur.chat.panel.width", 10f);
            PlayerPrefs.SetFloat("valkur.chat.panel.height", 10f);

            Rebuild();

            var size = Panel.GetComponent<RectTransform>().sizeDelta;
            Assert.GreaterOrEqual(size.x, 320f, "PANEL_MIN_W is the floor.");
            Assert.GreaterOrEqual(size.y, 244f, "PANEL_MIN_H is the floor.");
        }

        [Test]
        public void BuildUI_Panel_RefusesARememberedSizeLargerThanTheWindow()
        {
            PlayerPrefs.SetFloat("valkur.chat.panel.width", 9000f);
            PlayerPrefs.SetFloat("valkur.chat.panel.height", 9000f);

            Rebuild();

            var size = Panel.GetComponent<RectTransform>().sizeDelta;
            Assert.LessOrEqual(size.x, Screen.width,
                "A size saved on a large monitor and restored into a small window would " +
                "reach past the edge, taking the close button with it — and the one control " +
                "that gets a window unstuck is the last that may land off screen.");
            Assert.LessOrEqual(size.y, Screen.height);
        }

        [Test]
        public void BuildUI_Panel_HasOpaqueBackgroundAndNonExpandingVerticalLayout()
        {
            var img = Panel.GetComponent<Image>();
            var vlg = Panel.GetComponent<VerticalLayoutGroup>();

            Assert.IsNotNull(img, "The panel needs a background Image or the chat text sits on raw gameplay.");
            Assert.Greater(img.color.a, 0.5f,
                "The panel background must stay mostly opaque for the message text to be readable.");
            Assert.IsNotNull(vlg, "Panel rows are stacked by a VerticalLayoutGroup.");
            Assert.IsFalse(vlg.childForceExpandHeight,
                "Force-expanding height would stretch the title/input rows and squash the message area.");
        }

        [Test]
        public void BuildUI_Panel_ContainsExpectedRowsInOrder()
        {
            var names = new List<string>();
            foreach (Transform child in Panel.transform) names.Add(child.name);

            // The first five rows are laid out by the VerticalLayoutGroup in this exact order;
            // LangButton is deliberately last and free-floating in the top-right corner.
            //
            // TradeButton sits between the input row and Close: it is the only way into a
            // vendor's shop, since NPCInteractable.Interact() has no other caller. It is
            // built inactive and OnChatOpened shows it only when the character being talked
            // to actually sells something, so a conversation with Felipondor still presents
            // the four rows this test's earliest revision expected.
            //
            // TradeConfirmRow sits directly under the input, where the offer it is confirming
            // was just spoken. It is built inactive and appears only while the character has
            // a trade on the table.
            //
            // ResetButton follows, deliberately at the bottom next to Close and away from the
            // conversation: it is the only control in this panel that destroys player data,
            // and it takes two clicks.
            //
            // CloseXButton and LangButton are the two free-floating corner controls and are
            // last for that reason — sibling order is draw order, and nothing in the layout
            // arranges them, so they must be drawn over the rows they overlap.
            //
            // Portrait is last of all, and for the same reason one step further: it is also
            // free-floating (ignoreLayout), but unlike the corner buttons it occupies a
            // gutter every OTHER row was shortened to make, so it overlaps more of the panel
            // than either of them. Built earlier it would render underneath the conversation
            // it sits beside.
            CollectionAssert.AreEqual(
                new[] { "MsgRow", "ScrollArea", "InputRow", "TradeButton", "TradeConfirmRow", "ResetButton", "CloseButton", "ResizeGrip", "CloseXButton", "LangButton", "Portrait" }, names,
                "Panel row order defines the whole visual layout - reordering rearranges the panel.");
        }

        // -------------------------------------------------------------------------
        // Scroll area / content wiring
        // -------------------------------------------------------------------------

        [Test]
        public void BuildUI_ScrollRect_WiresViewportAndContent()
        {
            var scroll = Field<ScrollRect>("_scrollRect");
            var content = Field<RectTransform>("_contentRect");
            var scrollArea = Child(Panel, "ScrollArea");

            Assert.IsTrue(scroll != null, "_scrollRect must be assigned - OnMessageReceived auto-scrolls through it.");
            Assert.AreSame(content, scroll.content,
                "ScrollRect.content must be the Content rect, otherwise new messages never scroll into view.");
            Assert.AreSame(scrollArea.GetComponent<RectTransform>(), scroll.viewport,
                "ScrollRect.viewport must be the ScrollArea rect or content clipping is computed against the wrong rect.");
            Assert.IsFalse(scroll.horizontal,
                "Chat scrolls vertically only; horizontal scrolling would let message rows slide out of view.");
        }

        [Test]
        public void BuildUI_ScrollArea_MasksContentAndFlexesToFillPanel()
        {
            var scrollArea = Child(Panel, "ScrollArea");
            var le = scrollArea.GetComponent<LayoutElement>();

            Assert.IsNotNull(scrollArea.GetComponent<Mask>(),
                "Without a Mask the message rows draw outside the panel bounds.");
            Assert.IsNotNull(scrollArea.GetComponent<Image>(),
                "Mask requires a Graphic on the same GameObject to define the mask rect.");
            Assert.IsNotNull(le, "ScrollArea needs a LayoutElement to claim the panel's leftover height.");
            Assert.AreEqual(1f, le.flexibleHeight, 0.0001f,
                "flexibleHeight=1 is what makes the message list absorb the remaining panel height.");
            Assert.Greater(le.minHeight, 0f,
                "A minHeight guard keeps the message list visible when the panel is shrunk.");
        }

        [Test]
        public void BuildUI_Content_IsTopAnchoredChildOfScrollAreaWithSizeFitter()
        {
            var content = Field<RectTransform>("_contentRect");
            var scrollArea = Child(Panel, "ScrollArea");

            Assert.AreSame(scrollArea.transform, content.transform.parent,
                "Content must live inside the ScrollArea or it is neither masked nor scrolled.");
            Assert.AreEqual(new Vector2(0f, 1f), content.anchorMin, "Content grows downward from the top edge.");
            Assert.AreEqual(new Vector2(1f, 1f), content.anchorMax, "Content spans the full viewport width.");
            Assert.AreEqual(new Vector2(0.5f, 1f), content.pivot,
                "A top pivot is required for the oldest-at-the-top, newest-at-the-bottom message flow.");

            var fitter = content.GetComponent<ContentSizeFitter>();
            Assert.IsNotNull(fitter, "Without a ContentSizeFitter the content never grows and scrolling is dead.");
            Assert.AreEqual(ContentSizeFitter.FitMode.PreferredSize, fitter.verticalFit,
                "Vertical fit must be PreferredSize so content height tracks the number of message rows.");

            var vlg = content.GetComponent<VerticalLayoutGroup>();
            Assert.IsNotNull(vlg, "Message rows are stacked by a VerticalLayoutGroup on Content.");
            Assert.IsFalse(vlg.childForceExpandHeight,
                "Force-expanding message rows would give every single message the full viewport height.");
        }

        // -------------------------------------------------------------------------
        // Input row
        // -------------------------------------------------------------------------

        [Test]
        public void BuildUI_InputRow_ContainsInputFieldThenSendButton()
        {
            var names = new List<string>();
            foreach (Transform child in Child(Panel, "InputRow").transform) names.Add(child.name);

            CollectionAssert.AreEqual(new[] { "InputField", "SendButton" }, names,
                "Input row order must stay [InputField, SendButton] - the send button belongs on the right.");
        }

        [Test]
        public void BuildUI_InputField_WiresViewportTextAndPlaceholder()
        {
            var input = Field<TMP_InputField>("_inputField");

            Assert.IsTrue(input != null, "_inputField must be assigned - Update() and SubmitInput() dereference it.");
            Assert.IsTrue(input.textComponent != null,
                "A TMP_InputField without a textComponent throws the moment the player types.");
            Assert.IsTrue(input.placeholder != null,
                "The placeholder must be wired or the hint text never appears.");
            Assert.IsTrue(input.textViewport != null,
                "textViewport must be wired or the caret and text are not clipped to the field.");
            Assert.AreEqual("Text Area", input.textViewport.name,
                "textViewport must be the dedicated 'Text Area' child, not the field root.");
            Assert.AreSame(input.textViewport, input.textComponent.transform.parent,
                "The text component must be a child of the viewport that clips it.");
        }

        [Test]
        public void BuildUI_InputField_TextAndPlaceholderAreSeparateGameObjects()
        {
            var input = Field<TMP_InputField>("_inputField");

            Assert.AreNotSame(input.textComponent.gameObject, input.placeholder.gameObject,
                "Text and Placeholder must be distinct objects; TMP shows/hides the placeholder independently.");
            Assert.IsTrue(string.IsNullOrEmpty(input.textComponent.text),
                "The input field must start empty - prefilled text would be sent on the first Enter press.");
        }

        [Test]
        public void BuildUI_SendButton_HasSeparateImageAndLabelObjects()
        {
            var send = Child(Panel, "InputRow/SendButton");

            Assert.IsNotNull(send.GetComponent<Image>(), "SendButton needs an Image to be visible and clickable.");
            Assert.IsNotNull(send.GetComponent<Button>(), "SendButton needs a Button component.");
            Assert.IsNull(send.GetComponent<TextMeshProUGUI>(),
                "Image + TextMeshProUGUI on the same GameObject throws a NullReferenceException in Unity 2022.3.");

            var label = Child(send, "Text").GetComponent<TextMeshProUGUI>();
            Assert.IsTrue(label != null, "The SendButton label must be a TMP child.");
            Assert.AreEqual("Enviar", label.text, "The send button caption is part of the UI contract.");
        }

        // -------------------------------------------------------------------------
        // Close and language buttons
        // -------------------------------------------------------------------------

        [Test]
        public void BuildUI_CloseButton_HasSeparateImageAndLabelObjects()
        {
            var close = Child(Panel, "CloseButton");

            Assert.IsNotNull(close.GetComponent<Image>(), "CloseButton needs an Image background.");
            Assert.IsNotNull(close.GetComponent<Button>(), "CloseButton needs a Button component.");
            Assert.IsNull(close.GetComponent<TextMeshProUGUI>(),
                "Image + TextMeshProUGUI on the same GameObject throws a NullReferenceException in Unity 2022.3.");

            var label = Child(close, "Text").GetComponent<TextMeshProUGUI>();
            Assert.IsTrue(label != null, "The CloseButton label must be a TMP child.");
            StringAssert.Contains("ESC", label.text,
                "The close caption advertises the ESC shortcut - dropping it hides the keyboard affordance.");
        }

        [Test]
        public void BuildUI_LangButton_ShowsSpanishByDefaultOnASeparateLabelObject()
        {
            var langBtn = Child(Panel, "LangButton");
            var langText = Field<TextMeshProUGUI>("_langButtonText");

            Assert.IsNotNull(langBtn.GetComponent<Image>(), "LangButton needs an Image background.");
            Assert.IsNotNull(langBtn.GetComponent<Button>(), "LangButton needs a Button component.");
            Assert.IsNull(langBtn.GetComponent<TextMeshProUGUI>(),
                "Image + TextMeshProUGUI on the same GameObject throws a NullReferenceException in Unity 2022.3.");

            Assert.IsTrue(langText != null, "_langButtonText must be assigned - ToggleLang writes into it.");
            Assert.AreSame(langBtn.transform, langText.transform.parent,
                "The language label must be the LangButton's child so it moves with the button.");
            Assert.AreEqual("ES", langText.text,
                "The default label is ES, matching NPCMemory.preferredLanguage's 'es' default.");
        }

        [Test]
        public void BuildUI_LangButton_AnchorsToPanelTopRightWithExplicitSize()
        {
            var rt = Child(Panel, "LangButton").GetComponent<RectTransform>();

            Assert.AreEqual(Vector2.one, rt.anchorMin, "LangButton anchors to the panel's top-right corner.");
            Assert.AreEqual(Vector2.one, rt.anchorMax, "LangButton anchors to the panel's top-right corner.");
            Assert.AreEqual(Vector2.one, rt.pivot, "The pivot must match the anchor to keep the button inside the panel.");
            Assert.AreNotEqual(Vector2.zero, rt.sizeDelta,
                "LangButton floats outside the layout group, so it needs an explicit size or it collapses to nothing.");
        }

        // -------------------------------------------------------------------------
        // Title row and the references ChatUI.cs consumes
        // -------------------------------------------------------------------------

        [Test]
        public void BuildUI_TitleRow_HasDefaultCaptionAndNoBackgroundImage()
        {
            var title = Field<TextMeshProUGUI>("_titleText");

            Assert.IsTrue(title != null, "_titleText must be assigned - OnChatOpened writes the NPC name into it.");
            Assert.AreEqual("Chat — NPC", title.text,
                "The default title must match the 'Chat - <npc>' format that OnChatOpened later overwrites.");
            Assert.IsNull(title.GetComponent<Image>(),
                "The title row must not carry an Image on the same GameObject as its TMP component.");
        }

        [Test]
        public void BuildUI_Always_AssignsEveryReferenceDereferencedByChatUI()
        {
            // Exactly the fields ChatUI.cs uses without a null guard, in
            // OnChatOpened / OnChatClosed / OnMessageReceived / SubmitInput.
            Assert.IsTrue(Field<Canvas>("_canvas") != null, "_canvas must be assigned by BuildUI.");
            Assert.IsTrue(Field<GameObject>("_panel") != null, "_panel is dereferenced by OnChatOpened/OnChatClosed.");
            Assert.IsTrue(Field<GameObject>("_backdrop") != null, "_backdrop is toggled together with the panel.");
            Assert.IsTrue(Field<ScrollRect>("_scrollRect") != null, "_scrollRect is used for the auto-scroll on new messages.");
            Assert.IsTrue(Field<RectTransform>("_contentRect") != null, "_contentRect parents every message row.");
            Assert.IsTrue(Field<TMP_InputField>("_inputField") != null, "_inputField is read in Update and SubmitInput.");
            Assert.IsTrue(Field<TextMeshProUGUI>("_titleText") != null, "_titleText is written in OnChatOpened.");
            Assert.IsTrue(Field<TextMeshProUGUI>("_langButtonText") != null, "_langButtonText is written in ToggleLang.");
        }

        [Test]
        public void BuildUI_Always_LeavesMessageRowListEmpty()
        {
            var rows = Field<List<GameObject>>("_messageRows");

            Assert.IsNotNull(rows, "_messageRows must be initialised before any message can arrive.");
            Assert.AreEqual(0, rows.Count,
                "BuildUI must not seed message rows; history is replayed by OnChatOpened instead.");
        }

        // -------------------------------------------------------------------------
        // The Image + TMP separation rule, enforced over the whole tree
        // -------------------------------------------------------------------------

        [Test]
        public void BuildUI_Always_KeepsImageAndTextMeshProOnSeparateGameObjects()
        {
            var offenders = new List<string>();
            foreach (var t in CanvasGo.GetComponentsInChildren<Transform>(true))
            {
                var go = t.gameObject;
                if (go.GetComponent<Image>() != null && go.GetComponent<TextMeshProUGUI>() != null)
                    offenders.Add(go.name);
            }

            CollectionAssert.IsEmpty(offenders,
                "Image + TextMeshProUGUI on one GameObject throws an NRE in Unity 2022.3 - the label must live "
                + "on a child. Offending objects: " + string.Join(", ", offenders));
        }

        // -------------------------------------------------------------------------
        // Idempotency (the _isBuilt guard)
        // -------------------------------------------------------------------------

        [Test]
        public void BuildUI_CalledTwice_DoesNotDuplicateOrRebuildTheHierarchy()
        {
            var canvasBefore = CanvasGo;
            var panelBefore = Panel;
            var inputBefore = Field<TMP_InputField>("_inputField");
            int canvasChildrenBefore = canvasBefore.transform.childCount;
            int panelChildrenBefore = panelBefore.transform.childCount;

            BuildUI();

            Assert.AreEqual(1, _hostGo.transform.childCount,
                "A second BuildUI must not create a second ChatCanvas - that is what the _isBuilt guard is for.");
            Assert.AreSame(canvasBefore, CanvasGo, "The canvas reference must survive a repeated BuildUI call.");
            Assert.AreSame(panelBefore, Panel, "The panel reference must survive a repeated BuildUI call.");
            Assert.AreSame(inputBefore, Field<TMP_InputField>("_inputField"),
                "Rebuilding would orphan the input field that the Enter handler still points at.");
            Assert.AreEqual(canvasChildrenBefore, canvasBefore.transform.childCount,
                "Canvas children must not be duplicated by a second BuildUI call.");
            Assert.AreEqual(panelChildrenBefore, panelBefore.transform.childCount,
                "Panel children must not be duplicated by a second BuildUI call.");
        }

        [Test]
        public void BuildUI_CalledTwice_LeavesExactlyOneObjectPerNamedNode()
        {
            BuildUI();

            var counts = new Dictionary<string, int>();
            foreach (var t in CanvasGo.GetComponentsInChildren<Transform>(true))
            {
                counts.TryGetValue(t.name, out int n);
                counts[t.name] = n + 1;
            }

            foreach (var name in new[] { "Backdrop", "ChatPanel", "ScrollArea", "Content",
                                         "InputRow", "InputField", "SendButton", "CloseButton",
                                         "LangButton", "LangLabel", "Text Area", "Placeholder" })
            {
                Assert.IsTrue(counts.ContainsKey(name), "Node '" + name + "' disappeared from the hierarchy.");
                Assert.AreEqual(1, counts[name],
                    "Node '" + name + "' must exist exactly once - a duplicate means BuildUI really ran twice.");
            }
        }

        // -------------------------------------------------------------------------
        // Button callbacks must survive a missing ChatSystem
        // -------------------------------------------------------------------------

        [Test]
        public void CloseAndBackdropButtons_WithNoChatSystem_DoNotThrow()
        {
            // Both handlers rely on ChatSystem.Instance?.CloseChat(). Losing the null-conditional
            // would throw on any click made before ChatSystem exists in the scene.
            Assert.IsFalse(ChatSystem.HasInstance,
                "SetUp clears the ChatSystem singleton; a live one here means another "
                + "fixture leaked it and the null-guard paths below would not be exercised.");

            var closeBtn = Child(Panel, "CloseButton").GetComponent<Button>();
            var backdropBtn = Backdrop.GetComponent<Button>();

            Assert.DoesNotThrow(() => closeBtn.onClick.Invoke(),
                "The close button must tolerate a missing ChatSystem.");
            Assert.DoesNotThrow(() => backdropBtn.onClick.Invoke(),
                "Backdrop click-to-close must tolerate a missing ChatSystem.");
        }

        [Test]
        public void SendButton_WithEmptyInputAndNoChatSystem_DoesNotThrowOrAppendARow()
        {
            Assert.IsFalse(ChatSystem.HasInstance,
                "SetUp clears the ChatSystem singleton; a live one here means another "
                + "fixture leaked it and the null-guard paths below would not be exercised.");

            var sendBtn = Child(Panel, "InputRow/SendButton").GetComponent<Button>();

            // SubmitInput() must early-out on empty text before it ever touches ChatSystem.
            Assert.DoesNotThrow(() => sendBtn.onClick.Invoke(),
                "Sending an empty message must be a no-op, not an exception.");
            Assert.AreEqual(0, Field<List<GameObject>>("_messageRows").Count,
                "An empty submit must not append a message row.");
        }

        [Test]
        public void LangButton_WithNoActiveMemory_DoesNotThrowAndKeepsLabel()
        {
            Assert.IsFalse(ChatSystem.HasInstance,
                "SetUp clears the ChatSystem singleton; a live one here means another "
                + "fixture leaked it and the null-guard paths below would not be exercised.");

            var langBtn = Child(Panel, "LangButton").GetComponent<Button>();

            Assert.DoesNotThrow(() => langBtn.onClick.Invoke(),
                "ToggleLang must early-out when there is no active NPC memory to persist the change into.");
            Assert.AreEqual("ES", Field<TextMeshProUGUI>("_langButtonText").text,
                "The label must not flip when there is no memory to write the change to.");
        }

        // -------------------------------------------------------------------------
        // AppendMessageRow - the only builder API that mutates the tree at runtime
        // -------------------------------------------------------------------------

        [Test]
        public void AppendMessageRow_PlayerSender_AddsCyanTaggedRowUnderContent()
        {
            AppendMessageRow("Player", "hola mundo");

            var rows = Field<List<GameObject>>("_messageRows");
            Assert.AreEqual(1, rows.Count, "Every appended message must be tracked so ClearMessages can dispose it.");

            var content = Field<RectTransform>("_contentRect");
            Assert.AreSame(content.transform, rows[0].transform.parent,
                "Message rows must be parented to Content or they are neither masked nor scrolled.");

            var tmp = rows[0].GetComponent<TextMeshProUGUI>();
            Assert.IsTrue(tmp != null, "A message row must carry a TextMeshProUGUI.");
            StringAssert.Contains("<color=#00FFFF>Player</color>: hola mundo", tmp.text,
                "Player messages are tagged cyan; losing the rich-text tag makes senders indistinguishable.");
            Assert.IsTrue(tmp.richText, "richText must stay on or the colour tag renders as literal markup.");
        }

        [Test]
        public void AppendMessageRow_NonPlayerSender_UsesADifferentColourThanThePlayer()
        {
            AppendMessageRow("Player", "hi");
            AppendMessageRow("Herrero", "hi");

            var rows = Field<List<GameObject>>("_messageRows");
            string playerText = rows[0].GetComponent<TextMeshProUGUI>().text;
            string npcText = rows[1].GetComponent<TextMeshProUGUI>().text;

            Assert.AreNotEqual(playerText, npcText,
                "Player and NPC lines must not render identically - the sender colour is what distinguishes them.");
            StringAssert.Contains("Herrero</color>: hi", npcText, "The NPC name must sit inside the colour tag.");
            StringAssert.DoesNotContain("00FFFF", npcText,
                "Only the player's own lines are cyan; a cyan NPC line means the sender check regressed.");
        }

        [Test]
        public void AppendMessageRow_CalledRepeatedly_KeepsInsertionOrder()
        {
            for (int i = 0; i < 5; i++) AppendMessageRow("Player", "m" + i);

            var rows = Field<List<GameObject>>("_messageRows");
            Assert.AreEqual(5, rows.Count, "Every appended row must be tracked.");

            var content = Field<RectTransform>("_contentRect");
            for (int i = 0; i < 5; i++)
            {
                Assert.AreSame(rows[i].transform, content.transform.GetChild(i),
                    "Message rows must sit in the content in append order - the VerticalLayoutGroup renders "
                    + "sibling order top to bottom, so a reversal would show the conversation backwards.");
                StringAssert.EndsWith("m" + i, rows[i].GetComponent<TextMeshProUGUI>().text,
                    "Row " + i + " must hold the i-th message.");
            }
        }

        [Test]
        public void AppendMessageRow_UnicodeAndVeryLongText_IsPreservedVerbatim()
        {
            // NPC replies are offline/LLM text: accents, CJK, emoji and long paragraphs all occur.
            string payload = "niño 你好 😀 " + new string('x', 4000);
            AppendMessageRow("Herrero", payload);

            var tmp = Field<List<GameObject>>("_messageRows")[0].GetComponent<TextMeshProUGUI>();

            StringAssert.EndsWith(payload, tmp.text,
                "Message text must not be truncated or re-encoded - long replies rely on word wrap, not clipping.");
            Assert.IsTrue(tmp.enableWordWrapping,
                "Word wrapping must stay enabled or a long reply renders as a single clipped line.");
        }

        [Test]
        public void AppendMessageRow_NullSenderAndText_DoesNotThrowAndStillAddsARow()
        {
            // Providers can hand back nulls; the row builder must survive that.
            Assert.DoesNotThrow(() => AppendMessageRow(null, null),
                "A null sender or body must not take down the chat panel.");

            var rows = Field<List<GameObject>>("_messageRows");
            Assert.AreEqual(1, rows.Count, "A null message still occupies a row rather than silently vanishing.");
            Assert.IsTrue(rows[0].GetComponent<TextMeshProUGUI>() != null,
                "The row must still be a valid TMP row even with empty content.");
        }

        [Test]
        public void AppendMessageRow_EmptySender_KeepsTheSenderTextSeparator()
        {
            AppendMessageRow("", "solo texto");

            var tmp = Field<List<GameObject>>("_messageRows")[0].GetComponent<TextMeshProUGUI>();
            StringAssert.Contains("</color>: solo texto", tmp.text,
                "The 'sender: text' separator must survive an empty sender name.");
        }
    }
}
