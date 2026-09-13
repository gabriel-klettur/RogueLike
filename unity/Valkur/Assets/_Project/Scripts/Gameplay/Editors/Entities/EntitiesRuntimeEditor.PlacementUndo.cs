using UnityEngine;
using Valkur.UIKit;

namespace Valkur.Gameplay.Entities
{
    /// <summary>
    /// Placing and deleting a monster on the map, as undoable steps.
    ///
    /// <para><b>These were the last two mutations outside the stack, and they were the two
    /// worst ones to leave out.</b> After the undo rewrite, every edit to a DEFINITION was
    /// recoverable — sixty property rows, the auto-cast list, the timeline, the spell muzzle —
    /// while the two gestures an author actually spends the session making, dropping a monster
    /// on the map and taking one off, were not. The Add/Remove hint said so in as many words
    /// ("This is not undoable"), which documents the hole rather than closing it.</para>
    ///
    /// <para>Worse, it was INCONSISTENT in the direction nobody could predict: dragging an
    /// already-placed entity DID undo, because that single gesture was the one thing the
    /// original stack covered. So the editor undid moving a monster and refused to undo
    /// deleting it.</para>
    ///
    /// <para><b>A placement is restored by its ID, not by its object.</b> The GameObject is
    /// gone after a delete and a captured reference would be a dangling one, so both directions
    /// are expressed as "make the world contain (or not contain) the placement with this id" —
    /// which is also what keeps the saved record stable: re-spawning with the same
    /// <see cref="PersistedEntityInstance.PlacementId"/> is a restore, while minting a fresh id
    /// would read as a delete-and-create to anything holding a by_eid FSM override.</para>
    ///
    /// <para>This is deliberately NOT a confirmation dialog. A modal on every delete click is
    /// the wrong tool for a gesture an author repeats — it is one extra click on the ninety-nine
    /// deletes they meant, to protect the one they did not. Undo protects all hundred and costs
    /// nothing until it is needed. Buildings keeps its confirm because a building delete there
    /// takes a whole painted collision grid with it.</para>
    /// </summary>
    public partial class EntitiesRuntimeEditor
    {
        /// <summary>
        /// Everything needed to bring one placement back, with no reference to the object.
        /// </summary>
        private readonly struct PlacementRecord
        {
            public readonly string  Id;
            public readonly string  Key;
            public readonly Vector3 Position;
            public readonly float   RespawnSeconds;

            public PlacementRecord(string id, string key, Vector3 position, float respawnSeconds)
            {
                Id = id; Key = key; Position = position; RespawnSeconds = respawnSeconds;
            }
        }

        /// <summary>
        /// Snapshot a placement so it can be recreated later — from its AUTHORED record, not its
        /// transform. A monster that has walked since it was put down must come back where the
        /// author put it, not wherever it happened to be standing when it was deleted.
        /// </summary>
        private PlacementRecord Describe(PersistedEntityInstance marker)
        {
            if (PlacedEntities.TryGetRecord(marker.PlacementId, out var record))
                return new PlacementRecord(record.Id, record.MonsterKey,
                                           new Vector3(record.WorldPos.x, record.WorldPos.y, 0f),
                                           record.RespawnSeconds);
            return new PlacementRecord(marker.PlacementId, marker.MonsterKey,
                                       marker.transform.position, 0f);
        }

        /// <summary>
        /// Find a live placement by its stable id, or null.
        ///
        /// <para>INACTIVE ones are skipped, and that is what makes undo-then-redo in a single
        /// frame safe. <c>Object.Destroy</c> is deferred to end of frame, so a placement undone
        /// and immediately redone would otherwise have the doomed object still answering to its
        /// own id while the fresh one is created — two objects, one id, until the frame ends.
        /// <see cref="RemovePlacementById"/> deactivates before destroying precisely so this
        /// filter can tell them apart.</para>
        /// </summary>
        private static PersistedEntityInstance FindPlacement(string placementId)
        {
            if (string.IsNullOrEmpty(placementId)) return null;
            foreach (var marker in FindObjectsOfType<PersistedEntityInstance>())
                if (marker != null && marker.gameObject.activeSelf && !marker.IsDefeated &&
                    marker.PlacementId == placementId) return marker;
            return null;
        }

        /// <summary>
        /// Remove the placement with this id, if it is still out there.
        ///
        /// <para><c>DestroyImmediate</c> under Edit Mode because <c>Object.Destroy</c> is an
        /// outright error there, and this runs from EditMode fixtures as well as from a live
        /// session — the same branch every other teardown in these editors carries.</para>
        /// </summary>
        private void RemovePlacementById(string placementId)
        {
            // The service deactivates FIRST so the object leaves the world in THIS frame:
            // Destroy is deferred to end of frame, and a redo issued before then would find the
            // doomed object still answering to its id. It also removes the record when the
            // placement is currently dead, which a scene search could never reach.
            if (!PlacedEntities.Remove(placementId)) return;
            RefreshPicker();
        }

        /// <summary>Put a placement back exactly where and what it was.</summary>
        private void RestorePlacement(PlacementRecord record)
        {
            if (string.IsNullOrEmpty(record.Key)) return;
            SpawnMonsterAt(record.Key, record.Position, record.Id, record.RespawnSeconds);
            RefreshPicker();
        }

        /// <summary>
        /// Record a placement that has just been made, so Ctrl+Z takes it back off the map.
        /// </summary>
        private void RecordPlacementCreated(PersistedEntityInstance marker)
        {
            if (marker == null) return;
            var record = Describe(marker);
            string label = $"Place '{record.Key}'";

            _undo.Record(new UndoStack.LambdaCommand(label,
                doAction:   () => RestorePlacement(record),
                undoAction: () => RemovePlacementById(record.Id)));
        }

        /// <summary>
        /// Record a placement that is ABOUT to be deleted. Called before the destroy, because
        /// afterwards there is nothing left to read the key and the position off.
        /// </summary>
        private void RecordPlacementDeleted(PersistedEntityInstance marker)
        {
            if (marker == null) return;
            var record = Describe(marker);
            string label = $"Delete '{record.Key}'";

            _undo.Record(new UndoStack.LambdaCommand(label,
                doAction:   () => RemovePlacementById(record.Id),
                undoAction: () => RestorePlacement(record)));
        }
    }
}
