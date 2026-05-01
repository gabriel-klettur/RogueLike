using System;
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
        // ── Tools Panel ───────────────────────────────────────────────────────────
        // Mirrors Python particles_tool_bar_panel + particles_add_remove_panel.
        // Mode toolbar (Select / Place / Delete) + Add/Remove + Save/Reload + Undo/Redo.

        private static void BuildToolsPanel(Transform canvasT, ref UIRefs refs,
            Action onModeSelect, Action onModePlace, Action onModeDelete,
            Action onAddSystem,  Action onRemoveSystem,
            Action onUndo, Action onRedo, Action onSave, Action onReload)
        {
            refs.ToolsDropdown = MakeDrop("ParticlesToolsPanel", canvasT,
                PanelDock.TopLeft, PANEL_GAP, PANEL_TOP_OFFSET,
                TOOLS_W, TOOLS_H, "Tools",
                out var t, out refs.ToolsPanelDrag);

            // Mode row — Select / Place / Delete (3 columns)
            BuildSectionLabel(t, "MODE");
            var modeRow = CreateUI("ModeRow", t);
            modeRow.AddComponent<LayoutElement>().preferredHeight = 32f;
            var mh = modeRow.AddComponent<HorizontalLayoutGroup>();
            mh.spacing = 4f;
            mh.childForceExpandWidth = true;  mh.childForceExpandHeight = true;
            mh.childControlWidth = true;      mh.childControlHeight = true;
            refs.SelectBtnImg = AddActionBtn(modeRow.transform, "Select", 32f, onModeSelect, out _);
            refs.PlaceBtnImg  = AddActionBtn(modeRow.transform, "Place",  32f, onModePlace,  out _);
            refs.DeleteBtnImg = AddDangerBtn(modeRow.transform, "Delete", 32f, onModeDelete, out _);

            // Add / Remove row
            BuildSeparator(t);
            BuildSectionLabel(t, "SYSTEM");
            var arRow = CreateUI("AddRemoveRow", t);
            arRow.AddComponent<LayoutElement>().preferredHeight = 32f;
            var ah = arRow.AddComponent<HorizontalLayoutGroup>();
            ah.spacing = 4f;
            ah.childForceExpandWidth = true;  ah.childForceExpandHeight = true;
            ah.childControlWidth = true;      ah.childControlHeight = true;
            refs.AddSystemBtnImg    = AddActionBtn(arRow.transform, "+ Add",   32f, onAddSystem,    out _);
            refs.RemoveSystemBtnImg = AddDangerBtn(arRow.transform, "− Remove", 32f, onRemoveSystem, out _);

            // Save / Reload row
            BuildSeparator(t);
            BuildSectionLabel(t, "FILE");
            var sRow = CreateUI("SaveRow", t);
            sRow.AddComponent<LayoutElement>().preferredHeight = 30f;
            var sh = sRow.AddComponent<HorizontalLayoutGroup>();
            sh.spacing = 4f;
            sh.childForceExpandWidth = true;  sh.childForceExpandHeight = true;
            sh.childControlWidth = true;      sh.childControlHeight = true;
            refs.SaveBtnImg   = AddActionBtn(sRow.transform, "Save",   30f, onSave,   out _);
            refs.ReloadBtnImg = AddActionBtn(sRow.transform, "Reload", 30f, onReload, out _);

            // Undo / Redo row (stacked, label-update-friendly)
            BuildSeparator(t);
            BuildSectionLabel(t, "HISTORY");
            refs.UndoBtnImg = AddActionBtn(t, "Undo", 28f, onUndo, out refs.UndoBtnLabel);
            refs.RedoBtnImg = AddActionBtn(t, "Redo", 28f, onRedo, out refs.RedoBtnLabel);

            refs.ToolsDropdown.SetActive(false);
        }

        // ── Presets Panel ─────────────────────────────────────────────────────────
        // Mirrors Python particles_picker_panel: search + ALL/GROUP toggle + grid + status.

        private static void BuildPresetsPanel(Transform canvasT, ref UIRefs refs,
            Action<string> onSearchChanged, Action onToggleGroup)
        {
            float x = PANEL_GAP + TOOLS_W + PANEL_GAP;
            refs.PresetsDropdown = MakeDrop("ParticlesPresetsPanel", canvasT,
                PanelDock.TopLeft, x, PANEL_TOP_OFFSET,
                PRESETS_W, PRESETS_H, "Presets",
                out var t, out refs.PresetsPanelDrag);

            refs.SearchBox = SearchBox.Create(t, "Search presets…",
                v => onSearchChanged?.Invoke(v ?? ""));

            // ALL / GROUP toggle row
            var groupRow = CreateUI("GroupRow", t);
            groupRow.AddComponent<LayoutElement>().preferredHeight = 26f;
            var gh = groupRow.AddComponent<HorizontalLayoutGroup>();
            gh.spacing = 4f;
            gh.childForceExpandWidth = true;  gh.childForceExpandHeight = true;
            gh.childControlWidth = true;      gh.childControlHeight = true;

            var lblGo = CreateUI("DispLbl", groupRow.transform);
            lblGo.AddComponent<LayoutElement>().preferredWidth = 60f;
            var dispLbl       = lblGo.AddComponent<TextMeshProUGUI>();
            dispLbl.text      = "Display:";
            dispLbl.fontSize  = 10f;
            dispLbl.alignment = TextAlignmentOptions.MidlineLeft;
            dispLbl.color     = TEXT_MUTED;

            refs.GroupToggleImg = AddActionBtn(groupRow.transform, "ALL", 26f, onToggleGroup,
                out refs.GroupToggleLabel);

            // Grid picker
            var (scroll, content) = EditorUIHelpers.MakeGridPicker(t, "PresetGrid", 4, 64f, 4f);
            var le = scroll.gameObject.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.minHeight      = 240f;
            EditorUIHelpers.AddVerticalScrollbar(scroll);
            refs.PickerContent = content;

            refs.StatusText = EditorUIHelpers.MakeStatusText(t);

            refs.PresetsDropdown.SetActive(false);
        }

        // ── Spells Panel ──────────────────────────────────────────────────────────
        // Collapsible "SPELLS USING THIS PRESET" — Python particles_spells_list_panel.

        private static void BuildSpellsPanel(Transform canvasT, ref UIRefs refs,
            Action onToggleSpells)
        {
            // Dock under the right edge: Properties takes 280, Spells stacks below.
            float y = PANEL_TOP_OFFSET + PROPS_H + PANEL_GAP;
            refs.SpellsDropdown = MakeDrop("ParticlesSpellsPanel", canvasT,
                PanelDock.TopRight, PANEL_GAP, y,
                SPELLS_W, SPELLS_H, "Spells",
                out var t, out refs.SpellsPanelDrag);

            // Collapsible header (▼/▶ click to expand/collapse content)
            var headerBtn = EditorUIHelpers.MakeButton(t,
                "▼ SPELLS USING THIS PRESET",
                () => onToggleSpells?.Invoke(),
                24f, 11f);
            refs.SpellsHeaderTmp = headerBtn.GetComponentInChildren<TextMeshProUGUI>();
            refs.SpellsHeaderTmp.alignment = TextAlignmentOptions.Left;
            refs.SpellsHeaderTmp.margin    = new Vector4(8f, 0f, 0f, 0f);

            // Scroll + content for spell list rows
            var (scroll, content) = EditorUIHelpers.MakeScrollView(t, "SpellsScroll");
            scroll.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            EditorUIHelpers.AddVerticalScrollbar(scroll);
            refs.SpellsContent = content;

            refs.SpellsDropdown.SetActive(false);
        }
    }
}
