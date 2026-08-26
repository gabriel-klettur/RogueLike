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

        private Vector2    _lmbPressScreenPos;
        private bool       _consumedLmbReleaseAsDrag;

        /// <summary>
        /// The light the press landed on, and the anchor its undo will restore.
        ///
        /// These belong together. The old code latched a bool and an anchor position on press,
        /// then picked the light to drag from whatever was hovered when the drag threshold was
        /// crossed — so a press on light A followed by a small movement onto light B dragged B
        /// while the undo remembered A's position, and undoing sent B to where A used to be.
        /// </summary>
        private GameObject _lmbPressedLight;

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

            // Press: arm a pending drag iff the LMB lands on a hovered light, outside UI, and we
            // are NOT in Delete mode (which has destructive intent). Remember WHICH light, not
            // merely that there was one.
            if (lmbDown && !overUi && !_moving && _hoveredLight != null && _mode != EditorMode.Delete)
            {
                // A derived light follows its building. Dragging one moves it until the next
                // load and saves nothing, so refuse at the press rather than after the drag.
                if (WorldLightLoader.Instance != null &&
                    WorldLightLoader.Instance.CaptureLight(_hoveredLight) == null)
                {
                    SetStatus($"'{_hoveredLight.name}' belongs to a building — move the building instead.");
                }
                else
                {
                    _lmbPressedLight   = _hoveredLight;
                    _lmbPressScreenPos = MouseInputManager.GetScreenMousePosition();
                    _moveStartWorldPos = _hoveredLight.transform.position;
                }
            }

            // Hold: cross threshold → start moving, then follow the cursor.
            if (lmbHeld)
            {
                if (!_moving && _lmbPressedLight != null)
                {
                    Vector2 cur = MouseInputManager.GetScreenMousePosition();
                    if (Vector2.Distance(cur, _lmbPressScreenPos) > LMB_DRAG_THRESHOLD_PX)
                    {
                        // The light that was PRESSED, not whatever the cursor has since slid over.
                        _moving      = true;
                        _movingLight = _lmbPressedLight;
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
                _lmbPressedLight = null;
            }
        }

        /// <summary>
        /// Forget any armed-but-not-yet-started drag.
        ///
        /// Clearing <c>_moving</c> alone is not enough while the button is still down: the next
        /// frame sees a live latch and re-arms, so Esc did not cancel a drag so much as hand it to
        /// whichever light the cursor had reached — carrying the FIRST light's undo anchor with
        /// it. Deactivate has the same problem across sessions.
        /// </summary>
        private void ClearDragLatch()
        {
            _lmbPressedLight          = null;
            _consumedLmbReleaseAsDrag = false;
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

            // Address the light by its stable id, never by the captured GameObject. A captured
            // reference dies the moment some other undo deletes and re-creates that light, and a
            // dead reference makes the command a silent no-op that still moves between the undo
            // and redo stacks — so the stacks go on claiming edits the world never saw.
            var snapshot = WorldLightLoader.Instance.CaptureLight(moved);
            if (snapshot == null) return;   // derived light, or not ours: nothing to record
            int id = snapshot.Id;

            _undo.Record(new UndoStack.LambdaCommand(
                "Move light",
                doAction:   () => MoveById(id, to),
                undoAction: () => MoveById(id, from)));
            RebuildInstancesList();
            SetStatus($"Moved to ({to.x:F1}, {to.y:F1}). Save (Ctrl+S) to persist.");
        }

        /// <summary>Re-resolve the light at command time and move it, or say why it could not.</summary>
        private void MoveById(int id, Vector3 target)
        {
            var loader = WorldLightLoader.Instance;
            var go     = loader != null ? loader.FindLightById(id) : null;
            if (go == null)
            {
                Debug.LogWarning($"[LightingEditor] Undo/redo of a move could not find light id={id}.");
                return;
            }
            loader.MoveLight(go, target);
            RebuildInstancesList();
        }

        private void CancelMove()
        {
            if (!_moving || _movingLight == null) { _moving = false; _movingLight = null; return; }
            if (WorldLightLoader.Instance != null)
                WorldLightLoader.Instance.MoveLight(_movingLight, _moveStartWorldPos);
            _moving      = false;
            _movingLight = null;
            ClearDragLatch();
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

            // Snapshot the light we just made, so a redo re-creates THAT light — same id, same
            // overrides — rather than a fresh one that merely resembles it.
            var snapshot = WorldLightLoader.Instance.CaptureLight(go);

            _undo.Record(new UndoStack.LambdaCommand(
                $"Spawn {preset}",
                doAction:   () => RestoreSnapshot(snapshot),
                undoAction: () => RemoveById(snapshot != null ? snapshot.Id : 0)));
            FocusLight(go);
            RebuildInstancesList();
            SetStatus($"Spawned '{preset}' at ({worldPos.x:F1}, {worldPos.y:F1}).");
        }

        public void DeleteLight(GameObject lightGo)
        {
            if (lightGo == null || WorldLightLoader.Instance == null) return;
            string label = lightGo.name;

            // Capture BEFORE destroying. The preset key alone is not enough to bring a light
            // back: it says what family the light belongs to, not what it was. Undo used to
            // re-register from a key parsed out of the GameObject's NAME, which minted a new id
            // and dropped every per-instance override — measured live, id=1 with an authored
            // colour came back as id=15 with none.
            var snapshot = WorldLightLoader.Instance.CaptureLight(lightGo);
            if (snapshot == null)
            {
                // A derived light belongs to its building, not to the light file. Deleting it
                // here would only make it come back on the next load.
                SetStatus($"'{label}' comes from a building — delete the building instead.");
                return;
            }

            _undo.Record(new UndoStack.LambdaCommand(
                $"Delete {label}",
                doAction:   () => RemoveById(snapshot.Id),
                undoAction: () => RestoreSnapshot(snapshot)));
            WorldLightLoader.Instance.RemoveLight(lightGo);
            if (_selectedLight == lightGo) _selectedLight = null;
            RebuildInstancesList();
            SetStatus($"Deleted '{label}'. Save (Ctrl+S) to persist.");
        }

        /// <summary>Re-create a captured light and refresh the panel.</summary>
        private void RestoreSnapshot(WorldLightLoader.LightSnapshot snapshot)
        {
            var loader = WorldLightLoader.Instance;
            if (loader == null || snapshot == null) return;
            var go = loader.RestoreLight(snapshot);
            if (go == null)
                Debug.LogWarning($"[LightingEditor] Could not restore light id={snapshot.Id} " +
                                 $"(preset '{snapshot.PresetId}').");
            RebuildInstancesList();
        }

        /// <summary>Destroy the light with this id, if it is still there.</summary>
        private void RemoveById(int id)
        {
            var loader = WorldLightLoader.Instance;
            var go     = loader != null ? loader.FindLightById(id) : null;
            if (go == null)
            {
                // Say so. A command that runs, finds nothing and returns normally still moves
                // between the undo and redo stacks, so the history goes on claiming a step the
                // world never took — the same silence UndoStack.ReportFailure exists to break,
                // and one its try/catch cannot see because nothing threw.
                Debug.LogWarning($"[LightingEditor] Undo/redo of a delete could not find light id={id}.");
                return;
            }
            loader.RemoveLight(go);
            if (_selectedLight == go) _selectedLight = null;
            RebuildInstancesList();
        }

        private void FocusLight(GameObject lightGo)
        {
            _selectedLight = lightGo;
            RebuildInstancesList();
            RefreshPresetProperties(); // surfaces the live light's preset details
        }

        // Recovers presetKey from the WorldLightLoader naming convention "Light_{id}_{presetKey}".
    }
}
