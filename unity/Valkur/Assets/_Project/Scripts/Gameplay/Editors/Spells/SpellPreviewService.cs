using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Gameplay.Spells
{
    /// <summary>
    /// Off-screen, looping live-preview of a single <see cref="SpellDefinition"/>
    /// for the in-game Spells Runtime Editor (F4) "View" panel.
    ///
    /// Mirrors the camera/RT pattern of <see cref="VFX.ParticlePreviewService"/>:
    ///   • A dedicated orthographic camera at far-offscreen Y, culling only the
    ///     <c>SpellPreview</c> Unity layer, renders to a single <see cref="RenderTexture"/>
    ///     consumed by a <c>RawImage</c> in the View panel.
    ///   • A synthetic caster <see cref="Transform"/> sits at the centre of the stage.
    ///     Spells are fired by invoking the appropriate <see cref="ISpellExecutor"/>
    ///     directly — bypassing <see cref="SpellCaster.TryCast"/> so mana cost,
    ///     cooldown FSM, and SFX dispatch are all skipped.
    ///   • A simple time-based loop fires one cast, waits a cycle, clears the stage,
    ///     waits a small idle gap, and repeats.
    ///
    /// Frame capture / transport:
    ///   • During Active loop state each rendered frame is copied into a Texture2D
    ///     and appended to <c>_frames</c> (capped at MAX_CAPTURED_FRAMES = 240).
    ///   • Three transport modes govern Tick behaviour:
    ///       Live  — existing spell-fire loop + capture (default).
    ///       Slow  — no spell firing; replay captured frames at a reduced rate.
    ///       Paused — no advancement; expose a single chosen frame.
    /// </summary>
    public sealed partial class SpellPreviewService
    {
        // ── Constants ────────────────────────────────────────────────────────────

        internal const int   RT_SIZE            = 384;
        internal const float OFFSCREEN_Y        = -20000f;   // far from ParticlePreview (-10000)
        internal const float CAMERA_Z           = -50f;
        // ORTHO_SIZE_MIN: lower-bound for the camera ortho so tiny radial spells
        // (Aura radius=0.6, Slash hitRadius=1.25, …) don't crush the view onto
        // the spell — the player sprite needs ~3 wu of vertical space.
        internal const float ORTHO_SIZE_MIN     = 4f;
        // ORTHO_SIZE_MAX is intentionally unused as a clamp: the auto-fit must
        // grow to cover the FULL spell reach AND the caster so the user can always
        // see "both spell and player". Long-range spells use a wider ortho.
        internal const float ORTHO_SIZE_MAX     = 200f;
        internal const float ORTHO_SIZE_DEFAULT = 6f;
        internal const float BOUNDS_PADDING     = 0.5f;
        internal const float IDLE_GAP_SECONDS   = 0.25f;
        internal const float MIN_CYCLE_SECONDS  = 1.5f;
        internal const float MIN_PERSISTENT_SECONDS = 2.0f;
        internal const float CYCLE_TAIL_SECONDS = 0.2f;
        // User zoom: applied AFTER auto-fit. Values >1 zoom in (smaller ortho size), <1 zoom out.
        internal const float USER_ZOOM_MIN  = 0.25f;
        internal const float USER_ZOOM_MAX  = 6f;
        internal const float USER_ZOOM_STEP = 1.2f;

        // Frame capture: cap prevents unbounded Texture2D allocations for long spells.
        // 240 frames covers ~4 s at 60 fps — more than enough for any spell VFX cycle.
        internal const int   MAX_CAPTURED_FRAMES   = 240;
        internal const float NOMINAL_FRAME_DURATION = 1f / 60f;

        // Degenerate-bounds threshold: if the encapsulated size is bigger than this,
        // something stale or pooled is corrupting the framing calculation.
        internal const float BOUNDS_SANITY_RADIUS = 100f;

        // ── Transport state ──────────────────────────────────────────────────────

        /// <summary>
        /// Controls what the preview loop does each Tick.
        /// Live  = live spell fires and captures frames (default).
        /// Slow  = replay captured frames at a reduced rate; spell is not fired.
        /// Paused = frozen on a single chosen frame; nothing advances.
        /// </summary>
        public enum TransportMode { Live, Slow, Paused }

        internal TransportMode _transport = TransportMode.Live;

        // Captured frame buffer — one Texture2D per rendered frame during Active.
        internal readonly List<Texture2D> _frames = new List<Texture2D>();

        // Index of the frame currently shown in Slow/Paused mode.
        internal int _displayedFrame;

        // Fractional accumulator used to pace slow-motion playback between ticks.
        internal float _slowPlaybackAccum;

        // Playback speed for Slow mode (0.25 or 0.5).
        internal float _playbackSpeed = 1f;

        // ── Core state ───────────────────────────────────────────────────────────

        internal bool             _initialized;
        internal bool             _open;
        internal Camera           _camera;
        internal RenderTexture    _rt;
        internal GameObject       _stageRoot;
        internal GameObject       _casterGo;
        internal Transform        _casterTransform;
        internal Vector2          _direction = Vector2.right;
        internal SpellDefinition  _spell;
        internal GameObject       _projectilePrefab;
        internal bool             _projectilePrefabResolved;
        internal float            _userZoom = 1f;

        // Character overlay — child of _casterGo carrying the player SpriteRenderer.
        internal bool             _showCharacter;
        internal GameObject       _characterGo;
        internal Valkur.Gameplay.DirectionalAnimator _characterAnimator;

        // Loop timing.
        internal enum LoopState { Idle, Active, Cooldown }
        internal LoopState _loopState = LoopState.Idle;
        internal float     _loopTimer;

        // Tracking world-rooted spawns (e.g. Projectile) so they get cleaned per cycle.
        internal readonly List<GameObject> _trackedWorldSpawns = new List<GameObject>();

        // Baseline of pre-existing world VFX captured on Open() so newly spawned
        // ParticleSystems / impact-preset emitters created any time during a cycle
        // can be absorbed onto the SpellPreview layer and tracked for cleanup.
        internal readonly HashSet<GameObject> _baselineWorldVfx = new HashSet<GameObject>();
        internal readonly HashSet<GameObject> _absorbedWorldVfx = new HashSet<GameObject>();

        // Snapshot of every scene-root GO that existed at FireOnce time. Used by
        // AbsorbNewSceneRoots() each Active-state Tick to detect async spawns.
        internal HashSet<GameObject> _baselineSceneRoots = new HashSet<GameObject>();

        // One-shot error logging — avoids per-cycle console spam on misconfigured spells.
        internal bool _warnedNoExecutor;

        // Locked-bounds framing: seeded from the spell's metadata on SetSelectedSpell
        // so the camera ortho stays stable across the spell's full cycle. Never shrinks.
        internal Bounds _lockedBounds;
        internal bool   _lockedBoundsInitialized;

        // Range ruler — red line below the caster with a tiles label at the midpoint.
        internal GameObject     _rangeRulerGo;
        internal LineRenderer   _rangeRulerLine;
        internal TextMeshPro    _rangeRulerLabel;
        internal Material       _rangeRulerMaterial;
        internal const float    RULER_Y_OFFSET       = 1.5f;
        internal const float    RULER_LINE_WIDTH      = 0.10f;
        internal const float    RULER_LABEL_Y_OFFSET  = 0.6f;
        internal static readonly Color RULER_COLOR    = new Color(0.95f, 0.20f, 0.20f, 1f);

        // ── Character overlay public API ─────────────────────────────────────────

        /// <summary>Whether the character sprite overlay is currently shown on the preview stage.</summary>
        public bool ShowCharacter => _showCharacter;

        /// <summary>Attach or detach the active player's sprite on the synthetic caster.</summary>
        public void SetShowCharacter(bool show)
        {
            _showCharacter = show;
            ApplyCharacterState();
        }

        // ── Transport public API ─────────────────────────────────────────────────

        /// <summary>Number of Texture2Ds currently held in the capture buffer.</summary>
        public int CapturedFrameCount => _frames.Count;

        /// <summary>Current transport mode (Live / Slow / Paused).</summary>
        public TransportMode CurrentTransport => _transport;

        /// <summary>Current playback speed (0.25 / 0.5 / 1.0).</summary>
        public float PlaybackSpeed => _playbackSpeed;

        /// <summary>
        /// Index of the frame that GetDisplayTexture() currently returns in
        /// Slow or Paused mode.  In Live mode this is meaningless (returns _rt).
        /// </summary>
        public int DisplayedFrame => _displayedFrame;

        /// <summary>
        /// Returns the texture that should be shown in the RawImage each frame.
        /// Live → the live RenderTexture.
        /// Slow / Paused → the captured Texture2D at _displayedFrame (or _rt as fallback).
        /// </summary>
        public Texture GetDisplayTexture()
        {
            if (_transport == TransportMode.Live) return _rt;
            if (_frames.Count == 0)               return _rt;
            int idx = Mathf.Clamp(_displayedFrame, 0, _frames.Count - 1);
            return _frames[idx];
        }

        /// <summary>
        /// Switch transport mode.  Transitioning TO Live clears the capture buffer
        /// so the next Active cycle starts a fresh recording.
        /// </summary>
        public void SetTransport(TransportMode mode, float speed = 1f)
        {
            if (_transport == mode && Mathf.Approximately(_playbackSpeed, speed)) return;

            bool towardLive = mode == TransportMode.Live;
            _transport = mode;
            _playbackSpeed = Mathf.Clamp(speed, 0.0001f, 1f);
            _slowPlaybackAccum = 0f;

            if (towardLive)
            {
                DisposeAllFrames();
                ClearStage();
                _loopState = LoopState.Idle;
                _loopTimer = 0f;
            }
            else if (mode == TransportMode.Paused)
            {
                _displayedFrame = Mathf.Max(0, _frames.Count - 1);
            }
            // Slow: keep _displayedFrame as-is so switching from Paused or
            // changing speed doesn't jump the scrubber position unexpectedly.
        }

        /// <summary>Step the displayed frame by delta (positive = forward). Only meaningful in Slow or Paused mode.</summary>
        public void StepFrame(int delta)
        {
            if (_frames.Count == 0) return;
            _displayedFrame = Mathf.Clamp(_displayedFrame + delta, 0, _frames.Count - 1);
        }

        /// <summary>Seek to a normalised position in the capture buffer (0 = first frame, 1 = last).</summary>
        public void SeekToFraction(float t01)
        {
            if (_frames.Count == 0) return;
            _displayedFrame = Mathf.RoundToInt(t01 * (_frames.Count - 1));
            _displayedFrame = Mathf.Clamp(_displayedFrame, 0, _frames.Count - 1);
        }

        // ── Public API ───────────────────────────────────────────────────────────

        public void Initialize(Transform parent)
        {
            if (_initialized) return;

            int layer = ResolvePreviewLayer();

            _stageRoot = new GameObject("SpellPreviewStage_Internal");
            _stageRoot.transform.SetParent(parent, false);
            _stageRoot.transform.position = new Vector3(0f, OFFSCREEN_Y, 0f);
            SetLayerRecursive(_stageRoot, layer);

            _casterGo = new GameObject("SpellPreviewCaster");
            _casterGo.transform.SetParent(_stageRoot.transform, false);
            _casterGo.layer = layer;
            _casterTransform = _casterGo.transform;

            var camGo = new GameObject("SpellPreviewCamera");
            camGo.transform.SetParent(parent, false);
            camGo.transform.position = new Vector3(0f, OFFSCREEN_Y, CAMERA_Z);
            _camera = camGo.AddComponent<Camera>();
            _camera.orthographic     = true;
            _camera.orthographicSize = ORTHO_SIZE_DEFAULT;
            _camera.cullingMask      = 1 << layer;
            _camera.clearFlags       = CameraClearFlags.SolidColor;
            _camera.backgroundColor  = new Color(0.06f, 0.06f, 0.08f, 1f);
            _camera.nearClipPlane    = 0.1f;
            _camera.farClipPlane     = 200f;
            _camera.enabled          = false;

            var urpData = camGo.GetComponent<UniversalAdditionalCameraData>()
                       ?? camGo.AddComponent<UniversalAdditionalCameraData>();
            urpData.renderType    = CameraRenderType.Base;
            urpData.renderShadows = false;
            urpData.antialiasing  = AntialiasingMode.None;

            _rt = new RenderTexture(RT_SIZE, RT_SIZE, 16, RenderTextureFormat.ARGB32);
            _rt.name = "SpellPreview_RT";
            _rt.Create();
            _camera.targetTexture = _rt;

            BuildRangeRuler(layer);

            _initialized = true;
        }

        public RenderTexture GetPreviewTexture() => _initialized ? _rt : null;

        /// <summary>
        /// Tear the stage down and fire the current spell again from the top, without
        /// waiting out the cycle and the idle gap.
        ///
        /// <para>Exists for the Gather tab: a flourish is half a second long inside a cycle
        /// that is at least a second and a bit, so tuning a knob and waiting for the loop to
        /// come round means watching the change land two beats after the edit. Same teardown
        /// as <see cref="SetSelectedSpell"/> minus the caster rebuild, because the spell has
        /// not changed and its executor's components are still the right ones.</para>
        /// </summary>
        public void Restart()
        {
            if (!_initialized || _spell == null) return;
            ClearStage();
            _loopState = LoopState.Idle;
            _loopTimer = 0f;
            DisposeAllFrames();
            // Any transport but Live parks on a captured frame, so a restart nobody can see
            // reads as the button doing nothing.
            _transport         = TransportMode.Live;
            _slowPlaybackAccum = 0f;
        }

        public void SetSelectedSpell(SpellDefinition spell)
        {
            if (!_initialized) return;
            if (_spell == spell) return;
            _spell = spell;
            // Rebuild the caster GO so any components the previous executor attached
            // (e.g. LaserBeamController, AuraController) are fully stripped before the
            // next executor runs.
            RebuildCasterGo();
            ClearStage();
            _loopState = LoopState.Idle;
            _loopTimer = 0f;
            DisposeAllFrames();
            _transport         = TransportMode.Live;
            _slowPlaybackAccum = 0f;
            SeedLockedBoundsForSpell(spell);
        }

        public void SetDirection(Vector2 dir)
        {
            if (dir.sqrMagnitude < 0.0001f) return;
            _direction = dir.normalized;
            ApplyCharacterDirection();
        }

        /// <summary>Multiply the current user zoom (clamped). 1.0 = auto-fit baseline.</summary>
        public void ZoomIn()  => SetZoom(_userZoom * USER_ZOOM_STEP);
        public void ZoomOut() => SetZoom(_userZoom / USER_ZOOM_STEP);

        /// <summary>
        /// Apply a continuous zoom delta (e.g. mouse-wheel ticks). Each unit of
        /// delta applies one full step; fractional deltas are applied via Pow.
        /// </summary>
        public void ZoomBy(float delta)
        {
            if (Mathf.Abs(delta) < 0.0001f) return;
            SetZoom(_userZoom * Mathf.Pow(USER_ZOOM_STEP, delta));
        }

        public void SetZoom(float zoom)
        {
            _userZoom = Mathf.Clamp(zoom, USER_ZOOM_MIN, USER_ZOOM_MAX);
        }

        public float CurrentZoom => _userZoom;

        public bool HasProjectilePrefab => ResolveProjectilePrefab() != null;

        public void Open()
        {
            if (!_initialized) return;
            _open = true;
            if (_camera != null) _camera.enabled = true;
            CaptureWorldVfxBaseline();
            _loopState = LoopState.Idle;
            _loopTimer = 0f;
            DisposeAllFrames();
            _transport         = TransportMode.Live;
            _slowPlaybackAccum = 0f;
            // Always start with character overlay OFF so the panel is in a clean default state.
            _showCharacter = false;
            DestroyCharacterGo();
        }

        public void Close()
        {
            if (!_initialized) return;
            _open = false;
            if (_camera != null) _camera.enabled = false;
            ClearStage();
            _baselineWorldVfx.Clear();
            _loopState = LoopState.Idle;
            _loopTimer = 0f;
            DisposeAllFrames();
        }

        /// <summary>Drive the preview loop. Call from MonoBehaviour.Update while the panel is open.</summary>
        public void Tick()
        {
            if (!_initialized || !_open) return;

            float dt = Time.deltaTime;

            if (_transport == TransportMode.Paused)
            {
                UpdateCameraFraming();
                UpdateRangeRuler();
                return;
            }

            if (_transport == TransportMode.Slow)
            {
                if (_frames.Count > 1)
                {
                    float framesPerSecond = _playbackSpeed / NOMINAL_FRAME_DURATION;
                    _slowPlaybackAccum += framesPerSecond * dt;
                    int steps = Mathf.FloorToInt(_slowPlaybackAccum);
                    if (steps > 0)
                    {
                        _slowPlaybackAccum -= steps;
                        _displayedFrame = (_displayedFrame + steps) % _frames.Count;
                    }
                }
                UpdateCameraFraming();
                UpdateRangeRuler();
                return;
            }

            // Live transport — run the spell-fire loop.
            switch (_loopState)
            {
                case LoopState.Idle:
                    if (_spell != null)
                    {
                        DisposeAllFrames();
                        FireOnce();
                        _loopTimer = ComputeCycleTime(_spell);
                        _loopState = LoopState.Active;
                    }
                    break;

                case LoopState.Active:
                    AbsorbNewWorldVfx();
                    AbsorbNewSceneRoots();
                    CaptureCurrentFrame();
                    _loopTimer -= dt;
                    if (_loopTimer <= 0f)
                    {
                        ClearStage();
                        _loopTimer = IDLE_GAP_SECONDS;
                        _loopState = LoopState.Cooldown;
                    }
                    break;

                case LoopState.Cooldown:
                    _loopTimer -= dt;
                    if (_loopTimer <= 0f)
                        _loopState = LoopState.Idle;
                    break;
            }

            UpdateCameraFraming();
            UpdateRangeRuler();
        }

        public void Shutdown()
        {
            if (!_initialized) return;
            ClearStage();
            DisposeAllFrames();
            if (_rangeRulerMaterial != null) { SafeDestroy.Of(_rangeRulerMaterial); _rangeRulerMaterial = null; }
            _rangeRulerGo    = null;
            _rangeRulerLine  = null;
            _rangeRulerLabel = null;
            if (_rt != null) { _rt.Release(); SafeDestroy.Of(_rt); _rt = null; }
            if (_camera != null) { SafeDestroy.Of(_camera.gameObject); _camera = null; }
            if (_stageRoot != null) { SafeDestroy.Of(_stageRoot); _stageRoot = null; }
            _casterGo        = null;
            _casterTransform = null;
            _spell           = null;
            _open            = false;
            _initialized     = false;
        }

        // ── Internal — utils ─────────────────────────────────────────────────────

        internal static int ResolvePreviewLayer()
        {
            int idx = LayerMask.NameToLayer("SpellPreview");
            if (idx < 0)
            {
                Debug.LogWarning("[SpellPreviewService] Layer 'SpellPreview' not found in TagManager. " +
                                 "Falling back to Default (layer 0). Spell visuals may appear in the game world.");
                return 0;
            }
            return idx;
        }

        internal static void SetLayerRecursive(GameObject root, int layer)
        {
            if (root == null) return;
            root.layer = layer;
            for (int i = 0; i < root.transform.childCount; i++)
                SetLayerRecursive(root.transform.GetChild(i).gameObject, layer);
        }
    }
}
