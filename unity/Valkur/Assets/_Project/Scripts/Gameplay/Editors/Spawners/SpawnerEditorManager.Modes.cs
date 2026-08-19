using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay.Spawners;
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
            MarkInstancesDirty();
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
        /// outlines visible. Delete mode is gone (replaced by the Properties
        /// panel "Delete" button) so the only gate left is whether the user
        /// has toggled the Alt-outlines on. Internal so tests can probe the
        /// gating without touching the live mouse state.
        /// </summary>
        internal bool CanCenterClickInspect()
        {
            return _showAllOutlines;
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

            // Marked at the END of the drag, not while it is in progress: a spawner dragged
            // across the map moves every frame, and persisting each intermediate position
            // would rewrite the whole file dozens of times for one gesture.
            MarkInstancesDirty();

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
            // This used to return "Lobby" unconditionally, with a TODO waiting for a public
            // lookup that already existed. Harmless while the zone was only a label; not
            // harmless once the save started converting positions THROUGH the zone's origin,
            // because a spawner placed in zone_150_50 and labelled Lobby has its tile computed
            // against the wrong offset and comes back 100 tiles away.
            var zoneManager = FindObjectOfType<Valkur.Gameplay.World.ZoneManager>();
            if (zoneManager != null
                && zoneManager.TryGetZoneAtTile(
                       new Vector2Int(Mathf.RoundToInt(worldPos.x), Mathf.RoundToInt(worldPos.y)),
                       out var zone)
                && !string.IsNullOrEmpty(zone.zoneName))
            {
                return zone.zoneName;
            }

            Debug.LogWarning($"[SpawnerEditor] No zone covers ({worldPos.x:F0}, {worldPos.y:F0}); " +
                             "falling back to 'Lobby'. The spawner will save and load against " +
                             "Lobby's origin, so it will not come back where it was placed.");
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

        /// <summary>
        /// Writes every placed spawner to the active map slot's instances file.
        ///
        /// Returns false and writes nothing when the guard below trips, so callers that save
        /// automatically — closing the editor, Ctrl+S — cannot turn a bad session into data
        /// loss on disk.
        /// </summary>
        public bool SaveInstancesToJson()
        {
            // EditMode tests construct this manager and drive Activate/Deactivate. Now that
            // closing saves, an unguarded write would let the test runner replace the real
            // StreamingAssets file with whatever a fixture happened to have in its scene.
            // Same class of pollution as the run twin-save incident, and the same guard.
            if (Application.isEditor && !Application.isPlaying)
            {
                Debug.LogWarning("[SpawnerEditor] Save refused — Play Mode is not active. " +
                                 "EditMode test pollution prevention; production is unaffected.");
                return false;
            }

            _autosavePending = false;

            var all = FindObjectsOfType<SpawnerInstance>();

            string path = Path.Combine(
                Valkur.Core.MapEditorActiveSlot.DirForActiveSlot(STREAMING_SUBFOLDER),
                INSTANCES_FILENAME);

            // Refuse to replace a populated file with an empty one.
            //
            // Saving used to happen only when the user pressed the toolbar button, so an empty
            // scene could only ever be written deliberately. Now that closing the editor saves,
            // a session where the loader failed — no catalog, no ZoneManager, a parse error,
            // any of the paths in SpawnerInstanceLoader that log and return — would come up
            // with zero instances in the scene and quietly erase every spawner ever authored.
            //
            // Same shape as the Buildings save-collapse incident, and cheap to make impossible:
            // an empty save over a non-empty file is never what anyone wanted, and the manual
            // route out is to delete the file.
            if (all.Length == 0 && FileHasEntries(path))
            {
                SetStatus("ABORTING save — 0 spawners in scene but the file is not empty.");
                Debug.LogWarning($"[SpawnerEditor] ABORTING save — 0 instances in scene but " +
                                 $"'{path}' still holds spawners. Refusing to erase it. If you " +
                                 "really meant to clear the map, delete the file by hand.");
                return false;
            }

            var sb = new StringBuilder();
            sb.AppendLine("[");
            for (int i = 0; i < all.Length; i++)
            {
                var si  = all[i];
                var pos = si.transform.position;

                // The file stores tiles ZONE-RELATIVE with the row axis flipped; world space
                // is absolute with y growing upward. Writing RoundToInt(position) here — as
                // this did — put absolute coordinates into a field the loader reads as
                // zone-relative, so every reload shifted each spawner by its zone's origin.
                // Lobby is at (150, 50), which is why spawners marched 150 tiles right per
                // restart until they left the map. SpawnerTileMapping owns both directions
                // now, so they cannot disagree again.
                var tile = ResolveTileForSave(si, pos);
                int col = tile.x;
                int row = tile.y;

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

            // The path is map-slot aware: the default slot keeps the legacy
            // StreamingAssets/Spawners/ location, custom maps authored from the F11 Map Editor
            // write under persistentDataPath/Maps/<slot>/Spawners/. Placing a spawner on one
            // map must never overwrite another's file.
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString());
            SetStatus($"Saved {all.Length} instance(s).");
            Debug.Log($"[SpawnerEditor] Saved {all.Length} instance(s) → {path}");
            return true;
        }

        // ── Autosave ────────────────────────────────────────────────────────────

        /// <summary>
        /// Seconds of quiet after the last edit before the map is written.
        ///
        /// Not zero, for two reasons. Placing a run of spawners fires one edit per click, and
        /// a debounce collapses that burst into a single write instead of rewriting the whole
        /// file per spawner. More importantly <see cref="DeleteSelectedInstance"/> goes through
        /// SafeDestroy, which in Play Mode defers to <c>Destroy</c> — the object is still alive
        /// until the end of the frame, so a save that ran immediately would find the deleted
        /// spawner with FindObjectsOfType and write it straight back.
        /// </summary>
        private const float AUTOSAVE_DEBOUNCE_SECONDS = 0.75f;

        private bool _autosavePending;
        private float _autosaveDueAt;

        /// <summary>
        /// Records that the map changed. Every mutation funnels through here rather than
        /// calling save directly, so a new edit operation cannot forget to persist — which is
        /// exactly how this editor ended up with no automatic save at all.
        /// </summary>
        internal void MarkInstancesDirty()
        {
            _autosavePending = true;
            _autosaveDueAt = Time.unscaledTime + AUTOSAVE_DEBOUNCE_SECONDS;
        }

        /// <summary>Writes the map once the debounce has elapsed. Called every active frame.</summary>
        private void TickAutosave()
        {
            if (!_autosavePending) return;
            if (Time.unscaledTime < _autosaveDueAt) return;
            SaveInstancesToJson();
        }

        /// <summary>Writes immediately if anything is pending. Used when the editor closes.</summary>
        internal void FlushAutosave()
        {
            if (_autosavePending) SaveInstancesToJson();
        }

        /// <summary>
        /// The zone-relative tile to persist for a placed spawner.
        ///
        /// Falls back to the raw world position only when the zone cannot be resolved — which
        /// keeps a spawner in an unregistered zone round-tripping the way it always did rather
        /// than silently teleporting it to the origin, and matches the loader, which skips
        /// such an entry with a warning either way.
        /// </summary>
        private Vector2Int ResolveTileForSave(SpawnerInstance si, Vector3 worldPos)
        {
            var zoneManager = FindObjectOfType<Valkur.Gameplay.World.ZoneManager>();
            if (zoneManager != null && !string.IsNullOrEmpty(si.Zone)
                && zoneManager.TryGetZone(si.Zone, out var zoneDef))
            {
                return SpawnerTileMapping.WorldToTile(
                    worldPos, zoneDef.gridOffset, zoneManager.ZoneHeightTiles);
            }

            Debug.LogWarning($"[SpawnerEditor] Zone '{si.Zone}' could not be resolved for " +
                             $"'{si.InstanceId}'; persisting its raw world position.");
            return new Vector2Int(Mathf.RoundToInt(worldPos.x), Mathf.RoundToInt(worldPos.y));
        }

        /// <summary>
        /// Whether the instances file on disk currently holds at least one spawner.
        ///
        /// Deliberately crude — it looks for an object brace rather than parsing — because it
        /// only ever gates a refusal. A malformed file reads as "has entries" and blocks the
        /// save, which is the safe direction: the user still has whatever was there.
        /// </summary>
        private static bool FileHasEntries(string path)
        {
            try
            {
                return File.Exists(path) && File.ReadAllText(path).Contains("{");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SpawnerEditor] Could not read '{path}' to check it before " +
                                 $"saving ({e.Message}). Treating it as populated.");
                return true;
            }
        }
    }
}
