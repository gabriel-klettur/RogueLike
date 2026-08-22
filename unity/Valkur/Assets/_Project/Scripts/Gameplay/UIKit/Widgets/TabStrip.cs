using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Valkur.UIKit
{
    /// <summary>
    /// Horizontal tab bar widget for runtime editors and HUD windows.
    /// Mirrors Python editors' state_tabs / assets_subtabs panels. Each
    /// tab toggles a content panel; only one content is visible at a time.
    /// </summary>
    public sealed class TabStrip : MonoBehaviour
    {
        private class Tab
        {
            public string Key;
            public Image BgImage;
            public TextMeshProUGUI Label;
            public GameObject Content;
        }

        private readonly List<Tab> _tabs = new List<Tab>();
        private int _activeIndex = -1;

        // ── Wrapped mode ──────────────────────────────────────────────────────────
        // Zero means the classic single-row strip: tabs are parented straight to this
        // transform and share its width. Above zero, tabs are packed into rows of that
        // many, which is what lets a narrow panel carry nine readable tabs instead of
        // nine 28 px slivers.
        private int _columns;
        private float _rowHeight;
        private float _rowSpacing;
        private Transform _currentRow;
        private LayoutElement _rootLayout;
        private int _rowCount;

        /// <summary>
        /// Point size for tab labels. Tabs share the strip's width equally, so a strip with
        /// many tabs needs smaller text or every label wraps mid-word — "Portals" rendering
        /// as "Portal / s" is not a truncation the reader can undo.
        /// Defaults to the value every existing strip was built with.
        /// </summary>
        public float LabelFontSize = 11f;

        public int ActiveIndex => _activeIndex;
        public string ActiveKey => _activeIndex >= 0 && _activeIndex < _tabs.Count ? _tabs[_activeIndex].Key : null;
        public event Action<int, string> TabChanged;

        public void AddTab(string key, string label, GameObject content)
        {
            var btnGo = UIFactory.CreateUI("Tab_" + key, ResolveTabParent());
            var le = btnGo.AddComponent<LayoutElement>();
            // Lock height tightly so the parent VLG/HLG can't stretch the buttons.
            le.minHeight       = _columns > 0 ? _rowHeight : 24f;
            le.preferredHeight = _columns > 0 ? _rowHeight : 24f;
            le.flexibleHeight  = 0f;
            le.flexibleWidth   = 1f;
            var bg = btnGo.AddComponent<Image>();
            bg.color = UITheme.BTN_NORMAL;
            var btn = btnGo.AddComponent<Button>();
            btn.targetGraphic = bg;
            var tmp = UILabel.AddCenteredText(btnGo.transform, label, LabelFontSize,
                FontStyles.Bold, UITheme.TEXT_PRIMARY);
            int idx = _tabs.Count;
            btn.onClick.AddListener(() => SetActive(idx));

            var tab = new Tab { Key = key, BgImage = bg, Label = tmp, Content = content };
            _tabs.Add(tab);
            if (content != null) content.SetActive(false);
            if (_activeIndex < 0) SetActive(0);
        }

        public void SetActive(int index)
        {
            if (index < 0 || index >= _tabs.Count) return;
            for (int i = 0; i < _tabs.Count; i++)
            {
                bool on = (i == index);
                _tabs[i].BgImage.color = on ? UITheme.ACCENT_BG : UITheme.BTN_NORMAL;
                _tabs[i].Label.color = on ? UITheme.ACCENT : UITheme.TEXT_PRIMARY;
                if (_tabs[i].Content != null) _tabs[i].Content.SetActive(on);
            }
            _activeIndex = index;
            TabChanged?.Invoke(index, _tabs[index].Key);
        }

        public bool SetActive(string key)
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (_tabs[i].Key == key) { SetActive(i); return true; }
            }
            return false;
        }

        public int Count => _tabs.Count;

        /// <summary>
        /// Row that the next tab belongs in. Single-row strips always answer with this
        /// transform; wrapped strips open a new row every <see cref="_columns"/> tabs and
        /// grow the widget's locked height to match, so the parent layout reserves the
        /// right space without a ContentSizeFitter fighting it.
        /// </summary>
        private Transform ResolveTabParent()
        {
            if (_columns <= 0) return transform;
            if (_currentRow != null && _currentRow.childCount < _columns) return _currentRow;

            var rowGo = UIFactory.CreateUI("Row_" + _rowCount, transform);
            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 2f;
            hlg.childForceExpandWidth  = true;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            hlg.childAlignment         = TextAnchor.MiddleCenter;

            var rowLe = rowGo.AddComponent<LayoutElement>();
            rowLe.minHeight       = _rowHeight;
            rowLe.preferredHeight = _rowHeight;
            rowLe.flexibleHeight  = 0f;
            rowLe.layoutPriority  = 2;

            _currentRow = rowGo.transform;
            _rowCount++;

            if (_rootLayout != null)
            {
                float h = _rowCount * _rowHeight + (_rowCount - 1) * _rowSpacing;
                _rootLayout.minHeight       = h;
                _rootLayout.preferredHeight = h;
            }
            return _currentRow;
        }

        public static TabStrip Create(Transform parent, string name, float height = 26f)
        {
            var go = UIFactory.CreateUI(name, parent);
            // HLG also implements ILayoutElement and reports preferred height
            // from its children; add the LayoutElement AFTER and bump its
            // priority so the parent VLG honours our explicit, locked height
            // instead of stretching the strip.
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 2f;
            hlg.childForceExpandWidth  = true;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            hlg.childAlignment         = TextAnchor.MiddleCenter;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight       = height;
            le.preferredHeight = height;
            le.flexibleHeight  = 0f;
            le.layoutPriority  = 2;
            return go.AddComponent<TabStrip>();
        }

        /// <summary>
        /// Multi-row variant: tabs are packed <paramref name="columns"/> to a row and the
        /// widget grows downward as they are added.
        ///
        /// A single-row strip divides its width evenly, so it only stays legible while the
        /// tab count is small — nine tabs across the Buildings panel's 368 px of content
        /// would leave 39 px each and every label would wrap mid-word. Wrapping trades
        /// vertical space, which a docked editor panel has, for horizontal space, which it
        /// does not.
        /// </summary>
        public static TabStrip CreateWrapped(Transform parent, string name,
            int columns, float rowHeight = 24f, float rowSpacing = 2f)
        {
            var go = UIFactory.CreateUI(name, parent);
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.spacing                = rowSpacing;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;
            vlg.childAlignment         = TextAnchor.UpperCenter;

            var le = go.AddComponent<LayoutElement>();
            le.minHeight       = rowHeight;
            le.preferredHeight = rowHeight;
            le.flexibleHeight  = 0f;
            le.layoutPriority  = 2;

            var strip = go.AddComponent<TabStrip>();
            strip._columns    = Mathf.Max(1, columns);
            strip._rowHeight  = rowHeight;
            strip._rowSpacing = rowSpacing;
            strip._rootLayout = le;
            return strip;
        }
    }
}
