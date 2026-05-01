using System;
using System.Collections.Generic;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.VFX
{
    public partial class ParticlesRuntimeEditor : SingletonMonoBehaviour<ParticlesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        private const string INSTANCES_REL_PATH = "StreamingAssets/Particles/particles_instances.json";

        // Storage backend — defaults to file; tests inject InMemoryParticleInstanceStore.
        private IParticleInstanceStore _instanceStore;

        /// <summary>
        /// Injects a custom store. Must be called before any save/load operation.
        /// Used by tests to avoid disk access.
        /// </summary>
        public void SetInstanceStore(IParticleInstanceStore store)
        {
            _instanceStore = store;
        }

        private IParticleInstanceStore GetOrCreateStore()
        {
            if (_instanceStore == null)
                _instanceStore = new FileParticleInstanceStore();
            return _instanceStore;
        }

        // ── Dirty tracking ────────────────────────────────────────────────────

        private void MarkInstanceDataDirty()
        {
            _hasUnsavedInstanceChanges = true;
        }

        private void PersistDirtyInstanceChanges(string reason = null, bool force = false)
        {
            if ((!_hasUnsavedInstanceChanges && !force) || _isPersistingInstanceChanges) return;
            SaveInstancesToJson();
        }

        /// <summary>
        /// Wraps an editor edit so it pushes onto the UndoStack and auto-saves on
        /// both Do and Undo. Mirrors BuildingsRuntimeEditor.ExecutePersistedEdit.
        /// </summary>
        private void ExecutePersistedEdit(string label, Action doAction, Action undoAction)
        {
            _undo.Do(label,
                () =>
                {
                    doAction?.Invoke();
                    MarkInstanceDataDirty();
                    PersistDirtyInstanceChanges(label, force: true);
                },
                () =>
                {
                    undoAction?.Invoke();
                    MarkInstanceDataDirty();
                    PersistDirtyInstanceChanges($"Undo {label}", force: true);
                });
            RefreshUndoRedoLabels();
        }

        // ── Save ─────────────────────────────────────────────────────────────

        private void SaveInstancesToJson()
        {
            if (_isPersistingInstanceChanges) return;
            _isPersistingInstanceChanges = true;
            try
            {
                // Collect ALL persisted instances, including culled (inactive) ones.
                // FindObjectsOfType overload with includeInactive=true is available in Unity 2020+.
                var allComponents = FindObjectsOfType<PersistedParticleInstance>(includeInactive: true);
                var instances = new List<PersistedParticleInstance>(allComponents.Length);
                foreach (var inst in allComponents)
                {
                    if (inst == null) continue;
                    // Skip preview emitters — they are not parented under the loader
                    // and must never be persisted.
                    if (IsPreviewEmitter(inst.gameObject)) continue;
                    // Skip finite (one-shot) presets — they should never have been placed
                    // as decorations; filter them out progressively (legacy cleanup).
                    if (_catalog != null && !string.IsNullOrEmpty(inst.PresetId))
                    {
                        var p = _catalog.GetById(inst.PresetId);
                        if (p?.vfx != null && !p.vfx.loops)
                        {
                            Debug.LogWarning($"[ParticlesEditor] Skipping finite preset '{inst.PresetId}' during save (loops=false).");
                            continue;
                        }
                    }
                    instances.Add(inst);
                }

                var zm = FindObjectOfType<ZoneManager>();
                int zH = zm != null ? zm.ZoneHeightTiles : 50;

                string json = ParticleInstanceSerializer.Serialize(instances, zm, zH);
                GetOrCreateStore().Save(json);

                _hasUnsavedInstanceChanges = false;
                SetStatus($"Saved {instances.Count} particles → {INSTANCES_REL_PATH}");
                Debug.Log($"[ParticlesEditor] Saved {instances.Count} particles to {INSTANCES_REL_PATH}");
            }
            catch (Exception ex)
            {
                _hasUnsavedInstanceChanges = true;
                Debug.LogError($"[ParticlesEditor] Save failed: {ex.Message}\n{ex.StackTrace}");
                SetStatus("Save FAILED — see console.");
            }
            finally
            {
                _isPersistingInstanceChanges = false;
            }
        }

        // ── Reload ────────────────────────────────────────────────────────────

        private void ReloadFromJson()
        {
            var loader = FindObjectOfType<ParticleInstancesLoader>();
            if (loader == null)
            {
                SetStatus("Reload: ParticleInstancesLoader not found in scene.");
                return;
            }
            // Propagate the store so the loader and editor share the same backend.
            if (_instanceStore != null) loader.SetInstanceStore(_instanceStore);
            loader.Reload();
            _undo.Clear();
            _activeInstance = null;
            RefreshUndoRedoLabels();
            SetStatus("Reloaded from JSON.");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string ResolveZoneName(ZoneManager zm, Vector3 worldPos)
        {
            if (zm == null) return "Lobby";
            return zm.DetectZone(new Vector2(worldPos.x, worldPos.y));
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:   sb.Append(c);      break;
                }
            }
            return sb.ToString();
        }
    }
}
