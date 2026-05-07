using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Editors;

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
    ///
    /// Split into focused sub-partials:
    ///   Fill.Modal.cs   — spacing/options dialog construction + visual refresh helpers
    ///   Fill.Preview.cs — world tilemap resolution, hover update, spacing filter, picker blink
    ///   Fill.Commit.cs  — CommitFill: place + undo batch
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

        private void OnFillSpacingAccepted()
        {
            // ── Parse and clamp spacing ──────────────────────────────────────────
            int parsed = _fillSpacingTiles;
            if (_fillSpacingInput != null && int.TryParse(_fillSpacingInput.text, out int v))
                parsed = v;
            _fillSpacingTiles = BuildingsFillOptionsValidator.ClampSpacing(parsed);

            // ── Parse and clamp size variance ────────────────────────────────────
            int sizeMinParsed = _fillSizeMinPct;
            int sizeMaxParsed = _fillSizeMaxPct;
            if (_fillSizeMinInput != null && int.TryParse(_fillSizeMinInput.text, out int vMin))
                sizeMinParsed = vMin;
            if (_fillSizeMaxInput != null && int.TryParse(_fillSizeMaxInput.text, out int vMax))
                sizeMaxParsed = vMax;
            (_fillSizeMinPct, _fillSizeMaxPct) =
                BuildingsFillOptionsValidator.ClampSizeRange(sizeMinParsed, sizeMaxParsed);

            // ── Parse and clamp grove params ─────────────────────────────────────
            int groveCountParsed  = _fillGroveCount;
            int groveSpreadParsed = _fillGroveSpread;
            if (_fillGroveCountInput  != null && int.TryParse(_fillGroveCountInput.text,  out int gc))
                groveCountParsed = gc;
            if (_fillGroveSpreadInput != null && int.TryParse(_fillGroveSpreadInput.text, out int gs))
                groveSpreadParsed = gs;
            _fillGroveCount  = BuildingsFillOptionsValidator.ClampGroveCount(groveCountParsed);
            _fillGroveSpread = BuildingsFillOptionsValidator.ClampGroveSpread(groveSpreadParsed);

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
            _fillNoiseScale     = BuildingsFillOptionsValidator.ClampNoiseScale(noiseScaleParsed);
            _fillNoiseThreshold = BuildingsFillOptionsValidator.ClampNoiseThreshold(noiseThreshParsed);

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
