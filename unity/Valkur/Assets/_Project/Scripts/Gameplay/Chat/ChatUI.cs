using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// Runtime IMGUI-style chat panel UI.
    /// Shows message history, text input field, and send button.
    ///
    /// Maps to Python's ChatUISystem + ChatUIRenderer + ChatInputController.
    ///
    /// Python constants preserved:
    ///   Panel min size: 320×160 px
    ///   Panel default size: 520×250 px
    ///   Scroll step: 3 lines
    ///   Font size: 16 (main), 14 (small)
    ///
    /// The panel is resizable from its top-right grip and remembers the size between
    /// sessions. The ceiling is not a constant — it is the live viewport, so a size saved on
    /// a large monitor cannot come back larger than the window it is restored into.
    /// </summary>
    public partial class ChatUI : SingletonMonoBehaviour<ChatUI>
    {
        /// <summary>
        /// Narrowest the panel may be dragged, in pixels.
        ///
        /// <para>Raised from the ported 320 when the gutter became UNCONDITIONAL, and again
        /// from 420 when it grew by half. While it was reserved only for a character with a
        /// face, refusing to raise this was the right call — it would have clamped a size the
        /// player saved on a portrait-less NPC upward the moment they talked to Gatita. Now
        /// every conversation spends 144 px on the column before a word of dialogue is
        /// placed, so a floor that ignores it is a floor that leaves 168 px for the
        /// conversation. Tracks <see cref="PORTRAIT_GUTTER"/>: the two moved together, and a
        /// wider column with an unchanged floor is a floor that means something different.</para>
        /// </summary>
        private const float PANEL_MIN_W = 470f;

        /// <summary>
        /// Shortest the panel may be dragged, in pixels.
        ///
        /// <para>Raised from the ported 160 because that number had never been READ. Making
        /// the grip honour it turned it from a comment into a size the player can actually
        /// reach, and at 160 the panel cannot hold its own fixed rows: title 28, scroll floor,
        /// input 32, trade 24, reset 20, close 24, plus the gaps and 16 px of padding.</para>
        ///
        /// <para>Deliberately NOT raised past <see cref="PANEL_DEFAULT_H"/>, which would drag
        /// the default size up with it through the restore clamp and change the panel every
        /// player already knows. What makes 244 hold when a trade offer adds its confirmation
        /// row instead is <see cref="SCROLL_MIN_H"/>: the conversation is the elastic part and
        /// gives up the space, rather than the panel overflowing.</para>
        ///
        /// <para><c>ChatUIBuilderTests</c> lays the panel out AT this size, in both states,
        /// and fails if the rows no longer fit — so adding a row is a red test rather than a
        /// silently clipped conversation.</para>
        ///
        /// <para>Raised from 244 when Diario joined the gutter. It is DERIVED now, from the
        /// column that actually has to fit: the portrait, the two stacked buttons under it
        /// and Reiniciar pinned at the foot. Typing a number here and a button height there
        /// is how the tallest gutter quietly grows past the shortest panel — and the failure
        /// is silent, because free-floating children do not participate in layout and simply
        /// overlap.</para>
        /// </summary>
        private const float PANEL_MIN_H =
            PANEL_PADDING + PORTRAIT_SIZE_H +
            GUTTER_GAP + GUTTER_TRADE_HEIGHT +
            GUTTER_GAP + GUTTER_JOURNAL_HEIGHT +
            GUTTER_GAP +
            GUTTER_RESET_HEIGHT + PANEL_PADDING;

        /// <summary>
        /// Shortest the message area may be squeezed to, in pixels.
        ///
        /// <para>Lowered from 80 so the scroll area can absorb the trade confirmation row
        /// appearing beneath it. At 80 that row had nowhere to come from and the panel simply
        /// overflowed its own rect — silently, because uGUI clips rather than complaining, so
        /// it read as the bottom of the conversation being cut off at the exact moment an
        /// offer was on the table.</para>
        ///
        /// <para>48 px is about two lines at the panel's font size: not comfortable, and it is
        /// the transient state of a panel already at its smallest. The scroll bar still
        /// reaches the rest.</para>
        /// </summary>
        private const float SCROLL_MIN_H = 48f;

        /// <summary>
        /// Padding the panel keeps on every side. Named because the portrait gutter is
        /// expressed as an ADDITION to the left one, and the two have to move together.
        /// </summary>
        private const float PANEL_PADDING = 8f;

        /// <summary>
        /// Width reserved down the left of the panel for the character's face, when the
        /// character has one.
        ///
        /// <para>Deliberately NOT added to <see cref="PANEL_MIN_W"/>. A per-character minimum
        /// would have to be static mutable state on a class where Domain Reload is off, and
        /// it would clamp a size the player saved on a portrait-less NPC upward the moment
        /// they talked to Gatita. At the minimum width the conversation gives up the space
        /// instead — the same trade <see cref="SCROLL_MIN_H"/> makes for the trade row.</para>
        /// </summary>
        private const float PORTRAIT_GUTTER = 144f;

        /// <summary>
        /// Drawn size of the portrait frame. Height leads: the expression lives in the eyes
        /// and mouth, which are about a third of a face, so 141 px of face is roughly 47 px of
        /// the part the player is actually reading. The width follows the source art's
        /// 370:395 (132/141 = 0.936 against 0.937), and <c>preserveAspect</c> keeps a
        /// differently-shaped drawing honest.
        ///
        /// <para>Raised by half from 88x94. The faces are drawn at 370x395 and were being
        /// rendered at under a quarter of that, which is the resolution at which the
        /// difference between two expressions stops being legible — and the expression is the
        /// entire reason the portrait is on screen rather than a name.</para>
        /// </summary>
        private const float PORTRAIT_SIZE_W = 132f;
        private const float PORTRAIT_SIZE_H = 141f;

        /// <summary>Border of frame left visible around the face.</summary>
        private const float PORTRAIT_INSET = 3f;

        /// <summary>
        /// How long one expression takes to dissolve into the next. Short: this is a change
        /// of face, not a scene transition, and anything past about a fifth of a second reads
        /// as the portrait being slow rather than as the character reacting.
        /// </summary>
        private const float PORTRAIT_FADE_SEC = 0.14f;

        /// <summary>Frame behind the face. A shade darker than the panel so the head reads as
        /// being IN something rather than floating on the panel.</summary>
        private static readonly Color PORTRAIT_FRAME_COLOR = new Color(0.05f, 0.05f, 0.08f, 0.95f);

        /// <summary>
        /// The size a player who has never dragged the grip opens the panel at — the ported
        /// 520x250 raised by a quarter.
        ///
        /// <para>A bigger default is only half the change, and the missing half is why a
        /// number like this normally does nothing: the restore path prefers whatever the
        /// player last dragged the panel to, so anyone who had ever touched the grip would
        /// keep the old size forever and read the change as not having happened. That is what
        /// <see cref="PREF_LAYOUT_VERSION"/> is for.</para>
        ///
        /// <para>The width moved 650 to 700 when the gutter grew by half. The 48 px the column
        /// took are given back to the panel rather than to the conversation's cost: widening
        /// the face is not a reason to narrow the dialogue, and leaving the panel alone would
        /// have made every line wrap sooner as a side effect of a change about the face.</para>
        /// </summary>
        private const float PANEL_DEFAULT_W = 700f;
        private const float PANEL_DEFAULT_H = 312f;

        /// <summary>
        /// Width of the Send button. Small on purpose: the message field is the control the
        /// player uses, and Enter sends anyway — the button is the discoverable alternative,
        /// not the main event. It used to be given 60 and take half the row regardless,
        /// because the row's layout group was force-expanding its children.
        /// </summary>
        private const float SEND_BUTTON_WIDTH = 56f;

        /// <summary>
        /// Inset of the free-floating corner controls from the panel's top-right edge.
        ///
        /// They are floating because a child of the panel is otherwise owned by its
        /// <c>VerticalLayoutGroup</c>, which overwrites any anchor set on it — the LangButton
        /// once came out 504x0 that way, an invisible full-width strip eating the close
        /// button's clicks.
        /// </summary>
        private const float CORNER_MARGIN = 6f;

        /// <summary>Height shared by the corner controls, so they sit on one line.</summary>
        private const float CORNER_BUTTON_HEIGHT = 22f;

        /// <summary>Width of the close "X". Square-ish: it holds one glyph.</summary>
        private const float CLOSE_X_SIZE = 24f;

        /// <summary>Width of the language toggle, which holds a two-letter code.</summary>
        private const float LANG_BUTTON_WIDTH = 42f;

        /// <summary>Gap between the corner controls.</summary>
        private const float CORNER_GAP = 4f;

        /// <summary>
        /// Side of the square resize grip. Matches the 16 px every resizable runtime editor
        /// panel uses, so the target the player aims at is the same size everywhere.
        /// </summary>
        private const float RESIZE_GRIP_SIZE = 16f;

        /// <summary>
        /// Margin left between the panel edge and the largest the viewport will allow it to
        /// grow. Twice the panel's own 20 px offset from the corner, so a fully-grown panel
        /// keeps the same breathing space at the far edges that it has at the near ones.
        /// </summary>
        private const float PANEL_SCREEN_MARGIN = 40f;

        /// <summary>
        /// Where the remembered panel size lives.
        ///
        /// <para>PlayerPrefs, following <c>MusicPlayerHUD</c> — the project's other resizable
        /// non-editor panel — and NOT the editor workspace layer. That layer is a better store
        /// (schema-versioned, atomic, on disk rather than in the registry) and it is
        /// structurally out of reach here: every entry point on <c>IEditorWorkspaceService</c>
        /// is typed on <c>GameEditorManager.IGameEditor</c> and keyed on <c>EditorName</c>, so
        /// the chat panel would have to impersonate an editor to use it. Impersonating one
        /// would also put chat geometry under the editor open/close hooks, which chat does not
        /// pass through at all.</para>
        /// </summary>
        private const string PREF_PANEL_WIDTH = "valkur.chat.panel.width";

        /// <summary>Sibling of <see cref="PREF_PANEL_WIDTH"/>. See its note for the choice of store.</summary>
        private const string PREF_PANEL_HEIGHT = "valkur.chat.panel.height";

        /// <summary>
        /// Which arrangement of the panel the remembered size belongs to.
        ///
        /// <para>A saved size is only meaningful against the layout it was dragged on. When
        /// the rows are rearranged — three stacked action strips becoming one footer row, the
        /// default growing a quarter — a size from the old arrangement is not a preference to
        /// honour, it is a measurement of a panel that no longer exists. Restoring it hands
        /// the player the OLD size and makes a deliberate change look like one that failed to
        /// ship, which is worse than the size being reset: they can drag it back in one
        /// gesture, and they cannot discover a default they never see.</para>
        ///
        /// <para>Bump <see cref="PANEL_LAYOUT_VERSION"/> whenever the row structure or the
        /// default size changes, and never for a colour or a label.</para>
        /// </summary>
        private const string PREF_LAYOUT_VERSION = "valkur.chat.panel.layout";

        /// <summary>See <see cref="PREF_LAYOUT_VERSION"/>. 4 = gutter widened by half, 700x312.</summary>
        private const int PANEL_LAYOUT_VERSION = 4;

        /// <summary>
        /// Space the panel keeps between its rows. Wider than the ported 4 because the panel
        /// is now a quarter larger in both directions: keeping the old gap would have spent
        /// the whole increase on the conversation and left the chrome looking cramped inside
        /// a roomier window, which is the shape that reads as "stretched" rather than
        /// "bigger".
        /// </summary>
        private const float PANEL_SPACING = 6f;

        /// <summary>
        /// Height of the title row, which is the panel's first child and the strip the
        /// floating corner controls share.
        ///
        /// <para>Named because a second thing measures against it now: the journal overlay
        /// starts below it, and an overlay that swallows the close button and the resize grip
        /// is a window the player is stuck in. A literal in the builder and a literal in the
        /// overlay would be two numbers that have to agree, with nothing to make them.</para>
        /// </summary>
        private const float TITLE_ROW_HEIGHT = 28f;

        /// <summary>
        /// Width of the buttons that live in the left gutter. The portrait's width exactly,
        /// so the face and the controls under it share one edge and the gutter reads as a
        /// column rather than as three things that happen to be on the left.
        /// </summary>
        private const float GUTTER_BUTTON_WIDTH = PORTRAIT_SIZE_W;

        /// <summary>Height of the gutter's Comerciar button.</summary>
        private const float GUTTER_TRADE_HEIGHT = 28f;

        /// <summary>
        /// Height of the gutter's Diario button. The same as Comerciar's: they are the two
        /// things a player OPENS from this panel and there is no reason for one to look
        /// heavier than the other.
        /// </summary>
        private const float GUTTER_JOURNAL_HEIGHT = 28f;

        /// <summary>
        /// Height of the gutter's Reiniciar button. The same as Comerciar's, so the two
        /// controls in the column are one shape.
        ///
        /// <para>It used to be 34 to hold a wrapped second line, which the 88 px column
        /// forced. Measured at the widened 132: the idle caption needs 81 px and the armed
        /// one 110, against a 126 px text box — both fit on one line, so the extra height was
        /// reserving room for a wrap that can no longer happen. The label still WRAPS rather
        /// than elides, because a control that deletes player memory should overflow visibly
        /// rather than silently lose the end of its own caption.</para>
        /// </summary>
        private const float GUTTER_RESET_HEIGHT = 28f;

        /// <summary>Gap between the portrait and the button under it.</summary>
        private const float GUTTER_GAP = 6f;

        /// <summary>
        /// How much of each end of the title row the corner controls occupy.
        ///
        /// DERIVED rather than typed, so moving or resizing either button cannot leave the
        /// title running underneath it — that failure is silent, because nothing in uGUI
        /// arranges free-floating children against a layout row. Applied to BOTH ends because
        /// the title is centred: insetting only the right would shift the name off centre.
        /// </summary>
        private const float CORNER_STRIP_WIDTH =
            RESIZE_GRIP_SIZE + CORNER_GAP + CLOSE_X_SIZE + CORNER_GAP + LANG_BUTTON_WIDTH + CORNER_GAP;

        protected override bool Persist => false;

        private Canvas _canvas;
        private GameObject _panel;
        private GameObject _backdrop;
        private ScrollRect _scrollRect;
        private RectTransform _contentRect;
        private TMP_InputField _inputField;

        /// <summary>
        /// Tells the character whether the player is mid-sentence.
        ///
        /// <para>Keyed on the field HAVING TEXT rather than on it having focus. Focus is the
        /// wrong question twice over: the field is focused for the whole conversation —
        /// <c>ActivateInputField</c> is called on open and again after every send — so a
        /// focus test would say she is listening while she is the one talking, and it would
        /// never turn off. Text present is exactly "there is a sentence being composed", and
        /// it clears itself on submit, which is the same instant her reply starts.</para>
        /// </summary>
        private void OnInputTextChanged(string text)
        {
            if (ChatSystem.Instance == null) return;
            ChatSystem.Instance.SetPlayerTyping(!string.IsNullOrEmpty(text));
        }
        private TextMeshProUGUI _titleText;
        private TextMeshProUGUI _langButtonText;
        private TextMeshProUGUI _placeholderText;
        private TextMeshProUGUI _sendButtonText;
        private TextMeshProUGUI _tradeButtonText;
        private GameObject _tradeButton;
        private GameObject _journalButton;
        private TextMeshProUGUI _journalButtonText;
        private GameObject _resetButton;
        private GameObject _tradeConfirmRow;
        private TextMeshProUGUI _tradeOfferText;
        private TextMeshProUGUI _resetButtonText;

        /// <summary>
        /// The panel's own layout group, kept so the portrait gutter can be reserved and
        /// released per conversation by moving its left padding.
        /// </summary>
        private VerticalLayoutGroup _panelLayout;

        /// <summary>Idle label of the Reset button. Wraps to two lines in the gutter.</summary>
        private const string RESET_LABEL_IDLE = "Reiniciar memoria";

        /// <summary>Armed label. The second click is the one that deletes.</summary>
        private const string RESET_LABEL_ARMED = "¿Seguro? Pulsa otra vez";

        /// <summary>
        /// How long the armed state lasts. Long enough to mean a deliberate second click,
        /// short enough that a button left armed cannot fire on a later, unrelated one.
        /// </summary>
        private const float RESET_ARM_SECONDS = 3f;

        private float _resetArmedUntil;
        private readonly List<GameObject> _messageRows = new List<GameObject>();

        private bool _isBuilt;

        private void Start()
        {
            BuildUI();
            _panel.SetActive(false);
            if (_backdrop != null) _backdrop.SetActive(false);

            var chatSystem = ChatSystem.Instance;
            if (chatSystem != null)
            {
                chatSystem.OnChatOpened += OnChatOpened;
                chatSystem.OnChatClosed += OnChatClosed;
                chatSystem.OnMessageReceived += OnMessageReceived;
                chatSystem.OnHistoryReset += RebuildMessageRows;
                chatSystem.OnTradeOfferChanged += OnTradeOfferChanged;
                chatSystem.OnExpressionChanged += OnExpressionChanged;
                chatSystem.OnListeningChanged += OnListeningChanged;
                chatSystem.OnOverlayDismissRequested += CloseJournal;
                chatSystem.OnDayRolledOver += OnDayRolledOver;
                ChatLanguage.OnChanged += ApplyLanguageToChrome;
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            var chatSystem = ChatSystem.Instance;
            if (chatSystem != null)
            {
                chatSystem.OnChatOpened -= OnChatOpened;
                chatSystem.OnChatClosed -= OnChatClosed;
                chatSystem.OnMessageReceived -= OnMessageReceived;
                chatSystem.OnHistoryReset -= RebuildMessageRows;
                chatSystem.OnTradeOfferChanged -= OnTradeOfferChanged;
                chatSystem.OnExpressionChanged -= OnExpressionChanged;
                chatSystem.OnListeningChanged -= OnListeningChanged;
                chatSystem.OnOverlayDismissRequested -= CloseJournal;
                chatSystem.OnDayRolledOver -= OnDayRolledOver;
                ChatLanguage.OnChanged -= ApplyLanguageToChrome;
            }
        }

        private void Update()
        {
            // Before the early return below, which exists to watch for Enter and would
            // otherwise skip the crossfade on every frame the player is not pressing it.
            TickPortraitFade();

            // Enter key: open / send / close, depending on current state.
            // Routed through KeyboardInputManager so the legacy backend keeps
            // it working when the new InputSystem drops events (Unity 2022.3 bug).
            bool enterPressed = Valkur.Core.Input.KeyboardInputManager.WasEnterPressedThisFrame();
            if (!enterPressed) return;

            // If the DevConsole is open, let it consume Enter instead.
            var console = Valkur.Gameplay.DevConsole.Instance;
            if (console != null && console.IsOpen) return;

            var chatSystem = ChatSystem.Instance;
            if (chatSystem == null) return;

            if (!chatSystem.IsChatOpen)
            {
                // Chat not open — try to open with the nearest NPC, using the
                // player's world position as the proximity anchor.
                var player = EntityRegistry.PlayerTransform;
                Vector2 anchor = player != null ? (Vector2)player.position : Vector2.zero;
                chatSystem.TryOpenChat(anchor);
                return;
            }

            // The archive is up. Enter takes the player back to the conversation rather than
            // sending — the field is disabled and covered, so "send" would post whatever
            // draft was there before the diary was opened, into a panel they cannot see.
            if (IsJournalOpen)
            {
                CloseJournal();
                return;
            }

            // Chat is open: send if the field has text, close if it is empty.
            string text = _inputField != null ? (_inputField.text?.Trim() ?? "") : "";
            if (string.IsNullOrEmpty(text))
                chatSystem.CloseChat();
            else
                SubmitInput();
        }

        // ── Event Handlers ──

        private void OnChatOpened()
        {
            if (_backdrop != null) _backdrop.SetActive(true);
            _panel.SetActive(true);
            ClearMessages();

            var chatSystem = ChatSystem.Instance;
            string npcName = chatSystem.ActivePersona != null
                ? chatSystem.ActivePersona.displayName
                : "NPC";
            _titleText.text = $"Chat — {npcName}";

            // Only a character who actually sells something gets a Trade button — and this
            // has to be decided BEFORE the gutter is laid out, because a switched-off button
            // consumes no space in the column. Setting it afterwards leaves Diario floating
            // over a gap where Comerciar would have been, and nothing complains: the gutter's
            // children are ignoreLayout, so nothing arranges them.
            if (_tradeButton != null)
                _tradeButton.SetActive(chatSystem.ActiveVendor != null);

            // Before the history is replayed, so the panel is already the right SHAPE when
            // the rows are laid into it — reserving the gutter afterwards would lay every
            // row out twice on the frame a conversation opens.
            ConfigurePortraitFor(chatSystem.ActivePersona);

            // The panel follows the GLOBAL preference; the conversation's own memory is
            // brought into line with it rather than the other way round, so a language the
            // player chose with one character is the language the next one opens in.
            ApplyLanguageToChrome(ChatLanguage.Current);
            SyncActiveMemoryLanguage();

            DisarmReset();
            CloseJournal();
            OnTradeOfferChanged(false);

            // Show existing history
            foreach (var msg in chatSystem.History)
                AppendMessageRow(msg.sender, msg.text);

            _inputField.text = "";
            _inputField.ActivateInputField();
        }

        private void OnChatClosed()
        {
            // Before the panel goes: closing the overlay also clears the modal flag and gives
            // the input field back its interactable state, and both are properties of a panel
            // that is about to be shown again for someone else.
            CloseJournal();

            _panel.SetActive(false);
            if (_backdrop != null) _backdrop.SetActive(false);
        }

        private void OnMessageReceived(string sender, string text)
        {
            if (!_panel.activeSelf) return;
            AppendMessageRow(sender, text);

            // Auto-scroll to bottom
            Canvas.ForceUpdateCanvases();
            _scrollRect.verticalNormalizedPosition = 0f;
        }

        private void SubmitInput()
        {
            string text = _inputField.text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            ChatSystem.Instance?.SubmitPlayerMessage(text);
            _inputField.text = "";
            _inputField.ActivateInputField();
        }

        /// <summary>
        /// Opens the shop of the character being talked to, and closes the conversation
        /// behind it — two stacked modal panels would both be claiming the input blocker
        /// and Escape would only dismiss one of them.
        /// </summary>
        private void OnTradeClicked()
        {
            var chatSystem = ChatSystem.Instance;
            if (chatSystem == null) return;
            if (chatSystem.TryOpenTradeWithTarget())
                chatSystem.CloseChat();
        }

        /// <summary>
        /// Shows or hides the confirmation row, and writes what is being offered.
        ///
        /// The label is built from the QUOTE — the game's own numbers — not from anything
        /// the character said. What the player is agreeing to has to be the thing that will
        /// actually happen, stated in the same words the receipt will use.
        /// </summary>
        private void OnTradeOfferChanged(bool hasOffer)
        {
            if (_tradeConfirmRow == null) return;

            var chatSystem = ChatSystem.Instance;
            var offer = chatSystem != null ? chatSystem.PendingTrade : default;

            bool show = hasOffer && offer.IsValid && offer.Item != null;
            _tradeConfirmRow.SetActive(show);
            if (!show) return;

            string what = offer.Quantity > 1
                ? $"{offer.Quantity}x {offer.Item.displayName}"
                : offer.Item.displayName;

            _tradeOfferText.text = offer.Intent == Providers.TradeIntent.Buy
                ? $"Comprar {what} — {offer.TotalPrice} g"
                : $"Vender {what} — +{offer.TotalPrice} g";
        }

        private void OnTradeAccepted()
        {
            ChatSystem.Instance?.ConfirmPendingTrade();
        }

        private void OnTradeDeclined()
        {
            ChatSystem.Instance?.CancelPendingTrade();
        }

        /// <summary>
        /// Wipes what this NPC remembers, on the SECOND click.
        ///
        /// A confirm rather than a modal because this is a testing control used repeatedly:
        /// a dialog would be in the way twenty times an evening. The arming times out, so a
        /// button left armed by a click the player thought better of cannot fire minutes
        /// later on a different NPC.
        /// </summary>
        private void OnResetClicked()
        {
            var chatSystem = ChatSystem.Instance;
            if (chatSystem == null || !chatSystem.IsChatOpen) return;

            if (Time.unscaledTime > _resetArmedUntil)
            {
                _resetArmedUntil = Time.unscaledTime + RESET_ARM_SECONDS;
                if (_resetButtonText != null) _resetButtonText.text = RESET_LABEL_ARMED;
                return;
            }

            DisarmReset();
            OnTradeOfferChanged(false);
            if (chatSystem.ResetActiveMemory())
                RebuildMessageRows();
        }

        private void DisarmReset()
        {
            _resetArmedUntil = 0f;
            if (_resetButtonText != null) _resetButtonText.text = RESET_LABEL_IDLE;
        }

        /// <summary>
        /// Rebuilds every row from the current history. Used when the transcript was
        /// REPLACED rather than appended to, which OnMessageReceived cannot express.
        /// </summary>
        private void RebuildMessageRows()
        {
            var chatSystem = ChatSystem.Instance;
            if (chatSystem == null) return;

            ClearMessages();
            foreach (var msg in chatSystem.History)
                AppendMessageRow(msg.sender, msg.text);

            Canvas.ForceUpdateCanvases();
            if (_scrollRect != null) _scrollRect.verticalNormalizedPosition = 0f;
        }

        // ── Language toggle ──

        /// <summary>
        /// Cycles the panel between Spanish and English.
        ///
        /// <para>It used to require <c>ActiveMemory</c> and early-return without it, which
        /// made the button dead for the whole window between opening a panel and the memory
        /// arriving — and permanently dead for any conversation with no memory at all. The
        /// preference belongs to the player, so it is stored globally now and the per-NPC
        /// field is written alongside it, purely so <c>OpenAiChatProvider</c> keeps reading
        /// the value it always read.</para>
        /// </summary>
        private void ToggleLang()
        {
            ChatLanguage.Toggle();
            SyncActiveMemoryLanguage();
        }

        /// <summary>
        /// Copies the global preference onto the open conversation's memory and saves it.
        ///
        /// <para>The prompt builder reads <c>NPCMemory.preferredLanguage</c> and nothing
        /// else, so this is what keeps a language chosen in the panel reaching the model.
        /// Null-safe: a conversation with no memory simply has nothing to sync, and the
        /// panel is still in the right language because the panel reads the global.</para>
        /// </summary>
        private void SyncActiveMemoryLanguage()
        {
            NPCMemory mem = ChatSystem.Instance?.ActiveMemory;
            if (mem == null || mem.preferredLanguage == ChatLanguage.Current) return;

            mem.preferredLanguage = ChatLanguage.Current;
            NPCMemoryStore.Save(mem);
        }

        /// <summary>
        /// Re-labels every caption the panel owns. Bound to
        /// <c>ChatLanguage.OnChanged</c>, so a language switched from anywhere reaches an
        /// open panel rather than waiting for it to be reopened.
        ///
        /// <para>What it deliberately does NOT touch is the conversation itself. Those lines
        /// are authored Spanish from the persona archive and there is no English set to swap
        /// to; re-labelling them would mean inventing dialogue. English moves the chrome and
        /// the model's instruction, and leaves what the character actually said alone.</para>
        /// </summary>
        private void ApplyLanguageToChrome(string language)
        {
            if (_langButtonText != null) _langButtonText.text = ChatLanguage.Label;
            if (_placeholderText != null) _placeholderText.text = ChatLanguage.InputPlaceholder;
            if (_sendButtonText != null) _sendButtonText.text = ChatLanguage.Send;
            if (_tradeButtonText != null) _tradeButtonText.text = ChatLanguage.Trade;

            ApplyLanguageToJournal();

            // The archive's PAGE is language-dependent too — the day label, the counter, the
            // notice on a trimmed day — so a view that is up is redrawn rather than merely
            // relabelled. Guarded, because rebuilding rows for a hidden overlay is the cost
            // of the whole feature paid on every language toggle.
            if (IsJournalOpen) RenderJournalPage();
        }

        // ── UI Construction ──
        // See ChatUI.Builder.cs for BuildUI, AppendMessageRow, ClearMessages, CreateTextRow
    }
}
