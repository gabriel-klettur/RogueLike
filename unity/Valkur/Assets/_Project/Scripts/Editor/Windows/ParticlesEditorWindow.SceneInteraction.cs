using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.VFX;
using Valkur.Gameplay.World;

namespace Valkur.Editor
{
    public partial class ParticlesEditorWindow
    {
        // ------------------------------------------------------------------ Scene view (Place / Delete)

        private void OnSceneGUI(SceneView sceneView)
        {
            if (_mode == EditorMode.None) return;

            DrawSceneHandles(sceneView);

            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                Vector2 worldPos = SceneMouseToWorld(e.mousePosition, sceneView);

                if (_mode == EditorMode.Place)
                    HandlePlaceClick(worldPos);
                else if (_mode == EditorMode.Delete)
                    HandleDeleteClick(worldPos);

                e.Use();
                sceneView.Repaint();
                Repaint();
            }

            // Cancel on Escape
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                SetMode(EditorMode.None);
                e.Use();
            }
        }

        private void DrawSceneHandles(SceneView sceneView)
        {
            // Draw circles at all instance positions
            ZoneManager zm = UnityEngine.Object.FindObjectOfType<ZoneManager>();

            foreach (var inst in _instances)
            {
                Vector2 pos = ComputeSceneWorldPos(inst, zm);
                Color handleColor = (inst.preset_id == _selectedPresetId)
                    ? new Color(0.3f, 0.9f, 1f, 0.8f)
                    : new Color(0.9f, 0.6f, 0.2f, 0.5f);

                Handles.color = handleColor;
                Handles.DrawWireDisc(new Vector3(pos.x, pos.y, 0f), Vector3.forward, 0.3f);
                Handles.Label(new Vector3(pos.x + 0.35f, pos.y, 0f), inst.preset_id,
                    EditorStyles.miniLabel);
            }

            // Cursor hint
            string hint = _mode == EditorMode.Place
                ? $"Click to place: {_selectedPresetId ?? "(no preset selected)"}"
                : "Click to delete nearest emitter";
            Handles.BeginGUI();
            GUI.Label(new Rect(8, 8, 300, 24), hint, EditorStyles.boldLabel);
            Handles.EndGUI();
        }

        private void HandlePlaceClick(Vector2 worldPos)
        {
            if (string.IsNullOrEmpty(_selectedPresetId))
            {
                Debug.LogWarning("[ParticlesEditorWindow] Select a preset first.");
                return;
            }

            // Convert world position back to zone-relative pixels
            ZoneManager zm = UnityEngine.Object.FindObjectOfType<ZoneManager>();
            var (zone, relX, relY) = WorldPosToZoneRel(worldPos, zm);
            if (string.IsNullOrEmpty(zone))
            {
                // WorldPosToZoneRel refuses positions no zone covers. Falling back to the
                // selected zone here would anchor the instance to an origin it was never
                // placed against, which is how a rel pair silently becomes meaningless.
                return;
            }

            var inst = new ParticleInstanceData
            {
                id               = _nextId++,
                preset_id        = _selectedPresetId,
                zone             = zone,
                rel_x            = relX,
                rel_y            = relY,
                scale_multiplier = _scaleMultiplier
            };
            _instances.Add(inst);
            _selectedInstanceIdx = _instances.Count - 1;
            Debug.Log($"[ParticlesEditorWindow] Placed '{inst.preset_id}' at zone='{inst.zone}' rel=({inst.rel_x},{inst.rel_y})");
        }

        private void HandleDeleteClick(Vector2 worldPos)
        {
            ZoneManager zm = UnityEngine.Object.FindObjectOfType<ZoneManager>();
            int closestIdx = -1;
            float closestDist = float.MaxValue;

            for (int i = 0; i < _instances.Count; i++)
            {
                Vector2 pos = ComputeSceneWorldPos(_instances[i], zm);
                float d = Vector2.Distance(worldPos, pos);
                if (d < closestDist)
                {
                    closestDist = d;
                    closestIdx = i;
                }
            }

            if (closestIdx >= 0 && closestDist < 1.5f)
            {
                Debug.Log($"[ParticlesEditorWindow] Deleted instance id={_instances[closestIdx].id} '{_instances[closestIdx].preset_id}'");
                _instances.RemoveAt(closestIdx);
                if (_selectedInstanceIdx >= _instances.Count)
                    _selectedInstanceIdx = _instances.Count - 1;
            }
        }

        // ------------------------------------------------------------------ preview

        private void SpawnPreview(ParticlePresetDefinition preset)
        {
            DestroyPreview();

            SceneView sv = SceneView.lastActiveSceneView;
            Vector3 spawnPos = sv != null ? sv.pivot : Vector3.zero;
            spawnPos.z = 0f;

            _previewGo = new GameObject($"[Preview] {preset.id}");
            _previewGo.transform.position = spawnPos;
            _previewGo.AddComponent<ParticleEmitter>().ApplyPreset(preset);

            Selection.activeGameObject = _previewGo;
            Debug.Log($"[ParticlesEditorWindow] Spawned preview for '{preset.id}'");
        }

        private void DestroyPreview()
        {
            if (_previewGo != null)
            {
                DestroyImmediate(_previewGo);
                _previewGo = null;
            }
        }

        // ------------------------------------------------------------------ helpers

        private void SelectPreset(string presetId)
        {
            _selectedPresetId = presetId;
            if (_mode == EditorMode.None)
                SetMode(EditorMode.Place);
        }

        private void SetMode(EditorMode mode)
        {
            _mode = mode;
            // Re-register / unregister SceneView callback
            SceneView.duringSceneGui -= OnSceneGUI;
            if (mode != EditorMode.None)
                SceneView.duringSceneGui += OnSceneGUI;
            SceneView.RepaintAll();
        }

        private static Vector2 SceneMouseToWorld(Vector2 mousePos, SceneView sv)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mousePos);
            // For a 2D scene, project the ray onto Z=0
            float t = -ray.origin.z / ray.direction.z;
            Vector3 w = ray.origin + ray.direction * t;
            return new Vector2(w.x, w.y);
        }

        private Vector2 ComputeSceneWorldPos(ParticleInstanceData inst, ZoneManager zm)
        {
            Vector2Int offset = Vector2Int.zero;
            if (zm != null && !string.IsNullOrEmpty(inst.zone))
                if (zm.TryGetZone(inst.zone, out var def))
                    offset = def.gridOffset;

            // Shared with the writer above and with the runtime serializer. This used to
            // open-code `wy = offset.y - rel_y / PPU`, measuring from the zone's bottom edge
            // while the runtime measures from its top row, so the window drew every instance
            // (zoneHeightTiles - 1) tiles away from where the game put it.
            int zoneHeight = zm != null ? zm.ZoneHeightTiles : 50;
            float tileSize = zm != null ? zm.TileSize : 1f;
            return ParticleInstanceSerializer.RelToWorld(
                new Vector2Int(inst.rel_x, inst.rel_y), offset, zoneHeight, tileSize);
        }

        private (string zone, int relX, int relY) WorldPosToZoneRel(Vector2 worldPos, ZoneManager zm)
        {
            if (zm != null && zm.TryGetZoneAtTile(
                    new Vector2Int(Mathf.FloorToInt(worldPos.x / zm.TileSize),
                                   Mathf.FloorToInt(worldPos.y / zm.TileSize)), out var def))
            {
                // Shared with the runtime serializer, which is the other writer of this same
                // file. This used to be open-coded and measured rel_y from the zone's BOTTOM
                // edge while the runtime measures it from the TOP row — both self-consistent,
                // and between them every instance jumped (zoneHeightTiles - 1) tiles depending
                // on which tool had touched it last.
                var rel = ParticleInstanceSerializer.WorldToRel(
                    worldPos, def.gridOffset, zm.ZoneHeightTiles, zm.TileSize);
                return (def.zoneName, rel.x, rel.y);
            }

            // No zone covers this point. Writing raw world coordinates into a zone-relative
            // field — which is what this did, tagged with whatever zone happened to be
            // selected — is precisely the defect that made spawners drift by their zone's
            // origin on every restart. Refuse instead: the caller drops the placement and the
            // user is told, which is recoverable in a way silently-wrong data is not.
            Debug.LogWarning($"[ParticlesEditorWindow] No zone covers " +
                             $"({worldPos.x:F1}, {worldPos.y:F1}); refusing to place an instance " +
                             "there rather than persisting an unanchored position.");
            return (null, 0, 0);
        }

        private void FocusInstance(ParticleInstanceData inst)
        {
            ZoneManager zm = UnityEngine.Object.FindObjectOfType<ZoneManager>();
            Vector2 pos = ComputeSceneWorldPos(inst, zm);
            SceneView.lastActiveSceneView?.LookAt(new Vector3(pos.x, pos.y, 0f),
                Quaternion.identity, 5f);
        }

        // ------------------------------------------------------------------ JSON parse helpers

        private static int GetInt(Dictionary<string, object> d, string key, int def = 0)
        {
            if (d.TryGetValue(key, out var v) && v != null)
                try { return Convert.ToInt32(v); } catch { }
            return def;
        }

        private static string GetString(Dictionary<string, object> d, string key, string def = "")
        {
            if (d.TryGetValue(key, out var v) && v != null) return v.ToString();
            return def;
        }

        private static float GetFloat(Dictionary<string, object> d, string key, float def = 0f)
        {
            if (d.TryGetValue(key, out var v) && v != null)
                try { return Convert.ToSingle(v); } catch { }
            return def;
        }
    }
}
