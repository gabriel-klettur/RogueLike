using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Core.Input;

namespace Valkur.UI.MainMenu
{
    public partial class MainMenuUI
    {
        // ── Controls panel ───────────────────────────────────────────────────
        //
        // WHAT THIS USED TO BE. Four tabs of rebindable rows writing GameSettings.*KeyA
        // strings — 392 lines over a model that gameplay did not read. Every gameplay field
        // it wrote had zero production readers, measured: moveUpKeyA, dashKeyA,
        // spell1KeyA..4, primaryAttackMouse. Only twelve editor F-keys were bridged to the
        // real bindings, and only slot 0 of each. A player could rebind their movement here,
        // watch the panel update, save it, and change nothing about the game.
        //
        // The real model is ValkurInputActions and its editor is the in-game Controls editor
        // (ESC → Controls), which draws the keyboard and mouse and binds any action to any key
        // per War/Peace stance. The main menu has no gameplay scene to host that editor, so
        // here it reads the same live bindings and says where to change them. A read-only view
        // that is true beats an interactive one that is not.

        private readonly List<TextMeshProUGUI> _optInputValues = new List<TextMeshProUGUI>();
        private readonly List<InputActionDescriptor> _optInputActions = new List<InputActionDescriptor>();

        [Valkur.Core.SelfHealingStatic("Immutable list of action ids, built once from string literals. Holds no Unity object and is never mutated, so it cannot go stale across a Play session.")]

        private static readonly string[] OPT_SUMMARY_ACTION_IDS =
        {
            "Gameplay/Move",
            "Gameplay/Dash",
            "Gameplay/PrimaryAttack",
            "Gameplay/SecondaryAttack",
            "Gameplay/MiddleClick",
            "Gameplay/Interact",
            "Gameplay/Inventory",
            "Gameplay/DropItem",
            "Gameplay/ToggleStance",
            "Gameplay/Pause",
            "Editors/OpenGeneralEditor",
            "Editors/ToggleDevConsole",
        };

        private void BuildOptInputsPanel(Transform parent)
        {
            const float panelW = 760f;
            const float panelH = 520f;

            _optInputsPanel = CreateUIObject("OptInputsPanel", parent);
            var r = _optInputsPanel.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0.5f, 0.5f); r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f); r.anchoredPosition = Vector2.zero;
            r.sizeDelta = new Vector2(panelW, panelH);
            _optInputsPanel.AddComponent<Image>().color = PanelBg;

            AddOptNote(_optInputsPanel.transform,
                "CONTROLES",
                -30f, 26f, TextSelected, TextAlignmentOptions.Center);

            AddOptNote(_optInputsPanel.transform,
                "Controles activos, leidos de los bindings reales del juego.\n" +
                "Para cambiarlos: entra en la partida y abre ESC -> Controls.\n" +
                "Ahi tienes el teclado y el raton dibujados, y puedes asignar\n" +
                "cualquier accion a cualquier tecla, por postura (Guerra / Paz).",
                -72f, 84f, TextNormal, TextAlignmentOptions.Top);

            _optInputValues.Clear();
            _optInputActions.Clear();

            float top = -176f;
            const float rowH = 26f;
            int shown = 0;
            foreach (var id in OPT_SUMMARY_ACTION_IDS)
            {
                var descriptor = InputActionCatalog.Find(id);
                if (descriptor == null) continue;
                AddOptSummaryRow(_optInputsPanel.transform, descriptor, top - shown * rowH, rowH);
                shown++;
            }
        }

        private void AddOptNote(Transform parent, string text, float y, float height,
                                Color color, TextAlignmentOptions align)
        {
            var go = CreateUIObject("OptNote", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(-64f, height);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 15f;
            tmp.color = color;
            tmp.alignment = align;
            tmp.raycastTarget = false;
        }

        private void AddOptSummaryRow(Transform parent, InputActionDescriptor descriptor,
                                      float y, float rowH)
        {
            var row = CreateUIObject("ORow_" + descriptor.Action, parent);
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(-72f, rowH);

            var label = CreateUIObject("Label", row.transform);
            var lrt = label.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0f, 0f); lrt.anchorMax = new Vector2(0.55f, 1f);
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var ltmp = label.AddComponent<TextMeshProUGUI>();
            ltmp.text = descriptor.DisplayName;
            ltmp.fontSize = 15f;
            ltmp.color = TextNormal;
            ltmp.alignment = TextAlignmentOptions.Left;
            ltmp.raycastTarget = false;

            var value = CreateUIObject("Value", row.transform);
            var vrt = value.GetComponent<RectTransform>();
            vrt.anchorMin = new Vector2(0.55f, 0f); vrt.anchorMax = new Vector2(1f, 1f);
            vrt.offsetMin = Vector2.zero; vrt.offsetMax = Vector2.zero;
            var vtmp = value.AddComponent<TextMeshProUGUI>();
            vtmp.text = "";
            vtmp.fontSize = 15f;
            vtmp.color = TextSelected;
            vtmp.alignment = TextAlignmentOptions.Right;
            vtmp.raycastTarget = false;

            _optInputValues.Add(vtmp);
            _optInputActions.Add(descriptor);
        }
    }
}
