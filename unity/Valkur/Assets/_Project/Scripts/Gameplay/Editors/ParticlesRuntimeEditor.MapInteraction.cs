using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;

namespace Valkur.Gameplay.VFX
{
    public partial class ParticlesRuntimeEditor : SingletonMonoBehaviour<ParticlesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // â”€â”€ Map interaction â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void HandleMapInteraction()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            var cam = Camera.main;
            if (cam == null) return;
            Vector3 worldPos = cam.ScreenToWorldPoint(Valkur.Core.Input.MouseInputManager.GetScreenMousePosition());
            worldPos.z = 0f;

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
            var go = SpawnEmitterAt(preset, worldPos);
            if (go == null) return;

            ExecutePersistedEdit($"Place {presetId}",
                () => { if (go != null) go.SetActive(true); },
                () => { if (go != null) go.SetActive(false); });

            SetStatus($"Placed {presetId} at ({worldPos.x:F1}, {worldPos.y:F1}).");
        }

        // â”€â”€ Delete (with confirm modal) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void RequestDeleteWithConfirm(GameObject instance)
        {
            string pid = ExtractPresetIdFromName(instance.name) ?? "(unknown)";
            ShowConfirm(
                $"Delete particle instance?\n<b>{instance.name}</b>\nPreset: {pid}",
                () => DeleteInstance(instance));
        }

        private void DeleteInstance(GameObject instance)
        {
            if (instance == null) return;
            Vector3 pos = instance.transform.position;
            string  pid = ExtractPresetIdFromName(instance.name);
            if (instance == _activeInstance) SetActiveInstance(null);

            // For undo, we re-spawn from the preset (the original GO is gone after destroy).
            ExecutePersistedEdit($"Delete {pid ?? "particle"}",
                () => { if (instance != null) { if (Application.isPlaying) Destroy(instance); else DestroyImmediate(instance); } },
                () =>
                {
                    if (string.IsNullOrEmpty(pid) || _catalog == null) return;
                    var preset = _catalog.GetById(pid);
                    if (preset != null) SpawnEmitterAt(preset, pos);
                });
        }

        // â”€â”€ Spawn helper (shared by click + drag + undo) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private GameObject SpawnEmitterAt(ParticlePresetDefinition preset, Vector3 worldPos)
        {
            if (preset == null) return null;
            var loader = FindObjectOfType<ParticleInstancesLoader>();
            Transform parent = loader != null ? loader.transform : null;

            var go = new GameObject($"PE_{preset.id}_{System.DateTime.UtcNow.Ticks}");
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(worldPos.x, worldPos.y, 0f);

            var emitter = go.AddComponent<ParticleEmitter>();
            float scale = preset.id != null && preset.id.StartsWith("portal_", System.StringComparison.Ordinal) ? 2f : 1f;
            emitter.ApplyPreset(preset, scale);
            return go;
        }
    }
}
