using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;

namespace Valkur.UI.MainMenu
{
    /// <summary>
    /// Options sub-menu for the main menu.
    /// Mirrors Python: Opciones → Inputs / Sonido / Volver.
    /// Each sub-screen uses the same visual style as PauseMenuUI.
    /// </summary>
    public partial class MainMenuUI
    {
        // ── Screen state ─────────────────────────────────────────────────────
        private enum MenuScreen { Main, Options, Sounds, Inputs, LoadGame }
        private MenuScreen _menuScreen = MenuScreen.Main;

        // ── Options overlay & panels ─────────────────────────────────────────
        private GameObject _optOverlay;
        private GameObject _optPanel;
        private GameObject _optSoundsPanel;
        private GameObject _optInputsPanel;

        // ── Options list ─────────────────────────────────────────────────────
        private readonly string[] _optMenuOptions = { "Inputs", "Sonido", "Volver" };
        private int      _optMenuSel;
        private Image[]  _optMenuPills;
        private Image[]  _optMenuBars;
        private TextMeshProUGUI[] _optMenuTexts;

        // ── Sounds panel ─────────────────────────────────────────────────────
        private struct SoundRow
        {
            public TextMeshProUGUI valueText;
            public float min, max, step;
            public System.Func<float> get;
            public System.Action<float> set;
        }
        private readonly List<SoundRow> _optSoundRows = new List<SoundRow>();
        private int      _optSoundSel;
        private Image[]  _optSoundPills;
        private Image[]  _optSoundBars;
        private TextMeshProUGUI[] _optSoundLabels;

        // ── Inputs panel ─────────────────────────────────────────────────────
        private int _optInputsTabSel;
        private TextMeshProUGUI[] _optTabLabels;

        // ════════════════════════════════════════════════════════════════════
        // Screen management
        // ════════════════════════════════════════════════════════════════════

        private void ShowMenuScreen(MenuScreen screen)
        {
            _menuScreen = screen;
            bool showOpt = screen == MenuScreen.Options || screen == MenuScreen.Sounds || screen == MenuScreen.Inputs;
            bool showLoad = screen == MenuScreen.LoadGame;
            if (_optOverlay != null) _optOverlay.SetActive(showOpt);
            if (_optPanel != null) _optPanel.SetActive(screen == MenuScreen.Options);
            if (_optSoundsPanel != null) _optSoundsPanel.SetActive(screen == MenuScreen.Sounds);
            if (_optInputsPanel != null) _optInputsPanel.SetActive(screen == MenuScreen.Inputs);
            if (_mmLoadOverlay != null) _mmLoadOverlay.SetActive(showLoad);

            if (screen == MenuScreen.Options)
            { _optMenuSel = 0; UpdateOptListVisuals(); }
            if (screen == MenuScreen.Sounds)
            { _optSoundSel = 0; UpdateOptSoundsVisuals(); }
            if (screen == MenuScreen.Inputs)
            { _optInputsTabSel = 0; UpdateOptInputsPanel(); }
            if (screen == MenuScreen.LoadGame)
            { RefreshMMLoadPanel(); }
        }

        private void OptionsGoBack()
        {
            switch (_menuScreen)
            {
                case MenuScreen.Options:  ShowMenuScreen(MenuScreen.Main); break;
                case MenuScreen.Sounds:   ShowMenuScreen(MenuScreen.Options); break;
                case MenuScreen.Inputs:   ShowMenuScreen(MenuScreen.Options); break;
                case MenuScreen.LoadGame: ShowMenuScreen(MenuScreen.Main); break;
                default: break;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Input handlers
        // ════════════════════════════════════════════════════════════════════

        private void HandleOptionsListInput()
        {
            if (_navUpAction.WasPerformedThisFrame())
            { _optMenuSel = (_optMenuSel - 1 + _optMenuOptions.Length) % _optMenuOptions.Length; UpdateOptListVisuals(); }
            else if (_navDownAction.WasPerformedThisFrame())
            { _optMenuSel = (_optMenuSel + 1) % _optMenuOptions.Length; UpdateOptListVisuals(); }
            else if (_confirmAction.WasPerformedThisFrame())
            { ExecuteOptionsItem(_optMenuSel); }
            else if (_cancelAction.WasPerformedThisFrame())
            { OptionsGoBack(); }
        }

        private void HandleOptionsSoundsInput()
        {
            if (_navUpAction.WasPerformedThisFrame())
            { _optSoundSel = (_optSoundSel - 1 + _optSoundRows.Count) % _optSoundRows.Count; UpdateOptSoundsVisuals(); }
            else if (_navDownAction.WasPerformedThisFrame())
            { _optSoundSel = (_optSoundSel + 1) % _optSoundRows.Count; UpdateOptSoundsVisuals(); }
            else if (_navLeftAction.WasPerformedThisFrame())
            { ChangeOptSound(_optSoundSel, -1); }
            else if (_navRightAction.WasPerformedThisFrame())
            { ChangeOptSound(_optSoundSel, +1); }
            else if (_confirmAction.WasPerformedThisFrame())
            { GameSettings.Instance?.Save(); ServiceLocator.Get<IAudioService>()?.ApplySettings(); OptionsGoBack(); }
            else if (_cancelAction.WasPerformedThisFrame())
            { OptionsGoBack(); }
        }

        private void HandleOptionsInputsInput()
        {
            int tabCount = _optTabLabels != null ? _optTabLabels.Length : 0;
            bool tabLeft  = UnityEngine.InputSystem.Keyboard.current != null &&
                            UnityEngine.InputSystem.Keyboard.current.qKey.wasPressedThisFrame;
            bool tabRight = UnityEngine.InputSystem.Keyboard.current != null &&
                            UnityEngine.InputSystem.Keyboard.current.eKey.wasPressedThisFrame;

            if (tabLeft && tabCount > 0)
            { _optInputsTabSel = (_optInputsTabSel - 1 + tabCount) % tabCount; UpdateOptInputsPanel(); }
            else if (tabRight && tabCount > 0)
            { _optInputsTabSel = (_optInputsTabSel + 1) % tabCount; UpdateOptInputsPanel(); }
            else if (_cancelAction.WasPerformedThisFrame())
            { OptionsGoBack(); }
        }

        private void ExecuteOptionsItem(int idx)
        {
            switch (_optMenuOptions[idx])
            {
                case "Inputs": ShowMenuScreen(MenuScreen.Inputs); break;
                case "Sonido": ShowMenuScreen(MenuScreen.Sounds); break;
                case "Volver": ShowMenuScreen(MenuScreen.Main);   break;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Panel builders (called from BuildUI)
        // ════════════════════════════════════════════════════════════════════

        private void BuildOptionsSubmenu(Transform canvas)
        {
            _optOverlay = CreateUIObject("OptionsOverlay", canvas);
            StretchFull(_optOverlay);
            _optOverlay.AddComponent<Image>().color = OverlayColor;

            BuildOptListPanel(_optOverlay.transform);
            BuildOptSoundsPanel(_optOverlay.transform);
            BuildOptInputsPanel(_optOverlay.transform);

            _optOverlay.SetActive(false);
        }

        // ── Options list panel ───────────────────────────────────────────────

        private void BuildOptListPanel(Transform parent)
        {
            const float panelW = 380f;
            const float rowH   = 52f;
            const float titleH = 52f;
            const float padY   = 16f;
            const float barW   = 4f;
            float panelH = titleH + padY + _optMenuOptions.Length * rowH + padY;

            _optPanel = CreateUIObject("OptPanel", parent);
            var pr    = _optPanel.GetComponent<RectTransform>();
            pr.anchorMin = new Vector2(0.5f, 0.5f); pr.anchorMax = new Vector2(0.5f, 0.5f);
            pr.pivot = new Vector2(0.5f, 0.5f); pr.anchoredPosition = Vector2.zero;
            pr.sizeDelta = new Vector2(panelW, panelH);
            _optPanel.AddComponent<Image>().color = PanelBg;

            AddOptPanelTitle(_optPanel.transform, "Opciones");

            _optMenuPills = new Image[_optMenuOptions.Length];
            _optMenuBars  = new Image[_optMenuOptions.Length];
            _optMenuTexts = new TextMeshProUGUI[_optMenuOptions.Length];

            for (int i = 0; i < _optMenuOptions.Length; i++)
            {
                float cy = -(titleH + padY + i * rowH + rowH * 0.5f);

                var pGo = CreateUIObject($"OPill_{i}", _optPanel.transform);
                var pR  = pGo.GetComponent<RectTransform>();
                pR.anchorMin = new Vector2(0f, 1f); pR.anchorMax = new Vector2(1f, 1f);
                pR.pivot = new Vector2(0.5f, 0.5f);
                pR.anchoredPosition = new Vector2(0f, cy);
                pR.sizeDelta = new Vector2(0f, rowH - 4f);
                _optMenuPills[i] = pGo.AddComponent<Image>(); _optMenuPills[i].color = Color.clear;

                var bGo = CreateUIObject($"OBar_{i}", _optPanel.transform);
                var bR  = bGo.GetComponent<RectTransform>();
                bR.anchorMin = new Vector2(0f, 1f); bR.anchorMax = new Vector2(0f, 1f);
                bR.pivot = new Vector2(0f, 0.5f);
                bR.anchoredPosition = new Vector2(0f, cy);
                bR.sizeDelta = new Vector2(barW, rowH - 4f);
                _optMenuBars[i] = bGo.AddComponent<Image>(); _optMenuBars[i].color = Color.clear;

                var tGo = CreateUIObject($"OText_{i}", _optPanel.transform);
                var tR  = tGo.GetComponent<RectTransform>();
                tR.anchorMin = new Vector2(0f, 1f); tR.anchorMax = new Vector2(1f, 1f);
                tR.pivot = new Vector2(0f, 0.5f);
                tR.anchoredPosition = new Vector2(30f, cy);
                tR.sizeDelta = new Vector2(-30f, rowH);
                var tmp = tGo.AddComponent<TextMeshProUGUI>();
                tmp.text = _optMenuOptions[i]; tmp.fontSize = 22f;
                tmp.alignment = TextAlignmentOptions.Left; tmp.color = TextNormal;
                _optMenuTexts[i] = tmp;

                // Click/hover
                var hitGo = CreateUIObject($"OHit_{i}", _optPanel.transform);
                var hitR  = hitGo.GetComponent<RectTransform>();
                hitR.anchorMin = new Vector2(0f, 1f); hitR.anchorMax = new Vector2(1f, 1f);
                hitR.pivot = new Vector2(0.5f, 0.5f);
                hitR.anchoredPosition = new Vector2(0f, cy);
                hitR.sizeDelta = new Vector2(0f, rowH);
                var hitImg = hitGo.AddComponent<Image>(); hitImg.color = Color.clear;
                var btn = hitGo.AddComponent<Button>(); btn.targetGraphic = hitImg;
                var bc = btn.colors;
                bc.normalColor = Color.clear; bc.highlightedColor = Color.clear;
                bc.pressedColor = new Color(1f, 1f, 1f, 0.05f); bc.selectedColor = Color.clear;
                btn.colors = bc;
                int cap = i;
                btn.onClick.AddListener(() => ExecuteOptionsItem(cap));
                var trig = hitGo.AddComponent<EventTrigger>();
                var ent  = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                ent.callback.AddListener(_ => { _optMenuSel = cap; UpdateOptListVisuals(); });
                trig.triggers.Add(ent);
            }
        }

        // ── Sounds panel ─────────────────────────────────────────────────────

        private void BuildOptSoundsPanel(Transform parent)
        {
            var gs = GameSettings.Instance;

            var rowDefs = new (string label, float min, float max, float step,
                System.Func<float> get, System.Action<float> set)[]
            {
                ("Música",                      0f,    1f,   0.02f, () => gs.musicVolume,        v => gs.musicVolume        = v),
                ("Ambiente",                    0f,    1f,   0.02f, () => gs.ambientVolume,       v => gs.ambientVolume       = v),
                ("SFX",                         0f,    1f,   0.02f, () => gs.sfxVolume,            v => gs.sfxVolume            = v),
                ("Ambiente: mín intervalo (s)", 0f,   60f,   0.5f, () => gs.ambientMinInterval,  v => gs.ambientMinInterval  = v),
                ("Ambiente: máx intervalo (s)", 0f,  120f,   0.5f, () => gs.ambientMaxInterval,  v => gs.ambientMaxInterval  = v),
                ("Ducking: atenuación (dB)",  -24f,    0f,   1f,   () => gs.duckingAttenuation,  v => gs.duckingAttenuation  = v),
                ("Ducking: hold (ms)",          0f, 2000f,  25f,   () => gs.duckingHoldMs,       v => gs.duckingHoldMs       = v),
                ("Ducking: release (ms)",       0f, 2000f,  25f,   () => gs.duckingReleaseMs,    v => gs.duckingReleaseMs    = v),
            };

            const float rowH   = 40f;
            const float padX   = 20f;
            const float padY   = 16f;
            const float gap    = 6f;
            const float panelW = 540f;
            float panelH = padY * 2 + rowDefs.Length * rowH + (rowDefs.Length - 1) * gap + 60f;

            _optSoundsPanel = CreateUIObject("OptSoundsPanel", parent);
            var r = _optSoundsPanel.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0.5f, 0.5f); r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f); r.anchoredPosition = Vector2.zero;
            r.sizeDelta = new Vector2(panelW, panelH);
            _optSoundsPanel.AddComponent<Image>().color = PanelBg;

            AddOptPanelTitle(_optSoundsPanel.transform, "Opciones de Sonido");

            _optSoundRows.Clear();
            _optSoundPills  = new Image[rowDefs.Length];
            _optSoundBars   = new Image[rowDefs.Length];
            _optSoundLabels = new TextMeshProUGUI[rowDefs.Length];

            const float btnSize = 28f;

            for (int i = 0; i < rowDefs.Length; i++)
            {
                var def = rowDefs[i];
                float cy = -58f - i * (rowH + gap) - rowH * 0.5f;

                var pillGo = CreateUIObject($"OSPill_{i}", _optSoundsPanel.transform);
                SetOptRowRect(pillGo, cy, rowH);
                _optSoundPills[i] = pillGo.AddComponent<Image>(); _optSoundPills[i].color = Color.clear;

                var barGo = CreateUIObject($"OSBar_{i}", _optSoundsPanel.transform);
                var barR  = barGo.GetComponent<RectTransform>();
                barR.anchorMin = new Vector2(0f, 1f); barR.anchorMax = new Vector2(0f, 1f);
                barR.pivot = new Vector2(0f, 0.5f);
                barR.anchoredPosition = new Vector2(0f, cy);
                barR.sizeDelta = new Vector2(4f, rowH - 4f);
                _optSoundBars[i] = barGo.AddComponent<Image>(); _optSoundBars[i].color = Color.clear;

                var lblGo = CreateUIObject($"OSLabel_{i}", _optSoundsPanel.transform);
                var lblR  = lblGo.GetComponent<RectTransform>();
                lblR.anchorMin = new Vector2(0f, 1f); lblR.anchorMax = new Vector2(0.55f, 1f);
                lblR.pivot = new Vector2(0f, 0.5f);
                lblR.anchoredPosition = new Vector2(padX + 12f, cy);
                lblR.sizeDelta = new Vector2(0f, rowH);
                var lblTMP = lblGo.AddComponent<TextMeshProUGUI>();
                lblTMP.text = def.label; lblTMP.fontSize = 18f;
                lblTMP.alignment = TextAlignmentOptions.Left; lblTMP.color = TextNormal;
                _optSoundLabels[i] = lblTMP;

                var valGo = CreateUIObject($"OSVal_{i}", _optSoundsPanel.transform);
                var valR  = valGo.GetComponent<RectTransform>();
                valR.anchorMin = new Vector2(0.58f, 1f); valR.anchorMax = new Vector2(0.72f, 1f);
                valR.pivot = new Vector2(0.5f, 0.5f);
                valR.anchoredPosition = new Vector2(0f, cy);
                valR.sizeDelta = new Vector2(0f, rowH);
                var valTMP = valGo.AddComponent<TextMeshProUGUI>();
                valTMP.fontSize = 18f; valTMP.alignment = TextAlignmentOptions.Center;
                valTMP.color = AccentGold;

                int cap = i;
                AddOptStepButton(_optSoundsPanel.transform, $"OSMin_{i}", "-",
                    new Vector2(0.75f, 0.5f), cy, btnSize, () => ChangeOptSound(cap, -1));
                AddOptStepButton(_optSoundsPanel.transform, $"OSPlus_{i}", "+",
                    new Vector2(0.88f, 0.5f), cy, btnSize, () => ChangeOptSound(cap, +1));

                var hitGo = CreateUIObject($"OSHit_{i}", _optSoundsPanel.transform);
                SetOptRowRect(hitGo, cy, rowH);
                var hitImg = hitGo.AddComponent<Image>(); hitImg.color = Color.clear;
                var hitBtn = hitGo.AddComponent<Button>(); hitBtn.targetGraphic = hitImg;
                hitBtn.onClick.AddListener(() => { _optSoundSel = cap; UpdateOptSoundsVisuals(); });
                var trig  = hitGo.AddComponent<EventTrigger>();
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(_ => { _optSoundSel = cap; UpdateOptSoundsVisuals(); });
                trig.triggers.Add(enter);

                var sr = new SoundRow
                {
                    valueText = valTMP,
                    min = def.min, max = def.max, step = def.step,
                    get = def.get, set = def.set
                };
                _optSoundRows.Add(sr);
                RefreshOptSoundRowText(i);
            }

            AddOptHint(_optSoundsPanel.transform, "<- -> Ajustar  |  Enter Guardar  |  Esc Volver", panelH);
        }

        // ── Inputs panel ─────────────────────────────────────────────────────

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

        private void UpdateOptListVisuals()
        {
            if (_optMenuPills == null) return;
            for (int i = 0; i < _optMenuPills.Length; i++)
            {
                bool s = i == _optMenuSel;
                _optMenuPills[i].color = s ? PillColor  : Color.clear;
                _optMenuBars[i].color  = s ? AccentGold : Color.clear;
                _optMenuTexts[i].color = s ? TextSelected : TextNormal;
            }
        }

        private void UpdateOptSoundsVisuals()
        {
            if (_optSoundPills == null) return;
            for (int i = 0; i < _optSoundPills.Length; i++)
            {
                bool s = i == _optSoundSel;
                _optSoundPills[i].color = s ? PillColor  : Color.clear;
                _optSoundBars[i].color  = s ? AccentGold : Color.clear;
                if (_optSoundLabels != null && i < _optSoundLabels.Length)
                    _optSoundLabels[i].color = s ? TextSelected : TextNormal;
            }
        }

        private void UpdateOptInputsPanel()
        {
            if (_optTabLabels == null || _optInputsPanel == null) return;
            for (int i = 0; i < _optTabLabels.Length; i++)
            {
                if (_optTabLabels[i] != null)
                    _optTabLabels[i].color = i == _optInputsTabSel ? TextSelected : TextNormal;
                var container = _optInputsPanel.transform.Find($"OTabContent_{i}");
                if (container != null) container.gameObject.SetActive(i == _optInputsTabSel);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Sound helpers
        // ════════════════════════════════════════════════════════════════════

        private void ChangeOptSound(int i, int dir)
        {
            if (i < 0 || i >= _optSoundRows.Count) return;
            var row = _optSoundRows[i];
            float v = Mathf.Clamp(row.get() + dir * row.step, row.min, row.max);
            row.set(v);
            RefreshOptSoundRowText(i);
            ServiceLocator.Get<IAudioService>()?.ApplySettings();
            GameSettings.Instance?.Save();
        }

        private void RefreshOptSoundRowText(int i)
        {
            if (i < 0 || i >= _optSoundRows.Count) return;
            var row = _optSoundRows[i];
            float v = row.get();
            row.valueText.text = row.max <= 1f
                ? Mathf.RoundToInt(v * 100f).ToString()
                : v.ToString("F1");
        }

        // ════════════════════════════════════════════════════════════════════
        // UI builder helpers (prefixed to avoid conflicts with UIBuilder.cs)
        // ════════════════════════════════════════════════════════════════════

        private void AddOptPanelTitle(Transform parent, string text)
        {
            var go = CreateUIObject("OptTitle", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -12f);
            rt.sizeDelta = new Vector2(0f, 44f);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = 28f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = AccentGold; tmp.fontStyle = FontStyles.Bold;
        }

        private void AddOptHint(Transform parent, string text, float panelH)
        {
            var go = CreateUIObject("OptHint", parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 8f);
            rt.sizeDelta = new Vector2(0f, 28f);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = 14f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = VersionCol;
        }

        private void AddOptStepButton(Transform parent, string name, string label,
            Vector2 anchor, float cy, float size, UnityEngine.Events.UnityAction action)
        {
            var go = CreateUIObject(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(anchor.x, 1f); rt.anchorMax = new Vector2(anchor.x, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, cy);
            rt.sizeDelta = new Vector2(size, size);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.22f, 0.22f, 0.28f, 1f);
            var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
            btn.onClick.AddListener(action);
            // Text as child
            var txtGo = CreateUIObject("Label", go.transform);
            var txtR  = txtGo.GetComponent<RectTransform>();
            txtR.anchorMin = Vector2.zero; txtR.anchorMax = Vector2.one;
            txtR.sizeDelta = Vector2.zero; txtR.anchoredPosition = Vector2.zero;
            var tmp = txtGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label; tmp.fontSize = 20f;
            tmp.alignment = TextAlignmentOptions.Center; tmp.color = AccentGold;
            tmp.fontStyle = FontStyles.Bold;
            tmp.raycastTarget = false;
        }

        private void AddOptTableCell(Transform parent, string text, TextAlignmentOptions align,
            float anchorX, float anchorW)
        {
            var go = CreateUIObject(text.Replace(" ", "_"), parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(anchorX, 0f);
            rt.anchorMax = new Vector2(anchorX + anchorW, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = 15f;
            tmp.alignment = align; tmp.color = TextNormal;
            tmp.enableWordWrapping = false;
        }

        private static void SetOptRowRect(GameObject go, float cy, float h)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, cy);
            rt.sizeDelta = new Vector2(0f, h);
        }
    }
}
