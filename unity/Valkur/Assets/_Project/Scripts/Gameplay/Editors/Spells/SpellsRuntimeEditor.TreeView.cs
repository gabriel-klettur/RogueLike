using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Spells Editor — painting the Tree view. The model lives in
    /// <c>SpellsRuntimeEditor.Tree.cs</c>; this turns it into rows.
    /// </summary>
    public partial class SpellsRuntimeEditor
    {
        /// <summary>
        /// Rebuild the outline. Cheap enough to call on every filter keystroke: the shipped
        /// catalogue produces at most ~110 rows, the same order as the table it sits beside.
        /// </summary>
        private void RefreshTree()
        {
            if (_spellsTreeContent == null) return;

            // DETACH, then destroy. In Play Mode SafeDestroy defers to end of frame, so a
            // row that is merely destroyed is still a child of the VerticalLayoutGroup for
            // the rest of this frame and the rebuilt outline lands UNDER the old one. Costs
            // nothing here and makes a refresh exact whether or not the game is running,
            // which matters because a row click refreshes the list it is standing in.
            for (int i = 0; i < _spellTreeRows.Count; i++)
            {
                var go = _spellTreeRows[i];
                if (go == null) continue;
                go.transform.SetParent(null, false);
                Valkur.Core.SafeDestroy.Of(go);
            }
            _spellTreeRows.Clear();

            RebuildTreeModel();

            for (int i = 0; i < _treeRows.Count; i++)
            {
                var row = _treeRows[i];
                _spellTreeRows.Add(row.IsHeader
                    ? BuildTreeHeaderRow(row)
                    : BuildTreeSpellRow(row, i));
            }
        }

        private GameObject BuildTreeHeaderRow(TreeRow row)
        {
            var go = UIFactory.CreateUI("TreeHeader_" + row.SectionKey, _spellsTreeContent);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = TREE_HEADER_H;
            le.flexibleWidth = 1f;

            var bg = go.AddComponent<Image>();
            bg.color = row.IsOrphanSection ? TREE_ORPHAN_BG : TREE_HEADER_BG;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            string captured = row.SectionKey;
            btn.onClick.AddListener(() => ToggleTreeSection(captured));

            bool foldable = _treeSchoolFilter == TREE_SCHOOL_ALL;
            bool collapsed = SectionCollapsed(row.SectionKey);
            string chevron = !foldable ? "" : collapsed ? "▶  " : "▼  ";
            AddStretchedLabel(go.transform, chevron + row.Label, left: 6f, size: 11.5f,
                style: FontStyles.Bold,
                color: row.IsOrphanSection ? UITheme.ACCENT : UITheme.TEXT_PRIMARY);

            AddTrailingLabel(go.transform, row.Trailing, size: 10f, color: UITheme.TEXT_MUTED);

            return go;
        }

        private GameObject BuildTreeSpellRow(TreeRow row, int index)
        {
            var go = UIFactory.CreateUI("TreeRow_" + (row.SpellKey ?? index.ToString()),
                _spellsTreeContent);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = TREE_ROW_H;
            le.flexibleWidth = 1f;

            var bg = go.AddComponent<Image>();
            bool selected = !string.IsNullOrEmpty(row.SpellKey) && row.SpellKey == _selectedKey;
            bg.color = selected ? ROW_SELECTED : index % 2 == 0 ? ROW_ZEBRA_A : ROW_ZEBRA_B;

            if (!string.IsNullOrEmpty(row.SpellKey))
            {
                var btn = go.AddComponent<Button>();
                btn.targetGraphic = bg;
                var colors = btn.colors;
                colors.normalColor = bg.color;
                colors.highlightedColor = bg.color + new Color(0.05f, 0.05f, 0.08f, 0f);
                colors.pressedColor = ROW_SELECTED;
                colors.selectedColor = ROW_SELECTED;
                colors.fadeDuration = 0.08f;
                btn.colors = colors;

                string captured = row.SpellKey;
                btn.onClick.AddListener(() => SelectSpell(captured));
            }

            float indent = 6f + row.Depth * TREE_INDENT_PX;

            // One guide per indentation step, so a five-deep chain is countable at a glance
            // rather than something the eye has to measure against the panel edge.
            for (int d = 0; d < row.Depth; d++)
            {
                var guide = UIFactory.CreateUI("Guide" + d, go.transform);
                var grt = guide.GetComponent<RectTransform>();
                grt.anchorMin = new Vector2(0f, 0f);
                grt.anchorMax = new Vector2(0f, 1f);
                grt.pivot = new Vector2(0f, 0.5f);
                grt.anchoredPosition = new Vector2(6f + d * TREE_INDENT_PX + 3f, 0f);
                grt.sizeDelta = new Vector2(1f, -4f);
                guide.AddComponent<Image>().color = TREE_GUIDE;
            }

            AddStretchedLabel(go.transform, row.Label, indent, size: 10.5f,
                style: FontStyles.Normal, color: UITheme.TEXT_PRIMARY);

            AddTrailingLabel(go.transform, row.Trailing, size: 9.5f, color: UITheme.TEXT_MUTED);

            return go;
        }

        /// <summary>Width the cost/level column reserves on the right of every row.</summary>
        private const float TREE_TRAILING_W = 74f;

        /// <summary>
        /// The name, filling everything between its indent and the trailing column.
        ///
        /// <para>ANCHORED, never sized in absolute pixels. The Spells panel carries a resize
        /// handle, so a label pinned at a fixed width stops meeting the cost column the moment
        /// the author drags the panel wider — and the first version of this view did exactly
        /// that, with the cost column parked at x=220 in a row the layout had left 88 px
        /// wide.</para>
        /// </summary>
        private static void AddStretchedLabel(Transform parent, string text, float left,
            float size, FontStyles style, Color color)
        {
            var go = UIFactory.CreateUI("Label", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.offsetMin = new Vector2(left, 0f);
            rt.offsetMax = new Vector2(-TREE_TRAILING_W, 0f);
            ApplyTreeText(go, text, size, style, color, TextAlignmentOptions.MidlineLeft);
        }

        /// <summary>The cost and level, pinned to the right edge whatever the row is worth.</summary>
        private static void AddTrailingLabel(Transform parent, string text, float size, Color color)
        {
            var go = UIFactory.CreateUI("Trailing", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-6f, 0f);
            rt.sizeDelta = new Vector2(TREE_TRAILING_W - 8f, 0f);
            ApplyTreeText(go, text, size, FontStyles.Normal, color,
                TextAlignmentOptions.MidlineRight);
        }

        private static void ApplyTreeText(GameObject go, string text, float size,
            FontStyles style, Color color, TextAlignmentOptions alignment)
        {
            // Image + TMP on the same GameObject is a NullReferenceException in this project.
            // These labels carry no Image; the parent row owns the background.
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text ?? string.Empty;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
        }
    }
}
