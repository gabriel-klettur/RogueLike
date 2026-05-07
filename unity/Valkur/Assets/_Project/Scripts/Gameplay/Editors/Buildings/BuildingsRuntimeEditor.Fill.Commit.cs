using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Buildings
{
    public partial class BuildingsRuntimeEditor : SingletonMonoBehaviour<BuildingsRuntimeEditor>, GameEditorManager.IGameEditor
    {
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
                        float? hint = null;
                        if (sizeHints != null && sizeHints.TryGetValue(cells[i], out float h0))
                            hint = h0;
                        Vector2Int scaleOverride = BuildingsFillSizeCalculator.ComputeScaleOverride(
                            doRandomSize, sizeMinPct, sizeMaxPct, template.originalScale, hint, rng);

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
    }
}
