using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Enemies.FSM
{
    public static partial class FSMEditorUIBuilder
    {
        // ── Properties Panel (TopRight, tabbed) ───────────────────────────────────
        // Mirrors Python fsm_properties_panel: tab bar with
        // [State / Transition / Actions / Conditions / Blackboard]
        // and a scrollable rich-text content area beneath.

        private static void BuildPropertiesPanel(Transform canvasT, ref UIRefs refs,
            Action onTabState, Action onTabTransition, Action onTabActions,
            Action onTabConditions, Action onTabBlackboard)
        {
            refs.PropsDropdown = MakeDrop("FSMPropertiesPanel", canvasT,
                PanelDock.TopRight, PANEL_GAP, PANEL_TOP_OFFSET,
                PROPS_W, PROPS_H, "Properties", out var t, out refs.PropsPanelDrag);

            // Tab bar
            var tabBar = CreateUI("TabBar", t);
            tabBar.AddComponent<LayoutElement>().preferredHeight = 26f;
            var hlg = tabBar.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing                = 2f;
            hlg.childForceExpandWidth  = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;

            (refs.StateTabImg,      refs.StateTabTmp)      = AddTabBtn(tabBar.transform, "State",      onTabState);
            (refs.TransitionTabImg, refs.TransitionTabTmp) = AddTabBtn(tabBar.transform, "Transition", onTabTransition);
            (refs.ActionsTabImg,    refs.ActionsTabTmp)    = AddTabBtn(tabBar.transform, "Actions",    onTabActions);
            (refs.ConditionsTabImg, refs.ConditionsTabTmp) = AddTabBtn(tabBar.transform, "Conditions", onTabConditions);
            (refs.BlackboardTabImg, refs.BlackboardTabTmp) = AddTabBtn(tabBar.transform, "Bb",         onTabBlackboard);

            BuildSeparator(t);

            // Scrollable content
            var (scroll, content) = EditorUIHelpers.MakeScrollView(t, "PropsScroll");
            var le = scroll.gameObject.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.minHeight      = 200f;
            EditorUIHelpers.AddVerticalScrollbar(scroll);

            var textGo = CreateUI("PropsText", content);
            // Stretch to width, vertical sized by ContentSizeFitter
            var prt = textGo.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0f, 1f);
            prt.anchorMax = new Vector2(1f, 1f);
            prt.pivot     = new Vector2(0f, 1f);
            prt.offsetMin = new Vector2(0f, -PROPS_H);
            prt.offsetMax = Vector2.zero;
            refs.PropsText                    = textGo.AddComponent<TextMeshProUGUI>();
            refs.PropsText.text               = "Select a state or transition.";
            refs.PropsText.fontSize           = 11f;
            refs.PropsText.color              = TEXT_SECONDARY;
            refs.PropsText.richText           = true;
            refs.PropsText.alignment          = TextAlignmentOptions.TopLeft;
            refs.PropsText.enableWordWrapping = true;

            refs.PropsDropdown.SetActive(false);
        }

        private static (Image img, TextMeshProUGUI tmp) AddTabBtn(Transform parent, string label, Action onClick)
        {
            var go = CreateUI($"Tab_{label}", parent);
            var img   = go.AddComponent<Image>();
            img.color = BTN_NORMAL;

            var btn = go.AddComponent<Button>();
            var c   = btn.colors;
            c.normalColor      = BTN_NORMAL;
            c.highlightedColor = BTN_HOVER;
            c.pressedColor     = BTN_ACTIVE;
            btn.colors         = c;
            btn.targetGraphic  = img;
            if (onClick != null) btn.onClick.AddListener(() => onClick.Invoke());

            var tmp       = AddCenteredText(go.transform, label, 9f, FontStyles.Bold, TEXT_PRIMARY);
            tmp.alignment = TextAlignmentOptions.Center;
            return (img, tmp);
        }

        /// <summary>
        /// Apply highlight to the active tab (called from FSMRuntimeEditor when
        /// the active properties tab changes).
        /// </summary>
        public static void ApplyTabStyle(Image img, TextMeshProUGUI tmp, bool isActive)
        {
            if (img != null) img.color = isActive ? BTN_ACTIVE : BTN_NORMAL;
            if (tmp != null)
            {
                tmp.color     = isActive ? ACCENT      : TEXT_PRIMARY;
                tmp.fontStyle = isActive ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        private static void BuildSeparator(Transform parent)
        {
            var go = CreateUI("Sep", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 1f;
            go.AddComponent<Image>().color = SEPARATOR;
        }
    }
}
