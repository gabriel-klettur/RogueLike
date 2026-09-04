using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// Minimal vertical-list skill tree UI. Each node in the bound
    /// <see cref="SkillTree"/> renders as one row:
    ///   [icon] Display Name (cost)  — status — [Learn] button
    /// Status reflects gating logic from <see cref="LearnedSkills.CanLearn"/>:
    /// "Learned", "Locked: Requires X", "Cost: 2 / Have 1", "Available".
    /// Clicking Learn invokes <see cref="LearnedSkills.TryLearn"/>; the
    /// row repaints from the OnSkillLearned event.
    ///
    /// Genuine production UI would lay out the graph spatially (parent →
    /// child arrows, branch tabs, search). This list view is the data
    /// layer's testable face — designers replace it with a graph layout
    /// later via prefab.
    /// </summary>
    public sealed class SkillTreeHUD : SingletonMonoBehaviour<SkillTreeHUD>
    {
        [Tooltip("Player's LearnedSkills component. Auto-resolved via " +
                 "EntityRegistry.PlayerTransform when null.")]
        [SerializeField] private LearnedSkills skills;

        [Tooltip("Current player level used for level-gate checks. " +
                 "Re-fetched from Experience.Level when available.")]
        [SerializeField] private int playerLevel = 1;

        private Canvas _canvas;
        private GameObject _root;
        private GameObject _listContainer;
        private Text _headerLabel;

        // Open/closed state — toggled by F4 in normal play (designer
        // wires the input binding) but exposed publicly for tests.
        public bool IsOpen { get; private set; }

        protected override bool Persist => false;

        public void BindLearnedSkills(LearnedSkills ls, int level)
        {
            UnbindCurrent();
            skills = ls;
            playerLevel = level;
            if (skills != null)
                skills.OnLoadoutChanged += OnLoadoutChanged;
            if (IsOpen) Refresh();
        }

        public void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }

        public void Open()
        {
            EnsureBuilt();
            if (skills == null) AutoResolveSkills();
            IsOpen = true;
            if (_root != null) _root.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
            if (_root != null) _root.SetActive(false);
        }

        protected override void OnSingletonAwake()
        {
            EnsureBuilt();
        }

        protected override void OnDestroy()
        {
            UnbindCurrent();
            base.OnDestroy();
        }

        // ── Internal ───────────────────────────────────────────────────────────

        private void UnbindCurrent()
        {
            if (skills != null) skills.OnLoadoutChanged -= OnLoadoutChanged;
        }

        private void AutoResolveSkills()
        {
            var player = EntityRegistry.PlayerTransform;
            if (player == null) return;
            BindLearnedSkills(player.GetComponent<LearnedSkills>(),
                              player.GetComponent<Experience>()?.Level ?? 1);
        }

        private void OnLoadoutChanged() => Refresh();

        // Test seam — produces the textual representation of the tree
        // without rendering. Each node becomes one line.
        public string ComputeListText()
        {
            if (skills == null || skills.Tree == null) return string.Empty;
            var sb = new System.Text.StringBuilder();
            foreach (var node in skills.Tree.Nodes)
            {
                if (node == null) continue;
                sb.Append(node.displayName);
                sb.Append(" (cost ");
                sb.Append(node.pointCost);
                sb.Append("): ");
                sb.Append(StatusFor(node));
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private string StatusFor(SkillNode node)
        {
            if (node == null) return "?";

            int rank = skills.RankOf(node.skillId);
            int maxRank = Mathf.Max(1, node.maxRank);

            // The rank is stated on every MULTI-rank row, learned or not: a five-rank node
            // that says only "Learned" hides the single most important thing about it, which
            // is how much of it the player actually owns. A single-rank node has no such
            // information to carry, so it keeps the plain word.
            string held = maxRank > 1 ? $"[{rank}/{maxRank}] " : string.Empty;

            if (rank >= maxRank) return maxRank > 1 ? held + "Maxed" : "Learned";
            if (skills.CanLearn(node, playerLevel, out string reason)) return held + "Available";
            return held + "Locked: " + reason;
        }

        private void Refresh()
        {
            if (_listContainer == null || skills == null || skills.Tree == null) return;

            // Clear previous rows.
            for (int i = _listContainer.transform.childCount - 1; i >= 0; i--)
                Object.Destroy(_listContainer.transform.GetChild(i).gameObject);

            // The points balance belongs in the header, not in a row: it is the one number
            // that governs every row at once, and a player deciding where to spend reads it
            // before anything else.
            if (_headerLabel != null)
            {
                string treeName = string.IsNullOrWhiteSpace(skills.Tree.displayName)
                    ? "Skill Tree" : skills.Tree.displayName;
                _headerLabel.text = $"{treeName}   —   {skills.AvailablePoints} point(s) available";
            }

            // Repopulate, ordered by the authored layout so the panel reads as the tree the
            // designer drew rather than as whatever order the array happens to be in.
            var ordered = new List<SkillNode>(skills.Tree.Nodes);
            ordered.Sort((a, b) =>
            {
                if (a == null || b == null) return 0;
                int byRow = a.row.CompareTo(b.row);
                return byRow != 0 ? byRow : a.column.CompareTo(b.column);
            });

            foreach (var node in ordered)
            {
                if (node == null) continue;
                BuildRow(node);
            }
        }

        private void BuildRow(SkillNode node)
        {
            var row = new GameObject("Row_" + node.skillId);
            row.transform.SetParent(_listContainer.transform, false);
            var rt = row.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 32);

            var rowImg = row.AddComponent<Image>();
            rowImg.color = new Color(0.10f, 0.10f, 0.12f, 0.85f);

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(row.transform, false);
            var labelRt = labelGo.AddComponent<RectTransform>();
            labelRt.anchorMin = new Vector2(0f, 0f);
            labelRt.anchorMax = new Vector2(0.7f, 1f);
            labelRt.offsetMin = new Vector2(8, 4);
            labelRt.offsetMax = new Vector2(-4, -4);
            var label = labelGo.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.alignment = TextAnchor.MiddleLeft;
            label.color = Color.white;
            label.fontSize = 14;
            // The effect line is GENERATED from the node's modifiers rather than read from
            // its description, so a retune can never leave the panel lying about what a
            // talent does. See SkillNode.DescribeRank.
            string effect = node.DescribeRank(Mathf.Max(1, skills.RankOf(node.skillId) + 1));
            label.text = string.IsNullOrEmpty(effect)
                ? $"{node.displayName} (cost {node.pointCost}) — {StatusFor(node)}"
                : $"{node.displayName} (cost {node.pointCost}) — {StatusFor(node)}  ·  {effect}";

            // Learn button only when CanLearn — otherwise display-only.
            if (skills.CanLearn(node, playerLevel, out _))
            {
                var btnGo = new GameObject("LearnBtn");
                btnGo.transform.SetParent(row.transform, false);
                var btnRt = btnGo.AddComponent<RectTransform>();
                btnRt.anchorMin = new Vector2(0.72f, 0.1f);
                btnRt.anchorMax = new Vector2(0.98f, 0.9f);
                btnRt.offsetMin = btnRt.offsetMax = Vector2.zero;
                var btnImg = btnGo.AddComponent<Image>();
                btnImg.color = new Color(0.30f, 0.55f, 1f, 0.9f);
                var btn = btnGo.AddComponent<Button>();

                var btnLabelGo = new GameObject("BtnLabel");
                btnLabelGo.transform.SetParent(btnGo.transform, false);
                var btnLabelRt = btnLabelGo.AddComponent<RectTransform>();
                btnLabelRt.anchorMin = Vector2.zero;
                btnLabelRt.anchorMax = Vector2.one;
                btnLabelRt.offsetMin = btnLabelRt.offsetMax = Vector2.zero;
                var btnLabel = btnLabelGo.AddComponent<Text>();
                btnLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                btnLabel.alignment = TextAnchor.MiddleCenter;
                btnLabel.color = Color.white;
                btnLabel.fontSize = 14;
                btnLabel.text = "Learn";

                // Capture node reference in the closure correctly.
                var capturedNode = node;
                btn.onClick.AddListener(() => skills.TryLearn(capturedNode, playerLevel, out _));
            }
        }

        public void EnsureBuilt()
        {
            if (_canvas != null) return;

            _root = new GameObject("SkillTreeHUD_Root");
            _root.transform.SetParent(transform, false);

            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 60;
            _root.AddComponent<CanvasScaler>().uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _root.AddComponent<GraphicRaycaster>();

            // Centered modal panel.
            var panel = new GameObject("Panel");
            panel.transform.SetParent(_root.transform, false);
            var panelRt = panel.AddComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.2f, 0.15f);
            panelRt.anchorMax = new Vector2(0.8f, 0.85f);
            panelRt.offsetMin = panelRt.offsetMax = Vector2.zero;
            var panelImg = panel.AddComponent<Image>();
            panelImg.color = new Color(0f, 0f, 0f, 0.85f);

            // Header.
            var header = new GameObject("Header");
            header.transform.SetParent(panel.transform, false);
            var hdrRt = header.AddComponent<RectTransform>();
            hdrRt.anchorMin = new Vector2(0, 0.92f);
            hdrRt.anchorMax = new Vector2(1, 1);
            hdrRt.offsetMin = hdrRt.offsetMax = Vector2.zero;
            var hdrLabel = header.AddComponent<Text>();
            hdrLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            hdrLabel.alignment = TextAnchor.MiddleCenter;
            hdrLabel.color = Color.white;
            hdrLabel.fontSize = 18;
            hdrLabel.fontStyle = FontStyle.Bold;
            hdrLabel.text = "Skill Tree";
            _headerLabel = hdrLabel;

            // List container with VerticalLayoutGroup.
            _listContainer = new GameObject("List");
            _listContainer.transform.SetParent(panel.transform, false);
            var listRt = _listContainer.AddComponent<RectTransform>();
            listRt.anchorMin = new Vector2(0.05f, 0.05f);
            listRt.anchorMax = new Vector2(0.95f, 0.9f);
            listRt.offsetMin = listRt.offsetMax = Vector2.zero;
            var vlg = _listContainer.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = false;
            vlg.childControlWidth = true;

            _root.SetActive(false);
        }
    }
}
