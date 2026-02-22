using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.Rendering;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// Main orchestrator for the in-game tile editor.
    /// Toggle with F6. Handles input, delegates to TileBrush, manages undo stack.
    /// Maps to Python's TileEditorController + TileEditorEventHandler.
    ///
    /// Attach to a persistent GameObject in the gameplay scene, or let GameDirector create it.
    /// Requires a TileCatalog asset assigned via inspector or loaded at runtime.
    /// </summary>
    public class TileEditorManager : MonoBehaviour
    {
        [Header("Tile Catalog")]
        [SerializeField] private TileCatalog tileCatalog;

        [Header("Grid Reference")]
        [Tooltip("If null, will search for WorldGridBuilder at runtime.")]
        [SerializeField] private WorldGridBuilder worldGridBuilder;

        private TileEditorState _state;
        private TileEditorUI _ui;
        private Camera _mainCamera;

        // Undo/Redo stacks
        private readonly List<TileEditBatch> _undoStack = new List<TileEditBatch>();
        private readonly List<TileEditBatch> _redoStack = new List<TileEditBatch>();
        private TileEditBatch _currentBatch;

        // Brush preview
        private GameObject _brushPreviewGo;
        private SpriteRenderer _brushPreviewRenderer;

        private static TileEditorManager _instance;
        public static TileEditorManager Instance => _instance;

        // Screen border overlay (visual feedback when editor is active)
        private GameObject _borderOverlayGo;
        private TileEditorGridCursor _gridCursor;

        public TileEditorState State => _state;
        public bool IsActive => _state != null && _state.Active;

        /// <summary>
        /// Called by GameplaySceneSetup to wire the grid builder reference.
        /// </summary>
        public void SetGridBuilder(WorldGridBuilder builder)
        {
            worldGridBuilder = builder;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            _state = new TileEditorState();
        }

        private void Start()
        {
            if (_state == null) return; // Duplicate instance being destroyed

            _mainCamera = Camera.main;

            if (worldGridBuilder == null)
                worldGridBuilder = FindObjectOfType<WorldGridBuilder>();

            // Load tile catalog: try inspector assignment first, then Resources
            if (tileCatalog == null)
                tileCatalog = Resources.Load<TileCatalog>("TileCatalog");
            if (tileCatalog != null)
                TileRegistry.Instance.Load(tileCatalog);
            else
                Debug.LogWarning("[TileEditor] No TileCatalog found. Run Valkur > Atlas > Generate Tile Catalog, then place in Resources/ or assign via inspector.");

            // Create UI
            var uiGo = new GameObject("TileEditorUI");
            uiGo.transform.SetParent(transform);
            _ui = uiGo.AddComponent<TileEditorUI>();
            _ui.Initialize(_state, tileCatalog,
                OnTileSelected,
                OnToolChanged,
                OnLayerChanged,
                OnBrushSizeChanged);

            // Create brush preview
            CreateBrushPreview();

            // Create screen border overlay
            CreateScreenBorderOverlay();

            // Create grid cursor highlight
            CreateGridCursor();
        }

        private void Update()
        {
            if (_state == null) return;

            HandleToggle();

            if (!_state.Active) return;

            HandleToolShortcuts();
            HandleLayerScroll();
            HandleUndoRedo();
            HandleMouseInput();
            UpdateBrushPreview();
            UpdateGridCursor();
            UpdateViewPanelHover();
        }

        // =====================================================================
        // TOGGLE
        // =====================================================================

        private void HandleToggle()
        {
            if (Input.GetKeyDown(KeyCode.F6))
            {
                _state.Active = !_state.Active;
                _ui.SetVisible(_state.Active);

                if (_state.Active)
                {
                    // Reset to Ground layer on open to avoid accidental painting on wrong layer
                    _state.CurrentLayer = TilemapLayerSetup.TilemapLayer.Ground;
                    _ui.RefreshToolHighlights();
                    _ui.RefreshLayerLabel();
                    _ui.RefreshBrushSizeLabel();
                    _ui.RefreshTilePicker();
                    _ui.SetStatus("Tile Editor active. F6 to close.");
                    if (_borderOverlayGo != null) _borderOverlayGo.SetActive(true);
                    if (_gridCursor != null) _gridCursor.gameObject.SetActive(true);
                    UpdateBorderToolLabel();
                    Debug.Log("[TileEditor] Activated (F6)");
                }
                else
                {
                    EndBrushStroke();
                    HideBrushPreview();
                    if (_borderOverlayGo != null) _borderOverlayGo.SetActive(false);
                    if (_gridCursor != null) _gridCursor.gameObject.SetActive(false);
                    Debug.Log("[TileEditor] Deactivated (F6)");
                }
            }
        }

        // =====================================================================
        // TOOL SHORTCUTS
        // =====================================================================

        private void HandleToolShortcuts()
        {
            if (Input.GetKeyDown(KeyCode.B)) OnToolChanged(TileEditorState.Tool.Brush);
            else if (Input.GetKeyDown(KeyCode.E)) OnToolChanged(TileEditorState.Tool.Eraser);
            else if (Input.GetKeyDown(KeyCode.F)) OnToolChanged(TileEditorState.Tool.Fill);
            else if (Input.GetKeyDown(KeyCode.I)) OnToolChanged(TileEditorState.Tool.Eyedropper);
            else if (Input.GetKeyDown(KeyCode.S) && !Input.GetKey(KeyCode.LeftControl))
                OnToolChanged(TileEditorState.Tool.Select);
        }

        private void HandleLayerScroll()
        {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) < 0.1f) return;

            // Only cycle layers when not hovering over UI
            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                return;

            int val = (int)_state.CurrentLayer + (scroll > 0 ? 1 : -1);
            if (val < 0) val = 8;
            if (val > 8) val = 0;
            OnLayerChanged((TilemapLayerSetup.TilemapLayer)val);
        }

        private void HandleUndoRedo()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && Input.GetKeyDown(KeyCode.Z))
            {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                    Redo();
                else
                    Undo();
            }
        }

        // =====================================================================
        // MOUSE INPUT
        // =====================================================================

        private void HandleMouseInput()
        {
            // Skip if over UI
            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                return;

            var tilemap = GetCurrentTilemap();
            if (tilemap == null) return;

            Vector3Int cellPos = GetCellUnderMouse(tilemap);

            switch (_state.CurrentTool)
            {
                case TileEditorState.Tool.Brush:
                    HandleBrushInput(tilemap, cellPos);
                    break;
                case TileEditorState.Tool.Eraser:
                    HandleEraserInput(tilemap, cellPos);
                    break;
                case TileEditorState.Tool.Fill:
                    HandleFillInput(tilemap, cellPos);
                    break;
                case TileEditorState.Tool.Eyedropper:
                    HandleEyedropperInput(tilemap, cellPos);
                    break;
                case TileEditorState.Tool.Select:
                    HandleSelectInput(tilemap, cellPos);
                    break;
            }
        }

        private void HandleBrushInput(Tilemap tilemap, Vector3Int cellPos)
        {
            if (_state.SelectedTile == null) return;

            if (Input.GetMouseButtonDown(0))
            {
                StartBrushStroke(tilemap);
                var edits = TileBrush.Paint(tilemap, cellPos, _state.SelectedTile, _state.BrushSize);
                _currentBatch?.Edits.AddRange(edits);
                _state.IsDragging = true;
            }
            else if (Input.GetMouseButton(0) && _state.IsDragging)
            {
                var edits = TileBrush.Paint(tilemap, cellPos, _state.SelectedTile, _state.BrushSize);
                _currentBatch?.Edits.AddRange(edits);
            }
            else if (Input.GetMouseButtonUp(0))
            {
                EndBrushStroke();
                _state.IsDragging = false;
            }
        }

        private void HandleEraserInput(Tilemap tilemap, Vector3Int cellPos)
        {
            if (Input.GetMouseButtonDown(0))
            {
                StartBrushStroke(tilemap);
                var edits = TileBrush.Erase(tilemap, cellPos, _state.BrushSize);
                _currentBatch?.Edits.AddRange(edits);
                _state.IsDragging = true;
            }
            else if (Input.GetMouseButton(0) && _state.IsDragging)
            {
                var edits = TileBrush.Erase(tilemap, cellPos, _state.BrushSize);
                _currentBatch?.Edits.AddRange(edits);
            }
            else if (Input.GetMouseButtonUp(0))
            {
                EndBrushStroke();
                _state.IsDragging = false;
            }
        }

        private void HandleFillInput(Tilemap tilemap, Vector3Int cellPos)
        {
            if (_state.SelectedTile == null) return;

            if (Input.GetMouseButtonDown(0))
            {
                StartBrushStroke(tilemap);
                var edits = TileBrush.FloodFill(tilemap, cellPos, _state.SelectedTile);
                _currentBatch?.Edits.AddRange(edits);
                EndBrushStroke();
            }
        }

        private void HandleEyedropperInput(Tilemap tilemap, Vector3Int cellPos)
        {
            if (Input.GetMouseButtonDown(0))
            {
                var picked = TileBrush.Pick(tilemap, cellPos);
                if (picked != null)
                {
                    _state.SelectedTile = picked;
                    _ui.SetStatus($"Picked: {picked.name}");

                    // Update View Panel selected + left panel preview
                    Sprite sprite = null;
                    if (picked is Tile pickedTile) sprite = pickedTile.sprite;
                    _ui.UpdateViewPanelSelected(sprite, picked.name);
                    _ui.UpdateSelectedTilePreview(sprite, picked.name);

                    OnToolChanged(TileEditorState.Tool.Brush);
                }
            }
        }

        private void HandleSelectInput(Tilemap tilemap, Vector3Int cellPos)
        {
            if (Input.GetMouseButtonDown(0))
            {
                var tile = tilemap.GetTile(cellPos);
                string info = tile != null ? tile.name : "(empty)";
                _ui.SetStatus($"Cell ({cellPos.x},{cellPos.y}) Layer:{_state.CurrentLayer} Tile:{info}");

                // Update View Panel selected
                Sprite sprite = null;
                if (tile is Tile t) sprite = t.sprite;
                _ui.UpdateViewPanelSelected(sprite, info);
            }
        }

        // =====================================================================
        // SCREEN BORDER OVERLAY (visual mode indicator like Python)
        // =====================================================================

        private void CreateScreenBorderOverlay()
        {
            _borderOverlayGo = new GameObject("TileEditorBorderOverlay");
            _borderOverlayGo.transform.SetParent(transform);
            var overlay = _borderOverlayGo.AddComponent<TileEditorBorderOverlay>();
            overlay.Initialize();
            _borderOverlayGo.SetActive(false);
        }

        private void CreateGridCursor()
        {
            var cursorGo = new GameObject("TileEditorGridCursor");
            cursorGo.transform.SetParent(transform);
            _gridCursor = cursorGo.AddComponent<TileEditorGridCursor>();
            _gridCursor.Initialize();
            cursorGo.SetActive(false);
        }

        // =====================================================================
        // BRUSH STROKE MANAGEMENT
        // =====================================================================

        private void StartBrushStroke(Tilemap tilemap)
        {
            _currentBatch = new TileEditBatch { TargetTilemap = tilemap };
        }

        private void EndBrushStroke()
        {
            if (_currentBatch == null) return;
            if (_currentBatch.Edits.Count > 0)
            {
                _undoStack.Add(_currentBatch);
                if (_undoStack.Count > TileEditorState.MAX_UNDO)
                    _undoStack.RemoveAt(0);
                _redoStack.Clear();
            }
            _currentBatch = null;
        }

        private void Undo()
        {
            if (_undoStack.Count == 0) return;
            var batch = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            batch.Undo();
            _redoStack.Add(batch);
            _ui.SetStatus("Undo");
        }

        private void Redo()
        {
            if (_redoStack.Count == 0) return;
            var batch = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            batch.Redo();
            _undoStack.Add(batch);
            _ui.SetStatus("Redo");
        }

        // =====================================================================
        // BRUSH PREVIEW
        // =====================================================================

        private void CreateBrushPreview()
        {
            _brushPreviewGo = new GameObject("BrushPreview");
            _brushPreviewGo.transform.SetParent(transform);
            _brushPreviewRenderer = _brushPreviewGo.AddComponent<SpriteRenderer>();
            _brushPreviewRenderer.sortingOrder = 999;
            _brushPreviewRenderer.color = new Color(1f, 1f, 1f, 0.4f);
            _brushPreviewGo.SetActive(false);
        }

        private void UpdateBrushPreview()
        {
            // Brush preview disabled — grid cursor provides visual feedback instead.
            // The SpriteRenderer preview was rendering as a black rectangle because
            // atlas-packed sprites don't resolve correctly on a standalone SpriteRenderer
            // without proper sorting layer assignment.
            HideBrushPreview();
        }

        private void HideBrushPreview()
        {
            if (_brushPreviewGo != null)
                _brushPreviewGo.SetActive(false);
        }

        private void UpdateGridCursor()
        {
            if (_gridCursor == null) return;

            // Skip if over UI
            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            {
                _gridCursor.gameObject.SetActive(false);
                return;
            }

            var tilemap = GetCurrentTilemap();
            if (tilemap == null)
            {
                _gridCursor.gameObject.SetActive(false);
                return;
            }

            _gridCursor.gameObject.SetActive(true);
            Vector3Int cellPos = GetCellUnderMouse(tilemap);
            Vector3 worldPos = GetCellWorldCenter(tilemap, cellPos);
            _gridCursor.UpdateCursor(worldPos, _state.BrushSize, _state.CurrentTool);
        }

        // =====================================================================
        // CALLBACKS
        // =====================================================================

        private void OnTileSelected(TileCatalog.TileEntry entry)
        {
            _state.SelectedTile = entry.tile;
            _state.SelectedCategory = entry.category;
            _ui.SetStatus($"Selected: {entry.tileName}");

            // Update selected tile preview in left panel + view panel choice
            Sprite preview = entry.preview;
            if (preview == null && entry.tile is Tile tileAsset)
                preview = tileAsset.sprite;
            _ui.UpdateSelectedTilePreview(preview, entry.tileName);

            if (_state.CurrentTool == TileEditorState.Tool.Select ||
                _state.CurrentTool == TileEditorState.Tool.Eyedropper)
            {
                OnToolChanged(TileEditorState.Tool.Brush);
            }
        }

        private void OnToolChanged(TileEditorState.Tool tool)
        {
            EndBrushStroke();
            _state.CurrentTool = tool;
            _state.IsDragging = false;
            _ui.RefreshToolHighlights();
            _ui.SetStatus($"Tool: {tool}");
            UpdateBorderToolLabel();
        }

        private void UpdateBorderToolLabel()
        {
            if (_borderOverlayGo == null) return;
            var overlay = _borderOverlayGo.GetComponent<TileEditorBorderOverlay>();
            if (overlay != null)
                overlay.SetToolLabel(_state.CurrentTool.ToString().ToUpper());
        }

        private void OnLayerChanged(TilemapLayerSetup.TilemapLayer layer)
        {
            _state.CurrentLayer = layer;
            _ui.RefreshLayerLabel();
        }

        private void OnBrushSizeChanged(int newSize)
        {
            _state.BrushSize = Mathf.Clamp(newSize, 1, 5);
            _ui.RefreshBrushSizeLabel();
        }

        // =====================================================================
        // VIEW PANEL HOVER UPDATE
        // =====================================================================

        private void UpdateViewPanelHover()
        {
            if (_ui == null) return;

            // Skip if over UI
            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            {
                _ui.UpdateViewPanelHovered(null, "", "");
                return;
            }

            var tilemap = GetCurrentTilemap();
            if (tilemap == null)
            {
                _ui.UpdateViewPanelHovered(null, "", "");
                return;
            }

            Vector3Int cellPos = GetCellUnderMouse(tilemap);
            var tileBase = tilemap.GetTile(cellPos);
            if (tileBase != null)
            {
                Sprite sprite = null;
                if (tileBase is Tile t) sprite = t.sprite;
                string layerName = $"{(int)_state.CurrentLayer}: {_state.CurrentLayer}";
                _ui.UpdateViewPanelHovered(sprite, tileBase.name, layerName);
            }
            else
            {
                _ui.UpdateViewPanelHovered(null, $"({cellPos.x},{cellPos.y}) empty", $"{(int)_state.CurrentLayer}: {_state.CurrentLayer}");
            }
        }

        // =====================================================================
        // HELPERS
        // =====================================================================

        private Tilemap GetCurrentTilemap()
        {
            if (worldGridBuilder == null) return null;
            return worldGridBuilder.GetTilemap(_state.CurrentLayer);
        }

        private Vector3Int GetCellUnderMouse(Tilemap tilemap)
        {
            if (_mainCamera == null)
                _mainCamera = Camera.main;

            Vector3 mouseWorld = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
            mouseWorld.z = 0f;
            return tilemap.WorldToCell(mouseWorld);
        }

        /// <summary>
        /// Get the true world-space center of a cell, ignoring tileAnchor.
        /// CellToWorld returns the bottom-left corner; we add half the cell size.
        /// </summary>
        private Vector3 GetCellWorldCenter(Tilemap tilemap, Vector3Int cellPos)
        {
            Vector3 bottomLeft = tilemap.CellToWorld(cellPos);
            Vector3 cellSize = tilemap.cellSize;
            return bottomLeft + new Vector3(cellSize.x * 0.5f, cellSize.y * 0.5f, 0f);
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }
    }
}
