using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.VFX
{
    public static partial class ParticlesEditorUIBuilder
    {
        // ── Properties Panel ──────────────────────────────────────────────────────
        // Mirrors Python particles_properties_panel: PRESET section (kind, lifetime,
        // count, etc.) + INSTANCE section (id, position) + Delete Instance action.

        private static void BuildPropertiesPanel(Transform canvasT, ref UIRefs refs,
            Action onDeleteInstance = null,
            UnityAction<bool> onLoopsToggled = null)
        {
            refs.PropsDropdown = MakeDrop("ParticlesPropsPanel", canvasT,
                PanelDock.TopRight, PANEL_GAP, PANEL_TOP_OFFSET,
                PROPS_W, PROPS_H, "Properties",
                out var t, out refs.PropsPanelDrag);

            // PRESET PROPERTIES
            BuildSectionLabel(t, "PRESET PROPERTIES");
            var (presetScroll, presetContent) = EditorUIHelpers.MakeScrollView(t, "PresetPropsScroll");
            var presetLe = presetScroll.gameObject.AddComponent<LayoutElement>();
            presetLe.flexibleHeight = 2f;
            presetLe.minHeight      = 180f;
            EditorUIHelpers.AddVerticalScrollbar(presetScroll);
            // Editable rows live in the form; the text label survives beneath it as the
            // footer for what the form cannot yet edit (colour lists, curves, sprites).
            refs.PresetPropsForm = PropertyForm.Create(presetContent, "PresetPropsForm");
            refs.PresetPropsText           = EditorUIHelpers.AddLabel(presetContent,
                "Select a preset to view properties.", 11f);
            refs.PresetPropsText.color     = TEXT_SECONDARY;
            refs.PresetPropsText.alignment = TextAlignmentOptions.TopLeft;
            refs.PresetPropsText.enableWordWrapping = true;

            // LOOPS TOGGLE — lets designers flip loops per-preset without re-importing.
            // Placed as a sibling row (not inside the scroll) so it's always visible.
            // Parent = content of properties panel (VerticalLayoutGroup), so it obeys layout.
            // We use a row GO to host both the Toggle control and the label, avoiding
            // the Image+TMP-on-same-GO pitfall (CLAUDE.md gotcha).
            var toggleRowGo = CreateUI("LoopsToggleRow", t);
            toggleRowGo.AddComponent<LayoutElement>().preferredHeight = 22f;
            var rowHlg = toggleRowGo.AddComponent<HorizontalLayoutGroup>();
            rowHlg.spacing             = 6f;
            rowHlg.childForceExpandWidth  = false;
            rowHlg.childForceExpandHeight = true;
            rowHlg.childControlWidth      = true;
            rowHlg.childControlHeight     = true;
            rowHlg.childAlignment         = TextAnchor.MiddleLeft;
            rowHlg.padding                = new RectOffset(2, 2, 0, 0);

            // Toggle widget (checkbox) — no text on this GO
            var toggleGo = CreateUI("LoopsCheckbox", toggleRowGo.transform);
            toggleGo.AddComponent<LayoutElement>().preferredWidth = 18f;
            var bgImg = toggleGo.AddComponent<Image>();
            bgImg.color = new Color(0.15f, 0.15f, 0.18f, 1f);
            var toggle = toggleGo.AddComponent<Toggle>();

            // Checkmark child
            var checkGo  = CreateUI("Checkmark", toggleGo.transform);
            var checkRt  = checkGo.GetComponent<RectTransform>();
            checkRt.anchorMin = new Vector2(0.1f, 0.1f);
            checkRt.anchorMax = new Vector2(0.9f, 0.9f);
            checkRt.offsetMin = Vector2.zero;
            checkRt.offsetMax = Vector2.zero;
            var checkImg = checkGo.AddComponent<Image>();
            checkImg.color = UITheme.ACCENT;

            toggle.targetGraphic = bgImg;
            toggle.graphic       = checkImg;
            toggle.isOn          = true; // default; updated by ShowPresetProperties
            if (onLoopsToggled != null) toggle.onValueChanged.AddListener(onLoopsToggled);
            refs.LoopsToggle = toggle;

            // Label GO (separate from the Toggle GO — avoids Image+TMP same-GO bug)
            var labelGo = CreateUI("LoopsLabel", toggleRowGo.transform);
            labelGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var labelTmp       = labelGo.AddComponent<TextMeshProUGUI>();
            labelTmp.text      = "Loops (Persistent)";
            labelTmp.fontSize  = 10f;
            labelTmp.color     = TEXT_SECONDARY;
            labelTmp.alignment = TextAlignmentOptions.Left;
            labelTmp.enableWordWrapping = false;
            labelTmp.raycastTarget = false;

            BuildSeparator(t);

            // INSTANCE PROPERTIES
            BuildSectionLabel(t, "INSTANCE PROPERTIES");
            var (instScroll, instContent) = EditorUIHelpers.MakeScrollView(t, "InstancePropsScroll");
            var instLe = instScroll.gameObject.AddComponent<LayoutElement>();
            instLe.flexibleHeight = 1f;
            instLe.minHeight      = 80f;
            EditorUIHelpers.AddVerticalScrollbar(instScroll);
            refs.InstancePropsText           = EditorUIHelpers.AddLabel(instContent,
                "Select an instance on the map.", 11f);
            refs.InstancePropsText.color     = TEXT_SECONDARY;
            refs.InstancePropsText.alignment = TextAlignmentOptions.TopLeft;
            refs.InstancePropsText.enableWordWrapping = true;

            // DELETE INSTANCE button — shown only when an instance is selected.
            BuildSeparator(t);
            refs.DeleteInstanceBtnImg = AddDangerBtn(t, "Delete Instance",
                28f, onDeleteInstance, out _);
            refs.DeleteInstanceBtnGo = refs.DeleteInstanceBtnImg.gameObject;
            refs.DeleteInstanceBtnGo.SetActive(false); // hidden until an instance is selected

            refs.PropsDropdown.SetActive(false);
        }
    }
}
