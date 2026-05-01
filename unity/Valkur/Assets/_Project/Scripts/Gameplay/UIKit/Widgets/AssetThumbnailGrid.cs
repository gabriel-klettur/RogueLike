using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Valkur.UIKit
{
    /// <summary>
    /// Reusable thumbnail picker grid for runtime editors and HUD windows.
    /// Mirrors Python editors' asset grids
    /// (entities/panels/properties/asset_grid.py). Adds a search box, scroll
    /// view and a flexible number of columns.
    /// </summary>
    public sealed class AssetThumbnailGrid : MonoBehaviour
    {
        public sealed class Entry
        {
            public string Id;
            public string Label;
            public Sprite Thumb;
            public object Data;
        }

        private readonly List<Entry> _all = new List<Entry>();
        private readonly List<Button> _buttons = new List<Button>();
        private RectTransform _content;
        private GridLayoutGroup _grid;
        private TMP_InputField _search;
        private string _filter = "";
        private string _selectedId;

        public event Action<Entry> SelectionChanged;
        public string SelectedId => _selectedId;
        public Entry SelectedEntry => _all.Find(e => e.Id == _selectedId);

        public static AssetThumbnailGrid Create(Transform parent, string name, int columns = 4, float cellSize = 64f)
        {
            var root = UIFactory.CreateUI(name, parent);
            var rootLe = root.AddComponent<LayoutElement>();
            rootLe.flexibleWidth = 1f; rootLe.flexibleHeight = 1f;
            var rootVlg = root.AddComponent<VerticalLayoutGroup>();
            rootVlg.spacing = 4f; rootVlg.padding = new RectOffset(2, 2, 2, 2);
            rootVlg.childForceExpandWidth = true; rootVlg.childControlWidth = true;
            rootVlg.childControlHeight = true;

            var grid = root.AddComponent<AssetThumbnailGrid>();

            grid._search = SearchBox.Create(root.transform, "Search...", s =>
            {
                grid._filter = s ?? "";
                grid.Rebuild();
            });

            var (scroll, content) = UIFactory.MakeScrollView(root.transform, "Items");
            scroll.horizontal = false; scroll.vertical = true;
            var vlg = content.GetComponent<VerticalLayoutGroup>();
            if (vlg != null) Destroy(vlg);
            var csf = content.GetComponent<ContentSizeFitter>();
            if (csf == null) csf = content.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var gl = content.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(cellSize, cellSize + 16f);
            gl.spacing = new Vector2(4f, 4f);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = Mathf.Max(1, columns);
            grid._content = content;
            grid._grid = gl;
            return grid;
        }

        public void SetEntries(IEnumerable<Entry> entries)
        {
            _all.Clear();
            if (entries != null) _all.AddRange(entries);
            Rebuild();
        }

        public void SetColumns(int columns)
        {
            if (_grid != null) _grid.constraintCount = Mathf.Max(1, columns);
        }

        public void SelectById(string id)
        {
            _selectedId = id;
            for (int i = 0; i < _buttons.Count; i++) UpdateButtonVisual(i);
        }

        public void Rebuild()
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);
            _buttons.Clear();

            foreach (var e in _all)
            {
                if (!MatchesFilter(e, _filter)) continue;
                int index = _buttons.Count;
                var (btn, icon, label) = UIButton.MakeSlot(_content, e.Label ?? e.Id,
                    _grid.cellSize.x, null);
                if (e.Thumb != null)
                {
                    icon.sprite = e.Thumb;
                    icon.enabled = true;
                }
                var captured = e;
                btn.onClick.AddListener(() =>
                {
                    _selectedId = captured.Id;
                    for (int i = 0; i < _buttons.Count; i++) UpdateButtonVisual(i);
                    SelectionChanged?.Invoke(captured);
                });
                _buttons.Add(btn);
                UpdateButtonVisual(index);
            }
        }

        private bool MatchesFilter(Entry e, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            var f = filter.Trim();
            if (f.Length == 0) return true;
            if (!string.IsNullOrEmpty(e.Id) && e.Id.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!string.IsNullOrEmpty(e.Label) && e.Label.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private void UpdateButtonVisual(int index)
        {
            if (index < 0 || index >= _buttons.Count) return;
            var btn = _buttons[index];
            var img = btn.GetComponent<Image>();
            if (img == null) return;
            bool selected = IsSelectedAt(index);
            img.color = selected ? UITheme.SLOT_SELECTED : UITheme.SLOT_BG;
        }

        private bool IsSelectedAt(int index)
        {
            int i = -1;
            foreach (var e in _all)
            {
                if (!MatchesFilter(e, _filter)) continue;
                i++;
                if (i == index) return e.Id == _selectedId;
            }
            return false;
        }
    }
}
