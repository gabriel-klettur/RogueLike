using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.World;
using Valkur.UIKit;

namespace Valkur.Gameplay.VFX
{
    public partial class ParticlesRuntimeEditor : SingletonMonoBehaviour<ParticlesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        private const string INSTANCES_REL_PATH = "StreamingAssets/Particles/particles_instances.json";

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

        // ── Save ────────────────────────────────────────────────────────────────

        private void SaveInstancesToJson()
        {
            if (_isPersistingInstanceChanges) return;
            string dir  = Path.Combine(Application.streamingAssetsPath, "Particles");
            string path = Path.Combine(dir, "particles_instances.json");
            _isPersistingInstanceChanges = true;
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var emitters = FindEditorOwnedEmitters();
                var zm = FindObjectOfType<ZoneManager>();
                int zH = zm != null ? zm.ZoneHeightTiles : 0;
                const float PPU = 32f;

                var sb = new StringBuilder();
                sb.AppendLine("[");
                int nextId = 1;
                for (int i = 0; i < emitters.Count; i++)
                {
                    var rec = emitters[i];
                    string pid = rec.presetId ?? "";
                    int relX = 0, relY = 0;
                    string zone = ResolveZoneName(zm, rec.go.transform.position);
                    if (zm != null && zm.TryGetZone(zone, out var zd))
                    {
                        float wx = rec.go.transform.position.x;
                        float wy = rec.go.transform.position.y;
                        relX = Mathf.RoundToInt((wx - zd.gridOffset.x) * PPU);
                        relY = Mathf.RoundToInt((zd.gridOffset.y + (zH - 1) - wy) * PPU);
                    }
                    sb.Append("  {");
                    sb.Append($"\"id\": {nextId++}, ");
                    sb.Append($"\"preset_id\": \"{EscapeJson(pid)}\", ");
                    sb.Append($"\"zone\": \"{EscapeJson(zone)}\", ");
                    sb.Append($"\"rel_x\": {relX}, ");
                    sb.Append($"\"rel_y\": {relY}");
                    if (rec.scaleMultiplier > 0f && Mathf.Abs(rec.scaleMultiplier - 1f) > 0.0001f)
                        sb.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            ", \"scale_multiplier\": {0:F4}", rec.scaleMultiplier));
                    sb.Append("}");
                    if (i < emitters.Count - 1) sb.Append(",");
                    sb.AppendLine();
                }
                sb.AppendLine("]");
                File.WriteAllText(path, sb.ToString());

                _hasUnsavedInstanceChanges = false;
                SetStatus($"Saved {emitters.Count} particles → {INSTANCES_REL_PATH}");
                Debug.Log($"[ParticlesEditor] Saved {emitters.Count} particles to {path}");
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

        // ── Reload ──────────────────────────────────────────────────────────────

        private void ReloadFromJson()
        {
            var loader = FindObjectOfType<ParticleInstancesLoader>();
            if (loader == null)
            {
                SetStatus("Reload: ParticleInstancesLoader not found in scene.");
                return;
            }
            loader.Reload();
            _undo.Clear();
            _activeInstance = null;
            RefreshUndoRedoLabels();
            SetStatus("Reloaded from JSON.");
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private struct EmitterRecord
        {
            public GameObject go;
            public string presetId;
            public float scaleMultiplier;
        }

        private List<EmitterRecord> FindEditorOwnedEmitters()
        {
            var result = new List<EmitterRecord>();
            var all = FindObjectsOfType<ParticleEmitter>();
            foreach (var emitter in all)
            {
                if (emitter == null) continue;
                var go = emitter.gameObject;
                if (!go.activeInHierarchy) continue;
                string pid = ExtractPresetIdFromName(go.name);
                if (string.IsNullOrEmpty(pid)) continue; // skip emitters not tracked by the loader (e.g. spell VFX).
                result.Add(new EmitterRecord
                {
                    go = go,
                    presetId = pid,
                    scaleMultiplier = 1f
                });
            }
            return result;
        }

        private static string ResolveZoneName(ZoneManager zm, Vector3 worldPos)
        {
            if (zm == null) return "Lobby";
            foreach (var zone in zm.GetZonesSnapshot())
            {
                // Snapshot has gridOffset (tile units) — we don't have explicit zone bounds
                // here so fall back to the first zone match by Manhattan proximity.
                Vector2 off = zone.gridOffset;
                if (Vector2.Distance(new Vector2(worldPos.x, worldPos.y), off) < 100f)
                    return zone.zoneName;
            }
            return "Lobby";
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
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
