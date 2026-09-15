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
    /// <summary>ChatUIBuilderTests: the panel and rows tests. SetUp, TearDown and helpers live in ChatUIBuilderTests.cs.</summary>
    public partial class ChatUIBuilderTests
    {
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
            Assert.AreEqual(new Vector2(700f, 312f), rt.sizeDelta,
                "With nothing remembered, the panel must open at the documented default " +
                "PANEL_DEFAULT_W x PANEL_DEFAULT_H (700x312): the ported 520x250 raised by a " +
                "quarter, then 48px wider so that widening the portrait column took its space " +
                "from the panel rather than from the conversation. The fixture clears the " +
                "size prefs, so this is the DEFAULT path, not the restore path.");
        }

        [Test]
        public void BuildUI_Panel_OpensAtTheSizeThePlayerLeftIt()
        {
            PlayerPrefs.SetInt(LayoutVersionPrefKey, CurrentLayoutVersion);
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
            PlayerPrefs.SetInt(LayoutVersionPrefKey, CurrentLayoutVersion);
            PlayerPrefs.SetFloat("valkur.chat.panel.width", 10f);
            PlayerPrefs.SetFloat("valkur.chat.panel.height", 10f);

            Rebuild();

            var size = Panel.GetComponent<RectTransform>().sizeDelta;
            Assert.GreaterOrEqual(size.x, 470f, "PANEL_MIN_W is the floor.");
            Assert.GreaterOrEqual(size.y, 293f, "PANEL_MIN_H is the floor.");
        }

        [Test]
        public void BuildUI_Panel_RefusesARememberedSizeLargerThanTheWindow()
        {
            PlayerPrefs.SetInt(LayoutVersionPrefKey, CurrentLayoutVersion);
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

            // Read top to bottom: the title, the conversation, the offer on the table, and
            // the box you answer in. That is the whole vertical stack — every control now
            // floats, either in the top-right corner or in the left gutter.
            //
            // TradeConfirmRow sits directly UNDER the conversation and above the input,
            // where the offer it is confirming was just spoken. It is built inactive and
            // appears only while a trade is on the table.
            //
            // There is no Cerrar row. Escape, the backdrop and the corner X all close the
            // panel, and a full-width red bar cost a row of the conversation to say what an
            // X says by being an X.
            //
            // TradeButton, JournalButton and ResetButton are free-floating in the LEFT gutter
            // — under the face, then under each other, then at the foot of the column — and
            // CloseXButton and LangButton in the top-right corner. All of them are
            // ignoreLayout, and they come after the rows because sibling order is draw order
            // and nothing in the layout arranges them.
            //
            // Portrait comes after those, and for the same reason one step further: unlike
            // the corner buttons it occupies a gutter every OTHER row was shortened to make,
            // so it overlaps more of the panel than any of them.
            //
            // JournalOverlay is last of all, because it covers the whole conversation
            // including the gutter's own rows. Anything built after it would draw on top of
            // the archive the player is reading.
            CollectionAssert.AreEqual(
                new[] { "MsgRow", "ScrollArea", "TradeConfirmRow", "InputRow", "TradeButton", "QuestsButton", "JournalButton", "ResetButton", "ResizeGrip", "CloseXButton", "LangButton", "Portrait", "QuestOverlay", "JournalOverlay" }, names,
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

        /// <summary>
        /// Every scroll content in this panel is stretched across its viewport, and a
        /// stretched rect's <c>sizeDelta.x</c> is not a width — it is a SURPLUS over the
        /// parent. So the value has to be zero, and a fresh <c>RectTransform</c> is born
        /// with a hundred.
        ///
        /// <para>That is exactly how the Misiones sheet shipped: it copied the anchor and
        /// pivot lines from the Diario and not the one after them, so the list hung a
        /// hundred pixels wider than its own mask and — with the centre pivot splitting the
        /// surplus — every row lost its first fifty pixels. On screen that read as
        /// "Plaga e|n la despensa", the same seven characters missing from every line
        /// including the section headers.</para>
        ///
        /// <para>Written over ALL of them by NAME rather than as three separate tests,
        /// because the failure is a line nobody wrote rather than a line somebody broke:
        /// the next overlay built here inherits the assertion instead of the bug.</para>
        /// </summary>
        [Test]
        public void BuildUI_EveryScrollContent_AddsNoWidthOverItsViewport()
        {
            string[] contents = { "Content", "JournalContent", "QuestContent" };
            int checkedCount = 0;

            foreach (var rt in CanvasGo.GetComponentsInChildren<RectTransform>(true))
            {
                if (System.Array.IndexOf(contents, rt.name) < 0) continue;
                checkedCount++;

                Assert.AreEqual(0f, rt.anchorMin.x, 0.0001f, rt.name + " must stretch from the left edge.");
                Assert.AreEqual(1f, rt.anchorMax.x, 0.0001f, rt.name + " must stretch to the right edge.");
                Assert.AreEqual(0f, rt.sizeDelta.x, 0.0001f,
                    rt.name + ".sizeDelta.x is a surplus over the viewport, not a width. "
                    + "The RectTransform default of 100 hangs the list 50 px off each side of "
                    + "its own mask and cuts the first characters off every row.");
            }

            Assert.AreEqual(contents.Length, checkedCount,
                "One of the scroll contents was renamed or is no longer built - this test would "
                + "then be silently checking fewer rects than it claims.");
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
        public void BuildUI_HasNoFullWidthCloseStrip()
        {
            foreach (Transform child in Panel.transform)
                Assert.AreNotEqual("CloseButton", child.name,
                    "The full-width Cerrar strip is deliberately gone. Escape, the backdrop " +
                    "and the corner X all close this panel; the strip cost a row of the " +
                    "conversation to say what an X says by being an X.");
        }

        [Test]
        public void BuildUI_GutterButtons_FloatWithSeparateImageAndLabelObjects()
        {
            foreach (var name in new[] { "TradeButton", "QuestsButton", "JournalButton", "ResetButton" })
            {
                var go = Child(Panel, name);

                Assert.IsNotNull(go.GetComponent<Image>(), name + " needs an Image background.");
                Assert.IsNotNull(go.GetComponent<Button>(), name + " needs a Button component.");
                Assert.IsNull(go.GetComponent<TextMeshProUGUI>(),
                    "Image + TextMeshProUGUI on the same GameObject throws a NullReferenceException in Unity 2022.3.");

                var element = go.GetComponent<LayoutElement>();
                Assert.IsTrue(element != null && element.ignoreLayout,
                    name + " lives in the LEFT GUTTER, which is space made by the layout " +
                    "group's padding rather than a row. Without ignoreLayout the group " +
                    "claims the rect and hands it the full panel width at the bottom of the " +
                    "stack — which is exactly what happened to the LangButton.");

                var rt = (RectTransform)go.transform;
                Assert.AreEqual(0f, rt.anchorMin.x,
                    name + " anchors to the panel's LEFT edge, where the gutter is.");
                Assert.AreEqual(Const("GUTTER_BUTTON_WIDTH"), rt.sizeDelta.x,
                    name + " shares the portrait's width so the gutter reads as one column.");

                Assert.IsTrue(Child(go, "Text").GetComponent<TextMeshProUGUI>() != null,
                    "The " + name + " label must be a TMP child.");
            }
        }

        [Test]
        public void BuildUI_ResetButton_SitsInTheBottomLeftCornerAndDoesNotElideItsLabel()
        {
            var reset = Child(Panel, "ResetButton");
            var rt = (RectTransform)reset.transform;

            Assert.AreEqual(Vector2.zero, rt.anchorMin, "Reset anchors to the bottom-left corner.");
            Assert.AreEqual(Vector2.zero, rt.pivot,
                "The pivot must match the anchor or the button hangs off the panel's edge.");

            var label = Child(reset, "Text").GetComponent<TextMeshProUGUI>();
            Assert.IsTrue(label.enableWordWrapping,
                "A control that DELETES player memory must never be the one whose label is " +
                "cut to 'Reiniciar m...'. Both captions fit on one line in the widened " +
                "column — measured at 81px and 110px against a 126px box — so this does not " +
                "bite today; it is what makes a longer caption overflow VISIBLY tomorrow " +
                "instead of losing its own end.");
            Assert.AreNotEqual(TextOverflowModes.Ellipsis, label.overflowMode,
                "Wrapping and eliding are alternatives; eliding here would defeat the wrap.");
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
    }
}
