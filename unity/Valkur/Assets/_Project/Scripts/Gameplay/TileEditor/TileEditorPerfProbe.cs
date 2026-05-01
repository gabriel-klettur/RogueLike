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
    public partial class TileEditorPerfProbe : MonoBehaviour
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
            // Routed through KeyboardInputManager so the legacy backend keeps
            // these probe hotkeys working when the new InputSystem package
            // drops OS events.
            if (Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(UnityEngine.InputSystem.Key.F2, KeyCode.F2)) ToggleExtraCameras();
            if (Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(UnityEngine.InputSystem.Key.F3, KeyCode.F3)) ToggleSprites();
            if (Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(UnityEngine.InputSystem.Key.F4, KeyCode.F4)) ToggleLights();
            if (Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(UnityEngine.InputSystem.Key.F5, KeyCode.F5)) ToggleVolumes();
            if (Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(UnityEngine.InputSystem.Key.F6, KeyCode.F6)) TogglePostFx();
            if (Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(UnityEngine.InputSystem.Key.F7, KeyCode.F7)) ToggleExtraTilemaps();
        }

    }
}