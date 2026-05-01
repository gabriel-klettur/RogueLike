using UnityEngine;
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
        // count, etc.) + INSTANCE section (id, position).

        private static void BuildPropertiesPanel(Transform canvasT, ref UIRefs refs)
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
            refs.PresetPropsText           = EditorUIHelpers.AddLabel(presetContent,
                "Select a preset to view properties.", 11f);
            refs.PresetPropsText.color     = TEXT_SECONDARY;
            refs.PresetPropsText.alignment = TextAlignmentOptions.TopLeft;
            refs.PresetPropsText.enableWordWrapping = true;

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

            refs.PropsDropdown.SetActive(false);
        }
    }
}
