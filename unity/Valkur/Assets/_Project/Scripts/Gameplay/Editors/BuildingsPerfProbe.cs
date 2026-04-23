using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Profiling;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Buildings
{
    /// <summary>
    /// IMGUI overlay showing a real-time performance breakdown of the Buildings Editor.
    /// Designed to help diagnose frame-rate issues while editing buildings and testing
    /// colliders. Toggle with the PERF button in the Buildings Editor menu bar.
    ///
    /// Numbers refresh at ~1 Hz to avoid biasing the measurement. Profiler.Recorder
    /// rows update every frame for accurate timing.
    /// </summary>
    public class BuildingsPerfProbe : MonoBehaviour
    {
        public bool Visible;

        // ── Smoothed FPS ──────────────────────────────────────────────────────
        private float _accum;
        private int   _frames;
        private float _fps;
        private float _frameMs;

        // ── 1 Hz aggregate refresh ────────────────────────────────────────────
        private float _nextSampleTime;
        private const float SAMPLE_INTERVAL = 1.0f;

        // ── Buildings-specific metrics ────────────────────────────────────────
        private int _buildingsTotal;            // BuildingObject instances in scene
        private int _buildingsVisible;          // …with SpriteRenderer visible in camera
        private int _buildingCollidersTotal;    // Collider2D on Building layer (14)
        private int _buildingCollidersEnabled;
        private int _buildingCollidersActive;   // isTrigger=false, enabled, goActive
        private int _colliderSessionsActive;    // BuildingsColliderSession.Active == true
        private int _colliderSessionsDirty;     // …with HasUnsavedChanges (if accessible)

        // ── Generic scene metrics ─────────────────────────────────────────────
        private int  _spritesTotal, _spritesVisible;
        private int  _particlesTotal, _particlesActive, _particlesPlaying, _liveParticleCount;
        private int  _lightsTotal, _lightsActive;
        private int  _npcsAlive, _npcsUpdating;
        private int  _totalGameObjects;
        private int  _activeMonoBehaviours;
        private long _gcAllocBytes;
        private long _gcAllocLastSecondBytes;
        private float _gcLastSecondMark;
        private long  _gcLastBaseline;
        private int  _colliders2DActive, _colliders2DEnabled;
        private int  _rigidbodies2D;
        private int  _animators, _animatorsVisible;
        private int  _camerasActive;
        private int  _screenW, _screenH;
        private float _orthoSize, _viewWidthW;

        // ── Editor / render stats ─────────────────────────────────────────────
        private int  _drawCalls, _setPassCalls, _batches, _triangles, _vertices;
        private long _renderTextureMemBytes;
        private long _textureMemBytes;
        private int  _uniqueMaterialCount;
        private int  _shadowCasterTotal, _shadowCaster2DCount;
        private int  _light2DWithShadows;

        // Camera identification
        private string _cam0Name = "-", _cam1Name = "-", _cam2Name = "-";
        private string _cam0Info = "",  _cam1Info = "",  _cam2Info = "";
        private bool   _sceneViewVisible;
        private int    _gameViewWidth, _gameViewHeight;

        // ── Bisection toggles ─────────────────────────────────────────────────
        private bool _bisectExtraCamerasOff;
        private bool _bisectSpritesOff;
        private bool _bisectLightsOff;
        private bool _bisectVolumesOff;
        private bool _bisectPostFxOff;
        private bool _bisectBuildingCollidersOff;

        private readonly List<Camera>          _disabledCameras  = new List<Camera>();
        private readonly List<SpriteRenderer>  _disabledSprites  = new List<SpriteRenderer>();
        private readonly List<Behaviour>       _disabledLights   = new List<Behaviour>();
        private readonly List<Behaviour>       _disabledVolumes  = new List<Behaviour>();
        private readonly List<Collider2D>      _disabledBuildingColliders = new List<Collider2D>();
        private readonly List<Camera>          _camsWithPostFx   = new List<Camera>();
        private readonly List<bool>            _camsPostFxOriginal = new List<bool>();

        // ── Reflection caches ─────────────────────────────────────────────────
        private System.Type  _light2DType;
        private System.Type  _urpCamDataType;
        private PropertyInfo _urpRenderPostProp;
        private System.Type  _volumeType;
        private System.Type  _shadowCaster2DType;
        private System.Type  _colliderSessionType;
        private PropertyInfo _collSessionActiveProp;
        private PropertyInfo _collSessionDirtyProp;

        // ── Profiler.Recorder rows ────────────────────────────────────────────
        private struct RecorderRow { public string Label; public Recorder Recorder; }
        private RecorderRow[] _recorders;
        private float[]       _recorderMs;
        private const float   RECORDER_SMOOTH = 0.25f;

        // ── Camera cache ──────────────────────────────────────────────────────
        private Camera _cam;

        // ── IMGUI styles ──────────────────────────────────────────────────────
        private GUIStyle _headerStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _rowStyle;
        private GUIStyle _warnStyle;
        private GUIStyle _goodStyle;
        private bool     _stylesReady;

        // Buildings layer index (project convention)
        private const int BUILDING_LAYER = 14;

        // ─────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            _light2DType = System.Type.GetType(
                "UnityEngine.Rendering.Universal.Light2D, Unity.RenderPipelines.Universal.Runtime");
            _urpCamDataType = System.Type.GetType(
                "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (_urpCamDataType != null)
                _urpRenderPostProp = _urpCamDataType.GetProperty("renderPostProcessing",
                    BindingFlags.Public | BindingFlags.Instance);
            _volumeType = System.Type.GetType(
                "UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime");
            _shadowCaster2DType = System.Type.GetType(
                "UnityEngine.Rendering.Universal.ShadowCaster2D, Unity.RenderPipelines.Universal.Runtime");

            // Reflect BuildingsColliderSession at runtime to avoid assembly coupling
            _colliderSessionType = System.Type.GetType(
                "Valkur.Gameplay.Buildings.BuildingsColliderSession, Valkur.Gameplay");
            if (_colliderSessionType != null)
            {
                _collSessionActiveProp = _colliderSessionType.GetProperty("Active",
                    BindingFlags.Public | BindingFlags.Instance);
                _collSessionDirtyProp  = _colliderSessionType.GetProperty("HasUnsavedChanges",
                    BindingFlags.Public | BindingFlags.Instance);
            }

            // CPU profiler marker recorders
            string[] markers =
            {
                "BehaviourUpdate",
                "LateBehaviourUpdate",
                "FixedBehaviourUpdate",
                "Camera.Render",
                "PlayerLoop.PostLateUpdate.UpdateAllRenderers",
                "Physics2D.Simulate",
                "GC.Collect",
                "Gfx.WaitForPresentOnGfxThread",
                "WaitForTargetFPS",
                "Gfx.PresentFrame",
                "RenderLoop.DrawSRPBatcher",
                "RenderLoop.Draw",
                "Shadows.RenderJobDir",
                "Render.OpaqueGeometry",
                "Render.TransparentGeometry",
                "Light2D.Render",
                "Canvas.SendWillRenderCanvases",
                "EditorLoop",
                "EditorOverhead",
            };
            _recorders  = new RecorderRow[markers.Length];
            _recorderMs = new float[markers.Length];
            for (int i = 0; i < markers.Length; i++)
            {
                var rec = Recorder.Get(markers[i]);
                if (rec != null) rec.enabled = true;
                _recorders[i] = new RecorderRow { Label = markers[i], Recorder = rec };
            }

            Debug.Log("[BuildingsPerfProbe] Awake. Light2D resolved: " + (_light2DType != null));
        }

        private void Update()
        {
            // Smoothed FPS
            _accum  += Time.unscaledDeltaTime;
            _frames++;
            if (_accum >= 0.5f)
            {
                _fps     = _frames / _accum;
                _frameMs = (_accum / _frames) * 1000f;
                _accum   = 0f;
                _frames  = 0;
            }

            // Profiler recorder polling (every frame = accurate)
            if (_recorders != null)
            {
                for (int i = 0; i < _recorders.Length; i++)
                {
                    var rec = _recorders[i].Recorder;
                    if (rec == null) continue;
                    float sampleMs = (float)(rec.elapsedNanoseconds / 1_000_000.0);
                    _recorderMs[i] = Mathf.Lerp(_recorderMs[i], sampleMs, RECORDER_SMOOTH);
                }
            }

            if (!Visible) return;

            HandleBisectionHotkeys();

            if (Time.unscaledTime >= _nextSampleTime)
            {
                _nextSampleTime = Time.unscaledTime + SAMPLE_INTERVAL;
                Sample();
            }
        }

        // ── Bisection hotkeys ─────────────────────────────────────────────────

        private void HandleBisectionHotkeys()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            if (kb.f2Key.wasPressedThisFrame) ToggleExtraCameras();
            if (kb.f3Key.wasPressedThisFrame) ToggleSprites();
            if (kb.f4Key.wasPressedThisFrame) ToggleLights();
            if (kb.f5Key.wasPressedThisFrame) ToggleVolumes();
            if (kb.f6Key.wasPressedThisFrame) TogglePostFx();
            if (kb.f7Key.wasPressedThisFrame) ToggleBuildingColliders();
        }

        private void ToggleExtraCameras()
        {
            if (_bisectExtraCamerasOff)
            {
                foreach (var c in _disabledCameras) if (c) c.enabled = true;
                _disabledCameras.Clear();
                _bisectExtraCamerasOff = false;
            }
            else
            {
                var main = Camera.main;
                foreach (var c in Camera.allCameras)
                {
                    if (!c || c == main || !c.enabled) continue;
                    c.enabled = false;
                    _disabledCameras.Add(c);
                }
                _bisectExtraCamerasOff = true;
            }
            Debug.Log($"[BuildingsPerfProbe] Extra cameras: {(_bisectExtraCamerasOff ? "OFF" : "ON")}");
        }

        private void ToggleSprites()
        {
            if (_bisectSpritesOff)
            {
                foreach (var s in _disabledSprites) if (s) s.enabled = true;
                _disabledSprites.Clear();
                _bisectSpritesOff = false;
            }
            else
            {
                foreach (var s in Object.FindObjectsOfType<SpriteRenderer>())
                {
                    if (!s || !s.enabled) continue;
                    s.enabled = false;
                    _disabledSprites.Add(s);
                }
                _bisectSpritesOff = true;
            }
            Debug.Log($"[BuildingsPerfProbe] Sprites: {(_bisectSpritesOff ? "OFF" : "ON")}");
        }

        private void ToggleLights()
        {
            if (_bisectLightsOff)
            {
                foreach (var l in _disabledLights) if (l) l.enabled = true;
                _disabledLights.Clear();
                _bisectLightsOff = false;
            }
            else
            {
                if (_light2DType != null)
                {
                    foreach (var obj in Object.FindObjectsOfType(_light2DType))
                    {
                        var b = obj as Behaviour;
                        if (b == null || !b.enabled) continue;
                        b.enabled = false;
                        _disabledLights.Add(b);
                    }
                }
                _bisectLightsOff = true;
            }
            Debug.Log($"[BuildingsPerfProbe] Lights2D: {(_bisectLightsOff ? "OFF" : "ON")}");
        }

        private void ToggleVolumes()
        {
            if (_bisectVolumesOff)
            {
                foreach (var v in _disabledVolumes) if (v) v.enabled = true;
                _disabledVolumes.Clear();
                _bisectVolumesOff = false;
            }
            else
            {
                if (_volumeType != null)
                {
                    foreach (var obj in Object.FindObjectsOfType(_volumeType))
                    {
                        var b = obj as Behaviour;
                        if (b == null || !b.enabled) continue;
                        b.enabled = false;
                        _disabledVolumes.Add(b);
                    }
                }
                _bisectVolumesOff = true;
            }
            Debug.Log($"[BuildingsPerfProbe] Volumes: {(_bisectVolumesOff ? "OFF" : "ON")}");
        }

        private void TogglePostFx()
        {
            if (_urpCamDataType == null || _urpRenderPostProp == null) return;
            if (_bisectPostFxOff)
            {
                for (int i = 0; i < _camsWithPostFx.Count; i++)
                {
                    var c = _camsWithPostFx[i];
                    if (!c) continue;
                    var data = c.GetComponent(_urpCamDataType);
                    if (data != null) try { _urpRenderPostProp.SetValue(data, _camsPostFxOriginal[i]); } catch { }
                }
                _camsWithPostFx.Clear();
                _camsPostFxOriginal.Clear();
                _bisectPostFxOff = false;
            }
            else
            {
                foreach (var c in Camera.allCameras)
                {
                    if (!c) continue;
                    var data = c.GetComponent(_urpCamDataType);
                    if (data == null) continue;
                    try
                    {
                        bool orig = (bool)_urpRenderPostProp.GetValue(data);
                        _camsWithPostFx.Add(c);
                        _camsPostFxOriginal.Add(orig);
                        _urpRenderPostProp.SetValue(data, false);
                    }
                    catch { }
                }
                _bisectPostFxOff = true;
            }
            Debug.Log($"[BuildingsPerfProbe] PostFX: {(_bisectPostFxOff ? "OFF" : "ON")}");
        }

        private void ToggleBuildingColliders()
        {
            if (_bisectBuildingCollidersOff)
            {
                foreach (var col in _disabledBuildingColliders) if (col) col.enabled = true;
                _disabledBuildingColliders.Clear();
                _bisectBuildingCollidersOff = false;
            }
            else
            {
                foreach (var col in Object.FindObjectsOfType<Collider2D>())
                {
                    if (col == null || !col.enabled) continue;
                    if (col.gameObject.layer != BUILDING_LAYER) continue;
                    col.enabled = false;
                    _disabledBuildingColliders.Add(col);
                }
                _bisectBuildingCollidersOff = true;
            }
            Debug.Log($"[BuildingsPerfProbe] Building Colliders: {(_bisectBuildingCollidersOff ? "OFF" : "ON")}");
        }

        // ── Sample (1 Hz) ─────────────────────────────────────────────────────

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

        private void AddCell(string label, string value, GUIStyle style = null)
            => _cells.Add(new MetricCell { Label = label, Value = value, Style = style ?? _rowStyle });

        private void OnGUI()
        {
            if (!Visible) return;
            if (!_stylesReady) BuildStyles();

            _cells.Clear();

            // ── Frame rate ──
            AddCell("FPS",        $"{_fps,5:F0}  ({_frameMs:F1}ms)", FpsStyle(_fps));
            AddCell("Target FPS", $"{Application.targetFrameRate}  vsync={QualitySettings.vSyncCount}");

            // ── Buildings ──
            AddCell("Buildings",  $"vis {_buildingsVisible}/{_buildingsTotal}",
                                   WarnIfOver(_buildingsTotal, 200));
            AddCell("Bldg Cols",  $"act {_buildingCollidersActive} / en {_buildingCollidersEnabled} / tot {_buildingCollidersTotal}",
                                   WarnIfOver(_buildingCollidersActive, 300));
            AddCell("CollSess",   $"active {_colliderSessionsActive} dirty {_colliderSessionsDirty}",
                                   WarnIfOver(_colliderSessionsDirty, 0));

            // ── Scene ──
            AddCell("Sprites",    $"vis {_spritesVisible}/{_spritesTotal}",   WarnIfOver(_spritesVisible, 600));
            AddCell("Particles",  $"p{_particlesPlaying} live {_liveParticleCount}", WarnIfOver(_liveParticleCount, 3000));
            AddCell("Lights2D",   $"act {_lightsActive}/{_lightsTotal}",      WarnIfOver(_lightsActive, 24));
            AddCell("NPCs (FSM)", $"upd {_npcsUpdating}/{_npcsAlive}");
            AddCell("GameObjects",$"{_totalGameObjects}",                      WarnIfOver(_totalGameObjects, 5000));
            AddCell("MonoBehavs", $"active {_activeMonoBehaviours}",          WarnIfOver(_activeMonoBehaviours, 1500));
            AddCell("GC alloc/s", $"{_gcAllocLastSecondBytes / 1024f:F0} KB", WarnIfOver((int)(_gcAllocLastSecondBytes / 1024), 200));
            AddCell("Colliders2D",$"on {_colliders2DEnabled}/{_colliders2DActive}", WarnIfOver(_colliders2DEnabled, 600));
            AddCell("Rigidbody2D",$"{_rigidbodies2D}",                        WarnIfOver(_rigidbodies2D, 200));
            AddCell("Animators",  $"vis {_animatorsVisible}/{_animators}",    WarnIfOver(_animatorsVisible, 50));

#if UNITY_EDITOR
            AddCell("Draw calls", $"{_drawCalls} sp={_setPassCalls} b={_batches}", WarnIfOver(_drawCalls, 800));
            AddCell("Tris/Verts", $"{_triangles / 1000}k / {_vertices / 1000}k");
#endif

            // ── GPU / cameras ──
            AddCell("Cameras",    $"{_camerasActive} active",                 WarnIfOver(_camerasActive, 2));
            AddCell("Cam0",       $"{Trim(_cam0Name, 12)} {_cam0Info}");
            AddCell("Cam1",       $"{Trim(_cam1Name, 12)} {_cam1Info}");
            AddCell("Cam2",       $"{Trim(_cam2Name, 12)} {_cam2Info}");
#if UNITY_EDITOR
            AddCell("SceneView",  _sceneViewVisible ? "VISIBLE (gpu cost!)" : "hidden",
                                   _sceneViewVisible ? _warnStyle : _goodStyle);
            AddCell("GameView",   $"{_gameViewWidth}x{_gameViewHeight}");
            AddCell("RT mem",     $"{_renderTextureMemBytes / 1024 / 1024} MB", WarnIfOver((int)(_renderTextureMemBytes / 1024 / 1024), 200));
            AddCell("GPU drv",    $"{_textureMemBytes / 1024 / 1024} MB",     WarnIfOver((int)(_textureMemBytes / 1024 / 1024), 1500));
#endif
            AddCell("Screen",     $"{_screenW}x{_screenH}");
            AddCell("View w/u",   $"{_viewWidthW:F1}u  ortho={_orthoSize:F1}", WarnIfOver((int)_viewWidthW, 60));
            AddCell("ShadowCast", $"vis {_shadowCaster2DCount}/{_shadowCasterTotal}", WarnIfOver(_shadowCaster2DCount, 20));
            AddCell("LightShadw", $"{_light2DWithShadows}",                   WarnIfOver(_light2DWithShadows, 2));
            AddCell("Materials",  $"{_uniqueMaterialCount} uniq",             WarnIfOver(_uniqueMaterialCount, 30));

            // ── Bisection status ──
            AddCell("[F2] xCams",   _bisectExtraCamerasOff       ? "OFF" : "on", _bisectExtraCamerasOff       ? _warnStyle : _rowStyle);
            AddCell("[F3] Sprites", _bisectSpritesOff             ? "OFF" : "on", _bisectSpritesOff             ? _warnStyle : _rowStyle);
            AddCell("[F4] Lights",  _bisectLightsOff              ? "OFF" : "on", _bisectLightsOff              ? _warnStyle : _rowStyle);
            AddCell("[F5] Volumes", _bisectVolumesOff             ? "OFF" : "on", _bisectVolumesOff             ? _warnStyle : _rowStyle);
            AddCell("[F6] PostFX",  _bisectPostFxOff              ? "OFF" : "on", _bisectPostFxOff              ? _warnStyle : _rowStyle);
            AddCell("[F7] BldgCol", _bisectBuildingCollidersOff   ? "OFF" : "on", _bisectBuildingCollidersOff   ? _warnStyle : _rowStyle);

            // ── Profiler recorders ──
            if (_recorders != null)
            {
                for (int i = 0; i < _recorders.Length; i++)
                {
                    var rec = _recorders[i].Recorder;
                    string val = (rec != null && rec.isValid)
                        ? $"{_recorderMs[i],5:F2} ms"
                        : "n/a";
                    GUIStyle st = (rec != null && _recorderMs[i] >= 5f) ? _warnStyle : _rowStyle;
                    AddCell(ShortenMarker(_recorders[i].Label), val, st);
                }
            }

            // ── 5-column grid layout ──
            const int   COLS     = 5;
            const float COL_W    = 200f;
            const float ROW_H    = 16f;
            const float HEADER_H = 22f;
            const float PAD      = 8f;

            int   total  = _cells.Count;
            int   rows   = (total + COLS - 1) / COLS;
            float panelW = COLS * COL_W + PAD * 2f;
            float panelH = HEADER_H + rows * ROW_H + PAD * 2f + 4f;
            float x0     = 12f;
            float y0     = Screen.height - panelH - 12f;

            GUI.Box(new Rect(x0, y0, panelW, panelH), GUIContent.none);
            GUI.Label(new Rect(x0 + PAD, y0 + 4f, panelW - PAD * 2f, HEADER_H),
                      "BUILDINGS PERF PROBE", _headerStyle);

            float gridY = y0 + HEADER_H + 4f;
            for (int i = 0; i < total; i++)
            {
                int   col = i % COLS;
                int   row = i / COLS;
                float cx  = x0 + PAD + col * COL_W;
                float cy  = gridY + row * ROW_H;
                var   cell = _cells[i];
                GUI.Label(new Rect(cx,        cy, 80f,          ROW_H), cell.Label, _labelStyle);
                GUI.Label(new Rect(cx + 82f,  cy, COL_W - 84f,  ROW_H), cell.Value, cell.Style);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string ShortenMarker(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int idx  = s.LastIndexOf('.');
            string t = idx >= 0 ? s.Substring(idx + 1) : s;
            return t.Length > 18 ? t.Substring(0, 18) : t;
        }

        private static string Trim(string s, int n)
            => string.IsNullOrEmpty(s) ? "-" : (s.Length <= n ? s : s.Substring(0, n));

        private GUIStyle FpsStyle(float fps)
            => fps >= 110f ? _goodStyle : fps >= 60f ? _rowStyle : _warnStyle;

        private GUIStyle WarnIfOver(int v, int threshold) => v > threshold ? _warnStyle : _rowStyle;

        private void BuildStyles()
        {
            _headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.9f, 0.95f, 1f) } };
            _labelStyle  = new GUIStyle(GUI.skin.label) { fontSize = 11,
                normal = { textColor = new Color(0.60f, 0.62f, 0.68f) } };
            _rowStyle    = new GUIStyle(GUI.skin.label) { fontSize = 11,
                normal = { textColor = new Color(0.90f, 0.92f, 0.96f) } };
            _warnStyle   = new GUIStyle(_rowStyle) { normal = { textColor = new Color(1f, 0.85f, 0.2f) } };
            _goodStyle   = new GUIStyle(_rowStyle) { normal = { textColor = new Color(0.45f, 1f, 0.45f) } };
            _stylesReady = true;
        }
    }
}
