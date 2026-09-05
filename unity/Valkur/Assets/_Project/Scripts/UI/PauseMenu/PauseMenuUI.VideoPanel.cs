using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;

namespace Valkur.UI.PauseMenu
{
    /// <summary>
    /// Pause → Options → Video. Mirrors the main menu's Video panel row for
    /// row so a player who set the resolution before starting finds the same
    /// screen mid-run.
    ///
    /// Every offered size is exactly the camera's 2:1 target aspect, so
    /// <see cref="AspectRatioEnforcer"/> has nothing to letterbox and
    /// <c>Camera.pixelWidth / pixelHeight</c> is bit-exact — the condition
    /// under which one art texel covers a whole number of screen pixels on
    /// BOTH axes and the tilemap seam lines cannot appear. See
    /// <see cref="DisplaySettings"/> for the full reasoning.
    /// </summary>
    public partial class PauseMenuUI
    {
        private GameObject _videoPanel;

        private int _videoResIndex;
        private int _videoModeIndex;

        private const int VideoRowResolution = 0;
        private const int VideoRowMode       = 1;
        private const int VideoRowApply      = 2;
        private const int VideoRowCount      = 3;

        private int _videoSel;
        private Image[] _videoPills;
        private Image[] _videoBars;
        private TextMeshProUGUI[] _videoLabels;
        private TextMeshProUGUI[] _videoValues;
        private TextMeshProUGUI _videoStatus;

        // ── Builder ──────────────────────────────────────────────────────────

        private GameObject BuildVideoPanel(Transform parent)
        {
            var labels = new[] { "Resolution", "Display mode", "Apply" };

            const float rowH   = 44f;
            const float padX   = 20f;
            const float padY   = 16f;
            const float gap    = 8f;
            const float panelW = 540f;
            float panelH = padY * 2 + VideoRowCount * rowH + (VideoRowCount - 1) * gap + 118f;

            var panel = CreateUIObject("VideoPanel", parent);
            var r     = panel.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0.5f, 0.5f); r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f); r.anchoredPosition = Vector2.zero;
            r.sizeDelta = new Vector2(panelW, panelH);
            panel.AddComponent<Image>().color = PanelBg;

            AddPanelTitle(panel.transform, "Video Options", panelH, padX);

            _videoPills  = new Image[VideoRowCount];
            _videoBars   = new Image[VideoRowCount];
            _videoLabels = new TextMeshProUGUI[VideoRowCount];
            _videoValues = new TextMeshProUGUI[VideoRowCount];

            for (int i = 0; i < VideoRowCount; i++)
            {
                float cy  = -58f - i * (rowH + gap) - rowH * 0.5f;
                int   cap = i;

                var pillGo = CreateUIObject($"VPill_{i}", panel.transform);
                SetRowRect(pillGo, cy, rowH, 0f);
                _videoPills[i] = pillGo.AddComponent<Image>();
                _videoPills[i].color = Color.clear;
                AttachVideoRowHoverSelect(pillGo, cap);
                if (i == VideoRowApply)
                {
                    var applyBtn = pillGo.AddComponent<Button>();
                    applyBtn.targetGraphic = _videoPills[i];
                    var ac = applyBtn.colors;
                    ac.normalColor = Color.clear; ac.highlightedColor = Color.clear;
                    ac.pressedColor = new Color(1f, 1f, 1f, 0.05f); ac.selectedColor = Color.clear;
                    applyBtn.colors = ac;
                    applyBtn.onClick.AddListener(ApplyVideo);
                }

                var barGo = CreateUIObject($"VBar_{i}", panel.transform);
                var barR  = barGo.GetComponent<RectTransform>();
                barR.anchorMin = new Vector2(0f, 1f); barR.anchorMax = new Vector2(0f, 1f);
                barR.pivot = new Vector2(0f, 0.5f);
                barR.anchoredPosition = new Vector2(0f, cy);
                barR.sizeDelta = new Vector2(4f, rowH - 4f);
                _videoBars[i] = barGo.AddComponent<Image>();
                _videoBars[i].color = Color.clear;
                _videoBars[i].raycastTarget = false;

                var lblGo = CreateUIObject($"VLabel_{i}", panel.transform);
                var lblR  = lblGo.GetComponent<RectTransform>();
                lblR.anchorMin = new Vector2(0f, 1f); lblR.anchorMax = new Vector2(0.42f, 1f);
                lblR.pivot = new Vector2(0f, 0.5f);
                lblR.anchoredPosition = new Vector2(padX + 12f, cy);
                lblR.sizeDelta = new Vector2(0f, rowH);
                var lblTMP = lblGo.AddComponent<TextMeshProUGUI>();
                lblTMP.text = labels[i]; lblTMP.fontSize = 18f;
                lblTMP.alignment = TextAlignmentOptions.Left; lblTMP.color = TextNormal;
                lblTMP.raycastTarget = false;
                _videoLabels[i] = lblTMP;

                var valGo = CreateUIObject($"VVal_{i}", panel.transform);
                var valR  = valGo.GetComponent<RectTransform>();
                valR.anchorMin = new Vector2(0.46f, 1f); valR.anchorMax = new Vector2(0.90f, 1f);
                valR.pivot = new Vector2(0.5f, 0.5f);
                valR.anchoredPosition = new Vector2(0f, cy);
                valR.sizeDelta = new Vector2(0f, rowH);
                var valTMP = valGo.AddComponent<TextMeshProUGUI>();
                valTMP.fontSize = 18f; valTMP.alignment = TextAlignmentOptions.Center;
                valTMP.color = AccentGold;
                valTMP.raycastTarget = false;
                _videoValues[i] = valTMP;

                if (i != VideoRowApply)
                {
                    AddVideoArrow(panel.transform, $"VLeft_{i}", "<",
                        new Vector2(0.42f, 0.46f), cy, rowH, () => ChangeVideo(cap, -1));
                    AddVideoArrow(panel.transform, $"VRight_{i}", ">",
                        new Vector2(0.90f, 0.96f), cy, rowH, () => ChangeVideo(cap, +1));
                }
            }

            var statusGo = CreateUIObject("VStatus", panel.transform);
            var statusR  = statusGo.GetComponent<RectTransform>();
            statusR.anchorMin = new Vector2(0f, 0f); statusR.anchorMax = new Vector2(1f, 0f);
            statusR.pivot = new Vector2(0.5f, 0f);
            statusR.anchoredPosition = new Vector2(0f, 40f);
            statusR.sizeDelta = new Vector2(0f, 42f);
            _videoStatus = statusGo.AddComponent<TextMeshProUGUI>();
            _videoStatus.fontSize = 14f;
            _videoStatus.alignment = TextAlignmentOptions.Center;
            _videoStatus.color = VersionCol;
            _videoStatus.raycastTarget = false;

            AddHint(panel.transform, "<- -> Change  |  Enter Apply  |  Esc Back", panelH);

            LoadVideoFromSettings();
            RefreshVideoRows();
            return panel;
        }

        private void AddVideoArrow(Transform parent, string name, string glyph,
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

        private void AttachVideoRowHoverSelect(GameObject target, int rowIndex)
        {
            var trig  = target.GetComponent<EventTrigger>() ?? target.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            entry.callback.AddListener(_ => { _videoSel = rowIndex; UpdateVideoPanel(); });
            trig.triggers.Add(entry);
        }

        // ── State ────────────────────────────────────────────────────────────

        private void LoadVideoFromSettings()
        {
            var gs = GameSettings.Instance;
            _videoResIndex = gs != null
                ? DisplaySettings.ClampIndex(DisplaySettings.IndexOf(gs.resolutionWidth, gs.resolutionHeight))
                : 0;
            _videoModeIndex = gs != null
                ? Mathf.Clamp((int)gs.windowMode, 0, DisplaySettings.WindowModeLabels.Length - 1)
                : 0;
        }

        private void ChangeVideo(int row, int dir)
        {
            if (row == VideoRowResolution)
            {
                int n = DisplaySettings.Presets.Length;
                _videoResIndex = ((_videoResIndex + dir) % n + n) % n;
            }
            else if (row == VideoRowMode)
            {
                int n = DisplaySettings.WindowModeLabels.Length;
                _videoModeIndex = ((_videoModeIndex + dir) % n + n) % n;
            }
            else return;

            _videoSel = row;
            UpdateVideoPanel();
            RefreshVideoRows();
        }

        private void ApplyVideo()
        {
            var gs = GameSettings.Instance;
            if (gs == null) return;

            var preset = DisplaySettings.Presets[DisplaySettings.ClampIndex(_videoResIndex)];
            gs.resolutionWidth  = preset.Width;
            gs.resolutionHeight = preset.Height;
            gs.windowMode       = (WindowMode)_videoModeIndex;
            gs.Save();
            DisplaySettings.Apply(gs);
            RefreshVideoRows();
        }

        private void RefreshVideoRows()
        {
            if (_videoValues == null) return;

            var preset = DisplaySettings.Presets[DisplaySettings.ClampIndex(_videoResIndex)];
            _videoValues[VideoRowResolution].text = preset.Label;
            _videoValues[VideoRowMode].text =
                DisplaySettings.WindowModeLabel((WindowMode)_videoModeIndex);
            _videoValues[VideoRowApply].text = "Enter";

            if (_videoStatus == null) return;

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
            _videoStatus.text = $"Window {Screen.width} x {Screen.height}  |  viewport {viewport}  ({exact}){editorNote}";
        }

        private void UpdateVideoPanel()
        {
            if (_videoPills == null || _videoBars == null) return;
            for (int i = 0; i < _videoPills.Length; i++)
            {
                bool s = i == _videoSel;
                _videoPills[i].color = s ? PillColor  : Color.clear;
                _videoBars[i].color  = s ? AccentGold : Color.clear;
                if (_videoLabels != null && i < _videoLabels.Length)
                    _videoLabels[i].color = s ? TextSelected : TextNormal;
            }
        }

        // ── Input ────────────────────────────────────────────────────────────

        private void HandleVideoInput()
        {
            if (NavUpPressed())
            { _videoSel = (_videoSel - 1 + VideoRowCount) % VideoRowCount; UpdateVideoPanel(); }
            else if (NavDownPressed())
            { _videoSel = (_videoSel + 1) % VideoRowCount; UpdateVideoPanel(); }
            else if (NavLeftPressed())
            { ChangeVideo(_videoSel, -1); }
            else if (NavRightPressed())
            { ChangeVideo(_videoSel, +1); }
            else if (ConfirmPressed())
            { ApplyVideo(); }
            else if (CancelPressed())
            { GoBack(); }
        }
    }
}
