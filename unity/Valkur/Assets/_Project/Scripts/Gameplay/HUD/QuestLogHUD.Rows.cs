using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Chat;
using Valkur.Gameplay.Quests;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// One row per active quest, and the button that drops it.
    ///
    /// <para><b>Why the tracker stopped being one label.</b> A single TMP blob can say what the
    /// player is carrying and can never offer to do anything about it — so the only way to be
    /// rid of an errand accepted by mistake was to walk back to whoever gave it, or to open the
    /// DevConsole. Rows cost a rebuild per change, which is bounded by how many quests a person
    /// carries and happens on quest events rather than per frame.</para>
    ///
    /// <para><b>Dropping takes two clicks and arming is EXCLUSIVE</b>, the same rule the
    /// conversation panel's own Abandonar follows: this is the one control on the HUD that
    /// destroys progress the player earned, and a single misclick in a corner they are not
    /// looking at would throw away an errand they walked across the map for. It disarms when
    /// the row goes away, when the window closes, and when a different row is armed.</para>
    /// </summary>
    public sealed partial class QuestLogHUD
    {
        /// <summary>Width of the drop button on a row.</summary>
        private const float DROP_W = 52f;

        /// <summary>Vertical slack a row keeps around its text.</summary>
        private const float ROW_TEXT_PADDING = 8f;

        /// <summary>Shortest a row may be — one name and one objective.</summary>
        private const float ROW_MIN_H = 34f;

        /// <summary>
        /// Text width assumed when the real one cannot be measured — Edit Mode, where uGUI lays
        /// nothing out and the content rect reports its untouched default.
        /// </summary>
        private const float ROW_FALLBACK_TEXT_W = 220f;

        /// <summary>The quest whose drop button is armed, if any.</summary>
        private string _armedDropId;

        private readonly List<GameObject> _rows = new List<GameObject>();

        /// <summary>The quest ids currently drawn as rows. Read by tests.</summary>
        public IReadOnlyList<GameObject> Rows => _rows;

        private void ClearRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] == null) continue;
                // Destroy is an outright ERROR in Edit Mode, and this panel is reachable from
                // EditMode fixtures — the trap seven ControlsEditorTests went red on.
                if (Application.isPlaying) Destroy(_rows[i]);
                else DestroyImmediate(_rows[i]);
            }
            _rows.Clear();
        }

        /// <summary>
        /// Redraws the log. Called from every event that can change what is active, and from
        /// the window verbs.
        /// </summary>
        private void Refresh()
        {
            if (_rowsContent == null) return;

            ClearRows();

            bool any = false;
            if (manager != null)
            {
                foreach (var id in manager.ActiveIds)
                {
                    var quest = manager.GetActiveQuest(id);
                    if (quest == null) continue;
                    AddQuestRow(id, quest);
                    any = true;
                }
            }

            // Hidden with nothing to track, exactly as the old label was: a titled window
            // holding "no quests" is a dark rectangle whose only content is the news that it
            // has none. The window verbs are still remembered while it is away.
            if (_root != null) _root.SetActive(any && !_closed);

            ApplyWindowState();
        }

        private void AddQuestRow(string questId, Quest quest)
        {
            var row = new GameObject("QuestRow_" + questId);
            row.transform.SetParent(_rowsContent, false);
            row.AddComponent<RectTransform>();

            var rowImg = row.AddComponent<Image>();
            rowImg.color = new Color(0.09f, 0.11f, 0.13f, 0.75f);

            var le = row.AddComponent<LayoutElement>();
            // preferredHeight alone leaves flexibleHeight at its unset -1, so the value used
            // comes from the layout group, which reports 1 while childForceExpandHeight is on
            // — and every row then fights the others for the panel's spare pixels.
            le.flexibleHeight = 0f;

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(5, 5, 3, 3);
            hlg.spacing = 4f;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;

            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(row.transform, false);
            bodyGo.AddComponent<RectTransform>();
            var body = bodyGo.AddComponent<TextMeshProUGUI>();
            body.fontSize = 12;
            body.color = new Color(0.92f, 0.92f, 0.84f);
            body.alignment = TextAlignmentOptions.TopLeft;
            body.enableWordWrapping = true;
            body.raycastTarget = false;
            body.text = BuildRowText(quest);

            AddDropButton(row.transform, questId);

            // Sized from the text it actually holds. The width is COMPUTED from the window's
            // own geometry rather than read back off a rect that has not been laid out yet,
            // and the fallback catches the Edit Mode case where the measurement is 0.
            float textWidth = _windowSize.x - DROP_W - 24f;
            if (textWidth < 80f) textWidth = ROW_FALLBACK_TEXT_W;
            float textHeight = MeasureHeight(body, textWidth);
            le.preferredHeight = textHeight > 0f
                ? Mathf.Max(ROW_MIN_H, textHeight + ROW_TEXT_PADDING)
                : ROW_MIN_H;

            _rows.Add(row);
        }

        /// <summary>
        /// How tall this text wants to be, or 0 when nothing can measure it.
        ///
        /// <para><b>A bare <c>GetPreferredValues</c> THROWS here, and it did.</b> A
        /// <c>TextMeshProUGUI</c> picks up <c>TMP_Settings.defaultFontAsset</c> in its
        /// <c>Awake</c> — which Unity never calls on a component added in Edit Mode — so in a
        /// fixture the label has no font, and TMP dereferences it while sizing the material
        /// array. Four <c>QuestLogHUDTests</c> went red on exactly that
        /// NullReferenceException, thrown from inside TMP with the panel's own code nowhere in
        /// the message.</para>
        ///
        /// <para>Assigning the default first is the repair rather than a dodge: in Play Mode it
        /// is already what Awake did, and in a fixture it makes the measurement REAL instead of
        /// skipped. The zero is for the case nothing can rescue — a project with no default
        /// font asset — where the caller falls back to the row floor rather than to a height
        /// measured off nothing.</para>
        /// </summary>
        private static float MeasureHeight(TextMeshProUGUI label, float width)
        {
            if (label == null) return 0f;
            if (label.font == null) label.font = TMP_Settings.defaultFontAsset;
            if (label.font == null) return 0f;
            return label.GetPreferredValues(label.text, width, 0f).y;
        }

        private static string BuildRowText(Quest quest)
        {
            var sb = new StringBuilder();
            sb.Append("<b>")
              .Append(string.IsNullOrEmpty(quest.DisplayName) ? quest.Id : quest.DisplayName)
              .Append("</b>");

            foreach (var obj in quest.Objectives)
            {
                if (obj == null) continue;
                sb.AppendLine();
                // The tick is a COLOUR and a glyph, not a colour alone: the rest of the HUD
                // makes the same promise, and a row that separates done from pending by hue
                // is one a colour-blind player reads as a single block.
                sb.Append(obj.IsComplete ? "  <color=#8FBF6A>[x] " : "  [ ] ");
                sb.Append("<size=11>").Append(obj.Description).Append("</size>");
                sb.Append("  ").Append(obj.Current).Append('/').Append(obj.Target);
                if (obj.IsComplete) sb.Append("</color>");
            }
            return sb.ToString();
        }

        private void AddDropButton(Transform parent, string questId)
        {
            bool armed = string.Equals(_armedDropId, questId, StringComparison.OrdinalIgnoreCase);

            var go = new GameObject("QuestDropButton");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = DROP_W;
            le.flexibleWidth = 0f;

            var img = go.AddComponent<Image>();
            img.color = armed ? new Color(0.52f, 0.20f, 0.16f, 1f)
                              : new Color(0.24f, 0.19f, 0.19f, 1f);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            string id = questId;
            btn.onClick.AddListener(() => DropQuest(id));

            var textGo = new GameObject("Label");
            textGo.transform.SetParent(go.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            var label = textGo.AddComponent<TextMeshProUGUI>();
            label.text = armed ? ChatLanguage.QuestAbandonConfirm : ChatLanguage.QuestAbandon;
            label.fontSize = 10;
            label.color = new Color(0.92f, 0.88f, 0.86f);
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
        }

        /// <summary>
        /// Drops a quest, on the SECOND click.
        ///
        /// <para>Routed through <see cref="QuestService"/> rather than straight at the manager,
        /// so the tracker and the conversation panel drop a quest by the same path — and it
        /// falls back to the manager when no service is up, which is what an EditMode fixture
        /// and a scene built before the service existed both look like.</para>
        /// </summary>
        internal void DropQuest(string questId)
        {
            if (string.IsNullOrEmpty(questId)) return;

            if (!string.Equals(_armedDropId, questId, StringComparison.OrdinalIgnoreCase))
            {
                _armedDropId = questId;
                Refresh();
                return;
            }

            _armedDropId = null;

            var service = QuestService.Instance;
            if (service != null) service.Abandon(questId);
            else if (manager != null) manager.AbandonQuest(questId);

            // No Refresh() here on purpose: AbandonQuest raises OnQuestAbandoned and this
            // panel is subscribed, so redrawing would draw the same list twice — and if the
            // drop were ever refused, the row must stay rather than vanish on optimism.
        }
    }
}
