using UnityEngine;
using Valkur.Core.Input;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.FSM;
using Valkur.UIKit;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// Moving a placement by an exact amount: arrow keys, and a grid snap.
    ///
    /// <para><b>The only thing a placement owns is WHERE IT IS, and the only way to author it
    /// was to drag it with the mouse.</b> No snap, no keyboard step, no numeric field — so
    /// putting six guards in a line was six freehand drags and no way to line them up, and
    /// "one tile to the left" was a gesture rather than a value. Tile has nine tools and
    /// Buildings ten; this editor shipped with none, which is why the gap looked like a design
    /// choice instead of a hole.</para>
    ///
    /// <para>The step is a WORLD UNIT, which is one tile at this project's 16 PPU, and Shift
    /// makes it a tenth. Both go through <c>EditorInput.Tool</c> against the new
    /// <c>Editor.Entities</c> map: a raw <c>KeyCode</c> here would be invisible to the Controls
    /// editor and to the conflict scanner, which is how every duplicate-key bug this project
    /// has shipped was written.</para>
    ///
    /// <para><b>The snap is applied at PLACEMENT and at NUDGE, never as a running correction.</b>
    /// A snap that re-aligned the entity every frame would fight the mouse mid-drag and make
    /// the freehand placement the editor already had impossible.</para>
    /// </summary>
    public partial class EntitiesRuntimeEditor
    {
        /// <summary>One world unit is one tile at 16 PPU — the grid an author is looking at.</summary>
        private const float NUDGE_STEP        = 1f;
        private const float NUDGE_STEP_FINE   = 0.1f;

        /// <summary>Off by default: the editor has always placed freehand and a snap that
        /// arrived switched on would silently move the next thing an author put down.</summary>
        private bool _gridSnap;

        /// <summary>Round a world position onto the tile grid, keeping Z.</summary>
        private static Vector3 SnapToGrid(Vector3 p) =>
            new Vector3(Mathf.Round(p.x), Mathf.Round(p.y), p.z);

        /// <summary>Snap only if the author asked for it. The single place placement consults.</summary>
        private Vector3 ApplyGridSnap(Vector3 worldPos) =>
            _gridSnap ? SnapToGrid(worldPos) : worldPos;

        /// <summary>
        /// Arrow keys and the snap toggle. Called from <c>Update</c> while the editor is open.
        /// </summary>
        private void HandleEntitiesEditorTools()
        {
            if (EditorInput.Tool(InputActionCatalog.MapEntitiesEditor, "ToggleSnap"))
                ToggleGridSnap();

            // Read once: four calls to IsShiftHeld in one frame would all answer the same and
            // the intent is one decision, not four.
            float step = KeyboardInputManager.IsShiftHeld() ? NUDGE_STEP_FINE : NUDGE_STEP;

            if (EditorInput.Tool(InputActionCatalog.MapEntitiesEditor, "NudgeUp"))
                NudgeActiveEntity(new Vector3(0f,  step, 0f));
            if (EditorInput.Tool(InputActionCatalog.MapEntitiesEditor, "NudgeDown"))
                NudgeActiveEntity(new Vector3(0f, -step, 0f));
            if (EditorInput.Tool(InputActionCatalog.MapEntitiesEditor, "NudgeLeft"))
                NudgeActiveEntity(new Vector3(-step, 0f, 0f));
            if (EditorInput.Tool(InputActionCatalog.MapEntitiesEditor, "NudgeRight"))
                NudgeActiveEntity(new Vector3( step, 0f, 0f));
        }

        private void ToggleGridSnap()
        {
            _gridSnap = !_gridSnap;
            SetStatus(_gridSnap
                ? "Grid snap ON — placements and nudges land on whole tiles."
                : "Grid snap OFF — freehand placement.");
        }

        /// <summary>
        /// Move the selected placement, as one undoable step that also reaches the file.
        /// </summary>
        private void NudgeActiveEntity(Vector3 delta)
        {
            if (_activeEntity == null)
            {
                SetStatus("Nudge: nothing selected. Click an NPC on the map first.");
                return;
            }

            var brain = _activeEntity;
            Vector3 from = brain.transform.position;
            Vector3 to   = _gridSnap ? SnapToGrid(from + delta) : from + delta;
            if ((to - from).sqrMagnitude <= 0.0000001f) return;

            brain.transform.position = to;
            RecordEntityMove(brain, from, to, $"Nudge {brain.gameObject.name}");
            SetStatus($"{brain.gameObject.name} -> ({to.x:F2}, {to.y:F2})" +
                      (_gridSnap ? "  [snapped]" : ""));
        }

        /// <summary>
        /// One owner for "this placement moved": the undo step AND the save.
        ///
        /// <para><b>The save half was missing and it was a real defect.</b>
        /// <c>MarkEntityPlacementsDirty</c>'s own doc claims it is "called by every mutation
        /// (place, delete)" — and MOVE was not one of them, so dragging a monster to a new spot
        /// changed the world, recorded an undo step, and never scheduled a write. The new
        /// position survived only if some later edit happened to dirty the file first. Found
        /// while wiring the keyboard nudge, by asking what the drag path did that this one
        /// would have to copy.</para>
        /// </summary>
        private void RecordEntityMove(FSMMonsterBrain brain, Vector3 from, Vector3 to, string label)
        {
            if (brain == null) return;

            _undo.Record(new UndoStack.LambdaCommand($"{label} ({to.x:F1},{to.y:F1})",
                doAction:   () => { if (brain != null) { brain.transform.position = to;   NotePlacementMoved(brain); } },
                undoAction: () => { if (brain != null) { brain.transform.position = from; NotePlacementMoved(brain); } }));

            NotePlacementMoved(brain);
        }

        /// <summary>
        /// The X / Y of the placement currently selected, as editable rows in Identity.
        ///
        /// <para><b>The Properties panel described a DEFINITION and never a PLACEMENT.</b>
        /// Sixty rows of what a monster IS, and not one saying where this particular one
        /// stands — so the only fact an instance owns of its own had no field anywhere in the
        /// editor. Two entities cannot be aligned by eye at any zoom; they can be aligned by
        /// typing the same number twice.</para>
        ///
        /// <para>Shown only when a placement is selected: a coordinate row over a definition
        /// nobody has put down yet would be a box that reads 0,0 and refuses every value.</para>
        /// </summary>
        private void FillPlacementSection()
        {
            if (_activeEntity == null) return;
            var placement = PlacementOf(_activeEntity);
            if (placement == null) return;   // spawner- or dungeon-made, or a corpse: not ours to move

            Vector3 p = _activeEntity.transform.position;

            EntitiesEditorUIBuilder.AddEditableRow(_ui.PropsIdentitySection, "Pos X",
                p.x.ToString("0.###"), raw =>
                {
                    if (!float.TryParse(raw, System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture,
                                        out float x)) { SetStatus($"'{raw}' is not a number."); return; }
                    if (_activeEntity == null) return;
                    SetActiveEntityPosition(x, _activeEntity.transform.position.y);
                });

            EntitiesEditorUIBuilder.AddEditableRow(_ui.PropsIdentitySection, "Pos Y",
                p.y.ToString("0.###"), raw =>
                {
                    if (!float.TryParse(raw, System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture,
                                        out float y)) { SetStatus($"'{raw}' is not a number."); return; }
                    if (_activeEntity == null) return;
                    SetActiveEntityPosition(_activeEntity.transform.position.x, y);
                });

            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsIdentitySection, "Snap",
                _gridSnap ? "ON (G)" : "off (G)");

            FillPlacementLifecycleRows(placement.PlacementId);
        }

        /// <summary>
        /// What happens to this placement when the player kills it, as an authored value, plus how
        /// many of this map's placements the current run has already killed.
        ///
        /// <para>The respawn time is the only lifecycle fact the author owns. The KILL belongs to the
        /// run and is never written to the map file — which is why a killed placement can be
        /// missing from the world while still being in the file, and why this row says so.</para>
        /// </summary>
        private void FillPlacementLifecycleRows(string placementId)
        {
            var service = PlacedEntities;
            if (!service.TryGetRecord(placementId, out var record)) return;

            EntitiesEditorUIBuilder.AddEditableRow(_ui.PropsIdentitySection, "Respawn (s)",
                record.RespawnSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                raw =>
                {
                    if (!float.TryParse(raw, System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture,
                                        out float seconds) || seconds < 0f)
                    {
                        SetStatus($"'{raw}' is not a respawn time. 0 = stays dead for the run.");
                        return;
                    }
                    SetPlacementRespawn(placementId, seconds);
                });

            int defeated = 0;
            foreach (var r in service.Records)
                if (service.RunState.Contains(r.Id)) defeated++;

            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsIdentitySection, "On death",
                record.RespawnSeconds > 0f
                    ? $"returns after {record.RespawnSeconds:0.#} s"
                    : "stays dead this run");
            EntitiesEditorUIBuilder.AddPropertyRow(_ui.PropsIdentitySection, "Killed this run",
                defeated == 0 ? "none" : $"{defeated} (console: placed revive all)");
        }

        /// <summary>Set a placement's respawn time, as one undoable step.</summary>
        private void SetPlacementRespawn(string placementId, float seconds)
        {
            var service = PlacedEntities;
            if (!service.TryGetRecord(placementId, out var record)) return;
            float before = record.RespawnSeconds;
            if (Mathf.Approximately(before, seconds)) return;

            service.SetRespawnSeconds(placementId, seconds);
            _undo.Record(new UndoStack.LambdaCommand($"Respawn {seconds:0.#} s",
                doAction:   () => PlacedEntities.SetRespawnSeconds(placementId, seconds),
                undoAction: () => PlacedEntities.SetRespawnSeconds(placementId, before)));

            SetStatus(seconds > 0f
                ? $"Placement returns {seconds:0.#} s after it is killed."
                : "Placement stays dead for the rest of the run once killed.");
        }

        /// <summary>
        /// Put the selected placement at an exact coordinate — what the Properties panel's
        /// X / Y rows commit to.
        /// </summary>
        private void SetActiveEntityPosition(float x, float y)
        {
            if (_activeEntity == null) return;
            var brain = _activeEntity;
            Vector3 from = brain.transform.position;
            var to = new Vector3(x, y, from.z);
            if ((to - from).sqrMagnitude <= 0.0000001f) return;

            brain.transform.position = to;
            RecordEntityMove(brain, from, to, $"Move {brain.gameObject.name}");
            SetStatus($"{brain.gameObject.name} -> ({to.x:F2}, {to.y:F2}).");
        }
    }
}
