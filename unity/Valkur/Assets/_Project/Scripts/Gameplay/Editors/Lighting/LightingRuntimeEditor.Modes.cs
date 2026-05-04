using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Valkur.Core.Input;
using Valkur.UIKit;

namespace Valkur.Gameplay.World
{
    /// <summary>
    /// Lighting Editor — modes (Select / Spawn / Delete) and world cursor
    /// interaction. Mirrors the click-vs-drag UX of Items / Buildings so the
    /// player learns one gesture across every editor.
    /// </summary>
    public partial class LightingRuntimeEditor
    {
        // ── Mode handling ────────────────────────────────────────────────────

        private void SetMode(EditorMode mode)
        {
            _mode = mode;
            ApplyMode();
        }

        private void ApplyMode()
        {
            LightingEditorUIBuilder.ApplyToolBtnStyle(_ui.SelectBtnImg, _mode == EditorMode.Select);
            LightingEditorUIBuilder.ApplyToolBtnStyle(_ui.SpawnBtnImg,  _mode == EditorMode.Spawn);
            LightingEditorUIBuilder.ApplyToolBtnStyle(_ui.DeleteBtnImg, _mode == EditorMode.Delete, danger: true);
            SetStatus(_mode switch
            {
                EditorMode.Select => "Select: click a light to focus it. Drag to move.",
                EditorMode.Spawn  => string.IsNullOrEmpty(_selectedPresetKey)
                    ? "Spawn: pick a preset from the Presets panel first."
                    : $"Spawn '{_selectedPresetKey}': LMB on map to place.",
                EditorMode.Delete => "Delete: click a light on the map to remove it.",
                _ => $"Mode: {_mode}"
            });
        }

        // ── World mouse handling ─────────────────────────────────────────────

        private const float HIT_RADIUS_WORLD = 0.6f;
        private const float LMB_DRAG_THRESHOLD_PX = 6f;

        private Vector2 _lmbPressScreenPos;
        private bool    _lmbPressOnHovered;
        private bool    _consumedLmbReleaseAsDrag;

        private void HandleMapInteraction()
        {
            if (Mouse.current == null) return;
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            Vector2 screenPos = MouseInputManager.GetScreenMousePosition();
            Vector3 sp = new Vector3(screenPos.x, screenPos.y, -_mainCamera.transform.position.z);
            Vector3 worldPos = _mainCamera.ScreenToWorldPoint(sp);
            worldPos.z = 0f;

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            // Hover refresh — only outside UI; clears the hover when the cursor
            // re-enters a panel so panel hovers do not leave a stale highlight.
            _hoveredLight = overUi ? null : (WorldLightLoader.Instance != null
                ? WorldLightLoader.Instance.FindNearestLight(worldPos, HIT_RADIUS_WORLD)
                : null);

            // ── Move (LMB drag) — owns the press → release window when started
            //    over a hovered light. Items / Buildings use the same UX.
            UpdateMoveDrag(worldPos, overUi);
            if (_moving) return;
            if (_consumedLmbReleaseAsDrag)
            {
                _consumedLmbReleaseAsDrag = false;
                return;
            }

            if (!MouseInputManager.WasLeftMouseButtonPressedThisFrame()) return;
            if (overUi) return;

            switch (_mode)
            {
                case EditorMode.Delete:
                    if (_hoveredLight != null) DeleteLight(_hoveredLight);
                    else SetStatus("No light under cursor.");
                    break;
                case EditorMode.Spawn:
                    if (_hoveredLight != null) FocusLight(_hoveredLight);
                    else SpawnAt(worldPos);
                    break;
                case EditorMode.Select:
                default:
                    if (_hoveredLight != null) FocusLight(_hoveredLight);
                    else { _selectedLight = null; RebuildInstancesList(); }
                    break;
            }
        }

        private void UpdateMoveDrag(Vector3 worldPos, bool overUi)
        {
            bool lmbDown    = MouseInputManager.WasLeftMouseButtonPressedThisFrame();
            bool lmbHeld    = MouseInputManager.IsLeftMouseButtonPressed();
            bool lmbRelease = MouseInputManager.WasLeftMouseButtonReleasedThisFrame();

            // Press: arm a pending drag iff the LMB lands on a hovered light,
            // outside UI, and we are NOT in Delete mode (which has destructive intent).
            if (lmbDown && !overUi && !_moving && _hoveredLight != null && _mode != EditorMode.Delete)
            {
                _lmbPressOnHovered  = true;
                _lmbPressScreenPos  = MouseInputManager.GetScreenMousePosition();
                _moveStartWorldPos  = _hoveredLight.transform.position;
            }

            // Hold: cross threshold → start moving, then follow the cursor.
            if (lmbHeld)
            {
                if (!_moving && _lmbPressOnHovered && _hoveredLight != null)
                {
                    Vector2 cur = MouseInputManager.GetScreenMousePosition();
                    if (Vector2.Distance(cur, _lmbPressScreenPos) > LMB_DRAG_THRESHOLD_PX)
                    {
                        _moving      = true;
                        _movingLight = _hoveredLight;
                        FocusLight(_movingLight);
                        SetStatus("Moving light… release LMB to drop, Esc to cancel.");
                    }
                }
                if (_moving && _movingLight != null && WorldLightLoader.Instance != null)
                {
                    WorldLightLoader.Instance.MoveLight(_movingLight, new Vector3(worldPos.x, worldPos.y,
                        _movingLight.transform.position.z));
                }
                return;
            }

            // Release: commit if a real drag happened.
            if (lmbRelease)
            {
                if (_moving) CommitMove();
                _lmbPressOnHovered = false;
            }
        }

        private void CommitMove()
        {
            var moved   = _movingLight;
            Vector3 to  = moved != null ? moved.transform.position : Vector3.zero;
            Vector3 from = _moveStartWorldPos;
            _moving      = false;
            _movingLight = null;
            _consumedLmbReleaseAsDrag = true;

            if (moved == null || WorldLightLoader.Instance == null) return;

            _undo.Record(new UndoStack.LambdaCommand(
                "Move light",
                doAction:   () =>
                {
                    if (moved != null) WorldLightLoader.Instance.MoveLight(moved, to);
                },
                undoAction: () =>
                {
                    if (moved != null) WorldLightLoader.Instance.MoveLight(moved, from);
                }));
            RebuildInstancesList();
            SetStatus($"Moved to ({to.x:F1}, {to.y:F1}). Save (Ctrl+S) to persist.");
        }

        private void CancelMove()
        {
            if (!_moving || _movingLight == null) { _moving = false; _movingLight = null; return; }
            if (WorldLightLoader.Instance != null)
                WorldLightLoader.Instance.MoveLight(_movingLight, _moveStartWorldPos);
            _moving      = false;
            _movingLight = null;
            SetStatus("Move cancelled.");
        }

        // ── Spawn / delete primitives ────────────────────────────────────────

        private void SpawnAt(Vector3 worldPos)
        {
            if (string.IsNullOrEmpty(_selectedPresetKey))
            {
                SetStatus("Pick a preset before spawning.");
                return;
            }
            if (WorldLightLoader.Instance == null)
            {
                SetStatus("WorldLightLoader missing — cannot spawn.");
                return;
            }
            string preset = _selectedPresetKey;
            var go = WorldLightLoader.Instance.RegisterRuntimeLight(preset, worldPos);
            if (go == null) { SetStatus($"Could not spawn '{preset}'."); return; }

            _undo.Record(new UndoStack.LambdaCommand(
                $"Spawn {preset}",
                doAction:   () =>
                {
                    if (go == null && WorldLightLoader.Instance != null)
                        WorldLightLoader.Instance.RegisterRuntimeLight(preset, worldPos);
                },
                undoAction: () =>
                {
                    if (go != null && WorldLightLoader.Instance != null)
                        WorldLightLoader.Instance.RemoveLight(go);
                }));
            FocusLight(go);
            RebuildInstancesList();
            SetStatus($"Spawned '{preset}' at ({worldPos.x:F1}, {worldPos.y:F1}).");
        }

        public void DeleteLight(GameObject lightGo)
        {
            if (lightGo == null || WorldLightLoader.Instance == null) return;
            string label  = lightGo.name;
            Vector3 pos   = lightGo.transform.position;
            string preset = ExtractPresetFromName(lightGo.name);

            _undo.Record(new UndoStack.LambdaCommand(
                $"Delete {label}",
                doAction:   () => { /* the destruction below is the do-side */ },
                undoAction: () =>
                {
                    if (WorldLightLoader.Instance != null && !string.IsNullOrEmpty(preset))
                        WorldLightLoader.Instance.RegisterRuntimeLight(preset, pos);
                    RebuildInstancesList();
                }));
            WorldLightLoader.Instance.RemoveLight(lightGo);
            if (_selectedLight == lightGo) _selectedLight = null;
            RebuildInstancesList();
            SetStatus($"Deleted '{label}'. Save (Ctrl+S) to persist.");
        }

        private void FocusLight(GameObject lightGo)
        {
            _selectedLight = lightGo;
            RebuildInstancesList();
            RefreshPresetProperties(); // surfaces the live light's preset details
        }

        // Recovers presetKey from the WorldLightLoader naming convention "Light_{id}_{presetKey}".
        private static string ExtractPresetFromName(string goName)
        {
            if (string.IsNullOrEmpty(goName)) return null;
            int firstUnderscore  = goName.IndexOf('_');
            if (firstUnderscore < 0) return null;
            int secondUnderscore = goName.IndexOf('_', firstUnderscore + 1);
            if (secondUnderscore < 0 || secondUnderscore + 1 >= goName.Length) return null;
            return goName.Substring(secondUnderscore + 1);
        }
    }
}
