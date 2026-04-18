using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Valkur.Gameplay.Editors.EditorKit
{
    /// <summary>
    /// Horizontal tab bar widget for runtime editors.
    /// Mirrors Python editors' state_tabs / assets_subtabs panels.
    /// Each tab toggles a content panel; only one content is visible at a time.
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

        public int ActiveIndex => _activeIndex;
        public string ActiveKey => _activeIndex >= 0 && _activeIndex < _tabs.Count ? _tabs[_activeIndex].Key : null;
        public event Action<int, string> TabChanged;

        /// <summary>Adds a tab. Content is toggled when the tab is clicked.</summary>
        public void AddTab(string key, string label, GameObject content)
        {
            var btnGo = EditorUIHelpers.CreateUI("Tab_" + key, transform);
            var le = btnGo.AddComponent<LayoutElement>();
            le.preferredHeight = 26f; le.flexibleWidth = 1f;
            var bg = btnGo.AddComponent<Image>();
            bg.color = EditorUIHelpers.BTN_NORMAL;
            var btn = btnGo.AddComponent<Button>();
            btn.targetGraphic = bg;
            var tmp = EditorUIHelpers.AddCenteredText(btnGo.transform, label, 12f,
                FontStyles.Bold, EditorUIHelpers.TEXT_PRIMARY);
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
                _tabs[i].BgImage.color = on ? EditorUIHelpers.ACCENT_BG : EditorUIHelpers.BTN_NORMAL;
                _tabs[i].Label.color = on ? EditorUIHelpers.ACCENT : EditorUIHelpers.TEXT_PRIMARY;
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

        public static TabStrip Create(Transform parent, string name, float height = 28f)
        {
            var go = EditorUIHelpers.CreateUI(name, parent);
            go.AddComponent<LayoutElement>().preferredHeight = height;
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 2f; hlg.childForceExpandWidth = true;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            return go.AddComponent<TabStrip>();
        }
    }
}
