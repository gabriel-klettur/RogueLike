using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.UI;

namespace Valkur.UI.PauseMenu
{
    public partial class PauseMenuUI
    {
        // ── Keybindings panel builder ────────────────────────────────────────

        private GameObject BuildInputsPanel(Transform parent)
        {
            const float panelW = 740f;
            const float panelH = 500f;

            var panel = CreateUIObject("InputsPanel", parent);
            var r     = panel.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0.5f, 0.5f); r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f); r.anchoredPosition = Vector2.zero;
            r.sizeDelta = new Vector2(panelW, panelH);
            panel.AddComponent<Image>().color = PanelBg;

            AddPanelTitle(panel.transform, "Controls Settings", panelH, 20f);

            var tabs = new[] { "General", "Movement", "Spells", "Editors" };
            _tabLabels = new TextMeshProUGUI[tabs.Length];
            for (int t = 0; t < tabs.Length; t++)
            {
                var tGo = CreateUIObject($"Tab_{t}", panel.transform);
                var tR  = tGo.GetComponent<RectTransform>();
                tR.anchorMin = new Vector2(t / (float)tabs.Length, 1f);
                tR.anchorMax = new Vector2((t + 1) / (float)tabs.Length, 1f);
                tR.pivot = new Vector2(0.5f, 1f);
                tR.anchoredPosition = new Vector2(0f, -52f);
                tR.sizeDelta = new Vector2(0f, 36f);
                var img = tGo.AddComponent<Image>(); img.color = new Color(0.14f, 0.14f, 0.18f, 1f);

                int cap = t;
                var btn = tGo.AddComponent<Button>(); btn.targetGraphic = img;
                btn.onClick.AddListener(() => { _inputsTabSel = cap; UpdateInputsPanel(); });

                // Text as child GO (Image + TMP on same GO causes NPE)
                var txtGo = CreateUIObject($"TabLabel_{t}", tGo.transform);
                var txtR  = txtGo.GetComponent<RectTransform>();
                txtR.anchorMin = Vector2.zero; txtR.anchorMax = Vector2.one;
                txtR.sizeDelta = Vector2.zero; txtR.anchoredPosition = Vector2.zero;
                var tmp = txtGo.AddComponent<TextMeshProUGUI>();
                tmp.text = tabs[t]; tmp.fontSize = 18f;
                tmp.alignment = TextAlignmentOptions.Center; tmp.color = TextNormal;
                tmp.raycastTarget = false;
                _tabLabels[t] = tmp;
            }

            // ── Tab 0: General ───────────────────────────────────────────────
            {
                var container = CreateUIObject("TabContent_0", panel.transform);
                StretchFull(container);
                var rows = new (string label, string actionA, string actionB, string actionMouse)[]
                {
                    ("Pause",     "pause",            "-", "-"),
                    ("Inventory", "toggle_inventory", "-", "-"),
                };
                BuildStandardRows(container.transform, rows);
            }

            // ── Tab 1: Movement ──────────────────────────────────────────────
            {
                var container = CreateUIObject("TabContent_1", panel.transform);
                StretchFull(container);
                var rows = new (string label, string actionA, string actionB, string actionMouse)[]
                {
                    ("Up",    "move_up",    "move_up",    "-"),
                    ("Down",  "move_down",  "move_down",  "-"),
                    ("Left",  "move_left",  "move_left",  "-"),
                    ("Right", "move_right", "move_right", "-"),
                    ("Dash",  "dash",       "dash",       "-"),
                };
                BuildStandardRows(container.transform, rows);
            }

            // ── Tab 2: Spells ────────────────────────────────────────────────
            {
                var container = CreateUIObject("TabContent_2", panel.transform);
                StretchFull(container);
                var rows = new (string label, string actionA, string actionB, string actionMouse)[]
                {
                    ("Spell 1", "spell_1", "-", "attack_primary_mouse"),
                    ("Spell 2", "spell_2", "-", "attack_secondary_mouse"),
                    ("Spell 3", "spell_3", "-", "-"),
                    ("Spell 4", "spell_4", "-", "-"),
                };
                BuildStandardRows(container.transform, rows);
            }

            // ── Tab 3: Editors (sub-tabs) ────────────────────────────────────
            {
                var container = CreateUIObject("TabContent_3", panel.transform);
                StretchFull(container);
                // Secondary tab strip with one sub-tab per editor.
                // _editorSubTabSel and _editorSubTabLabels are partial-class fields
                // on PauseMenuUI (defined in PauseMenuUI.cs).
                BuildEditorSubTabs(container.transform, "ESubTab", "ESubContent");
            }

            AddHint(panel.transform, "Click on a key to rebind  |  Esc to cancel  |  Q / E Change tab", panelH);
            return panel;
        }

        /// <summary>
        /// Builds a standard row group (label + Key A + Key B + Mouse columns).
        /// Shared by General, Movement and Spells tabs.
        /// </summary>
        private void BuildStandardRows(Transform container,
            (string label, string actionA, string actionB, string actionMouse)[] rows)
        {
            const float rowH = 36f; const float gap = 6f; const float startY = -100f;
            const float col0 = 16f, col1 = 0.38f, col2 = 0.58f, col3 = 0.78f;

            for (int i = 0; i < rows.Length; i++)
            {
                float cy  = startY - i * (rowH + gap);
                var row   = CreateUIObject($"Row_{i}", container);
                var rowR  = row.GetComponent<RectTransform>();
                rowR.anchorMin = Vector2.up; rowR.anchorMax = new Vector2(1f, 1f);
                rowR.pivot = new Vector2(0f, 0.5f);
                rowR.anchoredPosition = new Vector2(col0, cy);
                rowR.sizeDelta = new Vector2(-col0 * 2, rowH);

                AddTableCell(row.transform, rows[i].label, TextAlignmentOptions.Left, 0f, 0.35f);
                AddRebindCell(row.transform, rows[i].actionA,     0, col1, 0.18f, "Key A");
                AddRebindCell(row.transform, rows[i].actionB,     1, col2, 0.18f, "Key B");
                AddRebindCell(row.transform, rows[i].actionMouse, 0, col3, 0.22f, "Mouse");
            }
        }

        /// <summary>
        /// Builds the 12-editor secondary tab strip inside the "Editors" main tab.
        /// Each sub-tab shows:
        ///   • one rebindable toggle-key row (or a read-only fixed row for Lighting Ctrl+F3).
        ///   • read-only rows for the editor's in-editor shortcuts (Ctrl+Z/Y/S, Esc).
        ///
        /// NOTE: The in-editor shortcuts (Ctrl+Z, Ctrl+Y, Ctrl+S, Esc) are displayed
        ///       as read-only because they are hardcoded in each editor's
        ///       KeyboardInputManager calls.  Making them rebindable would require
        ///       migrating every editor to InputAction-backed lookups — deferred.
        /// </summary>
        private void BuildEditorSubTabs(Transform container,
            string subTabPrefix, string contentPrefix)
        {
            var editors = EditorSubTabData.All;

            // ── Sub-tab strip (2 rows × 6) ───────────────────────────────────
            // Row A: Particles, Time&Weather, Spawners, Lighting, Spells, Entities
            // Row B: Inventory, Items, Tile, Buildings, Map, FSM
            const float stripY0 = -92f;
            const float stripY1 = -124f;
            const float subTabH = 28f;
            _editorSubTabLabels = new TextMeshProUGUI[editors.Length];

            for (int i = 0; i < editors.Length; i++)
            {
                int col = i % 6;
                int row = i / 6;
                float stripY = row == 0 ? stripY0 : stripY1;

                var tabGo = CreateUIObject($"{subTabPrefix}_{i}", container);
                var tabR  = tabGo.GetComponent<RectTransform>();
                tabR.anchorMin = new Vector2(col / 6f, 1f);
                tabR.anchorMax = new Vector2((col + 1) / 6f, 1f);
                tabR.pivot = new Vector2(0.5f, 1f);
                tabR.anchoredPosition = new Vector2(0f, stripY);
                tabR.sizeDelta = new Vector2(-2f, subTabH);
                var tabImg = tabGo.AddComponent<Image>();
                tabImg.color = new Color(0.10f, 0.10f, 0.14f, 1f);

                int cap = i;
                var tabBtn = tabGo.AddComponent<Button>(); tabBtn.targetGraphic = tabImg;
                tabBtn.onClick.AddListener(() =>
                {
                    _editorSubTabSel = cap;
                    RefreshEditorSubTabVisuals(container, contentPrefix);
                });

                var txtGo = CreateUIObject($"{subTabPrefix}Label_{i}", tabGo.transform);
                var txtR  = txtGo.GetComponent<RectTransform>();
                txtR.anchorMin = Vector2.zero; txtR.anchorMax = Vector2.one;
                txtR.sizeDelta = Vector2.zero; txtR.anchoredPosition = Vector2.zero;
                var tmp = txtGo.AddComponent<TextMeshProUGUI>();
                tmp.text = editors[i].ShortLabel;
                tmp.fontSize = 11f;
                tmp.alignment = TextAlignmentOptions.Center; tmp.color = TextNormal;
                tmp.raycastTarget = false;
                tmp.enableWordWrapping = false;
                _editorSubTabLabels[i] = tmp;
            }

            // ── Per-editor content areas ─────────────────────────────────────
            const float contentStartY = -160f;
            const float rowH = 34f; const float rowGap = 4f;
            const float padX = 16f;

            for (int i = 0; i < editors.Length; i++)
            {
                var ed = editors[i];
                var content = CreateUIObject($"{contentPrefix}_{i}", container);
                StretchFull(content);

                int rowIdx = 0;

                // Toggle key row
                float cy = contentStartY - rowIdx * (rowH + rowGap);
                var toggleRow = CreateUIObject("ToggleRow", content.transform);
                SetInputRowRect(toggleRow, cy, rowH, padX);

                AddTableCell(toggleRow.transform, ed.ToggleLabel, TextAlignmentOptions.Left, 0f, 0.55f);
                if (ed.IsFixedBinding)
                {
                    // Lighting Ctrl+F3: render as read-only — the Ctrl modifier is hardcoded
                    // in LightingRuntimeEditor.cs and is not managed by EditorBindingsApplier.
                    AddReadOnlyCell(toggleRow.transform, "Ctrl + F3 (fixed)", 0.58f, 0.42f);
                }
                else
                {
                    AddRebindCell(toggleRow.transform, ed.ActionName, 0, 0.58f, 0.22f, "Key A");
                }
                rowIdx++;

                if (ed.HasUndo)
                {
                    cy = contentStartY - rowIdx * (rowH + rowGap);
                    AddReadOnlyShortcutRow(content.transform, "Undo", "Ctrl + Z  (shared)", cy, rowH, padX);
                    rowIdx++;
                    cy = contentStartY - rowIdx * (rowH + rowGap);
                    AddReadOnlyShortcutRow(content.transform, "Redo", "Ctrl + Y  (shared)", cy, rowH, padX);
                    rowIdx++;
                }

                if (ed.HasSave)
                {
                    cy = contentStartY - rowIdx * (rowH + rowGap);
                    AddReadOnlyShortcutRow(content.transform, "Save", "Ctrl + S  (shared)", cy, rowH, padX);
                    rowIdx++;
                }

                if (ed.HasEsc)
                {
                    cy = contentStartY - rowIdx * (rowH + rowGap);
                    AddReadOnlyShortcutRow(content.transform, "Close / Cancel", "Esc  (shared)", cy, rowH, padX);
                    rowIdx++;
                }

                cy = contentStartY - rowIdx * (rowH + rowGap);
                AddReadOnlyShortcutRow(content.transform, "Pan Camera", "MMB drag  (shared)", cy, rowH, padX);
            }

            RefreshEditorSubTabVisuals(container, contentPrefix);
        }

        // ── Editor sub-tab visual refresh ─────────────────────────────────────

        private void RefreshEditorSubTabVisuals(Transform container, string contentPrefix)
        {
            if (_editorSubTabLabels == null) return;
            for (int i = 0; i < _editorSubTabLabels.Length; i++)
            {
                if (_editorSubTabLabels[i] != null)
                    _editorSubTabLabels[i].color = i == _editorSubTabSel ? TextSelected : TextNormal;

                var c = container.Find($"{contentPrefix}_{i}");
                if (c != null) c.gameObject.SetActive(i == _editorSubTabSel);
            }
        }

        // ── Read-only cell helpers ────────────────────────────────────────────

        /// <summary>
        /// Adds a dimmed read-only cell (no button, no rebind handler).
        /// Used for the Lighting fixed binding and in-editor shortcut display rows.
        /// </summary>
        private void AddReadOnlyCell(Transform parent, string value,
            float anchorLeft, float width)
        {
            var go = CreateUIObject("ReadOnly_" + value.Replace(" ", "_").Replace("+", "Plus"), parent);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(anchorLeft, 0f);
            r.anchorMax = new Vector2(anchorLeft + width, 1f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = new Vector2(4f, 6f); r.offsetMax = new Vector2(-4f, -6f);

            var img = go.AddComponent<Image>();
            img.color = new Color(0.09f, 0.09f, 0.12f, 1f);   // dimmer than interactive cells

            var textGo = CreateUIObject("Label", go.transform);
            var tr = textGo.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.sizeDelta = Vector2.zero;
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = value;
            tmp.fontSize = 13f;
            tmp.color = new Color(0.55f, 0.55f, 0.60f, 1f);    // dim text — not interactive
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
        }

        /// <summary>
        /// Adds a full-width read-only shortcut row inside an editor content area.
        /// </summary>
        private void AddReadOnlyShortcutRow(Transform container, string label, string shortcut,
            float cy, float rowH, float padX)
        {
            var row = CreateUIObject($"Shortcut_{label.Replace(" ", "_").Replace("/", "_")}", container);
            SetInputRowRect(row, cy, rowH, padX);

            AddTableCell(row.transform, label, TextAlignmentOptions.Left, 0f, 0.55f);
            AddReadOnlyCell(row.transform, shortcut, 0.58f, 0.42f);
        }

        private void SetInputRowRect(GameObject go, float cy, float rowH, float padX)
        {
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = Vector2.up; r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = new Vector2(padX, cy);
            r.sizeDelta = new Vector2(-padX * 2, rowH);
        }

        /// <summary>
        /// Adds a clickable cell that, on click, triggers an interactive rebind for the given action slot.
        /// When action is "-", renders a static "-" cell.
        /// </summary>
        private void AddRebindCell(Transform parent, string action, int slotIndex,
            float anchorLeft, float width, string prefix)
        {
            // If there's no action for this column, render static dash.
            if (string.IsNullOrEmpty(action) || action == "-")
            {
                AddTableCell(parent, $"{prefix}: -", TextAlignmentOptions.Left, anchorLeft, width);
                return;
            }

            var gs = GameSettings.Instance;
            string current = gs != null ? GameSettingsBindings.Get(gs, action, slotIndex) : "";
            if (string.IsNullOrEmpty(current)) current = "-";

            // Button-backed cell
            var go = CreateUIObject("RebindCell_" + action + "_" + slotIndex, parent);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(anchorLeft, 0f);
            r.anchorMax = new Vector2(anchorLeft + width, 1f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = new Vector2(4f, 6f); r.offsetMax = new Vector2(-4f, -6f);

            var img = go.AddComponent<Image>();
            img.color = new Color(0.12f, 0.12f, 0.18f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            // Label as child GO (TMP+Image on same GO is the documented gotcha)
            var textGo = CreateUIObject("Label", go.transform);
            var tr = textGo.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.sizeDelta = Vector2.zero;
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = $"{prefix}: {current}";
            tmp.fontSize = 14f; tmp.color = TextNormal;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;

            string capturedAction = action; int capturedSlot = slotIndex; string capturedPrefix = prefix;
            btn.onClick.AddListener(() => StartRebind(capturedAction, capturedSlot, capturedPrefix, tmp, img));
        }

        private KeyRebinder _rebinder;

        private void StartRebind(string action, int slotIndex, string prefix,
            TextMeshProUGUI label, Image cellBg)
        {
            if (_rebinder != null && _rebinder.IsActive) return;

            var origColor = cellBg.color;
            var origText = label.text;
            cellBg.color = new Color(0.55f, 0.45f, 0.15f, 1f);
            label.text = $"{prefix}: <i>Press any key...</i>";

            _rebinder?.Dispose();
            _rebinder = new KeyRebinder();
            _rebinder.Completed += captured =>
            {
                var gs = GameSettings.Instance;
                if (gs != null)
                {
                    GameSettingsBindings.Set(gs, action, slotIndex, captured);
                    gs.Save();
                    // Re-apply editor toggle overrides so the new key is live immediately.
                    EditorBindingsApplier.ReapplyAll();
                }
                label.text = $"{prefix}: {captured}";
                cellBg.color = origColor;
                _rebinder?.Dispose();
                _rebinder = null;
            };
            _rebinder.Cancelled += () =>
            {
                label.text = origText;
                cellBg.color = origColor;
                _rebinder?.Dispose();
                _rebinder = null;
            };
            _rebinder.Start();
        }

        // ── Inputs panel input ───────────────────────────────────────────────

        private void HandleInputsTabInput()
        {
            if (_rebinder != null && _rebinder.IsActive)
            {
                if (Valkur.Core.Input.KeyboardInputManager.WasEscapePressedThisFrame())
                    _rebinder.Cancel();
                return;
            }

            int tabCount = _tabLabels != null ? _tabLabels.Length : 0;
            bool tabLeft  = Valkur.Core.Input.KeyboardInputManager.WasQPressedThisFrame();
            bool tabRight = Valkur.Core.Input.KeyboardInputManager.WasEPressedThisFrame();

            if (tabLeft && tabCount > 0)
            { _inputsTabSel = (_inputsTabSel - 1 + tabCount) % tabCount; UpdateInputsPanel(); }
            else if (tabRight && tabCount > 0)
            { _inputsTabSel = (_inputsTabSel + 1) % tabCount; UpdateInputsPanel(); }
            else if ((_cancel != null && _cancel.WasPerformedThisFrame())
                  || Valkur.Core.Input.InputCompat.CancelPressed())
            { GoBack(); }
        }

        private void UpdateInputsPanel()
        {
            if (_tabLabels == null || _inputsPanel == null) return;
            for (int i = 0; i < _tabLabels.Length; i++)
            {
                if (_tabLabels[i] != null)
                    _tabLabels[i].color = i == _inputsTabSel ? TextSelected : TextNormal;

                var container = _inputsPanel.transform.Find($"TabContent_{i}");
                if (container != null) container.gameObject.SetActive(i == _inputsTabSel);
            }

            // When switching to the Editors tab, refresh editor sub-tab visuals.
            if (_inputsTabSel == 3)
            {
                var editorsContainer = _inputsPanel.transform.Find("TabContent_3");
                if (editorsContainer != null)
                    RefreshEditorSubTabVisuals(editorsContainer, "ESubContent");
            }
        }
    }
}
