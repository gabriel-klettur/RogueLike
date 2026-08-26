using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.Editors;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Gameplay.Buildings
{
    /// <summary>
    /// The Door authoring flyout: a panel below Tools that appears while the editor is in
    /// <c>EditorMode.Door</c>.
    ///
    /// It is a flyout rather than another block of rows in the Properties panel because a
    /// doorway is authored across TWO scopes at once, and mixing them into the per-instance
    /// inspector would hide that:
    ///
    ///   • "Has doorway" and the anchor rows write the TEMPLATE ScriptableObject and
    ///     therefore change EVERY placement of that art. The panel says so in its own header
    ///     rather than leaving the author to discover it on the eightieth house.
    ///   • The destination and spawn write THIS placement only.
    ///
    /// The layout follows the Erase scope sub-panel: same dock, same offset arithmetic off
    /// MODES_H, hidden unless its mode is active.
    /// </summary>
    public static partial class BuildingsEditorUIBuilder
    {
        private const float DOOR_SUB_W = 250f;
        private const float DOOR_SUB_H = PANEL_HDR_H + 396f;

        private static void BuildDoorSubPanel(Transform canvasT, ref UIRefs refs,
            Action onToggleHasDoor,
            Action<string> onTargetCommit,
            Action<string> onSpawnXCommit,
            Action<string> onSpawnYCommit,
            Action onAnchorXMinus, Action onAnchorXPlus,
            Action onAnchorYMinus, Action onAnchorYPlus,
            Action onSizeMinus,    Action onSizePlus,
            Action onApply,        Action onClear)
        {
            refs.DoorSubPanel = MakeDrop("DoorPanel", canvasT,
                PanelDock.TopLeft,
                PANEL_GAP,
                PANEL_TOP_OFFSET + MODES_H + PANEL_GAP,
                DOOR_SUB_W, DOOR_SUB_H, "Doorway",
                out var t, out var _);

            // ── Which building, and which scope each control writes ──
            var statusGo = CreateUI("DoorStatus", t);
            statusGo.AddComponent<LayoutElement>().preferredHeight = 64f;
            refs.DoorStatusText                    = statusGo.AddComponent<TextMeshProUGUI>();
            refs.DoorStatusText.text               = "Click a building on the map.";
            refs.DoorStatusText.fontSize           = 10f;
            refs.DoorStatusText.color              = TEXT_SECONDARY;
            refs.DoorStatusText.alignment          = TextAlignmentOptions.TopLeft;
            refs.DoorStatusText.enableWordWrapping = true;
            refs.DoorStatusText.richText           = true;

            // ── Template scope ──
            BuildSeparator(t);
            AddSectionLabel(t, "Template (all placements)");

            (refs.DoorHasDoorBtnImg, refs.DoorHasDoorBtnLabel) =
                AddFullWidthBtn(t, "[ ] Has doorway", 28f, onToggleHasDoor);

            BuildZRow(t, "Anchor X", onAnchorXMinus, onAnchorXPlus, out refs.DoorAnchorXVal);
            BuildZRow(t, "Anchor Y", onAnchorYMinus, onAnchorYPlus, out refs.DoorAnchorYVal);
            BuildZRow(t, "Size",     onSizeMinus,    onSizePlus,    out refs.DoorSizeVal);

            // ── Instance scope ──
            BuildSeparator(t);
            AddSectionLabel(t, "This placement");

            AddFieldLabel(t, "Target overlay");
            refs.DoorTargetField = EditorUIHelpers.AddInputField(t, "", onTargetCommit, 24f, 10f);

            var spawnRow = CreateUI("SpawnRow", t);
            spawnRow.AddComponent<LayoutElement>().preferredHeight = 24f;
            var shlg = spawnRow.AddComponent<HorizontalLayoutGroup>();
            shlg.spacing                = 4f;
            shlg.childForceExpandWidth  = true;
            shlg.childForceExpandHeight = true;
            shlg.childControlWidth      = true;
            shlg.childControlHeight     = true;

            refs.DoorSpawnXField = EditorUIHelpers.AddInputField(spawnRow.transform, "0", onSpawnXCommit, 24f, 10f);
            refs.DoorSpawnYField = EditorUIHelpers.AddInputField(spawnRow.transform, "0", onSpawnYCommit, 24f, 10f);

            AddFieldLabel(t, "Spawn X / Y in the destination");

            BuildSeparator(t);
            var applyBtn = AddFullWidthBtn(t, "Apply doorway", 30f, onApply);
            refs.DoorApplyBtnImg = applyBtn.Item1;
            AddFullWidthBtn(t, "Clear doorway", 26f, onClear);

            refs.DoorSubPanel.SetActive(false);
        }

        /// <summary>Small dim caption above (or below) a field. The panel has several.</summary>
        private static void AddFieldLabel(Transform parent, string text)
        {
            var go = CreateUI("FieldLabel", parent);
            go.AddComponent<LayoutElement>().preferredHeight = 14f;
            var tmp       = go.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.fontSize  = 9f;
            tmp.color     = TEXT_SECONDARY;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
        }
    }
}
