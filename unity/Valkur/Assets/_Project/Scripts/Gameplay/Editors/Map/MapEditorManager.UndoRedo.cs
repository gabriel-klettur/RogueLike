using System;
using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Undo/Redo orchestration for the F11 Map Editor. Every CRUD path inside
    /// the editor calls into the helpers here to record an inverse — Add,
    /// Remove, Move, Rename, ToggleEditable on zones, plus Place / Remove on
    /// portals. <see cref="PerformUndo"/> / <see cref="PerformRedo"/> are
    /// wired to Ctrl+Z / Ctrl+Y in <see cref="Update"/>.
    /// </summary>
    public partial class MapEditorManager
    {
        private readonly MapEditorUndoSystem _undoSystem = new MapEditorUndoSystem();

        // Re-entry guard. Set to true while replaying a Do/Undo closure so
        // recording helpers on the live editor's CRUD path skip pushing a
        // new entry — without this every undo would itself record a new
        // undo entry and the stack would never drain.
        private bool _isReplayingUndo;

        public bool CanUndo => _undoSystem.CanUndo;
        public bool CanRedo => _undoSystem.CanRedo;

        public void PerformUndo()
        {
            _isReplayingUndo = true;
            try
            {
                if (!_undoSystem.Undo(out var label))
                {
                    _ui?.SetStatus("Nothing to undo.");
                    return;
                }
                _ui?.SetStatus($"Undid: {label}");
            }
            finally { _isReplayingUndo = false; }
        }

        public void PerformRedo()
        {
            _isReplayingUndo = true;
            try
            {
                if (!_undoSystem.Redo(out var label))
                {
                    _ui?.SetStatus("Nothing to redo.");
                    return;
                }
                _ui?.SetStatus($"Redid: {label}");
            }
            finally { _isReplayingUndo = false; }
        }

        // ── Recording helpers (called from the existing CRUD partials) ─────────
        //
        // The call-site invariant is "the editor has just performed the op
        // successfully — record an inverse". Recording AFTER the act keeps
        // failed operations out of the stack automatically.

        internal void RecordZoneAdd(string zoneName, Vector2Int offset, bool editable)
        {
            if (_isReplayingUndo) return;
            string snapshot = zoneName;
            Vector2Int snapOffset = offset;
            bool snapEditable = editable;

            _undoSystem.Push(
                label: $"Add zone '{snapshot}'",
                @do: () =>
                {
                    if (zoneManager == null) return;
                    if (zoneManager.AddZone(snapshot, snapOffset, snapEditable))
                        PersistZonesToDisk();
                },
                undo: () =>
                {
                    if (zoneManager == null) return;
                    if (zoneManager.RemoveZone(snapshot))
                        PersistZonesToDisk();
                });
        }

        internal void RecordZoneRemove(string zoneName, Vector2Int offset, bool editable)
        {
            if (_isReplayingUndo) return;
            string snapshot = zoneName;
            Vector2Int snapOffset = offset;
            bool snapEditable = editable;

            _undoSystem.Push(
                label: $"Delete zone '{snapshot}'",
                @do: () =>
                {
                    if (zoneManager == null) return;
                    if (zoneManager.RemoveZone(snapshot))
                        PersistZonesToDisk();
                },
                undo: () =>
                {
                    if (zoneManager == null) return;
                    if (zoneManager.AddZone(snapshot, snapOffset, snapEditable))
                        PersistZonesToDisk();
                });
        }

        internal void RecordZoneMove(string zoneName, Vector2Int delta)
        {
            if (_isReplayingUndo) return;
            string snapshot = zoneName;
            Vector2Int snapDelta = delta;

            _undoSystem.Push(
                label: $"Move zone '{snapshot}' by [{snapDelta.x},{snapDelta.y}]",
                @do: () =>
                {
                    if (zoneManager == null) return;
                    if (zoneManager.MoveZone(snapshot, snapDelta))
                        PersistZonesToDisk();
                },
                undo: () =>
                {
                    if (zoneManager == null) return;
                    if (zoneManager.MoveZone(snapshot, -snapDelta))
                        PersistZonesToDisk();
                });
        }

        internal void RecordZoneRename(string oldName, string newName)
        {
            if (_isReplayingUndo) return;
            string oldSnap = oldName;
            string newSnap = newName;

            _undoSystem.Push(
                label: $"Rename '{oldSnap}' → '{newSnap}'",
                @do: () =>
                {
                    if (zoneManager == null) return;
                    if (zoneManager.RenameZone(oldSnap, newSnap))
                    {
                        Valkur.Gameplay.TileEditor.TileOverlayPersistence
                            .RenameOverride(oldSnap, newSnap, ActiveWorldId);
                        PersistZonesToDisk();
                    }
                },
                undo: () =>
                {
                    if (zoneManager == null) return;
                    if (zoneManager.RenameZone(newSnap, oldSnap))
                    {
                        Valkur.Gameplay.TileEditor.TileOverlayPersistence
                            .RenameOverride(newSnap, oldSnap, ActiveWorldId);
                        PersistZonesToDisk();
                    }
                });
        }

        internal void RecordZoneToggleEditable(string zoneName, bool newValue)
        {
            if (_isReplayingUndo) return;
            string snapshot = zoneName;
            bool snapNew = newValue;

            _undoSystem.Push(
                label: $"{(snapNew ? "Unlock" : "Lock")} zone '{snapshot}'",
                @do: () =>
                {
                    if (zoneManager == null) return;
                    if (zoneManager.SetZoneEditable(snapshot, snapNew))
                        PersistZonesToDisk();
                },
                undo: () =>
                {
                    if (zoneManager == null) return;
                    if (zoneManager.SetZoneEditable(snapshot, !snapNew))
                        PersistZonesToDisk();
                });
        }

        internal void RecordPortalAdd(string portalId)
        {
            if (_isReplayingUndo) return;
            // Capture the entry by id so undo can read the same record back
            // even if a Move/Rename op pushed it down the list. Re-spawn on
            // redo by re-adding the captured spec.
            string snapshotId = portalId;
            PortalPersistenceEntry spec = FindPortalEntry(snapshotId);

            _undoSystem.Push(
                label: $"Place portal '{snapshotId}'",
                @do: () => { if (FindPortalEntry(snapshotId) == null && spec != null) AddPortalFromEntry(spec); },
                undo: () => RemovePortal(snapshotId));
        }

        internal void RecordPortalRemove(PortalPersistenceEntry removed)
        {
            if (_isReplayingUndo) return;
            if (removed == null) return;
            PortalPersistenceEntry spec = ClonePortalEntry(removed);

            _undoSystem.Push(
                label: $"Remove portal '{spec.portalId}'",
                @do: () => RemovePortal(spec.portalId),
                undo: () =>
                {
                    if (FindPortalEntry(spec.portalId) == null)
                        AddPortalFromEntry(spec);
                });
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private PortalPersistenceEntry FindPortalEntry(string portalId)
        {
            if (string.IsNullOrEmpty(portalId)) return null;
            for (int i = 0; i < _portals.Count; i++)
            {
                if (_portals[i] != null && _portals[i].portalId == portalId)
                    return _portals[i];
            }
            return null;
        }

        private static PortalPersistenceEntry ClonePortalEntry(PortalPersistenceEntry src)
        {
            if (src == null) return null;
            return new PortalPersistenceEntry
            {
                portalId                  = src.portalId,
                sourceWorldX              = src.sourceWorldX,
                sourceWorldY              = src.sourceWorldY,
                destinationZoneName       = src.destinationZoneName,
                destinationUseZoneCenter  = src.destinationUseZoneCenter,
                destinationWorldX         = src.destinationWorldX,
                destinationWorldY         = src.destinationWorldY,
                activationRadius          = src.activationRadius,
            };
        }

        // Re-insert a full portal record (with the same id) without re-recording
        // an undo entry — the recording happens in the live AddPortal path.
        // This is the redo path of a removed portal.
        private void AddPortalFromEntry(PortalPersistenceEntry entry)
        {
            if (entry == null) return;
            _portals.Add(ClonePortalEntry(entry));
            // Spawn the runtime visual via the same lifecycle the live editor uses.
            var spawn = typeof(MapEditorManager).GetMethod("SpawnPortalObject",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            spawn?.Invoke(this, new object[] { _portals[_portals.Count - 1] });
            PersistZonesToDisk();
        }

        // Slot-switch / new-map sites call this to drop the cross-slot history.
        internal void ClearUndoHistory() => _undoSystem.Clear();
    }
}
