using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.TileEditor
{
    /// <summary>
    /// IMGUI overlay that shows a focused real-time breakdown of what's costing CPU/GPU.
    /// Designed to diagnose FPS drops in the Tile Editor (panning reveals far-away
    /// content). Toggle with Shift+F8 while the editor is active.
    /// Numbers are cheap to compute and refresh at ~10 Hz to avoid biasing the measurement.
    /// </summary>
    public class TileEditorPerfProbe : MonoBehaviour
    {
        public bool Visible;

        // ── Smoothed FPS ─────────────────────────────────────────────────────
        private float _accum;
        private int   _frames;
        private float _fps;
        private float _frameMs;

        // ── 1 Hz aggregate refresh (FindObjectsOfType is expensive; keep cheap) ──
        private float _nextSampleTime;
        private const float SAMPLE_INTERVAL = 1.0f;

        // Cached counts
        private int _tilemapsTotal, _tilemapsVisible;
        private int _spritesTotal, _spritesVisible;
        private int _particlesTotal, _particlesActive, _particlesPlaying, _liveParticleCount;
        private int _lightsTotal, _lightsActive;
        private int _npcsAlive, _npcsUpdating;
        private int _totalGameObjects;
        private int _activeMonoBehaviours;
        private long _gcAllocBytes;
        private long _gcAllocLastSecondBytes;
        private float _gcLastSecondMark;
        private long _gcLastBaseline;

        // Render stats (editor-only via UnityStats)
        private int _drawCalls, _setPassCalls, _batches, _triangles, _vertices;

        // GPU diagnostics
        private int _camerasActive;
        private int _screenW, _screenH;
        private float _orthoSize, _viewWidthW;
        private long _renderTextureMemBytes;     // editor-only
        private long _textureMemBytes;            // editor-only
        private int _shadowCaster2DCount, _shadowCasterTotal;
        private int _light2DWithShadows;
        private int _spritesNonStaticBatched;     // unique materials = poor batching
        private int _uniqueMaterialCount;
        private int _transparentSpriteCount;      // sprites with alpha-blend material
        private int _tilemapsChunked, _tilemapsIndividual;
        private float _estimatedOverdrawMultiplier; // tilesInView * layers / pixelsInView (rough)

        // Camera identification (which 2 cameras are active?)
        private string _cam0Name, _cam1Name, _cam2Name;
        private string _cam0Info, _cam1Info, _cam2Info;
        private bool _sceneViewVisible;
        private int _gameViewWidth, _gameViewHeight;

        // Bisection toggles (hotkeys to disable categories live)
        private bool _bisectExtraCamerasOff;
        private bool _bisectSpritesOff;
        private bool _bisectLightsOff;
        private bool _bisectVolumesOff;
        private bool _bisectPostFxOff;
        private bool _bisectExtraTilemapsOff;
        private readonly System.Collections.Generic.List<Camera> _disabledCameras = new System.Collections.Generic.List<Camera>();
        private readonly System.Collections.Generic.List<SpriteRenderer> _disabledSprites = new System.Collections.Generic.List<SpriteRenderer>();
        private readonly System.Collections.Generic.List<Behaviour> _disabledLights = new System.Collections.Generic.List<Behaviour>();
        private readonly System.Collections.Generic.List<Behaviour> _disabledVolumes = new System.Collections.Generic.List<Behaviour>();
        private readonly System.Collections.Generic.List<TilemapRenderer> _disabledTilemaps = new System.Collections.Generic.List<TilemapRenderer>();
        private readonly System.Collections.Generic.List<Camera> _camsWithPostFx = new System.Collections.Generic.List<Camera>();
        private readonly System.Collections.Generic.List<bool> _camsPostFxOriginal = new System.Collections.Generic.List<bool>();

        // Cached reflection for URP camera additional data (postProcessing toggle)
        private System.Type _urpCamDataType;
        private PropertyInfo _urpRenderPostProp;
        private MethodInfo _urpGetExtraData;

        // Volume type cached
        private System.Type _volumeType;

        // Tile counts (camera-window)
        private int _tilesInView;
        private int _tilemapColliders;
        private int _colliders2DActive, _colliders2DEnabled;
        private int _rigidbodies2D;
        private int _animators, _animatorsVisible;

        // ── Profiler.Recorder — in-build CPU timing per Unity built-in marker ──
        // These show ms/frame for the most relevant Unity systems. Goal: figure out
        // which one spikes when panning to the slow zone.
        private struct RecorderRow { public string Label; public Recorder Recorder; }
        private RecorderRow[] _recorders;
        private float[] _recorderMs;     // smoothed ms per recorder
        private const float RECORDER_SMOOTH = 0.25f;

        // Reflection cache — Light2D belongs to URP assembly that we don't want to hard-link.
        private System.Type _light2DType;

        // Cached camera info
        private Camera _cam;

        // GUI styles
        private GUIStyle _headerStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _rowStyle;
        private GUIStyle _warnStyle;
        private GUIStyle _goodStyle;
        private bool _stylesReady;

        private void Awake()
        {
            _light2DType = System.Type.GetType(
                "UnityEngine.Rendering.Universal.Light2D, Unity.RenderPipelines.Universal.Runtime");
            Debug.Log($"[PerfProbe] Awake. Light2D type resolved: {_light2DType != null}");

            // URP camera additional data — to toggle postFX
            _urpCamDataType = System.Type.GetType(
                "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (_urpCamDataType != null)
            {
                _urpRenderPostProp = _urpCamDataType.GetProperty("renderPostProcessing",
                    BindingFlags.Public | BindingFlags.Instance);
            }

            // Volume (post-processing) type
            _volumeType = System.Type.GetType("UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime");

            // Wire Profiler recorders. These are Unity built-in markers that work in any build.
            string[] markers = {
                "BehaviourUpdate",         // total cost of all MonoBehaviour.Update()
                "LateBehaviourUpdate",     // all LateUpdate()
                "FixedBehaviourUpdate",    // all FixedUpdate()
                "Camera.Render",           // total camera rendering
                "PlayerLoop.PostLateUpdate.UpdateAllRenderers",
                "Physics2D.Simulate",
                "Tilemap.RefreshTile",     // tilemap refresh churn
                "GC.Collect",              // GC pauses
                "Gfx.WaitForPresentOnGfxThread",  // GPU stall / vSync wait (CPU blocked on GPU)
                "WaitForTargetFPS",               // CPU idle waiting for frame budget (target FPS cap)
                "Gfx.PresentFrame",               // GPU present time
                "RenderLoop.DrawSRPBatcher",      // SRP Batcher cost
                "RenderLoop.Draw",                // Total render loop
                "Shadows.RenderJobDir",           // Shadow rendering
                "Render.OpaqueGeometry",
                "Render.TransparentGeometry",
                "Light2D.Render",                 // URP 2D lights pass
                "Canvas.SendWillRenderCanvases",  // UI cost
                "EditorLoop",                     // Editor overhead in Play mode
                "EditorOverhead",
            };
            _recorders = new RecorderRow[markers.Length];
            _recorderMs = new float[markers.Length];
            for (int i = 0; i < markers.Length; i++)
            {
                var rec = Recorder.Get(markers[i]);
                if (rec != null) rec.enabled = true;
                _recorders[i] = new RecorderRow { Label = markers[i], Recorder = rec };
            }
        }

        private void OnEnable()
        {
            Debug.Log($"[PerfProbe] OnEnable. Visible={Visible}");
        }

        private void Update()
        {
            // Smoothed FPS over the last 0.5s
            _accum += Time.unscaledDeltaTime;
            _frames++;
            if (_accum >= 0.5f)
            {
                _fps = _frames / _accum;
                _frameMs = (_accum / _frames) * 1000f;
                _accum = 0f;
                _frames = 0;
            }

            // Sample profiler recorders every frame (cheap) so values are accurate.
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

            // ── Bisection hotkeys (only when probe visible) ──
            HandleBisectionHotkeys();

            if (Time.unscaledTime >= _nextSampleTime)
            {
                _nextSampleTime = Time.unscaledTime + SAMPLE_INTERVAL;
                Sample();
            }
        }

        // ── Bisection: live-toggle categories to find the GPU culprit ──

        private void HandleBisectionHotkeys()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;

            if (kb.f2Key.wasPressedThisFrame) ToggleExtraCameras();
            if (kb.f3Key.wasPressedThisFrame) ToggleSprites();
            if (kb.f4Key.wasPressedThisFrame) ToggleLights();
            if (kb.f5Key.wasPressedThisFrame) ToggleVolumes();
            if (kb.f6Key.wasPressedThisFrame) TogglePostFx();
            if (kb.f7Key.wasPressedThisFrame) ToggleExtraTilemaps();
        }

        private void ToggleExtraCameras()
        {
            if (_bisectExtraCamerasOff)
            {
                foreach (var c in _disabledCameras) if (c != null) c.enabled = true;
                _disabledCameras.Clear();
                _bisectExtraCamerasOff = false;
                Debug.Log("[PerfProbe] Extra cameras: ON");
            }
            else
            {
                var main = Camera.main;
                foreach (var c in Camera.allCameras)
                {
                    if (c == null || c == main || !c.enabled) continue;
                    c.enabled = false;
                    _disabledCameras.Add(c);
                }
                _bisectExtraCamerasOff = true;
                Debug.Log($"[PerfProbe] Extra cameras: OFF ({_disabledCameras.Count} disabled)");
            }
        }

        private void ToggleSprites()
        {
            if (_bisectSpritesOff)
            {
                foreach (var s in _disabledSprites) if (s != null) s.enabled = true;
                _disabledSprites.Clear();
                _bisectSpritesOff = false;
                Debug.Log("[PerfProbe] Sprites: ON");
            }
            else
            {
                foreach (var s in Object.FindObjectsOfType<SpriteRenderer>())
                {
                    if (s == null || !s.enabled) continue;
                    s.enabled = false;
                    _disabledSprites.Add(s);
                }
                _bisectSpritesOff = true;
                Debug.Log($"[PerfProbe] Sprites: OFF ({_disabledSprites.Count} disabled)");
            }
        }

        private void ToggleLights()
        {
            if (_bisectLightsOff)
            {
                foreach (var l in _disabledLights) if (l != null) l.enabled = true;
                _disabledLights.Clear();
                _bisectLightsOff = false;
                Debug.Log("[PerfProbe] Lights2D: ON");
            }
            else
            {
                if (_light2DType != null)
                {
                    var lights = Object.FindObjectsOfType(_light2DType);
                    foreach (var l in lights)
                    {
                        var b = l as Behaviour;
                        if (b == null || !b.enabled) continue;
                        b.enabled = false;
                        _disabledLights.Add(b);
                    }
                }
                _bisectLightsOff = true;
                Debug.Log($"[PerfProbe] Lights2D: OFF ({_disabledLights.Count} disabled)");
            }
        }

        private void ToggleVolumes()
        {
            if (_bisectVolumesOff)
            {
                foreach (var v in _disabledVolumes) if (v != null) v.enabled = true;
                _disabledVolumes.Clear();
                _bisectVolumesOff = false;
                Debug.Log("[PerfProbe] Volumes: ON");
            }
            else
            {
                if (_volumeType != null)
                {
                    var vols = Object.FindObjectsOfType(_volumeType);
                    foreach (var v in vols)
                    {
                        var b = v as Behaviour;
                        if (b == null || !b.enabled) continue;
                        b.enabled = false;
                        _disabledVolumes.Add(b);
                    }
                }
                _bisectVolumesOff = true;
                Debug.Log($"[PerfProbe] Volumes: OFF ({_disabledVolumes.Count} disabled)");
            }
        }

        private void TogglePostFx()
        {
            if (_urpCamDataType == null || _urpRenderPostProp == null)
            {
                Debug.LogWarning("[PerfProbe] URP camera data not available — cannot toggle PostFX");
                return;
            }
            if (_bisectPostFxOff)
            {
                for (int i = 0; i < _camsWithPostFx.Count; i++)
                {
                    var c = _camsWithPostFx[i];
                    if (c == null) continue;
                    var data = c.GetComponent(_urpCamDataType);
                    if (data != null) try { _urpRenderPostProp.SetValue(data, _camsPostFxOriginal[i]); } catch { }
                }
                _camsWithPostFx.Clear();
                _camsPostFxOriginal.Clear();
                _bisectPostFxOff = false;
                Debug.Log("[PerfProbe] PostFX: restored");
            }
            else
            {
                foreach (var c in Camera.allCameras)
                {
                    if (c == null) continue;
                    var data = c.GetComponent(_urpCamDataType);
                    if (data == null) continue;
                    try
                    {
                        bool original = (bool)_urpRenderPostProp.GetValue(data);
                        _camsWithPostFx.Add(c);
                        _camsPostFxOriginal.Add(original);
                        _urpRenderPostProp.SetValue(data, false);
                    }
                    catch { }
                }
                _bisectPostFxOff = true;
                Debug.Log($"[PerfProbe] PostFX: OFF ({_camsWithPostFx.Count} cameras)");
            }
        }

        private void ToggleExtraTilemaps()
        {
            if (_bisectExtraTilemapsOff)
            {
                foreach (var t in _disabledTilemaps) if (t != null) t.enabled = true;
                _disabledTilemaps.Clear();
                _bisectExtraTilemapsOff = false;
                Debug.Log("[PerfProbe] Tilemaps: ON");
            }
            else
            {
                // Disable everything except ground-ish layer (lowest sortingOrder)
                var tms = Object.FindObjectsOfType<TilemapRenderer>();
                int minOrder = int.MaxValue;
                foreach (var t in tms) if (t.enabled && t.sortingOrder < minOrder) minOrder = t.sortingOrder;
                foreach (var t in tms)
                {
                    if (t == null || !t.enabled || t.sortingOrder == minOrder) continue;
                    t.enabled = false;
                    _disabledTilemaps.Add(t);
                }
                _bisectExtraTilemapsOff = true;
                Debug.Log($"[PerfProbe] Tilemaps: kept order={minOrder}, OFF {_disabledTilemaps.Count}");
            }
        }

        // ── Counts ──────────────────────────────────────────────────────────

        private void Sample()
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

            // ── Collider2D + Rigidbody2D counts ──
            var cols = Object.FindObjectsOfType<Collider2D>();
            _colliders2DActive = cols.Length;
            _colliders2DEnabled = 0;
            for (int i = 0; i < cols.Length; i++) if (cols[i].enabled) _colliders2DEnabled++;

            _rigidbodies2D = Object.FindObjectsOfType<Rigidbody2D>().Length;

            // ── Animators visible ──
            var anims = Object.FindObjectsOfType<Animator>();
            _animators = anims.Length;
            _animatorsVisible = 0;
            for (int i = 0; i < anims.Length; i++)
            {
                if (!anims[i].isActiveAndEnabled) continue;
                // Cheap viewport check
                Vector3 p = anims[i].transform.position;
                if (Mathf.Abs(p.x - cp.x) <= halfW + 2f && Mathf.Abs(p.y - cp.y) <= halfH + 2f)
                    _animatorsVisible++;
            }

            // ── GPU material/shadow/tilemap breakdown ──
            // Tilemap render modes (Chunk vs Individual). Individual = per-tile draw call.
            _tilemapsChunked = 0;
            _tilemapsIndividual = 0;
            for (int i = 0; i < tilemaps.Length; i++)
            {
                if (tilemaps[i] == null) continue;
                var tr = tilemaps[i].GetComponent<TilemapRenderer>();
                if (tr == null || !tr.enabled) continue;
                if (tr.mode == TilemapRenderer.Mode.Chunk) _tilemapsChunked++;
                else _tilemapsIndividual++;
            }

            // Sprite material uniqueness (more unique mats = more SetPass = bad batching)
            _uniqueMaterialCount = 0;
            _transparentSpriteCount = 0;
            _spritesNonStaticBatched = 0;
            var matSet = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < srs.Length; i++)
            {
                if (srs[i] == null || !srs[i].enabled || !srs[i].isVisible) continue;
                var sm = srs[i].sharedMaterial;
                if (sm != null) matSet.Add(sm.GetInstanceID());
                // sprite default shader is alpha-blended already
                _transparentSpriteCount++;
                if (srs[i].HasPropertyBlock()) _spritesNonStaticBatched++;
            }
            _uniqueMaterialCount = matSet.Count;

            // 2D Light shadow casters (URP). Reflection: ShadowCaster2D in
            // UnityEngine.Rendering.Universal namespace.
            _shadowCasterTotal = 0;
            _shadowCaster2DCount = 0;
            var shadowType = System.Type.GetType(
                "UnityEngine.Rendering.Universal.ShadowCaster2D, Unity.RenderPipelines.Universal.Runtime");
            if (shadowType != null)
            {
                var shads = Object.FindObjectsOfType(shadowType);
                _shadowCasterTotal = shads.Length;
                for (int i = 0; i < shads.Length; i++)
                {
                    var c = shads[i] as Component;
                    if (c != null && c.gameObject.activeInHierarchy && c.GetComponent<Behaviour>()?.enabled != false)
                    {
                        // Viewport cull check
                        Vector3 p = c.transform.position;
                        if (Mathf.Abs(p.x - cp.x) <= halfW + 4f && Mathf.Abs(p.y - cp.y) <= halfH + 4f)
                            _shadowCaster2DCount++;
                    }
                }
            }

            // Light2D with shadows enabled (reflection)
            _light2DWithShadows = 0;
            if (_light2DType != null)
            {
                var lights = Object.FindObjectsOfType(_light2DType);
                var shadowProp = _light2DType.GetProperty("shadowsEnabled",
                    BindingFlags.Public | BindingFlags.Instance);
                for (int i = 0; i < lights.Length; i++)
                {
                    var c = lights[i] as Component;
                    if (c == null || !c.gameObject.activeInHierarchy) continue;
                    if (shadowProp != null)
                    {
                        try
                        {
                            object v = shadowProp.GetValue(c);
                            if (v is bool b && b) _light2DWithShadows++;
                        }
                        catch { /* ignore */ }
                    }
                }
            }

            // Rough overdraw estimate: tilesInView × visible sprites in view, projected to screen pixels.
            // Useful only as a "is it big or small" indicator; not an exact metric.
            float pixelsInView = (float)_screenW * _screenH;
            float coveredPixels = (_tilesInView + _spritesVisible) * 16f * 16f * 4f; // 16PPU, ~4x scale guess
            _estimatedOverdrawMultiplier = pixelsInView > 0 ? coveredPixels / pixelsInView : 0f;
        }

        // ── IMGUI ───────────────────────────────────────────────────────────

        private struct MetricCell { public string Label; public string Value; public GUIStyle Style; }
        private readonly List<MetricCell> _cells = new List<MetricCell>(40);

        private void AddCell(string label, string value, GUIStyle style = null)
        {
            _cells.Add(new MetricCell { Label = label, Value = value, Style = style ?? _rowStyle });
        }

        private void OnGUI()
        {
            if (!Visible) return;
            if (!_stylesReady) BuildStyles();

            // ── Build the metric list ──
            _cells.Clear();
            AddCell("FPS",        $"{_fps,5:F0}  ({_frameMs:F1}ms)", FpsStyle(_fps));
            AddCell("Target FPS", $"{Application.targetFrameRate}  vSync={QualitySettings.vSyncCount}");
            AddCell("Tilemaps",   $"vis {_tilemapsVisible}/{_tilemapsTotal}");
            AddCell("Sprites",    $"vis {_spritesVisible}/{_spritesTotal}",  WarnIfOver(_spritesVisible, 600));
            AddCell("Particle FX",$"p{_particlesPlaying} a{_particlesActive}/{_particlesTotal}",
                                   WarnIfOver(_particlesPlaying, 50));
            AddCell("Particles",  $"{_liveParticleCount} live",              WarnIfOver(_liveParticleCount, 5000));
            AddCell("Lights2D",   $"act {_lightsActive}/{_lightsTotal}",     WarnIfOver(_lightsActive, 24));
            AddCell("NPCs (FSM)", $"upd {_npcsUpdating}/{_npcsAlive}");
            AddCell("GameObjects",$"{_totalGameObjects}",                    WarnIfOver(_totalGameObjects, 5000));
            AddCell("MonoBehavs", $"active {_activeMonoBehaviours}",         WarnIfOver(_activeMonoBehaviours, 1500));
            AddCell("GC alloc/s", $"{_gcAllocLastSecondBytes / 1024f,5:F0} KB",
                                   WarnIfOver((int)(_gcAllocLastSecondBytes / 1024), 200));
#if UNITY_EDITOR
            AddCell("Draw calls", $"{_drawCalls} sp={_setPassCalls} b={_batches}",
                                   WarnIfOver(_drawCalls, 800));
            AddCell("Tris/Verts", $"{_triangles / 1000}k / {_vertices / 1000}k");
#endif
            AddCell("Tiles view", $"{_tilesInView}  TC2D={_tilemapColliders}",
                                   WarnIfOver(_tilesInView, 4000));
            AddCell("Colliders2D",$"on {_colliders2DEnabled}/{_colliders2DActive}",
                                   WarnIfOver(_colliders2DEnabled, 600));
            AddCell("Rigidbody2D",$"{_rigidbodies2D}", WarnIfOver(_rigidbodies2D, 200));
            AddCell("Animators",  $"vis {_animatorsVisible}/{_animators}",   WarnIfOver(_animatorsVisible, 50));

            if (_cam != null)
            {
                float ch = _cam.orthographicSize;
                float cw = ch * _cam.aspect;
                Vector3 cpv = _cam.transform.position;
                AddCell("Camera",   $"o={ch:F0} ({cpv.x:F0},{cpv.y:F0})");
                AddCell("Viewport", $"{cw * 2f:F0}x{ch * 2f:F0} u");
            }

            // ── GPU diagnostics block ──
            AddCell("Cameras",     $"{_camerasActive} active",                 WarnIfOver(_camerasActive, 2));
            AddCell("Cam0",        $"{Trim(_cam0Name,12)} {_cam0Info}");
            AddCell("Cam1",        $"{Trim(_cam1Name,12)} {_cam1Info}");
            AddCell("Cam2",        $"{Trim(_cam2Name,12)} {_cam2Info}");
            AddCell("SceneView",   _sceneViewVisible ? "VISIBLE (gpu cost!)" : "hidden",
                                   _sceneViewVisible ? _warnStyle : _goodStyle);
            AddCell("GameView res",$"{_gameViewWidth}x{_gameViewHeight}");
            AddCell("Screen",      $"{_screenW}x{_screenH}");
            AddCell("View w/u",    $"{_viewWidthW:F1} u  ortho={_orthoSize:F1}", WarnIfOver((int)_viewWidthW, 60));
            AddCell("Overdraw~",   $"{_estimatedOverdrawMultiplier:F1}x est",  WarnIfOver((int)_estimatedOverdrawMultiplier, 4));
#if UNITY_EDITOR
            AddCell("RT mem",      $"{_renderTextureMemBytes / 1024 / 1024} MB", WarnIfOver((int)(_renderTextureMemBytes / 1024 / 1024), 200));
            AddCell("GPU drv",     $"{_textureMemBytes / 1024 / 1024} MB",     WarnIfOver((int)(_textureMemBytes / 1024 / 1024), 1500));
#endif
            AddCell("ShadowCast2D",$"vis {_shadowCaster2DCount}/{_shadowCasterTotal}", WarnIfOver(_shadowCaster2DCount, 20));
            AddCell("Light shdw",  $"{_light2DWithShadows}",                   WarnIfOver(_light2DWithShadows, 2));
            AddCell("TM modes",    $"chnk {_tilemapsChunked} ind {_tilemapsIndividual}", WarnIfOver(_tilemapsIndividual, 0));
            AddCell("Materials",   $"{_uniqueMaterialCount} uniq",             WarnIfOver(_uniqueMaterialCount, 30));
            AddCell("PropBlocks",  $"{_spritesNonStaticBatched} sprites",      WarnIfOver(_spritesNonStaticBatched, 50));
            AddCell("Transp spr",  $"{_transparentSpriteCount}",               WarnIfOver(_transparentSpriteCount, 200));

            // ── Bisection status ──
            AddCell("[F2] xCams",  _bisectExtraCamerasOff ? "OFF" : "on",      _bisectExtraCamerasOff ? _warnStyle : _rowStyle);
            AddCell("[F3] Sprites",_bisectSpritesOff ? "OFF" : "on",           _bisectSpritesOff ? _warnStyle : _rowStyle);
            AddCell("[F4] Lights", _bisectLightsOff ? "OFF" : "on",            _bisectLightsOff ? _warnStyle : _rowStyle);
            AddCell("[F5] Volumes",_bisectVolumesOff ? "OFF" : "on",           _bisectVolumesOff ? _warnStyle : _rowStyle);
            AddCell("[F6] PostFX", _bisectPostFxOff ? "OFF" : "on",            _bisectPostFxOff ? _warnStyle : _rowStyle);
            AddCell("[F7] xTMs",   _bisectExtraTilemapsOff ? "OFF" : "on",     _bisectExtraTilemapsOff ? _warnStyle : _rowStyle);

            // CPU breakdown rows
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
            const int COLS = 5;
            const float COL_W = 200f;
            const float ROW_H = 16f;
            const float HEADER_H = 22f;
            const float PAD = 8f;

            int total = _cells.Count;
            int rows = (total + COLS - 1) / COLS;

            float panelW = COLS * COL_W + PAD * 2f;
            float panelH = HEADER_H + rows * ROW_H + PAD * 2f + 4f;

            float x0 = 12f;
            float y0 = 80f;

            GUI.Box(new Rect(x0, y0, panelW, panelH), GUIContent.none);

            // Header
            GUI.Label(new Rect(x0 + PAD, y0 + 4f, panelW - PAD * 2f, HEADER_H),
                      "PERF PROBE  (Shift+F8 to hide)", _headerStyle);

            // Grid: row-major fill (cell index 0..3 → first row, 4..7 → second row, etc.)
            float gridY = y0 + HEADER_H + 4f;
            for (int i = 0; i < total; i++)
            {
                int col = i % COLS;
                int row = i / COLS;
                float cx = x0 + PAD + col * COL_W;
                float cy = gridY + row * ROW_H;

                var c = _cells[i];
                // Label takes ~40% of the column, value the rest
                GUI.Label(new Rect(cx,             cy, 80f,           ROW_H), c.Label, _labelStyle);
                GUI.Label(new Rect(cx + 82f,       cy, COL_W - 84f,   ROW_H), c.Value, c.Style);
            }
        }

        // Strip noisy prefixes from Profiler marker names so they fit in the column
        private static string ShortenMarker(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            // Drop everything before the last '.' segment, but keep meaningful names short.
            // e.g. "PlayerLoop.PostLateUpdate.UpdateAllRenderers" -> "UpdateAllRenderers"
            int idx = s.LastIndexOf('.');
            string tail = idx >= 0 ? s.Substring(idx + 1) : s;
            if (tail.Length > 18) tail = tail.Substring(0, 18);
            return tail;
        }

        private static string Trim(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "-";
            return s.Length <= n ? s : s.Substring(0, n);
        }

        private GUIStyle FpsStyle(float fps)
        {
            return fps >= 110f ? _goodStyle
                 : fps >= 60f ? _rowStyle
                 :              _warnStyle;
        }

        private GUIStyle WarnIfOver(int v, int threshold) => v > threshold ? _warnStyle : _rowStyle;

        private void BuildStyles()
        {
            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12, fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.9f, 0.95f, 1f) }
            };
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.60f, 0.62f, 0.68f) }
            };
            _rowStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.90f, 0.92f, 0.96f) }
            };
            _warnStyle = new GUIStyle(_rowStyle)
            {
                normal = { textColor = new Color(1f, 0.85f, 0.2f) }
            };
            _goodStyle = new GUIStyle(_rowStyle)
            {
                normal = { textColor = new Color(0.45f, 1f, 0.45f) }
            };
            _stylesReady = true;
        }
    }
}
