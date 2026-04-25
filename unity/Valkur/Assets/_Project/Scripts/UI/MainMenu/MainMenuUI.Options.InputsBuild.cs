using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;

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
            r.anchorMin = new Vector2(0.5f, 0.5f); r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f); r.anchoredPosition = Vector2.zero;
            r.sizeDelta = new Vector2(panelW, panelH);
            _optInputsPanel.AddComponent<Image>().color = PanelBg;

            AddOptPanelTitle(_optInputsPanel.transform, "Configurar Controles");

            var tabs = new[] { "General", "Movimientos", "Hechizos", "Editores" };
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

            var tabData = new (string label, string actionA, string actionB, string actionMouse)[][]
            {
                new[] {
                    ("Pausa",       "pause",            "-", "-"),
                    ("Inventario",  "toggle_inventory", "-", "-"),
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

            for (int t = 0; t < tabData.Length; t++)
            {
                var container = CreateUIObject($"OTabContent_{t}", _optInputsPanel.transform);
                StretchFull(container);
                var rows = tabData[t];
                for (int ri = 0; ri < rows.Length; ri++)
                {
                    float cy  = startY - ri * (rowH + gap);
                    var row   = CreateUIObject($"ORow_{t}_{ri}", container.transform);
                    var rowR  = row.GetComponent<RectTransform>();
                    rowR.anchorMin = Vector2.up; rowR.anchorMax = new Vector2(1f, 1f);
                    rowR.pivot = new Vector2(0f, 0.5f);
                    rowR.anchoredPosition = new Vector2(16f, cy);
                    rowR.sizeDelta = new Vector2(-32f, rowH);

                    AddOptTableCell(row.transform, rows[ri].label, TextAlignmentOptions.Left, 0f, 0.35f);
                    AddOptRebindCell(row.transform, rows[ri].actionA,     0, 0.38f, 0.18f, "Tecla A");
                    AddOptRebindCell(row.transform, rows[ri].actionB,     1, 0.58f, 0.18f, "Tecla B");
                    AddOptRebindCell(row.transform, rows[ri].actionMouse, 0, 0.78f, 0.22f, "Raton");
                }
            }

            AddOptHint(_optInputsPanel.transform, "Click en una tecla para reasignar  |  Esc para cancelar  |  Q / E Cambiar pestaña", panelH);
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