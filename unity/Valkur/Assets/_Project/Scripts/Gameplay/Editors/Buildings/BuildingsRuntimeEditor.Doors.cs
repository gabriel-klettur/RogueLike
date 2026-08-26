using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Buildings
{
    /// <summary>
    /// Door authoring for the F10 editor — Phase 2 of .github/BUILDING_DOORS_ROADMAP.md.
    ///
    /// A doorway is authored across TWO scopes and this file is careful to keep them apart,
    /// because conflating them is the one mistake that would quietly corrupt a catalog:
    ///
    ///   • WHERE THE DOORWAY IS (hasDoor, anchor, size) is a property of the ART and lives on
    ///     the <see cref="BuildingTemplateData"/> ScriptableObject. Editing it changes EVERY
    ///     placement of that template. The panel header says so, the status line repeats it,
    ///     and the asset is marked dirty so the change survives leaving Play Mode.
    ///   • WHERE IT LEADS (target overlay, spawn) is a property of THIS placement and lives in
    ///     <c>overrides.door</c>, written by the same SaveInstancesToJson every other
    ///     per-instance edit goes through.
    ///
    /// Template edits go on the undo stack WITHOUT forcing a JSON write, because no instance
    /// data changed; instance edits go through <c>ExecutePersistedEdit</c> like every other
    /// mutation in this editor, so they are undoable and on disk immediately.
    /// </summary>
    public partial class BuildingsRuntimeEditor
    {
        // ── Authoring steps ─────────────────────────────────────────────────────────
        // One press moves the doorway by 2 % of the building's own size. Small enough to
        // land on a drawn door on a 32 px-wide sprite, large enough that crossing a whole
        // facade is a dozen presses rather than fifty.
        private const float DOOR_ANCHOR_STEP = 0.02f;
        private const float DOOR_SIZE_STEP   = 0.02f;
        private const float DOOR_SIZE_MIN    = 0.04f;
        private const float DOOR_SIZE_MAX    = 1.00f;

        // ── UI refs (mapped from UIRefs in BuildUI) ─────────────────────────────────
        private GameObject      _doorSubPanel;
        private TextMeshProUGUI _doorStatusTmp;
        private Image           _doorHasDoorBtnImg;
        private TextMeshProUGUI _doorHasDoorBtnLabel;
        private TMP_InputField  _doorTargetField;
        private TMP_InputField  _doorSpawnXField;
        private TMP_InputField  _doorSpawnYField;
        private TextMeshProUGUI _doorAnchorXVal;
        private TextMeshProUGUI _doorAnchorYVal;
        private TextMeshProUGUI _doorSizeVal;

        // ── Panel edit buffer ───────────────────────────────────────────────────────
        // Field commits land here rather than on the building, so "Apply" writes the target
        // and the spawn as ONE undo entry. Committing each field straight through would put
        // a half-authored door (a target with last building's spawn) on disk in between.
        private string _doorPendingTarget = "";
        private float  _doorPendingSpawnX;
        private float  _doorPendingSpawnY;

        // World-space overlay drawing the doorway rect of the active building.
        private LineRenderer _doorOverlayLine;

        // ── Mode lifecycle ──────────────────────────────────────────────────────────

        private void OnDoorButtonClicked()
        {
            if (_mode == EditorMode.Door) ExitDoorMode();
            else                          SetMode(EditorMode.Door);
        }

        /// <summary>Show the flyout and sync it to whatever is selected. Called from SetMode.</summary>
        private void EnterDoorModeUi()
        {
            if (_doorSubPanel != null) _doorSubPanel.SetActive(true);
            LoadDoorPanelFromActive();
            RefreshDoorPanel();
        }

        /// <summary>
        /// Hide the flyout and drop the world overlay. <paramref name="setSelectMode"/> is
        /// false when SetMode is already switching away, which is what keeps the two from
        /// calling each other in a loop.
        /// </summary>
        private void ExitDoorMode(bool setSelectMode = true)
        {
            if (_doorSubPanel != null) _doorSubPanel.SetActive(false);
            DestroyDoorOverlay();
            if (setSelectMode && _mode == EditorMode.Door) SetMode(EditorMode.Select);
        }

        /// <summary>Door mode's map click: pick the building whose doorway the flyout edits.</summary>
        private void HandleDoorModeClick(Vector3 worldPos)
        {
            RecomputeHoverStack(worldPos);
            if (_hoveredBuilding == null)
            {
                Toast("No building here. Click one to edit its doorway.");
                return;
            }
            SetActiveBuilding(_hoveredBuilding);
            LoadDoorPanelFromActive();
            RefreshDoorPanel();
        }

        // ── Panel <-> data ──────────────────────────────────────────────────────────

        private void LoadDoorPanelFromActive()
        {
            var spec = _activeBuilding != null ? _activeBuilding.DoorSpec : null;
            _doorPendingTarget = spec != null ? spec.target : "";
            _doorPendingSpawnX = spec != null ? spec.spawnX : 0f;
            _doorPendingSpawnY = spec != null ? spec.spawnY : 0f;

            if (_doorTargetField != null) _doorTargetField.SetTextWithoutNotify(_doorPendingTarget);
            if (_doorSpawnXField != null) _doorSpawnXField.SetTextWithoutNotify(FormatCoord(_doorPendingSpawnX));
            if (_doorSpawnYField != null) _doorSpawnYField.SetTextWithoutNotify(FormatCoord(_doorPendingSpawnY));
        }

        private static string FormatCoord(float v)
            => v.ToString("0.###", CultureInfo.InvariantCulture);

        private void RefreshDoorPanel()
        {
            if (_doorStatusTmp == null) return;

            var b = _activeBuilding;
            var t = b != null ? b.Template : null;

            if (b == null || t == null)
            {
                _doorStatusTmp.text = "Click a building on the map.";
                SetDoorHasDoorLabel(false, interactable: false);
                if (_doorAnchorXVal != null) _doorAnchorXVal.text = "--";
                if (_doorAnchorYVal != null) _doorAnchorYVal.text = "--";
                if (_doorSizeVal    != null) _doorSizeVal.text    = "--";
                return;
            }

            string leadsTo = b.DoorSpec != null && b.DoorSpec.IsValid
                ? b.DoorSpec.target
                : "<i>nowhere</i>";

            _doorStatusTmp.text =
                $"<b>ID {b.InstanceId}</b>  ({t.name})\n" +
                $"Leads to: {leadsTo}\n" +
                (t.hasDoor
                    ? "<color=#8fd18f>Template declares a doorway.</color>"
                    : "<color=#d1a05a>Template has NO doorway — turn it on below.</color>");

            SetDoorHasDoorLabel(t.hasDoor, interactable: true);

            if (_doorAnchorXVal != null) _doorAnchorXVal.text = t.doorOffsetNormalized.x.ToString("0.00", CultureInfo.InvariantCulture);
            if (_doorAnchorYVal != null) _doorAnchorYVal.text = t.doorOffsetNormalized.y.ToString("0.00", CultureInfo.InvariantCulture);
            if (_doorSizeVal    != null) _doorSizeVal.text    = t.doorSizeNormalized.x.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private void SetDoorHasDoorLabel(bool on, bool interactable)
        {
            if (_doorHasDoorBtnLabel != null)
                _doorHasDoorBtnLabel.text = (on ? "[X]" : "[ ]") + " Has doorway";
            if (_doorHasDoorBtnImg != null)
                _doorHasDoorBtnImg.color = !interactable
                    ? EditorUIHelpers.BTN_NORMAL
                    : (on ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL);
        }

        // ── Field commits ───────────────────────────────────────────────────────────

        private void OnDoorTargetCommitted(string value)
        {
            _doorPendingTarget = (value ?? "").Trim();
        }

        private void OnDoorSpawnCommitted(string value, bool isX)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            {
                Toast($"'{value}' is not a number. Spawn coordinates are world units, e.g. 25.5");
                if (isX && _doorSpawnXField != null) _doorSpawnXField.SetTextWithoutNotify(FormatCoord(_doorPendingSpawnX));
                if (!isX && _doorSpawnYField != null) _doorSpawnYField.SetTextWithoutNotify(FormatCoord(_doorPendingSpawnY));
                return;
            }
            if (isX) _doorPendingSpawnX = parsed;
            else     _doorPendingSpawnY = parsed;
        }

        // ── Template scope ──────────────────────────────────────────────────────────

        private void ToggleTemplateHasDoor()
        {
            var t = _activeBuilding != null ? _activeBuilding.Template : null;
            if (t == null) { Toast("Select a building first."); return; }

            TrySetTemplateHasDoor(t, !t.hasDoor, out string message);
            Toast(message);
        }

        private void ApplyTemplateHasDoor(BuildingTemplateData t, bool value)
        {
            t.hasDoor = value;
            MarkTemplateDirty(t);
            ReapplyDoorsForTemplate(t);
            RefreshDoorPanel();
        }

        private void NudgeDoorAnchor(int dx, int dy)
        {
            var t = _activeBuilding != null ? _activeBuilding.Template : null;
            if (t == null) { Toast("Select a building first."); return; }

            Vector2 before = t.doorOffsetNormalized;
            Vector2 after  = new Vector2(
                Mathf.Clamp01(before.x + dx * DOOR_ANCHOR_STEP),
                Mathf.Clamp01(before.y + dy * DOOR_ANCHOR_STEP));
            if (after == before) return;

            _undo.Do($"Move doorway on {t.name}",
                () => ApplyTemplateAnchor(t, after, t.doorSizeNormalized),
                () => ApplyTemplateAnchor(t, before, t.doorSizeNormalized));
        }

        private void NudgeDoorSize(int delta)
        {
            var t = _activeBuilding != null ? _activeBuilding.Template : null;
            if (t == null) { Toast("Select a building first."); return; }

            Vector2 before = t.doorSizeNormalized;
            float   scalar = Mathf.Clamp(before.x + delta * DOOR_SIZE_STEP, DOOR_SIZE_MIN, DOOR_SIZE_MAX);
            // Width and height move together: a doorway is authored as "how much of the
            // facade", and two independent steppers for a rect nobody measures separately
            // is two chances to leave it lopsided.
            Vector2 after = new Vector2(scalar, Mathf.Clamp(before.y + delta * DOOR_SIZE_STEP, DOOR_SIZE_MIN, DOOR_SIZE_MAX));
            if (after == before) return;

            _undo.Do($"Resize doorway on {t.name}",
                () => ApplyTemplateAnchor(t, t.doorOffsetNormalized, after),
                () => ApplyTemplateAnchor(t, t.doorOffsetNormalized, before));
        }

        private void ApplyTemplateAnchor(BuildingTemplateData t, Vector2 offset, Vector2 size)
        {
            t.doorOffsetNormalized = offset;
            t.doorSizeNormalized   = size;
            MarkTemplateDirty(t);
            ReapplyDoorsForTemplate(t);
            RefreshDoorPanel();
        }

        /// <summary>
        /// Re-attach or re-place every live doorway belonging to <paramref name="t"/>.
        ///
        /// This is the step that makes template authoring visible at all: the anchor lives on
        /// a shared asset, so a change to it moves the doorway of every placement, and none of
        /// them would notice without being told.
        /// </summary>
        private void ReapplyDoorsForTemplate(BuildingTemplateData t)
        {
            if (t == null) return;
            var all = FindObjectsOfType<BuildingObject>();
            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (b == null || b.Template != t) continue;

                if (!t.hasDoor) { BuildingDoorFactory.Remove(b); continue; }
                if (b.DoorSpec != null && b.DoorSpec.IsValid) BuildingDoorFactory.TryAttach(b, b.DoorSpec);
            }
        }

        /// <summary>
        /// Persist a ScriptableObject edit made at runtime.
        ///
        /// SetDirty alone, never Undo.RecordObject: a bulk edit recorded on the GLOBAL editor
        /// undo stack is what reverted 193 building templates in memory to their empty
        /// creation state the first time anything popped that stack (see CLAUDE.md).
        /// </summary>
        private static void MarkTemplateDirty(BuildingTemplateData t)
        {
#if UNITY_EDITOR
            if (t == null) return;
            UnityEditor.EditorUtility.SetDirty(t);
#endif
        }

        // ── Instance scope ──────────────────────────────────────────────────────────

        private void ApplyDoorFromPanel()
        {
            TrySetDoor(_activeBuilding, _doorPendingTarget, _doorPendingSpawnX, _doorPendingSpawnY,
                       out string message);
            Toast(message);
        }

        private void ClearDoorOnActive()
        {
            TryClearDoor(_activeBuilding, out string message);
            Toast(message);
        }

        // ── Authoring seams ──────────────────────────────────────────────────
        // DevConsole lives in the SAME assembly, so `internal` is enough for it and for the
        // test suite. Both go through the SAME ExecutePersistedEdit the panel uses, so a door
        // authored from the console is undoable and on disk exactly like one authored by
        // clicking - there is no second write path to keep in step.

        /// <summary>
        /// Point one building's doorway at an overlay. Returns false with a human-readable
        /// reason when the combination cannot work.
        /// </summary>
        internal bool TrySetDoor(BuildingObject b, string target, float spawnX, float spawnY,
                                 out string message)
        {
            if (b == null)          { message = "Select a building first."; return false; }
            if (b.Template == null) { message = "That building has no template."; return false; }

            target = (target ?? "").Trim();
            if (string.IsNullOrEmpty(target))
            {
                message = "Set a target overlay first, e.g. house_interior_small.overlay.json";
                return false;
            }

            if (!b.Template.hasDoor)
            {
                message = $"'{b.Template.name}' has no doorway yet — turn 'Has doorway' on first.";
                return false;
            }

            if (!WorldTransitionService.IsOverlayLoadable(target))
            {
                // Refused rather than warned: a door authored against a file that does not
                // load is a door that strands the player, and it would be found by walking
                // into it rather than by reading the console.
                message = $"'{target}' is not a loadable overlay in StreamingAssets/Maps.";
                return false;
            }

            var after  = new BuildingDoorSpec { target = target, spawnX = spawnX, spawnY = spawnY };
            var before = b.DoorSpec?.Clone();

            ExecutePersistedEdit($"Set doorway on ID {b.InstanceId}",
                () => ApplyDoorSpec(b, after),
                () => ApplyDoorSpec(b, before));

            message = $"ID {b.InstanceId} now leads to '{after.target}'.";
            return true;
        }

        /// <summary>Remove one building's doorway destination, persisted and undoable.</summary>
        internal bool TryClearDoor(BuildingObject b, out string message)
        {
            if (b == null)          { message = "Select a building first."; return false; }
            if (b.DoorSpec == null) { message = "That building has no doorway to clear."; return false; }

            var before = b.DoorSpec.Clone();
            ExecutePersistedEdit($"Clear doorway on ID {b.InstanceId}",
                () => ApplyDoorSpec(b, null),
                () => ApplyDoorSpec(b, before));

            message = $"Doorway cleared on ID {b.InstanceId}.";
            return true;
        }

        /// <summary>
        /// Turn the doorway on or off for a TEMPLATE - every placement of that art.
        /// </summary>
        internal bool TrySetTemplateHasDoor(BuildingTemplateData t, bool value, out string message)
        {
            if (t == null) { message = "No template."; return false; }
            if (t.hasDoor == value)
            {
                message = $"'{t.name}' already {(value ? "has" : "has no")} doorway.";
                return true;
            }

            bool before = t.hasDoor;
            _undo.Do($"{(value ? "Add" : "Remove")} doorway on {t.name}",
                () => ApplyTemplateHasDoor(t, value),
                () => ApplyTemplateHasDoor(t, before));

            message = value
                ? $"'{t.name}' now has a doorway — on every placement of it."
                : $"'{t.name}' no longer has a doorway — removed from every placement.";
            return true;
        }

        /// <summary>
        /// Move and/or resize the doorway on a TEMPLATE. A null argument leaves that half alone.
        /// </summary>
        internal bool TrySetTemplateAnchor(BuildingTemplateData t, Vector2? offset, float? size,
                                           out string message)
        {
            if (t == null) { message = "No template."; return false; }

            Vector2 beforeOffset = t.doorOffsetNormalized;
            Vector2 beforeSize   = t.doorSizeNormalized;

            Vector2 afterOffset = offset.HasValue
                ? new Vector2(Mathf.Clamp01(offset.Value.x), Mathf.Clamp01(offset.Value.y))
                : beforeOffset;
            float   clamped     = size.HasValue ? Mathf.Clamp(size.Value, DOOR_SIZE_MIN, DOOR_SIZE_MAX) : 0f;
            Vector2 afterSize   = size.HasValue ? new Vector2(clamped, clamped) : beforeSize;

            if (afterOffset == beforeOffset && afterSize == beforeSize)
            {
                message = "Doorway anchor unchanged.";
                return true;
            }

            _undo.Do($"Set doorway anchor on {t.name}",
                () => ApplyTemplateAnchor(t, afterOffset, afterSize),
                () => ApplyTemplateAnchor(t, beforeOffset, beforeSize));

            message = $"'{t.name}' doorway at ({afterOffset.x:0.00}, {afterOffset.y:0.00}) " +
                      $"size {afterSize.x:0.00} — on every placement of it.";
            return true;
        }

        private void ApplyDoorSpec(BuildingObject b, BuildingDoorSpec spec)
        {
            if (b == null) return;
            // TryAttach owns BOTH halves of the write: it clones the spec onto the building
            // AND creates/removes the live doorway. Assigning b.DoorSpec here as well would
            // be a second, racing owner of the same field.
            BuildingDoorFactory.TryAttach(b, spec);
            LoadDoorPanelFromActive();
            RefreshDoorPanel();
        }

        // ── World overlay ───────────────────────────────────────────────────────────

        /// <summary>
        /// Draw the doorway rect of the active building while Door mode is open. Called every
        /// frame from Update, alongside the split line and the collider overlay.
        /// </summary>
        private void UpdateDoorOverlay()
        {
            bool wanted = _mode == EditorMode.Door
                          && _activeBuilding != null
                          && _activeBuilding.TryGetDoorWorldRect(out _);

            if (!wanted) { DestroyDoorOverlay(); return; }

            _activeBuilding.TryGetDoorWorldRect(out var r);
            EnsureDoorOverlay();

            const float z = -0.2f;
            _doorOverlayLine.SetPosition(0, new Vector3(r.xMin, r.yMin, z));
            _doorOverlayLine.SetPosition(1, new Vector3(r.xMax, r.yMin, z));
            _doorOverlayLine.SetPosition(2, new Vector3(r.xMax, r.yMax, z));
            _doorOverlayLine.SetPosition(3, new Vector3(r.xMin, r.yMax, z));

            bool live = _activeBuilding.HasUsableDoor;
            var colour = live ? new Color(0.55f, 0.95f, 0.55f, 1f) : new Color(0.95f, 0.78f, 0.35f, 1f);
            _doorOverlayLine.startColor = colour;
            _doorOverlayLine.endColor   = colour;
        }

        private void EnsureDoorOverlay()
        {
            if (_doorOverlayLine != null) return;

            var go = new GameObject("BuildingsEditor_DoorOverlay");
            go.transform.SetParent(transform, false);
            _doorOverlayLine = go.AddComponent<LineRenderer>();
            _doorOverlayLine.useWorldSpace  = true;
            _doorOverlayLine.loop           = true;
            _doorOverlayLine.positionCount  = 4;
            _doorOverlayLine.startWidth     = 0.05f;
            _doorOverlayLine.endWidth       = 0.05f;
            _doorOverlayLine.numCornerVertices = 0;
            // sharedMaterial, never material: assigning .material clones the shared asset
            // once per overlay and leaks it in EditMode tests.
            _doorOverlayLine.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            _doorOverlayLine.sortingLayerName = SortingConfig.LAYER_VFX;
            _doorOverlayLine.sortingOrder     = 60;
        }

        private void DestroyDoorOverlay()
        {
            if (_doorOverlayLine == null) return;
            var go = _doorOverlayLine.gameObject;
            _doorOverlayLine = null;
            if (Application.isPlaying) Destroy(go);
            else                       DestroyImmediate(go);
        }
    }
}
