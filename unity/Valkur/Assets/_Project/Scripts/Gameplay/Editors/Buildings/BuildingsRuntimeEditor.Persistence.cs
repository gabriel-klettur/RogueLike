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
        private void MarkInstanceDataDirty()
        {
            _hasUnsavedInstanceChanges = true;
        }

        private void PersistDirtyInstanceChanges(string reason = null, bool force = false)
        {
            if ((!_hasUnsavedInstanceChanges && !force) || _isPersistingInstanceChanges)
                return;

            SaveInstancesToJson();
        }

        private void ExecutePersistedEdit(string label, Action doAction, Action undoAction)
        {
            _undo.Do(label,
                () =>
                {
                    doAction?.Invoke();
                    MarkInstanceDataDirty();
                    PersistDirtyInstanceChanges(label, force: true);
                },
                () =>
                {
                    undoAction?.Invoke();
                    MarkInstanceDataDirty();
                    PersistDirtyInstanceChanges($"Undo {label}", force: true);
                });
        }

        private void SaveInstancesToJson()
        {
            if (_isPersistingInstanceChanges) return;

            // Map-slot aware path resolution. The default slot keeps the legacy
            // StreamingAssets/Buildings/ location so existing builds + the
            // BuildingsDataGuard backup pipeline continue to work; custom slots
            // route to persistentDataPath/Maps/<slot>/Buildings/ so editing one
            // map can never silently overwrite another. See MapEditorActiveSlot.
            string activeSlot = Valkur.Core.MapEditorActiveSlot.Read();
            string dir  = Valkur.Core.MapEditorActiveSlot.BuildingsDir(activeSlot);
            string path = Path.Combine(dir, "buildings_instances.json");
            _isPersistingInstanceChanges = true;
            try
            {
                EnsureColliderDataLoaded();
                if (_activeColliderSession != null && _activeColliderSession.WorkingGrid != null)
                    PersistSessionToStore(_activeColliderSession);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var sb = new StringBuilder();
                sb.AppendLine("[");
                var zm = FindObjectOfType<ZoneManager>();
                int zH = zm != null ? zm.ZoneHeightTiles : 0;

                var all = FindObjectsOfType<BuildingObject>()
                    .Where(b => b != null && b.gameObject.activeInHierarchy && b.Template != null)
                    .OrderBy(b => b.InstanceId).ToList();

                int nextId = 1;
                for (int i = 0; i < all.Count; i++)
                {
                    var b = all[i];
                    int oldInstanceId = b.InstanceId;
                    RemapColliderInstanceStore(oldInstanceId, nextId);
                    b.InstanceId = nextId++;
                    int relX = 0, relY = 0;
                    string zone = b.ZoneName ?? "Lobby";
                    if (zm != null && zm.TryGetZone(zone, out var zd))
                    {
                        int effW = (b.ScaleOverride.x > 0) ? b.ScaleOverride.x : b.Template.originalScale.x;
                        int effH = (b.ScaleOverride.y > 0) ? b.ScaleOverride.y : b.Template.originalScale.y;
                        const float PPU = 32f;
                        float wx = b.transform.position.x;
                        float wy = b.transform.position.y;
                        relX = Mathf.RoundToInt((wx - zd.gridOffset.x) * PPU - effW * 0.5f);
                        relY = Mathf.RoundToInt((zd.gridOffset.y + (zH - 1) - wy) * PPU - effH);
                    }

                    sb.Append("  {");
                    sb.Append($"\"id\": {b.InstanceId}, ");
                    sb.Append($"\"template_id\": {b.Template.templateId}, ");
                    sb.Append($"\"zone\": \"{EscapeJson(zone)}\", ");
                    sb.Append($"\"rel_x\": {relX}, ");
                    sb.Append($"\"rel_y\": {relY}");

                    var sov = b.ScaleOverride;
                    bool hasCollisionOverride = _colliderInstanceStore.TryGetValue(b.InstanceId, out var instanceGrid);
                    bool writeCollisionOverride = hasCollisionOverride &&
                        string.Equals(b.EffectiveColliderScope, "CU", StringComparison.OrdinalIgnoreCase);
                    bool hasColliderScope = !string.IsNullOrEmpty(b.ColliderScopeOverride);
                    bool hasZBottomOverride = b.ZBottomOffset != 0;
                    bool hasZTopOverride = b.ZTopOffset != 0;
                    bool hasOv = b.SplitRatioOverride >= 0f || sov.x > 0 || sov.y > 0 || hasColliderScope || hasZBottomOverride || hasZTopOverride || writeCollisionOverride;
                    if (hasOv)
                    {
                        sb.Append(", \"overrides\": {");
                        bool first = true;
                        if (sov.x > 0 || sov.y > 0) { sb.Append($"\"scale\": [{sov.x}, {sov.y}]"); first = false; }
                        if (b.SplitRatioOverride >= 0f)
                        {
                            if (!first) sb.Append(", ");
                            sb.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                "\"split_ratio\": {0:F4}", b.SplitRatioOverride));
                            first = false;
                        }
                        if (hasColliderScope)
                        {
                            if (!first) sb.Append(", ");
                            sb.Append($"\"collider_scope\": \"{EscapeJson(b.ColliderScopeOverride)}\"");
                            first = false;
                        }
                        if (hasZBottomOverride)
                        {
                            if (!first) sb.Append(", ");
                            sb.Append($"\"z_bottom\": {b.ZBottomOffset}");
                            first = false;
                        }
                        if (hasZTopOverride)
                        {
                            if (!first) sb.Append(", ");
                            sb.Append($"\"z_top\": {b.ZTopOffset}");
                            first = false;
                        }
                        if (writeCollisionOverride && instanceGrid != null)
                        {
                            if (!first) sb.Append(", ");
                            sb.Append("\"collision_override\": ");
                            AppendGridJson(sb, instanceGrid, 0);
                        }
                        sb.Append("}");
                    }
                    sb.Append("}");
                    if (i < all.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }
                sb.AppendLine("]");

                File.WriteAllText(path, sb.ToString());
                PruneColliderInstanceStore(all);
                WriteColliderStoresToDisk(dir);
#if UNITY_EDITOR
                // Refresh the backup copy via reflection so we don't create a
                // runtime→editor assembly dependency. BuildingsDataGuard.RefreshBackup()
                // lives in Valkur.Editor (Editor-only assembly). Only meaningful
                // for the default slot — its target file IS the StreamingAssets
                // baseline that BuildingsDataGuard protects. Custom slots live
                // under persistentDataPath and aren't part of the guarded asset
                // graph.
                if (Application.isPlaying && Valkur.Core.MapEditorActiveSlot.IsDefault(activeSlot))
                {
                    UnityEditor.EditorApplication.delayCall += () =>
                    {
                        var t = System.Type.GetType(
                            "Valkur.Editor.BuildingsDataGuard, Valkur.Editor");
                        t?.GetMethod("RefreshBackup",
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.Static)
                         ?.Invoke(null, null);
                    };
                }
#endif
                string slotLabel = Valkur.Core.MapEditorActiveSlot.IsDefault(activeSlot)
                    ? INSTANCES_REL_PATH
                    : $"slot '{activeSlot}' ({path})";
                if (_statusTmp != null) _statusTmp.text = $"Saved {all.Count} buildings → {slotLabel}";
                Debug.Log($"[BuildingsEditor] Saved {all.Count} buildings to {path} (slot={activeSlot})");
                _hasUnsavedInstanceChanges = false;
                RefreshCollidersPanel();
            }
            catch (System.Exception ex)
            {
                _hasUnsavedInstanceChanges = true;
                Debug.LogError($"[BuildingsEditor] Save failed: {ex.Message}\n{ex.StackTrace}");
                if (_statusTmp != null) _statusTmp.text = "Save FAILED — see console.";
            }
            finally
            {
                _isPersistingInstanceChanges = false;
            }
        }
        private const string INSTANCES_REL_PATH = "StreamingAssets/Buildings/buildings_instances.json";

        private void ReloadFromJson()
        {
            CacheBuildingLoader();
            if (_buildingLoader == null) { Toast("BuildingLoader not found in scene."); return; }
            ResetColliderAuthoringState();
            _buildingLoader.LoadBuildings();
            _undo.Clear();
            _activeBuilding = null;
            _hoveredBuilding = null;
            InvalidateBuildingCache();
            ApplyBuildingsVisibility();
            RefreshInspector();
            if (_statusTmp != null) _statusTmp.text = "Reloaded from JSON.";
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  CONFIRM MODAL
        // ──────────────────────────────────────────────────────────────────────────

        private void ShowConfirm(string text, System.Action onYes)
        {
            if (_confirmModal == null) { onYes?.Invoke(); return; }
            _confirmText.text = text;
            _pendingConfirmYes = onYes;
            _confirmModal.SetActive(true);
            _confirmModal.transform.SetAsLastSibling();
        }

        private void HideConfirm()
        {
            _pendingConfirmYes = null;
            if (_confirmModal != null) _confirmModal.SetActive(false);
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  TUTORIAL
        // ──────────────────────────────────────────────────────────────────────────

        private void ToggleTutorial()
        {
            if (_tutorialRoot == null) return;
            bool show = !_tutorialRoot.activeSelf;
            _tutorialRoot.SetActive(show);
            if (show) { _tutorialRoot.transform.SetAsLastSibling(); RefreshTutorial(); }
        }

        private void StepTutorial(int delta)
        {
            _tutorialStep = (_tutorialStep + delta + TUTORIAL_STEPS.Length) % TUTORIAL_STEPS.Length;
            RefreshTutorial();
        }

        private void RefreshTutorial()
        {
            if (_tutorialStepLabel == null) return;
            var (title, body) = TUTORIAL_STEPS[_tutorialStep];
            _tutorialStepLabel.text = $"{title}   ({_tutorialStep + 1}/{TUTORIAL_STEPS.Length})";
            _tutorialBodyTmp.text = body;
        }

        private void ToggleBuildingsVisible()
        {
            _buildingsVisible = !_buildingsVisible;
            ApplyBuildingsVisibility();
            RefreshBuildingsVisibilityButton();

            if (!_buildingsVisible)
            {
                _hoveredBuilding = null;
                _hoverStack.Clear();
            }

            if (_statusTmp != null)
                _statusTmp.text = _buildingsVisible ? "Buildings visible." : "Buildings hidden.";
        }

        private void ApplyBuildingsVisibility()
        {
            var all = GetCachedBuildings();
            for (int i = 0; i < all.Length; i++)
            {
                var building = all[i];
                if (building == null) continue;

                var renderers = building.GetComponentsInChildren<SpriteRenderer>(true);
                for (int j = 0; j < renderers.Length; j++)
                {
                    if (renderers[j] != null)
                        renderers[j].enabled = _buildingsVisible;
                }
            }

            if (!_buildingsVisible)
                HideOutlines();
        }

        private void RefreshBuildingsVisibilityButton()
        {
            BuildingsEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.BuildingVisibilityMenuBtnImg,
                _uiRefs.BuildingVisibilityMenuBtnTmp,
                _buildingsVisible);
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  PER-FRAME OVERLAY UPDATES (outlines + handles + ID label)
        // ──────────────────────────────────────────────────────────────────────────

        private void UpdateOutlineState()
        {
            if (_hoverFx == null || _activeFx == null) return;
            if (!_buildingsVisible)
            {
                HideOutlines();
                return;
            }

            // Hover (skip if same as active to avoid double-drawing)
            if (_hoveredBuilding != null && _hoveredBuilding != _activeBuilding)
            {
                bool red = _removeMode || _mode == EditorMode.Delete;
                _hoverFx.Configure(
                    color:        red ? HOVER_REMOVE_RED : HOVER_CYAN,
                    thicknessWorld: red ? HOVER_THICKNESS_WORLD * 1.5f : HOVER_THICKNESS_WORLD,
                    drawFill:     red,
                    fillColor:    HOVER_REMOVE_FILL);
                _hoverFx.Follow(_hoveredBuilding);
            }
            else
            {
                _hoverFx.Follow(null); _hoverFx.SetVisible(false);
            }

            // Active
            if (_activeBuilding != null) _activeFx.Follow(_activeBuilding);
            else { _activeFx.Follow(null); _activeFx.SetVisible(false); }
        }

        private void UpdateFloatingHandles()
        {
            if (_handlesRoot == null) return;
            if (!_buildingsVisible)
            {
                _handlesRoot.SetActive(false);
                return;
            }
            bool show = _activeBuilding != null && !_removeMode;
            _handlesRoot.SetActive(show);
            if (!show) return;

            if (!_activeBuilding.TryGetWorldRect(out var rect)) { _handlesRoot.SetActive(false); return; }
            var cam = Camera.main;
            if (cam == null) return;

            // Project building top-right corner to canvas (pivot=top-right → badge sits inside frame)
            Vector3 worldTopRight = new Vector3(rect.xMax, rect.yMax, 0f);
            Vector3 screenTR      = cam.WorldToScreenPoint(worldTopRight);
            Vector2 canvasTR      = ScreenToCanvasPos(screenTR);

            // Compute proportional badge size from the building's canvas-space width
            Vector3 worldTopLeft = new Vector3(rect.xMin, rect.yMax, 0f);
            Vector3 screenTL     = cam.WorldToScreenPoint(worldTopLeft);
            Vector2 canvasTL     = ScreenToCanvasPos(screenTL);
            float canvasW        = Mathf.Abs(canvasTR.x - canvasTL.x);
            float handleSize     = Mathf.Clamp(canvasW * 0.20f, 20f, 52f);

            var rt = _handlesRoot.GetComponent<RectTransform>();
            rt.sizeDelta        = new Vector2(handleSize, handleSize);
            rt.anchoredPosition = canvasTR;
        }
    }
}
