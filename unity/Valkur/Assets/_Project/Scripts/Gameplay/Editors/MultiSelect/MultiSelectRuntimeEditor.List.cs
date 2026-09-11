using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.MultiSelect
{
    /// <summary>
    /// The list of what is selected, one row per member, and the row click that isolates one.
    ///
    /// <para>IT IS THE OTHER HALF OF NAVIGATING A STACK. Clicking the same spot cycles down
    /// through whatever is under the cursor, which is fast and blind — the author sees one
    /// thing at a time and has to remember what went past. Boxing the cluster and then picking
    /// from a written list is the same job done slowly and with everything visible. Neither is
    /// redundant: the cycle wins when the stack is two deep, the list wins when it is nine.</para>
    ///
    /// <para>A row click ISOLATES rather than toggles — it leaves exactly that one selected —
    /// because the list exists to answer "I want that one, on its own, to move it". Toggling
    /// is what Ctrl+click on the world already does.</para>
    /// </summary>
    public sealed partial class MultiSelectRuntimeEditor
    {
        private const float LIST_ROW_H  = 17f;
        private const int   LIST_ROWS   = 5;      // visible at once; the rest scrolls
        private const float LIST_H      = LIST_ROW_H * LIST_ROWS;

        /// <summary>Rows are built, not virtualised, so the list is capped. Fifty is far past
        /// what anyone reads and still cheap; beyond it the count line is the honest readout
        /// and a row per member would be a hitch per selection change for nothing.</summary>
        private const int LIST_MAX_ROWS = 50;

        private RectTransform _listContent;
        private readonly List<GameObject> _listRows = new List<GameObject>(LIST_MAX_ROWS);

        // Rebuild only when the selection actually changed. RefreshPanel runs on every pick,
        // every prune and every frame a member dies; rebuilding fifty uGUI rows on each would
        // be the Controls editor's 213 ms-per-keystroke defect in a different costume.
        private int        _listBuiltCount = -1;
        private GameObject _listBuiltPrimary;

        private void BuildSelectionList(Transform parent)
        {
            var (scroll, content) = UIFactory.MakeScrollView(parent, "SelectionList", LIST_H);
            _listContent = content;

            var le = scroll.gameObject.GetComponent<LayoutElement>() ??
                     scroll.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = LIST_H;
            // MakeScrollView sets flexibleHeight = 1 when it is given a height, which in a
            // VerticalLayoutGroup makes the list swallow every spare pixel in the panel — the
            // same trap the tab strip shipped at 180 px tall.
            le.flexibleHeight  = 0f;
            le.minHeight       = LIST_H;

            EditorUIHelpers.AddVerticalScrollbar(scroll);
        }

        private void RefreshSelectionList()
        {
            if (_listContent == null) return;

            var primary = _selection.Primary;
            if (_selection.Count == _listBuiltCount && primary.Go == _listBuiltPrimary) return;
            _listBuiltCount   = _selection.Count;
            _listBuiltPrimary = primary.Go;

            for (int i = 0; i < _listRows.Count; i++)
            {
                if (_listRows[i] == null) continue;
                _listRows[i].SetActive(false);
                // Object.Destroy is an ERROR in Edit Mode, and a runtime editor's list rebuild
                // is reached from EditMode fixtures.
                if (Application.isPlaying) Destroy(_listRows[i]);
                else                       DestroyImmediate(_listRows[i]);
            }
            _listRows.Clear();

            var items = _selection.Items;
            int shown = Mathf.Min(items.Count, LIST_MAX_ROWS);
            for (int i = 0; i < shown; i++) AddListRow(items[i], items[i].Go == primary.Go);

            if (items.Count > shown) AddOverflowRow(items.Count - shown);
        }

        private void AddListRow(SelectionItem item, bool isPrimary)
        {
            if (!item.IsAlive) return;

            var bg = EditorUIHelpers.AddActionBtn(_listContent, "", LIST_ROW_H,
                () => IsolateInSelection(item), out var tmp, fontSize: 9f,
                style: FontStyles.Normal);

            // Left-aligned with room for the swatch: a centred id is unreadable as a list.
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.margin    = new Vector4(12f, 0f, 4f, 0f);
            tmp.text      = item.Domain.Describe(item.Go) + (isPrimary ? "   (ancla)" : "");
            tmp.color     = isPrimary ? UITheme.ACCENT : UITheme.TEXT_PRIMARY;

            var sw  = EditorUIHelpers.CreateUI("Swatch", bg.transform);
            var srt = (RectTransform)sw.transform;
            srt.anchorMin = srt.anchorMax = new Vector2(0f, 0.5f);
            srt.pivot     = new Vector2(0f, 0.5f);
            srt.sizeDelta = new Vector2(3f, LIST_ROW_H - 6f);
            srt.anchoredPosition = new Vector2(3f, 0f);
            var swImg = sw.AddComponent<Image>();
            swImg.color = item.Domain.Tint;
            swImg.raycastTarget = false;

            var btn = bg.GetComponent<Button>();
            if (btn != null) UIButton.SetTint(btn, isPrimary ? UITheme.ACCENT_BG : UITheme.BTN_NORMAL);

            _listRows.Add(bg.gameObject);
        }

        private void AddOverflowRow(int hidden)
        {
            var go = EditorUIHelpers.CreateUI("ListOverflow", _listContent);
            go.AddComponent<LayoutElement>().preferredHeight = LIST_ROW_H;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text          = $"... y {hidden} mas";
            tmp.fontSize      = 9f;
            tmp.fontStyle     = FontStyles.Italic;
            tmp.color         = UITheme.TEXT_MUTED;
            tmp.alignment     = TextAlignmentOptions.MidlineLeft;
            tmp.margin        = new Vector4(12f, 0f, 4f, 0f);
            tmp.raycastTarget = false;
            _listRows.Add(go);
        }

        /// <summary>Leave exactly this one selected. The point of the list: box a cluster, read
        /// what it caught, keep the one you meant — and then move it.</summary>
        private void IsolateInSelection(SelectionItem item)
        {
            if (!item.IsAlive) { SetStatus("Ese elemento ya no existe."); return; }

            _selection.Clear();
            _selection.Add(item.Domain, item.Go);
            RefreshPanel();
            SetStatus($"Aislado: {item.Domain.Describe(item.Go)}. Arrastralo para moverlo.");
        }
    }
}
