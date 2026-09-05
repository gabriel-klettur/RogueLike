using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Core.Input;

namespace Valkur.UI.PauseMenu
{
    public partial class PauseMenuUI
    {
        // ── Keybindings panel ────────────────────────────────────────────────
        //
        // WHAT THIS USED TO BE, AND WHY IT IS GONE. Four tabs of rebindable rows, 461 lines,
        // writing to GameSettings.*KeyA strings — and every gameplay field it wrote had ZERO
        // readers in production, measured by grep: moveUpKeyA, dashKeyA, spell1KeyA..4,
        // primaryAttackMouse, pauseKeyA, toggleInventoryKeyA. Only twelve editor F-keys were
        // ever bridged to the real bindings, by EditorBindingsApplier, and only slot 0 of each.
        // So a player could rebind their movement here, see the new key in this panel, save it
        // to disk, and change nothing at all about the game. It was a control surface over a
        // model nothing read.
        //
        // The real model is ValkurInputActions, and its editor is the Controls editor (ESC →
        // Controls), which draws the keyboard and the mouse and can bind any action to any
        // key per War/Peace stance. This panel now READS that model — the live effective
        // paths, not a parallel string table — and hands off to the editor for changes. A
        // read-only view that is true beats an interactive one that is not.

        private readonly List<TextMeshProUGUI> _inputRowValues = new List<TextMeshProUGUI>();
        private readonly List<InputActionDescriptor> _inputRowActions = new List<InputActionDescriptor>();

        /// <summary>The actions worth showing at a glance. Not the whole catalog: this is a
        /// reminder of the controls, not a substitute for the editor.</summary>
        [Valkur.Core.SelfHealingStatic("Immutable list of action ids, built once from string literals. Holds no Unity object and is never mutated, so it cannot go stale across a Play session.")]
        private static readonly string[] SUMMARY_ACTION_IDS =
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

        private GameObject BuildInputsPanel(Transform parent)
        {
            const float panelW = 740f;
            const float panelH = 500f;

            var panel = CreateUIObject("InputsPanel", parent);
            var r = panel.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0.5f, 0.5f); r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f); r.anchoredPosition = Vector2.zero;
            r.sizeDelta = new Vector2(panelW, panelH);
            panel.AddComponent<Image>().color = PanelBg;

            AddPanelTitle(panel.transform, "Controles", panelH, 20f);

            AddInputsNote(panel.transform, panelH,
                "Estos son los controles activos ahora mismo, leidos de los bindings reales.\n" +
                "Para cambiarlos abre ESC -> Controls: teclado y raton dibujados, por postura.");

            _inputRowValues.Clear();
            _inputRowActions.Clear();

            float top = -132f;
            const float rowH = 26f;
            for (int i = 0; i < SUMMARY_ACTION_IDS.Length; i++)
            {
                var descriptor = InputActionCatalog.Find(SUMMARY_ACTION_IDS[i]);
                if (descriptor == null) continue;
                AddSummaryRow(panel.transform, descriptor, top - i * rowH, panelW, rowH);
            }

            return panel;
        }

        private void AddInputsNote(Transform parent, float panelH, string text)
        {
            var go = CreateUIObject("InputsNote", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -62f);
            rt.sizeDelta = new Vector2(-64f, 56f);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 14f;
            tmp.color = TextNormal;
            tmp.alignment = TextAlignmentOptions.Top;
            tmp.raycastTarget = false;
        }

        private void AddSummaryRow(Transform parent, InputActionDescriptor descriptor,
                                   float y, float panelW, float rowH)
        {
            var row = CreateUIObject("Row_" + descriptor.Action, parent);
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(-64f, rowH);

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

            _inputRowValues.Add(vtmp);
            _inputRowActions.Add(descriptor);
        }

        /// <summary>
        /// Repaints from the LIVE bindings. Called on every open, so a rebind made in the
        /// Controls editor is reflected here without either surface knowing about the other —
        /// they read the same asset, which is the whole point of there being one model.
        /// </summary>
        private void UpdateInputsPanel()
        {
            var asset = InputService.Instance?.Asset;
            for (int i = 0; i < _inputRowValues.Count && i < _inputRowActions.Count; i++)
            {
                var descriptor = _inputRowActions[i];
                var map = asset?.FindActionMap(descriptor.Map, throwIfNotFound: false);
                var action = map?.FindAction(descriptor.Action, throwIfNotFound: false);

                string chip = action == null ? "?" : InputBindingResolver.PrimaryLabel(action);
                _inputRowValues[i].text = string.IsNullOrEmpty(chip) ? "sin asignar" : chip;
                _inputRowValues[i].color = string.IsNullOrEmpty(chip) ? TextNormal : TextSelected;
            }
        }

        /// <summary>
        /// The panel is read-only, so the only keyboard verb it needs is "go back", which
        /// <c>HandleGlobalBack</c> already owns. Kept as a no-op rather than deleted because
        /// <c>PauseMenuUI.Input</c> dispatches on the screen enum and a missing arm there is a
        /// silent dead screen.
        /// </summary>
        private void HandleInputsTabInput() { }
    }
}
