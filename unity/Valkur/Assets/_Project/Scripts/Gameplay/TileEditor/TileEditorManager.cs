using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using Valkur.Core;
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
    public class TileEditorManager : SingletonMonoBehaviour<TileEditorManager>
    {
        [Header("Tile Catalog")]
        [SerializeField] private TileCatalog tileCatalog;

        [Header("Grid Reference")]
        [Tooltip("If null, will search for WorldGridBuilder at runtime.")]
        [SerializeField] private WorldGridBuilder worldGridBuilder;

        private TileEditorState _state;
        private TileEditorUI _ui;
        private Camera _mainCamera;

        // --- Input Actions (New Input System) ---
        private InputAction _toggleAction;
        private InputAction _toolBrushAction;
        private InputAction _toolEraserAction;
        private InputAction _toolFillAction;
        private InputAction _toolEyedropperAction;
        private InputAction _toolSelectAction;
        private InputAction _undoAction;
        private InputAction _redoAction;
        private InputAction _ctrlModifier;

        // Undo/Redo stacks
        private readonly List<TileEditBatch> _undoStack = new List<TileEditBatch>();
        private readonly List<TileEditBatch> _redoStack = new List<TileEditBatch>();
        private TileEditBatch _currentBatch;

        // Brush preview
        private GameObject _brushPreviewGo;
        private SpriteRenderer _brushPreviewRenderer;

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

        protected override void OnSingletonAwake()
        {
            _state = new TileEditorState();

            // Create input actions
            _toggleAction = new InputAction("ToggleTileEditor", InputActionType.Button, "<Keyboard>/f6");
            _toggleAction.Enable();

            _toolBrushAction = new InputAction("ToolBrush", InputActionType.Button, "<Keyboard>/b");
            _toolBrushAction.Enable();
            _toolEraserAction = new InputAction("ToolEraser", InputActionType.Button, "<Keyboard>/e");
            _toolEraserAction.Enable();
            _toolFillAction = new InputAction("ToolFill", InputActionType.Button, "<Keyboard>/f");
            _toolFillAction.Enable();
            _toolEyedropperAction = new InputAction("ToolEyedropper", InputActionType.Button, "<Keyboard>/i");
            _toolEyedropperAction.Enable();
            _toolSelectAction = new InputAction("ToolSelect", InputActionType.Button, "<Keyboard>/s");
            _toolSelectAction.Enable();

            _undoAction = new InputAction("Undo", InputActionType.Button, "<Keyboard>/z");
            _undoAction.Enable();
            _redoAction = new InputAction("Redo", InputActionType.Button, "<Keyboard>/z");
            _redoAction.Enable();
            _ctrlModifier = new InputAction("CtrlMod", InputActionType.Button);
            _ctrlModifier.AddBinding("<Keyboard>/leftCtrl");
            _ctrlModifier.AddBinding("<Keyboard>/rightCtrl");
            _ctrlModifier.Enable();
        }


        private void Start()
        {
            if (_state == null) return; // Duplicate instance being destroyed

            _mainCamera = Camera.main;

            if (worldGridBuilder == null)
                worldGridBuilder = FindObjectOfType<WorldGridBuilder>();

            // Load tile catalog: build at runtime from sprites in Resources/Tiles/
            if (tileCatalog == null)
                tileCatalog = TileCatalog.BuildFromResources();
            if (tileCatalog == null || tileCatalog.Entries.Count == 0)
            {
                // Fallback: try pre-built asset
                tileCatalog = Resources.Load<TileCatalog>("TileCatalog");
            }
            if (tileCatalog != null)
                TileRegistry.Instance.Load(tileCatalog);
            else
                Debug.LogError("[TileEditor] No tiles found. Ensure sprites exist in Resources/Tiles/{category}/ folders.");

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
            if (_toggleAction != null && _toggleAction.WasPerformedThisFrame())
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
            bool ctrl = _ctrlModifier != null && _ctrlModifier.IsPressed();
            if (_toolBrushAction.WasPerformedThisFrame()) OnToolChanged(TileEditorState.Tool.Brush);
            else if (_toolEraserAction.WasPerformedThisFrame()) OnToolChanged(TileEditorState.Tool.Eraser);
            else if (_toolFillAction.WasPerformedThisFrame()) OnToolChanged(TileEditorState.Tool.Fill);
            else if (_toolEyedropperAction.WasPerformedThisFrame()) OnToolChanged(TileEditorState.Tool.Eyedropper);
            else if (_toolSelectAction.WasPerformedThisFrame() && !ctrl)
                OnToolChanged(TileEditorState.Tool.Select);
        }

        private void HandleLayerScroll()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            float scroll = mouse.scroll.ReadValue().y;
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
            bool ctrl = _ctrlModifier != null && _ctrlModifier.IsPressed();
            if (ctrl && _undoAction.WasPerformedThisFrame())
            {
                var kb = Keyboard.current;
                bool shift = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
                if (shift)
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

        private bool _brushDiagLogged = false;

        private void HandleBrushInput(Tilemap tilemap, Vector3Int cellPos)
        {
            if (_state.SelectedTile == null) return;
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                StartBrushStroke(tilemap);
                var edits = TileBrush.Paint(tilemap, cellPos, _state.SelectedTile, _state.BrushSize);
                _currentBatch?.Edits.AddRange(edits);
                _state.IsDragging = true;

                if (!_brushDiagLogged)
                {
                    _brushDiagLogged = true;
                    LogBrushDiagnostics(tilemap, cellPos);
                }
            }
            else if (mouse.leftButton.isPressed && _state.IsDragging)
            {
                var edits = TileBrush.Paint(tilemap, cellPos, _state.SelectedTile, _state.BrushSize);
                _currentBatch?.Edits.AddRange(edits);
            }
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                EndBrushStroke();
                _state.IsDragging = false;
            }
        }

        private void HandleEraserInput(Tilemap tilemap, Vector3Int cellPos)
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                StartBrushStroke(tilemap);
                var edits = TileBrush.Erase(tilemap, cellPos, _state.BrushSize);
                _currentBatch?.Edits.AddRange(edits);
                _state.IsDragging = true;
            }
            else if (mouse.leftButton.isPressed && _state.IsDragging)
            {
                var edits = TileBrush.Erase(tilemap, cellPos, _state.BrushSize);
                _currentBatch?.Edits.AddRange(edits);
            }
            else if (mouse.leftButton.wasReleasedThisFrame)
            {
                EndBrushStroke();
                _state.IsDragging = false;
            }
        }

        private void HandleFillInput(Tilemap tilemap, Vector3Int cellPos)
        {
            if (_state.SelectedTile == null) return;
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                StartBrushStroke(tilemap);
                var edits = TileBrush.FloodFill(tilemap, cellPos, _state.SelectedTile);
                _currentBatch?.Edits.AddRange(edits);
                EndBrushStroke();
            }
        }

        private void HandleEyedropperInput(Tilemap tilemap, Vector3Int cellPos)
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.leftButton.wasPressedThisFrame)
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
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.leftButton.wasPressedThisFrame)
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

            var mouse = Mouse.current;
            Vector3 mouseWorld = _mainCamera.ScreenToWorldPoint(
                mouse != null ? (Vector3)mouse.position.ReadValue() : Vector3.zero);
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

        private void LogBrushDiagnostics(Tilemap tilemap, Vector3Int cellPos)
        {
            var tile = _state.SelectedTile;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== [TileEditor] BRUSH DIAGNOSTICS (first paint) ===");

            // Tile info
            sb.AppendLine($"  tile={tile?.name ?? "NULL"} type={tile?.GetType().Name ?? "?"}");
            if (tile is UnityEngine.Tilemaps.Tile t)
            {
                var spr = t.sprite;
                sb.AppendLine($"  sprite={spr?.name ?? "NULL"} spriteNull={spr == null}");
                if (spr != null)
                {
                    sb.AppendLine($"  sprite.texture={spr.texture?.name ?? "NULL"} texNull={spr.texture == null}");
                    if (spr.texture != null)
                        sb.AppendLine($"  texSize={spr.texture.width}x{spr.texture.height} ppu={spr.pixelsPerUnit}");
                }
                sb.AppendLine($"  tile.color={t.color}");
            }

            // Tilemap info
            sb.AppendLine($"  tilemap={tilemap.name} cellPos={cellPos}");
            var renderer = tilemap.GetComponent<TilemapRenderer>();
            if (renderer != null)
            {
                sb.AppendLine($"  renderer.enabled={renderer.enabled}");
                sb.AppendLine($"  sortingLayer={renderer.sortingLayerName} sortingOrder={renderer.sortingOrder}");
                var mat = renderer.sharedMaterial;
                sb.AppendLine($"  material={mat?.name ?? "NULL"} shader={mat?.shader?.name ?? "NULL"}");
            }
            else
            {
                sb.AppendLine("  renderer=NULL (no TilemapRenderer!)");
            }

            // Light2D check — also read lightType to verify Global vs Freeform
            var light2DType = System.Type.GetType(
                "UnityEngine.Rendering.Universal.Light2D, Unity.RenderPipelines.Universal.Runtime");
            if (light2DType != null)
            {
                var lights = FindObjectsOfType(light2DType);
                sb.AppendLine($"  Light2D count={lights.Length}");

                // Try to read lightType property or m_LightType field
                var ltProp = light2DType.GetProperty("lightType",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var ltField = light2DType.GetField("m_LightType",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var intProp = light2DType.GetProperty("intensity",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                foreach (var l in lights)
                {
                    var go = ((Component)l).gameObject;
                    string ltVal = "?";
                    if (ltProp != null)
                    {
                        try { ltVal = $"prop={ltProp.GetValue(l)} ({(int)ltProp.GetValue(l)})"; }
                        catch { ltVal = "prop-read-error"; }
                    }
                    else if (ltField != null)
                    {
                        try { ltVal = $"field={ltField.GetValue(l)} ({(int)ltField.GetValue(l)})"; }
                        catch { ltVal = "field-read-error"; }
                    }
                    else
                    {
                        ltVal = "NO_PROP_OR_FIELD";
                    }

                    string intVal = "?";
                    if (intProp != null)
                    {
                        try { intVal = intProp.GetValue(l)?.ToString(); }
                        catch { intVal = "read-error"; }
                    }

                    sb.AppendLine($"    Light2D: '{go.name}' active={go.activeInHierarchy} lightType={ltVal} intensity={intVal}");
                }
            }
            else
            {
                sb.AppendLine("  Light2D type NOT FOUND (URP 2D Renderer missing?)");
            }

            sb.AppendLine("=== END DIAGNOSTICS ===");
            Debug.Log(sb.ToString());
        }

        protected override void OnDestroy()
        {
            _toggleAction?.Disable(); _toggleAction?.Dispose();
            _toolBrushAction?.Disable(); _toolBrushAction?.Dispose();
            _toolEraserAction?.Disable(); _toolEraserAction?.Dispose();
            _toolFillAction?.Disable(); _toolFillAction?.Dispose();
            _toolEyedropperAction?.Disable(); _toolEyedropperAction?.Dispose();
            _toolSelectAction?.Disable(); _toolSelectAction?.Dispose();
            _undoAction?.Disable(); _undoAction?.Dispose();
            _redoAction?.Disable(); _redoAction?.Dispose();
            _ctrlModifier?.Disable(); _ctrlModifier?.Dispose();
            base.OnDestroy();
        }
    }
}
