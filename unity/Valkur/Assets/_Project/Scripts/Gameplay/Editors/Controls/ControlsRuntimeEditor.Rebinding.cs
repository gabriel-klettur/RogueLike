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
        private const float ROW_H       = 26f;
        private const float CHIP_W      = 118f;
        private const float SLOT_NAME_W = 66f;
        private const float SUB_INDENT  = 20f;

        /// <summary>
        /// One realised row and everything filtering it needs, so a keystroke can HIDE rows
        /// instead of destroying and rebuilding them.
        ///
        /// <para>That split is worth 213 ms per keystroke, measured. Every row carries up to
        /// five buttons and seven TextMeshPro components, so building the sixty-three of them
        /// costs about as much as opening the editor — and the shipped code paid it on every
        /// character typed into the search box. Rows are built once per CONTEXT (a human-scale
        /// event) and shown or hidden after that.</para>
        /// </summary>
        private sealed class RowEntry
        {
            public GameObject Go;
            public string ActionId;

            /// <summary>True for a per-slot row, which is additionally gated on its action
            /// being expanded.</summary>
            public bool IsSlotRow;

            /// <summary>Lowercased name, action, payload key and every key it is bound to.
            /// Slot rows inherit their action's, so a filter keeps a row and its slots
            /// together.</summary>
            public string SearchBlob;

            /// <summary>The expander's own label, so opening a row does not need a rebuild.</summary>
            public TextMeshProUGUI ExpanderLabel;
        }

        private readonly List<RowEntry> _entries = new List<RowEntry>();
        private GameObject _emptyNotice;

        /// <summary>Action ids whose per-slot rows are open. An action with one binding never
        /// enters this set — it has nothing to expand into.</summary>
        private readonly HashSet<string> _expanded = new HashSet<string>(StringComparer.Ordinal);

        private InputActionDescriptor _capturing;
        private int _captureBindingIndex = -1;

        internal bool IsCapturing => _capturing != null;
        internal InputActionDescriptor CapturingAction => _capturing;
        internal int CapturingSlot => _captureBindingIndex;

        // ── The list ─────────────────────────────────────────────────────────

        /// <summary>
        /// Realises every row this CONTEXT can show — the action rows and, for a multi-control
        /// action, its per-slot rows — and then applies the search filter by visibility.
        ///
        /// <para>Called when the underlying facts change: a context switch, a rebind, a mask
        /// change, a reset. NOT on a keystroke: see <see cref="ApplyFilter"/>.</para>
        /// </summary>
        private void RebuildActionList()
        {
            if (_ui?.ListContent == null) return;

            foreach (var entry in _entries) DestroySafely(entry.Go);
            _entries.Clear();

            var svc = InputService.Instance;

            foreach (var descriptor in InputActionCatalog.All)
            {
                if (!InputContextPolicy.BelongsTo(descriptor, _viewContext)) continue;

                var action = ResolveAction(svc, descriptor);
                var slots = SlotsOf(action);
                string blob = SearchBlobFor(descriptor, slots);

                var row = BuildRow(descriptor, action, slots, out var expanderLabel);
                _entries.Add(new RowEntry
                {
                    Go = row, ActionId = descriptor.Id, IsSlotRow = false,
                    SearchBlob = blob, ExpanderLabel = expanderLabel,
                });

                if (slots.Count <= 1) continue;
                for (int i = 0; i < slots.Count; i++)
                    _entries.Add(new RowEntry
                    {
                        Go = BuildSlotRow(descriptor, action, slots, i),
                        ActionId = descriptor.Id, IsSlotRow = true, SearchBlob = blob,
                    });
            }

            ApplyFilter();
        }

        /// <summary>
        /// Shows the rows that match the search box and are not folded away, and hides the
        /// rest. No allocation and no rebuild, which is why the search box is usable at all.
        /// </summary>
        private void ApplyFilter()
        {
            if (_ui?.ListContent == null) return;

            string needle = _search?.Trim().ToLowerInvariant() ?? "";
            int shown = 0;

            foreach (var entry in _entries)
            {
                bool matches = needle.Length == 0 || entry.SearchBlob.Contains(needle);
                bool visible = matches && (!entry.IsSlotRow || _expanded.Contains(entry.ActionId));
                if (entry.Go != null && entry.Go.activeSelf != visible) entry.Go.SetActive(visible);
                if (visible && !entry.IsSlotRow) shown++;

                if (entry.ExpanderLabel != null)
                    entry.ExpanderLabel.text = _expanded.Contains(entry.ActionId) ? "v" : ">";
            }

            ShowEmptyNotice(shown == 0, needle);
        }

        private void ShowEmptyNotice(bool visible, string needle)
        {
            if (!visible)
            {
                if (_emptyNotice != null) _emptyNotice.SetActive(false);
                return;
            }

            if (_emptyNotice == null) _emptyNotice = BuildEmptyNotice();
            _emptyNotice.SetActive(true);
            _emptyNotice.transform.SetAsLastSibling();
            var label = _emptyNotice.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
                label.text = needle.Length == 0
                    ? "Nada que mostrar en este contexto."
                    : $"Ninguna accion coincide con '{needle}'.";
        }

        /// <summary>Everything a search can match this action on, lowercased once at build time
        /// rather than on every keystroke. Searching by KEY is half of what a rebinding surface
        /// is for — "what is on F5" is the question an author asks before moving something onto
        /// it — and it was the half that was missing, while the dev console's own
        /// <c>binding</c> command had it.</summary>
        private static string SearchBlobFor(InputActionDescriptor d, List<InputBindingSlot> slots)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(d.DisplayName).Append(' ').Append(d.Action).Append(' ').Append(d.PayloadKey);
            foreach (var slot in slots)
            {
                if (!slot.IsBound) continue;
                sb.Append(' ').Append(slot.Label).Append(' ').Append(slot.Path);
                // "shift 1" and "shift+1" both find a chord, which is how an author asks "what
                // is on the second layer of 1".
                if (slot.IsChord) sb.Append(' ').Append(InputChord.ShortModifier(slot.ModifierPath))
                                    .Append(' ').Append(InputControlPaths.LabelForPath(slot.Path));
            }
            return sb.ToString().ToLowerInvariant();
        }

        /// <summary>
        /// The bindable slots of an action: every non-composite binding, in asset order.
        ///
        /// <para>Composite HEADERS are skipped and composite PARTS are kept, which is the only
        /// arrangement in which Move can be rebound at all: it is one action with eight real
        /// controls, and a rebind that could only reach "slot 0" moved the W of WASD and left
        /// the other seven exactly where they were. That was the shipped behaviour.</para>
        /// </summary>
        private static List<InputBindingSlot> SlotsOf(InputAction action)
        {
            var slots = new List<InputBindingSlot>(4);
            if (action == null) return slots;

            // InputChord.Slots is the one walk that folds a Shift+key chord into a single slot.
            // Walked part by part, "Shift+1" would offer to rebind its Shift as a slot of its
            // own and print the chip as "Shift izq.  +1".
            foreach (var s in InputChord.Slots(action))
                slots.Add(new InputBindingSlot(s.Index, s.Part, s.Path, s.ModifierPath));
            return slots;
        }

        /// <summary>One rebindable control of one action.</summary>
        internal readonly struct InputBindingSlot
        {
            /// <summary>Index into <c>action.bindings</c> — what ApplyBindingOverride needs.
            /// For a chord, the KEY's index: a rebind moves the key and keeps the modifier.</summary>
            public readonly int Index;

            /// <summary>Composite part name ("up", "left"), or empty for a plain binding.</summary>
            public readonly string Part;

            /// <summary>The live path, override first. Empty means unassigned.</summary>
            public readonly string Path;

            /// <summary>The chord's modifier path; empty for anything that is not a chord.</summary>
            public readonly string ModifierPath;

            public InputBindingSlot(int index, string part, string path, string modifierPath = "")
            {
                Index = index; Part = part ?? ""; Path = path ?? ""; ModifierPath = modifierPath ?? "";
            }

            public bool IsBound => !string.IsNullOrEmpty(Path);
            public bool IsChord => !string.IsNullOrEmpty(ModifierPath);

            public string Label => !IsBound ? "sin asignar"
                                 : IsChord ? InputChord.LabelFor(ModifierPath, Path)
                                           : InputControlPaths.LabelForPath(Path);

            /// <summary>What to call this slot in a per-slot row. The InputSystem fixes a
            /// 2DVector's part names, so they are the same four words on every composite.</summary>
            public string SlotName => Part switch
            {
                "up"    => "Arriba",
                "down"  => "Abajo",
                "left"  => "Izq.",
                "right" => "Der.",
                ""      => "",
                _       => Part,
            };
        }

        // ── Rows ─────────────────────────────────────────────────────────────

        private GameObject BuildRow(InputActionDescriptor d, InputAction action,
                                    List<InputBindingSlot> slots,
                                    out TextMeshProUGUI expanderLabel)
        {
            expanderLabel = null;
            bool live = InputContextPolicy.IsLive(d, _viewContext);
            var go = MakeRowShell("Row_" + d.Action, 0f);

            var name = AddText(go.transform, d.DisplayName, 11f,
                               live ? UITheme.TEXT_PRIMARY : UITheme.TEXT_MUTED, flexibleWidth: 1f);
            if (!live) name.text = d.DisplayName + "  (silenciado)";

            AddText(go.transform, ChipTextFor(slots), 10f,
                    slots.Count == 0 || !slots[0].IsBound ? UITheme.TEXT_MUTED : UITheme.ACCENT,
                    preferredWidth: CHIP_W);

            AddContextChips(go.transform, d);

            if (slots.Count > 1)
            {
                bool open = _expanded.Contains(d.Id);
                var expander = SmallButton(go.transform, open ? "v" : ">", 26f,
                                           () => ToggleExpanded(d));
                expanderLabel = expander.GetComponentInChildren<TextMeshProUGUI>(true);
                UIHoverText.Attach(expander.gameObject, _ui.Status,
                    $"{d.DisplayName} tiene {slots.Count} teclas. Abre la fila para cambiar cada una por separado.");
            }
            else
            {
                AddSlotButtons(go.transform, d, action, slots, slots.Count == 1 ? 0 : -1);
            }

            return go;
        }

        private GameObject BuildSlotRow(InputActionDescriptor d, InputAction action,
                                        List<InputBindingSlot> slots, int ordinal)
        {
            var slot = slots[ordinal];
            var go = MakeRowShell($"Slot_{d.Action}_{ordinal}", SUB_INDENT);

            string slotName = string.IsNullOrEmpty(slot.SlotName) ? $"#{ordinal + 1}" : slot.SlotName;
            AddText(go.transform, slotName, 10f, UITheme.TEXT_SECONDARY, preferredWidth: SLOT_NAME_W);
            AddText(go.transform, slot.Label, 10f,
                    slot.IsBound ? UITheme.ACCENT : UITheme.TEXT_MUTED, flexibleWidth: 1f);

            AddSlotButtons(go.transform, d, action, slots, ordinal);
            return go;
        }

        private GameObject MakeRowShell(string name, float indent)
        {
            var go = UIFactory.CreateUI(name, _ui.ListContent);
            var bg = go.AddComponent<Image>();
            bg.color = indent > 0f ? UITheme.BG_SURFACE : UITheme.SLOT_BG;

            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.padding = new RectOffset(6 + (int)indent, 6, 3, 3);
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleHeight = 0f;
            return go;
        }

        /// <summary>
        /// The "..." (assign) and "x" (clear) pair. <paramref name="ordinal"/> is -1 for an
        /// action the asset gives no bindable slot at all, where both are dead.
        /// </summary>
        private void AddSlotButtons(Transform parent, InputActionDescriptor d, InputAction action,
                                    List<InputBindingSlot> slots, int ordinal)
        {
            bool rebindable = d.Rebindable && ordinal >= 0 && action != null;

            var assign = SmallButton(parent, "...", 28f, () => BeginCapture(d, ordinal));
            assign.interactable = rebindable;
            UIHoverText.Attach(assign.gameObject, _ui.Status, rebindable
                ? $"Asignar una tecla o un boton del raton a «{d.DisplayName}»."
                : InputContextPolicy.Explain(InputContextPolicy.EvaluateRebind(d)));

            bool clearable = rebindable && ordinal < slots.Count && slots[ordinal].IsBound;
            var clear = SmallButton(parent, "x", 22f, () => ClearBinding(d, ordinal));
            clear.interactable = clearable;
            UIHoverText.Attach(clear.gameObject, _ui.Status,
                $"Dejar «{d.DisplayName}» sin tecla. Sigue en la lista, para poder devolversela.");
        }

        private void ToggleExpanded(InputActionDescriptor d)
        {
            if (!_expanded.Remove(d.Id)) _expanded.Add(d.Id);
            ApplyFilter();
        }

        /// <summary>
        /// The chip on the main row. One binding prints its key; several print the first and a
        /// count, because the joined form ("W S A D Arriba Abajo Izq. Der.") is 42 characters
        /// in a 118 px chip and arrives as an ellipsis that says nothing at all.
        /// </summary>
        private static string ChipTextFor(List<InputBindingSlot> slots)
        {
            if (slots.Count == 0) return "sin binding";
            if (slots.Count == 1) return slots[0].Label;

            int bound = 0;
            foreach (var s in slots) if (s.IsBound) bound++;
            return bound == 0 ? "sin asignar" : $"{slots[0].Label}  +{slots.Count - 1}";
        }

        /// <summary>
        /// <c>Object.Destroy</c> is an outright ERROR in Edit Mode, not a warning, and it is
        /// the same trap the Entities and Buildings editors already carry this branch for. It
        /// matters here because the row list is rebuilt from an EditMode fixture on every
        /// context switch and every rebind — seven tests went red on the log line alone.
        /// </summary>
        private static void DestroySafely(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else                       DestroyImmediate(go);
        }

        private GameObject BuildEmptyNotice()
        {
            var go = UIFactory.CreateUI("Empty", _ui.ListContent);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 40f;
            le.flexibleHeight = 0f;
            AddText(go.transform, "", 11f, UITheme.TEXT_MUTED, flexibleWidth: 1f);
            return go;
        }

        // ── Context chips ────────────────────────────────────────────────────

        /// <summary>
        /// The posture chips, for the gameplay tabs only.
        ///
        /// <para>THREE STATES, AND THE THIRD IS WHY THIS IS NOT A PAIR OF TOGGLES. A chip is
        /// ON, OFF, or LOCKED — and a locked chip is drawn differently and does nothing, rather
        /// than being drawn as a toggle that refuses. Four actions are locked because switching
        /// them off is a soft lock (walking, aiming, the dash, the stance toggle), and the Peace
        /// half of every damage action is locked because Peace is a safe posture rather than a
        /// second key layout. The version that shipped drew an interactive chip on eight
        /// actions of which six had no reader at all, so the panel reported a change it could
        /// not make.</para>
        ///
        /// <para>An editor context gets no chips: what decides whether an editor's tool is live
        /// is which editor is open, and a chip that could only ever be on is a control that
        /// does nothing.</para>
        /// </summary>
        private void AddContextChips(Transform parent, InputActionDescriptor d)
        {
            if (!InputContexts.IsGameplay(_viewContext)) return;
            if (d.Map != InputActionCatalog.MapGameplay) return;

            // A locked action draws the SAME TWO CHIPS, both in the refused state, rather than a
            // single glyph standing in for them. The glyph was one more symbol to learn, and it
            // hid the thing worth reading: that walking lives in both postures and will go on
            // doing so. Chip() already renders and explains a refusal, so there is no branch.
            var mask = InputContextPolicy.ContextsOf(d);
            Chip(parent, "G", d, InputContextMask.War, mask);
            Chip(parent, "P", d, InputContextMask.Peace, mask);
        }

        private void Chip(Transform parent, string label, InputActionDescriptor d,
                          InputContextMask bit, InputContextMask mask)
        {
            bool on = (mask & bit) != 0;

            // Whether TOGGLING is allowed, which is the only question a toggle can ask. Testing
            // "either direction is allowed" instead would light the Peace chip on all 24 spell
            // slots — turning it OFF is legal there, and it is already off — so the panel would
            // show an interactive control whose only possible action is refused.
            bool allowed = InputContextPolicy.Evaluate(d, mask ^ bit) == InputAssignmentVerdict.Allowed;

            var btn = SmallButton(parent, label, 22f, allowed ? (Action)(() => ToggleContextBit(d, bit)) : null);
            btn.interactable = allowed;

            var img = btn.GetComponent<Image>();
            if (img != null)
                img.color = !allowed ? UITheme.DANGER_IDLE : on ? UITheme.BTN_ACTIVE : UITheme.BTN_NORMAL;

            string where = bit == InputContextMask.War ? "Guerra" : "Paz";
            UIHoverText.Attach(btn.gameObject, _ui.Status, allowed
                ? $"«{d.DisplayName}» {(on ? "esta" : "no esta")} viva en {where}. Click para cambiarlo."
                : $"«{d.DisplayName}» no puede vivir en {where}. " +
                  InputContextPolicy.Explain(InputContextPolicy.Evaluate(d, mask ^ bit)));
        }

        private void ToggleContextBit(InputActionDescriptor d, InputContextMask bit)
        {
            var before = InputContextPolicy.ContextsOf(d);
            var next = before ^ bit;
            var verdict = InputContextPolicy.SetContexts(d, next);
            if (verdict != InputAssignmentVerdict.Allowed)
            {
                SetStatus(InputContextPolicy.Explain(verdict));
                return;
            }

            PushEdit(new ControlsEdit
            {
                Label = $"{d.DisplayName} en {Describe(next)}",
                ActionId = d.Id,
                BindingIndex = -1,
                MaskBefore = before,
                MaskAfter = next,
            });
            InputBindingStore.MarkDirty();
            RebuildActionList();
            RepaintAll();
            SetStatus($"{d.DisplayName}: ahora vive en {Describe(InputContextPolicy.ContextsOf(d))}.");
        }

        private static string Describe(InputContextMask mask)
        {
            var play = mask & InputContextMask.Gameplay;
            return play switch
            {
                InputContextMask.Gameplay => "Guerra y Paz",
                InputContextMask.War      => "Guerra",
                InputContextMask.Peace    => "Paz",
                _                         => "ninguna postura (silenciada)",
            };
        }

        // ── Capture ──────────────────────────────────────────────────────────

        /// <summary>
        /// Starts "press a key" for one SLOT of one action.
        ///
        /// <para>Deliberately NOT Unity's
        /// <c>InputActionRebindingExtensions.PerformInteractiveRebinding</c>: that listens to
        /// raw devices, so it happily captures a control this project cannot express as an
        /// <see cref="InputControlEntry"/> — and a binding whose legacy half resolves to
        /// <see cref="KeyCode.None"/> works in the editor and dies the first time the 2022.3
        /// event-drop bug fires.</para>
        /// </summary>
        private void BeginCapture(InputActionDescriptor d, int ordinal)
        {
            var verdict = InputContextPolicy.EvaluateRebind(d);
            if (verdict != InputAssignmentVerdict.Allowed)
            {
                SetStatus(InputContextPolicy.Explain(verdict));
                return;
            }

            _capturing = d;
            _captureBindingIndex = ordinal;

            // Escape cancels the capture and must not ALSO reach the General Editor, which
            // reads the same press in the same frame and would close this editor out from
            // under the author. Update order between the two is undefined, so the claim is the
            // only thing that makes the outcome deterministic.
            EscapeOwnership.Claim(this);

            ControlsEditorUIBuilder.SetCaptureVisible(_ui, true,
                $"Pulsa la tecla para «{d.DisplayName}».\n" +
                "Tambien vale una tecla del teclado dibujado, un boton del raton dibujado, " +
                "o el boton derecho / central / la rueda directamente.\n" +
                "Click fuera o Escape para cancelar.");
        }

        internal void CancelCapture()
        {
            _capturing = null;
            _captureBindingIndex = -1;
            EscapeOwnership.Release(this);
            ControlsEditorUIBuilder.SetCaptureVisible(_ui, false);
        }

        /// <summary>
        /// Polls for a real press while capturing — both backends, through the centralized
        /// helpers.
        ///
        /// <para>It used to read <c>Keyboard.current</c> directly, which is a raw device read
        /// of exactly the kind this project bans, and it cost more than tidiness: the raw
        /// InputSystem half is the one that DIES under the 2022.3 event-drop bug, so a capture
        /// could stop answering in the very session where the player had gone looking for the
        /// Controls editor because their keys had stopped working.
        /// <see cref="KeyboardInputManager"/> ORs the legacy backend and honours
        /// <see cref="InputBlocker"/>, which is also the reason a capture cannot fire while the
        /// chat or the console holds focus.</para>
        ///
        /// <para>THE LEFT MOUSE BUTTON IS NOT POLLED, on purpose: it is how the author clicks
        /// the drawn board, so polling it would bind LMB to whatever they were trying to point
        /// at. Left click reaches this through the drawn mouse's own left-button cap, which is
        /// the one place where clicking it MEANS "the left button".</para>
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

            bool chordSlot = CapturingChordModifier() != null;
            foreach (var entry in InputControlPaths.Entries)
            {
                if (entry.Key == Key.Escape) continue;   // handled above, as the cancel
                // On a chord slot the modifier is fixed, so the author naturally presses
                // Shift first — capturing that would bind the chord's key to Shift itself.
                if (chordSlot && IsModifierKey(entry.Key)) continue;
                if (!KeyboardInputManager.WasKeyPressedThisFrame(entry.Key, entry.Legacy)) continue;
                CompleteCaptureWithPath(entry.Path);
                return;
            }

            if (MouseInputManager.WasRightMouseButtonPressedThisFrame())
            { CompleteCaptureWithMouse(MouseControl.Right); return; }

            if (MouseInputManager.WasMiddleMouseButtonPressedThisFrame())
            { CompleteCaptureWithMouse(MouseControl.Middle); return; }

            float wheel = MouseInputManager.GetMouseWheelDelta();
            if (wheel > 0f) { CompleteCaptureWithMouse(MouseControl.WheelUp); return; }
            if (wheel < 0f) { CompleteCaptureWithMouse(MouseControl.WheelDown); return; }
        }

        private void CompleteCaptureWithMouse(MouseControl control) =>
            CompleteCaptureWithPath(InputControlPaths.PathForMouse(control));

        private static bool IsModifierKey(Key key) =>
            key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LeftCtrl  || key == Key.RightCtrl  ||
            key == Key.LeftAlt   || key == Key.RightAlt;

        /// <summary>The modifier path of the slot being captured, or null when it is not a chord.</summary>
        private string CapturingChordModifier()
        {
            if (_capturing == null) return null;
            var action = ResolveAction(InputService.Instance, _capturing);
            var slots = InputChord.Slots(action);
            if (_captureBindingIndex < 0 || _captureBindingIndex >= slots.Count) return null;
            var slot = slots[_captureBindingIndex];
            return slot.IsChord ? slot.ModifierPath : null;
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

            int index = ResolveBindingIndex(action, _captureBindingIndex);
            if (index < 0)
            {
                SetStatus($"'{d.DisplayName}' no tiene ningun binding que reasignar.");
                CancelCapture();
                return;
            }

            // A chord keeps its modifier: the override lands on the KEY, and what the player
            // now presses — and what may clash — is the chord, not the bare key.
            string modifier = CapturingChordModifier();
            string pressed = string.IsNullOrEmpty(modifier) ? path : InputChord.Compose(modifier, path);

            ApplyOverride(d, action, index, path, $"{d.DisplayName} -> {InputControlPaths.LabelForPath(pressed)}");
            CancelCapture();
            RebuildActionList();
            RepaintAll();

            string label = InputControlPaths.LabelForPath(pressed);
            var live = LiveOn(pressed);
            var severity = InputConflictScanner.Classify(live);
            SetStatus(severity >= InputClashSeverity.Modifier
                ? $"{d.DisplayName} -> {label}. OJO: {SubtitleFor(live)} responden a esa tecla aqui."
                : $"{d.DisplayName} -> {label}. Ctrl+S para guardar.");
        }

        /// <summary>
        /// Drops a binding without dropping the action, by overriding its path with the empty
        /// string — the InputSystem's own way of saying "no control".
        ///
        /// <para>The action stays in the list, silenced-looking rather than gone, which is what
        /// makes clearing a key a decision the player can take back. It is also how the
        /// fourteen retired editor toggles reach a key at all: they ship with an empty binding
        /// for exactly this reason.</para>
        /// </summary>
        private void ClearBinding(InputActionDescriptor d, int ordinal)
        {
            var action = ResolveAction(InputService.Instance, d);
            int index = ResolveBindingIndex(action, ordinal);
            if (action == null || index < 0) return;

            ApplyOverride(d, action, index, "", $"{d.DisplayName} sin tecla");
            RebuildActionList();
            RepaintAll();
            SetStatus($"{d.DisplayName}: sin tecla. Sigue en la lista para poder devolversela.");
        }

        /// <summary>
        /// Writes a binding override and records it so Ctrl+Z can take it back.
        ///
        /// <para>The BEFORE value is the binding's <c>overridePath</c>, not its
        /// <c>effectivePath</c>, and the difference is the whole correctness of undo: null
        /// means "no override, use the asset's own path" while the empty string means "cleared".
        /// Recording the effective path would turn every undo of a first edit into a hard-coded
        /// override of the shipped key — indistinguishable on screen, and it would survive a
        /// reset-to-defaults of everything else.</para>
        /// </summary>
        private void ApplyOverride(InputActionDescriptor d, InputAction action, int index,
                                   string path, string label)
        {
            var edit = new ControlsEdit
            {
                Label = label,
                ActionId = d.Id,
                BindingIndex = index,
                PathBefore = OverridePathOf(action, index),
                PathAfter = path,
            };

            InputActionRebindingExtensions.ApplyBindingOverride(action, index, path);
            InputBindingResolver.Invalidate();
            InputBindingStore.MarkDirty();
            PushEdit(edit);
        }

        /// <summary>
        /// Turns a slot ORDINAL — what the row shows — into an index into
        /// <c>action.bindings</c>, which is what <c>ApplyBindingOverride</c> takes.
        ///
        /// <para>Composite headers name no control and must be skipped, or an override lands on
        /// the "2DVector" row and moves nothing while reporting success. The ordinal is what
        /// makes a multi-control action rebindable at all: the shipped code hardcoded slot 0,
        /// so Move offered eight keys and could only ever move the W.</para>
        /// </summary>
        private static int ResolveBindingIndex(InputAction action, int ordinal)
        {
            if (action == null || ordinal < 0) return -1;

            // The same walk SlotsOf uses, so ordinal N here is row N there — a chord is one slot
            // in both, and an override lands on its KEY rather than on its modifier.
            var slots = InputChord.Slots(action);
            return ordinal < slots.Count ? slots[ordinal].Index : -1;
        }

        private static InputAction ResolveAction(InputService svc, InputActionDescriptor d)
        {
            var map = svc?.Asset?.FindActionMap(d.Map, throwIfNotFound: false);
            return map?.FindAction(d.Action, throwIfNotFound: false);
        }

        // ── Row primitives ───────────────────────────────────────────────────

        private Button SmallButton(Transform parent, string label, float width, Action onClick)
        {
            var btn = EditorUIHelpers.MakeButton(parent, label, () => onClick?.Invoke(), 20f, 10f);
            var le = btn.gameObject.GetComponent<LayoutElement>()
                  ?? btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.flexibleWidth = 0f;
            return btn;
        }

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
