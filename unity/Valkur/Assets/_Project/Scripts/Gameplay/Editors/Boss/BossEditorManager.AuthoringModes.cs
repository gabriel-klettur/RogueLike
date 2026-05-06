using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.Infrastructure;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.Boss
{
    /// <summary>
    /// Authoring modes for the Boss Editor Cue Inspector.
    ///
    /// Mode picker appears above the chart header inside the Cue Inspector.
    /// Four modes:
    ///   Numeric  — existing per-cue rows (default).
    ///   Tap      — Space records a new cue at the current clock position.
    ///   Quantize — 16 or 32-cell step-sequencer grid.
    ///   Auto     — surfaces the "Import beats from track" flow with a confirm.
    /// </summary>
    public partial class BossEditorManager
        : SingletonMonoBehaviour<BossEditorManager>, GameEditorManager.IGameEditor
    {
        // ── Mode state ─────────────────────────────────────────────────────────

        private enum AuthoringMode { Numeric, Tap, Quantize, Auto }
        private AuthoringMode _authoringMode = AuthoringMode.Numeric;

        // Quantize grid buttons — rebuilt on RefreshCuesPanel (same lifetime
        // as the rest of the cues content).
        private readonly List<Image> _quantizeCellImages = new List<Image>();

        // ── Update hook ────────────────────────────────────────────────────────

        // Called from the main Update() while the editor is active.
        private void TickAuthoringModes()
        {
            if (_authoringMode != AuthoringMode.Tap) return;
            if (_selectedChart == null) return;

            var clock = MusicBeatClock.Instance;
            if (clock == null || !clock.IsActive) return;

            if (KeyboardInputManager.WasKeyPressedThisFrame(Key.Space, KeyCode.Space))
                TapRecordCue(clock);
        }

        // ── Tap record ────────────────────────────────────────────────────────

        private void TapRecordCue(MusicBeatClock clock)
        {
            int beatsPerBar = Mathf.Max(1, clock.BeatsPerBar);
            int beatIndex   = clock.CurrentBeat;
            int bar         = beatIndex / beatsPerBar;
            int beat        = beatIndex % beatsPerBar;
            float frac      = clock.BeatPhase01;

            // Fold bar into the chart's barsPerLoop window.
            int barsPerLoop = Mathf.Max(1, _selectedChart.barsPerLoop);
            bar = bar % barsPerLoop;

            var newCue = new BossCue
            {
                bar          = bar,
                beat         = beat,
                beatFraction = frac,
                type         = BossCueType.CastSpell,
                targeting    = BossCueTargeting.ToPlayer,
                targetKey    = "",
                payload      = 0f,
                note         = "tap",
            };

            var chart    = _selectedChart;
            int insertAt = chart.cues.Count;

            _undo.Do("Tap Cue",
                () =>
                {
                    chart.cues.Add(newCue);
                    MarkDirty(chart);
                    _selectedCueIndex = chart.cues.Count - 1;
                    RefreshCuesPanel();
                },
                () =>
                {
                    if (chart.cues.Count > insertAt) chart.cues.RemoveAt(insertAt);
                    MarkDirty(chart);
                    _selectedCueIndex = Mathf.Clamp(_selectedCueIndex, -1, chart.cues.Count - 1);
                    RefreshCuesPanel();
                });

            RefreshUndoRedoButtons();
            SetStatus($"Tap recorded at bar {bar} beat {beat}.{frac:F2}");
        }

        // ── Quantize grid ─────────────────────────────────────────────────────

        /// <summary>Builds a step-sequencer grid below the mode picker.</summary>
        private void BuildQuantizeGrid(RectTransform parent)
        {
            if (_selectedChart == null) return;

            var clock = MusicBeatClock.Instance;
            int beatsPerBar  = clock != null ? Mathf.Max(1, clock.BeatsPerBar) : 4;
            int barsPerLoop  = Mathf.Max(1, _selectedChart.barsPerLoop);
            int totalSlots   = Mathf.Min(barsPerLoop * beatsPerBar, 32);

            _quantizeCellImages.Clear();

            var headerGo = EditorUIHelpers.CreateUI("QuantizeHdr", parent);
            headerGo.AddComponent<LayoutElement>().preferredHeight = 20f;
            var headerTmp = headerGo.AddComponent<TextMeshProUGUI>();
            headerTmp.text      = $"Step sequencer — {totalSlots} slots ({barsPerLoop} bars × {beatsPerBar} beats)";
            headerTmp.fontSize  = 9f;
            headerTmp.color     = EditorUIHelpers.TEXT_SECONDARY;
            headerTmp.alignment = TextAlignmentOptions.MidlineLeft;

            // Row of toggle cells — wrap at 16.
            int cols = Mathf.Min(totalSlots, 16);
            int rows = Mathf.CeilToInt(totalSlots / (float)cols);

            for (int row = 0; row < rows; row++)
            {
                var rowGo = EditorUIHelpers.CreateUI($"QuantizeRow{row}", parent);
                rowGo.AddComponent<LayoutElement>().preferredHeight = 22f;
                var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing = 2f;
                hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
                hlg.childControlWidth = true;      hlg.childControlHeight = true;

                for (int col = 0; col < cols; col++)
                {
                    int slot = row * cols + col;
                    if (slot >= totalSlots) break;

                    int bar   = slot / beatsPerBar;
                    int beat  = slot % beatsPerBar;
                    bool hasC = ChartHasCueAt(bar, beat);

                    var cellGo = EditorUIHelpers.CreateUI($"QCell_{slot}", rowGo.transform);
                    cellGo.AddComponent<LayoutElement>().preferredWidth = 18f;

                    var cellImg = cellGo.AddComponent<Image>();
                    cellImg.color = hasC ? EditorUIHelpers.ACCENT : new Color(0.18f, 0.18f, 0.22f, 1f);
                    _quantizeCellImages.Add(cellImg);

                    var btn = cellGo.AddComponent<Button>();
                    btn.targetGraphic = cellImg;
                    int capturedSlot = slot;
                    int capturedBar  = bar;
                    int capturedBeat = beat;
                    btn.onClick.AddListener(() => ToggleQuantizeSlot(capturedBar, capturedBeat));

                    // Beat label: bar.beat
                    var lblGo = EditorUIHelpers.CreateUI("Lbl", cellGo.transform);
                    UIFactory.StretchFill(lblGo);
                    var lbl = lblGo.AddComponent<TextMeshProUGUI>();
                    lbl.text              = $"{bar}.{beat}";
                    lbl.fontSize          = 6f;
                    lbl.color             = EditorUIHelpers.TEXT_MUTED;
                    lbl.alignment         = TextAlignmentOptions.Center;
                    lbl.enableWordWrapping = false;
                }
            }
        }

        private bool ChartHasCueAt(int bar, int beat)
        {
            if (_selectedChart == null) return false;
            foreach (var c in _selectedChart.cues)
                if (c.bar == bar && c.beat == beat) return true;
            return false;
        }

        private void ToggleQuantizeSlot(int bar, int beat)
        {
            if (_selectedChart == null) return;
            var chart = _selectedChart;

            // Find first cue at this slot (if any).
            int found = -1;
            for (int i = 0; i < chart.cues.Count; i++)
                if (chart.cues[i].bar == bar && chart.cues[i].beat == beat) { found = i; break; }

            if (found >= 0)
            {
                // Toggle off — delete it.
                var removed = chart.cues[found];
                _undo.Do($"Quantize off {bar}.{beat}",
                    () => { chart.cues.RemoveAt(found); MarkDirty(chart); RefreshCuesPanel(); },
                    () => { chart.cues.Insert(found, removed); MarkDirty(chart); RefreshCuesPanel(); });
            }
            else
            {
                // Toggle on — add new cue.
                var newCue = new BossCue
                {
                    bar = bar, beat = beat, beatFraction = 0f,
                    type = BossCueType.CastSpell,
                    targeting = BossCueTargeting.ToPlayer,
                    targetKey = "", payload = 0f, note = "quantize",
                };
                int insertAt = chart.cues.Count;
                _undo.Do($"Quantize on {bar}.{beat}",
                    () => { chart.cues.Add(newCue); MarkDirty(chart); RefreshCuesPanel(); },
                    () =>
                    {
                        if (chart.cues.Count > insertAt) chart.cues.RemoveAt(insertAt);
                        MarkDirty(chart);
                        RefreshCuesPanel();
                    });
            }
            RefreshUndoRedoButtons();
        }

        // ── Auto mode ─────────────────────────────────────────────────────────

        private void BuildAutoModePanel(RectTransform parent)
        {
            if (_selectedChart == null) return;

            var catalog = GetAudioCatalog();
            MusicTrackEntry track = catalog?.GetTrack(_selectedChart.musicTrackId);

            int beatCount = track?.beatTimes != null ? track.beatTimes.Length : 0;
            int barsPerLoop = Mathf.Max(1, _selectedChart.barsPerLoop);
            int beatsPerBar = (track != null && track.beatsPerBar > 0) ? track.beatsPerBar : 4;
            int slotsInLoop = beatsPerBar * barsPerLoop;
            int willFill    = beatCount > 0 ? Mathf.Min(beatCount, slotsInLoop) : 0;

            string info = beatCount > 0
                ? $"Import {willFill} beats from '{_selectedChart.musicTrackId}' " +
                  $"({barsPerLoop} bars × {beatsPerBar} beats/bar).\n" +
                  $"Existing cues at matching slots will be skipped."
                : $"Track '{_selectedChart.musicTrackId}' has no analysed beatTimes.\n" +
                  "Run analyze_music.py first or set the Track ID correctly.";

            var infoGo = EditorUIHelpers.CreateUI("AutoInfo", parent);
            infoGo.AddComponent<LayoutElement>().preferredHeight = 44f;
            var infoTmp = infoGo.AddComponent<TextMeshProUGUI>();
            infoTmp.text             = info;
            infoTmp.fontSize         = 9f;
            infoTmp.color            = EditorUIHelpers.TEXT_SECONDARY;
            infoTmp.enableWordWrapping = true;
            infoTmp.alignment        = TextAlignmentOptions.TopLeft;

            if (beatCount > 0)
            {
                var btnRow = EditorUIHelpers.CreateUI("AutoBtnRow", parent);
                btnRow.AddComponent<LayoutElement>().preferredHeight = 26f;
                var hlg = btnRow.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing = 4f; hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;
                hlg.childControlWidth = true; hlg.childControlHeight = true;
                EditorUIHelpers.AddActionBtn(btnRow.transform, $"Import {willFill} beats", 26f,
                    () => ImportBeatsFromActiveTrack(), out _);
            }
        }

        // ── Mode picker widget (injected into the Cue Inspector header) ────────

        internal void BuildModePicker(RectTransform parent)
        {
            var row = EditorUIHelpers.CreateUI("ModePicker", parent);
            row.AddComponent<LayoutElement>().preferredHeight = 26f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 3f; hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true; hlg.childControlHeight = true;

            var lbl = EditorUIHelpers.AddLabel(row.transform, "Mode:", 9f);
            lbl.color = EditorUIHelpers.TEXT_SECONDARY;
            (lbl.gameObject.GetComponent<LayoutElement>() ??
             lbl.gameObject.AddComponent<LayoutElement>()).preferredWidth = 38f;

            AuthoringMode[] modes  = { AuthoringMode.Numeric, AuthoringMode.Tap, AuthoringMode.Quantize, AuthoringMode.Auto };
            string[]        labels = { "Numeric", "Tap", "Quantize", "Auto" };

            for (int i = 0; i < modes.Length; i++)
            {
                AuthoringMode capturedMode = modes[i];
                string        capturedLbl  = labels[i];
                bool          isCurrent    = _authoringMode == capturedMode;

                var btnGo = EditorUIHelpers.CreateUI($"ModeBtn_{capturedLbl}", row.transform);
                (btnGo.GetComponent<LayoutElement>() ??
                 btnGo.AddComponent<LayoutElement>()).preferredWidth = 56f;
                var btnImg = btnGo.AddComponent<Image>();
                // BTN_ACTIVE for the current mode — matches ParticlesRuntimeEditor.RefreshModeButtons.
                btnImg.color = isCurrent ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
                var btn = btnGo.AddComponent<Button>();
                btn.targetGraphic = btnImg;
                var c = btn.colors;
                c.normalColor      = btnImg.color;
                c.highlightedColor = EditorUIHelpers.BTN_HOVER;
                c.pressedColor     = EditorUIHelpers.BTN_ACTIVE;
                btn.colors         = c;
                btn.onClick.AddListener(() =>
                {
                    _authoringMode = capturedMode;
                    RefreshCuesPanel();
                    SetStatus($"Mode: {capturedLbl}");
                });

                UILabel.AddCenteredText(btnGo.transform, capturedLbl, 8f, TMPro.FontStyles.Normal,
                    isCurrent ? EditorUIHelpers.ACCENT : EditorUIHelpers.TEXT_PRIMARY);
            }
        }

        /// <summary>
        /// Called by RefreshCuesPanel after the chart header and before the cue
        /// rows to inject the mode-specific UI.
        /// </summary>
        internal void BuildModeSpecificContent(RectTransform parent)
        {
            switch (_authoringMode)
            {
                case AuthoringMode.Quantize: BuildQuantizeGrid(parent); break;
                case AuthoringMode.Auto:     BuildAutoModePanel(parent); break;
                // Numeric and Tap use no extra panel; Tap hint is shown in status.
                case AuthoringMode.Tap:
                    var hintGo = EditorUIHelpers.CreateUI("TapHint", parent);
                    hintGo.AddComponent<LayoutElement>().preferredHeight = 20f;
                    var hintTmp = hintGo.AddComponent<TextMeshProUGUI>();
                    hintTmp.text      = "Press SPACE while music plays to record a cue at the current beat.";
                    hintTmp.fontSize  = 9f;
                    hintTmp.color     = EditorUIHelpers.ACCENT_DIM;
                    hintTmp.alignment = TextAlignmentOptions.MidlineLeft;
                    hintTmp.enableWordWrapping = true;
                    break;
            }
        }
    }
}
