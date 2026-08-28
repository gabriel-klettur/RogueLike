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
        // ── Sets Panel ────────────────────────────────────────────────────────────
        // Mirrors Python fsm_sets_panel: search box + scrollable list of FSM Sets.

        private static void BuildSetsPanel(Transform canvasT, ref UIRefs refs,
            Action<string> onSearchChanged)
        {
            float setsX = PANEL_GAP + TOOLS_W + PANEL_GAP;
            refs.SetsDropdown = MakeDrop("FSMSetsPanel", canvasT,
                PanelDock.TopLeft, setsX, PANEL_TOP_OFFSET,
                SETS_W, SETS_H, "FSM Sets", out var t, out refs.SetsPanelDrag);

            refs.SearchBox = SearchBox.Create(t, "Search sets\u2026",
                v => onSearchChanged?.Invoke(v ?? ""));

            var (scroll, content) = EditorUIHelpers.MakeScrollView(t, "SetsScroll");
            var pickerLE = scroll.gameObject.AddComponent<LayoutElement>();
            pickerLE.flexibleHeight = 1f;
            pickerLE.minHeight      = 200f;
            EditorUIHelpers.AddVerticalScrollbar(scroll);
            refs.SetsContent = content;

            refs.StatusText = EditorUIHelpers.MakeStatusText(t);

            refs.SetsDropdown.SetActive(false);
        }

        // ── Entities Assignment Panel ─────────────────────────────────────────────
        // Mirrors Python fsm_assigment_entities (entity → FSM Set mapping list).

        private static void BuildEntitiesPanel(Transform canvasT, ref UIRefs refs)
        {
            float x = PANEL_GAP + TOOLS_W + PANEL_GAP + SETS_W + PANEL_GAP;
            refs.EntitiesDropdown = MakeDrop("FSMEntitiesPanel", canvasT,
                PanelDock.TopLeft, x, PANEL_TOP_OFFSET,
                ENTITIES_W, ENTITIES_H, "Entities Assignment", out var t, out refs.EntitiesPanelDrag);

            var hintGo = CreateUI("EntHint", t);
            hintGo.AddComponent<LayoutElement>().preferredHeight = 32f;
            refs.EntitiesHintText                    = hintGo.AddComponent<TextMeshProUGUI>();
            refs.EntitiesHintText.text               =
                "Map monsters to FSM Sets. Amber rows have NO set (they boot a bare " +
                "IdleState); grey rows inherit MonsterDefinition.fsmSet.";
            refs.EntitiesHintText.fontSize           = 10f;
            refs.EntitiesHintText.color              = TEXT_SECONDARY;
            refs.EntitiesHintText.enableWordWrapping = true;
            refs.EntitiesHintText.alignment          = TextAlignmentOptions.TopLeft;

            var (scroll, content) = EditorUIHelpers.MakeScrollView(t, "EntitiesScroll");
            var le = scroll.gameObject.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.minHeight      = 240f;
            EditorUIHelpers.AddVerticalScrollbar(scroll);
            refs.EntitiesContent = content;

            refs.EntitiesDropdown.SetActive(false);
        }

        // ── Animations Assignment Panel ───────────────────────────────────────────
        // Mirrors Python fsm_assigment_animations (state → animation mapping list).

        private static void BuildAnimationsPanel(Transform canvasT, ref UIRefs refs)
        {
            // Anchored to TopRight near the Properties panel — keeps the central
            // graph area clean. Slightly overlaps Properties when both are open;
            // user can drag either panel via its header to reposition.
            float x = PANEL_GAP + PROPS_W + PANEL_GAP;
            refs.AnimationsDropdown = MakeDrop("FSMAnimationsPanel", canvasT,
                PanelDock.TopRight, x, PANEL_TOP_OFFSET,
                ANIMATIONS_W, ANIMATIONS_H, "Animations Assignment", out var t, out refs.AnimationsPanelDrag);

            var hintGo = CreateUI("AnimHint", t);
            hintGo.AddComponent<LayoutElement>().preferredHeight = 32f;
            refs.AnimationsHintText                    = hintGo.AddComponent<TextMeshProUGUI>();
            refs.AnimationsHintText.text               =
                "Map FSM state classes to animation clips. '<' / '>' cycles the target: " +
                "'default' is inherited by every set; per-set tabs override it.";
            refs.AnimationsHintText.fontSize           = 10f;
            refs.AnimationsHintText.color              = TEXT_SECONDARY;
            refs.AnimationsHintText.enableWordWrapping = true;
            refs.AnimationsHintText.alignment          = TextAlignmentOptions.TopLeft;

            var (scroll, content) = EditorUIHelpers.MakeScrollView(t, "AnimationsScroll");
            var le = scroll.gameObject.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.minHeight      = 240f;
            EditorUIHelpers.AddVerticalScrollbar(scroll);
            refs.AnimationsContent = content;

            refs.AnimationsDropdown.SetActive(false);
        }
    }
}
