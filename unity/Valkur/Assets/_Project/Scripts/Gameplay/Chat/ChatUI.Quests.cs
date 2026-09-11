using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay.Quests;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// Misiones: what this character is handing out, and what they are waiting to be
    /// told about.
    ///
    /// <para>IT LIVES IN THE CONVERSATION FOR THE SAME REASON THE DIARIO DOES. A quest
    /// is given by a PERSON — that is the whole reason the offer is keyed on a
    /// personaId rather than on a map marker — so a separate quest window would be a
    /// panel about a character floating next to the character. Covering the message
    /// area keeps the face, the title and the gutter on screen, so it plainly stays
    /// the same conversation.</para>
    ///
    /// <para>WHY IT HAS TO EXIST AT ALL: without it a quest was reachable only from the
    /// console. The layer had a catalogue, a manager, nine objective kinds, rewards and
    /// persistence, and no door — which is the authored-and-inert shape this project
    /// has shipped a dozen times and this file is the door.</para>
    ///
    /// <para>ESCAPE IS NOT READ HERE. <see cref="ChatSystem"/> owns that key for the
    /// whole subsystem; this view raises the same modal flag the Diario does and
    /// listens for the dismissal. Two readers of one key in an undefined Update order
    /// is how one press closes the overlay AND the panel behind it.</para>
    ///
    /// <para>The two views are MUTUALLY EXCLUSIVE and each closes the other on open.
    /// They occupy the same rectangle, and two opaque sheets on one rect is a window
    /// whose content depends on which was built last.</para>
    /// </summary>
    public partial class ChatUI
    {
        /// <summary>
        /// Shortest a quest card may be. It is a FLOOR, not the height: the card is sized
        /// from the text it actually holds.
        ///
        /// <para>It shipped as a flat 78 px and that could not hold a card own content.
        /// A card is a bold name line, the character hook — the shipped ones run to two
        /// or three sentences — and a reward line, wrapped into roughly 434 px at font 11.
        /// Gatita opening hook alone is six lines that way, so the paragraph the sheet
        /// exists to show was the part being ellipsised away.</para>
        /// </summary>
        private const float QUEST_CARD_MIN_HEIGHT = 78f;

        /// <summary>Height of a section header ("DISPONIBLES").</summary>
        private const float QUEST_HEADER_HEIGHT = 18f;

        /// <summary>Width of the Aceptar / Entregar button on a card.</summary>
        private const float QUEST_ACTION_WIDTH = 76f;

        /// <summary>Padding inside the overlay. The Diario's, so the two sit identically.</summary>
        private const float QUEST_PADDING = 6f;

        /// <summary>Left+right padding inside one card, matching its layout group.</summary>
        private const float CARD_SIDE_PADDING = 6f;

        /// <summary>Gap between a card prose and its action button.</summary>
        private const float CARD_INNER_SPACING = 6f;

        /// <summary>Vertical slack a card keeps around its text.</summary>
        private const float CARD_TEXT_PADDING = 14f;

        /// <summary>
        /// Body width assumed when the real one cannot be measured — Edit Mode, where uGUI
        /// lays nothing out and the content rect reports its untouched default.
        /// </summary>
        private const float QUEST_FALLBACK_BODY_WIDTH = 420f;

        private GameObject _questRoot;
        private ScrollRect _questScroll;
        private RectTransform _questContent;
        private TextMeshProUGUI _questTitle;
        private TextMeshProUGUI _questEmpty;

        private readonly List<GameObject> _questRows = new List<GameObject>();
        private readonly List<QuestDefinition> _questOffers = new List<QuestDefinition>();
        private readonly List<QuestDefinition> _questTurnIns = new List<QuestDefinition>();
        private readonly List<QuestDefinition> _questActive = new List<QuestDefinition>();
        private readonly List<QuestDefinition> _questLocked = new List<QuestDefinition>();

        /// <summary>
        /// The quest whose Abandonar button is armed, if any.
        ///
        /// <para>Two clicks, like Reiniciar in the same gutter and for the same reason: it
        /// is the only control in this panel that destroys progress the player earned, and
        /// a single misclick beside Entregar would throw away an errand they had walked
        /// across the map for.</para>
        /// </summary>
        private string _armedAbandonId;

        /// <summary>True while the offer list is covering the conversation.</summary>
        internal bool IsQuestsOpen => _questRoot != null && _questRoot.activeSelf;

        // ── Build ───────────────────────────────────────────────────────────

        /// <summary>
        /// Creates the overlay, hidden. Built once with the panel rather than on first
        /// use, like every other view here: a scroll view with a layout group in it is
        /// not something to pay for in the middle of a conversation.
        /// </summary>
        private void BuildQuestOverlay(Transform panel)
        {
            _questRoot = new GameObject("QuestOverlay");
            _questRoot.transform.SetParent(panel, false);

            var rt = _questRoot.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;

            // Exactly the Diario's rect: left of the message area so the face and the
            // gutter stay visible, clear of the title row and its corner controls.
            rt.offsetMin = new Vector2(PANEL_PADDING + PORTRAIT_GUTTER, PANEL_PADDING);
            rt.offsetMax = new Vector2(-PANEL_PADDING, -JOURNAL_TOP_INSET);

            // Without this the panel's VerticalLayoutGroup claims the rect and the
            // stretch above is overwritten on the next rebuild.
            _questRoot.AddComponent<LayoutElement>().ignoreLayout = true;

            // Opaque, for the Diario's reason: the conversation is directly underneath
            // and two legible transcripts read as a rendering fault.
            _questRoot.AddComponent<Image>().color = new Color(0.06f, 0.07f, 0.05f, 1f);

            var vlg = _questRoot.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(
                (int)QUEST_PADDING, (int)QUEST_PADDING, (int)QUEST_PADDING, (int)QUEST_PADDING);
            vlg.spacing = 4f;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            BuildQuestTitleRow(_questRoot.transform);
            BuildQuestScroll(_questRoot.transform);

            _questRoot.SetActive(false);
        }

        private void BuildQuestTitleRow(Transform parent)
        {
            var rowGo = new GameObject("QuestNav");
            rowGo.transform.SetParent(parent, false);
            rowGo.AddComponent<RectTransform>();

            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = JOURNAL_NAV_HEIGHT;

            // preferredHeight alone leaves flexibleHeight at its unset -1, so the value
            // used comes from the layout group on this same GameObject — which reports 1
            // while childForceExpandHeight is on, and the row then competes with the list
            // for every spare pixel. The chat input row shipped exactly that bug.
            le.flexibleHeight = 0f;

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;

            var titleGo = new GameObject("QuestTitle");
            titleGo.transform.SetParent(rowGo.transform, false);
            titleGo.AddComponent<RectTransform>();
            _questTitle = titleGo.AddComponent<TextMeshProUGUI>();
            _questTitle.fontSize = 14;
            _questTitle.color = new Color(0.88f, 0.92f, 0.72f);
            _questTitle.alignment = TextAlignmentOptions.Left;
            _questTitle.enableWordWrapping = false;
            _questTitle.overflowMode = TextOverflowModes.Ellipsis;

            var back = CreateInlineButton(
                rowGo.transform, ChatLanguage.JournalBack,
                new Color(0.18f, 0.38f, 0.5f, 1f), JOURNAL_BACK_WIDTH);
            back.onClick.AddListener(CloseQuests);
            // CreateInlineButton names the object after its LABEL, which is a translated
            // word — the hierarchy would otherwise be shaped by the player's language,
            // and a node name is what every test and probe addresses it by.
            back.gameObject.name = "QuestBackButton";
        }

        private void BuildQuestScroll(Transform parent)
        {
            var scrollGo = new GameObject("QuestScroll");
            scrollGo.transform.SetParent(parent, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();

            var le = scrollGo.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;

            scrollGo.AddComponent<Image>().color = new Color(0.03f, 0.04f, 0.03f, 1f);
            scrollGo.AddComponent<Mask>().showMaskGraphic = true;

            _questScroll = scrollGo.AddComponent<ScrollRect>();
            _questScroll.horizontal = false;
            _questScroll.movementType = ScrollRect.MovementType.Clamped;
            _questScroll.scrollSensitivity = 24f;

            // Named QuestContent and not "Content": the chat panel's message scroll
            // already owns that name, and ChatUIBuilderTests counts named nodes to prove
            // BuildUI did not run twice — a second "Content" reads as exactly that
            // failure and says nothing about the real one.
            var contentGo = new GameObject("QuestContent");
            contentGo.transform.SetParent(scrollGo.transform, false);
            _questContent = contentGo.AddComponent<RectTransform>();
            _questContent.anchorMin = new Vector2(0f, 1f);
            _questContent.anchorMax = new Vector2(1f, 1f);
            _questContent.pivot     = new Vector2(0.5f, 1f);

            // MANDATORY, and its absence is the defect this sheet shipped with. A fresh
            // RectTransform is born with sizeDelta (100,100), and against a STRETCHED
            // anchor that is not a width — it is a hundred pixels WIDER THAN THE PARENT.
            // With the centre pivot the surplus splits, so every row hung fifty pixels off
            // each side of the mask and the first seven characters of every line were cut:
            // "Plaga e|n la despensa". The Diario sets it; this copied the three lines
            // above it and not this one.
            _questContent.sizeDelta = Vector2.zero;

            var contentVlg = contentGo.AddComponent<VerticalLayoutGroup>();
            contentVlg.spacing = 4f;
            contentVlg.padding = new RectOffset(4, 4, 4, 4);
            contentVlg.childForceExpandWidth = true;
            contentVlg.childForceExpandHeight = false;
            contentVlg.childControlHeight = true;

            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _questScroll.content = _questContent;
            _questScroll.viewport = scrollRt;

            // The "nothing here" line lives in the content rather than beside it, so an
            // empty list reads as an empty LIST rather than as a list that failed to draw.
            var emptyGo = new GameObject("QuestEmpty");
            emptyGo.transform.SetParent(contentGo.transform, false);
            emptyGo.AddComponent<RectTransform>();
            _questEmpty = emptyGo.AddComponent<TextMeshProUGUI>();
            _questEmpty.fontSize = 12;
            _questEmpty.color = new Color(0.66f, 0.64f, 0.58f);
            _questEmpty.alignment = TextAlignmentOptions.TopLeft;
            _questEmpty.enableWordWrapping = true;
            emptyGo.AddComponent<LayoutElement>().preferredHeight = 40f;
        }

        // ── Opening and closing ─────────────────────────────────────────────

        /// <summary>The Misiones button. A toggle, like Diario: it is the only control that names the view.</summary>
        private void OnQuestsClicked()
        {
            if (IsQuestsOpen) CloseQuests();
            else OpenQuests();
        }

        private void OpenQuests()
        {
            var chatSystem = ChatSystem.Instance;
            if (chatSystem == null || !chatSystem.IsChatOpen || _questRoot == null) return;

            // One rect, one sheet. See the type note.
            CloseJournal();

            _questRoot.SetActive(true);

            // The field is UNDERNEATH the overlay and uGUI focus is not blocked by an
            // image drawn over it, so without this the player types into a box they
            // cannot see. The text is left alone — a half-written message is not
            // something to throw away for a glance at a quest list.
            if (_inputField != null)
            {
                _inputField.DeactivateInputField();
                _inputField.interactable = false;
            }

            chatSystem.SetModalOverlay(true);

            RenderQuests();
        }

        /// <summary>
        /// Hides the list and hands the conversation back. Safe to call when already
        /// closed, which is what makes it the single answer to every way out: the
        /// Volver button, the Misiones toggle, Escape, and the panel closing underneath.
        /// </summary>
        private void CloseQuests()
        {
            // An armed Abandonar must not survive the panel: coming back to a red "Seguro?"
            // minutes later, with no memory of having pressed anything, is one click from
            // losing a quest.
            _armedAbandonId = null;

            if (_questRoot == null) return;

            bool wasOpen = _questRoot.activeSelf;
            _questRoot.SetActive(false);
            ClearQuestRows();

            if (_inputField != null && !_inputField.interactable)
            {
                _inputField.interactable = true;
                if (wasOpen) _inputField.ActivateInputField();
            }

            if (wasOpen) ChatSystem.Instance?.SetModalOverlay(false);
        }

        /// <summary>
        /// Shuts every sheet that can cover the conversation.
        ///
        /// <para>ONE entry point, because there are now two overlays and five ways out —
        /// the Volver buttons, each view's own toggle, Escape, Enter, and the panel
        /// closing underneath them. Five callers each remembering to close two views is
        /// how one of them gets forgotten, and the failure is a panel that opens on
        /// somebody else's quest list.</para>
        ///
        /// <para>Both are safe to call when already closed, which is what lets this be
        /// unconditional.</para>
        /// </summary>
        private void CloseOverlays()
        {
            CloseJournal();
            CloseQuests();
        }

        // ── Rendering ───────────────────────────────────────────────────────

        private void ClearQuestRows()
        {
            for (int i = 0; i < _questRows.Count; i++)
            {
                if (_questRows[i] == null) continue;
                // Destroy is an outright ERROR in Edit Mode, and this panel is reachable
                // from EditMode fixtures — the trap seven ControlsEditorTests went red on.
                if (Application.isPlaying) Destroy(_questRows[i]);
                else DestroyImmediate(_questRows[i]);
            }
            _questRows.Clear();
        }

        /// <summary>
        /// Draws what this character can do for the player right now: first what they
        /// are waiting to be TOLD about, then what they are handing out.
        ///
        /// <para>That order is the design. A player who has finished a job and walked
        /// back across the map wants the button that closes it, and burying it under
        /// three new offers is how a completed quest gets left open.</para>
        /// </summary>
        internal void RenderQuests()
        {
            ClearQuestRows();

            var service = QuestService.Instance;
            var persona = ChatSystem.Instance != null ? ChatSystem.Instance.ActivePersona : null;
            string personaId = persona != null ? persona.personaId : null;
            string npcName   = persona != null && !string.IsNullOrEmpty(persona.displayName)
                ? persona.displayName : "???";

            if (_questTitle != null) _questTitle.text = ChatLanguage.QuestsTitle(npcName);

            _questOffers.Clear();
            _questTurnIns.Clear();
            _questActive.Clear();
            _questLocked.Clear();
            if (service != null && !string.IsNullOrEmpty(personaId))
            {
                service.TurnInsReadyFor(personaId, _questTurnIns);
                service.OffersFor(personaId, _questOffers);
                service.ActiveFrom(personaId, _questActive);
                service.LockedFor(personaId, _questLocked);
            }

            bool any = _questTurnIns.Count > 0 || _questOffers.Count > 0
                    || _questActive.Count > 0 || _questLocked.Count > 0;
            if (_questEmpty != null)
            {
                _questEmpty.gameObject.SetActive(!any);
                _questEmpty.text = ChatLanguage.QuestsNone;
            }
            if (!any) return;

            if (_questTurnIns.Count > 0)
            {
                AddQuestHeader(ChatLanguage.QuestsWaiting);
                for (int i = 0; i < _questTurnIns.Count; i++)
                    AddQuestCard(_questTurnIns[i], turnIn: true);
            }

            if (_questOffers.Count > 0)
            {
                AddQuestHeader(ChatLanguage.QuestsOnOffer);
                for (int i = 0; i < _questOffers.Count; i++)
                    AddQuestCard(_questOffers[i], turnIn: false);
            }

            // LAST, and it is the section with no button: work already accepted is a
            // reminder, not a decision. Putting it above the offers would bury the two
            // rows the player came here to press under a progress readout they can also
            // read from the tracker in the corner.
            if (_questActive.Count > 0)
            {
                AddQuestHeader(ChatLanguage.QuestsInProgress);
                for (int i = 0; i < _questActive.Count; i++)
                    AddProgressCard(_questActive[i]);
            }

            // LAST of all: what this character has that the player has not earned yet. It
            // is the only section that is purely aspirational, so it sits below everything
            // actionable — but it is SHOWN, because a quest filtered out for level is
            // indistinguishable from one that does not exist, and the player then has no
            // reason to come back.
            if (_questLocked.Count > 0)
            {
                AddQuestHeader(ChatLanguage.QuestsLocked);
                for (int i = 0; i < _questLocked.Count; i++)
                    AddLockedCard(_questLocked[i]);
            }
        }

        /// <summary>
        /// A quest the player cannot take yet: name, why, and no button.
        ///
        /// <para>Deliberately dim rather than merely uncoloured. The card has to read as
        /// UNAVAILABLE at a glance from the same distance as the actionable ones, or the
        /// player clicks at it and learns the panel has rows that do nothing.</para>
        /// </summary>
        private void AddLockedCard(QuestDefinition def)
        {
            var service = QuestService.Instance;
            if (def == null) return;

            var card = new GameObject("QuestLocked_" + def.questId);
            card.transform.SetParent(_questContent, false);
            card.AddComponent<RectTransform>();
            card.AddComponent<Image>().color = new Color(0.07f, 0.07f, 0.08f, 1f);

            var le = card.AddComponent<LayoutElement>();
            le.flexibleHeight = 0f;

            var hlg = card.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset((int)CARD_SIDE_PADDING, (int)CARD_SIDE_PADDING, 4, 4);
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;

            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(card.transform, false);
            bodyGo.AddComponent<RectTransform>();
            var body = bodyGo.AddComponent<TextMeshProUGUI>();
            body.fontSize = 11;
            body.color = new Color(0.52f, 0.52f, 0.50f);
            body.alignment = TextAlignmentOptions.TopLeft;
            body.enableWordWrapping = true;

            string reason = service != null ? service.DescribeLock(def) : string.Empty;
            var sb = new System.Text.StringBuilder();
            sb.Append("<b>").Append(def.displayName).Append("</b>");
            if (!string.IsNullOrEmpty(reason))
                sb.AppendLine().Append("<size=10>").Append(reason).Append("</size>");
            body.text = sb.ToString();

            float bodyWidth = _questContent.rect.width - CARD_SIDE_PADDING * 2f;
            if (bodyWidth < 120f) bodyWidth = QUEST_FALLBACK_BODY_WIDTH;
            le.preferredHeight = body.GetPreferredValues(body.text, bodyWidth, 0f).y + CARD_TEXT_PADDING;

            _questRows.Add(card);
        }

        /// <summary>
        /// A quest of this character that the player is part-way through: its name and
        /// every objective with its counter, and no button.
        ///
        /// <para>Before this the giver forgot you. An accepted quest leaves the offers and
        /// does not reach the turn-ins until it is finished, so from the moment the player
        /// said yes until the moment they were done, the character who sent them showed an
        /// empty sheet — and the Misiones button vanished with it, which reads as the
        /// errand never having happened.</para>
        /// </summary>
        private void AddProgressCard(QuestDefinition def)
        {
            var service = QuestService.Instance;
            var quest = service != null && service.Manager != null
                ? service.Manager.GetActiveQuest(def.questId) : null;
            if (quest == null) return;

            var card = new GameObject("QuestProgress_" + def.questId);
            card.transform.SetParent(_questContent, false);
            card.AddComponent<RectTransform>();
            card.AddComponent<Image>().color = new Color(0.09f, 0.10f, 0.13f, 1f);

            var le = card.AddComponent<LayoutElement>();
            le.flexibleHeight = 0f;

            var hlg = card.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset((int)CARD_SIDE_PADDING, (int)CARD_SIDE_PADDING, 4, 4);
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;

            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(card.transform, false);
            bodyGo.AddComponent<RectTransform>();
            var body = bodyGo.AddComponent<TextMeshProUGUI>();
            body.fontSize = 11;
            body.color = new Color(0.80f, 0.82f, 0.76f);
            body.alignment = TextAlignmentOptions.TopLeft;
            body.enableWordWrapping = true;

            var sb = new System.Text.StringBuilder();
            sb.Append("<b>").Append(def.displayName).Append("</b>");
            for (int i = 0; i < quest.Objectives.Count; i++)
            {
                var obj = quest.Objectives[i];
                if (obj == null) continue;
                sb.AppendLine();
                sb.Append(obj.IsComplete ? "  <color=#8FBF6A>[x] " : "  [ ] ");
                sb.Append(obj.Description).Append("  ").Append(obj.Current).Append('/').Append(obj.Target);
                if (obj.IsComplete) sb.Append("</color>");
            }
            body.text = sb.ToString();

            bool armed = string.Equals(_armedAbandonId, def.questId, System.StringComparison.OrdinalIgnoreCase);
            var drop = CreateInlineButton(
                card.transform,
                armed ? ChatLanguage.QuestAbandonConfirm : ChatLanguage.QuestAbandon,
                armed ? new Color(0.52f, 0.20f, 0.16f, 1f) : new Color(0.24f, 0.19f, 0.19f, 1f),
                QUEST_ACTION_WIDTH);
            drop.gameObject.name = "QuestAbandonButton";
            string abandonId = def.questId;
            drop.onClick.AddListener(() => AbandonQuest(abandonId));

            float bodyWidth = _questContent.rect.width - CARD_SIDE_PADDING * 2f
                            - QUEST_ACTION_WIDTH - CARD_INNER_SPACING;
            if (bodyWidth < 120f) bodyWidth = QUEST_FALLBACK_BODY_WIDTH;
            le.preferredHeight = body.GetPreferredValues(body.text, bodyWidth, 0f).y + CARD_TEXT_PADDING;

            _questRows.Add(card);
        }

        /// <summary>
        /// Drops a quest, on the SECOND click.
        ///
        /// <para>The first arms it and repaints the row red; the second does it. Arming is
        /// EXCLUSIVE — clicking a different quest's button disarms the first — so a player
        /// who armed one and changed their mind cannot destroy a different errand by
        /// clicking where the confirmation used to be.</para>
        ///
        /// <para>Until this existed, abandoning was reachable only from the DevConsole, so
        /// a quest accepted by mistake was permanent for anyone actually playing.</para>
        /// </summary>
        private void AbandonQuest(string questId)
        {
            var service = QuestService.Instance;
            if (service == null) return;

            if (!string.Equals(_armedAbandonId, questId, System.StringComparison.OrdinalIgnoreCase))
            {
                _armedAbandonId = questId;
                RenderQuests();
                return;
            }

            _armedAbandonId = null;
            if (service.Abandon(questId)) RenderQuests();
        }

        private void AddQuestHeader(string label)
        {
            var go = new GameObject("QuestHeader");
            go.transform.SetParent(_questContent, false);
            go.AddComponent<RectTransform>();

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = QUEST_HEADER_HEIGHT;
            le.flexibleHeight = 0f;

            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 11;
            text.color = new Color(0.62f, 0.72f, 0.55f);
            text.alignment = TextAlignmentOptions.Left;
            text.enableWordWrapping = false;

            _questRows.Add(go);
        }

        /// <summary>
        /// One quest: its name, its hook, what it pays, and the one button that acts on
        /// it. The button says <c>Entregar</c> for something finished and
        /// <c>Aceptar</c> for something offered, because those are two different acts and
        /// a single generic verb would make the two sections look interchangeable.
        /// </summary>
        private void AddQuestCard(QuestDefinition def, bool turnIn)
        {
            if (def == null) return;

            var card = new GameObject("QuestCard_" + def.questId);
            card.transform.SetParent(_questContent, false);
            card.AddComponent<RectTransform>();
            card.AddComponent<Image>().color = new Color(0.10f, 0.12f, 0.09f, 1f);

            var le = card.AddComponent<LayoutElement>();
            le.flexibleHeight = 0f;

            var hlg = card.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(6, 6, 4, 4);
            hlg.spacing = 6f;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;

            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(card.transform, false);
            bodyGo.AddComponent<RectTransform>();
            var body = bodyGo.AddComponent<TextMeshProUGUI>();
            body.fontSize = 11;
            body.color = new Color(0.88f, 0.86f, 0.80f);
            body.alignment = TextAlignmentOptions.TopLeft;
            body.enableWordWrapping = true;
            body.overflowMode = TextOverflowModes.Ellipsis;
            body.text = BuildQuestCardText(def);

            // Sized from the text rather than from a constant. TMP can only answer this
            // once it knows how wide it will be, and uGUI performs no layout in Edit Mode
            // — so the width is COMPUTED from the sheet own geometry rather than read
            // back off a rect that has not been laid out yet, and the fixture-safe floor
            // catches the Edit Mode case where the preferred height comes back as 0.
            float bodyWidth = _questContent.rect.width - CARD_SIDE_PADDING * 2f
                            - QUEST_ACTION_WIDTH - CARD_INNER_SPACING;
            if (bodyWidth < 120f) bodyWidth = QUEST_FALLBACK_BODY_WIDTH;
            float textHeight = body.GetPreferredValues(body.text, bodyWidth, 0f).y;
            le.preferredHeight = Mathf.Max(QUEST_CARD_MIN_HEIGHT, textHeight + CARD_TEXT_PADDING);

            var action = CreateInlineButton(
                card.transform,
                turnIn ? ChatLanguage.QuestTurnIn : ChatLanguage.QuestAccept,
                turnIn ? new Color(0.20f, 0.45f, 0.24f, 1f) : new Color(0.18f, 0.38f, 0.5f, 1f),
                QUEST_ACTION_WIDTH);
            action.gameObject.name = turnIn ? "QuestTurnInButton" : "QuestAcceptButton";

            string questId = def.questId;
            if (turnIn) action.onClick.AddListener(() => HandInQuest(questId));
            else        action.onClick.AddListener(() => AcceptQuest(questId));

            _questRows.Add(card);
        }

        private static string BuildQuestCardText(QuestDefinition def)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<b>").Append(def.displayName).Append("</b>");
            if (def.recommendedLevel > 0)
                sb.Append("  <size=9>").Append(ChatLanguage.QuestSuggestedLevel(def.recommendedLevel)).Append("</size>");
            sb.AppendLine();

            string hook = string.IsNullOrWhiteSpace(def.hookLine) ? def.description : def.hookLine;
            if (!string.IsNullOrWhiteSpace(hook)) sb.AppendLine(hook);

            // What the quest will actually ASK. Without it the player accepts on the
            // strength of a paragraph of character voice and only learns the tasks from the
            // tracker afterwards — which is the one moment they can no longer decline.
            if (def.objectives != null && def.objectives.Length > 0)
            {
                sb.Append("<size=10><color=#9AA79A>").Append(ChatLanguage.QuestObjectivesPreview);
                for (int i = 0; i < def.objectives.Length; i++)
                {
                    sb.AppendLine();
                    sb.Append("  - ").Append(Quests.QuestManager.DescribeObjective(def.objectives[i]));
                }
                sb.Append("</color></size>").AppendLine();
            }

            sb.Append("<size=9>").Append(ChatLanguage.QuestReward(def.xpReward, def.coinReward)).Append("</size>");
            return sb.ToString();
        }

        // ── Actions ─────────────────────────────────────────────────────────

        /// <summary>
        /// Takes the quest and says so in the character's own voice, then redraws — the
        /// accepted quest leaves the offer list, which is the only feedback that cannot
        /// be mistaken for a button that did nothing.
        /// </summary>
        private void AcceptQuest(string questId)
        {
            var service = QuestService.Instance;
            if (service == null) return;
            if (!service.TryAccept(questId)) return;

            // An ACKNOWLEDGEMENT, never the pitch again. The hook is already on screen
            // twice by this point — spoken into the transcript when the panel opened, and
            // printed on the card the player just pressed — so repeating it a third time
            // reads as the button having failed and done nothing but scroll.
            var def = service.Catalog != null ? service.Catalog.Find(questId) : null;
            if (def != null)
                ChatSystem.Instance?.SpeakAsActiveNpc(ChatLanguage.QuestAccepted(def.displayName));

            RenderQuests();
        }

        /// <summary>
        /// Closes a finished quest.
        ///
        /// <para>It does NOT complete the quest itself — it fires the same
        /// <c>OnNpcConversed</c> the conversation fires, and the generated turn-in
        /// objective is what notices. One completion path, whether the player pressed a
        /// button or simply walked up and talked; a second one here would be a way to
        /// finish a quest whose gate had not actually closed.</para>
        /// </summary>
        private void HandInQuest(string questId)
        {
            var persona = ChatSystem.Instance != null ? ChatSystem.Instance.ActivePersona : null;
            if (persona == null || string.IsNullOrEmpty(persona.personaId)) return;

            Valkur.Core.GameEvents.FireNpcConversed(persona.personaId);
            RenderQuests();
        }

        /// <summary>
        /// Shows or hides the gutter's Misiones button for the character in front of the
        /// player. Called from the same place that decides the rest of the column.
        ///
        /// <para>Conditional, like Comerciar and unlike Diario: most characters have
        /// nothing to give most of the time, and a permanently dead button is a control
        /// that teaches the player it does nothing.</para>
        /// </summary>
        private bool ShouldShowQuestButton(string personaId)
        {
            var service = QuestService.Instance;
            if (service == null || string.IsNullOrEmpty(personaId)) return false;

            service.TurnInsReadyFor(personaId, _questTurnIns);
            if (_questTurnIns.Count > 0) return true;

            service.OffersFor(personaId, _questOffers);
            if (_questOffers.Count > 0) return true;

            // A quest of theirs still in progress keeps the button on screen. Hiding it
            // there is what made an accepted quest look like it had never been given.
            service.ActiveFrom(personaId, _questActive);
            return _questActive.Count > 0;
        }
    }
}
