using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.Controls
{
    /// <summary>
    /// The action list, the capture flow, and the one rule that is not configurable: nothing
    /// that reaches the damage path may be given a Peace binding.
    /// </summary>
    public partial class ControlsRuntimeEditor
    {
        private readonly List<GameObject> _rows = new List<GameObject>();

        private InputActionDescriptor _capturing;
        private int _captureBindingIndex = -1;

        internal bool IsCapturing => _capturing != null;

        // ── The list ─────────────────────────────────────────────────────────

        private void RebuildActionList()
        {
            if (_ui?.ListContent == null) return;

            foreach (var row in _rows) if (row != null) Destroy(row);
            _rows.Clear();

            string needle = _search?.Trim().ToLowerInvariant() ?? "";
            var svc = InputService.Instance;

            foreach (var descriptor in InputActionCatalog.All)
            {
                if (!Matches(descriptor, needle)) continue;
                if (!ShownInContext(descriptor)) continue;
                _rows.Add(BuildRow(descriptor, svc));
            }

            if (_rows.Count == 0)
                _rows.Add(BuildEmptyNotice(needle));
        }

        private static bool Matches(InputActionDescriptor d, string needle)
        {
            if (string.IsNullOrEmpty(needle)) return true;
            return d.DisplayName.ToLowerInvariant().Contains(needle)
                || d.Action.ToLowerInvariant().Contains(needle)
                || d.PayloadKey.ToLowerInvariant().Contains(needle);
        }

        /// <summary>
        /// Which actions the list offers in the context being viewed.
        ///
        /// <para>In Peace the damage actions are not greyed out, they are ABSENT. A disabled
        /// row is a control the author keeps trying, and this refusal is not a limitation to be
        /// worked around — it is the property the stance exists to provide.</para>
        ///
        /// <para>In an editor context the list is that editor's world: the shared verbs plus
        /// its OWN tools, and nothing from gameplay or from another editor. That is the rule
        /// stated plainly — an open editor takes the whole keyboard, so what it can bind is
        /// only what it can do.</para>
        /// </summary>
        private bool ShownInContext(InputActionDescriptor d) =>
            InputContextPolicy.IsLive(d, _viewContext);

        private GameObject BuildRow(InputActionDescriptor d, InputService svc)
        {
            var go = UIFactory.CreateUI("Row_" + d.Action, _ui.ListContent);
            var bg = go.AddComponent<Image>();
            bg.color = UITheme.SLOT_BG;

            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.padding = new RectOffset(6, 6, 3, 3);
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 26f;
            le.flexibleHeight = 0f;

            AddText(go.transform, d.DisplayName, 11f, UITheme.TEXT_PRIMARY, flexibleWidth: 1f);

            var action = ResolveAction(svc, d);
            string chip = action == null ? "?" : InputBindingResolver.PrimaryLabel(action);
            if (string.IsNullOrEmpty(chip)) chip = "sin asignar";
            AddText(go.transform, chip, 10f,
                    string.IsNullOrEmpty(chip) ? UITheme.TEXT_MUTED : UITheme.ACCENT,
                    preferredWidth: 96f);

            // Posture chips, for gameplay actions that could legally differ. An editor action
            // gets none: its context is decided by which editor owns it, and a chip that could
            // only ever be on would be a control that does nothing.
            if (d.Map == InputActionCatalog.MapGameplay && !d.ReachesDamage)
                AddStanceChips(go.transform, d);

            var assign = EditorUIHelpers.MakeButton(go.transform, "...",
                () => BeginCapture(d), 20f, 10f);
            var assignLe = assign.gameObject.GetComponent<LayoutElement>()
                        ?? assign.gameObject.AddComponent<LayoutElement>();
            assignLe.preferredWidth = 28f;
            assignLe.flexibleWidth = 0f;
            assign.interactable = d.Rebindable;

            return go;
        }

        private GameObject BuildEmptyNotice(string needle)
        {
            var go = UIFactory.CreateUI("Empty", _ui.ListContent);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 40f;
            le.flexibleHeight = 0f;
            AddText(go.transform,
                string.IsNullOrEmpty(needle)
                    ? "Nada que mostrar en este contexto."
                    : $"Ninguna accion coincide con '{needle}'.",
                11f, UITheme.TEXT_MUTED, flexibleWidth: 1f);
            return go;
        }

        private void AddStanceChips(Transform parent, InputActionDescriptor d)
        {
            var mask = InputContextPolicy.ContextsOf(d);
            Chip(parent, "G", (mask & InputContextMask.War) != 0,
                 () => ToggleStanceBit(d, InputContextMask.War));
            Chip(parent, "P", (mask & InputContextMask.Peace) != 0,
                 () => ToggleStanceBit(d, InputContextMask.Peace));
        }

        private void Chip(Transform parent, string label, bool on, Action onClick)
        {
            var btn = EditorUIHelpers.MakeButton(parent, label, () => onClick?.Invoke(), 20f, 10f);
            var le = btn.gameObject.GetComponent<LayoutElement>()
                  ?? btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 22f;
            le.flexibleWidth = 0f;
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = on ? UITheme.BTN_ACTIVE : UITheme.BTN_NORMAL;
        }

        private void ToggleStanceBit(InputActionDescriptor d, InputContextMask bit)
        {
            var next = InputContextPolicy.ContextsOf(d) ^ bit;
            var verdict = InputContextPolicy.SetContexts(d, next);
            if (verdict != InputAssignmentVerdict.Allowed)
            {
                SetStatus(InputContextPolicy.Explain(verdict));
                return;
            }

            _dirty = true;
            RebuildActionList();
            RepaintAll();
            SetStatus($"{d.DisplayName}: ahora vive en {Describe(InputContextPolicy.ContextsOf(d))}.");
        }

        private static string Describe(InputContextMask mask) => mask switch
        {
            InputContextMask.Gameplay  => "Guerra y Paz",
            InputContextMask.War   => "Guerra",
            InputContextMask.Peace => "Paz",
            _                => "ninguna postura",
        };

        // ── Capture ──────────────────────────────────────────────────────────

        /// <summary>
        /// Starts "press a key". Deliberately NOT Unity's
        /// <c>InputActionRebindingExtensions.PerformInteractiveRebinding</c>: that listens to
        /// raw devices, so it happily captures a key this project cannot express as a
        /// <see cref="InputControlEntry"/> — and a binding whose legacy half resolves to
        /// <see cref="KeyCode.None"/> works in the editor and dies the first time the 2022.3
        /// event-drop bug fires. Clicking a drawn cap is the primary path and this poll is the
        /// secondary one; both funnel through <see cref="CompleteCaptureWithPath"/>.
        /// </summary>
        private void BeginCapture(InputActionDescriptor d)
        {
            var verdict = InputContextPolicy.EvaluateRebind(d);
            if (verdict != InputAssignmentVerdict.Allowed)
            {
                SetStatus(InputContextPolicy.Explain(verdict));
                return;
            }

            _capturing = d;
            _captureBindingIndex = 0;

            if (_ui?.CaptureOverlay != null)
            {
                _ui.CaptureOverlay.SetActive(true);
                _ui.CaptureText.text =
                    $"Pulsa la tecla o el boton para «{d.DisplayName}».\n" +
                    "Tambien puedes hacer click en una tecla del teclado dibujado.\n" +
                    "Click fuera o Escape para cancelar.";
            }
        }

        internal void CancelCapture()
        {
            _capturing = null;
            _captureBindingIndex = -1;
            if (_ui?.CaptureOverlay != null) _ui.CaptureOverlay.SetActive(false);
        }

        /// <summary>
        /// Polls for a real key press while capturing.
        ///
        /// <para>The SCAN reads the device directly, which is one of the documented exceptions
        /// to this project's input rule: the job is "which physical control was pressed", and
        /// every centralized helper answers about a control the caller has already named — so
        /// there is nothing to route through. The CANCEL is not an exception and goes through
        /// <see cref="KeyboardInputManager"/>, which also means Escape keeps working while a
        /// modal holds input (it is on the always-allowed list).</para>
        /// </summary>
        private void TickCapture()
        {
            if (!IsCapturing) return;

            if (EditorInput.ClosePressed())
            {
                CancelCapture();
                SetStatus("Reasignacion cancelada.");
                return;
            }

            var kb = Keyboard.current;
            if (kb == null) return;

            foreach (var entry in InputControlPaths.Entries)
            {
                if (entry.Key == Key.Escape) continue;   // handled above, as the cancel
                if (!kb[entry.Key].wasPressedThisFrame) continue;
                CompleteCaptureWithPath(entry.Path);
                return;
            }
        }

        private void CompleteCaptureWithPath(string path)
        {
            var d = _capturing;
            if (d == null || string.IsNullOrEmpty(path)) { CancelCapture(); return; }

            var action = ResolveAction(InputService.Instance, d);
            if (action == null)
            {
                SetStatus($"'{d.Id}' no tiene accion en el asset — no se puede reasignar.");
                CancelCapture();
                return;
            }

            int index = ResolveOverridableBindingIndex(action, _captureBindingIndex);
            if (index < 0)
            {
                SetStatus($"'{d.DisplayName}' no tiene ningun binding que reasignar.");
                CancelCapture();
                return;
            }

            InputActionRebindingExtensions.ApplyBindingOverride(action, index, path);
            InputBindingResolver.Invalidate();
            _dirty = true;

            CancelCapture();
            RebuildActionList();
            RepaintAll();

            string label = InputControlPaths.LabelForPath(path);
            var clash = LiveOn(path);
            SetStatus(clash.Count > 1
                ? $"{d.DisplayName} → {label}. OJO: esa tecla ya tiene {clash.Count} acciones vivas en esta postura."
                : $"{d.DisplayName} → {label}. Recuerda GUARDAR.");
        }

        /// <summary>
        /// Which binding slot a rebind writes. Composite headers name no control and must be
        /// skipped, or an override lands on the "2DVector" row and moves nothing while
        /// reporting success.
        /// </summary>
        private static int ResolveOverridableBindingIndex(InputAction action, int preferred)
        {
            var bindings = action.bindings;
            int seen = 0;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].isComposite) continue;
                if (seen == preferred) return i;
                seen++;
            }
            // Fall back to the first real binding rather than refusing: a caller asking for
            // slot 2 of a one-binding action means "move it", not "do nothing".
            for (int i = 0; i < bindings.Count; i++)
                if (!bindings[i].isComposite) return i;
            return -1;
        }

        private static InputAction ResolveAction(InputService svc, InputActionDescriptor d)
        {
            var map = svc?.Asset?.FindActionMap(d.Map, throwIfNotFound: false);
            return map?.FindAction(d.Action, throwIfNotFound: false);
        }

        // ── Row primitives ───────────────────────────────────────────────────

        private static TextMeshProUGUI AddText(Transform parent, string text, float size,
                                               Color color, float flexibleWidth = 0f,
                                               float preferredWidth = 0f)
        {
            // Image and TextMeshProUGUI on one GameObject throw a NullReferenceException in
            // this project, so every label is its own object under the row.
            var go = UIFactory.CreateUI("Text", parent);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;

            var le = go.AddComponent<LayoutElement>();
            if (preferredWidth > 0f) { le.preferredWidth = preferredWidth; le.flexibleWidth = 0f; }
            else le.flexibleWidth = flexibleWidth;
            return tmp;
        }
    }
}
