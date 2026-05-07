using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Gameplay.Quests;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// Minimal text-only quest log. Reads the active quests off
    /// <see cref="QuestManager"/> and renders each one as
    ///   <c>"Quest Name"</c>
    ///   <c>  - Objective 1 (3/5)</c>
    ///   <c>  - Objective 2 (1/2)</c>
    /// in a Canvas overlay anchored to the top-right corner.
    ///
    /// Refreshes on the manager's OnQuestStarted / OnQuestCompleted events
    /// AND on each KillCountObjective.OnProgressChanged tick so the text
    /// is live without per-frame polling. Built procedurally — no prefab
    /// required.
    ///
    /// Real production UI would have collapsible categories, sort by
    /// completion, fade-in / fade-out animations. This is the text-only
    /// stepping stone the data layer is already wired for; designers
    /// upgrade visuals via prefab / UI toolkit later.
    /// </summary>
    public sealed class QuestLogHUD : SingletonMonoBehaviour<QuestLogHUD>
    {
        [Tooltip("Manager driving the log. Auto-resolved via FindObjectOfType when null.")]
        [SerializeField] private QuestManager manager;

        private Canvas _canvas;
        private GameObject _root;
        private Text _textLabel;

        // Per-objective subscription so we can detach on quest completion
        // without leaking event handlers.
        private readonly Dictionary<KillCountObjective, System.Action<int, int>> _kcHandlers
            = new Dictionary<KillCountObjective, System.Action<int, int>>();

        protected override bool Persist => false;

        public QuestManager Manager => manager;

        public void BindManager(QuestManager mgr)
        {
            UnbindManager();
            manager = mgr;
            if (manager == null) return;
            manager.OnQuestStarted   += OnQuestStarted;
            manager.OnQuestCompleted += OnQuestCompleted;
            // Subscribe to whatever active objectives already exist (rare but
            // possible if the HUD binds AFTER quests started — e.g. save load).
            foreach (var id in manager.ActiveIds)
                SubscribeQuestObjectives(id);
            Refresh();
        }

        protected override void OnSingletonAwake()
        {
            EnsureBuilt();
            if (manager == null) manager = FindObjectOfType<QuestManager>();
            if (manager != null) BindManager(manager);
        }

        protected override void OnDestroy()
        {
            UnbindManager();
            base.OnDestroy();
        }

        // ── Internal ───────────────────────────────────────────────────────────

        private void UnbindManager()
        {
            if (manager != null)
            {
                manager.OnQuestStarted   -= OnQuestStarted;
                manager.OnQuestCompleted -= OnQuestCompleted;
            }
            foreach (var kv in _kcHandlers)
                if (kv.Key != null) kv.Key.OnProgressChanged -= kv.Value;
            _kcHandlers.Clear();
        }

        private void OnQuestStarted(string questId)
        {
            SubscribeQuestObjectives(questId);
            Refresh();
        }

        private void OnQuestCompleted(string questId)
        {
            // Drop any per-objective subscriptions for this quest so the
            // dictionary doesn't grow forever as quests rotate. The
            // objective objects themselves stop ticking after completion.
            var toRemove = new List<KillCountObjective>();
            foreach (var kv in _kcHandlers)
            {
                if (kv.Key != null && kv.Key.Id.StartsWith(questId + "."))
                {
                    kv.Key.OnProgressChanged -= kv.Value;
                    toRemove.Add(kv.Key);
                }
            }
            foreach (var k in toRemove) _kcHandlers.Remove(k);
            Refresh();
        }

        private void SubscribeQuestObjectives(string questId)
        {
            if (manager == null) return;
            var quest = manager.GetActiveQuest(questId);
            if (quest == null) return;
            foreach (var obj in quest.Objectives)
            {
                if (obj is KillCountObjective kc && !_kcHandlers.ContainsKey(kc))
                {
                    System.Action<int, int> handler = (cur, tgt) => Refresh();
                    kc.OnProgressChanged += handler;
                    _kcHandlers[kc] = handler;
                }
            }
        }

        // Test seam — used by unit tests to drive a redraw and verify the
        // resulting text string without standing up a real Canvas.
        public string ComputeLogText()
        {
            if (manager == null) return string.Empty;

            var sb = new StringBuilder();
            foreach (var id in manager.ActiveIds)
            {
                var quest = manager.GetActiveQuest(id);
                if (quest == null) continue;
                sb.Append(string.IsNullOrEmpty(quest.DisplayName) ? quest.Id : quest.DisplayName);
                sb.Append('\n');
                foreach (var obj in quest.Objectives)
                {
                    if (obj == null) continue;
                    sb.Append("  - ");
                    sb.Append(obj.Description);
                    sb.Append(" (");
                    sb.Append(obj.Current);
                    sb.Append('/');
                    sb.Append(obj.Target);
                    sb.Append(')');
                    if (obj.IsComplete) sb.Append(" (done)");
                    sb.Append('\n');
                }
            }
            return sb.ToString();
        }

        private void Refresh()
        {
            if (_textLabel == null) return;
            _textLabel.text = ComputeLogText();
            if (_root != null)
                _root.SetActive(!string.IsNullOrEmpty(_textLabel.text));
        }

        public void EnsureBuilt()
        {
            if (_canvas != null) return;

            _root = new GameObject("QuestLogHUD_Root");
            _root.transform.SetParent(transform, false);

            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 40;
            _root.AddComponent<CanvasScaler>().uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _root.AddComponent<GraphicRaycaster>();

            // Top-right anchored panel.
            var bg = new GameObject("BG");
            bg.transform.SetParent(_root.transform, false);
            var bgRt = bg.AddComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0.72f, 0.55f);
            bgRt.anchorMax = new Vector2(0.99f, 0.92f);
            bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
            var bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0.55f);

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(bg.transform, false);
            var textRt = textGo.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(8, 8);
            textRt.offsetMax = new Vector2(-8, -8);
            _textLabel = textGo.AddComponent<Text>();
            _textLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _textLabel.alignment = TextAnchor.UpperLeft;
            _textLabel.color = new Color(0.95f, 0.95f, 0.85f);
            _textLabel.fontSize = 14;
            _textLabel.text = "";

            _root.SetActive(false);
        }
    }
}
