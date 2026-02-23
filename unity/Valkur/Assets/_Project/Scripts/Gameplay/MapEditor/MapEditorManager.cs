using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Runtime map editor migrated from Python workflow.
    /// Toggle with F7 to manage zones and define editable areas consumed by TileEditor.
    /// </summary>
    public class MapEditorManager : SingletonMonoBehaviour<MapEditorManager>
    {
        [Header("References")]
        [SerializeField] private ZoneManager zoneManager;
        [SerializeField] private WorldGridBuilder worldGridBuilder;
        [SerializeField] private TileEditorManager tileEditorManager;

        [Header("Overlay")]
        [SerializeField] private float overlayLineWidth = 0.08f;

        private MapEditorState _state;
        private MapEditorInputHandler _input;
        private MapEditorUI _ui;
        private Camera _mainCamera;

        private readonly List<GameObject> _zoneOverlayObjects = new List<GameObject>();
        private Material _overlayLineMaterial;
        private GameObject _overlayRoot;

        private bool _isAddZoneFlowActive;
        private bool _hasPendingAddTarget;
        private Vector2Int _pendingAddZoneOffset;
        private GameObject _addZonePreviewObject;
        private string _pendingDeleteZoneName;

        public bool IsActive => _state != null && _state.Active;

        [Serializable]
        private class ZonePersistenceFile
        {
            public bool restrictTileEditingToEditableZones;
            public int nextZoneIndex;
            public List<ZonePersistenceEntry> zones = new List<ZonePersistenceEntry>();
        }

        [Serializable]
        private class ZonePersistenceEntry
        {
            public string zoneName;
            public int gridOffsetX;
            public int gridOffsetY;
            public bool editableInTileEditor;
        }

        private string PersistencePath => Path.Combine(Application.persistentDataPath, "map_editor_zones.json");

        protected override void OnSingletonAwake()
        {
            _state = new MapEditorState();
            _input = new MapEditorInputHandler();
            _input.CreateActions();
        }

        private void Start()
        {
            _mainCamera = Camera.main;

            if (zoneManager == null)
                zoneManager = FindObjectOfType<ZoneManager>();
            if (worldGridBuilder == null)
                worldGridBuilder = FindObjectOfType<WorldGridBuilder>();
            if (tileEditorManager == null)
                tileEditorManager = TileEditorManager.Instance != null ? TileEditorManager.Instance : FindObjectOfType<TileEditorManager>();

            if (zoneManager == null)
            {
                var zoneManagerGo = new GameObject("ZoneManager");
                zoneManager = zoneManagerGo.AddComponent<ZoneManager>();
                Debug.LogWarning("[MapEditor] ZoneManager not found. Created runtime ZoneManager so F7 map editor can start.");
            }

            CreateOverlayRoot();
            CreateUI();
            LoadZonesFromDisk();
            HandleZonesChanged();

            zoneManager.OnZonesChanged += HandleZonesChanged;
            ApplyTileEditorConstraint();
        }

        private void Update()
        {
            if (_input == null || zoneManager == null) return;

            if (_input.WasTogglePressed())
                ToggleActive();

            if (!_state.Active) return;

            if (_ui != null && _ui.IsTypingInput)
                return;

            if (_ui != null && _ui.IsModalOpen)
            {
                if (_isAddZoneFlowActive && _input.WasSelectPressed() && !_input.IsPointerOverUI())
                    MarkAddZoneTargetAtCursor();
                return;
            }

            if (_isAddZoneFlowActive && _input.WasSelectPressed() && !_input.IsPointerOverUI())
            {
                MarkAddZoneTargetAtCursor();
                return;
            }

            if (_input.WasSelectPressed() && !_input.IsPointerOverUI())
                SelectZoneAtCursor();

            if (_input.WasCreatePressed())
                BeginAddZoneFlow();

            if (_input.WasDuplicatePressed())
                DuplicateSelectedZone();

            if (_input.WasDeletePressed())
                RequestDeleteSelectedZone();

            if (_input.WasRenamePressed())
                RenameSelectedZone(_ui != null ? _ui.NameInput : string.Empty);

            if (_input.WasToggleEditablePressed())
                ToggleSelectedZoneEditable();
        }

        private void ToggleActive()
        {
            _state.Active = !_state.Active;

            if (_ui != null)
                _ui.SetVisible(_state.Active);
            if (_overlayRoot != null)
                _overlayRoot.SetActive(_state.Active);

            if (_state.Active)
            {
                if (_ui != null)
                    _ui.SetStatus("Map Editor active. F7 to close.");
                Debug.Log("[MapEditor] Activated (F7).");
            }
            else
            {
                CancelAddZoneFlow();
                if (_ui != null)
                    _ui.SetStatus("Map Editor inactive.");
                Debug.Log("[MapEditor] Deactivated (F7).");
            }
        }

        private void CreateOverlayRoot()
        {
            _overlayRoot = new GameObject("MapEditorZoneOverlayRoot");
            _overlayRoot.transform.SetParent(transform, false);
            _overlayRoot.SetActive(false);

            var shader = Shader.Find("Sprites/Default");
            if (shader != null)
                _overlayLineMaterial = new Material(shader);
            else
                Debug.LogWarning("[MapEditor] Shader 'Sprites/Default' not found. Zone overlays may not render.");
        }

        private void CreateUI()
        {
            var uiGo = new GameObject("MapEditorUI");
            uiGo.transform.SetParent(transform, false);
            _ui = uiGo.AddComponent<MapEditorUI>();
            _ui.Initialize(
                _state,
                OnZoneSelected,
                BeginAddZoneFlow,
                ConfirmAddZone,
                CancelAddZoneFlow,
                DuplicateSelectedZone,
                RequestDeleteSelectedZone,
                ConfirmDeleteSelectedZone,
                RenameSelectedZone,
                RenameZoneByName,
                ToggleSelectedZoneEditable,
                ToggleZoneEditableByName,
                MoveSelectedZone,
                SetRestrictTileEditing);
            _ui.SetVisible(false);
            _ui.SetRestrictToggle(_state.RestrictTileEditingToEditableZones);
        }

        private void OnZoneSelected(string zoneName)
        {
            _state.SelectZone(zoneName);
            if (_isAddZoneFlowActive && zoneManager.TryGetZone(zoneName, out var zone))
                _ui?.SetAddZoneSource(zone.zoneName, zone.editableInTileEditor);
            RefreshSelectionUIAndOverlay();
        }

        private void SelectZoneAtCursor()
        {
            if (!TryGetCursorTile(out var tilePos))
            {
                _ui?.SetStatus("No tile under cursor.");
                return;
            }

            if (!zoneManager.TryGetZoneAtTile(tilePos, out var zone))
            {
                _state.ClearSelection();
                _ui?.SetStatus($"No zone at tile {tilePos.x},{tilePos.y}");
                RefreshSelectionUIAndOverlay();
                return;
            }

            _state.SelectZone(zone.zoneName);
            _ui?.SetStatus($"Selected zone {zone.zoneName}");
            RefreshSelectionUIAndOverlay();
        }

        private void BeginAddZoneFlow()
        {
            if (!_state.HasSelection || !zoneManager.TryGetZone(_state.SelectedZone, out var sourceZone))
            {
                _ui?.SetStatus("Select a source zone before Add Zone.");
                return;
            }

            _isAddZoneFlowActive = true;
            _hasPendingAddTarget = false;
            _pendingAddZoneOffset = default;

            int width = Mathf.Max(1, zoneManager.ZoneWidthTiles);
            int height = Mathf.Max(1, zoneManager.ZoneHeightTiles);

            _ui?.ShowAddZoneDialog(GenerateUniqueZoneName(), sourceZone.zoneName, sourceZone.editableInTileEditor);
            _ui?.SetAddZoneTarget(default, width, height, false);
            _ui?.SetStatus("Add Zone mode: click world to mark a 50x50 zone target, then confirm.");
            UpdateAddZonePreviewVisibility();
        }

        private void MarkAddZoneTargetAtCursor()
        {
            if (!_isAddZoneFlowActive)
                return;

            if (!TryGetCursorTile(out var tilePos))
            {
                _ui?.SetStatus("Cannot mark add target: cursor tile unavailable.");
                return;
            }

            int width = Mathf.Max(1, zoneManager.ZoneWidthTiles);
            int height = Mathf.Max(1, zoneManager.ZoneHeightTiles);

            int alignedX = Mathf.FloorToInt(tilePos.x / (float)width) * width;
            int alignedY = Mathf.FloorToInt(tilePos.y / (float)height) * height;

            _pendingAddZoneOffset = new Vector2Int(alignedX, alignedY);
            _hasPendingAddTarget = true;

            UpdateAddZonePreview();
            _ui?.SetAddZoneTarget(_pendingAddZoneOffset, width, height, true);
            _ui?.SetStatus($"Add Zone target marked at [{alignedX},{alignedY}] ({width}x{height}).");
        }

        private void ConfirmAddZone(string requestedZoneName, bool useSelectedZoneAsTemplate, bool editableInTileEditor)
        {
            if (!_isAddZoneFlowActive)
            {
                _ui?.SetStatus("Add Zone flow is not active.");
                return;
            }

            if (!_hasPendingAddTarget)
            {
                _ui?.SetStatus("Mark a 50x50 target in the world before confirming Add Zone.");
                return;
            }

            string zoneName = (requestedZoneName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(zoneName))
            {
                _ui?.SetStatus("Add Zone failed: empty name.");
                return;
            }

            bool created;
            if (useSelectedZoneAsTemplate)
            {
                if (!_state.HasSelection)
                {
                    _ui?.SetStatus("Add Zone failed: source zone is required for template mode.");
                    return;
                }

                string sourceZoneName = _state.SelectedZone;
                created = zoneManager.AddZoneFromTemplate(sourceZoneName, zoneName, _pendingAddZoneOffset, editableInTileEditor);
            }
            else
            {
                created = zoneManager.AddZone(zoneName, _pendingAddZoneOffset, editableInTileEditor);
            }

            if (!created)
            {
                _ui?.SetStatus($"Add Zone failed for '{zoneName}'. Check name uniqueness and source-zone selection.");
                return;
            }

            _state.SelectZone(zoneName);
            _state.NextZoneIndex++;
            PersistZonesToDisk();

            CancelAddZoneFlow();
            _ui?.SetStatus($"Zone '{zoneName}' added at [{_pendingAddZoneOffset.x},{_pendingAddZoneOffset.y}].");
            RefreshSelectionUIAndOverlay();
        }

        private void CancelAddZoneFlow()
        {
            _isAddZoneFlowActive = false;
            _hasPendingAddTarget = false;
            _ui?.HideAddZoneDialog();
            UpdateAddZonePreviewVisibility();
        }

        private void RequestDeleteSelectedZone()
        {
            if (!_state.HasSelection)
            {
                _ui?.SetStatus("Select a zone before deleting.");
                return;
            }

            _pendingDeleteZoneName = _state.SelectedZone;
            _ui?.ShowDeleteZoneDialog(_pendingDeleteZoneName);
        }

        private void ConfirmDeleteSelectedZone()
        {
            if (string.IsNullOrWhiteSpace(_pendingDeleteZoneName))
            {
                _ui?.SetStatus("No pending zone to delete.");
                return;
            }

            DeleteZoneByName(_pendingDeleteZoneName);
            _pendingDeleteZoneName = null;
        }

        private void DeleteZoneByName(string zoneName)
        {
            if (string.IsNullOrWhiteSpace(zoneName))
            {
                _ui?.SetStatus("Delete failed: invalid zone.");
                return;
            }

            var zones = zoneManager.GetZonesSnapshot();
            if (zones.Length <= 1)
            {
                _ui?.SetStatus("Cannot delete the last remaining zone.");
                return;
            }

            if (!zoneManager.RemoveZone(zoneName))
            {
                _ui?.SetStatus($"Could not delete zone '{zoneName}'.");
                return;
            }

            if (_state.HasSelection && _state.SelectedZone == zoneName)
                _state.ClearSelection();

            _ui?.HideDeleteZoneDialog();
            PersistZonesToDisk();
            _ui?.SetStatus($"Zone '{zoneName}' deleted.");
            RefreshSelectionUIAndOverlay();
        }

        private void DuplicateSelectedZone()
        {
            if (!_state.HasSelection)
            {
                _ui?.SetStatus("Select a zone before duplicating.");
                return;
            }

            string sourceZoneName = _state.SelectedZone;

            if (!zoneManager.DuplicateZone(sourceZoneName, out var duplicatedZoneName))
            {
                _ui?.SetStatus($"Could not duplicate zone '{sourceZoneName}'.");
                return;
            }

            _state.SelectZone(duplicatedZoneName);

            int dx = Mathf.Max(1, zoneManager.ZoneWidthTiles);
            zoneManager.MoveZone(duplicatedZoneName, new Vector2Int(dx, 0));

            PersistZonesToDisk();
            _ui?.SetStatus($"Zone '{sourceZoneName}' duplicated to '{duplicatedZoneName}' and shifted by [{dx},0].");
            RefreshSelectionUIAndOverlay();
        }

        private void RenameSelectedZone(string newName)
        {
            if (!_state.HasSelection)
            {
                _ui?.SetStatus("Select a zone before renaming.");
                return;
            }

            RenameZoneByName(_state.SelectedZone, newName);
        }

        private void RenameZoneByName(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(oldName))
            {
                _ui?.SetStatus("Rename failed: invalid zone.");
                return;
            }

            string trimmed = (newName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                _ui?.SetStatus("Rename failed: empty name.");
                return;
            }

            if (!zoneManager.RenameZone(oldName, trimmed))
            {
                _ui?.SetStatus($"Rename failed: '{trimmed}' may already exist.");
                return;
            }

            _state.SelectZone(trimmed);
            PersistZonesToDisk();
            _ui?.SetStatus($"Renamed '{oldName}' to '{trimmed}'.");
            RefreshSelectionUIAndOverlay();
        }

        private void ToggleSelectedZoneEditable()
        {
            if (!_state.HasSelection)
            {
                _ui?.SetStatus("Select a zone first.");
                return;
            }

            ToggleZoneEditableByName(_state.SelectedZone);
        }

        private void ToggleZoneEditableByName(string zoneName)
        {
            if (string.IsNullOrWhiteSpace(zoneName))
            {
                _ui?.SetStatus("Could not update zone editable state: invalid zone.");
                return;
            }

            if (!zoneManager.TryGetZone(zoneName, out var zone))
            {
                _ui?.SetStatus($"Zone '{zoneName}' no longer exists.");
                return;
            }

            _state.SelectZone(zoneName);

            bool target = !zone.editableInTileEditor;
            if (!zoneManager.SetZoneEditable(zone.zoneName, target))
            {
                _ui?.SetStatus("Could not update zone editable state.");
                return;
            }

            PersistZonesToDisk();
            _ui?.SetStatus($"Zone '{zone.zoneName}' editable = {target}");
            RefreshSelectionUIAndOverlay();
        }

        private void MoveSelectedZone(Vector2Int direction)
        {
            if (!_state.HasSelection)
            {
                _ui?.SetStatus("Select a zone before moving.");
                return;
            }

            int dx = direction.x * Mathf.Max(1, zoneManager.ZoneWidthTiles);
            int dy = direction.y * Mathf.Max(1, zoneManager.ZoneHeightTiles);

            if (!zoneManager.MoveZone(_state.SelectedZone, new Vector2Int(dx, dy)))
            {
                _ui?.SetStatus("Failed to move zone.");
                return;
            }

            PersistZonesToDisk();
            _ui?.SetStatus($"Zone '{_state.SelectedZone}' moved by [{dx},{dy}].");
            RefreshSelectionUIAndOverlay();
        }

        private void SetRestrictTileEditing(bool restrict)
        {
            _state.RestrictTileEditingToEditableZones = restrict;
            ApplyTileEditorConstraint();
            PersistZonesToDisk();

            if (_ui != null)
                _ui.SetStatus(restrict
                    ? "Tile editor restricted to editable zones."
                    : "Tile editor can edit all cells.");
        }

        private void HandleZonesChanged()
        {
            RefreshZoneListUI();
            RebuildZoneOverlays();
            ApplyTileEditorConstraint();

            if (_state.HasSelection && !zoneManager.TryGetZone(_state.SelectedZone, out _))
                _state.ClearSelection();

            if (_isAddZoneFlowActive)
            {
                if (_state.HasSelection && zoneManager.TryGetZone(_state.SelectedZone, out var zone))
                    _ui?.SetAddZoneSource(zone.zoneName, zone.editableInTileEditor);
                else
                    _ui?.SetAddZoneSource("(none)", true);
            }

            RefreshSelectionUIAndOverlay();
        }

        private void RefreshZoneListUI()
        {
            if (_ui == null || zoneManager == null) return;
            _ui.RefreshZones(zoneManager.GetZonesSnapshot());
            _ui.SetRestrictToggle(_state.RestrictTileEditingToEditableZones);
        }

        private void RefreshSelectionUIAndOverlay()
        {
            bool editable = false;
            if (_state.HasSelection && zoneManager.TryGetZone(_state.SelectedZone, out var zone))
                editable = zone.editableInTileEditor;

            _ui?.SetSelectedZone(_state.SelectedZone, editable);
            RecolorZoneOverlays();
        }

        private void ApplyTileEditorConstraint()
        {
            if (tileEditorManager == null)
                tileEditorManager = TileEditorManager.Instance != null ? TileEditorManager.Instance : FindObjectOfType<TileEditorManager>();

            if (tileEditorManager == null) return;

            if (_state.RestrictTileEditingToEditableZones)
                tileEditorManager.SetEditConstraint(zoneManager.IsTileInEditableZone);
            else
                tileEditorManager.ClearEditConstraint();
        }

        private void UpdateAddZonePreviewVisibility()
        {
            if (_addZonePreviewObject == null)
                return;

            _addZonePreviewObject.SetActive(_isAddZoneFlowActive && _hasPendingAddTarget && _state.Active);
        }

        private void UpdateAddZonePreview()
        {
            if (!_hasPendingAddTarget || _overlayRoot == null)
            {
                UpdateAddZonePreviewVisibility();
                return;
            }

            if (_addZonePreviewObject == null)
            {
                _addZonePreviewObject = new GameObject("MapEditorAddZonePreview");
                _addZonePreviewObject.transform.SetParent(_overlayRoot.transform, false);

                var line = _addZonePreviewObject.AddComponent<LineRenderer>();
                line.positionCount = 5;
                line.loop = false;
                line.useWorldSpace = true;
                line.widthMultiplier = Mathf.Max(overlayLineWidth * 1.15f, 0.06f);
                line.material = _overlayLineMaterial;
                line.sortingLayerName = SortingConfig.LAYER_OVERHEAD;
                line.sortingOrder = SortingConfig.Z_UI + 2;
                line.startColor = new Color(0.36f, 0.86f, 1f, 0.95f);
                line.endColor = line.startColor;

                var labelGo = new GameObject("Label");
                labelGo.transform.SetParent(_addZonePreviewObject.transform, false);
                var text = labelGo.AddComponent<TextMeshPro>();
                text.fontSize = 3.1f;
                text.alignment = TextAlignmentOptions.Center;
                text.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_OVERHEAD);
                text.sortingOrder = SortingConfig.Z_UI + 2;
            }

            float tileSize = Mathf.Max(0.01f, zoneManager.TileSize);
            int width = Mathf.Max(1, zoneManager.ZoneWidthTiles);
            int height = Mathf.Max(1, zoneManager.ZoneHeightTiles);

            float minX = _pendingAddZoneOffset.x * tileSize;
            float maxX = (_pendingAddZoneOffset.x + width) * tileSize;
            float minY = _pendingAddZoneOffset.y * tileSize;
            float maxY = (_pendingAddZoneOffset.y + height) * tileSize;
            float z = -0.015f;

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
                previewText.text = $"NEW ZONE [{_pendingAddZoneOffset.x},{_pendingAddZoneOffset.y}]";
            }

            UpdateAddZonePreviewVisibility();
        }

        private void RebuildZoneOverlays()
        {
            for (int i = 0; i < _zoneOverlayObjects.Count; i++)
            {
                if (_zoneOverlayObjects[i] != null)
                    Destroy(_zoneOverlayObjects[i]);
            }
            _zoneOverlayObjects.Clear();

            if (_overlayRoot == null || zoneManager == null) return;

            var zones = zoneManager.GetZonesSnapshot();
            float tileSize = Mathf.Max(0.01f, zoneManager.TileSize);

            for (int i = 0; i < zones.Length; i++)
            {
                var zone = zones[i];
                var zoneRect = zoneManager.GetZoneRect(zone);

                var zoneGo = new GameObject($"ZoneOverlay_{zone.zoneName}");
                zoneGo.transform.SetParent(_overlayRoot.transform, false);

                var line = zoneGo.AddComponent<LineRenderer>();
                line.positionCount = 5;
                line.loop = false;
                line.useWorldSpace = true;
                line.widthMultiplier = overlayLineWidth;
                line.material = _overlayLineMaterial;
                line.sortingLayerName = SortingConfig.LAYER_OVERHEAD;
                line.sortingOrder = SortingConfig.Z_UI;

                float minX = zoneRect.xMin * tileSize;
                float maxX = zoneRect.xMax * tileSize;
                float minY = zoneRect.yMin * tileSize;
                float maxY = zoneRect.yMax * tileSize;
                float z = -0.02f;

                line.SetPosition(0, new Vector3(minX, minY, z));
                line.SetPosition(1, new Vector3(maxX, minY, z));
                line.SetPosition(2, new Vector3(maxX, maxY, z));
                line.SetPosition(3, new Vector3(minX, maxY, z));
                line.SetPosition(4, new Vector3(minX, minY, z));

                var labelGo = new GameObject("Label");
                labelGo.transform.SetParent(zoneGo.transform, false);
                labelGo.transform.position = new Vector3((minX + maxX) * 0.5f, maxY + 0.25f, z);
                var text = labelGo.AddComponent<TextMeshPro>();
                text.text = zone.zoneName;
                text.fontSize = 3.2f;
                text.alignment = TextAlignmentOptions.Center;
                text.sortingLayerID = SortingLayer.NameToID(SortingConfig.LAYER_OVERHEAD);
                text.sortingOrder = SortingConfig.Z_UI;

                _zoneOverlayObjects.Add(zoneGo);
            }

            RecolorZoneOverlays();
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
                Color lineColor;
                if (selected) lineColor = new Color(1f, 0.82f, 0.3f, 0.95f);
                else if (zone.editableInTileEditor) lineColor = new Color(0.4f, 0.96f, 0.4f, 0.82f);
                else lineColor = new Color(1f, 0.36f, 0.36f, 0.82f);

                var line = zoneGo.GetComponent<LineRenderer>();
                if (line != null)
                {
                    line.startColor = lineColor;
                    line.endColor = lineColor;
                }

                var text = zoneGo.GetComponentInChildren<TextMeshPro>();
                if (text != null)
                {
                    text.color = selected ? new Color(1f, 0.9f, 0.45f, 1f) : lineColor;
                    text.text = $"{zone.zoneName} {(zone.editableInTileEditor ? "[EDIT]" : "[LOCK]")}";
                }
            }
        }

        private bool TryGetCursorTile(out Vector2Int tilePos)
        {
            if (_mainCamera == null)
                _mainCamera = Camera.main;

            var mouse = Mouse.current;
            if (_mainCamera == null || mouse == null)
            {
                tilePos = default;
                return false;
            }

            Vector3 mouseWorld = _mainCamera.ScreenToWorldPoint(mouse.position.ReadValue());
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
            while (safety < 10000)
            {
                string candidate = $"zone_{_state.NextZoneIndex:000}";
                if (!zoneManager.TryGetZone(candidate, out _))
                    return candidate;

                _state.NextZoneIndex++;
                safety++;
            }

            return $"zone_{Guid.NewGuid().ToString("N").Substring(0, 6)}";
        }

        private void PersistZonesToDisk()
        {
            if (zoneManager == null) return;

            var data = new ZonePersistenceFile
            {
                restrictTileEditingToEditableZones = _state.RestrictTileEditingToEditableZones,
                nextZoneIndex = _state.NextZoneIndex
            };

            var zones = zoneManager.GetZonesSnapshot();
            for (int i = 0; i < zones.Length; i++)
            {
                data.zones.Add(new ZonePersistenceEntry
                {
                    zoneName = zones[i].zoneName,
                    gridOffsetX = zones[i].gridOffset.x,
                    gridOffsetY = zones[i].gridOffset.y,
                    editableInTileEditor = zones[i].editableInTileEditor
                });
            }

            try
            {
                string json = JsonUtility.ToJson(data, prettyPrint: true);
                File.WriteAllText(PersistencePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MapEditor] Failed to persist zones to '{PersistencePath}': {ex.Message}");
            }
        }

        private void LoadZonesFromDisk()
        {
            if (zoneManager == null) return;
            if (!File.Exists(PersistencePath)) return;

            try
            {
                string json = File.ReadAllText(PersistencePath);
                var data = JsonUtility.FromJson<ZonePersistenceFile>(json);
                if (data == null || data.zones == null || data.zones.Count == 0)
                    return;

                var existingZones = zoneManager.GetZonesSnapshot();
                var musicByName = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < existingZones.Length; i++)
                    musicByName[existingZones[i].zoneName] = existingZones[i].zoneMusic;

                var newZones = new List<ZoneManager.ZoneDefinition>(data.zones.Count);
                for (int i = 0; i < data.zones.Count; i++)
                {
                    var entry = data.zones[i];
                    AudioClip zoneMusic = null;
                    if (!string.IsNullOrWhiteSpace(entry.zoneName) && musicByName.TryGetValue(entry.zoneName, out var clip))
                        zoneMusic = clip;

                    newZones.Add(new ZoneManager.ZoneDefinition
                    {
                        zoneName = entry.zoneName,
                        gridOffset = new Vector2Int(entry.gridOffsetX, entry.gridOffsetY),
                        zoneMusic = zoneMusic,
                        editableInTileEditor = entry.editableInTileEditor
                    });
                }

                zoneManager.ReplaceZones(newZones);
                _state.RestrictTileEditingToEditableZones = data.restrictTileEditingToEditableZones;
                _state.NextZoneIndex = Mathf.Max(1, data.nextZoneIndex);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MapEditor] Failed to load persisted zones from '{PersistencePath}': {ex.Message}");
            }
        }

        protected override void OnDestroy()
        {
            if (zoneManager != null)
                zoneManager.OnZonesChanged -= HandleZonesChanged;

            _input?.Dispose();

            if (tileEditorManager != null)
                tileEditorManager.ClearEditConstraint();

            if (_overlayLineMaterial != null)
                Destroy(_overlayLineMaterial);

            if (_addZonePreviewObject != null)
                Destroy(_addZonePreviewObject);

            base.OnDestroy();
        }
    }
}
