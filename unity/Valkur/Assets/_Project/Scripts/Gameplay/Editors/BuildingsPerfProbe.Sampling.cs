using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Profiling;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Buildings
{
    public partial class BuildingsPerfProbe : MonoBehaviour
    {
        private void Sample()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            Vector3 cp   = _cam.transform.position;
            float   halfH = _cam.orthographicSize;
            float   halfW = halfH * _cam.aspect;

            // Buildings
            _buildingsTotal   = 0;
            _buildingsVisible = 0;
            var buildingObjs = Object.FindObjectsOfType<BuildingObject>();
            for (int i = 0; i < buildingObjs.Length; i++)
            {
                if (!buildingObjs[i].isActiveAndEnabled) continue;
                _buildingsTotal++;
                // Visible = any sprite in camera view
                var sr = buildingObjs[i].GetComponentInChildren<SpriteRenderer>(false);
                if (sr != null && sr.isVisible) _buildingsVisible++;
            }

            // Building Collider2Ds (layer 14)
            _buildingCollidersTotal   = 0;
            _buildingCollidersEnabled = 0;
            _buildingCollidersActive  = 0;
            var allCols2D = Object.FindObjectsOfType<Collider2D>();
            for (int i = 0; i < allCols2D.Length; i++)
            {
                var col = allCols2D[i];
                if (col == null || col.gameObject.layer != BUILDING_LAYER) continue;
                _buildingCollidersTotal++;
                if (col.enabled)
                {
                    _buildingCollidersEnabled++;
                    if (col.gameObject.activeInHierarchy && !col.isTrigger)
                        _buildingCollidersActive++;
                }
            }

            // BuildingsColliderSession instances (reflected)
            _colliderSessionsActive = 0;
            _colliderSessionsDirty  = 0;
            if (_colliderSessionType != null)
            {
                var sessions = Object.FindObjectsOfType(_colliderSessionType);
                for (int i = 0; i < sessions.Length; i++)
                {
                    if (_collSessionActiveProp != null)
                        try { if ((bool)_collSessionActiveProp.GetValue(sessions[i])) _colliderSessionsActive++; } catch { }
                    if (_collSessionDirtyProp != null)
                        try { if ((bool)_collSessionDirtyProp.GetValue(sessions[i])) _colliderSessionsDirty++; } catch { }
                }
            }

            // SpriteRenderers (all)
            _spritesTotal   = 0;
            _spritesVisible = 0;
            var srs = Object.FindObjectsOfType<SpriteRenderer>();
            for (int i = 0; i < srs.Length; i++)
            {
                if (!srs[i].enabled) continue;
                _spritesTotal++;
                if (srs[i].isVisible) _spritesVisible++;
            }

            // Particles
            _particlesTotal = _particlesActive = _particlesPlaying = _liveParticleCount = 0;
            var pss = Object.FindObjectsOfType<ParticleSystem>();
            for (int i = 0; i < pss.Length; i++)
            {
                _particlesTotal++;
                if (pss[i].gameObject.activeInHierarchy) _particlesActive++;
                if (pss[i].isPlaying) _particlesPlaying++;
                _liveParticleCount += pss[i].particleCount;
            }

            // Light2Ds
            _lightsTotal = _lightsActive = 0;
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

            // NPCs
            _npcsAlive = _npcsUpdating = 0;
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

            // GC alloc rate
            long now = System.GC.GetTotalMemory(false);
            if (Time.unscaledTime - _gcLastSecondMark >= 1f)
            {
                _gcAllocLastSecondBytes = System.Math.Max(0, now - _gcLastBaseline);
                _gcLastBaseline  = now;
                _gcLastSecondMark = Time.unscaledTime;
            }
            _gcAllocBytes = now;

            // Scene counts
            _totalGameObjects    = Object.FindObjectsOfType<Transform>().Length;
            _activeMonoBehaviours = 0;
            var allMbs = Object.FindObjectsOfType<MonoBehaviour>();
            for (int i = 0; i < allMbs.Length; i++) if (allMbs[i].isActiveAndEnabled) _activeMonoBehaviours++;

            // Colliders / rigidbodies
            _colliders2DActive  = allCols2D.Length;
            _colliders2DEnabled = 0;
            for (int i = 0; i < allCols2D.Length; i++) if (allCols2D[i].enabled) _colliders2DEnabled++;
            _rigidbodies2D = Object.FindObjectsOfType<Rigidbody2D>().Length;

            // Animators
            var anims = Object.FindObjectsOfType<Animator>();
            _animators = anims.Length; _animatorsVisible = 0;
            for (int i = 0; i < anims.Length; i++)
            {
                if (!anims[i].isActiveAndEnabled) continue;
                Vector3 p = anims[i].transform.position;
                if (Mathf.Abs(p.x - cp.x) <= halfW + 2f && Mathf.Abs(p.y - cp.y) <= halfH + 2f)
                    _animatorsVisible++;
            }

#if UNITY_EDITOR
            _drawCalls            = UnityEditor.UnityStats.drawCalls;
            _setPassCalls         = UnityEditor.UnityStats.setPassCalls;
            _batches              = UnityEditor.UnityStats.batches;
            _triangles            = UnityEditor.UnityStats.triangles;
            _vertices             = UnityEditor.UnityStats.vertices;
            _renderTextureMemBytes = UnityEditor.UnityStats.renderTextureBytes;
            _textureMemBytes       = Profiler.GetAllocatedMemoryForGraphicsDriver();
#endif

            // Camera info
            _camerasActive = 0;
            var allCams = Camera.allCameras;
            for (int i = 0; i < allCams.Length; i++) if (allCams[i].isActiveAndEnabled) _camerasActive++;
            _screenW    = Screen.width;
            _screenH    = Screen.height;
            _orthoSize  = _cam.orthographicSize;
            _viewWidthW = _orthoSize * 2f * _cam.aspect;

            _cam0Name = _cam1Name = _cam2Name = "-";
            _cam0Info = _cam1Info = _cam2Info = "";
            int active = 0;
            for (int i = 0; i < allCams.Length && active < 3; i++)
            {
                var c = allCams[i];
                if (!c || !c.isActiveAndEnabled) continue;
                string tname = c.targetTexture != null ? "RT" : "scr";
                bool postFx = false;
                if (_urpCamDataType != null && _urpRenderPostProp != null)
                {
                    var data = c.GetComponent(_urpCamDataType);
                    if (data != null) try { postFx = (bool)_urpRenderPostProp.GetValue(data); } catch { }
                }
                string info = $"{tname} pfx={(postFx ? "Y" : "N")} d={c.depth:F0}";
                switch (active) { case 0: _cam0Name = c.name; _cam0Info = info; break;
                                  case 1: _cam1Name = c.name; _cam1Info = info; break;
                                  case 2: _cam2Name = c.name; _cam2Info = info; break; }
                active++;
            }

#if UNITY_EDITOR
            _sceneViewVisible = UnityEditor.SceneView.sceneViews.Count > 0;
            _gameViewWidth  = (int)UnityEditor.Handles.GetMainGameViewSize().x;
            _gameViewHeight = (int)UnityEditor.Handles.GetMainGameViewSize().y;
#endif

            // Material uniqueness among visible sprites
            _uniqueMaterialCount = 0;
            var matSet = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < srs.Length; i++)
            {
                if (srs[i] == null || !srs[i].enabled || !srs[i].isVisible) continue;
                var sm = srs[i].sharedMaterial;
                if (sm != null) matSet.Add(sm.GetInstanceID());
            }
            _uniqueMaterialCount = matSet.Count;

            // Shadow casters in view
            _shadowCasterTotal = _shadowCaster2DCount = 0;
            if (_shadowCaster2DType != null)
            {
                var shads = Object.FindObjectsOfType(_shadowCaster2DType);
                _shadowCasterTotal = shads.Length;
                for (int i = 0; i < shads.Length; i++)
                {
                    var c = shads[i] as Component;
                    if (c == null || !c.gameObject.activeInHierarchy) continue;
                    Vector3 p = c.transform.position;
                    if (Mathf.Abs(p.x - cp.x) <= halfW + 4f && Mathf.Abs(p.y - cp.y) <= halfH + 4f)
                        _shadowCaster2DCount++;
                }
            }

            // Light2D with shadows
            _light2DWithShadows = 0;
            if (_light2DType != null)
            {
                var shadowProp = _light2DType.GetProperty("shadowsEnabled", BindingFlags.Public | BindingFlags.Instance);
                var lights = Object.FindObjectsOfType(_light2DType);
                for (int i = 0; i < lights.Length; i++)
                {
                    var c = lights[i] as Component;
                    if (c == null || !c.gameObject.activeInHierarchy) continue;
                    if (shadowProp != null)
                        try { if ((bool)shadowProp.GetValue(c)) _light2DWithShadows++; } catch { }
                }
            }
        }

        // ── IMGUI ─────────────────────────────────────────────────────────────

        private struct MetricCell { public string Label; public string Value; public GUIStyle Style; }
        private readonly List<MetricCell> _cells = new List<MetricCell>(64);

    }
}