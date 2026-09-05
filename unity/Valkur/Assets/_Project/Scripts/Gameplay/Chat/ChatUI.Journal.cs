using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Valkur.Gameplay.Chat
{
    /// <summary>
    /// The Diario: everything this character and the player have ever said, one day at a
    /// time.
    ///
    /// <para>WHY IT COVERS THE CONVERSATION INSTEAD OF BEING ITS OWN WINDOW. A second panel
    /// would need its own position, its own remembered size, its own close control and its
    /// own answer to what Escape does — four things this panel has already solved — and it
    /// would be a window about a conversation floating next to the conversation. Covering the
    /// message area keeps the title, the face and the gutter on screen, so the player is
    /// plainly still talking to the same person while reading what they said last week.</para>
    ///
    /// <para>WHAT IT DELIBERATELY DOES NOT COVER: the title bar and the corner controls. An
    /// overlay that swallows the close button and the resize grip is a window the player is
    /// stuck in, and the Diario button itself stays visible so the control that opened the
    /// view is the control that closes it.</para>
    ///
    /// <para>ESCAPE IS NOT READ HERE. <see cref="ChatSystem"/> owns that key for the whole
    /// subsystem; this view raises a flag and listens for the dismissal. Two readers of one
    /// key in an undefined Update order is how a single press closes the overlay AND the
    /// panel behind it, or neither, depending on the frame.</para>
    /// </summary>
    public partial class ChatUI
    {
        /// <summary>
        /// How far down the panel the overlay starts: below the title row and the floating
        /// corner controls that share that line.
        ///
        /// <para>DERIVED, like <c>CORNER_STRIP_WIDTH</c>, and for the same reason: a typed
        /// number here goes stale the moment the title row changes height, and the failure is
        /// invisible — nothing arranges a free-floating child, so the overlay would simply
        /// start swallowing the close button.</para>
        /// </summary>
        private const float JOURNAL_TOP_INSET = PANEL_PADDING + TITLE_ROW_HEIGHT + PANEL_SPACING;

        /// <summary>Height of the day selector strip.</summary>
        private const float JOURNAL_NAV_HEIGHT = 24f;

        /// <summary>Height of the line that counts the pages and the messages on one.</summary>
        private const float JOURNAL_SUMMARY_HEIGHT = 16f;

        /// <summary>Width of the two arrow buttons that walk the days.</summary>
        private const float JOURNAL_ARROW_WIDTH = 28f;

        /// <summary>Width of the button that goes back to the conversation.</summary>
        private const float JOURNAL_BACK_WIDTH = 64f;

        /// <summary>Padding inside the overlay.</summary>
        private const float JOURNAL_PADDING = 6f;

        /// <summary>
        /// How many lines of one day are actually built as widgets.
        ///
        /// <para>A page is unbounded — a player can talk to a vendor all afternoon — and this
        /// view builds one TMP row per line. The Items editor measured what that costs at
        /// scale: 6,840 widgets took 3.5 seconds to open a panel, and every one of them was a
        /// perfectly reasonable half a millisecond. Rather than virtualise a view that is
        /// read, not edited, the NEWEST lines are built and the rest are declared: a day with
        /// more than this says so at the top, and the file still holds all of it.</para>
        /// </summary>
        private const int JOURNAL_MAX_RENDERED_ENTRIES = 300;

        private GameObject _journalRoot;
        private ScrollRect _journalScroll;
        private RectTransform _journalContent;
        private TextMeshProUGUI _journalDayLabel;
        private TextMeshProUGUI _journalSummary;
        private TextMeshProUGUI _journalBackText;
        private Button _journalOlderButton;
        private Button _journalNewerButton;

        private readonly List<GameObject> _journalRows = new List<GameObject>();

        /// <summary>
        /// The days on record, newest first, as of the moment the view was opened.
        ///
        /// <para>Snapshotted rather than re-listed per frame: it is a directory listing, and
        /// the only thing that can add to it while the view is up is a midnight rollover,
        /// which announces itself.</para>
        /// </summary>
        private List<ChatJournalPageRef> _journalPages = new List<ChatJournalPageRef>();

        private int _journalIndex;

        /// <summary>True while the archive is covering the conversation.</summary>
        internal bool IsJournalOpen => _journalRoot != null && _journalRoot.activeSelf;

        /// <summary>Which day is being read. -1 when the view is closed or the archive is empty.</summary>
        internal int JournalIndex => IsJournalOpen && _journalPages.Count > 0 ? _journalIndex : -1;

        // ── Build ───────────────────────────────────────────────────────────

        /// <summary>
        /// Creates the overlay, hidden. Called once from the builder, like everything else in
        /// this panel: a view built on first use would pay for itself in the middle of a
        /// conversation, and this one is a scroll view with a layout group in it.
        /// </summary>
        private void BuildJournalOverlay(Transform panel)
        {
            _journalRoot = new GameObject("JournalOverlay");
            _journalRoot.transform.SetParent(panel, false);

            var rt = _journalRoot.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;

            // Left of the message area, so the face and the gutter stay visible; clear of the
            // title row at the top. Stretched on both axes, so it follows the panel when the
            // grip resizes it and never needs to be told a size.
            rt.offsetMin = new Vector2(PANEL_PADDING + PORTRAIT_GUTTER, PANEL_PADDING);
            rt.offsetMax = new Vector2(-PANEL_PADDING, -JOURNAL_TOP_INSET);

            // Without this the panel's VerticalLayoutGroup claims the rect and the stretch
            // above is overwritten on the next rebuild — the trap every free child of this
            // panel is documented against.
            _journalRoot.AddComponent<LayoutElement>().ignoreLayout = true;

            // OPAQUE on purpose. The conversation is directly underneath and a translucent
            // sheet would leave two transcripts legible at once, which is unreadable and
            // reads as a rendering fault rather than as a design.
            _journalRoot.AddComponent<Image>().color = new Color(0.06f, 0.05f, 0.09f, 1f);

            var vlg = _journalRoot.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(
                (int)JOURNAL_PADDING, (int)JOURNAL_PADDING,
                (int)JOURNAL_PADDING, (int)JOURNAL_PADDING);
            vlg.spacing = 4f;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            BuildJournalNavRow(_journalRoot.transform);
            BuildJournalSummary(_journalRoot.transform);
            BuildJournalScroll(_journalRoot.transform);

            _journalRoot.SetActive(false);
        }

        /// <summary>The day selector: older, the day itself, newer, and the way out.</summary>
        private void BuildJournalNavRow(Transform parent)
        {
            var rowGo = new GameObject("JournalNav");
            rowGo.transform.SetParent(parent, false);
            rowGo.AddComponent<RectTransform>();

            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = JOURNAL_NAV_HEIGHT;

            // Both halves are needed. preferredHeight alone leaves flexibleHeight at its
            // unset -1, so the value actually used comes from the layout group on this same
            // GameObject — which reports 1 while childForceExpandHeight is on, and the row
            // then competes with the transcript for every spare pixel. The chat input row
            // shipped exactly that bug.
            le.flexibleHeight = 0f;

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;

            var older = new Color(0.20f, 0.20f, 0.28f, 1f);

            _journalOlderButton = CreateInlineButton(rowGo.transform, "<", older, JOURNAL_ARROW_WIDTH);
            _journalOlderButton.onClick.AddListener(() => StepJournal(+1));

            // CreateInlineButton names the object after its LABEL, which for these three is
            // either punctuation or a translated word — so the hierarchy would be shaped by
            // the language the player happens to be in. Named explicitly instead, because a
            // node name is what the tests and every future probe address it by.
            _journalOlderButton.gameObject.name = "JournalOlderButton";

            var labelGo = new GameObject("DayLabel");
            labelGo.transform.SetParent(rowGo.transform, false);
            labelGo.AddComponent<RectTransform>();
            _journalDayLabel = labelGo.AddComponent<TextMeshProUGUI>();
            _journalDayLabel.fontSize = 14;
            _journalDayLabel.color = new Color(0.92f, 0.88f, 0.72f);
            _journalDayLabel.alignment = TextAlignmentOptions.Center;
            _journalDayLabel.enableWordWrapping = false;
            _journalDayLabel.overflowMode = TextOverflowModes.Ellipsis;

            _journalNewerButton = CreateInlineButton(rowGo.transform, ">", older, JOURNAL_ARROW_WIDTH);
            _journalNewerButton.onClick.AddListener(() => StepJournal(-1));
            _journalNewerButton.gameObject.name = "JournalNewerButton";

            var back = CreateInlineButton(
                rowGo.transform, ChatLanguage.JournalBack,
                new Color(0.18f, 0.38f, 0.5f, 1f), JOURNAL_BACK_WIDTH);
            back.onClick.AddListener(CloseJournal);
            back.gameObject.name = "JournalBackButton";
            _journalBackText = back.GetComponentInChildren<TextMeshProUGUI>();
        }

        /// <summary>The line that says which page of how many, and how long it is.</summary>
        private void BuildJournalSummary(Transform parent)
        {
            var go = new GameObject("JournalSummary");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = JOURNAL_SUMMARY_HEIGHT;
            le.flexibleHeight = 0f;

            _journalSummary = go.AddComponent<TextMeshProUGUI>();
            _journalSummary.fontSize = 11;
            _journalSummary.color = new Color(0.62f, 0.62f, 0.70f);
            _journalSummary.alignment = TextAlignmentOptions.Center;
            _journalSummary.enableWordWrapping = false;
            _journalSummary.overflowMode = TextOverflowModes.Ellipsis;
        }

        /// <summary>The page itself, scrollable. Same construction as the conversation's.</summary>
        private void BuildJournalScroll(Transform parent)
        {
            var scrollGo = new GameObject("JournalScroll");
            scrollGo.transform.SetParent(parent, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();

            var le = scrollGo.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.minHeight = SCROLL_MIN_H;

            scrollGo.AddComponent<Image>().color = new Color(0.03f, 0.03f, 0.05f, 0.85f);
            scrollGo.AddComponent<Mask>().showMaskGraphic = true;

            _journalScroll = scrollGo.AddComponent<ScrollRect>();
            _journalScroll.horizontal = false;
            _journalScroll.scrollSensitivity = 20f;

            // NOT "Content". The conversation's own scroll content is called that, and
            // ChatUIBuilderTests counts node names across the whole canvas to catch BuildUI
            // running twice — a second "Content" reads as exactly that failure.
            var contentGo = new GameObject("JournalContent");
            contentGo.transform.SetParent(scrollGo.transform, false);
            _journalContent = contentGo.AddComponent<RectTransform>();
            _journalContent.anchorMin = new Vector2(0f, 1f);
            _journalContent.anchorMax = new Vector2(1f, 1f);
            _journalContent.pivot = new Vector2(0.5f, 1f);
            _journalContent.sizeDelta = Vector2.zero;

            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.spacing = 2f;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            contentGo.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            _journalScroll.content = _journalContent;
            _journalScroll.viewport = scrollRt;
        }

        // ── Opening and closing ─────────────────────────────────────────────

        /// <summary>The Diario button. A toggle, because it is the only control that names the view.</summary>
        private void OnJournalClicked()
        {
            if (IsJournalOpen) CloseJournal();
            else OpenJournal();
        }

        /// <summary>
        /// Reads the archive off disk and shows the most recent day.
        ///
        /// <para>Opening on the NEWEST page rather than the oldest: the day a player wants is
        /// almost always the last one, and an archive that opens at the beginning of time
        /// makes them walk the whole thing to reach it.</para>
        /// </summary>
        private void OpenJournal()
        {
            var chatSystem = ChatSystem.Instance;
            if (chatSystem == null || !chatSystem.IsChatOpen || _journalRoot == null) return;

            _journalPages = chatSystem.ListJournalPages();
            _journalIndex = 0;

            _journalRoot.SetActive(true);

            // The field is UNDERNEATH the overlay, and uGUI focus is not blocked by an image
            // drawn over it — so without this the player types into a box they cannot see and
            // finds their sentence waiting when they come back. The text itself is left
            // alone: a half-written message is not something to throw away for a glance at
            // the diary.
            if (_inputField != null)
            {
                _inputField.DeactivateInputField();
                _inputField.interactable = false;
            }

            // ChatSystem owns Escape. See the type note.
            chatSystem.SetModalOverlay(true);

            ApplyLanguageToJournal();
            RenderJournalPage();
        }

        /// <summary>
        /// Hides the archive and hands the conversation back.
        ///
        /// <para>Safe to call when it is already closed, which is what makes it the single
        /// answer to every way out: the Volver button, the Diario toggle, Escape, and the
        /// panel closing underneath it.</para>
        /// </summary>
        private void CloseJournal()
        {
            if (_journalRoot == null) return;

            bool wasOpen = _journalRoot.activeSelf;
            _journalRoot.SetActive(false);
            ClearJournalRows();

            if (_inputField != null && !_inputField.interactable)
            {
                _inputField.interactable = true;
                if (wasOpen) _inputField.ActivateInputField();
            }

            ChatSystem.Instance?.SetModalOverlay(false);
        }

        /// <summary>
        /// Walks the archive. Positive is OLDER, because the list is newest first and the
        /// arrow the player presses is the one pointing backwards in time.
        /// </summary>
        private void StepJournal(int delta)
        {
            if (_journalPages.Count == 0) return;

            int next = Mathf.Clamp(_journalIndex + delta, 0, _journalPages.Count - 1);
            if (next == _journalIndex) return;

            _journalIndex = next;
            RenderJournalPage();
        }

        /// <summary>
        /// Midnight arrived while the archive was open: yesterday has just been sealed and
        /// today's page is a new one, so the list the view snapshotted is out of date.
        /// </summary>
        private void OnDayRolledOver(string sealedDayKey)
        {
            if (!IsJournalOpen) return;

            var chatSystem = ChatSystem.Instance;
            if (chatSystem == null) return;

            // Hold the reader's place by DAY rather than by index. The new page arrives at
            // the FRONT of a newest-first list, so every index below it shifts by one and a
            // player reading last Tuesday would silently be moved to last Wednesday.
            string reading = _journalIndex >= 0 && _journalIndex < _journalPages.Count
                ? _journalPages[_journalIndex].DayKey
                : "";

            _journalPages = chatSystem.ListJournalPages();
            _journalIndex = Mathf.Max(0, IndexOfDay(reading));
            RenderJournalPage();
        }

        private int IndexOfDay(string dayKey)
        {
            if (string.IsNullOrEmpty(dayKey)) return -1;
            for (int i = 0; i < _journalPages.Count; i++)
                if (string.Equals(_journalPages[i].DayKey, dayKey, StringComparison.Ordinal))
                    return i;
            return -1;
        }

        // ── Rendering ───────────────────────────────────────────────────────

        /// <summary>Draws whichever day <see cref="_journalIndex"/> points at.</summary>
        private void RenderJournalPage()
        {
            ClearJournalRows();

            if (_journalPages.Count == 0)
            {
                _journalDayLabel.text = "";
                _journalSummary.text = "";
                SetJournalArrows(false, false);
                AddJournalRow(ChatLanguage.JournalNoPages, new Color(0.6f, 0.6f, 0.68f), 13f);
                return;
            }

            var pageRef = _journalPages[_journalIndex];
            SetJournalArrows(
                older: _journalIndex < _journalPages.Count - 1,
                newer: _journalIndex > 0);

            ChatJournalPage page = ChatSystem.Instance?.LoadJournalPage(pageRef);

            bool isToday = string.Equals(pageRef.DayKey, ChatDayClock.TodayKey, StringComparison.Ordinal);
            _journalDayLabel.text = pageRef.Label(ChatLanguage.IsEnglish) +
                                    (isToday ? $" ({ChatLanguage.JournalToday})" : "");

            if (page == null || page.entries == null)
            {
                _journalSummary.text = "";
                AddJournalRow(ChatLanguage.JournalPageUnreadable, new Color(0.8f, 0.5f, 0.5f), 13f);
                return;
            }

            _journalSummary.text = ChatLanguage.JournalPageSummary(
                _journalIndex + 1, _journalPages.Count, page.entries.Count);

            RenderJournalEntries(page);

            // Top of the page, not the bottom: a diary is read from the start of the day.
            // The live conversation does the opposite, and deliberately — there the newest
            // line is the one being spoken.
            Canvas.ForceUpdateCanvases();
            if (_journalScroll != null) _journalScroll.verticalNormalizedPosition = 1f;
        }

        /// <summary>
        /// Builds one row per line, newest-capped. See
        /// <see cref="JOURNAL_MAX_RENDERED_ENTRIES"/> for why a very long day is trimmed IN
        /// THE VIEW and says so, rather than being trimmed on disk.
        /// </summary>
        private void RenderJournalEntries(ChatJournalPage page)
        {
            int total = page.entries.Count;
            int first = Mathf.Max(0, total - JOURNAL_MAX_RENDERED_ENTRIES);

            if (first > 0)
                AddJournalRow(
                    ChatLanguage.IsEnglish
                        ? $"… {first} earlier line(s) not shown."
                        : $"… {first} línea(s) anteriores no mostradas.",
                    new Color(0.55f, 0.55f, 0.62f), 11f);

            for (int i = first; i < total; i++)
                AddJournalEntryRow(page.entries[i]);
        }

        /// <summary>One line of the page, attributed and time-stamped.</summary>
        private void AddJournalEntryRow(ChatJournalEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.text)) return;

            string time = FormatLocalTime(entry.timestampIso8601);
            string stamp = string.IsNullOrEmpty(time)
                ? ""
                : $"<color=#6E6E7A>{time}</color>  ";

            if (entry.role == ChatJournalEntry.ROLE_SYSTEM)
            {
                AddJournalRow($"{stamp}<i><color=#8FA98F>{entry.text}</color></i>",
                              Color.white, 12f);
                return;
            }

            bool isPlayer = entry.role == ChatJournalEntry.ROLE_PLAYER;
            string speaker = isPlayer
                ? ChatLanguage.JournalYou
                : (string.IsNullOrEmpty(entry.speaker) ? "?" : entry.speaker);

            // The same two colours the live transcript uses, so a remembered line and a
            // spoken one are recognisably the same person.
            Color senderColor = isPlayer ? Color.cyan : new Color(1f, 0.8f, 0.4f);
            string hex = ColorUtility.ToHtmlStringRGB(senderColor);

            AddJournalRow($"{stamp}<color=#{hex}>{speaker}</color>: {entry.text}", Color.white, 13f);
        }

        private void AddJournalRow(string richText, Color color, float fontSize)
        {
            var row = CreateTextRow(
                _journalContent.transform, richText, fontSize, color, TextAlignmentOptions.TopLeft);
            _journalRows.Add(row);
        }

        private void ClearJournalRows()
        {
            for (int i = 0; i < _journalRows.Count; i++)
                if (_journalRows[i] != null) Destroy(_journalRows[i]);
            _journalRows.Clear();
        }

        private void SetJournalArrows(bool older, bool newer)
        {
            if (_journalOlderButton != null) _journalOlderButton.interactable = older;
            if (_journalNewerButton != null) _journalNewerButton.interactable = newer;
        }

        /// <summary>
        /// The wall-clock time a line was written, in the player's own timezone.
        ///
        /// <para>Stored UTC and shown local, which is the same split the day key already
        /// makes: timestamps are compared and sorted, so they are absolute, and a diary is
        /// read, so it is local. An unparseable stamp yields no time rather than a wrong one
        /// — a line with no clock beside it reads as a line, and "00:00" reads as midnight.
        /// </para>
        /// </summary>
        private static string FormatLocalTime(string iso8601)
        {
            if (string.IsNullOrEmpty(iso8601)) return "";

            if (!DateTime.TryParse(iso8601, CultureInfo.InvariantCulture,
                                   DateTimeStyles.RoundtripKind, out DateTime parsed))
                return "";

            return parsed.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Re-labels the captions this view owns. Called on open and from
        /// <c>ApplyLanguageToChrome</c>, so switching language with the archive up re-labels
        /// it in place instead of waiting for it to be reopened.
        ///
        /// <para>It relabels and does not redraw. The page's own text — the day label, the
        /// counter, the "not shown" notice — is language-dependent too, but rebuilding three
        /// hundred rows belongs to the caller that knows whether anything is on screen;
        /// doing it here would make opening the view render it twice.</para>
        /// </summary>
        private void ApplyLanguageToJournal()
        {
            if (_journalButtonText != null) _journalButtonText.text = ChatLanguage.Journal;
            if (_journalBackText != null) _journalBackText.text = ChatLanguage.JournalBack;
        }
    }
}
