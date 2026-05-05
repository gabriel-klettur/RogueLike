using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.UI;

namespace Valkur.UI.MainMenu
{
    public partial class MainMenuUI
    {

        private void BuildOptInputsPanel(Transform parent)
        {
            const float panelW = 740f;
            const float panelH = 500f;

            _optInputsPanel = CreateUIObject("OptInputsPanel", parent);
            var r = _optInputsPanel.GetComponent<RectTransform>();
            // Anchored below the ROGUELIKE 1.0 logo (logo bottom = -260 from canvas top).
            r.anchorMin = new Vector2(0.5f, 1f); r.anchorMax = new Vector2(0.5f, 1f);
            r.pivot = new Vector2(0.5f, 1f); r.anchoredPosition = new Vector2(0f, -280f);
            r.sizeDelta = new Vector2(panelW, panelH);
            _optInputsPanel.AddComponent<Image>().color = PanelBg;

            AddOptPanelTitle(_optInputsPanel.transform, "Controls Settings");

            var tabs = new[] { "General", "Movement", "Spells", "Editors" };
            _optTabLabels = new TextMeshProUGUI[tabs.Length];
            for (int t = 0; t < tabs.Length; t++)
            {
                var tGo = CreateUIObject($"OTab_{t}", _optInputsPanel.transform);
                var tR  = tGo.GetComponent<RectTransform>();
                tR.anchorMin = new Vector2(t / (float)tabs.Length, 1f);
                tR.anchorMax = new Vector2((t + 1) / (float)tabs.Length, 1f);
                tR.pivot = new Vector2(0.5f, 1f);
                tR.anchoredPosition = new Vector2(0f, -52f);
                tR.sizeDelta = new Vector2(0f, 36f);
                var img = tGo.AddComponent<Image>(); img.color = new Color(0.14f, 0.14f, 0.18f, 1f);

                int cap = t;
                var btn = tGo.AddComponent<Button>(); btn.targetGraphic = img;
                btn.onClick.AddListener(() => { _optInputsTabSel = cap; UpdateOptInputsPanel(); });

                // Text as child (Image + TMP on same GO causes NPE)
                var txtGo = CreateUIObject($"OTabLabel_{t}", tGo.transform);
                var txtR  = txtGo.GetComponent<RectTransform>();
                txtR.anchorMin = Vector2.zero; txtR.anchorMax = Vector2.one;
                txtR.sizeDelta = Vector2.zero; txtR.anchoredPosition = Vector2.zero;
                var tmp = txtGo.AddComponent<TextMeshProUGUI>();
                tmp.text = tabs[t]; tmp.fontSize = 18f;
                tmp.alignment = TextAlignmentOptions.Center; tmp.color = TextNormal;
                tmp.raycastTarget = false;
                _optTabLabels[t] = tmp;
            }

            // ── Tab 0: General ───────────────────────────────────────────────
            {
                var container = CreateUIObject("OTabContent_0", _optInputsPanel.transform);
                StretchFull(container);
                var rows = new (string label, string actionA, string actionB, string actionMouse)[]
                {
                    ("Pause",     "pause",            "-", "-"),
                    ("Inventory", "toggle_inventory", "-", "-"),
                };
                BuildOptStandardRows(container.transform, rows);
            }

            // ── Tab 1: Movement ──────────────────────────────────────────────
            {
                var container = CreateUIObject("OTabContent_1", _optInputsPanel.transform);
                StretchFull(container);
                var rows = new (string label, string actionA, string actionB, string actionMouse)[]
                {
                    ("Up",    "move_up",    "move_up",    "-"),
                    ("Down",  "move_down",  "move_down",  "-"),
                    ("Left",  "move_left",  "move_left",  "-"),
                    ("Right", "move_right", "move_right", "-"),
                    ("Dash",  "dash",       "dash",       "-"),
                };
                BuildOptStandardRows(container.transform, rows);
            }

            // ── Tab 2: Spells ────────────────────────────────────────────────
            {
                var container = CreateUIObject("OTabContent_2", _optInputsPanel.transform);
                StretchFull(container);
                var rows = new (string label, string actionA, string actionB, string actionMouse)[]
                {
                    ("Spell 1", "spell_1", "-", "attack_primary_mouse"),
                    ("Spell 2", "spell_2", "-", "attack_secondary_mouse"),
                    ("Spell 3", "spell_3", "-", "-"),
                    ("Spell 4", "spell_4", "-", "-"),
                };
                BuildOptStandardRows(container.transform, rows);
            }

            // ── Tab 3: Editors (sub-tabs) ────────────────────────────────────
            {
                var container = CreateUIObject("OTabContent_3", _optInputsPanel.transform);
                StretchFull(container);
                // Secondary tab strip with one sub-tab per editor.
                // _optEditorSubTabSel and _optEditorSubTabLabels are partial-class fields
                // on MainMenuUI (defined in MainMenuUI.Options.cs).
                BuildOptEditorSubTabs(container.transform, "OESubTab", "OESubContent");
            }

            AddOptHint(_optInputsPanel.transform, "Click on a key to rebind  |  Esc to cancel  |  Q / E Change tab", panelH);
        }

        /// <summary>
        /// Builds a standard row group for non-editor tabs (General / Movement / Spells).
        /// </summary>
        private void BuildOptStandardRows(Transform container,
            (string label, string actionA, string actionB, string actionMouse)[] rows)
        {
            const float rowH = 36f; const float gap = 6f; const float startY = -100f;
            const float padX = 16f;

            for (int ri = 0; ri < rows.Length; ri++)
            {
                float cy  = startY - ri * (rowH + gap);
                var row   = CreateUIObject($"ORow_{ri}", container);
                var rowR  = row.GetComponent<RectTransform>();
                rowR.anchorMin = Vector2.up; rowR.anchorMax = new Vector2(1f, 1f);
                rowR.pivot = new Vector2(0f, 0.5f);
                rowR.anchoredPosition = new Vector2(padX, cy);
                rowR.sizeDelta = new Vector2(-padX * 2, rowH);

                AddOptTableCell(row.transform, rows[ri].label, TextAlignmentOptions.Left, 0f, 0.35f);
                AddOptRebindCell(row.transform, rows[ri].actionA,     0, 0.38f, 0.18f, "Key A");
                AddOptRebindCell(row.transform, rows[ri].actionB,     1, 0.58f, 0.18f, "Key B");
                AddOptRebindCell(row.transform, rows[ri].actionMouse, 0, 0.78f, 0.22f, "Mouse");
            }
        }

        /// <summary>
        /// Builds the 12-editor secondary tab strip for the Main Menu inputs panel.
        /// Mirrors PauseMenuUI.BuildEditorSubTabs using the opt-prefixed helpers.
        ///
        /// NOTE: In-editor shortcuts (Ctrl+Z/Y/S, Esc) are read-only — making them
        ///       rebindable requires migrating each editor's KeyboardInputManager calls
        ///       to InputAction-backed lookups (deferred).
        /// </summary>
        private void BuildOptEditorSubTabs(Transform container,
            string subTabPrefix, string contentPrefix)
        {
            var editors = EditorSubTabData.All;

            // ── Sub-tab strip (2 rows × 6) ───────────────────────────────────
            const float stripY0 = -92f;
            const float stripY1 = -124f;
            const float subTabH = 28f;
            _optEditorSubTabLabels = new TextMeshProUGUI[editors.Length];

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
                    _optEditorSubTabSel = cap;
                    RefreshOptEditorSubTabVisuals(container, contentPrefix);
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
                _optEditorSubTabLabels[i] = tmp;
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
                var toggleRow = CreateUIObject("OToggleRow", content.transform);
                SetOptInputRowRect(toggleRow, cy, rowH, padX);

                AddOptTableCell(toggleRow.transform, ed.ToggleLabel, TextAlignmentOptions.Left, 0f, 0.55f);
                if (ed.IsFixedBinding)
                {
                    AddOptReadOnlyCell(toggleRow.transform, "Ctrl + F3 (fixed)", 0.58f, 0.42f);
                }
                else
                {
                    AddOptRebindCell(toggleRow.transform, ed.ActionName, 0, 0.58f, 0.22f, "Key A");
                }
                rowIdx++;

                if (ed.HasUndo)
                {
                    cy = contentStartY - rowIdx * (rowH + rowGap);
                    AddOptReadOnlyShortcutRow(content.transform, "Undo", "Ctrl + Z  (shared)", cy, rowH, padX);
                    rowIdx++;
                    cy = contentStartY - rowIdx * (rowH + rowGap);
                    AddOptReadOnlyShortcutRow(content.transform, "Redo", "Ctrl + Y  (shared)", cy, rowH, padX);
                    rowIdx++;
                }

                if (ed.HasSave)
                {
                    cy = contentStartY - rowIdx * (rowH + rowGap);
                    AddOptReadOnlyShortcutRow(content.transform, "Save", "Ctrl + S  (shared)", cy, rowH, padX);
                    rowIdx++;
                }

                if (ed.HasEsc)
                {
                    cy = contentStartY - rowIdx * (rowH + rowGap);
                    AddOptReadOnlyShortcutRow(content.transform, "Close / Cancel", "Esc  (shared)", cy, rowH, padX);
                    rowIdx++;
                }

                cy = contentStartY - rowIdx * (rowH + rowGap);
                AddOptReadOnlyShortcutRow(content.transform, "Pan Camera", "MMB drag  (shared)", cy, rowH, padX);
            }

            RefreshOptEditorSubTabVisuals(container, contentPrefix);
        }

        private void RefreshOptEditorSubTabVisuals(Transform container, string contentPrefix)
        {
            if (_optEditorSubTabLabels == null) return;
            for (int i = 0; i < _optEditorSubTabLabels.Length; i++)
            {
                if (_optEditorSubTabLabels[i] != null)
                    _optEditorSubTabLabels[i].color = i == _optEditorSubTabSel ? TextSelected : TextNormal;
                var c = container.Find($"{contentPrefix}_{i}");
                if (c != null) c.gameObject.SetActive(i == _optEditorSubTabSel);
            }
        }

        private void AddOptReadOnlyCell(Transform parent, string value,
            float anchorLeft, float width)
        {
            var go = CreateUIObject("OReadOnly_" + value.Replace(" ", "_").Replace("+", "Plus"), parent);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(anchorLeft, 0f);
            r.anchorMax = new Vector2(anchorLeft + width, 1f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = new Vector2(4f, 6f); r.offsetMax = new Vector2(-4f, -6f);

            var img = go.AddComponent<Image>();
            img.color = new Color(0.09f, 0.09f, 0.12f, 1f);

            var textGo = CreateUIObject("Label", go.transform);
            var tr = textGo.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.sizeDelta = Vector2.zero;
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = value;
            tmp.fontSize = 13f;
            tmp.color = new Color(0.55f, 0.55f, 0.60f, 1f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
        }

        private void AddOptReadOnlyShortcutRow(Transform container, string label, string shortcut,
            float cy, float rowH, float padX)
        {
            var row = CreateUIObject($"OShortcut_{label.Replace(" ", "_").Replace("/", "_")}", container);
            SetOptInputRowRect(row, cy, rowH, padX);

            AddOptTableCell(row.transform, label, TextAlignmentOptions.Left, 0f, 0.55f);
            AddOptReadOnlyCell(row.transform, shortcut, 0.58f, 0.42f);
        }

        private static void SetOptInputRowRect(GameObject go, float cy, float rowH, float padX)
        {
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = Vector2.up; r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = new Vector2(padX, cy);
            r.sizeDelta = new Vector2(-padX * 2, rowH);
        }

        private KeyRebinder _optRebinder;

        private void AddOptRebindCell(Transform parent, string action, int slotIndex,
            float anchorLeft, float width, string prefix)
        {
            if (string.IsNullOrEmpty(action) || action == "-")
            {
                AddOptTableCell(parent, $"{prefix}: -", TextAlignmentOptions.Left, anchorLeft, width);
                return;
            }

            var gs = GameSettings.Instance;
            string current = gs != null ? GameSettingsBindings.Get(gs, action, slotIndex) : "";
            if (string.IsNullOrEmpty(current)) current = "-";

            var go = CreateUIObject("OptRebindCell_" + action + "_" + slotIndex, parent);
            var r = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(anchorLeft, 0f);
            r.anchorMax = new Vector2(anchorLeft + width, 1f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = new Vector2(4f, 6f); r.offsetMax = new Vector2(-4f, -6f);

            var img = go.AddComponent<Image>();
            img.color = new Color(0.12f, 0.12f, 0.18f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

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
            btn.onClick.AddListener(() => StartOptRebind(capturedAction, capturedSlot, capturedPrefix, tmp, img));
        }

        private void StartOptRebind(string action, int slotIndex, string prefix,
            TextMeshProUGUI label, Image cellBg)
        {
            if (_optRebinder != null && _optRebinder.IsActive) return;

            var origColor = cellBg.color;
            var origText = label.text;
            cellBg.color = new Color(0.55f, 0.45f, 0.15f, 1f);
            label.text = $"{prefix}: <i>Press any key...</i>";

            _optRebinder?.Dispose();
            _optRebinder = new KeyRebinder();
            _optRebinder.Completed += captured =>
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
                _optRebinder?.Dispose();
                _optRebinder = null;
            };
            _optRebinder.Cancelled += () =>
            {
                label.text = origText;
                cellBg.color = origColor;
                _optRebinder?.Dispose();
                _optRebinder = null;
            };
            _optRebinder.Start();
        }

        // ════════════════════════════════════════════════════════════════════
        // Visuals update
        // ════════════════════════════════════════════════════════════════════

    }
}
