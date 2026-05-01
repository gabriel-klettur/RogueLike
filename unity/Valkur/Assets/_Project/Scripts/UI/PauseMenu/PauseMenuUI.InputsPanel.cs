using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;

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

            AddPanelTitle(panel.transform, "Configurar Controles", panelH, 20f);

            var tabs = new[] { "General", "Movimientos", "Hechizos", "Editores" };
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

            var gs = GameSettings.Instance;
            // Tab rows: each row lists the display label and the underlying action keys used by GameSettingsBindings.
            // Action "-" means no binding slot for that column.
            var tabData = new (string label, string actionA, string actionB, string actionMouse)[][]
            {
                new[] {
                    ("Pausa",       "pause",               "-", "-"),
                    ("Inventario",  "toggle_inventory",    "-", "-"),
                },
                new[] {
                    ("Arriba",      "move_up",    "move_up",    "-"),
                    ("Abajo",       "move_down",  "move_down",  "-"),
                    ("Izquierda",   "move_left",  "move_left",  "-"),
                    ("Derecha",     "move_right", "move_right", "-"),
                    ("Dash",        "dash",       "dash",       "-"),
                },
                new[] {
                    ("Hechizo 1",   "spell_1", "-", "attack_primary_mouse"),
                    ("Hechizo 2",   "spell_2", "-", "attack_secondary_mouse"),
                    ("Hechizo 3",   "spell_3", "-", "-"),
                    ("Hechizo 4",   "spell_4", "-", "-"),
                },
                new[] {
                    ("Editor Tiles", "toggle_tile_editor", "-", "-"),
                    ("Editor Mapa",  "toggle_map_editor",  "-", "-"),
                },
            };

            const float rowH = 36f; const float gap = 6f; const float startY = -100f;
            const float col0 = 16f, col1 = 0.38f, col2 = 0.58f, col3 = 0.78f;

            for (int t = 0; t < tabData.Length; t++)
            {
                var container = CreateUIObject($"TabContent_{t}", panel.transform);
                StretchFull(container);
                var rows = tabData[t];
                for (int i = 0; i < rows.Length; i++)
                {
                    float cy  = startY - i * (rowH + gap);
                    var row   = CreateUIObject($"Row_{t}_{i}", container.transform);
                    var rowR  = row.GetComponent<RectTransform>();
                    rowR.anchorMin = Vector2.up; rowR.anchorMax = new Vector2(1f, 1f);
                    rowR.pivot = new Vector2(0f, 0.5f);
                    rowR.anchoredPosition = new Vector2(col0, cy);
                    rowR.sizeDelta = new Vector2(-col0 * 2, rowH);

                    AddTableCell(row.transform, rows[i].label, TextAlignmentOptions.Left, 0f, 0.35f);
                    AddRebindCell(row.transform, rows[i].actionA,     0, col1, 0.18f, "Tecla A");
                    AddRebindCell(row.transform, rows[i].actionB,     1, col2, 0.18f, "Tecla B");
                    AddRebindCell(row.transform, rows[i].actionMouse, 0, col3, 0.22f, "Raton");
                }
            }

            AddHint(panel.transform, "Click en una tecla para reasignar  |  Esc para cancelar  |  Q / E Cambiar pestana", panelH);
            return panel;
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
        }
    }
}
