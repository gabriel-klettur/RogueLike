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
    public partial class BuildingsPerfProbe : MonoBehaviour
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
            // Routed through KeyboardInputManager so the legacy backend keeps
            // these probe hotkeys working when the new InputSystem package
            // drops OS events.
            if (Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(UnityEngine.InputSystem.Key.F2, KeyCode.F2)) ToggleExtraCameras();
            if (Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(UnityEngine.InputSystem.Key.F3, KeyCode.F3)) ToggleSprites();
            if (Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(UnityEngine.InputSystem.Key.F4, KeyCode.F4)) ToggleLights();
            if (Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(UnityEngine.InputSystem.Key.F5, KeyCode.F5)) ToggleVolumes();
            if (Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(UnityEngine.InputSystem.Key.F6, KeyCode.F6)) TogglePostFx();
            if (Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(UnityEngine.InputSystem.Key.F7, KeyCode.F7)) ToggleBuildingColliders();
        }

    }
}