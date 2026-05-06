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

        private void ToggleCollidersVisible()
        {
            _collidersVisible = !_collidersVisible;
            if (_collidersVisible)
            {
                // The overlay is a purely VISUAL layer. The physical BoxCollider2D
                // children are already in their authoritative state from
                //   • BuildingCollisionLoader.TryApplyGrid (scene boot)
                //   • HandleColliderPaint / ApplyGridSnapshot (live edits + undo)
                //   • RefreshCollisionFor (Picker / MapInteraction structural changes)
                // so toggling visibility must NEVER walk every building and rebuild
                // its colliders again. We only ensure the authoring stores are
                // loaded (cheap one-shot JSON read) so the overlay can resolve
                // cells, then start the progressive build coroutine — which
                // spreads the GameObject creation over multiple frames so the
                // editor stays responsive even with ~150 buildings on screen.
                EnsureColliderDataLoaded();
                if (_logDiagOnShow) LogColliderDiagnostics();
                StartProgressiveShowOverlay();
            }
            else
            {
                // Hide is cheap: just set visibility false on every existing
                // overlay. No GameObject creation, no per-cell math.
                StopProgressiveShowOverlay();
                SetTilemapCollidersVisible(false);
                RefreshCollidersOverlay();
            }
            if (_uiRefs.CollVisibilityBtnLabel != null)
                _uiRefs.CollVisibilityBtnLabel.text = _collidersVisible ? "Hide Colliders" : "Show Colliders";
            RefreshCollidersPanel();
            // The "Colliders visible (N shapes)." toast is emitted by the
            // progressive coroutine when it finishes; here we only handle Hide.
            if (!_collidersVisible) Toast("Colliders hidden.");
        }

        /// <summary>
        /// Print a one-shot diagnostic snapshot of every BuildingObject's
        /// physical collider state (root collider + CollTile children) so we
        /// can verify in the Console exactly what the physics engine sees:
        /// per-tile world position, world size, layer, isTrigger flag. If the
        /// player walks through a "wall", the offending row will look wrong
        /// here (wrong layer, isTrigger=true, zero size, far-away position…).
        /// </summary>
        private void LogColliderDiagnostics()
        {
            int worldLayer = LayerMask.NameToLayer("World");
            var all = FindObjectsOfType<BuildingObject>();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[BuildingsEditor] Show Colliders → diagnostics for {all.Length} buildings " +
                          $"(expected layer 'World' = {worldLayer}):");
            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (b == null) continue;
                int tiles = 0, mismatched = 0, triggers = 0;
                var boxes = b.GetComponentsInChildren<BoxCollider2D>(includeInactive: false);
                BoxCollider2D first = null;
                for (int j = 0; j < boxes.Length; j++)
                {
                    var box = boxes[j];
                    if (!box.enabled) continue;
                    if (box.transform.name.StartsWith("_ColliderDebug_", StringComparison.Ordinal)) continue;
                    tiles++;
                    if (first == null) first = box;
                    if (box.gameObject.layer != worldLayer && worldLayer >= 0) mismatched++;
                    if (box.isTrigger) triggers++;
                }
                string firstInfo = first != null
                    ? $" first={first.name} center={first.bounds.center} size={first.bounds.size} layer={LayerMask.LayerToName(first.gameObject.layer)} trigger={first.isTrigger}"
                    : " (no enabled colliders)";
                sb.AppendLine($"  • {b.name} (id={b.InstanceId}) → {tiles} active colliders, " +
                              $"{mismatched} on wrong layer, {triggers} triggers." + firstInfo);
            }
            Debug.Log(sb.ToString(), this);
        }

        private bool BrushOn => _collBrushMode != CollBrushMode.Off;

        /// <summary>
        /// Add or remove <see cref="TilemapColliderDebugOverlay"/> on every
        /// <see cref="CompositeCollider2D"/> that is backed by a <see cref="UnityEngine.Tilemaps.TilemapCollider2D"/>.
        /// Called alongside building-collider visibility changes so the user sees
        /// a single unified "Show Colliders" view covering both building BoxCollider2Ds
        /// and tile-layer composite paths.
        /// </summary>
        private static void SetTilemapCollidersVisible(bool visible)
        {
            var composites = FindObjectsOfType<CompositeCollider2D>();
            foreach (var cc in composites)
            {
                // Only decorate composites that are driven by a TilemapCollider2D —
                // skip physics-only CompositeCollider2Ds on regular rigidbodies.
                if (cc.GetComponent<UnityEngine.Tilemaps.TilemapCollider2D>() == null) continue;

                var overlay = cc.GetComponent<TilemapColliderDebugOverlay>();
                if (overlay == null && visible)
                    overlay = cc.gameObject.AddComponent<TilemapColliderDebugOverlay>();
                if (overlay != null)
                    overlay.SetVisible(visible);
            }
        }

        private void SetBrushOn(bool on)
        {
            if (on)
            {
                // Resume the last selected action; default to Paint if none.
                if (_lastBrushAction != CollBrushMode.Solid && _lastBrushAction != CollBrushMode.Walk)
                    _lastBrushAction = CollBrushMode.Solid;
                SetCollBrushMode(_lastBrushAction);
            }
            else
            {
                SetCollBrushMode(CollBrushMode.Off);
            }
        }

        private void SetBrushAction(CollBrushMode action)
        {
            // Only Paint (Solid → "#") and Erase (Walk → ".") are valid actions.
            if (action != CollBrushMode.Solid && action != CollBrushMode.Walk) return;
            // Clicking the already-active action toggles the brush OFF.
            if (BrushOn && _collBrushMode == action)
            {
                SetCollBrushMode(CollBrushMode.Off);
                return;
            }
            _lastBrushAction = action;
            SetCollBrushMode(action);
        }

        private static string ActionLabel(CollBrushMode action)
            => action == CollBrushMode.Solid ? "# Paint"
             : action == CollBrushMode.Walk  ? ". Erase"
             : action.ToString();

        private void SetCollBrushMode(CollBrushMode mode)
        {
            _collBrushMode = mode;
            if (mode == CollBrushMode.Solid || mode == CollBrushMode.Walk)
                _lastBrushAction = mode;
            RefreshBrushButtonHighlights();
            if (mode != CollBrushMode.Off && !_collidersVisible)
            {
                _collidersVisible = true;
                if (_uiRefs.CollVisibilityBtnLabel != null)
                    _uiRefs.CollVisibilityBtnLabel.text = "Hide Colliders";
                // Visual-only path: load authoring data if needed, then start
                // the progressive overlay build. Physical colliders are NOT
                // rebuilt — same reasoning as ToggleCollidersVisible above.
                EnsureColliderDataLoaded();
                StartProgressiveShowOverlay();
            }
            if (_uiRefs.CollBrushToggleLabel != null)
                _uiRefs.CollBrushToggleLabel.text = BrushOn
                    ? $"Brush: ON ({ActionLabel(_lastBrushAction)})"
                    : "Brush: OFF";
            RefreshCollidersPanel();
            Toast(BrushOn ? $"Brush ON ({ActionLabel(_collBrushMode)})." : "Brush OFF.");
        }

        private void OnCollBrushSizeChanged(int v)
        {
            _collBrushSize = Mathf.Clamp(v, 1, 8);
            RefreshCollBrushSizePresets();
            RefreshCollidersPanel();
        }

        private void RefreshCollBrushSizePresets()
        {
            if (_uiRefs.CollBrushSizePresetImgs == null) return;
            for (int i = 0; i < _uiRefs.CollBrushSizePresetImgs.Count; i++)
            {
                int size   = i + 1;
                bool active = size == _collBrushSize;
                if (_uiRefs.CollBrushSizePresetImgs[i] != null)
                    _uiRefs.CollBrushSizePresetImgs[i].color =
                        active ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
                if (_uiRefs.CollBrushSizePresetLabels != null
                    && i < _uiRefs.CollBrushSizePresetLabels.Count
                    && _uiRefs.CollBrushSizePresetLabels[i] != null)
                    _uiRefs.CollBrushSizePresetLabels[i].color =
                        active ? EditorUIHelpers.ACCENT : EditorUIHelpers.TEXT_SECONDARY;
            }
            if (_uiRefs.CollBrushSizeLabel != null)
                _uiRefs.CollBrushSizeLabel.text = $"{_collBrushSize}x{_collBrushSize}";
        }

        private void RefreshBrushButtonHighlights()
        {
            // Brush ON/OFF toggle highlight.
            ApplyBrushBtnStyle(_uiRefs.CollBrushToggleImg, BrushOn);
            // Action highlight: highlight the action that would apply on next click.
            // When brush is OFF, still indicate the remembered action so the user knows
            // what will activate when they press B.
            CollBrushMode shownAction = BrushOn ? _collBrushMode : _lastBrushAction;
            ApplyBrushBtnStyle(_uiRefs.CollPaintBtnImg, shownAction == CollBrushMode.Solid);
            ApplyBrushBtnStyle(_uiRefs.CollEraseBtnImg, shownAction == CollBrushMode.Walk);
        }

        private static void ApplyBrushBtnStyle(Image img, bool selected)
        {
            if (img == null) return;
            img.color = selected ? new Color(0.20f, 0.55f, 0.85f, 1f)
                                 : new Color(0.18f, 0.18f, 0.20f, 1f);
        }

        private void RefreshCollidersPanel()
        {
            // Update scope button label whenever we refresh.
            if (_uiRefs.CollScopeBtnLabel != null)
            {
                string scopeNow = _activeBuilding != null
                    ? _activeBuilding.EffectiveColliderScope
                    : "--";
                string scopeDesc = scopeNow == "CU" ? "this only"
                                 : scopeNow == "CG" ? "all of type"
                                 : "no selection";
                _uiRefs.CollScopeBtnLabel.text = $"Scope: {scopeNow} ({scopeDesc})";
            }

            if (_uiRefs.CollTargetText == null || _uiRefs.CollStateText == null) return;

            string brushLabel = BrushOn ? $"ON {ActionLabel(_collBrushMode)}" : "OFF";

            if (_activeBuilding == null || _activeBuilding.Template == null)
            {
                _uiRefs.CollTargetText.text = "No building selected.";
                _uiRefs.CollStateText.text  = $"Grid: -- | Brush {brushLabel} x{_collBrushSize}";
                return;
            }

            EnsureColliderDataLoaded();
            var session = EnsureActiveColliderSession();
            if (session == null || session.WorkingGrid == null)
            {
                _uiRefs.CollTargetText.text = $"ID {_activeBuilding.InstanceId} | Scope {_activeBuilding.EffectiveColliderScope}";
                _uiRefs.CollStateText.text  = $"Grid: -- | Brush {brushLabel} x{_collBrushSize}";
                return;
            }

            string scope = session.Scope == ColliderAuthoringScope.CU ? "CU" : "CG";
            string target = session.Scope == ColliderAuthoringScope.CU
                ? $"instance:{session.InstanceId}"
                : string.IsNullOrEmpty(session.ImageKey) ? "image:(none)" : $"image:{session.ImageKey}";
            string dirty = IsSessionDirty(session) ? "Dirty" : "Saved";
            int solids = CountSolidCells(session.WorkingGrid);
            _uiRefs.CollTargetText.text = $"ID {session.InstanceId} | Scope {scope}\n{target}";
            _uiRefs.CollStateText.text =
                $"Grid: {session.WorkingGrid.width}x{session.WorkingGrid.height} | Solids {solids} | {dirty} | Brush {brushLabel} x{_collBrushSize}";
        }

        // Reusable scratch buffer for authoring-cell computation. Authoring cell
        // sets are pushed to overlays via IList<Rect>; the overlay copies the
        // contents into its own array, so we can safely reuse this list across
        // every building/frame and avoid the per-call List<Rect>(256) allocation.
        private readonly List<Rect> _authoringCellsScratch = new List<Rect>(256);

        // Cached BuildingObject snapshot used by full-refresh paths. Invalidated
        // by InvalidateBuildingCache() whenever the editor knows the set may
        // have changed (placement, deletion, undo/redo, scene reload). Avoids
        // repeated FindObjectsOfType allocations.
    }
}