using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Map Editor "Maps" explorer — saves the current zone universe as a named
    /// slot, lists existing slots, loads them back, renames or deletes them.
    /// Each slot is a self-contained <see cref="ZonePersistenceFile"/> JSON
    /// snapshot under <c>Application.persistentDataPath/Maps/</c>; the
    /// live working copy (<c>map_editor_zones.json</c>) is unchanged so
    /// existing recovery / migration paths keep working.
    ///
    /// Tile overrides are deliberately NOT routed per slot in this revision —
    /// they remain shared across maps via <c>MapOverrides/&lt;zone&gt;.overlay.json</c>,
    /// keyed only by zone name. Slot-aware tile routing is a follow-up.
    /// </summary>
    public partial class MapEditorManager
    {
        public event Action OnMapSlotsChanged;

        private MapEditorMapSlots _slotStore;

        public string ActiveMapSlot => ResolveSlotStore().ActiveSlot;
        public string[] ListMapSlots() => ResolveSlotStore().ListSlots().ToArray();

        private MapEditorMapSlots ResolveSlotStore()
        {
            if (_slotStore == null) _slotStore = new MapEditorMapSlots();
            return _slotStore;
        }

        // ── Save current zones as a named slot ───────────────────────────────────

        public bool SaveCurrentMapAs(string slotName)
        {
            if (string.IsNullOrWhiteSpace(slotName)) return false;
            string clean = MapEditorMapSlots.Sanitize(slotName);
            if (string.IsNullOrEmpty(clean)) return false;

            // Make sure the working copy reflects the current ZoneManager.
            PersistZonesToDisk();

            string json = ReadWorkingCopyJson();
            if (json == null) return false;

            var store = ResolveSlotStore();
            if (!store.WriteSlot(clean, json)) return false;
            store.SetActive(clean);
            OnMapSlotsChanged?.Invoke();
            return true;
        }

        // ── Load a named slot into the live ZoneManager ──────────────────────────

        public bool LoadMapSlot(string slotName)
        {
            if (string.IsNullOrWhiteSpace(slotName)) return false;
            string clean = MapEditorMapSlots.Sanitize(slotName);
            if (string.IsNullOrEmpty(clean)) return false;

            var store = ResolveSlotStore();
            string json = store.ReadSlot(clean);
            if (json == null) return false;

            // Snapshot the current state into its existing slot first so the
            // user doesn't silently lose unsaved edits.
            BackupCurrentToActiveSlot();

            ZonePersistenceFile data;
            try { data = JsonUtility.FromJson<ZonePersistenceFile>(json); }
            catch (Exception ex)
            {
                Debug.LogError($"[MapEditor] Slot '{clean}' parse failed: {ex.Message}");
                return false;
            }
            if (data == null) return false;

            ApplySlotToZoneManager(data);
            store.SetActive(clean);
            PersistZonesToDisk();
            ResolveBuildingLoader()?.ClearGeneratedAbove(BIOME_INSTANCE_ID_BASE);
            OnMapSlotsChanged?.Invoke();
            return true;
        }

        // ── Begin a fresh blank map ──────────────────────────────────────────────

        public bool BeginNewMap(string slotName)
        {
            string clean = MapEditorMapSlots.Sanitize(slotName);
            if (string.IsNullOrEmpty(clean)) clean = MapEditorMapSlots.DEFAULT_SLOT;

            BackupCurrentToActiveSlot();

            zoneManager?.ReplaceZones(Array.Empty<ZoneManager.ZoneDefinition>());
            if (_state != null)
            {
                _state.RestrictTileEditingToEditableZones = false;
                _state.NextZoneIndex = 1;
            }
            PersistZonesToDisk();

            ResolveBuildingLoader()?.ClearGeneratedAbove(BIOME_INSTANCE_ID_BASE);
            ResolveSlotStore().SetActive(clean);
            OnMapSlotsChanged?.Invoke();
            return true;
        }

        // ── Delete + Rename ──────────────────────────────────────────────────────

        public bool DeleteMapSlot(string slotName)
        {
            string clean = MapEditorMapSlots.Sanitize(slotName);
            if (string.IsNullOrEmpty(clean)) return false;
            bool ok = ResolveSlotStore().DeleteSlot(clean);
            if (ok) OnMapSlotsChanged?.Invoke();
            return ok;
        }

        public bool RenameMapSlot(string oldName, string newName)
        {
            string oldClean = MapEditorMapSlots.Sanitize(oldName);
            string newClean = MapEditorMapSlots.Sanitize(newName);
            if (string.IsNullOrEmpty(oldClean) || string.IsNullOrEmpty(newClean)) return false;
            bool ok = ResolveSlotStore().RenameSlot(oldClean, newClean);
            if (ok) OnMapSlotsChanged?.Invoke();
            return ok;
        }

        // ── Internals ────────────────────────────────────────────────────────────

        private void BackupCurrentToActiveSlot()
        {
            var store = ResolveSlotStore();
            string active = store.ActiveSlot;
            if (string.IsNullOrEmpty(active)) return;
            PersistZonesToDisk();
            string json = ReadWorkingCopyJson();
            if (json != null)
                store.WriteSlot(active, json);
        }

        private string ReadWorkingCopyJson()
        {
            try
            {
                string raw = ResolveZonesRepository().ReadWithSidecarFallback(_persistenceWorldId, out _);
                return raw;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MapEditor.Slots] Read working copy failed: {ex.Message}");
                return null;
            }
        }

        private void ApplySlotToZoneManager(ZonePersistenceFile data)
        {
            if (zoneManager == null || data == null) return;

            var defs = new List<ZoneManager.ZoneDefinition>(
                data.zones != null ? data.zones.Count : 0);
            if (data.zones != null)
            {
                for (int i = 0; i < data.zones.Count; i++)
                {
                    var entry = data.zones[i];
                    if (string.IsNullOrWhiteSpace(entry.zoneName)) continue;
                    defs.Add(new ZoneManager.ZoneDefinition
                    {
                        zoneName             = entry.zoneName,
                        gridOffset           = new Vector2Int(entry.gridOffsetX, entry.gridOffsetY),
                        zoneMusic            = null,
                        editableInTileEditor = entry.editableInTileEditor,
                    });
                }
            }
            zoneManager.ReplaceZones(defs);

            if (_state != null)
            {
                _state.RestrictTileEditingToEditableZones = data.restrictTileEditingToEditableZones;
                _state.NextZoneIndex = Mathf.Max(1, data.nextZoneIndex);
            }
        }
    }
}
