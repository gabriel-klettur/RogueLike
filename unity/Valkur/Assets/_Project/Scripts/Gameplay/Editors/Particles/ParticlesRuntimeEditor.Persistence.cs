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

        /// <summary>
        /// Below this many on-disk records a shrink is not treated as catastrophic — deleting
        /// two of three emitters is ordinary editing, and a low floor would fire constantly.
        /// </summary>
        private const int CATASTROPHIC_DROP_FLOOR = 10;

        /// <summary>
        /// A save keeping less than this fraction of what the file holds is refused. Chosen so
        /// that ordinary bulk edits pass while 221 -> 3 does not.
        /// </summary>
        private const float CATASTROPHIC_DROP_RATIO = 0.5f;

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
        /// Consumed by the next <see cref="SaveInstancesToJson"/> to permit writing an empty
        /// list over a populated file.
        ///
        /// This is a one-shot field rather than a method parameter because the EditMode tests
        /// drive these methods through reflection resolved by name and arity — an added
        /// parameter throws TargetParameterCountException, and an overload makes GetMethod
        /// ambiguous. The signatures stay exactly as the tests found them.
        /// </summary>
        private bool _allowEmptyWriteOnce;

        /// <summary>
        /// How many records the on-disk file currently holds, or -1 when it cannot be read.
        ///
        /// Counted straight off the raw JSON rather than through
        /// <see cref="ParticleInstanceSerializer.Deserialize"/> on purpose: Deserialize needs a
        /// ZoneManager and drops records whose zone it cannot resolve, so it would under-report
        /// exactly in the situation this guard exists to catch.
        /// </summary>
        private int CountRecordsOnDisk()
        {
            try
            {
                string json = GetOrCreateStore().Load();
                if (string.IsNullOrEmpty(json)) return 0;

                var parsed = MiniJsonRuntime.Deserialize(json);
                if (parsed is List<object> bare) return bare.Count;          // v1 bare array
                if (parsed is Dictionary<string, object> obj
                    && obj.TryGetValue("instances", out var inst)
                    && inst is List<object> list) return list.Count;          // v2 wrapper
                return -1;                                                    // unrecognised shape
            }
            catch
            {
                return -1;
            }
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

        /// <summary>
        /// Same as <see cref="ExecutePersistedEdit"/>, for edits whose whole purpose is
        /// removal. Reaching zero instances by deleting them is legitimate and must be
        /// persisted; reaching zero because the world was cleared behind the editor's back is
        /// data loss, and the save guard cannot tell those apart on its own.
        ///
        /// Only the Do half is exempt — undo restores instances, it never removes them.
        /// </summary>
        private void ExecuteDeletionEdit(string label, Action doAction, Action undoAction)
        {
            _allowEmptyWriteOnce = true;
            try { ExecutePersistedEdit(label, doAction, undoAction); }
            finally { _allowEmptyWriteOnce = false; }
        }

        // ── Save ─────────────────────────────────────────────────────────────

        private void SaveInstancesToJson()
        {
            if (_isPersistingInstanceChanges) return;
            bool allowEmptyWrite = _allowEmptyWriteOnce;
            _allowEmptyWriteOnce = false;
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

                // ── Anti-wipe guard ──────────────────────────────────────────────
                // Every edit force-saves whatever PersistedParticleInstance components are
                // in the scene. That is fine while the scene mirrors the file, and
                // catastrophic when it does not: ParticleInstancesLoader.Reload() is
                // ClearAll() followed by LoadAndSpawn() with no transaction, and
                // MapEditorManager.ClearAllSpawnedWorldContent() calls ClearAll() on its
                // own. After either one leaves the scene empty, the very next edit
                // serialises nothing and overwrites the file with an empty array.
                //
                // This is not hypothetical. particles_instances.json held 221 placed
                // emitters (falling leaves, fountains, portals, pollen) and was reduced to
                // 4 bytes in commit 23e315073. Mirrors the ABORTING-save guards in
                // BuildingsRuntimeEditor.Persistence, which exist for the same class of bug.
                if (!allowEmptyWrite)
                {
                    int onDisk = CountRecordsOnDisk();
                    string abortReason = null;

                    if (onDisk < 0)
                    {
                        // Unparseable: refuse rather than replace something we cannot read.
                        if (instances.Count == 0)
                            abortReason = "scene holds 0 particle instances and the file could not be parsed.";
                    }
                    else if (instances.Count == 0 && onDisk > 0)
                    {
                        abortReason = $"scene holds 0 particle instances but the file holds {onDisk}.";
                    }
                    else if (onDisk >= CATASTROPHIC_DROP_FLOOR &&
                             instances.Count < onDisk * CATASTROPHIC_DROP_RATIO)
                    {
                        // The partial case, which the empty check alone does not catch: a
                        // world holding 221 emitters was reduced to the 3 that happened to be
                        // in the scene, because only those 3 had been spawned.
                        abortReason = $"scene holds {instances.Count} particle instances but the " +
                                      $"file holds {onDisk} — too large a drop to be an edit.";
                    }

                    if (abortReason != null)
                    {
                        Debug.LogError($"[ParticlesEditor] ABORTING save — {abortReason} File NOT written. " +
                                       "The world was probably cleared or only partially loaded; " +
                                       "restart Play Mode to reload the last good on-disk state. " +
                                       "If the drop is intentional, delete the instances explicitly.");
                        SetStatus("Save ABORTED — see console.");
                        return;
                    }
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
