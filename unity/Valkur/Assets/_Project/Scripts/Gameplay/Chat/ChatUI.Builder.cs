using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.UIKit;

namespace Valkur.Gameplay.Chat
{
    public partial class ChatUI
    {
        private void BuildUI()
        {
            if (_isBuilt) return;
            _isBuilt = true;

            // Screen-space overlay canvas
            var canvasGo = new GameObject("ChatCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 200;
            canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGo.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1600, 800);
            canvasGo.AddComponent<GraphicRaycaster>();

            // Modal backdrop — invisible fullscreen image that captures clicks
            // outside the panel and closes the chat. Must be the first child
            // so the panel (created next) draws on top and absorbs clicks
            // landing inside its rect. The backdrop is toggled together with
            // the panel in OnChatOpened/Closed — leaving it always-on would
            // make EventSystem.IsPointerOverGameObject() return true for the
            // entire screen and break things like the camera zoom.
            _backdrop = new GameObject("Backdrop");
            _backdrop.transform.SetParent(canvasGo.transform, false);
            var backdropRt = _backdrop.AddComponent<RectTransform>();
            backdropRt.anchorMin = Vector2.zero;
            backdropRt.anchorMax = Vector2.one;
            backdropRt.offsetMin = Vector2.zero;
            backdropRt.offsetMax = Vector2.zero;
            var backdropImg = _backdrop.AddComponent<Image>();
            backdropImg.color = new Color(0f, 0f, 0f, 0f); // alpha=0 → invisible but raycastable
            backdropImg.raycastTarget = true;
            var backdropBtn = _backdrop.AddComponent<Button>();
            backdropBtn.transition = Selectable.Transition.None;
            backdropBtn.onClick.AddListener(() => ChatSystem.Instance?.CloseChat());

            // Panel
            _panel = new GameObject("ChatPanel");
            _panel.transform.SetParent(canvasGo.transform, false);
            var panelRt = _panel.AddComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0f, 0f);
            panelRt.anchorMax = new Vector2(0f, 0f);
            panelRt.pivot = new Vector2(0f, 0f);
            panelRt.anchoredPosition = new Vector2(20f, 20f);
            panelRt.sizeDelta = ResolveStartingPanelSize();

            var panelImg = _panel.AddComponent<Image>();
            panelImg.color = new Color(0.08f, 0.08f, 0.12f, 0.92f);

            var vlg = _panel.AddComponent<VerticalLayoutGroup>();
            _panelLayout = vlg;
            vlg.padding = new RectOffset(
                (int)PANEL_PADDING, (int)PANEL_PADDING, (int)PANEL_PADDING, (int)PANEL_PADDING);
            vlg.spacing = PANEL_SPACING;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // Title bar
            var titleGo = CreateTextRow(_panel.transform, "Chat — NPC", 18, Color.white, TextAlignmentOptions.Center);
            _titleText = titleGo.GetComponentInChildren<TextMeshProUGUI>();
            var titleLe = titleGo.AddComponent<LayoutElement>();
            titleLe.preferredHeight = TITLE_ROW_HEIGHT;

            // Kept clear of the two floating corner buttons. The row is owned by the panel's
            // VerticalLayoutGroup, which overwrites its RectTransform every rebuild, so the
            // inset goes on the TEXT's margin rather than on offsetMax — the same trap the
            // LangButton note below records. Without it a long persona name runs under the
            // close button and the player reads a name that is missing its last two letters.
            _titleText.margin = new Vector4(CORNER_STRIP_WIDTH, 0f, CORNER_STRIP_WIDTH, 0f);

            // Scroll area
            var scrollGo = new GameObject("ScrollArea");
            scrollGo.transform.SetParent(_panel.transform, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            var scrollLe = scrollGo.AddComponent<LayoutElement>();
            scrollLe.flexibleHeight = 1f;
            scrollLe.minHeight = SCROLL_MIN_H;

            var scrollImg = scrollGo.AddComponent<Image>();
            scrollImg.color = new Color(0.05f, 0.05f, 0.08f, 0.6f);
            scrollGo.AddComponent<Mask>().showMaskGraphic = true;

            _scrollRect = scrollGo.AddComponent<ScrollRect>();
            _scrollRect.horizontal = false;
            _scrollRect.scrollSensitivity = 20f;

            // Content container
            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(scrollGo.transform, false);
            _contentRect = contentGo.AddComponent<RectTransform>();
            _contentRect.anchorMin = new Vector2(0, 1);
            _contentRect.anchorMax = new Vector2(1, 1);
            _contentRect.pivot = new Vector2(0.5f, 1);
            _contentRect.sizeDelta = new Vector2(0, 0);

            var contentVlg = contentGo.AddComponent<VerticalLayoutGroup>();
            contentVlg.padding = new RectOffset(4, 4, 4, 4);
            contentVlg.spacing = 2f;
            contentVlg.childForceExpandWidth = true;
            contentVlg.childForceExpandHeight = false;

            var csf = contentGo.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scrollRect.content = _contentRect;
            _scrollRect.viewport = scrollRt;

            // Trade confirmation row. Hidden unless the character has an offer on the table.
            //
            // A row rather than a modal: the offer is part of the conversation and reads as
            // one, and a dialog over the panel would hide the sentence that explains what is
            // being bought. Two explicit buttons rather than typing "si", because the reply
            // to a confirmation must not itself be sent to a language model to interpret —
            // that is the one place a misread spends real money.
            _tradeConfirmRow = new GameObject("TradeConfirmRow");
            _tradeConfirmRow.transform.SetParent(_panel.transform, false);
            var confirmLe = _tradeConfirmRow.AddComponent<LayoutElement>();
            confirmLe.preferredHeight = 28f;

            // Same reason as the input row above: this row also carries a
            // HorizontalLayoutGroup, so without this it inherits that group's flexibleHeight
            // and swells whenever a trade is on the table — measured at 100 px against its
            // 28 px preference, at the exact moment the player is reading an offer.
            confirmLe.flexibleHeight = 0f;
            var confirmBg = _tradeConfirmRow.AddComponent<Image>();
            confirmBg.color = new Color(0.16f, 0.13f, 0.06f, 1f);

            var confirmHlg = _tradeConfirmRow.AddComponent<HorizontalLayoutGroup>();
            confirmHlg.spacing = 4f;
            confirmHlg.padding = new RectOffset(6, 6, 3, 3);
            confirmHlg.childForceExpandWidth = false;
            confirmHlg.childForceExpandHeight = true;

            var offerGo = new GameObject("OfferLabel");
            offerGo.transform.SetParent(_tradeConfirmRow.transform, false);
            offerGo.AddComponent<RectTransform>();
            var offerLe = offerGo.AddComponent<LayoutElement>();
            offerLe.flexibleWidth = 1f;
            _tradeOfferText = offerGo.AddComponent<TextMeshProUGUI>();
            _tradeOfferText.fontSize = 12;
            _tradeOfferText.color = new Color(1f, 0.88f, 0.55f);
            _tradeOfferText.alignment = TextAlignmentOptions.Left;
            _tradeOfferText.enableWordWrapping = false;
            _tradeOfferText.overflowMode = TextOverflowModes.Ellipsis;

            var acceptBtn = CreateInlineButton(_tradeConfirmRow.transform, ChatLanguage.Accept,
                new Color(0.20f, 0.50f, 0.24f, 1f), 64f);
            acceptBtn.onClick.AddListener(OnTradeAccepted);

            var declineBtn = CreateInlineButton(_tradeConfirmRow.transform, "No",
                new Color(0.42f, 0.20f, 0.20f, 1f), 40f);
            declineBtn.onClick.AddListener(OnTradeDeclined);

            _tradeConfirmRow.SetActive(false);

            // Input row
            var inputRow = new GameObject("InputRow");
            inputRow.transform.SetParent(_panel.transform, false);
            var irRt = inputRow.AddComponent<RectTransform>();
            var irLe = inputRow.AddComponent<LayoutElement>();
            irLe.preferredHeight = 32f;

            // Explicitly NOT flexible, and this line is load-bearing. Unity resolves each
            // layout property from the highest-PRIORITY component that supplies it, per
            // property: the LayoutElement below wins the preferred height at 32, but it leaves
            // flexibleHeight unset (-1), so the value taken is the HorizontalLayoutGroup's own
            // — and a group with childForceExpandHeight reports flexibleHeight 1. The row was
            // therefore competing with the conversation for every spare pixel and winning a
            // share of it: measured at a 340-tall panel, 80 px of input box against a
            // 32 px preference. It also overflowed the panel at the DEFAULT size, which is
            // why the last message line was clipped mid-word.
            irLe.flexibleHeight = 0f;

            var irHlg = inputRow.AddComponent<HorizontalLayoutGroup>();
            irHlg.spacing = 4f;
            irHlg.childForceExpandHeight = true;

            // childForceExpandWidth defaults to TRUE, which hands every child an equal share
            // of the leftover width regardless of what it asked for — so the Send button
            // ignored its 60 px preferred width and grew to half the row, dwarfing the field
            // it belongs to. Off, the field's flexibleWidth takes the slack and the button
            // stays the size it declares.
            irHlg.childForceExpandWidth = false;

            // Input field
            var inputGo = new GameObject("InputField");
            inputGo.transform.SetParent(inputRow.transform, false);
            var inputBg = inputGo.AddComponent<Image>();
            inputBg.color = new Color(0.15f, 0.15f, 0.2f, 1f);

            var inputLe = inputGo.AddComponent<LayoutElement>();
            inputLe.flexibleWidth = 1f;

            // TMP child for input text area
            var textAreaGo = new GameObject("Text Area");
            textAreaGo.transform.SetParent(inputGo.transform, false);
            var textAreaRt = textAreaGo.AddComponent<RectTransform>();
            textAreaRt.anchorMin = Vector2.zero;
            textAreaRt.anchorMax = Vector2.one;
            textAreaRt.offsetMin = new Vector2(6, 2);
            textAreaRt.offsetMax = new Vector2(-6, -2);

            var inputText = new GameObject("Text");
            inputText.transform.SetParent(textAreaGo.transform, false);
            var itRt = inputText.AddComponent<RectTransform>();
            itRt.anchorMin = Vector2.zero;
            itRt.anchorMax = Vector2.one;
            itRt.offsetMin = Vector2.zero;
            itRt.offsetMax = Vector2.zero;
            var itTmp = inputText.AddComponent<TextMeshProUGUI>();
            itTmp.fontSize = 14;
            itTmp.color = Color.white;

            var placeholder = new GameObject("Placeholder");
            placeholder.transform.SetParent(textAreaGo.transform, false);
            var phRt = placeholder.AddComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = Vector2.zero;
            phRt.offsetMax = Vector2.zero;
            var phTmp = placeholder.AddComponent<TextMeshProUGUI>();
            phTmp.text = ChatLanguage.InputPlaceholder;
            _placeholderText = phTmp;
            phTmp.fontSize = 14;
            phTmp.color = new Color(0.5f, 0.5f, 0.5f);
            phTmp.fontStyle = FontStyles.Italic;

            _inputField = inputGo.AddComponent<TMP_InputField>();
            _inputField.textViewport = textAreaRt;
            _inputField.textComponent = itTmp;
            _inputField.placeholder = phTmp;
            _inputField.caretColor = Color.white;

            // What tells the character somebody is talking TO her. The field's own change
            // event rather than a per-frame poll of its text: the panel already runs an
            // Update for the Enter key, but a poll there would re-answer the same question
            // sixty times a second to raise an event that changes twice a conversation.
            _inputField.onValueChanged.AddListener(OnInputTextChanged);

            // Send button
            var sendGo = new GameObject("SendButton");
            sendGo.transform.SetParent(inputRow.transform, false);
            var sendImg = sendGo.AddComponent<Image>();
            sendImg.color = new Color(0.2f, 0.5f, 0.3f, 1f);
            var sendLe = sendGo.AddComponent<LayoutElement>();
            sendLe.preferredWidth = SEND_BUTTON_WIDTH;
            sendLe.flexibleWidth = 0f;   // never absorb slack — the message field gets it all

            var sendTxtGo = new GameObject("Text");
            sendTxtGo.transform.SetParent(sendGo.transform, false);
            var stRt = sendTxtGo.AddComponent<RectTransform>();
            stRt.anchorMin = Vector2.zero;
            stRt.anchorMax = Vector2.one;
            stRt.offsetMin = Vector2.zero;
            stRt.offsetMax = Vector2.zero;
            var stTmp = sendTxtGo.AddComponent<TextMeshProUGUI>();
            stTmp.text = ChatLanguage.Send;
            _sendButtonText = stTmp;
            stTmp.fontSize = 12;
            stTmp.color = Color.white;
            stTmp.alignment = TextAlignmentOptions.Center;

            var sendBtn = sendGo.AddComponent<Button>();
            sendBtn.onClick.AddListener(SubmitInput);

            // ── Gutter column ───────────────────────────────────────────────────────────
            //
            // Comerciar and Reiniciar live in the strip down the left, under the face, and
            // they FLOAT there for the reason every other free child of this panel does: the
            // VerticalLayoutGroup owns any child it lays out and overwrites its anchors.
            //
            // There is no Cerrar strip any more. Four controls closed this panel — Escape,
            // the backdrop, the corner X and a full-width red bar — and the bar was the one
            // that cost a row of the conversation to say what the X already says by being an
            // X. The one thing it carried alone was the "(ESC)" hint, and that IS a real
            // loss: nothing on screen now teaches the shortcut. It was not worth a row, and
            // there is no honest place left for it — the title is centred and a trailing
            // hint pushes the character's name off centre, and the X is 24 px of one glyph.
            //
            // Their WIDTH is the portrait's, exactly, so the column has one edge instead of
            // three things that happen to be on the left.
            _tradeButton = CreateGutterButton(_panel.transform, "TradeButton", ChatLanguage.Trade,
                new Color(0.18f, 0.38f, 0.5f, 1f), 12f, wrap: false, out _tradeButtonText);
            var tradeRt = (RectTransform)_tradeButton.transform;
            tradeRt.anchorMin = new Vector2(0f, 1f);
            tradeRt.anchorMax = new Vector2(0f, 1f);
            tradeRt.pivot     = new Vector2(0f, 1f);
            tradeRt.sizeDelta = new Vector2(GUTTER_BUTTON_WIDTH, GUTTER_TRADE_HEIGHT);
            // Y is set per conversation by ConfigurePortraitFor: it sits under the face when
            // there is one and takes the top of the column when there is not, and that
            // decision belongs where the gutter's contents are already decided.
            _tradeButton.GetComponent<Button>().onClick.AddListener(OnTradeClicked);
            _tradeButton.SetActive(false);

            // Diario sits under Comerciar and is UNCONDITIONAL, where Comerciar is not: five
            // of the six characters do not trade, and all of them can be remembered. Its Y,
            // like Comerciar's, is set per conversation by LayoutGutterColumn — the column
            // closes up when a character has no face and no shop, and one owner for that
            // arithmetic is the whole reason it is not decided here.
            _journalButton = CreateGutterButton(_panel.transform, "JournalButton", ChatLanguage.Journal,
                new Color(0.22f, 0.20f, 0.34f, 1f), 12f, wrap: false, out _journalButtonText);
            var journalRt = (RectTransform)_journalButton.transform;
            journalRt.anchorMin = new Vector2(0f, 1f);
            journalRt.anchorMax = new Vector2(0f, 1f);
            journalRt.pivot     = new Vector2(0f, 1f);
            journalRt.sizeDelta = new Vector2(GUTTER_BUTTON_WIDTH, GUTTER_JOURNAL_HEIGHT);
            _journalButton.GetComponent<Button>().onClick.AddListener(OnJournalClicked);

            // Bottom-left, as far from the conversation as the panel allows. It is the only
            // control here that destroys player data, and it takes two clicks.
            _resetButton = CreateGutterButton(_panel.transform, "ResetButton", RESET_LABEL_IDLE,
                new Color(0.24f, 0.19f, 0.10f, 1f), 10f, wrap: true, out _resetButtonText);
            _resetButtonText.color = new Color(0.85f, 0.76f, 0.55f);
            var resetRt = (RectTransform)_resetButton.transform;
            resetRt.anchorMin = new Vector2(0f, 0f);
            resetRt.anchorMax = new Vector2(0f, 0f);
            resetRt.pivot     = new Vector2(0f, 0f);
            resetRt.anchoredPosition = new Vector2(PANEL_PADDING, PANEL_PADDING);
            resetRt.sizeDelta = new Vector2(GUTTER_BUTTON_WIDTH, GUTTER_RESET_HEIGHT);
            _resetButton.GetComponent<Button>().onClick.AddListener(OnResetClicked);

            // ── Resize grip (top-right corner of panel) ───────────────────────
            //
            // TOP-right, not the bottom-right every editor uses, because the panel's pivot is
            // bottom-left: it is pinned near the bottom of the screen, so its bottom edge
            // cannot move and a bottom grip could only ever change the width. It grows up and
            // to the right, into the empty part of the screen.
            //
            // The same PanelResizeHandle the four resizable editors use (F1/F4/F7/F8), so
            // there is one drag-to-resize implementation in the project rather than a second
            // one that would drift — MusicPlayerHUD already rolled its own and it resizes by
            // localScale instead of sizeDelta.
            var gripGo = new GameObject("ResizeGrip");
            gripGo.transform.SetParent(_panel.transform, false);
            var gripRt = gripGo.AddComponent<RectTransform>();

            gripGo.AddComponent<LayoutElement>().ignoreLayout = true;
            gripRt.anchorMin = new Vector2(1f, 1f);
            gripRt.anchorMax = new Vector2(1f, 1f);
            gripRt.pivot = new Vector2(1f, 1f);
            gripRt.anchoredPosition = Vector2.zero;
            gripRt.sizeDelta = new Vector2(RESIZE_GRIP_SIZE, RESIZE_GRIP_SIZE);

            // The graphic is what makes the grip hit-testable at all — without a Graphic, uGUI
            // raycasts nothing and the handle would be invisible AND inert, for one reason.
            // Its corner is set from the same enum as the handle's so the glyph cannot end up
            // advertising a drag in a direction the handle does not perform.
            var gripGraphic = gripGo.AddComponent<TriangleHandleGraphic>();
            gripGraphic.color = new Color(0.55f, 0.58f, 0.68f, 0.85f);
            gripGraphic.Corner = ResizeGripCorner.TopRight;

            var grip = gripGo.AddComponent<PanelResizeHandle>();
            grip.Target = panelRt;
            grip.Corner = ResizeGripCorner.TopRight;

            // The floor is the pair of constants that had sat in this file unread since it was
            // ported: they are what the panel was always meant to refuse to shrink past. The
            // ceiling is the live viewport, resolved on every rebuild.
            grip.MinSize = new Vector2(PANEL_MIN_W, PANEL_MIN_H);
            grip.MaxSize = MaxPanelSize();
            grip.Resized += PersistPanelSize;

            // ── Close "X" (top-right corner of panel) ─────────────────────────
            //
            // The full-width "Cerrar (ESC)" strip below already closes the panel, and so do
            // Escape and a click on the backdrop. None of them is the control a player LOOKS
            // for: a window with no X in its corner reads as one you are stuck in, which is
            // why the shop grew the same button. It is also the only close control that is
            // visible without reading — the strip is a label, and a player mid-conversation
            // scans for a shape.
            var closeXGo = new GameObject("CloseXButton");
            closeXGo.transform.SetParent(_panel.transform, false);
            var closeXRt = closeXGo.AddComponent<RectTransform>();

            // ignoreLayout for the reason spelled out on the LangButton below: a child of the
            // panel is owned by its VerticalLayoutGroup, which overwrites these anchors.
            closeXGo.AddComponent<LayoutElement>().ignoreLayout = true;
            closeXRt.anchorMin = new Vector2(1f, 1f);
            closeXRt.anchorMax = new Vector2(1f, 1f);
            closeXRt.pivot = new Vector2(1f, 1f);
            // Left of the resize grip, which owns the corner itself.
            closeXRt.anchoredPosition = new Vector2(-(RESIZE_GRIP_SIZE + CORNER_GAP), -CORNER_MARGIN);
            closeXRt.sizeDelta = new Vector2(CLOSE_X_SIZE, CORNER_BUTTON_HEIGHT);

            var closeXImg = closeXGo.AddComponent<Image>();
            closeXImg.color = new Color(0.45f, 0.14f, 0.14f, 1f);

            // Image and TMP on separate objects — both on one GameObject is a NullReference.
            var closeXLabelGo = new GameObject("CloseXLabel");
            closeXLabelGo.transform.SetParent(closeXGo.transform, false);
            var closeXLabelRt = closeXLabelGo.AddComponent<RectTransform>();
            closeXLabelRt.anchorMin = Vector2.zero;
            closeXLabelRt.anchorMax = Vector2.one;
            closeXLabelRt.offsetMin = Vector2.zero;
            closeXLabelRt.offsetMax = Vector2.zero;
            var closeXTmp = closeXLabelGo.AddComponent<TextMeshProUGUI>();
            closeXTmp.text = "X";
            closeXTmp.fontSize = 14;
            closeXTmp.color = Color.white;
            closeXTmp.alignment = TextAlignmentOptions.Center;

            var closeXBtn = closeXGo.AddComponent<Button>();
            closeXBtn.onClick.AddListener(() => ChatSystem.Instance?.CloseChat());

            // ── Language toggle button (top-right corner of panel) ────────────
            var langBtnGo = new GameObject("LangButton");
            langBtnGo.transform.SetParent(_panel.transform, false);
            var langRt = langBtnGo.AddComponent<RectTransform>();

            // ignoreLayout is what actually makes it float. It is a CHILD of the panel, so
            // the panel's VerticalLayoutGroup owns it and overwrites the anchors set below —
            // measured live, it came out 504x0 at the bottom of the stack, an invisible
            // full-width strip lying across the Close button and eating its clicks, with the
            // "ES" label overflowing out of a zero-height rect. The comment here used to
            // claim it was "built outside the VerticalLayoutGroup", which was never true of
            // anything parented to the panel.
            var langLayout = langBtnGo.AddComponent<LayoutElement>();
            langLayout.ignoreLayout = true;
            langRt.anchorMin = new Vector2(1f, 1f);
            langRt.anchorMax = new Vector2(1f, 1f);
            langRt.pivot = new Vector2(1f, 1f);

            // Shifted left to clear the close button, which now owns the corner itself. Two
            // free-floating children of the same corner do not collide in layout — nothing
            // arranges them — so an overlap is silent and costs whichever draws first its
            // clicks, exactly as the LangButton once did to the Cerrar strip.
            langRt.anchoredPosition = new Vector2(
                -(RESIZE_GRIP_SIZE + CORNER_GAP + CLOSE_X_SIZE + CORNER_GAP), -CORNER_MARGIN);
            langRt.sizeDelta = new Vector2(LANG_BUTTON_WIDTH, CORNER_BUTTON_HEIGHT);

            var langImg = langBtnGo.AddComponent<Image>();
            langImg.color = new Color(0.18f, 0.25f, 0.45f, 1f);

            // Label child (Image + TMP on separate objects avoids NullRef gotcha)
            var langLblGo = new GameObject("LangLabel");
            langLblGo.transform.SetParent(langBtnGo.transform, false);
            var langLblRt = langLblGo.AddComponent<RectTransform>();
            langLblRt.anchorMin = Vector2.zero;
            langLblRt.anchorMax = Vector2.one;
            langLblRt.offsetMin = Vector2.zero;
            langLblRt.offsetMax = Vector2.zero;
            _langButtonText = langLblGo.AddComponent<TextMeshProUGUI>();
            _langButtonText.text = ChatLanguage.Label;
            _langButtonText.fontSize = 12;
            _langButtonText.color = Color.white;
            _langButtonText.alignment = TextAlignmentOptions.Center;

            var langBtn = langBtnGo.AddComponent<Button>();
            langBtn.onClick.AddListener(ToggleLang);

            // Last, so it draws over the rows whose gutter it occupies rather than under
            // them. It starts hidden; whether a conversation reserves the gutter at all is
            // ConfigurePortraitFor's decision, per character.
            BuildPortrait(_panel.transform);

            // After the portrait, because it covers it too: the journal is a view of the
            // whole panel, not a row inside the conversation. Built hidden.
            BuildJournalOverlay(_panel.transform);
        }

        /// <summary>
        /// One button of the left gutter: a named GameObject carrying the Image and the
        /// Button, with the label on a TMP CHILD.
        ///
        /// <para>The split is not tidiness — an Image and a TextMeshProUGUI on the same
        /// GameObject is a NullReferenceException in this project, which is why every button
        /// in this file is built the same way and why a helper is better than four copies of
        /// it.</para>
        ///
        /// <para>It floats: <c>ignoreLayout</c>, with the caller setting the anchors. A child
        /// of the panel is otherwise owned by its <c>VerticalLayoutGroup</c>, which overwrites
        /// any anchor set on it — the LangButton once came out 504x0 that way, an invisible
        /// full-width strip eating another control's clicks.</para>
        ///
        /// <para><paramref name="wrap"/> decides whether a long caption breaks onto a second
        /// line or is elided. Eliding is right for a caption the player can guess from colour
        /// and position, and wrong for one that names what a button will DELETE.</para>
        /// </summary>
        private static GameObject CreateGutterButton(Transform parent, string name, string label,
                                                     Color color, float fontSize, bool wrap,
                                                     out TextMeshProUGUI labelText)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            go.AddComponent<LayoutElement>().ignoreLayout = true;

            go.AddComponent<Image>().color = color;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(3f, 2f);
            textRt.offsetMax = new Vector2(-3f, -2f);

            labelText = textGo.AddComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.fontSize = fontSize;
            labelText.color = Color.white;
            labelText.alignment = TextAlignmentOptions.Center;
            labelText.enableWordWrapping = wrap;
            labelText.overflowMode = wrap ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;

            go.AddComponent<Button>();
            return go;
        }

        /// <summary>A fixed-width button inside a horizontal row.</summary>
        private static Button CreateInlineButton(Transform parent, string label, Color color, float width)
        {
            var go = new GameObject(label + "Button");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();

            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.flexibleWidth = 0f;

            go.AddComponent<Image>().color = color;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 12;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;

            return go.AddComponent<Button>();
        }

        private void AppendMessageRow(string sender, string text)
        {
            bool isPlayer = sender == ChatSystem.PLAYER_SENDER;
            Color senderColor = isPlayer ? Color.cyan : new Color(1f, 0.8f, 0.4f);
            string formatted = $"<color=#{ColorUtility.ToHtmlStringRGB(senderColor)}>{sender}</color>: {text}";

            var row = CreateTextRow(_contentRect.transform, formatted, 14, Color.white, TextAlignmentOptions.TopLeft);
            _messageRows.Add(row);
        }

        private void ClearMessages()
        {
            foreach (var row in _messageRows)
                if (row != null) Destroy(row);
            _messageRows.Clear();
        }

        private static GameObject CreateTextRow(Transform parent, string text, float fontSize, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject("MsgRow");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = align;
            tmp.enableWordWrapping = true;
            tmp.richText = true;
            tmp.overflowMode = TextOverflowModes.Overflow;

            return go;
        }
    }
}
