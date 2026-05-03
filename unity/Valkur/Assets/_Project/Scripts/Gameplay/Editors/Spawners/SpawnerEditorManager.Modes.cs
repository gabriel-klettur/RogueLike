using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.UIKit;

namespace Valkur.Gameplay.Spawners
{
    /// <summary>
    /// SpawnerEditor — map interaction (Select/Place/Delete + drag) and
    /// JSON persistence of placed instances.
    /// </summary>
    public partial class SpawnerEditorManager
    {
        private const float SELECTION_RADIUS_WORLD   = 1.5f;
        // Hit radius used for the Alt-toggle hover and centre-click shortcut.
        // Sized large enough that clicking near the visible centre marker is
        // forgiving on any zoom level, but tight enough that Place mode still
        // wins for clicks on empty tiles around the spawner. Kept in sync with
        // <see cref="HoverHelpStatus"/> for the cursor affordance.
        internal const float CENTER_HIT_RADIUS_WORLD = 0.55f;
        private const string STREAMING_SUBFOLDER     = "Spawners";
        private const string INSTANCES_FILENAME      = "spawners_instances.json";

        // ── Map interaction (called every Update while active) ───────────────────

        private void HandleMapInteraction()
        {
            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null) return;
            }

            Vector2 screen = MouseInputManager.GetScreenMousePosition();
            Vector3 world  = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
            world.z = 0f;

            // An active RMB drag must follow the cursor every frame even if it
            // briefly travels over a panel — releasing RMB anywhere commits the
            // move. Mirrors the Buildings / Entities pattern.
            if (_dragging && _selectedInstance != null)
            {
                _selectedInstance.transform.position = world + _dragOffset;
                if (MouseInputManager.WasRightMouseButtonReleasedThisFrame())
                    FinalizeMoveDrag();
                return;
            }

            // Don't react to clicks that land on UI panels.
            if (IsPointerOverEditorUI()) return;

            // Quick-inspect shortcut: when outlines are visible, clicking on a
            // spawner's centre dot selects it regardless of the current mode and
            // ensures the Properties panel is open. We deliberately skip this
            // path in Delete mode so a click on the marker still deletes.
            if (TryHandleCenterClickInspect(world)) return;

            // RMB-press anywhere on the map → start dragging the spawner under
            // the cursor (Buildings / Entities parity). Works in any mode and
            // selects the dragged instance as a side effect.
            if (TryStartMoveDrag(world)) return;

            switch (_mode)
            {
                case EditorMode.Place:  HandlePlaceMode(world);  break;
                case EditorMode.Select: HandleSelectMode(world); break;
                case EditorMode.Delete: HandleDeleteMode(world); break;
            }
        }

        private static bool IsPointerOverEditorUI()
        {
            var es = EventSystem.current;
            return es != null && es.IsPointerOverGameObject();
        }

        private void HandlePlaceMode(Vector3 worldPos)
        {
            if (_clickAction != null && _clickAction.WasPerformedThisFrame() && _selectedTemplate != null)
                PlaceSpawner(_selectedTemplate, worldPos);
        }

        private void HandleSelectMode(Vector3 worldPos)
        {
            if (_clickAction == null || !_clickAction.WasPerformedThisFrame()) return;

            var hit = FindSpawnerAtPosition(worldPos);
            SelectInstance(hit);
            SetStatus(hit == null ? "Nothing under cursor." : $"Selected '{hit.InstanceId}'.");
        }

        private void HandleDeleteMode(Vector3 worldPos)
        {
            if (_clickAction == null || !_clickAction.WasPerformedThisFrame()) return;

            var hit = FindSpawnerAtPosition(worldPos);
            if (hit == null)
            {
                SetStatus("Nothing under cursor.");
                return;
            }

            string id = hit.InstanceId;
            Destroy(hit.gameObject);
            if (_selectedInstance == hit) _selectedInstance = null;
            SetStatus($"Deleted '{id}'.");
            RefreshPropertiesPanel();
        }

        // ── Spawner ops ─────────────────────────────────────────────────────────

        private void PlaceSpawner(SpawnerTemplateData template, Vector3 worldPos)
        {
            string zone = ResolveZone(worldPos);
            int col = Mathf.RoundToInt(worldPos.x);
            int row = Mathf.RoundToInt(worldPos.y);
            string instanceId = $"{template.templateId}_{zone}_{col}_{row}";

            var go = new GameObject($"Spawner_{instanceId}");
            go.transform.position = worldPos;

            var si = go.AddComponent<SpawnerInstance>();
            var spawner = FindObjectOfType<MonsterSpawner>();
            si.Initialize(template, instanceId, zone, spawner);

            SelectInstance(si);
            SetStatus($"Placed '{instanceId}' at ({worldPos.x:F1}, {worldPos.y:F1}).");
        }

        private SpawnerInstance FindSpawnerAtPosition(Vector3 worldPos)
            => FindSpawnerAtPosition(worldPos, SELECTION_RADIUS_WORLD);

        private SpawnerInstance FindSpawnerAtPosition(Vector3 worldPos, float maxDist)
        {
            var all = FindObjectsOfType<SpawnerInstance>();
            if (all == null || all.Length == 0) return null;

            // Project to 2D for hit testing — z is irrelevant for top-down picking.
            var positions = new Vector2[all.Length];
            for (int i = 0; i < all.Length; i++)
                positions[i] = all[i] != null ? (Vector2)all[i].transform.position : Vector2.positiveInfinity;

            int idx = SpawnerHitTester.FindClosestWithinRadius(positions, worldPos, maxDist);
            return idx >= 0 ? all[idx] : null;
        }

        /// <summary>
        /// Quick-inspect shortcut wired into <see cref="HandleMapInteraction"/>.
        /// Activates when the Alt-toggle outlines are visible: a press anywhere
        /// inside <see cref="CENTER_HIT_RADIUS_WORLD"/> of a spawner centre selects
        /// that spawner and opens the Properties panel, regardless of mode (except
        /// Delete, where a click on the marker still deletes — that's the explicit
        /// purpose of Delete mode).
        ///
        /// Reads the mouse press through <see cref="MouseInputManager"/> so the
        /// legacy backend kicks in if the new InputSystem package drops events
        /// (Unity 2022.3 Editor bug — see <c>MouseInputManager</c> XML).
        ///
        /// Decomposed into <see cref="CanCenterClickInspect"/> + the mouse guard
        /// + <see cref="PerformCenterClickInspect"/> so the gating and the effect
        /// can be unit-tested independently of the live mouse state.
        /// </summary>
        private bool TryHandleCenterClickInspect(Vector3 worldPos)
        {
            if (!CanCenterClickInspect())                                return false;
            if (!MouseInputManager.WasLeftMouseButtonPressedThisFrame()) return false;
            return PerformCenterClickInspect(worldPos);
        }

        /// <summary>
        /// Returns whether the centre-click inspect shortcut is currently armed —
        /// outlines visible AND not in Delete mode. Internal so tests can probe
        /// the gating without touching the live mouse state.
        /// </summary>
        internal bool CanCenterClickInspect()
        {
            if (!_showAllOutlines)            return false;
            if (_mode == EditorMode.Delete)   return false;
            return true;
        }

        /// <summary>
        /// Executes the inspect shortcut at a given world position: looks up the
        /// nearest spawner centre within <see cref="CENTER_HIT_RADIUS_WORLD"/>,
        /// selects it, opens Properties, and updates the status line. Returns
        /// true when a spawner was selected.
        /// </summary>
        internal bool PerformCenterClickInspect(Vector3 worldPos)
        {
            var hit = FindSpawnerAtPosition(worldPos, CENTER_HIT_RADIUS_WORLD);
            if (hit == null) return false;

            SelectInstance(hit);
            SetDropdownOpen("props", true);
            RefreshMenuBtnHighlights();
            SetStatus($"Inspecting '{hit.InstanceId}'.");
            return true;
        }

        // ── Move-drag (Buildings / Entities parity) ──────────────────────────────

        /// <summary>
        /// RMB-press → start dragging the spawner under the cursor. Works in
        /// any mode (Select / Place / Delete). The drag follows the cursor in
        /// <see cref="HandleMapInteraction"/> until RMB is released, at which
        /// point <see cref="FinalizeMoveDrag"/> records the move on the undo
        /// stack. Reads the mouse press through <see cref="MouseInputManager"/>
        /// so the legacy backend kicks in if the new InputSystem package drops
        /// events (Unity 2022.3 Editor bug).
        /// </summary>
        private bool TryStartMoveDrag(Vector3 worldPos)
        {
            if (!MouseInputManager.WasRightMouseButtonPressedThisFrame()) return false;
            return BeginMoveDrag(worldPos) != null;
        }

        /// <summary>
        /// Pure side-effecting kernel of the RMB drag-start: looks for a
        /// spawner under <paramref name="worldPos"/> and arms the move state.
        /// Returns the dragged instance, or <c>null</c> if nothing was hit.
        /// Internal so EditMode tests can drive the drag lifecycle without
        /// simulating mouse events.
        /// </summary>
        internal SpawnerInstance BeginMoveDrag(Vector3 worldPos)
        {
            var hit = FindSpawnerAtPosition(worldPos);
            if (hit == null) return null;

            SelectInstance(hit);
            _dragging          = true;
            _dragStartWorldPos = hit.transform.position;
            _dragOffset        = hit.transform.position - worldPos;
            SetStatus($"Move drag: '{hit.InstanceId}' — release RMB to commit.");
            return hit;
        }

        /// <summary>
        /// Closes an in-progress drag: clears the flag, records an undo entry
        /// when the position actually changed, refreshes the Properties panel
        /// so the new coordinates are visible, and writes a status. A no-op
        /// drag (released without movement) is reported as cancelled — no
        /// undo entry is recorded so the stack stays clean.
        /// </summary>
        internal void FinalizeMoveDrag()
        {
            _dragging = false;
            if (_selectedInstance == null) return;

            var instance = _selectedInstance;
            Vector3 from = _dragStartWorldPos;
            Vector3 to   = instance.transform.position;
            if ((to - from).sqrMagnitude <= 0.0001f)
            {
                SetStatus("Move cancelled (no movement).");
                return;
            }

            string id    = instance.InstanceId;
            string label = $"Move {id} ({to.x:F1},{to.y:F1})";
            _undo.Record(new UndoStack.LambdaCommand(label,
                doAction:   () => { if (instance != null) instance.transform.position = to;   },
                undoAction: () => { if (instance != null) instance.transform.position = from; }));

            SetStatus($"Moved '{id}' → ({to.x:F1}, {to.y:F1}).");
            RefreshPropertiesPanel();
        }

        private void SelectInstance(SpawnerInstance instance)
        {
            _selectedInstance = instance;
            RefreshPropertiesPanel();
        }

        private string ResolveZone(Vector3 worldPos)
        {
            // TODO: route through ZoneManager.GetZoneAt(worldPos) once the zone
            // editor exposes a public lookup. Defaults to Lobby for parity with
            // the original placement helper.
            _ = worldPos;
            return "Lobby";
        }

        private void CancelCurrentMode()
        {
            if (_mode != EditorMode.Select)
            {
                SetMode(EditorMode.Select);
                _selectedTemplate = null;
                SetStatus("Cancelled — back to Select.");
                return;
            }
            Deactivate();
        }

        // ── Persistence (Save) ──────────────────────────────────────────────────

        public void SaveInstancesToJson()
        {
            var all = FindObjectsOfType<SpawnerInstance>();
            var sb = new StringBuilder();
            sb.AppendLine("[");
            for (int i = 0; i < all.Length; i++)
            {
                var si  = all[i];
                var pos = si.transform.position;
                int col = Mathf.RoundToInt(pos.x);
                int row = Mathf.RoundToInt(pos.y);

                sb.Append("  {");
                sb.Append($"\"template_id\": \"{si.Template?.templateId ?? "?"}\", ");
                sb.Append($"\"zone\": \"{si.Zone}\", ");
                sb.Append($"\"tile\": [{col}, {row}], ");
                sb.Append($"\"id\": \"{si.InstanceId}\"");
                sb.Append('}');
                if (i < all.Length - 1) sb.Append(',');
                sb.AppendLine();
            }
            sb.AppendLine("]");

            string path = Path.Combine(Application.streamingAssetsPath, STREAMING_SUBFOLDER, INSTANCES_FILENAME);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString());
            SetStatus($"Saved {all.Length} instance(s).");
            Debug.Log($"[SpawnerEditor] Saved {all.Length} instance(s) → {path}");
        }
    }
}
