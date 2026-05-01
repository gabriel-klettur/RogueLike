using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Zone overlay rendering (preview, tile outlines, label recoloring) and
    /// cursor-to-tile utility for <see cref="MapEditorManager"/>.
    /// </summary>
    public partial class MapEditorManager
    {
        private void UpdateAddZonePreview()
        {
            if (!_hasPendingAddTarget || _overlayRoot == null) { UpdateAddZonePreviewVisibility(); return; }

            if (_addZonePreviewObject == null)
            {
                _addZonePreviewObject = new GameObject("MapEditorAddZonePreview");
                _addZonePreviewObject.transform.SetParent(_overlayRoot.transform, false);

                var line = _addZonePreviewObject.AddComponent<LineRenderer>();
                line.positionCount = 5; line.loop = false; line.useWorldSpace = true;
                line.widthMultiplier = Mathf.Max(overlayLineWidth * 1.15f, 0.06f);
                line.material = _overlayLineMaterial;
                line.sortingLayerName = SortingConfig.LAYER_OVERHEAD;
                line.sortingOrder = SortingConfig.Z_UI + 2;
                line.startColor = new Color(0.36f, 0.86f, 1f, 0.95f);
                line.endColor   = line.startColor;
                line.numCapVertices    = 2;
                line.numCornerVertices = 2;
                line.alignment         = LineAlignment.View;

                var labelGo = new GameObject("Label");
                labelGo.transform.SetParent(_addZonePreviewObject.transform, false);
                var text = labelGo.AddComponent<TextMeshPro>();
                text.fontSize   = 3.1f;
                text.alignment  = TextAlignmentOptions.Center;
                text.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_OVERHEAD);
                text.sortingOrder   = SortingConfig.Z_UI + 2;
            }

            float tileSize = Mathf.Max(0.01f, zoneManager.TileSize);
            int   width    = Mathf.Max(1, zoneManager.ZoneWidthTiles);
            int   height   = Mathf.Max(1, zoneManager.ZoneHeightTiles);

            float minX = _pendingAddZoneOffset.x * tileSize;
            float maxX = (_pendingAddZoneOffset.x + width)  * tileSize;
            float minY = _pendingAddZoneOffset.y * tileSize;
            float maxY = (_pendingAddZoneOffset.y + height) * tileSize;
            float z    = -0.015f;

            var previewLine = _addZonePreviewObject.GetComponent<LineRenderer>();
            previewLine.SetPosition(0, new Vector3(minX, minY, z));
            previewLine.SetPosition(1, new Vector3(maxX, minY, z));
            previewLine.SetPosition(2, new Vector3(maxX, maxY, z));
            previewLine.SetPosition(3, new Vector3(minX, maxY, z));
            previewLine.SetPosition(4, new Vector3(minX, minY, z));

            var previewText = _addZonePreviewObject.GetComponentInChildren<TextMeshPro>();
            if (previewText != null)
            {
                previewText.transform.position = new Vector3((minX + maxX) * 0.5f, maxY + 0.25f, z);
                previewText.color = new Color(0.52f, 0.94f, 1f, 1f);
                previewText.text  = $"NEW ZONE [{_pendingAddZoneOffset.x},{_pendingAddZoneOffset.y}]";
            }

            UpdateAddZonePreviewVisibility();
        }

        /// <summary>
        /// Returns the world-units width that corresponds to a target on-screen
        /// pixel width for the active orthographic camera. Clamped to the
        /// inspector-configured <see cref="overlayLineWidth"/> /
        /// <see cref="overlayLineMaxWidth"/> range so borders stay visible at
        /// close zoom and don't blow up at far zoom.
        /// </summary>
        private float ComputeAdaptiveLineWidth()
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            float baseWidth = overlayLineWidth;
            if (_mainCamera == null || !_mainCamera.orthographic || Screen.height <= 1)
                return baseWidth;

            // worldPerPixel = (2 * orthoSize) / screenHeight  â†’ constant pixel size in world units
            float worldPerPixel = (2f * _mainCamera.orthographicSize) / Screen.height;
            float adaptive      = worldPerPixel * Mathf.Max(0.5f, overlayLinePixelWidth);
            return Mathf.Clamp(adaptive, baseWidth, Mathf.Max(baseWidth, overlayLineMaxWidth));
        }

        /// <summary>
        /// Per-frame applier that keeps zone-border line widths visually constant
        /// in screen pixels regardless of camera zoom â€” fixes "borders disappear
        /// when zoomed out" bug.
        /// Also re-applies inset positions so each border's outer edge stays exactly
        /// on the zone boundary at all zoom levels (prevents adjacent borders overlapping).
        /// </summary>
        private void UpdateOverlayLineWidths()
        {
            if (_zoneOverlayObjects == null || _zoneOverlayObjects.Count == 0)
                return;

            float w             = ComputeAdaptiveLineWidth();
            float wSelected     = w * 1.6f;
            string selectedZone = _state?.SelectedZone;
            bool   hasSelection = _state != null && _state.HasSelection;
            float tileSize      = zoneManager != null ? Mathf.Max(0.01f, zoneManager.TileSize) : 1f;

            for (int i = 0; i < _zoneOverlayObjects.Count; i++)
            {
                var go = _zoneOverlayObjects[i];
                if (go == null) continue;
                var lr = go.GetComponent<LineRenderer>();
                if (lr == null) continue;

                bool isSelected = hasSelection && go.name == "ZoneOverlay_" + selectedZone;
                float thisW = isSelected ? wSelected : w;
                lr.widthMultiplier = thisW;

                // Re-apply inset so each border's outer edge is flush with the zone boundary.
                // Inset = half the line width so the line sits fully inside the zone.
                string zoneName = go.name.Replace("ZoneOverlay_", string.Empty);
                if (zoneManager != null && zoneManager.TryGetZone(zoneName, out var zone))
                {
                    var r = zoneManager.GetZoneRect(zone);
                    var worldRect = new Rect(r.xMin * tileSize, r.yMin * tileSize,
                                            r.width * tileSize, r.height * tileSize);
                    ApplyZoneBorderPositions(lr, worldRect, thisW * 0.5f);
                }
            }

            if (_addZonePreviewObject != null)
            {
                var prev = _addZonePreviewObject.GetComponent<LineRenderer>();
                if (prev != null) prev.widthMultiplier = w * 1.15f;
            }
        }

        /// <summary>
        /// Sets the 5 positions of a zone-border LineRenderer so that the line is
        /// inset by <paramref name="inset"/> world units from every edge of <paramref name="worldRect"/>.
        /// Passing inset = lineWidth/2 places the outer edge of the line exactly on the
        /// zone boundary, preventing adjacent zones' borders from visually overlapping.
        /// </summary>
        private static void ApplyZoneBorderPositions(LineRenderer lr, Rect worldRect, float inset)
        {
            float minX = worldRect.xMin + inset, maxX = worldRect.xMax - inset;
            float minY = worldRect.yMin + inset, maxY = worldRect.yMax - inset;
            const float z = -0.02f;
            lr.SetPosition(0, new Vector3(minX, minY, z));
            lr.SetPosition(1, new Vector3(maxX, minY, z));
            lr.SetPosition(2, new Vector3(maxX, maxY, z));
            lr.SetPosition(3, new Vector3(minX, maxY, z));
            lr.SetPosition(4, new Vector3(minX, minY, z));
        }

        private void RebuildZoneOverlays()
        {
            for (int i = 0; i < _zoneOverlayObjects.Count; i++)
                if (_zoneOverlayObjects[i] != null) Destroy(_zoneOverlayObjects[i]);
            _zoneOverlayObjects.Clear();

            if (_overlayRoot == null || zoneManager == null) return;

            var   zones    = zoneManager.GetZonesSnapshot();
            float tileSize = Mathf.Max(0.01f, zoneManager.TileSize);

            for (int i = 0; i < zones.Length; i++)
            {
                var zone     = zones[i];
                var zoneRect = zoneManager.GetZoneRect(zone);

                var zoneGo = new GameObject($"ZoneOverlay_{zone.zoneName}");
                zoneGo.transform.SetParent(_overlayRoot.transform, false);

                var line = zoneGo.AddComponent<LineRenderer>();
                line.positionCount = 5; line.loop = false; line.useWorldSpace = true;
                line.widthMultiplier = overlayLineWidth;
                line.material        = _overlayLineMaterial;
                line.sortingLayerName = SortingConfig.LAYER_OVERHEAD;
                line.sortingOrder     = SortingConfig.Z_UI;
                line.numCapVertices    = 2;
                line.numCornerVertices = 2;
                line.alignment         = LineAlignment.View;

                float minX = zoneRect.xMin * tileSize, maxX = zoneRect.xMax * tileSize;
                float minY = zoneRect.yMin * tileSize, maxY = zoneRect.yMax * tileSize;
                const float z = -0.02f;
                // Initial inset = overlayLineWidth/2 so the border sits inside the zone boundary.
                // UpdateOverlayLineWidths() will keep the inset in sync with adaptive line width.
                ApplyZoneBorderPositions(line,
                    new Rect(minX, minY, maxX - minX, maxY - minY),
                    overlayLineWidth * 0.5f);

                var labelGo = new GameObject("Label");
                labelGo.transform.SetParent(zoneGo.transform, false);
                labelGo.transform.position = new Vector3((minX + maxX) * 0.5f, maxY + 0.25f, z);
                var text = labelGo.AddComponent<TextMeshPro>();
                text.text     = zone.zoneName; text.fontSize = 3.2f;
                text.alignment = TextAlignmentOptions.Center;
                text.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_OVERHEAD);
                text.sortingOrder   = SortingConfig.Z_UI;

                _zoneOverlayObjects.Add(zoneGo);
            }

            RecolorZoneOverlays();
            UpdateOverlayLineWidths();
        }

        private void RecolorZoneOverlays()
        {
            for (int i = 0; i < _zoneOverlayObjects.Count; i++)
            {
                var zoneGo = _zoneOverlayObjects[i];
                if (zoneGo == null) continue;

                string zoneName = zoneGo.name.Replace("ZoneOverlay_", string.Empty);
                if (!zoneManager.TryGetZone(zoneName, out var zone)) continue;

                bool selected = _state.HasSelection && _state.SelectedZone == zoneName;
                Color lineColor = selected
                    ? new Color(1f, 0.82f, 0.3f, 0.95f)
                    : zone.editableInTileEditor
                        ? new Color(0.4f, 0.96f, 0.4f, 0.85f)
                        : new Color(1f, 0.04f, 0.04f, 1f);   // vivid red for locked zones

                var lr = zoneGo.GetComponent<LineRenderer>();
                if (lr != null)
                {
                    lr.startColor  = lineColor;
                    lr.endColor    = lineColor;
                    lr.sortingOrder = selected ? SortingConfig.Z_UI + 1 : SortingConfig.Z_UI;
                }

                var text = zoneGo.GetComponentInChildren<TextMeshPro>();
                if (text != null)
                {
                    text.color = selected ? new Color(1f, 0.9f, 0.45f, 1f) : lineColor;
                    text.text  = $"{zone.zoneName} {(zone.editableInTileEditor ? "[EDIT]" : "[LOCK]")}";
                }
            }
        }

        private bool TryGetCursorTile(out Vector2Int tilePos)
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            var mouse = Mouse.current;
            if (_mainCamera == null || mouse == null) { tilePos = default; return false; }

            Vector3 mouseWorld = _mainCamera.ScreenToWorldPoint(Valkur.Core.Input.MouseInputManager.GetScreenMousePosition());
            mouseWorld.z = 0f;

            if (worldGridBuilder != null)
            {
                var tilemap = worldGridBuilder.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
                if (tilemap != null)
                {
                    Vector3Int cell = tilemap.WorldToCell(mouseWorld);
                    tilePos = new Vector2Int(cell.x, cell.y);
                    return true;
                }
            }

            float tileSize = Mathf.Max(0.01f, zoneManager.TileSize);
            tilePos = new Vector2Int(
                Mathf.FloorToInt(mouseWorld.x / tileSize),
                Mathf.FloorToInt(mouseWorld.y / tileSize));
            return true;
        }

        private string GenerateUniqueZoneName()
        {
            int safety = 0;
            while (safety++ < 10000)
            {
                string candidate = $"zone_{_state.NextZoneIndex:000}";
                if (!zoneManager.TryGetZone(candidate, out _)) return candidate;
                _state.NextZoneIndex++;
            }
            return $"zone_{Guid.NewGuid().ToString("N").Substring(0, 6)}";
        }

        /// <summary>
        /// Suggests a zone name based on its tile-grid offset (e.g. "zone_150_150"
        /// for an offset of [150,150]). If the offset-based name already exists,
        /// appends "_2", "_3"… until a free name is found. This is more
        /// informative than the sequential "zone_001" counter when zones are
        /// being placed by clicking on the map.
        /// </summary>
        private string GenerateOffsetZoneName(Vector2Int offset)
        {
            string baseName = $"zone_{offset.x}_{offset.y}";
            if (!zoneManager.TryGetZone(baseName, out _)) return baseName;

            for (int i = 2; i < 1000; i++)
            {
                string candidate = $"{baseName}_{i}";
                if (!zoneManager.TryGetZone(candidate, out _)) return candidate;
            }
            return $"{baseName}_{Guid.NewGuid().ToString("N").Substring(0, 4)}";
        }
    }
}
