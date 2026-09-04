using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;

namespace Valkur.Gameplay.HUD
{
    /// <summary>
    /// The character sheet's GRIMOIRE tab: the schools of magic, what each teaches, and
    /// what the character may buy next with arcane points.
    ///
    /// It is a separate panel from <see cref="SkillTreeHUD"/> for the same reason the data
    /// is separate: a talent is a number and a spell is a verb, they cost different
    /// currencies, and putting them in one list makes the player compare "+5 % melee
    /// damage" against "unlock Meteor Shower" as if those were the same kind of choice.
    ///
    /// A school the character has no affinity for is shown, not hidden — it just costs
    /// more, and the row says so. Hiding it would turn a class into a wall; charging for it
    /// turns a class into a tendency, which is the design <see cref="SpellTree"/> records.
    /// </summary>
    public sealed class SpellTreeHUD : SingletonMonoBehaviour<SpellTreeHUD>
    {
        [SerializeField] private KnownSpells grimoire;
        [SerializeField] private int playerLevel = 1;

        private Canvas _canvas;
        private GameObject _root;
        private GameObject _listContainer;
        private GameObject _tabStrip;
        private Text _headerLabel;
        private int _activeSchool;

        public bool IsOpen { get; private set; }
        public int ActiveSchool => _activeSchool;

        protected override bool Persist => false;

        public void Bind(KnownSpells value, int level)
        {
            Unbind();
            grimoire = value;
            playerLevel = level;
            if (grimoire != null) grimoire.OnLoadoutChanged += Refresh;
            if (IsOpen) Refresh();
        }

        public void Open()
        {
            EnsureBuilt();
            if (grimoire == null) AutoResolve();
            IsOpen = true;
            if (_root != null) _root.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
            if (_root != null) _root.SetActive(false);
        }

        public void Toggle() { if (IsOpen) Close(); else Open(); }

        public void SelectSchool(int index)
        {
            _activeSchool = Mathf.Max(0, index);
            Refresh();
        }

        protected override void OnSingletonAwake() => EnsureBuilt();

        protected override void OnDestroy()
        {
            Unbind();
            base.OnDestroy();
        }

        private void Unbind()
        {
            if (grimoire != null) grimoire.OnLoadoutChanged -= Refresh;
        }

        private void AutoResolve()
        {
            var player = EntityRegistry.PlayerTransform;
            if (player == null) return;
            Bind(player.GetComponent<KnownSpells>(),
                 player.GetComponent<Experience>()?.Level ?? 1);
        }

        private SpellTree ActiveTree()
        {
            if (grimoire == null || grimoire.Trees.Count == 0) return null;
            int index = Mathf.Clamp(_activeSchool, 0, grimoire.Trees.Count - 1);
            return grimoire.Trees[index];
        }

        /// <summary>Test seam — the active school as text, one line per node.</summary>
        public string ComputeListText()
        {
            var tree = ActiveTree();
            if (tree == null) return string.Empty;

            var sb = new System.Text.StringBuilder();
            foreach (var node in tree.Nodes)
            {
                if (node == null) continue;
                sb.Append(node.ResolveDisplayName());
                sb.Append(" (");
                sb.Append(grimoire.ResolveCost(tree, node));
                sb.Append("): ");
                sb.Append(StatusFor(tree, node));
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private string StatusFor(SpellTree tree, SpellNode node)
        {
            if (node == null) return "?";
            if (grimoire.IsNodeLearned(node)) return "Known";
            if (grimoire.CanLearn(tree, node, playerLevel, out string reason)) return "Available";
            return "Locked: " + reason;
        }

        private void Refresh()
        {
            if (_listContainer == null || grimoire == null) return;

            RebuildTabs();

            for (int i = _listContainer.transform.childCount - 1; i >= 0; i--)
                Object.Destroy(_listContainer.transform.GetChild(i).gameObject);

            var tree = ActiveTree();
            if (tree == null)
            {
                if (_headerLabel != null)
                    _headerLabel.text = "Grimoire   —   no schools loaded";
                return;
            }

            if (_headerLabel != null)
            {
                string affinity = tree.HasAffinity(grimoire.ClassKey)
                    ? "affinity"
                    : $"off-affinity ×{tree.offAffinityCostMultiplier:0.#}";
                _headerLabel.text = $"{tree.displayName}  ({affinity})   —   " +
                                    $"{grimoire.AvailablePoints} arcane point(s)";
            }

            foreach (var node in tree.Nodes)
            {
                if (node == null) continue;
                BuildRow(tree, node);
            }
        }

        private void RebuildTabs()
        {
            if (_tabStrip == null) return;

            for (int i = _tabStrip.transform.childCount - 1; i >= 0; i--)
                Object.Destroy(_tabStrip.transform.GetChild(i).gameObject);

            var trees = grimoire.Trees;
            for (int i = 0; i < trees.Count; i++)
            {
                var tree = trees[i];
                if (tree == null) continue;

                var tabGo = new GameObject("Tab_" + tree.schoolKey);
                tabGo.transform.SetParent(_tabStrip.transform, false);
                var img = tabGo.AddComponent<Image>();
                // The school's own accent, dimmed when it is not the open one. Colour is
                // what makes eight tabs scannable at a glance; the label alone is not.
                img.color = i == _activeSchool
                    ? new Color(tree.accent.r, tree.accent.g, tree.accent.b, 0.65f)
                    : new Color(tree.accent.r * 0.35f, tree.accent.g * 0.35f, tree.accent.b * 0.35f, 0.5f);

                var btn = tabGo.AddComponent<Button>();
                int captured = i;
                btn.onClick.AddListener(() => SelectSchool(captured));

                var labelGo = new GameObject("Label");
                labelGo.transform.SetParent(tabGo.transform, false);
                var labelRt = labelGo.AddComponent<RectTransform>();
                labelRt.anchorMin = Vector2.zero;
                labelRt.anchorMax = Vector2.one;
                labelRt.offsetMin = labelRt.offsetMax = Vector2.zero;
                var label = labelGo.AddComponent<Text>();
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                label.alignment = TextAnchor.MiddleCenter;
                label.color = Color.white;
                label.fontSize = 12;
                label.text = tree.displayName;
            }
        }

        private void BuildRow(SpellTree tree, SpellNode node)
        {
            var row = new GameObject("Row_" + node.nodeId);
            row.transform.SetParent(_listContainer.transform, false);
            var rt = row.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 30);

            var rowImg = row.AddComponent<Image>();
            rowImg.color = grimoire.IsNodeLearned(node)
                ? new Color(0.12f, 0.18f, 0.14f, 0.85f)
                : new Color(0.10f, 0.10f, 0.12f, 0.85f);

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(row.transform, false);
            var labelRt = labelGo.AddComponent<RectTransform>();
            labelRt.anchorMin = new Vector2(0f, 0f);
            labelRt.anchorMax = new Vector2(0.74f, 1f);
            labelRt.offsetMin = new Vector2(8, 3);
            labelRt.offsetMax = new Vector2(-4, -3);
            var label = labelGo.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.alignment = TextAnchor.MiddleLeft;
            label.color = Color.white;
            label.fontSize = 13;

            string effects = node.DescribeEffects();
            label.text = $"{node.ResolveDisplayName()} ({grimoire.ResolveCost(tree, node)} AP) — " +
                         $"{StatusFor(tree, node)}" +
                         (string.IsNullOrEmpty(effects) ? string.Empty : $"  ·  {effects}");

            if (!grimoire.CanLearn(tree, node, playerLevel, out _)) return;

            var btnGo = new GameObject("LearnBtn");
            btnGo.transform.SetParent(row.transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0.76f, 0.12f);
            btnRt.anchorMax = new Vector2(0.98f, 0.88f);
            btnRt.offsetMin = btnRt.offsetMax = Vector2.zero;
            var btnImg = btnGo.AddComponent<Image>();
            btnImg.color = new Color(tree.accent.r, tree.accent.g, tree.accent.b, 0.85f);
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
            btnLabel.color = Color.black;
            btnLabel.fontSize = 13;
            btnLabel.text = "Learn";

            var capturedTree = tree;
            var capturedNode = node;
            btn.onClick.AddListener(() => grimoire.TryLearn(capturedTree, capturedNode, playerLevel, out _));
        }

        public void EnsureBuilt()
        {
            if (_canvas != null) return;

            _root = new GameObject("SpellTreeHUD_Root");
            _root.transform.SetParent(transform, false);

            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 60;
            _root.AddComponent<CanvasScaler>().uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _root.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Panel");
            panel.transform.SetParent(_root.transform, false);
            var panelRt = panel.AddComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.2f, 0.15f);
            panelRt.anchorMax = new Vector2(0.8f, 0.85f);
            panelRt.offsetMin = panelRt.offsetMax = Vector2.zero;
            var panelImg = panel.AddComponent<Image>();
            panelImg.color = new Color(0f, 0f, 0f, 0.85f);

            var header = new GameObject("Header");
            header.transform.SetParent(panel.transform, false);
            var hdrRt = header.AddComponent<RectTransform>();
            hdrRt.anchorMin = new Vector2(0, 0.93f);
            hdrRt.anchorMax = new Vector2(1, 1);
            hdrRt.offsetMin = hdrRt.offsetMax = Vector2.zero;
            _headerLabel = header.AddComponent<Text>();
            _headerLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _headerLabel.alignment = TextAnchor.MiddleCenter;
            _headerLabel.color = Color.white;
            _headerLabel.fontSize = 17;
            _headerLabel.fontStyle = FontStyle.Bold;
            _headerLabel.text = "Grimoire";

            _tabStrip = new GameObject("Tabs");
            _tabStrip.transform.SetParent(panel.transform, false);
            var tabsRt = _tabStrip.AddComponent<RectTransform>();
            tabsRt.anchorMin = new Vector2(0.03f, 0.855f);
            tabsRt.anchorMax = new Vector2(0.97f, 0.925f);
            tabsRt.offsetMin = tabsRt.offsetMax = Vector2.zero;
            var hlg = _tabStrip.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 3;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;

            _listContainer = new GameObject("List");
            _listContainer.transform.SetParent(panel.transform, false);
            var listRt = _listContainer.AddComponent<RectTransform>();
            listRt.anchorMin = new Vector2(0.03f, 0.03f);
            listRt.anchorMax = new Vector2(0.97f, 0.845f);
            listRt.offsetMin = listRt.offsetMax = Vector2.zero;
            var vlg = _listContainer.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 3;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childControlHeight = false;
            vlg.childControlWidth = true;

            _root.SetActive(false);
        }
    }
}
