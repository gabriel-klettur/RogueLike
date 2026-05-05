using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.VFX
{
    public partial class ParticlesRuntimeEditor : SingletonMonoBehaviour<ParticlesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // â”€â”€ Map interaction â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void HandleMapInteraction()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            var cam = Camera.main;
            if (cam == null) return;
            Vector3 worldPos = cam.ScreenToWorldPoint(Valkur.Core.Input.MouseInputManager.GetScreenMousePosition());
            worldPos.z = 0f;

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            // Update hover state every frame so the outline follows the cursor.
            _hoveredInstance = overUi ? null : HitTestEmitter(worldPos);

            if (overUi) return;

            // RMB drag to move an existing instance.
            if (_dragging && _dragTarget != null)
            {
                _dragTarget.transform.position = worldPos + _dragOffset;
                if (Valkur.Core.Input.MouseInputManager.WasRightMouseButtonReleasedThisFrame())
                {
                    var moved = _dragTarget;
                    Vector3 startPos = _dragStartWorldPos;
                    Vector3 endPos   = moved.transform.position;
                    _dragging = false;
                    _dragTarget = null;
                    if (Vector3.Distance(startPos, endPos) > 0.001f)
                    {
                        ExecutePersistedEdit("Move particle",
                            () => { if (moved != null) moved.transform.position = endPos; },
                            () => { if (moved != null) moved.transform.position = startPos; });
                    }
                }
                return;
            }

            // LMB click â€” Place / Delete / Select.
            if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonPressedThisFrame())
            {
                if (_mode == EditorMode.Place && !string.IsNullOrEmpty(_selectedPresetId))
                {
                    SpawnFromMapClick(_selectedPresetId, worldPos);
                }
                else if (_mode == EditorMode.Delete)
                {
                    var hit = HitTestEmitter(worldPos);
                    if (hit != null) RequestDeleteWithConfirm(hit);
                }
                else // Select
                {
                    var hit = HitTestEmitter(worldPos);
                    SetActiveInstance(hit);
                }
            }

            // RMB on Select to start moving the picked instance.
            if (Valkur.Core.Input.MouseInputManager.WasRightMouseButtonPressedThisFrame() && _mode == EditorMode.Select)
            {
                var hit = HitTestEmitter(worldPos);
                if (hit != null)
                {
                    _dragTarget = hit;
                    _dragging   = true;
                    _dragStartWorldPos = hit.transform.position;
                    _dragOffset = hit.transform.position - worldPos;
                    SetActiveInstance(hit);
                }
            }
        }

        private void SetActiveInstance(GameObject instance)
        {
            _activeInstance = instance;
            ShowInstanceProperties(instance);
        }

        // Hit-test a ParticleEmitter under the cursor. Walks up parents because
        // the emitter is on the same GO that holds a ParticleSystem child.
        private GameObject HitTestEmitter(Vector3 worldPos)
        {
            var col = Physics2D.OverlapCircle(worldPos, 0.5f);
            if (col != null)
            {
                var emitter = col.GetComponentInParent<ParticleEmitter>();
                if (emitter != null) return emitter.gameObject;
            }
            // Fallback: nearest ParticleEmitter within radius.
            var all = FindObjectsOfType<ParticleEmitter>();
            float bestSqr = 0.6f * 0.6f;
            GameObject best = null;
            foreach (var em in all)
            {
                if (em == null) continue;
                float sqr = (em.transform.position - worldPos).sqrMagnitude;
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    best = em.gameObject;
                }
            }
            return best;
        }

        // â”€â”€ Place â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void SpawnFromMapClick(string presetId, Vector3 worldPos)
        {
            if (string.IsNullOrEmpty(presetId) || _catalog == null) return;
            var preset = _catalog.GetById(presetId);
            if (preset == null)
            {
                SetStatus($"Spawn failed: preset '{presetId}' not in catalog.");
                return;
            }
            // Reject finite (one-shot) presets — they cannot be placed as persistent decorations.
            if (preset.vfx != null && !preset.vfx.loops)
            {
                SetStatus($"'{presetId}' is one-shot (loops=false); cannot be placed as persistent decoration.");
                return;
            }
            var go = SpawnEmitterAt(preset, worldPos);
            if (go == null) return;

            ExecutePersistedEdit($"Place {presetId}",
                () => { if (go != null) go.SetActive(true); },
                () => { if (go != null) go.SetActive(false); });

            SetStatus($"Placed {presetId} at ({worldPos.x:F1}, {worldPos.y:F1}).");
        }

        // â”€â”€ Delete (with confirm modal) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static string GetPresetIdFromGo(GameObject go)
        {
            if (go == null) return null;
            var identity = go.GetComponent<PersistedParticleInstance>();
            if (identity != null && !string.IsNullOrEmpty(identity.PresetId))
                return identity.PresetId;
            return ExtractPresetIdFromName(go.name);
        }

        private void RequestDeleteWithConfirm(GameObject instance)
        {
            string pid = GetPresetIdFromGo(instance) ?? "(unknown)";
            ShowConfirm(
                $"Delete particle instance?\n<b>{instance.name}</b>\nPreset: {pid}",
                () => DeleteInstance(instance));
        }

        private void DeleteInstance(GameObject instance)
        {
            if (instance == null) return;
            Vector3 pos = instance.transform.position;
            string  pid = GetPresetIdFromGo(instance);
            if (instance == _activeInstance) SetActiveInstance(null);

            // For undo, we re-spawn from the preset (the original GO is gone after destroy).
            ExecutePersistedEdit($"Delete {pid ?? "particle"}",
                () => { if (instance != null) SafeDestroy.Of(instance); },
                () =>
                {
                    if (string.IsNullOrEmpty(pid) || _catalog == null) return;
                    var preset = _catalog.GetById(pid);
                    if (preset != null) SpawnEmitterAt(preset, pos);
                });
        }

        // â”€â”€ Spawn helper (shared by click + drag + undo) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private GameObject SpawnEmitterAt(ParticlePresetDefinition preset, Vector3 worldPos, float scaleMultiplier = -1f)
        {
            if (preset == null) return null;
            var loader = FindObjectOfType<ParticleInstancesLoader>();
            Transform parent = loader != null ? loader.transform : null;

            float scale = scaleMultiplier > 0f
                ? scaleMultiplier
                : (preset.id != null && preset.id.StartsWith("portal_", System.StringComparison.Ordinal) ? 2f : 1f);

            var go = new GameObject($"PE_{preset.id}");
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);

            // Attach the identity component — marks this as an editor-owned persisted emitter.
            var identity = go.AddComponent<PersistedParticleInstance>();
            identity.Initialize(preset.id, scale);

            var emitter = go.AddComponent<ParticleEmitter>();
            emitter.ApplyPreset(preset, scale);
            return go;
        }

        // -- Delete all in zone (double-confirm via two sequential modals) ----------

        // First modal: summary warning with zone name and instance count.
        // Second modal: final confirm before executing (same modal pattern reused).
        private void RequestDeleteAllInZoneWithConfirm()
        {
            var zm      = FindObjectOfType<ZoneManager>();
            string zone = zm != null ? zm.CurrentZone : "Lobby";

            var all = FindObjectsOfType<ParticleEmitter>();
            int count = 0;
            foreach (var em in all)
            {
                if (em == null || !em.gameObject.activeInHierarchy) continue;
                if (IsPreviewEmitter(em.gameObject)) continue;
                string emZone = ResolveZoneName(zm, em.transform.position);
                if (string.Equals(emZone, zone, System.StringComparison.OrdinalIgnoreCase))
                    count++;
            }

            if (count == 0)
            {
                SetStatus($"No particle instances in zone '{zone}'.");
                return;
            }

            // First confirmation modal.
            ShowConfirm(
                $"Delete <b>{count}</b> particle instances in zone <b>{zone}</b>?\n\nThis action can be undone with Undo.",
                () =>
                {
                    // Second confirmation modal (final).
                    ShowConfirm(
                        $"CONFIRM DELETE: <b>{count}</b> instances in <b>{zone}</b>.\nContinue?",
                        () => DeleteAllInZone(zone));
                });
        }

        private void DeleteAllInZone(string zoneName)
        {
            var zm  = FindObjectOfType<ZoneManager>();
            var all = FindObjectsOfType<ParticleEmitter>();

            var targets = new System.Collections.Generic.List<(GameObject go, string pid, Vector3 pos)>();
            foreach (var em in all)
            {
                if (em == null || !em.gameObject.activeInHierarchy) continue;
                if (IsPreviewEmitter(em.gameObject)) continue;
                string emZone = ResolveZoneName(zm, em.transform.position);
                if (!string.Equals(emZone, zoneName, System.StringComparison.OrdinalIgnoreCase)) continue;
                string pid = GetPresetIdFromGo(em.gameObject);
                targets.Add((em.gameObject, pid, em.transform.position));
            }

            if (targets.Count == 0)
            {
                SetStatus("No particle instances found in zone to delete.");
                return;
            }

            foreach (var (go, _, _) in targets)
            {
                if (go == _activeInstance) SetActiveInstance(null);
                if (go == _hoveredInstance) _hoveredInstance = null;
            }

            ExecutePersistedEdit($"Delete all in zone ({zoneName})",
                () =>
                {
                    foreach (var (go, _, _) in targets)
                        if (go != null) SafeDestroy.Of(go);
                },
                () =>
                {
                    foreach (var (_, pid, pos) in targets)
                    {
                        if (string.IsNullOrEmpty(pid) || _catalog == null) continue;
                        var preset = _catalog.GetById(pid);
                        if (preset != null) SpawnEmitterAt(preset, pos);
                    }
                });

            SetStatus($"Deleted {targets.Count} instance(s) in zone '{zoneName}'.");
        }

        // -- Delete selected instance from Properties panel -------------------------

        private void RequestDeleteSelectedInstanceWithConfirm()
        {
            if (_activeInstance == null)
            {
                SetStatus("No instance selected. Click an emitter on the map first.");
                return;
            }
            RequestDeleteWithConfirm(_activeInstance);
        }

        // Skip off-screen preview emitters owned by ParticlePreviewService — their
        // GameObjects are named "PPrev_Emitter_*" and live as children of this
        // editor's transform, NOT under the world. Without this filter, delete-all-
        // in-zone destroys them (DetectZone falls back to currentZone for off-world
        // positions) and the preview service then NREs each frame.
        private static bool IsPreviewEmitter(GameObject go)
        {
            return go != null && go.name != null &&
                   go.name.StartsWith("PPrev_", System.StringComparison.Ordinal);
        }
    }
}
