using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.Boss
{
    /// <summary>
    /// UI construction and panel-refresh methods for the Boss Editor.
    /// Mirrors the Particles / Entities pattern: menu-bar + 3 draggable panels.
    /// </summary>
    public partial class BossEditorManager
        : SingletonMonoBehaviour<BossEditorManager>, GameEditorManager.IGameEditor
    {
        // ── UI Construction ─────────────────────────────────────────────────────

        private void BuildUI()
        {
            _canvas = EditorUIHelpers.CreateEditorCanvas("BossEditorCanvas", 110);
            _canvas.transform.SetParent(transform, false);

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            EditorUIHelpers.StretchFill(_root);

            _ui = BossEditorUIBuilder.BuildAll(
                _root.transform,
                onDropdownToggle:    ToggleDropdown,
                onUndo:              () => { _undo.Undo(); RefreshUndoRedoButtons(); SetStatus("Undo"); },
                onRedo:              () => { _undo.Redo(); RefreshUndoRedoButtons(); SetStatus("Redo"); },
                onSaveChart:         SaveSelectedChart,
                onAddPhase:          AddPhase,
                onAddChart:          AddChart,
                onAddCue:            AddCue,
                onToggleTutorial:    ToggleTutorial,
                onToggleLivePreview: ToggleLivePreview);

            SetPreviewButtonRefs(_ui.PreviewBtnImg, _ui.PreviewBtnTmp);

            // Wire panel-close callbacks.
            if (_ui.BossesPanelDrag != null) _ui.BossesPanelDrag.OnClose = () => { _openDropdowns.Remove("bosses"); RefreshMenuBtnHighlights(); };
            if (_ui.PhasesPanelDrag != null) _ui.PhasesPanelDrag.OnClose = () => { _openDropdowns.Remove("phases"); RefreshMenuBtnHighlights(); };
            if (_ui.CuesPanelDrag   != null) _ui.CuesPanelDrag.OnClose   = () => { _openDropdowns.Remove("cues");   RefreshMenuBtnHighlights(); };

            BuildTutorial();
            BuildConfirmModal();
        }

        // ── Tutorial ───────────────────────────────────────────────────────────

        private void BuildTutorial()
        {
            _tutorialRoot = EditorUIHelpers.MakePanel("Tutorial", _root.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(520f, 240f));
            var vlg = _tutorialRoot.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(16, 16, 14, 14);
            vlg.spacing = 8f; vlg.childForceExpandWidth = true;

            EditorUIHelpers.MakeTitleBar(_tutorialRoot.transform, "BOSS EDITOR TUTORIAL");

            _tutorialStepLabel = EditorUIHelpers.AddLabel(_tutorialRoot.transform, "", 14f);
            _tutorialStepLabel.fontStyle = FontStyles.Bold;
            _tutorialStepLabel.color     = EditorUIHelpers.ACCENT;

            var bodyGo = EditorUIHelpers.CreateUI("Body", _tutorialRoot.transform);
            bodyGo.AddComponent<LayoutElement>().flexibleHeight = 1f;
            _tutorialBodyTmp = bodyGo.AddComponent<TextMeshProUGUI>();
            _tutorialBodyTmp.fontSize           = 12f;
            _tutorialBodyTmp.color              = EditorUIHelpers.TEXT_PRIMARY;
            _tutorialBodyTmp.alignment          = TextAlignmentOptions.TopLeft;
            _tutorialBodyTmp.enableWordWrapping = true;

            var nav = EditorUIHelpers.CreateUI("Nav", _tutorialRoot.transform);
            nav.AddComponent<LayoutElement>().preferredHeight = 32f;
            var hlg = nav.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f; hlg.childForceExpandWidth = true;
            EditorUIHelpers.MakeButton(nav.transform, "Prev",  () => StepTutorial(-1), 28f, 12f);
            EditorUIHelpers.MakeButton(nav.transform, "Next",  () => StepTutorial(+1), 28f, 12f);
            EditorUIHelpers.MakeButton(nav.transform, "Close", () => _tutorialRoot.SetActive(false), 28f, 12f);

            _tutorialStep = 0;
            RefreshTutorial();
            _tutorialRoot.SetActive(false);
        }

        private void ToggleTutorial()
        {
            if (_tutorialRoot == null) return;
            bool show = !_tutorialRoot.activeSelf;
            _tutorialRoot.SetActive(show);
            if (show) { _tutorialRoot.transform.SetAsLastSibling(); RefreshTutorial(); }
        }

        private void StepTutorial(int delta)
        {
            _tutorialStep = (_tutorialStep + delta + TUTORIAL_STEPS.Length) % TUTORIAL_STEPS.Length;
            RefreshTutorial();
        }

        private void RefreshTutorial()
        {
            if (_tutorialStepLabel == null) return;
            var (title, body) = TUTORIAL_STEPS[_tutorialStep];
            _tutorialStepLabel.text = $"{title}   ({_tutorialStep + 1}/{TUTORIAL_STEPS.Length})";
            _tutorialBodyTmp.text   = body;
        }

        // ── Confirm-delete modal ───────────────────────────────────────────────

        private void BuildConfirmModal()
        {
            _confirmModal = EditorUIHelpers.MakePanel("ConfirmModal", _root.transform,
                new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            var bgImg = _confirmModal.GetComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 140f / 255f);

            var inner = EditorUIHelpers.MakePanel("Inner", _confirmModal.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(520f, 200f));
            var vlg = inner.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(20, 20, 18, 18);
            vlg.spacing = 12f; vlg.childForceExpandWidth = true;
            EditorUIHelpers.MakeTitleBar(inner.transform, "CONFIRM DELETE");

            _confirmText = EditorUIHelpers.AddLabel(inner.transform, "?", 13f);
            _confirmText.color     = EditorUIHelpers.TEXT_PRIMARY;
            _confirmText.alignment = TextAlignmentOptions.MidlineLeft;
            _confirmText.richText  = true;

            var btnRow = EditorUIHelpers.CreateUI("Btns", inner.transform);
            btnRow.AddComponent<LayoutElement>().preferredHeight = 36f;
            var hlg = btnRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12f; hlg.childForceExpandWidth = true;
            EditorUIHelpers.MakeDangerButton(btnRow.transform, "Delete",
                () => { var cb = _pendingConfirmYes; HideConfirm(); cb?.Invoke(); }, 32f);
            EditorUIHelpers.MakeButton(btnRow.transform, "Cancel",
                () => HideConfirm(), 32f, 12f);

            _confirmModal.SetActive(false);
        }

        private void ShowConfirm(string text, System.Action onYes)
        {
            if (_confirmModal == null) { onYes?.Invoke(); return; }
            _confirmText.text    = text;
            _pendingConfirmYes   = onYes;
            _confirmModal.SetActive(true);
            _confirmModal.transform.SetAsLastSibling();
        }

        private void HideConfirm()
        {
            _pendingConfirmYes = null;
            if (_confirmModal != null) _confirmModal.SetActive(false);
        }

        // ── Panel refresh ──────────────────────────────────────────────────────

        private void RefreshBossList()
        {
            if (_ui.BossListContent == null) return;
            RebuildBossCache();

            // Clear existing rows.
            for (int i = _ui.BossListContent.childCount - 1; i >= 0; i--)
                Object.Destroy(_ui.BossListContent.GetChild(i).gameObject);

            if (_allBossDefs == null || _allBossDefs.Length == 0)
            {
                var hint = EditorUIHelpers.AddLabel(_ui.BossListContent, "No BossDefinition assets found.", 11f);
                hint.color = EditorUIHelpers.TEXT_MUTED;
                hint.enableWordWrapping = true;
                return;
            }

            foreach (var def in _allBossDefs)
            {
                if (def == null) continue;
                var captured = def;
                bool isSelected = def == _selectedBoss;
                int phases = def.phases != null ? def.phases.Length : 0;

                var rowGo = EditorUIHelpers.CreateUI($"BossRow_{def.name}", _ui.BossListContent);
                rowGo.AddComponent<LayoutElement>().preferredHeight = 32f;

                var rowImg = rowGo.AddComponent<Image>();
                rowImg.color = isSelected ? EditorUIHelpers.SLOT_SELECTED : EditorUIHelpers.BTN_NORMAL;

                var btn = rowGo.AddComponent<Button>();
                var c   = btn.colors;
                c.normalColor      = isSelected ? EditorUIHelpers.SLOT_SELECTED : EditorUIHelpers.BTN_NORMAL;
                c.highlightedColor = EditorUIHelpers.BTN_HOVER;
                c.pressedColor     = EditorUIHelpers.BTN_ACTIVE;
                btn.colors         = c;
                btn.targetGraphic  = rowImg;
                btn.onClick.AddListener(() => SelectBoss(captured));

                var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
                hlg.padding = new RectOffset(8, 8, 0, 0);
                hlg.spacing = 6f; hlg.childForceExpandWidth = false;
                hlg.childForceExpandHeight = true; hlg.childControlWidth = true; hlg.childControlHeight = true;

                var nameGo = EditorUIHelpers.CreateUI("Name", rowGo.transform);
                nameGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
                var nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
                nameTmp.text       = def.name;
                nameTmp.fontSize   = 11f;
                nameTmp.fontStyle  = isSelected ? FontStyles.Bold : FontStyles.Normal;
                nameTmp.color      = isSelected ? EditorUIHelpers.ACCENT : EditorUIHelpers.TEXT_PRIMARY;
                nameTmp.enableWordWrapping = false;
                nameTmp.overflowMode = TMPro.TextOverflowModes.Truncate;
                nameTmp.alignment  = TextAlignmentOptions.MidlineLeft;

                var phaseGo = EditorUIHelpers.CreateUI("PhaseCount", rowGo.transform);
                phaseGo.AddComponent<LayoutElement>().preferredWidth = 28f;
                var phaseTmp = phaseGo.AddComponent<TextMeshProUGUI>();
                phaseTmp.text      = $"{phases}p";
                phaseTmp.fontSize  = 10f;
                phaseTmp.color     = EditorUIHelpers.TEXT_MUTED;
                phaseTmp.alignment = TextAlignmentOptions.MidlineRight;
            }
        }

        private void RefreshPhasesPanel()
        {
            if (_ui.PhasesContent == null) return;

            for (int i = _ui.PhasesContent.childCount - 1; i >= 0; i--)
                Object.Destroy(_ui.PhasesContent.GetChild(i).gameObject);

            if (_selectedBoss == null)
            {
                var hint = EditorUIHelpers.AddLabel(_ui.PhasesContent, "Select a boss first.", 11f);
                hint.color = EditorUIHelpers.TEXT_MUTED;
                return;
            }

            if (_selectedBoss.phases == null || _selectedBoss.phases.Length == 0)
            {
                var hint = EditorUIHelpers.AddLabel(_ui.PhasesContent, "No phases — click '+ Phase'.", 11f);
                hint.color = EditorUIHelpers.TEXT_MUTED;
            }

            for (int pi = 0; pi < (_selectedBoss.phases?.Length ?? 0); pi++)
            {
                int   capturedPi = pi;
                var   phase      = _selectedBoss.phases[pi];
                bool  isSel      = pi == _selectedPhaseIndex;

                // Phase header row
                var phaseRow = EditorUIHelpers.CreateUI($"Phase_{pi}", _ui.PhasesContent);
                phaseRow.AddComponent<LayoutElement>().preferredHeight = 28f;
                var phaseImg = phaseRow.AddComponent<Image>();
                phaseImg.color = isSel ? EditorUIHelpers.ACCENT_BG : new Color(0.2f, 0.2f, 0.2f, 0.9f);

                var btn = phaseRow.AddComponent<Button>();
                var c   = btn.colors;
                c.normalColor      = phaseImg.color;
                c.highlightedColor = EditorUIHelpers.BTN_HOVER;
                btn.colors         = c;
                btn.targetGraphic  = phaseImg;
                btn.onClick.AddListener(() => SelectPhase(capturedPi));

                var hlg = phaseRow.AddComponent<HorizontalLayoutGroup>();
                hlg.padding = new RectOffset(8, 4, 0, 0); hlg.spacing = 4f;
                hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
                hlg.childControlWidth = true; hlg.childControlHeight = true;

                string phaseLabel = string.IsNullOrEmpty(phase.label) ? $"Phase {pi}" : phase.label;

                var phaseLblGo = EditorUIHelpers.CreateUI("PhaseLabel", phaseRow.transform);
                phaseLblGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
                var phaseTmp = phaseLblGo.AddComponent<TextMeshProUGUI>();
                phaseTmp.text      = $"{phaseLabel}  HP≥{phase.hpThreshold:P0}";
                phaseTmp.fontSize  = 11f;
                phaseTmp.color     = isSel ? EditorUIHelpers.ACCENT : EditorUIHelpers.TEXT_PRIMARY;
                phaseTmp.alignment = TextAlignmentOptions.MidlineLeft;
                phaseTmp.enableWordWrapping = false;

                var delBtnGo = EditorUIHelpers.CreateUI("DelPhase", phaseRow.transform);
                var delBtnLe = delBtnGo.AddComponent<LayoutElement>();
                delBtnLe.preferredWidth  = 22f;
                delBtnLe.preferredHeight = 22f;
                var delImg = delBtnGo.AddComponent<Image>();
                delImg.color = UITheme.DANGER_IDLE;
                var delBtn = delBtnGo.AddComponent<Button>();
                delBtn.targetGraphic = delImg;
                int capturedPiDel = pi;
                delBtn.onClick.AddListener(() => RequestDeletePhase(capturedPiDel));
                UILabel.AddCenteredText(delBtnGo.transform, "×", 11f, FontStyles.Bold, EditorUIHelpers.TEXT_PRIMARY);

                // Chart rows (indented, shown only when phase is selected)
                if (isSel && phase.charts != null)
                {
                    foreach (var chart in phase.charts)
                    {
                        if (chart == null) continue;
                        var captured = chart;
                        bool chartSel = chart == _selectedChart;

                        var chartRow = EditorUIHelpers.CreateUI($"Chart_{chart.name}", _ui.PhasesContent);
                        chartRow.AddComponent<LayoutElement>().preferredHeight = 24f;
                        var chartImg = chartRow.AddComponent<Image>();
                        chartImg.color = chartSel ? EditorUIHelpers.SLOT_SELECTED : EditorUIHelpers.BTN_NORMAL;

                        var cbtn = chartRow.AddComponent<Button>();
                        var cc   = cbtn.colors;
                        cc.normalColor      = chartImg.color;
                        cc.highlightedColor = EditorUIHelpers.BTN_HOVER;
                        cbtn.colors         = cc;
                        cbtn.targetGraphic  = chartImg;
                        cbtn.onClick.AddListener(() => SelectChart(captured));

                        var chlg = chartRow.AddComponent<HorizontalLayoutGroup>();
                        chlg.padding = new RectOffset(24, 4, 0, 0); chlg.spacing = 4f;
                        chlg.childForceExpandWidth = false; chlg.childForceExpandHeight = true;
                        chlg.childControlWidth = true; chlg.childControlHeight = true;

                        var trackGo = EditorUIHelpers.CreateUI("Track", chartRow.transform);
                        trackGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
                        var trackTmp = trackGo.AddComponent<TextMeshProUGUI>();
                        trackTmp.text      = $"♪ {chart.musicTrackId}  ({chart.cues?.Count ?? 0} cues)";
                        trackTmp.fontSize  = 10f;
                        trackTmp.color     = chartSel ? EditorUIHelpers.ACCENT : EditorUIHelpers.TEXT_SECONDARY;
                        trackTmp.alignment = TextAlignmentOptions.MidlineLeft;
                        trackTmp.enableWordWrapping = false;

                        var chartDelGo = EditorUIHelpers.CreateUI("DelChart", chartRow.transform);
                        var chartDelLe = chartDelGo.AddComponent<LayoutElement>();
                        chartDelLe.preferredWidth  = 20f;
                        chartDelLe.preferredHeight = 20f;
                        var chartDelImg = chartDelGo.AddComponent<Image>();
                        chartDelImg.color = UITheme.DANGER_IDLE;
                        var chartDelBtn = chartDelGo.AddComponent<Button>();
                        chartDelBtn.targetGraphic = chartDelImg;
                        var capturedChart = chart;
                        chartDelBtn.onClick.AddListener(() => RequestDeleteChart(capturedChart));
                        UILabel.AddCenteredText(chartDelGo.transform, "×", 10f, FontStyles.Bold, EditorUIHelpers.TEXT_PRIMARY);
                    }
                }
            }
        }

        private void RefreshCuesPanel()
        {
            if (_ui.CuesContent == null) return;

            for (int i = _ui.CuesContent.childCount - 1; i >= 0; i--)
                Object.Destroy(_ui.CuesContent.GetChild(i).gameObject);

            if (_selectedChart == null)
            {
                var hint = EditorUIHelpers.AddLabel(_ui.CuesContent, "Select a chart to inspect cues.", 11f);
                hint.color = EditorUIHelpers.TEXT_MUTED;
                return;
            }

            // Mode picker (Numeric | Tap | Quantize | Auto)
            BuildModePicker(_ui.CuesContent);
            EditorUIHelpers.BuildSeparator(_ui.CuesContent);

            // Mode-specific UI (Quantize grid, Auto info, Tap hint)
            BuildModeSpecificContent(_ui.CuesContent);

            // Chart header: track id + bars per loop
            BuildChartHeaderRow(_ui.CuesContent);

            // Cue rows (only in Numeric and Tap modes)
            if (_authoringMode == AuthoringMode.Numeric || _authoringMode == AuthoringMode.Tap)
            {
                if (_selectedChart.cues == null || _selectedChart.cues.Count == 0)
                {
                    var hint = EditorUIHelpers.AddLabel(_ui.CuesContent, "No cues — click '+ Cue'.", 11f);
                    hint.color = EditorUIHelpers.TEXT_MUTED;
                }
                else
                {
                    for (int ci = 0; ci < _selectedChart.cues.Count; ci++)
                        BuildCueRow(_ui.CuesContent, ci);
                }
            }

            // Timeline strip at the bottom of the cues content.
            EditorUIHelpers.BuildSeparator(_ui.CuesContent);
            BuildTimelineStrip(_ui.CuesContent);
            ApplyTimelineChart();
        }

        private void BuildChartHeaderRow(RectTransform parent)
        {
            var chart = _selectedChart;

            EditorUIHelpers.BuildSeparator(parent);

            // Track id row
            {
                var row = EditorUIHelpers.CreateUI("TrackRow", parent);
                row.AddComponent<LayoutElement>().preferredHeight = 26f;
                var hlg = row.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing = 4f; hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
                hlg.childControlWidth = true; hlg.childControlHeight = true;

                var lbl = EditorUIHelpers.AddLabel(row.transform, "Track ID:", 10f);
                lbl.color = EditorUIHelpers.TEXT_SECONDARY;
                var lblLe = lbl.gameObject.GetComponent<LayoutElement>() ?? lbl.gameObject.AddComponent<LayoutElement>();
                lblLe.preferredWidth = 70f;

                var fld = EditorUIHelpers.AddInputField(row.transform, chart.musicTrackId,
                    v => { if (chart != null) { chart.musicTrackId = v; MarkDirty(chart); } }, 24f, 10f);
                var fldLe = fld.gameObject.GetComponent<LayoutElement>() ?? fld.gameObject.AddComponent<LayoutElement>();
                fldLe.flexibleWidth = 1f;
            }

            // Import beats from analysed track (uses MusicTrackEntry.beatTimes)
            {
                var row = EditorUIHelpers.CreateUI("ImportBeatsRow", parent);
                row.AddComponent<LayoutElement>().preferredHeight = 26f;
                var hlg = row.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing = 4f; hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
                hlg.childControlWidth = true; hlg.childControlHeight = true;

                var spacer = EditorUIHelpers.CreateUI("Spacer", row.transform);
                (spacer.GetComponent<LayoutElement>() ??
                 spacer.AddComponent<LayoutElement>()).preferredWidth = 70f;

                EditorUIHelpers.AddActionBtn(row.transform, "Import beats from track", 0f,
                    () => ImportBeatsFromActiveTrack(), out var importBtnImg, 10f);
                (importBtnImg.gameObject.GetComponent<LayoutElement>() ??
                 importBtnImg.gameObject.AddComponent<LayoutElement>()).flexibleWidth = 1f;
            }

            // Bars per loop row
            {
                var row = EditorUIHelpers.CreateUI("BarsRow", parent);
                row.AddComponent<LayoutElement>().preferredHeight = 26f;
                var hlg = row.AddComponent<HorizontalLayoutGroup>();
                hlg.spacing = 4f; hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;
                hlg.childControlWidth = true; hlg.childControlHeight = true;

                var lbl = EditorUIHelpers.AddLabel(row.transform, "Bars/Loop:", 10f);
                lbl.color = EditorUIHelpers.TEXT_SECONDARY;
                var lblLe2 = lbl.gameObject.GetComponent<LayoutElement>() ?? lbl.gameObject.AddComponent<LayoutElement>();
                lblLe2.preferredWidth = 70f;

                var fld = EditorUIHelpers.AddInputField(row.transform, chart.barsPerLoop.ToString(),
                    v => { if (chart != null && int.TryParse(v, out int n)) { chart.barsPerLoop = Mathf.Max(1, n); MarkDirty(chart); } },
                    24f, 10f);
                var fldLe2 = fld.gameObject.GetComponent<LayoutElement>() ?? fld.gameObject.AddComponent<LayoutElement>();
                fldLe2.flexibleWidth = 1f;
            }

            EditorUIHelpers.BuildSeparator(parent);
        }

        // BuildCueRow, RefreshCueRow, and widget helpers (AddSmallIntField,
        // AddSmallSlider, AddTypeDropdown, AddTargetingDropdown) live in
        // BossEditorManager.CueWidgets.cs to stay under the ~250-line cap.
    }
}
