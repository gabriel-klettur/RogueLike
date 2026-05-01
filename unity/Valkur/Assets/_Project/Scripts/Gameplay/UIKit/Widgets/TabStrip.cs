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

        public int ActiveIndex => _activeIndex;
        public string ActiveKey => _activeIndex >= 0 && _activeIndex < _tabs.Count ? _tabs[_activeIndex].Key : null;
        public event Action<int, string> TabChanged;

        public void AddTab(string key, string label, GameObject content)
        {
            var btnGo = UIFactory.CreateUI("Tab_" + key, transform);
            var le = btnGo.AddComponent<LayoutElement>();
            // Lock height tightly so the parent VLG/HLG can't stretch the buttons.
            le.minHeight       = 24f;
            le.preferredHeight = 24f;
            le.flexibleHeight  = 0f;
            le.flexibleWidth   = 1f;
            var bg = btnGo.AddComponent<Image>();
            bg.color = UITheme.BTN_NORMAL;
            var btn = btnGo.AddComponent<Button>();
            btn.targetGraphic = bg;
            var tmp = UILabel.AddCenteredText(btnGo.transform, label, 11f,
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
    }
}
