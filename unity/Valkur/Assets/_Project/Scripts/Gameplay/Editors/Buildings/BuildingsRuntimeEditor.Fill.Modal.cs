using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Gameplay.Buildings
{
    public partial class BuildingsRuntimeEditor : SingletonMonoBehaviour<BuildingsRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // ── Spacing/Options modal ────────────────────────────────────────────────

        private void BuildFillSpacingModal()
        {
            if (_fillSpacingModal != null) return;

            // Fullscreen dim overlay
            _fillSpacingModal = EditorUIHelpers.MakePanel("FillSpacingModal", _root.transform,
                new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            var bgImg = _fillSpacingModal.GetComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 140f / 255f);

            // Inner panel (520 × 440 px)
            var inner = EditorUIHelpers.MakePanel("Inner", _fillSpacingModal.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(520f, 440f));
            var vlg = inner.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(20, 20, 18, 18);
            vlg.spacing = 10f;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth      = true;
            vlg.childControlHeight     = true;

            EditorUIHelpers.MakeTitleBar(inner.transform, "FILL TOOL — OPTIONS");

            // ── Section 1: Spacing ────────────────────────────────────────────
            var spacingLbl = EditorUIHelpers.AddLabel(inner.transform,
                "Minimum distance between buildings (tiles, 1–20):", 13f);
            spacingLbl.color     = EditorUIHelpers.TEXT_PRIMARY;
            spacingLbl.alignment = TextAlignmentOptions.MidlineLeft;

            _fillSpacingInput = EditorUIHelpers.AddInputField(inner.transform,
                _fillSpacingTiles.ToString(), null, height: 36f, fontSize: 16f);
            _fillSpacingInput.contentType    = TMP_InputField.ContentType.IntegerNumber;
            _fillSpacingInput.characterLimit = 2;

            EditorUIHelpers.BuildSeparator(inner.transform);

            // ── Section 2: Size variance ──────────────────────────────────────
            var sizeHdrLbl = EditorUIHelpers.AddLabel(inner.transform, "Size Variance", 12f);
            sizeHdrLbl.color     = EditorUIHelpers.TEXT_SECONDARY;
            sizeHdrLbl.fontStyle = FontStyles.Bold;

            // Toggle button
            var randomSizeBtn = EditorUIHelpers.MakeButton(inner.transform,
                _fillRandomSize ? "[X] Random size per building" : "[ ] Random size per building",
                () => { _fillRandomSize = !_fillRandomSize; RefreshRandomSizeToggleVisual(); },
                height: 32f, fontSize: 12f);
            _fillRandomSizeCheckImg  = randomSizeBtn.GetComponent<Image>();
            _fillRandomSizeCheckText = randomSizeBtn.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            RefreshRandomSizeToggleVisual();

            // Min%/Max% row
            var sizeRow = EditorUIHelpers.CreateUI("SizeRow", inner.transform);
            sizeRow.AddComponent<LayoutElement>().preferredHeight = 36f;
            var sizeHlg = sizeRow.AddComponent<HorizontalLayoutGroup>();
            sizeHlg.spacing              = 12f;
            sizeHlg.childForceExpandWidth = true;
            sizeHlg.childControlWidth     = true;
            sizeHlg.childControlHeight    = true;

            BuildLabeledIntInput(sizeRow.transform, "Min %:", _fillSizeMinPct.ToString(),
                out _fillSizeMinInput, characterLimit: 3);
            BuildLabeledIntInput(sizeRow.transform, "Max %:", _fillSizeMaxPct.ToString(),
                out _fillSizeMaxInput, characterLimit: 3);

            EditorUIHelpers.BuildSeparator(inner.transform);

            // ── Section 3: Placement style ────────────────────────────────────
            var placeLbl = EditorUIHelpers.AddLabel(inner.transform, "Placement Style", 12f);
            placeLbl.color     = EditorUIHelpers.TEXT_SECONDARY;
            placeLbl.fontStyle = FontStyles.Bold;

            // Mode selector row (Uniform / Groves / Noise)
            var modeRow = EditorUIHelpers.CreateUI("PlaceModeRow", inner.transform);
            modeRow.AddComponent<LayoutElement>().preferredHeight = 32f;
            var modeHlg = modeRow.AddComponent<HorizontalLayoutGroup>();
            modeHlg.spacing              = 8f;
            modeHlg.childForceExpandWidth = true;
            modeHlg.childControlWidth     = true;
            modeHlg.childControlHeight    = true;

            var uniformBtn = EditorUIHelpers.MakeButton(modeRow.transform, "Uniform",
                () => { _fillPlacementMode = FillPlacementMode.Uniform; RefreshPlacementModeButtonsVisual(); RefreshPlacementParamsVisibility(); },
                height: 32f, fontSize: 12f);
            _fillModeUniformBtnImg = uniformBtn.GetComponent<Image>();

            var grovesBtn = EditorUIHelpers.MakeButton(modeRow.transform, "Groves",
                () => { _fillPlacementMode = FillPlacementMode.Groves; RefreshPlacementModeButtonsVisual(); RefreshPlacementParamsVisibility(); },
                height: 32f, fontSize: 12f);
            _fillModeGrovesBtnImg = grovesBtn.GetComponent<Image>();

            var noiseBtn = EditorUIHelpers.MakeButton(modeRow.transform, "Noise",
                () => { _fillPlacementMode = FillPlacementMode.Noise; RefreshPlacementModeButtonsVisual(); RefreshPlacementParamsVisibility(); },
                height: 32f, fontSize: 12f);
            _fillModeNoiseBtnImg = noiseBtn.GetComponent<Image>();

            RefreshPlacementModeButtonsVisual();

            // Groves sub-row
            var grovesRow = EditorUIHelpers.CreateUI("GrovesRow", inner.transform);
            grovesRow.AddComponent<LayoutElement>().preferredHeight = 36f;
            var grovesHlg = grovesRow.AddComponent<HorizontalLayoutGroup>();
            grovesHlg.spacing              = 12f;
            grovesHlg.childForceExpandWidth = true;
            grovesHlg.childControlWidth     = true;
            grovesHlg.childControlHeight    = true;

            BuildLabeledIntInput(grovesRow.transform, "Cluster count:", _fillGroveCount.ToString(),
                out _fillGroveCountInput, characterLimit: 2);
            BuildLabeledIntInput(grovesRow.transform, "Spread (tiles):", _fillGroveSpread.ToString(),
                out _fillGroveSpreadInput, characterLimit: 2);

            // Noise sub-row
            var noiseRow = EditorUIHelpers.CreateUI("NoiseRow", inner.transform);
            noiseRow.AddComponent<LayoutElement>().preferredHeight = 36f;
            var noiseHlg = noiseRow.AddComponent<HorizontalLayoutGroup>();
            noiseHlg.spacing              = 12f;
            noiseHlg.childForceExpandWidth = true;
            noiseHlg.childControlWidth     = true;
            noiseHlg.childControlHeight    = true;

            BuildLabeledDecimalInput(noiseRow.transform, "Noise scale:", _fillNoiseScale.ToString("F2"),
                out _fillNoiseScaleInput);
            BuildLabeledDecimalInput(noiseRow.transform, "Threshold:", _fillNoiseThreshold.ToString("F2"),
                out _fillNoiseThresholdInput);

            // Store row GOs on the sub-rows so RefreshPlacementParamsVisibility can toggle them.
            // We use the existing GameObject references captured in the closure via local vars.
            // Tag them with name for lookup.
            grovesRow.name  = "GrovesParamRow";
            noiseRow.name   = "NoiseParamRow";

            RefreshPlacementParamsVisibility();

            // ── Button row ────────────────────────────────────────────────────
            var btnRow = EditorUIHelpers.CreateUI("Btns", inner.transform);
            btnRow.AddComponent<LayoutElement>().preferredHeight = 36f;
            var hlg = btnRow.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing              = 12f;
            hlg.childForceExpandWidth = true;
            EditorUIHelpers.MakeButton(btnRow.transform, "Accept",
                () => OnFillSpacingAccepted(), 32f, 12f);
            EditorUIHelpers.MakeDangerButton(btnRow.transform, "Cancel",
                () => ExitFillMode(), 32f);

            _fillSpacingModal.SetActive(false);
        }

        // ── Modal helper: labeled input field in a row ────────────────────────────

        private static void BuildLabeledIntInput(Transform parent, string labelText,
            string initialValue, out TMP_InputField field, int characterLimit = 3)
        {
            var cell = EditorUIHelpers.CreateUI($"Cell_{labelText}", parent);
            var cellVlg = cell.AddComponent<VerticalLayoutGroup>();
            cellVlg.childForceExpandWidth  = true;
            cellVlg.childControlWidth      = true;
            cellVlg.childControlHeight     = true;
            cellVlg.spacing = 2f;

            var lbl = EditorUIHelpers.AddLabel(cell.transform, labelText, 11f);
            lbl.color = EditorUIHelpers.TEXT_MUTED;

            field = EditorUIHelpers.AddInputField(cell.transform, initialValue, null, height: 28f, fontSize: 13f);
            field.contentType    = TMP_InputField.ContentType.IntegerNumber;
            field.characterLimit = characterLimit;
        }

        private static void BuildLabeledDecimalInput(Transform parent, string labelText,
            string initialValue, out TMP_InputField field)
        {
            var cell = EditorUIHelpers.CreateUI($"Cell_{labelText}", parent);
            var cellVlg = cell.AddComponent<VerticalLayoutGroup>();
            cellVlg.childForceExpandWidth  = true;
            cellVlg.childControlWidth      = true;
            cellVlg.childControlHeight     = true;
            cellVlg.spacing = 2f;

            var lbl = EditorUIHelpers.AddLabel(cell.transform, labelText, 11f);
            lbl.color = EditorUIHelpers.TEXT_MUTED;

            field = EditorUIHelpers.AddInputField(cell.transform, initialValue, null, height: 28f, fontSize: 13f);
            field.contentType    = TMP_InputField.ContentType.DecimalNumber;
            field.characterLimit = 6;
        }

        // ── Toggle/mode visual refresh helpers ────────────────────────────────────

        private void RefreshRandomSizeToggleVisual()
        {
            if (_fillRandomSizeCheckImg  != null)
                _fillRandomSizeCheckImg.color  = _fillRandomSize ? EditorUIHelpers.ACCENT : EditorUIHelpers.BTN_NORMAL;
            if (_fillRandomSizeCheckText != null)
                _fillRandomSizeCheckText.text  = _fillRandomSize
                    ? "[X] Random size per building"
                    : "[ ] Random size per building";
        }

        private void RefreshPlacementModeButtonsVisual()
        {
            if (_fillModeUniformBtnImg != null)
                _fillModeUniformBtnImg.color = _fillPlacementMode == FillPlacementMode.Uniform
                    ? EditorUIHelpers.ACCENT : EditorUIHelpers.BTN_NORMAL;
            if (_fillModeGrovesBtnImg != null)
                _fillModeGrovesBtnImg.color = _fillPlacementMode == FillPlacementMode.Groves
                    ? EditorUIHelpers.ACCENT : EditorUIHelpers.BTN_NORMAL;
            if (_fillModeNoiseBtnImg != null)
                _fillModeNoiseBtnImg.color = _fillPlacementMode == FillPlacementMode.Noise
                    ? EditorUIHelpers.ACCENT : EditorUIHelpers.BTN_NORMAL;
        }

        private void RefreshPlacementParamsVisibility()
        {
            if (_fillSpacingModal == null) return;
            // Find the named sub-rows under the inner panel.
            var inner = _fillSpacingModal.transform.Find("Inner");
            if (inner == null) return;
            var grovesRow = inner.Find("GrovesParamRow");
            var noiseRow  = inner.Find("NoiseParamRow");
            if (grovesRow != null) grovesRow.gameObject.SetActive(_fillPlacementMode == FillPlacementMode.Groves);
            if (noiseRow  != null) noiseRow.gameObject.SetActive(_fillPlacementMode == FillPlacementMode.Noise);
        }

        private void ShowFillSpacingModal()
        {
            if (_fillSpacingModal == null) return;
            // Sync fields to current state values
            if (_fillSpacingInput    != null) _fillSpacingInput.text    = _fillSpacingTiles.ToString();
            if (_fillSizeMinInput    != null) _fillSizeMinInput.text    = _fillSizeMinPct.ToString();
            if (_fillSizeMaxInput    != null) _fillSizeMaxInput.text    = _fillSizeMaxPct.ToString();
            if (_fillGroveCountInput  != null) _fillGroveCountInput.text  = _fillGroveCount.ToString();
            if (_fillGroveSpreadInput != null) _fillGroveSpreadInput.text = _fillGroveSpread.ToString();
            if (_fillNoiseScaleInput     != null) _fillNoiseScaleInput.text     = _fillNoiseScale.ToString("F2");
            if (_fillNoiseThresholdInput != null) _fillNoiseThresholdInput.text = _fillNoiseThreshold.ToString("F2");
            RefreshRandomSizeToggleVisual();
            RefreshPlacementModeButtonsVisual();
            RefreshPlacementParamsVisibility();
            _fillSpacingModal.SetActive(true);
            _fillSpacingModal.transform.SetAsLastSibling();
        }

        private void HideFillSpacingModal()
        {
            if (_fillSpacingModal != null) _fillSpacingModal.SetActive(false);
        }
    }
}
