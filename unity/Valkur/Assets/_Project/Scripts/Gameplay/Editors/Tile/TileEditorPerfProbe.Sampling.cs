using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TileEditor
{
    public partial class TileEditorPerfProbe : MonoBehaviour
    {        private void Sample()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            // Tilemaps
            _tilemapsTotal = 0;
            _tilemapsVisible = 0;
            var trends = Object.FindObjectsOfType<TilemapRenderer>();
            for (int i = 0; i < trends.Length; i++)
            {
                if (!trends[i].enabled) continue;
                _tilemapsTotal++;
                if (trends[i].isVisible) _tilemapsVisible++;
            }

            // SpriteRenderers (excluding tilemap chunks)
            _spritesTotal = 0;
            _spritesVisible = 0;
            var srs = Object.FindObjectsOfType<SpriteRenderer>();
            for (int i = 0; i < srs.Length; i++)
            {
                if (!srs[i].enabled) continue;
                _spritesTotal++;
                if (srs[i].isVisible) _spritesVisible++;
            }

            // ParticleSystems
            _particlesTotal = 0;
            _particlesActive = 0;
            _particlesPlaying = 0;
            _liveParticleCount = 0;
            var pss = Object.FindObjectsOfType<ParticleSystem>();
            for (int i = 0; i < pss.Length; i++)
            {
                _particlesTotal++;
                if (pss[i].gameObject.activeInHierarchy) _particlesActive++;
                if (pss[i].isPlaying) _particlesPlaying++;
                _liveParticleCount += pss[i].particleCount;
            }

            // Light2Ds
            _lightsTotal = 0;
            _lightsActive = 0;
            if (_light2DType != null)
            {
                var lights = Object.FindObjectsOfType(_light2DType);
                for (int i = 0; i < lights.Length; i++)
                {
                    var c = lights[i] as Component;
                    if (c == null) continue;
                    _lightsTotal++;
                    if (c.gameObject.activeInHierarchy) _lightsActive++;
                }
            }

            // NPCs (FSMMonsterBrain) and EntityCulling
            _npcsAlive = 0;
            _npcsUpdating = 0;
            var brainType = System.Type.GetType("Valkur.Gameplay.FSM.FSMMonsterBrain, Valkur.Gameplay");
            var cullType  = System.Type.GetType("Valkur.Gameplay.EntityCulling, Valkur.Gameplay");
            if (brainType != null)
            {
                var brains = Object.FindObjectsOfType(brainType);
                _npcsAlive = brains.Length;
                if (cullType != null)
                {
                    var prop = cullType.GetProperty("ShouldUpdate", BindingFlags.Public | BindingFlags.Instance);
                    for (int i = 0; i < brains.Length; i++)
                    {
                        var go = (brains[i] as Component)?.gameObject;
                        if (go == null) continue;
                        var cull = go.GetComponent(cullType);
                        if (cull == null) { _npcsUpdating++; continue; }
                        bool su = prop != null && (bool)prop.GetValue(cull);
                        if (su) _npcsUpdating++;
                    }
                }
                else _npcsUpdating = _npcsAlive;
            }

            // GC allocation rate (per second)
            long now = System.GC.GetTotalMemory(false);
            if (Time.unscaledTime - _gcLastSecondMark >= 1f)
            {
                _gcAllocLastSecondBytes = System.Math.Max(0, now - _gcLastBaseline);
                _gcLastBaseline = now;
                _gcLastSecondMark = Time.unscaledTime;
            }
            _gcAllocBytes = now;

            // Total GameObjects in scene
            var allGos = Object.FindObjectsOfType<Transform>();
            _totalGameObjects = allGos.Length;

            // MonoBehaviours that have an Update method (rough proxy via overriding type)
            var allMbs = Object.FindObjectsOfType<MonoBehaviour>();
            _activeMonoBehaviours = 0;
            for (int i = 0; i < allMbs.Length; i++)
            {
                if (allMbs[i].isActiveAndEnabled) _activeMonoBehaviours++;
            }

#if UNITY_EDITOR
            _drawCalls    = UnityEditor.UnityStats.drawCalls;
            _setPassCalls = UnityEditor.UnityStats.setPassCalls;
            _batches      = UnityEditor.UnityStats.batches;
            _triangles    = UnityEditor.UnityStats.triangles;
            _vertices     = UnityEditor.UnityStats.vertices;
            _renderTextureMemBytes = UnityEditor.UnityStats.renderTextureBytes;
            _textureMemBytes       = UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver();
#endif

            // ── GPU diagnostics ──
            _camerasActive = 0;
            var allCams = Camera.allCameras;
            for (int i = 0; i < allCams.Length; i++) if (allCams[i].isActiveAndEnabled) _camerasActive++;
            _screenW = Screen.width;
            _screenH = Screen.height;
            _orthoSize = _cam.orthographicSize;
            _viewWidthW = _orthoSize * 2f * _cam.aspect;

            // ── Identify cameras (up to 3 names) ──
            _cam0Name = _cam1Name = _cam2Name = "-";
            _cam0Info = _cam1Info = _cam2Info = "";
            int active = 0;
            for (int i = 0; i < allCams.Length && active < 3; i++)
            {
                var c = allCams[i];
                if (c == null || !c.isActiveAndEnabled) continue;
                string tname = c.targetTexture != null ? "RT" : "screen";
                bool postFx = false;
                if (_urpCamDataType != null && _urpRenderPostProp != null)
                {
                    var data = c.GetComponent(_urpCamDataType);
                    if (data != null)
                    {
                        try { postFx = (bool)_urpRenderPostProp.GetValue(data); }
                        catch { /* ignore */ }
                    }
                }
                string info = $"{tname} pfx={(postFx ? "Y" : "N")} d={c.depth:F0}";
                switch (active)
                {
                    case 0: _cam0Name = c.name; _cam0Info = info; break;
                    case 1: _cam1Name = c.name; _cam1Info = info; break;
                    case 2: _cam2Name = c.name; _cam2Info = info; break;
                }
                active++;
            }

#if UNITY_EDITOR
            // Detect Scene View visible (it renders the scene every frame too in Editor)
            _sceneViewVisible = UnityEditor.SceneView.lastActiveSceneView != null
                              && UnityEditor.SceneView.lastActiveSceneView.hasFocus
                              || UnityEditor.SceneView.sceneViews.Count > 0;
            _gameViewWidth  = (int)UnityEditor.Handles.GetMainGameViewSize().x;
            _gameViewHeight = (int)UnityEditor.Handles.GetMainGameViewSize().y;
#endif

            // ── Tile count in view ──
            _tilesInView = 0;
            _tilemapColliders = 0;
            float halfH = _cam.orthographicSize;
            float halfW = halfH * _cam.aspect;
            Vector3 cp = _cam.transform.position;
            var tilemaps = Object.FindObjectsOfType<Tilemap>();
            for (int i = 0; i < tilemaps.Length; i++)
            {
                var tm = tilemaps[i];
                if (tm == null || !tm.gameObject.activeInHierarchy) continue;
                var renderer = tm.GetComponent<TilemapRenderer>();
                if (renderer == null || !renderer.enabled) continue;

                // Compute the tilemap-local cell range that overlaps the camera window.
                Vector3 minWorld = new Vector3(cp.x - halfW, cp.y - halfH, 0f);
                Vector3 maxWorld = new Vector3(cp.x + halfW, cp.y + halfH, 0f);
                Vector3Int minCell = tm.WorldToCell(minWorld);
                Vector3Int maxCell = tm.WorldToCell(maxWorld);
                int minX = Mathf.Min(minCell.x, maxCell.x);
                int maxX = Mathf.Max(minCell.x, maxCell.x);
                int minY = Mathf.Min(minCell.y, maxCell.y);
                int maxY = Mathf.Max(minCell.y, maxCell.y);
                int z = minCell.z;

                // Cap iteration to a sane window to avoid pathological tilemaps.
                int xCells = Mathf.Min(maxX - minX + 1, 256);
                int yCells = Mathf.Min(maxY - minY + 1, 256);
                for (int xi = 0; xi < xCells; xi++)
                for (int yi = 0; yi < yCells; yi++)
                {
                    if (tm.HasTile(new Vector3Int(minX + xi, minY + yi, z))) _tilesInView++;
                }

                if (tm.GetComponent<TilemapCollider2D>() != null) _tilemapColliders++;
            }

            SampleObjectCounts();
        }

        // ── IMGUI ───────────────────────────────────────────────────────────

        private struct MetricCell { public string Label; public string Value; public GUIStyle Style; }
        private readonly List<MetricCell> _cells = new List<MetricCell>(40);

    }
}