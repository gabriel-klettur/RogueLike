using UnityEngine;
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
                var tmp = tGo.AddComponent<TextMeshProUGUI>();
                tmp.text = tabs[t]; tmp.fontSize = 18f;
                tmp.alignment = TextAlignmentOptions.Center; tmp.color = TextNormal;
                _tabLabels[t] = tmp;

                int cap = t;
                var btn = tGo.AddComponent<Button>(); btn.targetGraphic = img;
                btn.onClick.AddListener(() => { _inputsTabSel = cap; UpdateInputsPanel(); });
            }

            var gs = GameSettings.Instance;
            var tabData = new (string action, string keyA, string keyB, string mouse)[][]
            {
                new[] {
                    ("Pausa",       gs.pauseKeyA,            "-", "-"),
                    ("Inventario",  gs.toggleInventoryKeyA,  "-", "-"),
                },
                new[] {
                    ("Arriba",      gs.moveUpKeyA,    gs.moveUpKeyB,    "-"),
                    ("Abajo",       gs.moveDownKeyA,  gs.moveDownKeyB,  "-"),
                    ("Izquierda",   gs.moveLeftKeyA,  gs.moveLeftKeyB,  "-"),
                    ("Derecha",     gs.moveRightKeyA, gs.moveRightKeyB, "-"),
                    ("Dash",        gs.dashKeyA,      gs.dashKeyB,      "-"),
                },
                new[] {
                    ("Hechizo 1",   gs.spell1KeyA, "-", gs.primaryAttackMouse),
                    ("Hechizo 2",   gs.spell2KeyA, "-", gs.secondaryAttackMouse),
                    ("Hechizo 3",   gs.spell3KeyA, "-", "-"),
                    ("Hechizo 4",   gs.spell4KeyA, "-", "-"),
                },
                new[] {
                    ("Editor Tiles", gs.toggleTileEditorKeyA, "-", "-"),
                    ("Editor Mapa",  gs.toggleMapEditorKeyA,  "-", "-"),
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

                    AddTableCell(row.transform, rows[i].action,             TextAlignmentOptions.Left, 0f,   0.35f);
                    AddTableCell(row.transform, "Tecla A: " + rows[i].keyA, TextAlignmentOptions.Left, col1, 0.18f);
                    AddTableCell(row.transform, "Tecla B: " + rows[i].keyB, TextAlignmentOptions.Left, col2, 0.18f);
                    AddTableCell(row.transform, "Raton: " + rows[i].mouse,  TextAlignmentOptions.Left, col3, 0.22f);
                }
            }

            AddHint(panel.transform, "Q / E  Cambiar pestana  |  Esc Volver  (reasignacion proximamente)", panelH);
            return panel;
        }
    }
}
