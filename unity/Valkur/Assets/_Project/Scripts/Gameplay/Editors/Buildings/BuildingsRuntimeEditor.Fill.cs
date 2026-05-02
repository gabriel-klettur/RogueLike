using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Tilemaps;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Buildings
{
    /// <summary>
    /// Partial class containing the Fill tool implementation for BuildingsRuntimeEditor.
    ///
    /// Flow:
    ///   1. OnFillButtonClicked  → opens spacing/options modal (AwaitingSpacing).
    ///   2. OnFillSpacingAccepted → closes modal, starts picker blink (AwaitingTemplate).
    ///   3. OnFillTemplatePicked  → captures template, stops blink (AwaitingTile).
    ///   4. UpdateFillHover       → each frame: flood-fill + placement strategy + spacing
    ///                              filter + preview overlay.
    ///   5. CommitFill            → places all accepted cells in one undo batch, returns to Select.
    ///   6. ExitFillMode          → cancels at any step, returns to Select.
    ///
    /// Extended options (v2):
    ///   • Per-tree size randomization — checkbox + Min%/Max% inputs.
    ///   • Smart placement modes: Uniform (current), Groves (Gaussian clusters), Noise (Perlin).
    ///   • When Groves + Random size are both active, tree sizes correlate with cluster proximity.
    /// </summary>
    public partial class BuildingsRuntimeEditor : SingletonMonoBehaviour<BuildingsRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // ── Entry point ─────────────────────────────────────────────────────────

        /// <summary>Called when the Fill button is clicked in the Tools panel.</summary>
        private void OnFillButtonClicked()
        {
            // If already in Fill mode, cancel it.
            if (_mode == EditorMode.Fill)
            {
                ExitFillMode();
                return;
            }

            // Resolve the Ground tilemap once; if unavailable, abort with a warning.
            ResolveWorldTilemap();
            if (_worldGroundTilemap == null)
            {
                Debug.LogWarning("[Fill] WorldGridBuilder.GetTilemap(Ground) returned null — " +
                                 "ensure a WorldGridBuilder with a built grid is present in the scene.");
                Toast("Fill: Ground tilemap not found in scene.");
                return;
            }

            _mode = EditorMode.Fill;
            _fillStep = FillStep.AwaitingSpacing;
            RefreshModeButtons();
            BuildFillSpacingModal();
            ShowFillSpacingModal();
            if (_statusTmp != null)
                _statusTmp.text = "Fill: configure options, then click Accept.";
        }

        // ── World tilemap resolution ─────────────────────────────────────────────

        private void ResolveWorldTilemap()
        {
            if (_worldGroundTilemap != null) return;
            var grid = FindObjectOfType<WorldGridBuilder>();
            _worldGroundTilemap = grid?.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            if (_worldGroundTilemap == null)
                Debug.LogWarning("[Fill] Could not resolve Ground tilemap from WorldGridBuilder.");
        }

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
                _fillRandomSize ? "[✓] Random size per building" : "[ ] Random size per building",
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
                    ? "[✓] Random size per building"
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

        private void OnFillSpacingAccepted()
        {
            // ── Parse and clamp spacing ──────────────────────────────────────────
            int parsed = _fillSpacingTiles;
            if (_fillSpacingInput != null && int.TryParse(_fillSpacingInput.text, out int v))
                parsed = v;
            _fillSpacingTiles = Mathf.Clamp(parsed, 1, 20);

            // ── Parse and clamp size variance ────────────────────────────────────
            int sizeMinParsed = _fillSizeMinPct;
            int sizeMaxParsed = _fillSizeMaxPct;
            if (_fillSizeMinInput != null && int.TryParse(_fillSizeMinInput.text, out int vMin))
                sizeMinParsed = vMin;
            if (_fillSizeMaxInput != null && int.TryParse(_fillSizeMaxInput.text, out int vMax))
                sizeMaxParsed = vMax;
            _fillSizeMinPct = Mathf.Clamp(sizeMinParsed, 20, 300);
            _fillSizeMaxPct = Mathf.Clamp(sizeMaxParsed, 20, 300);
            if (_fillSizeMinPct > _fillSizeMaxPct)
            {
                int tmp = _fillSizeMinPct;
                _fillSizeMinPct = _fillSizeMaxPct;
                _fillSizeMaxPct = tmp;
            }

            // ── Parse and clamp grove params ─────────────────────────────────────
            int groveCountParsed  = _fillGroveCount;
            int groveSpreadParsed = _fillGroveSpread;
            if (_fillGroveCountInput  != null && int.TryParse(_fillGroveCountInput.text,  out int gc))
                groveCountParsed = gc;
            if (_fillGroveSpreadInput != null && int.TryParse(_fillGroveSpreadInput.text, out int gs))
                groveSpreadParsed = gs;
            _fillGroveCount  = Mathf.Clamp(groveCountParsed,  1, 10);
            _fillGroveSpread = Mathf.Clamp(groveSpreadParsed, 2, 20);

            // ── Parse and clamp noise params ─────────────────────────────────────
            float noiseScaleParsed     = _fillNoiseScale;
            float noiseThreshParsed    = _fillNoiseThreshold;
            if (_fillNoiseScaleInput     != null &&
                float.TryParse(_fillNoiseScaleInput.text,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float ns))
                noiseScaleParsed = ns;
            if (_fillNoiseThresholdInput != null &&
                float.TryParse(_fillNoiseThresholdInput.text,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float nt))
                noiseThreshParsed = nt;
            _fillNoiseScale     = Mathf.Clamp(noiseScaleParsed,  0.05f, 1.0f);
            _fillNoiseThreshold = Mathf.Clamp(noiseThreshParsed, 0f,    1f);

            // ── Generate session seed (stable for the rest of this Fill session) ──
            _fillSessionSeed = (int)(Time.realtimeSinceStartup * 1000) ^
                               UnityEngine.Random.Range(int.MinValue, int.MaxValue);

            HideFillSpacingModal();
            _fillStep = FillStep.AwaitingTemplate;
            StartPickerBlink();

            // Open the Buildings panel if it is not already visible.
            if (!_openDropdowns.Contains("buildings"))
            {
                SetDropdownOpen("buildings", true);
                _openDropdowns.Add("buildings");
                RefreshMenuBtnHighlights();
            }

            if (_statusTmp != null)
                _statusTmp.text = $"Fill: spacing={_fillSpacingTiles}, mode={_fillPlacementMode}. " +
                                   "Click a building in the BUILDINGS panel to select a template.";
        }

        // ── Picker blink ─────────────────────────────────────────────────────────

        private void StartPickerBlink()
        {
            StopPickerBlink();
            _fillPickerBlinkCoroutine = StartCoroutine(PickerBlinkRoutine());
        }

        private void StopPickerBlink()
        {
            if (_fillPickerBlinkCoroutine != null)
            {
                StopCoroutine(_fillPickerBlinkCoroutine);
                _fillPickerBlinkCoroutine = null;
            }
            // Restore header to its default color
            if (_buildingsPanelHeaderImg != null)
                _buildingsPanelHeaderImg.color = TileEditorTheme.HeaderBg;
        }

        private IEnumerator PickerBlinkRoutine()
        {
            Color baseColor = _buildingsPanelHeaderImg != null
                ? _buildingsPanelHeaderImg.color
                : TileEditorTheme.HeaderBg;
            Color accentColor = new Color(EditorUIHelpers.ACCENT.r,
                                          EditorUIHelpers.ACCENT.g,
                                          EditorUIHelpers.ACCENT.b, 1f);
            while (true)
            {
                float t = Mathf.PingPong(Time.time * 2f, 1f); // 0..1 at 2 Hz
                Color blended = Color.Lerp(baseColor, accentColor, t * 0.6f);
                if (_buildingsPanelHeaderImg != null)
                    _buildingsPanelHeaderImg.color = blended;
                yield return null;
            }
        }

        // ── Template selection callback (called from Picker.cs) ──────────────────

        /// <summary>
        /// Called by SelectTemplate() when _fillStep == AwaitingTemplate.
        /// Captures the chosen template, stops the blink, advances to AwaitingTile.
        /// </summary>
        private void OnFillTemplatePicked(int templateId)
        {
            if (_catalog == null || _catalog.GetById(templateId) == null)
            {
                Toast($"Fill: template #{templateId} not found in catalog.");
                ExitFillMode();
                return;
            }

            _fillTemplateId = templateId;
            StopPickerBlink();
            _fillStep = FillStep.AwaitingTile;
            EnsureFillOverlay();

            if (_statusTmp != null)
                _statusTmp.text = $"Fill: template #{templateId} selected (spacing {_fillSpacingTiles} tiles, " +
                                   $"mode {_fillPlacementMode}). Hover over the map and click to fill.";
        }

        // ── Preview overlay lifecycle ─────────────────────────────────────────────

        private void EnsureFillOverlay()
        {
            if (_fillOverlay != null)
            {
                _fillOverlay.gameObject.SetActive(true);
                return;
            }
            var go = new GameObject("BuildingsEditor.FillOverlay");
            go.transform.SetParent(transform, false);
            _fillOverlay = go.AddComponent<BuildingsFillPreviewOverlay>();
            _fillOverlay.Initialize(_mainCamera != null ? _mainCamera : Camera.main);
        }

        private void HideFillOverlay()
        {
            if (_fillOverlay != null)
            {
                _fillOverlay.Clear();
                _fillOverlay.gameObject.SetActive(false);
            }
        }

        // ── Hover update ─────────────────────────────────────────────────────────

        /// <summary>
        /// Called every frame while _fillStep == AwaitingTile and the cursor is over
        /// the world (not over UI). Samples the Ground tilemap, runs flood-fill,
        /// applies the selected placement strategy, runs the spacing filter,
        /// and updates the preview overlay.
        /// </summary>
        private void UpdateFillHover(Vector3 worldPos)
        {
            if (_worldGroundTilemap == null) return;

            // Convert world position to cell coordinates
            Vector3Int cell = _worldGroundTilemap.WorldToCell(worldPos);

            // Only recompute when the hovered cell changes (perf guard)
            if (cell == _fillSampleCell && _fillCandidateCells.Count > 0) return;

            _fillSampleCell = cell;
            _fillSampleTile = _worldGroundTilemap.GetTile(cell);

            if (_fillSampleTile == null)
            {
                // Empty cell — clear preview and keep waiting
                _fillCandidateCells.Clear();
                HideFillOverlay();
                if (_statusTmp != null)
                    _statusTmp.text = "Fill: empty cell (no tile) — move to a tile to preview.";
                return;
            }

            // Flood-fill BFS to collect connected cells with the same tile
            var rawCells = TileBrush.ComputeFloodFillCells(_worldGroundTilemap, cell);

            // Apply smart placement strategy first (subsamples raw flood cells).
            HashSet<Vector3Int> postStrategy;
            _fillSizeHintsByCell = null;
            switch (_fillPlacementMode)
            {
                case FillPlacementMode.Groves:
                {
                    var result = BuildingsFillPlacementStrategy.ApplyGroves(
                        rawCells, _fillGroveCount, _fillGroveSpread, _fillSessionSeed);
                    postStrategy = result.cells;
                    if (_fillRandomSize) _fillSizeHintsByCell = result.sizeHints;
                    break;
                }
                case FillPlacementMode.Noise:
                    postStrategy = BuildingsFillPlacementStrategy.ApplyNoise(
                        rawCells, _fillNoiseScale, _fillNoiseThreshold, _fillSessionSeed);
                    break;
                default:
                    postStrategy = rawCells;
                    break;
            }

            var accepted = ApplySpacingFilter(postStrategy, _fillSpacingTiles, _worldGroundTilemap);

            _fillCandidateCells.Clear();
            foreach (var c in accepted) _fillCandidateCells.Add(c);

            EnsureFillOverlay();
            _fillOverlay.SetCells(_fillCandidateCells, _worldGroundTilemap);

            if (_statusTmp != null)
                _statusTmp.text = $"Fill preview: {accepted.Count} placement(s) " +
                                   $"(from {rawCells.Count} tiles, mode: {_fillPlacementMode}). " +
                                   "Left-click to commit. Esc to cancel.";
        }

        // ── Spacing filter ────────────────────────────────────────────────────────

        /// <summary>
        /// Greedy row-major spacing filter.
        /// Accepts a candidate cell only if its world-center is at least spacingTiles
        /// away (Euclidean) from every already-placed building AND every already-accepted
        /// candidate cell.
        ///
        /// Delegates to <see cref="BuildingsFillSpacingFilter.Apply"/> so the algorithm
        /// can be unit-tested without a live editor session.
        /// </summary>
        private List<Vector3Int> ApplySpacingFilter(
            IEnumerable<Vector3Int> candidates,
            int spacingTiles,
            Tilemap tilemap)
        {
            if (candidates == null) return new List<Vector3Int>();

            // Collect world positions of already-existing buildings.
            var existing = FindObjectsOfType<BuildingObject>();
            var existingPositions = new List<Vector2>(existing.Length);
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] != null)
                    existingPositions.Add(new Vector2(
                        existing[i].transform.position.x,
                        existing[i].transform.position.y));
            }

            return BuildingsFillSpacingFilter.Apply(candidates, spacingTiles, tilemap, existingPositions);
        }

        // ── Commit ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Places one BuildingObject per accepted candidate cell, wrapped in a single
        /// undo operation (Ctrl+Z reverts the entire Fill batch at once).
        /// Applies per-cell scale overrides when _fillRandomSize is true.
        /// </summary>
        private void CommitFill()
        {
            if (_worldGroundTilemap == null)
            {
                ExitFillMode();
                return;
            }

            if (_fillSampleTile == null)
            {
                Toast("Fill: clicked on an empty tile — move to a tile to fill.");
                return;
            }

            if (_fillCandidateCells.Count == 0)
            {
                Toast("Fill: no candidate cells (all blocked by spacing).");
                ExitFillMode();
                return;
            }

            if (_catalog == null || _catalog.GetById(_fillTemplateId) == null)
            {
                Toast("Fill: template not found.");
                ExitFillMode();
                return;
            }

            // Snapshot candidates into a stable list (sorted row-major, same as filter output)
            var cells = new List<Vector3Int>(_fillCandidateCells);
            cells.Sort((a, b) =>
            {
                if (b.y != a.y) return b.y.CompareTo(a.y);
                return a.x.CompareTo(b.x);
            });

            int templateId = _fillTemplateId;
            var tilemap    = _worldGroundTilemap;

            // Pre-compute all world positions so we can capture them in the undo closure.
            var worldPositions = new List<Vector3>(cells.Count);
            for (int i = 0; i < cells.Count; i++)
            {
                Vector3 wp = tilemap.GetCellCenterWorld(cells[i]);
                wp.z = 0f;
                worldPositions.Add(wp);
            }

            // Pre-compute per-cell scale overrides (deterministic from session seed).
            bool doRandomSize = _fillRandomSize;
            int  sessionSeed  = _fillSessionSeed;
            int  sizeMinPct   = _fillSizeMinPct;
            int  sizeMaxPct   = _fillSizeMaxPct;
            // Capture size hints by cell (may be null if Uniform/Noise mode)
            var sizeHints = _fillSizeHintsByCell != null
                ? new Dictionary<Vector3Int, float>(_fillSizeHintsByCell)
                : null;

            // Allocate instance IDs sequentially (NextInstanceId already scans the scene).
            CacheBuildingLoader();
            int startId = NextInstanceId();

            var placedObjects = new List<BuildingObject>();

            ExecutePersistedEdit($"Fill {cells.Count} buildings",
                () =>
                {
                    placedObjects.Clear();
                    var template = _catalog?.GetById(templateId);
                    if (template == null) return;

                    var rng = new System.Random(sessionSeed);

                    for (int i = 0; i < worldPositions.Count; i++)
                    {
                        int newId   = startId + i;
                        string zone = DetectZoneAt(worldPositions[i]);
                        var go      = new GameObject($"Building_{newId}_{template.name}");
                        go.transform.SetParent(_buildingsRoot, worldPositionStays: false);
                        go.transform.position = worldPositions[i];
                        go.layer = 11; // World
                        var bObj    = go.AddComponent<BuildingObject>();
                        bObj.ZoneName   = zone;
                        bObj.InstanceId = newId;

                        // Compute scale override if random size is enabled.
                        Vector2Int scaleOverride = Vector2Int.zero;
                        if (doRandomSize)
                        {
                            float minF = sizeMinPct / 100f;
                            float maxF = sizeMaxPct / 100f;
                            float s;
                            if (sizeHints != null && sizeHints.TryGetValue(cells[i], out float hint))
                            {
                                // hint: 1.0 = at cluster center (large), 0.0 = at spread fringe (small).
                                float baseS  = Mathf.Lerp(minF, maxF, hint);
                                float jitter = (float)(rng.NextDouble() * 0.20 - 0.10) * (maxF - minF);
                                s = Mathf.Clamp(baseS + jitter, minF, maxF);
                            }
                            else
                            {
                                s = Mathf.Lerp(minF, maxF, (float)rng.NextDouble());
                            }
                            int w = Mathf.Max(1, Mathf.RoundToInt(template.originalScale.x * s));
                            int h = Mathf.Max(1, Mathf.RoundToInt(template.originalScale.y * s));
                            scaleOverride = new Vector2Int(w, h);
                        }

                        bObj.Apply(template, scaleOverride, -1f);
                        var newRenderers = bObj.GetComponentsInChildren<SpriteRenderer>(true);
                        for (int r = 0; r < newRenderers.Length; r++)
                            if (newRenderers[r] != null)
                                newRenderers[r].enabled = _buildingsVisible;
                        RefreshCollisionFor(bObj);
                        placedObjects.Add(bObj);
                    }

                    InvalidateBuildingCache();
                    if (_statusTmp != null)
                        _statusTmp.text = $"Fill placed {placedObjects.Count} buildings (template #{templateId}).";
                },
                () =>
                {
                    for (int i = placedObjects.Count - 1; i >= 0; i--)
                    {
                        if (placedObjects[i] != null)
                        {
                            placedObjects[i].gameObject.SetActive(false);
                            Destroy(placedObjects[i].gameObject);
                        }
                    }
                    placedObjects.Clear();
                    InvalidateBuildingCache();
                    if (_statusTmp != null)
                        _statusTmp.text = "Fill reverted.";
                });

            ExitFillMode();
        }

        // ── Cancel / cleanup ─────────────────────────────────────────────────────

        /// <summary>
        /// Clean up all Fill sub-state and return to Select mode.
        /// Safe to call at any FillStep including Idle.
        /// </summary>
        /// <param name="setSelectMode">
        /// When true (default) calls SetMode(Select). Pass false when already
        /// being called from SetMode to avoid recursion.
        /// </param>
        private void ExitFillMode(bool setSelectMode = true)
        {
            StopPickerBlink();
            HideFillSpacingModal();
            HideFillOverlay();

            _fillStep             = FillStep.Idle;
            _fillTemplateId       = -1;
            _fillSampleTile       = null;
            _fillSampleCell       = Vector3Int.zero;
            _fillCandidateCells.Clear();
            _fillSizeHintsByCell  = null;
            _fillSessionSeed      = 0;

            if (setSelectMode && _mode == EditorMode.Fill)
            {
                _mode = EditorMode.Select;
                RefreshModeButtons();
                if (_statusTmp != null)
                    _statusTmp.text = "Fill cancelled. Back to Select mode.";
            }
        }
    }
}
