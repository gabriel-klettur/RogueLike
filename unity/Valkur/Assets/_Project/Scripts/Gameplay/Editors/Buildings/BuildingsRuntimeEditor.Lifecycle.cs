using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Buildings
{
    public partial class BuildingsRuntimeEditor : SingletonMonoBehaviour<BuildingsRuntimeEditor>, GameEditorManager.IGameEditor
    {

        public void Activate()
        {
            if (!_uiBuilt)
            {
                try { BuildUI(); _uiBuilt = true; }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[BuildingsEditor] BuildUI failed: {ex.Message}\n{ex.StackTrace}");
                    CleanupUI();
                    return;
                }
            }
            EnsureRuntimeFx();
            CacheBuildingLoader();
            _active = true;
            _canvas.gameObject.SetActive(true);
            _canvas.enabled = true;
            _root.SetActive(true);
            _mode = EditorMode.Select;
            RefreshPicker();
            RefreshModeButtons();
            RefreshInspector();
            ApplyBuildingsVisibility();
            RefreshBuildingsVisibilityButton();
            if (_statusTmp != null)
                _statusTmp.text = "Buildings Editor active. F10 = close. ESC = cancel.";
            _mainCamera = Camera.main;
            if (Valkur.Gameplay.CameraSetup.Instance != null)
                Valkur.Gameplay.CameraSetup.Instance.DetachFollow();
            HideHUDs();
            Debug.Log("[BuildingsEditor] Activated (F10)");
        }

        public void Deactivate()
        {
            PersistDirtyInstanceChanges("Deactivate");
            _buildingsVisible = true;
            ApplyBuildingsVisibility();
            _active = false;
            if (_uiBuilt && _root != null)
            {
                _root.SetActive(false);
                if (_canvas != null) { _canvas.enabled = false; _canvas.gameObject.SetActive(false); }
            }
            HideOutlines();
            _selectedTemplateId = -1;
            _propertiesMode = PropertiesMode.None;
            _activeBuilding = null;
            _hoveredBuilding = null;
            _hoverStack.Clear();
            _dragging = false;
            _resizing = false;
            _splitDragging = false;
            _splitHovering = false;
            _removeMode = false;
            _collBrushMode = CollBrushMode.Off;
            _activeColliderSession = null;
            _colliderStroke.Active = false;
            _cameraPan.Reset();
            _doubleClick.Reset();
            HideCollBrushCursor();
            CancelPickerDrag();
            HideConfirm();
            ExitFillMode();
            ExitEraseMode(setSelectMode: false);
            // Drop any pending undo/redo entries — Ctrl+Z should be a strict no-op
            // outside the editor. Without this clear, an undo stack populated during
            // the previous editor session could be triggered by Unity's own Edit>Undo
            // shortcut (Ctrl+Z) routed through the focused Game view, replaying our
            // do/undo lambdas after the editor is already closed.
            _undo.Clear();
            RestoreHUDs();
            if (Valkur.Gameplay.CameraSetup.Instance != null)
                Valkur.Gameplay.CameraSetup.Instance.ReattachFollow();
            if (GameEditorManager.HasInstance) GameEditorManager.Instance.NotifyDeactivated(this);
            Debug.Log("[BuildingsEditor] Deactivated (F10)");
        }

        private void OnApplicationQuit()
        {
            PersistDirtyInstanceChanges("ApplicationQuit");
        }

        private void ToggleActive() { if (_active) Deactivate(); else Activate(); }

        private void CleanupUI()
        {
            if (_root != null)   { Destroy(_root);   _root = null; }
            if (_canvas != null) { Destroy(_canvas.gameObject); _canvas = null; }
            _uiRefs = default;
            _openDropdowns.Clear();
            _pickerContent = null; _statusTmp = null; _propsTmp = null;
            _selectBtnImg = _placeBtnImg = _deleteBtnImg = _resizeBtnImg = null;
            _addBtnImg = _removeBtnImg = null;
            _searchBox = null;
            _inspectorRoot = null; _splitSlider = null;
            _zBottomVal = _zTopVal = null;
            _gridColsVal = _gridRowsVal = null;
            _scopeBtnLabel = null; _scopeBtnImg = null;
            _handlesRoot = null; _handleR = null;
            _zTopBadgeRt = null; _zBotBadgeRt = null;
            _zTopBadgeTmp = null; _zBotBadgeTmp = null;
            _tutorialRoot = null; _tutorialStepLabel = _tutorialBodyTmp = null;
            _confirmModal = null; _confirmText = null;
            _idLabelTmp = null; _idLabelRt = null;
            _splitLineRt = null; _splitLineImg = null;
            _splitHandleRt = null; _splitHandleImg = null;
            _dragGhostGo = null; _dragGhostRt = null; _dragGhostImg = null; _dragGhostOutline = null;
            _pickerDragging = false; _pickerDragTemplateId = -1;
            _fillBtnImg = null;
            _eraseBtnImg = null;
            _eraseSubPanel = null;
            _eraseTilesAreaBtnImg = null;
            _eraseZoneBtnImg = null;
            _eraseConfirmModal = null;
            _eraseConfirmText = null;
            _eraseConfirmYes = null;
            _eraseMatches.Clear();
            _eraseAreaCells.Clear();
            _eraseStep = EraseStep.Idle;
            _eraseMatchFxPool.Clear();
            _fillSpacingModal = null; _fillSpacingInput = null;
            _buildingsPanelHeaderImg = null; _fillBtnImg = null;
            _uiBuilt = false;
        }

        private void EnsureRuntimeFx()
        {
            if (_hoverFx == null)
            {
                var go = new GameObject("BuildingsEditor.HoverFx");
                go.transform.SetParent(transform, false);
                _hoverFx = go.AddComponent<BuildingOutlineRenderer>();
                _hoverFx.Configure(HOVER_CYAN, HOVER_THICKNESS_WORLD, drawFill: false, fillColor: Color.clear);
            }
            if (_activeFx == null)
            {
                var go = new GameObject("BuildingsEditor.ActiveFx");
                go.transform.SetParent(transform, false);
                _activeFx = go.AddComponent<BuildingOutlineRenderer>();
                _activeFx.Configure(ACTIVE_YELLOW, ACTIVE_THICKNESS_WORLD, drawFill: false, fillColor: Color.clear);
            }
        }

        private void HideOutlines()
        {
            if (_hoverFx  != null) { _hoverFx.Follow(null);  _hoverFx.SetVisible(false); }
            if (_activeFx != null) { _activeFx.Follow(null); _activeFx.SetVisible(false); }
            foreach (var fx in _sameTemplateFxPool)
                if (fx != null) { fx.Follow(null); fx.SetVisible(false); }
            if (_idLabelRt  != null) _idLabelRt.gameObject.SetActive(false);
            if (_handlesRoot != null) _handlesRoot.SetActive(false);
            if (_zTopBadgeRt != null) _zTopBadgeRt.gameObject.SetActive(false);
            if (_zBotBadgeRt != null) _zBotBadgeRt.gameObject.SetActive(false);
            if (_splitLineRt   != null) _splitLineRt.gameObject.SetActive(false);
            if (_splitHandleRt != null) _splitHandleRt.gameObject.SetActive(false);
        }

        /// <summary>
        /// Rebuilds the pool of orange-outline renderers that highlight every scene
        /// building that uses the same source image asset as <paramref name="active"/>.
        /// Called whenever the selected building changes.
        /// </summary>
        private void RebuildSameTemplateFx(BuildingObject active)
        {
            _sameTemplateBuildings.Clear();

            if (active != null && active.Template != null)
            {
                // Match by source image path so ALL buildings that visually use the
                // same sprite asset are highlighted, regardless of templateId.
                string activeImage = NormalizeAssetPath(active.Template.sourceImagePath ?? "");
                if (!string.IsNullOrEmpty(activeImage))
                {
                    var all = GetCachedBuildings();
                    for (int i = 0; i < all.Length; i++)
                    {
                        var b = all[i];
                        if (b == null || b == active || b.Template == null) continue;
                        string bImage = NormalizeAssetPath(b.Template.sourceImagePath ?? "");
                        if (string.Equals(bImage, activeImage, StringComparison.OrdinalIgnoreCase))
                            _sameTemplateBuildings.Add(b);
                    }
                }
            }

            // Grow the pool if this selection has more peers than we've seen before.
            while (_sameTemplateFxPool.Count < _sameTemplateBuildings.Count)
            {
                var go = new GameObject("BuildingsEditor.SameTemplateFx");
                go.transform.SetParent(transform, false);
                var fx = go.AddComponent<BuildingOutlineRenderer>();
                fx.Configure(SAME_TEMPLATE_ORANGE, SAME_TEMPLATE_THICKNESS_WORLD, drawFill: false, fillColor: Color.clear);
                _sameTemplateFxPool.Add(fx);
            }

            // Assign Follow targets; hide surplus pool entries.
            for (int i = 0; i < _sameTemplateFxPool.Count; i++)
            {
                if (i < _sameTemplateBuildings.Count)
                {
                    _sameTemplateFxPool[i].Follow(_sameTemplateBuildings[i]);
                    _sameTemplateFxPool[i].SetVisible(true);
                }
                else
                {
                    _sameTemplateFxPool[i].Follow(null);
                    _sameTemplateFxPool[i].SetVisible(false);
                }
            }
        }

        // â”€â”€ Collider-brush hover cursor â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void EnsureCollBrushCursor()
        {
            if (_collBrushCursorGo != null) return;

            _collBrushCursorMat = new Material(
                Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                ?? Shader.Find("Sprites/Default"))
                { hideFlags = HideFlags.HideAndDontSave };

            _collBrushCursorGo = new GameObject("BuildingsEditor.CollBrushCursor");
            _collBrushCursorGo.transform.SetParent(transform, false);

            var lr = _collBrushCursorGo.AddComponent<LineRenderer>();
            lr.useWorldSpace   = true;
            lr.loop            = true;
            lr.positionCount   = 4;
            lr.startWidth      = CollBrushCursorLineWidth;
            lr.endWidth        = CollBrushCursorLineWidth;
            lr.sortingOrder    = 998;
            lr.sharedMaterial  = _collBrushCursorMat;
            lr.startColor      = CollBrushCursorColor;
            lr.endColor        = CollBrushCursorColor;
            _collBrushCursorLine = lr;

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(_collBrushCursorGo.transform, false);
            _collBrushCursorFill              = fillGo.AddComponent<SpriteRenderer>();
            _collBrushCursorFill.sortingOrder  = 997;
            var fillColor = CollBrushCursorColor;
            fillColor.a   = CollBrushCursorFillAlpha;
            _collBrushCursorFill.color  = fillColor;
            _collBrushCursorFill.sprite = CreateCursorSprite();
            _collBrushCursorGo.SetActive(false);
        }

        private void HideCollBrushCursor()
        {
            if (_collBrushCursorGo != null) _collBrushCursorGo.SetActive(false);
        }

        private void UpdateCollBrushCursor()
        {
            if (!BrushOn || _activeBuilding == null)
            {
                HideCollBrushCursor();
                return;
            }

            // Don't bail when Mouse.current is null — MouseInputManager wraps
            // the legacy backend so we still have a position to render the cursor.
            var cam = Camera.main;
            if (cam == null) { HideCollBrushCursor(); return; }

            if (!_activeBuilding.TryGetWorldRect(out var rect)) { HideCollBrushCursor(); return; }

            Vector2 screenPos = Valkur.Core.Input.MouseInputManager.GetScreenMousePosition();
            Vector3 worldPos  = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -cam.transform.position.z));
            worldPos.z = 0f;

            if (!rect.Contains(worldPos)) { HideCollBrushCursor(); return; }

            var session = EnsureActiveColliderSession();
            if (session?.WorkingGrid == null || session.WorkingGrid.width <= 0 || session.WorkingGrid.height <= 0)
            {
                HideCollBrushCursor();
                return;
            }

            int gridW = session.WorkingGrid.width;
            int gridH = session.WorkingGrid.height;
            float cellW = rect.width  / gridW;
            float cellH = rect.height / gridH;

            float u = Mathf.Clamp01((worldPos.x - rect.xMin) / rect.width);
            float v = Mathf.Clamp01((worldPos.y - rect.yMin) / rect.height);
            int col = Mathf.Clamp(Mathf.FloorToInt(u * gridW), 0, gridW - 1);
            int row = Mathf.Clamp(Mathf.FloorToInt((1f - v) * gridH), 0, gridH - 1);

            // Cursor is centred on the hit cell and covers the full brush footprint.
            float cx = rect.xMin + (col + 0.5f) * cellW;
            float cy = rect.yMax - (row + 0.5f) * cellH;  // row 0 = top of building
            float halfW = _collBrushSize * cellW * 0.5f;
            float halfH = _collBrushSize * cellH * 0.5f;
            var center  = new Vector3(cx, cy, 0f);

            EnsureCollBrushCursor();
            _collBrushCursorGo.SetActive(true);

            // Border
            _collBrushCursorLine.SetPosition(0, center + new Vector3(-halfW, -halfH));
            _collBrushCursorLine.SetPosition(1, center + new Vector3( halfW, -halfH));
            _collBrushCursorLine.SetPosition(2, center + new Vector3( halfW,  halfH));
            _collBrushCursorLine.SetPosition(3, center + new Vector3(-halfW,  halfH));

            // Fill
            _collBrushCursorFill.transform.position   = center;
            _collBrushCursorFill.transform.localScale  = new Vector3(halfW * 2f, halfH * 2f, 1f);
        }

        private static Sprite CreateCursorSprite()
        {
            var tex    = new Texture2D(4, 4) { filterMode = FilterMode.Point };
            var pixels = new Color[16];
            for (int i = 0; i < 16; i++) pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        }

        private void CacheBuildingLoader()
        {
            if (_buildingLoader != null && _buildingsRoot != null) return;
            _buildingLoader = FindObjectOfType<BuildingLoader>();
            if (_buildingLoader != null)
            {
                var f = typeof(BuildingLoader).GetField("_buildingsRoot",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                _buildingsRoot = f?.GetValue(_buildingLoader) as Transform;
            }
            // Fallback: spawn under our own transform
            if (_buildingsRoot == null) _buildingsRoot = transform;
        }

        // ── HUD visibility management ────────────────────────────────────────────────

        /// <summary>
        /// Captures the current active-state of Spell HUD, Inventory, and Music Player,
        /// then hides each one while the Buildings Editor is open.
        /// Called once from Activate() so state is always captured before anything is hidden.
        /// </summary>
        private void HideHUDs()
        {
            // Spell Bar HUD — SingletonMonoBehaviour in Valkur.Gameplay.UI
            var spellBar = Valkur.Gameplay.UI.SpellBarHUD.HasInstance
                ? Valkur.Gameplay.UI.SpellBarHUD.Instance
                : null;
            if (spellBar != null)
            {
                _hudSpellBarWasActive = spellBar.gameObject.activeSelf;
                if (_hudSpellBarWasActive) spellBar.gameObject.SetActive(false);
            }
            else
            {
                _hudSpellBarWasActive = false;
            }

            // Inventory UI — SingletonMonoBehaviour in Valkur.Gameplay.Inventory
            var inv = Valkur.Gameplay.Inventory.InventoryUI.HasInstance
                ? Valkur.Gameplay.Inventory.InventoryUI.Instance
                : null;
            if (inv != null)
            {
                _hudInventoryWasActive = inv.gameObject.activeSelf;
                if (_hudInventoryWasActive) inv.gameObject.SetActive(false);
            }
            else
            {
                _hudInventoryWasActive = false;
            }

            // Music Player HUD — lives in Valkur.UI (no compile-time reference allowed from Gameplay).
            // Locate once by the fixed GameObject name assigned in HUDBootstrap, then cache.
            // Re-search if the cached reference was destroyed (scene reload with Domain Reload OFF).
            if (_hudMusicPlayerGo == null || !_hudMusicPlayerGo)
                _hudMusicPlayerGo = GameObject.Find("MusicPlayerHUD");
            if (_hudMusicPlayerGo != null)
            {
                _hudMusicPlayerWasActive = _hudMusicPlayerGo.activeSelf;
                if (_hudMusicPlayerWasActive) _hudMusicPlayerGo.SetActive(false);
            }
            else
            {
                _hudMusicPlayerWasActive = false;
            }
        }

        /// <summary>
        /// Restores each HUD to the active-state it had when HideHUDs() was called.
        /// Called once from Deactivate() to preserve the player's HUD layout.
        /// </summary>
        private void RestoreHUDs()
        {
            if (_hudSpellBarWasActive)
            {
                var spellBar = Valkur.Gameplay.UI.SpellBarHUD.HasInstance
                    ? Valkur.Gameplay.UI.SpellBarHUD.Instance
                    : null;
                if (spellBar != null) spellBar.gameObject.SetActive(true);
            }

            if (_hudInventoryWasActive)
            {
                var inv = Valkur.Gameplay.Inventory.InventoryUI.HasInstance
                    ? Valkur.Gameplay.Inventory.InventoryUI.Instance
                    : null;
                if (inv != null) inv.gameObject.SetActive(true);
            }

            if (_hudMusicPlayerWasActive && _hudMusicPlayerGo != null)
                _hudMusicPlayerGo.SetActive(true);
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        //  UI BUILD
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    }
}
