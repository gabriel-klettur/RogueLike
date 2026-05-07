using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Buildings
{
    public partial class BuildingsRuntimeEditor : SingletonMonoBehaviour<BuildingsRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // ── World tilemap resolution ─────────────────────────────────────────────

        private void ResolveWorldTilemap()
        {
            if (_worldGroundTilemap != null) return;
            var grid = FindObjectOfType<WorldGridBuilder>();
            _worldGroundTilemap = grid?.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
            if (_worldGroundTilemap == null)
                Debug.LogWarning("[Fill] Could not resolve Ground tilemap from WorldGridBuilder.");
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
    }
}
