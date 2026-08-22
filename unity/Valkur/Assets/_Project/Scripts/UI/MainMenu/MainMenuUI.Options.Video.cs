using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;

namespace Valkur.UI.MainMenu
{
    /// <summary>
    /// Options → Video. Three rows: Resolution, Display mode, Apply.
    ///
    /// The list is deliberately short and curated (see
    /// <see cref="DisplaySettings"/>): every offered size is exactly the
    /// camera's 2:1 target aspect, so <see cref="AspectRatioEnforcer"/> has
    /// nothing to letterbox and <c>Camera.pixelWidth / pixelHeight</c> is
    /// bit-exact — the condition under which a tile texel covers a whole
    /// number of screen pixels on BOTH axes and the tilemap seam lines cannot
    /// appear.
    ///
    /// Changes are staged locally and only committed on Apply, so a player
    /// scrolling past a resolution their monitor can't show doesn't get their
    /// window resized under them.
    /// </summary>
    public partial class MainMenuUI
    {
        private GameObject _optVideoPanel;

        // Staged (uncommitted) selection.
        private int _optVideoResIndex;
        private int _optVideoModeIndex;

        // Row 0 = Resolution, 1 = Display mode, 2 = Apply.
        private const int OptVideoRowResolution = 0;
        private const int OptVideoRowMode       = 1;
        private const int OptVideoRowApply      = 2;
        private const int OptVideoRowCount      = 3;

        private int _optVideoSel;
        private Image[] _optVideoPills;
        private Image[] _optVideoBars;
        private TextMeshProUGUI[] _optVideoLabels;
        private TextMeshProUGUI[] _optVideoValues;
        private TextMeshProUGUI _optVideoStatus;

        // ════════════════════════════════════════════════════════════════════
        // Build
        // ════════════════════════════════════════════════════════════════════

        private void BuildOptVideoPanel(Transform parent)
        {
            var labels = new[] { "Resolution", "Display mode", "Apply" };

            const float rowH   = 44f;
            const float padX   = 20f;
            const float padY   = 16f;
            const float gap    = 8f;
            const float panelW = 540f;
            float panelH = padY * 2 + OptVideoRowCount * rowH + (OptVideoRowCount - 1) * gap + 118f;

            _optVideoPanel = CreateUIObject("OptVideoPanel", parent);
            var r = _optVideoPanel.GetComponent<RectTransform>();
            // Same anchor as the Sound panel: below the ROGUELIKE 1.0 logo.
            r.anchorMin = new Vector2(0.5f, 1f); r.anchorMax = new Vector2(0.5f, 1f);
            r.pivot = new Vector2(0.5f, 1f); r.anchoredPosition = new Vector2(0f, -280f);
            r.sizeDelta = new Vector2(panelW, panelH);
            _optVideoPanel.AddComponent<Image>().color = PanelBg;

            AddOptPanelTitle(_optVideoPanel.transform, "Video Options");

            _optVideoPills  = new Image[OptVideoRowCount];
            _optVideoBars   = new Image[OptVideoRowCount];
            _optVideoLabels = new TextMeshProUGUI[OptVideoRowCount];
            _optVideoValues = new TextMeshProUGUI[OptVideoRowCount];

            for (int i = 0; i < OptVideoRowCount; i++)
            {
                float cy  = -58f - i * (rowH + gap) - rowH * 0.5f;
                int   cap = i;

                var pillGo = CreateUIObject($"OVPill_{i}", _optVideoPanel.transform);
                SetOptRowRect(pillGo, cy, rowH);
                _optVideoPills[i] = pillGo.AddComponent<Image>();
                _optVideoPills[i].color = Color.clear;
                AttachOptVideoRowHover(pillGo, cap);
                // The Apply row is the only one where a click on the row body
                // means "execute"; the other two are changed with the arrows.
                if (i == OptVideoRowApply)
                {
                    var applyBtn = pillGo.AddComponent<Button>();
                    applyBtn.targetGraphic = _optVideoPills[i];
                    var ac = applyBtn.colors;
                    ac.normalColor = Color.clear; ac.highlightedColor = Color.clear;
                    ac.pressedColor = new Color(1f, 1f, 1f, 0.05f); ac.selectedColor = Color.clear;
                    applyBtn.colors = ac;
                    applyBtn.onClick.AddListener(ApplyOptVideo);
                }

                var barGo = CreateUIObject($"OVBar_{i}", _optVideoPanel.transform);
                var barR  = barGo.GetComponent<RectTransform>();
                barR.anchorMin = new Vector2(0f, 1f); barR.anchorMax = new Vector2(0f, 1f);
                barR.pivot = new Vector2(0f, 0.5f);
                barR.anchoredPosition = new Vector2(0f, cy);
                barR.sizeDelta = new Vector2(4f, rowH - 4f);
                _optVideoBars[i] = barGo.AddComponent<Image>();
                _optVideoBars[i].color = Color.clear;
                _optVideoBars[i].raycastTarget = false;

                var lblGo = CreateUIObject($"OVLabel_{i}", _optVideoPanel.transform);
                var lblR  = lblGo.GetComponent<RectTransform>();
                lblR.anchorMin = new Vector2(0f, 1f); lblR.anchorMax = new Vector2(0.42f, 1f);
                lblR.pivot = new Vector2(0f, 0.5f);
                lblR.anchoredPosition = new Vector2(padX + 12f, cy);
                lblR.sizeDelta = new Vector2(0f, rowH);
                var lblTMP = lblGo.AddComponent<TextMeshProUGUI>();
                lblTMP.text = labels[i]; lblTMP.fontSize = 18f;
                lblTMP.alignment = TextAlignmentOptions.Left; lblTMP.color = TextNormal;
                lblTMP.raycastTarget = false;
                _optVideoLabels[i] = lblTMP;

                var valGo = CreateUIObject($"OVVal_{i}", _optVideoPanel.transform);
                var valR  = valGo.GetComponent<RectTransform>();
                valR.anchorMin = new Vector2(0.46f, 1f); valR.anchorMax = new Vector2(0.90f, 1f);
                valR.pivot = new Vector2(0.5f, 0.5f);
                valR.anchoredPosition = new Vector2(0f, cy);
                valR.sizeDelta = new Vector2(0f, rowH);
                var valTMP = valGo.AddComponent<TextMeshProUGUI>();
                valTMP.fontSize = 18f; valTMP.alignment = TextAlignmentOptions.Center;
                valTMP.color = AccentGold;
                valTMP.raycastTarget = false;
                _optVideoValues[i] = valTMP;

                // Arrow buttons — mouse parity with the keyboard left/right nudge.
                if (i != OptVideoRowApply)
                {
                    AddOptVideoArrow(_optVideoPanel.transform, $"OVLeft_{i}", "<",
                        new Vector2(0.42f, 0.46f), cy, rowH, () => ChangeOptVideo(cap, -1));
                    AddOptVideoArrow(_optVideoPanel.transform, $"OVRight_{i}", ">",
                        new Vector2(0.90f, 0.96f), cy, rowH, () => ChangeOptVideo(cap, +1));
                }
            }

            // Live viewport readout. This is the number that actually decides
            // whether a seam can appear, so it's worth showing verbatim rather
            // than echoing back the setting the player just picked.
            var statusGo = CreateUIObject("OVStatus", _optVideoPanel.transform);
            var statusR  = statusGo.GetComponent<RectTransform>();
            statusR.anchorMin = new Vector2(0f, 0f); statusR.anchorMax = new Vector2(1f, 0f);
            statusR.pivot = new Vector2(0.5f, 0f);
            statusR.anchoredPosition = new Vector2(0f, 40f);
            statusR.sizeDelta = new Vector2(0f, 42f);
            _optVideoStatus = statusGo.AddComponent<TextMeshProUGUI>();
            _optVideoStatus.fontSize = 14f;
            _optVideoStatus.alignment = TextAlignmentOptions.Center;
            _optVideoStatus.color = VersionCol;
            _optVideoStatus.raycastTarget = false;

            AddOptHint(_optVideoPanel.transform, "<- -> Change  |  Enter Apply  |  Esc Back", panelH);

            LoadOptVideoFromSettings();
            RefreshOptVideoRows();
        }

        private void AddOptVideoArrow(Transform parent, string name, string glyph,
            Vector2 anchorX, float cy, float rowH, UnityEngine.Events.UnityAction onClick)
        {
            var go = CreateUIObject(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(anchorX.x, 1f); rt.anchorMax = new Vector2(anchorX.y, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, cy);
            rt.sizeDelta = new Vector2(0f, rowH - 6f);

            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.04f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            // Image + TMP on the same GameObject NREs — the glyph is a child.
            var txtGo = CreateUIObject(name + "_T", go.transform);
            var txtR  = txtGo.GetComponent<RectTransform>();
            txtR.anchorMin = Vector2.zero; txtR.anchorMax = Vector2.one;
            txtR.offsetMin = Vector2.zero; txtR.offsetMax = Vector2.zero;
            var tmp = txtGo.AddComponent<TextMeshProUGUI>();
            tmp.text = glyph; tmp.fontSize = 20f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = TextNormal;
            tmp.raycastTarget = false;
        }

        private void AttachOptVideoRowHover(GameObject target, int rowIndex)
        {
            var trig  = target.GetComponent<EventTrigger>() ?? target.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            entry.callback.AddListener(_ => { _optVideoSel = rowIndex; UpdateOptVideoVisuals(); });
            trig.triggers.Add(entry);
        }

        // ════════════════════════════════════════════════════════════════════
        // State
        // ════════════════════════════════════════════════════════════════════

        private void LoadOptVideoFromSettings()
        {
            var gs = GameSettings.Instance;
            _optVideoResIndex  = gs != null
                ? DisplaySettings.ClampIndex(DisplaySettings.IndexOf(gs.resolutionWidth, gs.resolutionHeight))
                : 0;
            _optVideoModeIndex = gs != null
                ? Mathf.Clamp((int)gs.windowMode, 0, DisplaySettings.WindowModeLabels.Length - 1)
                : 0;
        }

        private void ChangeOptVideo(int row, int dir)
        {
            if (row == OptVideoRowResolution)
            {
                int n = DisplaySettings.Presets.Length;
                _optVideoResIndex = ((_optVideoResIndex + dir) % n + n) % n;
            }
            else if (row == OptVideoRowMode)
            {
                int n = DisplaySettings.WindowModeLabels.Length;
                _optVideoModeIndex = ((_optVideoModeIndex + dir) % n + n) % n;
            }
            else return;

            _optVideoSel = row;
            UpdateOptVideoVisuals();
            RefreshOptVideoRows();
        }

        private void ApplyOptVideo()
        {
            var gs = GameSettings.Instance;
            if (gs == null) return;

            var preset = DisplaySettings.Presets[DisplaySettings.ClampIndex(_optVideoResIndex)];
            gs.resolutionWidth  = preset.Width;
            gs.resolutionHeight = preset.Height;
            gs.windowMode       = (WindowMode)_optVideoModeIndex;
            gs.Save();
            DisplaySettings.Apply(gs);
            RefreshOptVideoRows();
        }

        private void RefreshOptVideoRows()
        {
            if (_optVideoValues == null) return;

            var preset = DisplaySettings.Presets[DisplaySettings.ClampIndex(_optVideoResIndex)];
            _optVideoValues[OptVideoRowResolution].text = preset.Label;
            _optVideoValues[OptVideoRowMode].text =
                DisplaySettings.WindowModeLabel((WindowMode)_optVideoModeIndex);
            _optVideoValues[OptVideoRowApply].text = "Enter";

            if (_optVideoStatus == null) return;

            var cam = Camera.main;
            string viewport = cam != null
                ? $"{cam.pixelWidth} x {cam.pixelHeight}"
                : $"{Screen.width} x {Screen.height}";
            string exact = cam != null && cam.pixelHeight > 0
                && cam.pixelWidth == Mathf.RoundToInt(cam.pixelHeight * DisplaySettings.TargetAspect)
                ? "exact 2:1 — seam-free"
                : "not 2:1 — seams possible";
            string editorNote = Application.isEditor
                ? "\nEditor: the Game View size wins; set it to a fixed 2:1 size."
                : string.Empty;
            _optVideoStatus.text = $"Window {Screen.width} x {Screen.height}  |  viewport {viewport}  ({exact}){editorNote}";
        }

        private void UpdateOptVideoVisuals()
        {
            if (_optVideoPills == null) return;
            for (int i = 0; i < _optVideoPills.Length; i++)
            {
                bool s = i == _optVideoSel;
                _optVideoPills[i].color = s ? PillColor  : Color.clear;
                _optVideoBars[i].color  = s ? AccentGold : Color.clear;
                if (_optVideoLabels != null && i < _optVideoLabels.Length)
                    _optVideoLabels[i].color = s ? TextSelected : TextNormal;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Input
        // ════════════════════════════════════════════════════════════════════

        private void HandleOptionsVideoInput()
        {
            if (Valkur.Core.Input.InputCompat.NavUpPressed())
            { _optVideoSel = (_optVideoSel - 1 + OptVideoRowCount) % OptVideoRowCount; UpdateOptVideoVisuals(); }
            else if (Valkur.Core.Input.InputCompat.NavDownPressed())
            { _optVideoSel = (_optVideoSel + 1) % OptVideoRowCount; UpdateOptVideoVisuals(); }
            else if (Valkur.Core.Input.InputCompat.NavLeftPressed())
            { ChangeOptVideo(_optVideoSel, -1); }
            else if (Valkur.Core.Input.InputCompat.NavRightPressed())
            { ChangeOptVideo(_optVideoSel, +1); }
            else if (Valkur.Core.Input.InputCompat.ConfirmPressed())
            { ApplyOptVideo(); }
            else if (Valkur.Core.Input.InputCompat.CancelPressed())
            { OptionsGoBack(); }
        }
    }
}
