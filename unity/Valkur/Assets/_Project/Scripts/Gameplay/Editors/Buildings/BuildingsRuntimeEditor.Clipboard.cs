using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Buildings
{
    /// <summary>
    /// Ctrl+C / Ctrl+V on placed buildings: copy the selection, paste a duplicate under the
    /// cursor. With a group selected, the whole group — its relative layout kept.
    ///
    /// <para>WHAT IS COPIED IS "EVERYTHING A PLACEMENT IS", and that list is not a judgement
    /// call — it is exactly the set of fields <c>SaveInstancesToJson</c> writes into one
    /// record: the template, the scale and split-ratio overrides, both sorting layers, the
    /// collider scope, the interactable flag, the doorway and the per-instance collision grid.
    /// Copying a subset produces a duplicate that looks identical and behaves differently,
    /// which is the failure nobody reports because both halves look right. Anything the
    /// serializer grows must be added here too, and <c>BuildingsClipboardTests</c> reads the
    /// serializer's own source for the override keys so a new one cannot be forgotten in
    /// silence.</para>
    ///
    /// <para>THE DOORWAY TRAVELS WITH THE COPY, deliberately.
    /// <see cref="BuildingDoorSpec"/>'s own summary already states the rule — the destination
    /// "travels with the building when it is moved, duplicated or deleted" — so a paste that
    /// dropped it would be the only duplication path in the project quietly disagreeing with
    /// that. Two front doors into one interior is a thing an author can see and clear; a copy
    /// that silently lost its destination is not.</para>
    ///
    /// <para>THE CLIPBOARD HOLDS SNAPSHOTS, NOT THE SOURCE OBJECTS. A live reference would
    /// paste whatever the source has become — or throw once it is deleted — so the copy is
    /// taken by value at Ctrl+C, the door spec is <see cref="BuildingDoorSpec.Clone"/>d and the
    /// collision grid goes through <c>CloneGrid</c>, for the same reason those two carry clone
    /// helpers at all: a shared instance lets an edit on the original rewrite data the
    /// clipboard still considers pristine. The one live reference kept is the
    /// <see cref="BuildingTemplateData"/>, which is a shared catalogue asset by design.</para>
    ///
    /// <para>A GROUP IS STORED AS OFFSETS FROM ITS ANCHOR. The anchor is the primary (the
    /// last entry, matching <see cref="BuildingSelectionSet"/>'s order) and it carries a zero
    /// offset; every other entry carries <c>position - anchor.position</c>. The paste lands
    /// the anchor under the cursor and the rest fall into place, so a row of six lamps copied
    /// from one street comes down as the same row on the next. Storing absolute positions
    /// instead would have made the paste land where the ORIGINALS stood, i.e. on top of them.</para>
    /// </summary>
    public partial class BuildingsRuntimeEditor
    {
        /// <summary>One copied placement, by value.</summary>
        private sealed class BuildingClipboardEntry
        {
            public BuildingTemplateData Template;
            public Vector2Int           ScaleOverride;
            public float                SplitRatioOverride;
            public int                  ZBottom;
            public int                  ZTop;
            public string               ColliderScopeOverride;
            public int                  InteractableOverride;
            public BuildingDoorSpec     Door;
            public ColliderGridData     CollisionOverride;

            /// <summary>Where this one sits relative to the group's anchor. Zero on the
            /// anchor itself and on any single-building copy.</summary>
            public Vector3 Offset;

            /// <summary>
            /// The source's drawn world height, measured at copy time.
            ///
            /// <para>It is what lets the paste land the anchor's VISUAL CENTRE on the
            /// cursor: a <see cref="BuildingObject"/>'s transform sits on its ground line
            /// (<c>TryGetWorldRect</c> returns <c>yMin = pos.y</c>), so pasting at the raw
            /// cursor position puts the building's feet there and the whole sprite appears
            /// shoved upward — the same correction the picker's drag-drop already makes.
            /// MEASURED rather than derived from <c>originalScale</c>, because a copied
            /// building may carry a scale override and a copy of a half-size house would
            /// otherwise be offset by half of the FULL size.</para>
            /// </summary>
            public float SourceWorldHeight;
        }

        /// <summary>Anchor LAST, so <c>_clipboard[_clipboard.Count - 1]</c> is the building
        /// the cursor lands on. Empty until the first Ctrl+C.</summary>
        private readonly List<BuildingClipboardEntry> _clipboard = new List<BuildingClipboardEntry>();

        /// <summary>True once something has been copied — read by the paste guard and by the
        /// tests, so "is there anything to paste" has one answer.</summary>
        internal bool HasClipboard => _clipboard.Count > 0 && _clipboard[_clipboard.Count - 1].Template != null;

        internal int ClipboardCount => _clipboard.Count;

        // ──────────────────────────────────────────────────────────────────────────
        //  COPY
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>Snapshot the selection — the group when there is one, else the active
        /// building. Overwrites any previous copy.</summary>
        internal bool CopyActiveBuilding()
        {
            var group = GroupTargets();
            if (group.Count == 0)            { Toast("Nothing to copy — select a building first."); return false; }

            var anchor = group[group.Count - 1];
            if (anchor == null || anchor.Template == null) { Toast("That building has no template to copy."); return false; }

            var entries = new List<BuildingClipboardEntry>(group.Count);
            for (int i = 0; i < group.Count; i++)
            {
                var b = group[i];
                if (b == null || b.Template == null) continue;   // a templateless member is skipped, not fatal
                entries.Add(Snapshot(b, anchor.transform.position));
            }
            if (entries.Count == 0) { Toast("That building has no template to copy."); return false; }

            _clipboard.Clear();
            _clipboard.AddRange(entries);

            Toast(entries.Count == 1
                ? $"Copied ID {anchor.InstanceId} (#{anchor.Template.templateId} {anchor.Template.name}) — Ctrl+V to paste."
                : $"Copied {entries.Count} buildings (anchor ID {anchor.InstanceId}) — Ctrl+V to paste the group.");
            return true;
        }

        private BuildingClipboardEntry Snapshot(BuildingObject b, Vector3 anchorPos)
        {
            float height = b.TryGetWorldRect(out var rect) && rect.height > 0f
                ? rect.height
                : (b.Template.originalScale.y > 0 ? b.Template.originalScale.y / BUILDING_PPU : 0f);

            _colliderInstanceStore.TryGetValue(b.InstanceId, out var storedGrid);
            bool isCU = string.Equals(b.EffectiveColliderScope, "CU", StringComparison.OrdinalIgnoreCase);

            return new BuildingClipboardEntry
            {
                Template              = b.Template,
                ScaleOverride         = b.ScaleOverride,
                SplitRatioOverride    = b.SplitRatioOverride,
                ZBottom               = b.ZBottom,
                ZTop                  = b.ZTop,
                ColliderScopeOverride = b.ColliderScopeOverride,
                InteractableOverride  = b.InteractableOverride,
                Door                  = b.DoorSpec?.Clone(),
                // A CG grid belongs to the IMAGE, so the duplicate inherits it by sharing the
                // template. Copying it per instance would fork one shared edit into two
                // private ones that drift apart on the next paint.
                CollisionOverride     = isCU ? CloneGrid(storedGrid) : null,
                Offset                = b.transform.position - anchorPos,
                SourceWorldHeight     = height,
            };
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  PASTE
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>Paste the clipboard under the mouse. Refuses rather than guessing a
        /// position when the pointer is not over the map.</summary>
        internal bool PasteClipboardAtCursor()
        {
            if (!HasClipboard) { Toast("Clipboard empty — copy a building with Ctrl+C first."); return false; }

            var cam = Camera.main;
            if (cam == null) { Toast("No camera — cannot resolve the paste position."); return false; }

            // A paste has to land SOMEWHERE, and the only honest answer while the pointer sits
            // over a panel is "not here": the cursor's world projection is behind the UI, where
            // the author cannot see what arrived and would press Ctrl+V again.
            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            {
                Toast("Move the pointer over the map to paste.");
                return false;
            }

            Vector3 worldPos = cam.ScreenToWorldPoint(
                Valkur.Core.Input.MouseInputManager.GetScreenMousePosition());
            worldPos.z = 0f;
            worldPos.y -= _clipboard[_clipboard.Count - 1].SourceWorldHeight * 0.5f;

            return PasteClipboardAt(worldPos);
        }

        /// <summary>
        /// The positioned half, so the console and the tests can paste without a pointer.
        /// <paramref name="anchorPos"/> is the ANCHOR's ground line, matching
        /// <c>PlaceBuilding</c>; the rest of a group lands at its stored offsets.
        /// </summary>
        internal bool PasteClipboardAt(Vector3 anchorPos)
        {
            if (!HasClipboard) { Toast("Clipboard empty — copy a building with Ctrl+C first."); return false; }

            var entries = new List<BuildingClipboardEntry>(_clipboard);
            CacheBuildingLoader();
            int firstId = NextInstanceId();

            var created = new List<BuildingObject>(entries.Count);
            ExecutePersistedEdit(entries.Count == 1
                    ? $"Paste #{entries[0].Template.templateId}"
                    : $"Paste {entries.Count} buildings",
                () =>
                {
                    created.Clear();
                    for (int i = 0; i < entries.Count; i++)
                    {
                        int newId = firstId + i;
                        var bObj = SpawnFromEntry(entries[i], anchorPos + entries[i].Offset, newId);
                        created.Add(bObj);
                    }
                    InvalidateBuildingCache();

                    // The pasted group becomes the selection, its anchor primary — so a
                    // second Ctrl+V or a Delete acts on what just arrived, not on what was
                    // copied. In Simple scope the reconcile pass keeps only the anchor.
                    _selection.Clear();
                    for (int i = 0; i < created.Count; i++)
                        if (created[i] != null) _selection.Add(created[i]);
                    SetActiveBuilding(created.Count > 0 ? created[created.Count - 1] : null);

                    Toast(created.Count == 1
                        ? $"Pasted #{entries[0].Template.templateId} at ({anchorPos.x:F1}, {anchorPos.y:F1}) -> ID {firstId}"
                        : $"Pasted {created.Count} buildings at ({anchorPos.x:F1}, {anchorPos.y:F1}) -> IDs {firstId}..{firstId + created.Count - 1}");
                },
                () =>
                {
                    for (int i = 0; i < created.Count; i++)
                    {
                        if (created[i] == null) continue;
                        created[i].gameObject.SetActive(false);
                        Destroy(created[i].gameObject);
                        // Drop the grid with the building. Left behind, it is a private
                        // collision override keyed to an id NextInstanceId can hand out again,
                        // so the next unrelated placement would inherit these painted cells.
                        _colliderInstanceStore.Remove(firstId + i);
                    }
                    created.Clear();
                    _selection.Clear();
                    InvalidateBuildingCache();
                    if (_activeBuilding == null) RefreshInspector();
                });

            return created.Count > 0;
        }

        /// <summary>Build one placed building from a snapshot. Mirrors <c>PlaceBuilding</c>'s
        /// construction step for step, with the overrides applied on top.</summary>
        private BuildingObject SpawnFromEntry(BuildingClipboardEntry entry, Vector3 worldPos, int newId)
        {
            var go = new GameObject($"Building_{newId}_{entry.Template.name}");
            go.transform.SetParent(_buildingsRoot, worldPositionStays: false);
            go.transform.position = worldPos;
            go.layer = 11; // World
            var bObj = go.AddComponent<BuildingObject>();
            bObj.ZoneName              = DetectZoneAt(worldPos);
            bObj.InstanceId            = newId;
            bObj.ColliderScopeOverride = entry.ColliderScopeOverride;
            bObj.ZBottom               = entry.ZBottom;
            bObj.ZTop                  = entry.ZTop;
            bObj.InteractableOverride  = entry.InteractableOverride;
            // Apply() builds the renderers and re-sorts, so it runs AFTER the fields it
            // does not read and BEFORE anything that measures the result.
            bObj.Apply(entry.Template, entry.ScaleOverride, entry.SplitRatioOverride);

            var newRenderers = bObj.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < newRenderers.Length; i++)
                if (newRenderers[i] != null)
                    newRenderers[i].enabled = _buildingsVisible;

            // The grid is keyed by INSTANCE id, so the duplicate needs its own copy under
            // the new key — sharing the source's entry would make one paint stroke edit
            // both buildings, which is exactly what CU scope means it must not do.
            if (entry.CollisionOverride != null)
                _colliderInstanceStore[newId] = CloneGrid(entry.CollisionOverride);

            RefreshCollisionFor(bObj);

            // BuildingDoorFactory owns BOTH halves of a door write — the spec on the
            // building and the live trigger — so the paste goes through it rather than
            // assigning DoorSpec, which would leave a destination with no doorway.
            if (entry.Door != null)
                BuildingDoorFactory.TryAttach(bObj, entry.Door);

            // Register with the loader so a map-slot switch destroys this via
            // ClearSpawned() instead of leaving an orphan the next save bleeds into the
            // wrong slot's JSON.
            _buildingLoader?.RegisterPlacedBuilding(bObj);
            return bObj;
        }
    }
}
