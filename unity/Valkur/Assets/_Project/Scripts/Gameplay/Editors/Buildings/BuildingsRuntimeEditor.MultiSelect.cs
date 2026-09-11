using System.Collections.Generic;
using UnityEngine;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Buildings
{
    /// <summary>
    /// The seam the cross-domain Selection tool reaches this editor through.
    ///
    /// <para>It is a PARTIAL of the editor rather than a helper beside it, and that is the
    /// whole reason this file is small: a partial shares the class's private members, so the
    /// tool reaches <c>GetCachedBuildings</c>, <c>NextInstanceId</c>, <c>Snapshot</c> and
    /// <c>SpawnFromEntry</c> without a single existing line changing visibility. The
    /// alternative — widening a dozen members to <c>internal</c> — would have made every one
    /// of them look like public API to the next reader.</para>
    ///
    /// <para>THESE PRIMITIVES DO NOT RECORD UNDO, and that is deliberate. A group operation
    /// spans three domains and has to be ONE undo step, so the Selection tool owns the
    /// <c>UndoStack</c> entry and calls these as its do/undo bodies. Routing them through
    /// <c>ExecutePersistedEdit</c> instead would push three entries for one gesture and a
    /// single undo would leave two thirds of the group moved.</para>
    ///
    /// <para>DELETION IS <c>SetActive(false)</c>, NEVER <c>Destroy</c> — the convention this
    /// editor already uses. <c>SaveInstancesToJson</c> scans with the default
    /// <c>FindObjectsOfType</c>, which skips inactive objects, so a deactivated building
    /// leaves the file on the next write and comes back exactly as it was on undo. (The
    /// Particles editor is the opposite and its own seam records why.)</para>
    /// </summary>
    public partial class BuildingsRuntimeEditor
    {
        /// <summary>Every placed building the tool may select. Snapshot, never the cache.</summary>
        internal void MultiSelectCollect(List<BuildingObject> buffer)
        {
            if (buffer == null) return;
            buffer.Clear();
            var all = GetCachedBuildings();
            if (all == null) return;
            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (b == null || !b.gameObject.activeInHierarchy) continue;
                buffer.Add(b);
            }
        }

        /// <summary>
        /// The building's drawn footprint, which is what the tool hit-tests and boxes.
        /// Falls back to a small square on a building whose renderers are not built yet, so a
        /// mid-load placement is still clickable rather than silently unreachable.
        /// </summary>
        internal bool MultiSelectTryGetRect(BuildingObject b, out Rect rect)
        {
            rect = default;
            if (b == null) return false;
            if (b.TryGetWorldRect(out rect) && rect.width > 0f && rect.height > 0f) return true;
            var p = b.transform.position;
            rect = new Rect(p.x - 0.5f, p.y, 1f, 1f);
            return true;
        }

        /// <summary>
        /// Place a building at <paramref name="worldPos"/> (its ground line, matching
        /// <c>PlaceBuilding</c>). Re-sorts and re-places the doorway for the same reason the
        /// editor's own drag commit does — the door is a child, so it travels with the
        /// building, but its rect is derived from the world bounds and has to be rebuilt.
        /// </summary>
        internal void MultiSelectApplyMove(BuildingObject b, Vector3 worldPos)
        {
            if (b == null) return;
            b.transform.position = worldPos;
            b.ZoneName = DetectZoneAt(worldPos);
            b.RefreshSorting();
            BuildingDoorFactory.RefreshGeometry(b);
        }

        /// <summary>Deactivate (delete) or reactivate (undo). Never destroys — see the class summary.</summary>
        internal void MultiSelectSetAlive(BuildingObject b, bool alive)
        {
            if (b == null) return;
            b.gameObject.SetActive(alive);
            if (!alive && _activeBuilding == b)
            {
                _activeBuilding = null;
                _propertiesMode = PropertiesMode.None;
            }
            InvalidateBuildingCache();
        }

        /// <summary>
        /// Duplicate one building at <paramref name="worldPos"/>, carrying everything a
        /// placement is. It goes through the clipboard's own <c>Snapshot</c> +
        /// <c>SpawnFromEntry</c> pair rather than a second copier, because that pair is what
        /// <c>BuildingsClipboardTests</c> holds against the serializer's field list — a
        /// private copy here would be the half that silently stops carrying the next field.
        /// </summary>
        internal BuildingObject MultiSelectDuplicate(BuildingObject src, Vector3 worldPos)
        {
            if (src == null || src.Template == null) return null;
            CacheBuildingLoader();
            var entry = Snapshot(src, src.transform.position);
            var copy  = SpawnFromEntry(entry, worldPos, NextInstanceId());
            InvalidateBuildingCache();
            return copy;
        }

        /// <summary>Drop a duplicate the tool is rolling back. Destroy is correct here: the
        /// object never reached the file, so there is nothing for a deactivation to preserve.</summary>
        internal void MultiSelectDiscardDuplicate(BuildingObject copy)
        {
            if (copy == null) return;
            _colliderInstanceStore.Remove(copy.InstanceId);
            copy.gameObject.SetActive(false);
            Destroy(copy.gameObject);
            InvalidateBuildingCache();
        }

        /// <summary>
        /// Snapshot a building BY VALUE for the cross-domain clipboard, and rebuild one from
        /// that snapshot.
        ///
        /// <para>These reuse this editor's own <c>Snapshot</c> / <c>SpawnFromEntry</c> pair
        /// rather than a second copier. That pair is what <c>BuildingsClipboardTests</c> holds
        /// against the serializer's field list — a private copy here would be the half that
        /// silently stops carrying the next field a placement grows, which is exactly the
        /// failure "copy everything a placement is" exists to prevent.</para>
        ///
        /// <para>The token is returned as <see cref="object"/> because <c>BuildingClipboardEntry</c>
        /// is private to this class: the tool's adapter lives in another type and only ever
        /// hands the token back here.</para>
        /// </summary>
        internal object MultiSelectCaptureForClipboard(BuildingObject b)
        {
            if (b == null || b.Template == null) return null;
            // Anchor at the building's own position: the group's relative layout is the tool's
            // business, not this editor's, so every entry is captured with a zero offset.
            return Snapshot(b, b.transform.position);
        }

        internal BuildingObject MultiSelectSpawnFromClipboard(object token, Vector3 worldPos)
        {
            if (!(token is BuildingClipboardEntry entry) || entry.Template == null) return null;
            CacheBuildingLoader();
            var copy = SpawnFromEntry(entry, worldPos, NextInstanceId());
            InvalidateBuildingCache();
            return copy;
        }

        /// <summary>The drawn sprites of a live building, for the paste ghost. Captured at COPY
        /// time from the real renderers rather than rebuilt from the template: a building is
        /// sliced into a footprint half and a canopy half at load, and re-slicing an atlas page
        /// costs 20 ms a call — the single most expensive thing this project ever measured.</summary>
        internal void MultiSelectCollectGhostSprites(BuildingObject b, List<SpriteRenderer> buffer)
        {
            if (b == null || buffer == null) return;
            b.GetComponentsInChildren(true, buffer);
        }

        /// <summary>
        /// Make <paramref name="b"/> this editor's active building, so opening the editor from
        /// somewhere else lands ON the thing the author double-clicked rather than merely
        /// opening a panel they then have to hunt in.
        ///
        /// <para>Called AFTER Activate, never before: activation seeds the editor's own mode
        /// and inspector, and a selection written first is overwritten by it.</para>
        /// </summary>
        internal void MultiSelectFocus(BuildingObject b)
        {
            if (b == null) return;
            _selection.Clear();
            _selection.Add(b);
            SetActiveBuilding(b);
        }

        /// <summary>Write the buildings file. The tool calls this once per group operation.</summary>
        internal void MultiSelectPersist()
        {
            MarkInstanceDataDirty();
            PersistDirtyInstanceChanges("Selection tool", force: true);
        }
    }
}
