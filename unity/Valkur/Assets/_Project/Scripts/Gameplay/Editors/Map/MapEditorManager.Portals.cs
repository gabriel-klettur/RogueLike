using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.MapEditor
{
    /// <summary>
    /// Portal-placement subsystem for the F11 Map Editor. Owns the in-memory
    /// list of portals for the active map slot, spawns them as runtime
    /// <see cref="ZonePortal"/> GameObjects, and drives the placement flow
    /// triggered from the toolbar.
    ///
    /// On-disk representation lives on <see cref="ZonePersistenceFile.portals"/>
    /// (schema 1.1+), so portal records travel with the slot file alongside
    /// zones — no separate file to keep in sync. Spawned visuals are parented
    /// under <see cref="_portalsRoot"/> for cheap clear-and-respawn on slot
    /// load.
    /// </summary>
    public partial class MapEditorManager
    {
        private readonly List<PortalPersistenceEntry> _portals = new List<PortalPersistenceEntry>();
        private readonly List<GameObject> _portalObjects = new List<GameObject>();
        private GameObject _portalsRoot;

        // Placement flow state (mirrors the AddZoneFlow design — single in-flight
        // user gesture from "click toolbar" to "confirm via dialog").
        private bool _isPlacePortalActive;
        private bool _hasPendingPortalSource;
        private Vector3 _pendingPortalSourceWorld;
        private int _placePortalFlowStartedFrame = -1;
        private GameObject _portalSourcePreviewObject;

        /// <summary>True while the user is in the "click on the map" stage of
        /// the portal-placement flow.</summary>
        public bool IsPlacePortalFlowActive => _isPlacePortalActive;

        // ── Lifecycle ───────────────────────────────────────────────────────────

        private void EnsurePortalsRoot()
        {
            if (_portalsRoot != null) return;
            _portalsRoot = new GameObject("MapEditorPortals");
            // Top-level so cleanup is one Destroy() call regardless of who
            // is alive elsewhere; the child portals own their own colliders
            // and visuals so they remain self-contained.
            _portalsRoot.transform.SetParent(null);
        }

        // ── Persistence hooks (called from MapEditorManager.Persistence.cs) ─────

        internal void HydratePortalsFromPersistence(ZonePersistenceFile data)
        {
            _portals.Clear();
            if (data?.portals == null) return;
            for (int i = 0; i < data.portals.Count; i++)
            {
                var entry = data.portals[i];
                if (entry == null) continue;
                _portals.Add(entry);
            }
            RespawnAllPortals();
        }

        internal void WritePortalsIntoPersistence(ZonePersistenceFile data)
        {
            if (data == null) return;
            data.portals = new List<PortalPersistenceEntry>(_portals);
        }

        // ── Spawn / despawn ─────────────────────────────────────────────────────

        private void RespawnAllPortals()
        {
            DespawnAllPortalObjects();
            EnsurePortalsRoot();
            for (int i = 0; i < _portals.Count; i++)
                SpawnPortalObject(_portals[i]);
        }

        private void DespawnAllPortalObjects()
        {
            for (int i = 0; i < _portalObjects.Count; i++)
            {
                if (_portalObjects[i] != null)
                    DestroyPortalObject(_portalObjects[i]);
            }
            _portalObjects.Clear();
        }

        // Wraps Destroy / DestroyImmediate so the same code path works from
        // Play Mode AND from EditMode tests. Plain Destroy() throws
        // "Destroy may not be called from edit mode" when invoked outside Play.
        private static void DestroyPortalObject(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else                       DestroyImmediate(go);
        }

        private void SpawnPortalObject(PortalPersistenceEntry entry)
        {
            if (entry == null) return;
            EnsurePortalsRoot();
            var spec = new ZonePortalFactory.PortalSpawnSpec
            {
                worldPosition             = new Vector3(entry.sourceWorldX, entry.sourceWorldY, 0f),
                destinationZoneName       = entry.destinationZoneName,
                useDestinationZoneCenter  = entry.destinationUseZoneCenter,
                destinationWorldPosition  = new Vector2(entry.destinationWorldX, entry.destinationWorldY),
                activationRadius          = entry.activationRadius,
            };
            var go = ZonePortalFactory.Spawn(_portalsRoot.transform, spec, zoneManager);
            go.name = $"ZonePortal[{entry.portalId}]";
            _portalObjects.Add(go);
        }

        // ── Public API for tests + UI ───────────────────────────────────────────

        public int PortalCount => _portals.Count;

        // Snapshot returns the internal DTO type, so accessibility must match
        // the type's `internal` visibility — the test assembly has
        // [InternalsVisibleTo] for Valkur.Tests.EditMode, which is the only
        // out-of-assembly caller that needs this.
        /// <summary>Read-only snapshot for inspection / testing.</summary>
        internal IReadOnlyList<PortalPersistenceEntry> SnapshotPortals() => _portals.AsReadOnly();

        /// <summary>
        /// Add a portal record and spawn its runtime object. Returns the
        /// stable portalId assigned to the new portal — caller can use it to
        /// later invoke <see cref="RemovePortal"/>.
        /// </summary>
        internal string AddPortal(Vector3 sourceWorld, string destinationZoneName,
            bool useZoneCenter, Vector2 destinationWorld, float activationRadius)
        {
            var entry = new PortalPersistenceEntry
            {
                portalId                  = Guid.NewGuid().ToString("N").Substring(0, 12),
                sourceWorldX              = sourceWorld.x,
                sourceWorldY              = sourceWorld.y,
                destinationZoneName       = destinationZoneName ?? string.Empty,
                destinationUseZoneCenter  = useZoneCenter,
                destinationWorldX         = destinationWorld.x,
                destinationWorldY         = destinationWorld.y,
                activationRadius          = activationRadius,
            };
            _portals.Add(entry);
            SpawnPortalObject(entry);
            PersistZonesToDisk();
            return entry.portalId;
        }

        /// <summary>Remove a portal by id. Returns true if removed.</summary>
        public bool RemovePortal(string portalId)
        {
            if (string.IsNullOrEmpty(portalId)) return false;
            int idx = _portals.FindIndex(p => p != null && p.portalId == portalId);
            if (idx < 0) return false;
            _portals.RemoveAt(idx);
            // Match the runtime object by name (set in SpawnPortalObject).
            string targetName = $"ZonePortal[{portalId}]";
            for (int i = _portalObjects.Count - 1; i >= 0; i--)
            {
                if (_portalObjects[i] != null && _portalObjects[i].name == targetName)
                {
                    DestroyPortalObject(_portalObjects[i]);
                    _portalObjects.RemoveAt(i);
                }
            }
            PersistZonesToDisk();
            return true;
        }

        // ── Placement flow ──────────────────────────────────────────────────────

        public void BeginPlacePortalFlow()
        {
            // Cancel any conflicting flow first so the two never race.
            if (_isAddZoneFlowActive) CancelAddZoneFlow();

            _isPlacePortalActive       = true;
            _hasPendingPortalSource    = false;
            _pendingPortalSourceWorld  = Vector3.zero;
            _placePortalFlowStartedFrame = Time.frameCount;
            _ui?.SetPlacePortalMode(true);
            _ui?.SetStatus("Place Portal: click on the map to mark the portal source.");
        }

        public void CancelPlacePortalFlow()
        {
            _isPlacePortalActive = false;
            _hasPendingPortalSource = false;
            if (_portalSourcePreviewObject != null)
                _portalSourcePreviewObject.SetActive(false);
            _ui?.SetPlacePortalMode(false);
            _ui?.HidePortalDialog();
        }

        // ── UI handlers (forwarded into the placement flow above) ───────────────

        private void OnBeginPlacePortalFromUI() => BeginPlacePortalFlow();
        private void OnCancelPlacePortalFromUI() => CancelPlacePortalFlow();
        private void OnConfirmPlacePortalFromUI(string destinationZone, bool useCenter,
            Vector2 destWorld, float radius)
            => ConfirmPlacePortal(destinationZone, useCenter, destWorld, radius);

        internal void MarkPortalSourceAtCursor()
        {
            if (!_isPlacePortalActive) return;
            if (!TryGetCursorWorld(out var worldPos))
            {
                _ui?.SetStatus("Cannot mark portal source: cursor world position unavailable.");
                return;
            }

            _pendingPortalSourceWorld = worldPos;
            _hasPendingPortalSource   = true;

            // Default destination: the first available zone other than the
            // one the source landed in. The dialog overrides this on confirm.
            string defaultDest = ResolveDefaultPortalDestination(worldPos);
            _ui?.ShowPortalDialog(worldPos, defaultDest, ListZoneNamesForPortalDialog());
            _ui?.SetStatus($"Portal source at [{worldPos.x:0.##},{worldPos.y:0.##}] — pick a destination zone.");
        }

        public void ConfirmPlacePortal(string destinationZoneName, bool useZoneCenter,
            Vector2 destinationWorld, float activationRadius)
        {
            if (!_isPlacePortalActive || !_hasPendingPortalSource)
            {
                _ui?.SetStatus("Place Portal flow is not active.");
                return;
            }

            if (string.IsNullOrWhiteSpace(destinationZoneName))
            {
                _ui?.SetStatus("Place Portal failed: destination zone is required.");
                return;
            }

            string id = AddPortal(_pendingPortalSourceWorld, destinationZoneName,
                                   useZoneCenter, destinationWorld, activationRadius);
            CancelPlacePortalFlow();
            _ui?.SetStatus($"Portal placed (id {id}) → '{destinationZoneName}'.");
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private string ResolveDefaultPortalDestination(Vector3 sourceWorld)
        {
            if (zoneManager == null) return string.Empty;
            // Prefer the first zone that is NOT the one the source falls into,
            // so the default is something a user would actually pick.
            var snap = zoneManager.GetZonesSnapshot();
            string sourceZoneName = null;
            if (worldGridBuilder != null)
            {
                var tilemap = worldGridBuilder.GetTilemap(TilemapLayerSetup.TilemapLayer.Ground);
                if (tilemap != null)
                {
                    Vector3Int cell = tilemap.WorldToCell(sourceWorld);
                    if (zoneManager.TryGetZoneAtTile(new Vector2Int(cell.x, cell.y), out var sourceZone))
                        sourceZoneName = sourceZone.zoneName;
                }
            }
            for (int i = 0; i < snap.Length; i++)
            {
                if (!string.Equals(snap[i].zoneName, sourceZoneName, StringComparison.OrdinalIgnoreCase))
                    return snap[i].zoneName;
            }
            return snap.Length > 0 ? snap[0].zoneName : string.Empty;
        }

        private List<string> ListZoneNamesForPortalDialog()
        {
            var list = new List<string>();
            if (zoneManager == null) return list;
            var snap = zoneManager.GetZonesSnapshot();
            for (int i = 0; i < snap.Length; i++)
            {
                if (!string.IsNullOrEmpty(snap[i].zoneName))
                    list.Add(snap[i].zoneName);
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        private bool TryGetCursorWorld(out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return false;
            var screenMouse = Valkur.Core.Input.MouseInputManager.GetScreenMousePosition();
            worldPos = _mainCamera.ScreenToWorldPoint(screenMouse);
            worldPos.z = 0f;
            return true;
        }
    }
}
