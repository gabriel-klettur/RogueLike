using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Valkur.Data;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Manages off-screen RenderTexture previews for particle presets in the Particles Editor (F1).
    ///
    /// Architecture (1 shared camera, round-robin per frame):
    ///   • One orthographic Camera (culls only "ParticlePreview" layer 16) renders continuously.
    ///   • Each frame the camera moves to a different emitter slot and renders to that slot's RT.
    ///   • Pool of POOL_SIZE (24) thumb RT 96×96 slots.
    ///   • One dedicated large RT 256×256 for the selected-preset preview.
    ///   • The large emitter has its own camera slot that renders every LARGE_REFRESH_FRAMES frames.
    ///   • ParticleEmitter GameObjects at y=OFFSCREEN_Y + i*SLOT_SPACING, never visible in gameplay.
    ///   • On Shutdown(): all RTs released, all GameObjects destroyed.
    ///
    /// Fidelity guarantee:
    ///   Preview calls the exact same ApplyPreset() path as SpawnEmitterAt() on the map — no
    ///   parameters are altered. Camera ortho-size auto-fits to the particle bounds every frame
    ///   so wide effects (water_flow, portal) are never cropped. Burst effects are looped:
    ///   when IsAlive() returns false the emitter is replayed so the thumbnail stays animated.
    ///
    /// URP note: the camera is always enabled with a targetTexture assigned so URP renders it
    /// normally in its pipeline. Particle materials use Particles/Unlit or Sprites/Default —
    /// no Light2D required.
    /// </summary>
    public sealed class ParticlePreviewService
    {
        // ── Constants ────────────────────────────────────────────────────────────

        private const int   THUMB_SIZE        = 96;
        private const int   LARGE_SIZE        = 256;
        private const int   POOL_SIZE         = 24;
        private const float OFFSCREEN_Y       = -10000f;
        private const float SLOT_SPACING      = 12f;    // world-units between emitter slots
        private const float CAMERA_Z          = -50f;

        // Default ortho sizes used as a minimum — auto-fit may grow them.
        private const float ORTHO_SIZE_THUMB_MIN  = 1.5f;
        private const float ORTHO_SIZE_LARGE_MIN  = 2.25f;
        // Maximum ortho to cap very large effects in thumbnail view.
        private const float ORTHO_SIZE_THUMB_MAX  = 4f;
        private const float ORTHO_SIZE_LARGE_MAX  = 8f;
        // Padding factor around the computed bounds.
        private const float BOUNDS_PADDING    = 0.25f;

        private const int   LARGE_REFRESH_FRAMES = 3;  // render large preview every N thumb frames

        // ── State ────────────────────────────────────────────────────────────────

        private Camera           _camera;
        private RenderTexture    _largeRT;
        private GameObject       _largeEmitterGo;
        private ParticleEmitter  _largeEmitter;
        private ParticlePresetDefinition _largePresetDef;
        private string           _selectedPresetId;
        private int              _thumbFrameCounter;
        private int              _largeFrameCounter;
        private int              _activeSlotCount;

        // Pool of thumb slots.
        private readonly ThumbSlot[] _pool = new ThumbSlot[POOL_SIZE];

        // Mapping from preset id → pool slot index.
        private readonly Dictionary<string, int> _presetToSlot = new Dictionary<string, int>();

        // Visible presets list (parallel to pool slots).
        private readonly List<ParticlePresetDefinition> _visible = new List<ParticlePresetDefinition>();

        // Per-slot definition cache so we can restart burst emitters.
        private readonly ParticlePresetDefinition[] _slotDef = new ParticlePresetDefinition[POOL_SIZE];

        private bool  _initialized;
        private float _speedAccumulator; // fractional frame accumulator for sub-1x speed

        // ── Playback controls ────────────────────────────────────────────────────

        /// <summary>
        /// When true the per-emitter simulation is frozen. The camera still runs
        /// so the last rendered frame is visible in the RT.
        /// NOTE: Only the preview simulation is paused — map emitters are unaffected.
        /// </summary>
        public bool IsPaused { get; private set; }

        /// <summary>
        /// Multiplier applied to the preview simulation delta time each Tick().
        /// 0.25, 0.5, or 1.0 are the exposed values.
        /// </summary>
        public float SpeedMultiplier { get; private set; } = 1f;

        /// <summary>Pause the preview simulation.</summary>
        public void Pause()  { IsPaused = true;  }
        /// <summary>Resume the preview simulation.</summary>
        public void Resume() { IsPaused = false; }
        /// <summary>Toggle pause state; returns the new IsPaused value.</summary>
        public bool TogglePause() { IsPaused = !IsPaused; return IsPaused; }
        /// <summary>Set the simulation speed multiplier (0.25, 0.5, 1.0, …).</summary>
        public void SetSpeedMultiplier(float m) { SpeedMultiplier = Mathf.Max(0.01f, m); }

        // ── Zoom controls ─────────────────────────────────────────────────────────

        private const float ZOOM_MIN  = 0.25f;
        private const float ZOOM_MAX  = 4.0f;
        private const float ZOOM_STEP = 1.25f;

        /// <summary>
        /// User-controlled zoom for the large preview camera.
        /// 1.0 = auto-fit baseline; &gt;1 zooms in (smaller ortho); &lt;1 zooms out.
        /// Clamped to [0.25, 4.0].
        /// </summary>
        public float LargeOrthoZoom { get; private set; } = 1f;

        /// <summary>Zoom in by one step (multiply by 1.25, clamp).</summary>
        public void ZoomIn()  => SetZoom(LargeOrthoZoom * ZOOM_STEP);
        /// <summary>Zoom out by one step (multiply by 0.8, clamp).</summary>
        public void ZoomOut() => SetZoom(LargeOrthoZoom / ZOOM_STEP);
        /// <summary>Set an absolute zoom value (clamped to [0.25, 4.0]).</summary>
        public void SetZoom(float zoom)  { LargeOrthoZoom = Mathf.Clamp(zoom, ZOOM_MIN, ZOOM_MAX); }
        /// <summary>Reset zoom back to the auto-fit baseline (1.0).</summary>
        public void ResetZoom()          { LargeOrthoZoom = 1f; }

        // ── Inner type ───────────────────────────────────────────────────────────

        private sealed class ThumbSlot
        {
            public RenderTexture   RT;
            public GameObject      EmitterGo;
            public ParticleEmitter Emitter;
            public string          PresetId;
        }

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Allocate camera, pool and large RT. Call once on editor Activate().
        /// </summary>
        public void Initialize(Transform parent)
        {
            if (_initialized) return;

            int layer = ResolvePreviewLayer();

            // ── Camera ──────────────────────────────────────────────────────────
            var camGo = new GameObject("ParticlePreviewCamera");
            camGo.transform.SetParent(parent, false);
            _camera = camGo.AddComponent<Camera>();
            _camera.orthographic     = true;
            _camera.orthographicSize = ORTHO_SIZE_THUMB_MIN;
            _camera.cullingMask      = 1 << layer;
            _camera.clearFlags       = CameraClearFlags.SolidColor;
            _camera.backgroundColor  = new Color(0.08f, 0.08f, 0.10f, 1f);
            _camera.nearClipPlane    = 0.1f;
            _camera.farClipPlane     = 200f;
            // Keep camera enabled so URP picks it up in its render loop.
            _camera.enabled          = true;

            // Configure URP camera data: standalone Base camera, no shadows, no AA.
            var urpData = camGo.GetComponent<UniversalAdditionalCameraData>()
                       ?? camGo.AddComponent<UniversalAdditionalCameraData>();
            urpData.renderType    = CameraRenderType.Base;
            urpData.renderShadows = false;
            urpData.antialiasing  = AntialiasingMode.None;

            // ── Thumb pool ───────────────────────────────────────────────────────
            for (int i = 0; i < POOL_SIZE; i++)
            {
                var rt = new RenderTexture(THUMB_SIZE, THUMB_SIZE, 16, RenderTextureFormat.ARGB32);
                rt.name = $"PPrev_Thumb_{i}";
                rt.Create();

                var go = new GameObject($"PPrev_Emitter_{i}");
                go.transform.SetParent(parent, false);
                go.transform.position = SlotPosition(i);
                go.layer = layer;

                _pool[i] = new ThumbSlot
                {
                    RT        = rt,
                    EmitterGo = go,
                    Emitter   = go.AddComponent<ParticleEmitter>(),
                    PresetId  = null
                };
            }

            // ── Large RT + emitter ───────────────────────────────────────────────
            _largeRT = new RenderTexture(LARGE_SIZE, LARGE_SIZE, 16, RenderTextureFormat.ARGB32);
            _largeRT.name = "PPrev_Large";
            _largeRT.Create();

            var largeGo = new GameObject("PPrev_Emitter_Large");
            largeGo.transform.SetParent(parent, false);
            largeGo.transform.position = SlotPosition(POOL_SIZE); // slot beyond the thumb pool
            largeGo.layer = layer;

            _largeEmitter  = largeGo.AddComponent<ParticleEmitter>();
            _largeEmitterGo = largeGo;

            // Point camera at slot 0 initially.
            PointCameraAtThumb(0);

            _initialized = true;
        }

        /// <summary>
        /// Update the visible preset list and assign pool slots.
        /// Call after filter/sort changes.
        /// </summary>
        public void SetVisiblePresets(IReadOnlyList<ParticlePresetDefinition> presets)
        {
            if (!_initialized) return;

            _visible.Clear();
            _visible.AddRange(presets);
            _presetToSlot.Clear();

            int layer = ResolvePreviewLayer();
            _activeSlotCount = Mathf.Min(_visible.Count, POOL_SIZE);

            for (int i = 0; i < _visible.Count; i++)
            {
                int slotIdx = i % POOL_SIZE;
                var def     = _visible[i];
                if (def == null) continue;

                string pid = def.id ?? "";
                _presetToSlot[pid] = slotIdx;

                var slot = _pool[slotIdx];
                if (slot.PresetId != pid)
                {
                    slot.PresetId  = pid;
                    _slotDef[slotIdx] = def;
                    SafeApplyPreset(slot.Emitter, def);
                    SetLayerRecursive(slot.EmitterGo, layer);
                }
            }

            _thumbFrameCounter = 0;
        }

        /// <summary>Returns the 96×96 thumbnail RT for a preset id, or null if not in pool.</summary>
        public RenderTexture GetPreviewTexture(string presetId)
        {
            if (!_initialized || string.IsNullOrEmpty(presetId)) return null;
            return _presetToSlot.TryGetValue(presetId, out int idx) ? _pool[idx].RT : null;
        }

        /// <summary>Returns the 256×256 large preview RT (selected preset).</summary>
        public RenderTexture GetLargePreviewTexture() => _initialized ? _largeRT : null;

        /// <summary>
        /// Set the selected preset; the large preview will track this emitter.
        /// </summary>
        public void SetSelectedPreset(string presetId, ParticlePresetDefinition def)
        {
            if (!_initialized) return;
            _selectedPresetId = presetId;
            _largePresetDef   = def;
            if (def != null)
            {
                SafeApplyPreset(_largeEmitter, def);
                SetLayerRecursive(_largeEmitterGo, ResolvePreviewLayer());
            }
            _largeFrameCounter = 0; // trigger immediate large render on next Tick
        }

        /// <summary>
        /// Drive the preview camera round-robin. Call from MonoBehaviour.Update().
        /// </summary>
        public void Tick()
        {
            if (!_initialized || _activeSlotCount == 0) return;

            // When paused, still point the camera (so the last frame stays visible)
            // but skip the restart-if-dead simulation advancement.
            if (IsPaused)
            {
                PointCameraAtThumb(_thumbFrameCounter);
                return;
            }

            // SpeedMultiplier: at <1x we skip frames proportionally.
            // At 0.5x we only advance every 2nd tick, etc.
            // Simple frame-skip strategy: accumulate a counter and skip when below threshold.
            _speedAccumulator += SpeedMultiplier;
            if (_speedAccumulator < 1f) return;  // not enough time accumulated
            _speedAccumulator -= 1f;

            _largeFrameCounter++;
            bool renderLarge = _largeFrameCounter >= LARGE_REFRESH_FRAMES
                            && !string.IsNullOrEmpty(_selectedPresetId);

            if (renderLarge)
            {
                _largeFrameCounter = 0;
                RestartIfDead(_largeEmitter, _largePresetDef);
                PointCameraAtLarge();
            }
            else
            {
                // Advance to next thumb slot in round-robin.
                _thumbFrameCounter = (_thumbFrameCounter + 1) % _activeSlotCount;
                var slot = _pool[_thumbFrameCounter];
                if (slot != null)
                    RestartIfDead(slot.Emitter, _slotDef[_thumbFrameCounter]);
                PointCameraAtThumb(_thumbFrameCounter);
            }
        }

        /// <summary>
        /// Release all resources. Safe to call multiple times.
        /// </summary>
        public void Shutdown()
        {
            if (!_initialized) return;

            for (int i = 0; i < POOL_SIZE; i++)
            {
                var s = _pool[i];
                if (s == null) continue;
                if (s.EmitterGo != null) Object.Destroy(s.EmitterGo);
                if (s.RT        != null) { s.RT.Release(); Object.Destroy(s.RT); }
                _pool[i] = null;
                _slotDef[i] = null;
            }

            if (_largeEmitterGo != null) Object.Destroy(_largeEmitterGo);
            if (_largeRT        != null) { _largeRT.Release(); Object.Destroy(_largeRT); }
            if (_camera         != null) Object.Destroy(_camera.gameObject);

            _presetToSlot.Clear();
            _visible.Clear();
            _selectedPresetId = null;
            _largePresetDef   = null;
            _activeSlotCount  = 0;
            _speedAccumulator = 0f;
            IsPaused          = false;
            SpeedMultiplier   = 1f;
            LargeOrthoZoom    = 1f;
            _initialized      = false;
        }

        // ── Private ──────────────────────────────────────────────────────────────

        /// <summary>
        /// If a non-looping (burst) emitter has finished playing, restart it so
        /// thumbnails remain animated. Identical to how a looping emitter behaves.
        /// </summary>
        private static void RestartIfDead(ParticleEmitter emitter, ParticlePresetDefinition def)
        {
            if (emitter == null || def == null) return;
            var ps = emitter.GetComponentInChildren<ParticleSystem>();
            if (ps == null) return;
            // IsAlive() returns false when the system finished and no particles remain.
            if (!ps.main.loop && !ps.IsAlive(true))
                SafeApplyPreset(emitter, def); // re-applies which calls ps.Play()
        }

        /// <summary>
        /// Compute the orthographic half-size needed to frame all active particles
        /// in this slot, accounting for the RT aspect ratio.
        /// Falls back to the provided minimum if bounds are degenerate.
        /// </summary>
        private static float ComputeOrthoSize(GameObject emitterGo, float minSize, float maxSize)
        {
            if (emitterGo == null) return minSize;

            // Accumulate the world-space bounds of every ParticleSystemRenderer child.
            bool hasBounds = false;
            var combined = new Bounds(emitterGo.transform.position, Vector3.zero);

            foreach (var psr in emitterGo.GetComponentsInChildren<ParticleSystemRenderer>())
            {
                if (psr == null) continue;
                var b = psr.bounds;
                // Discard degenerate zero-size bounds (system not yet emitted anything)
                if (b.size.sqrMagnitude < 0.0001f) continue;
                if (!hasBounds) { combined = b; hasBounds = true; }
                else combined.Encapsulate(b);
            }

            if (!hasBounds) return minSize;

            // Half-extents in X and Y, padded.
            float halfX = combined.extents.x + BOUNDS_PADDING;
            float halfY = combined.extents.y + BOUNDS_PADDING;
            // Camera ortho size is half the vertical extent; scale X if wider than RT (square RT here).
            float needed = Mathf.Max(halfX, halfY);
            return Mathf.Clamp(needed, minSize, maxSize);
        }

        private void PointCameraAtThumb(int slotIndex)
        {
            var slot = _pool[slotIndex];
            if (slot == null || slot.RT == null || slot.EmitterGo == null) return;

            Vector3 ep = slot.EmitterGo.transform.position;
            _camera.transform.position  = new Vector3(ep.x, ep.y, CAMERA_Z);
            _camera.orthographicSize    = ComputeOrthoSize(slot.EmitterGo, ORTHO_SIZE_THUMB_MIN, ORTHO_SIZE_THUMB_MAX);
            _camera.targetTexture       = slot.RT;
        }

        private void PointCameraAtLarge()
        {
            if (_largeEmitterGo == null || _largeRT == null) return;

            Vector3 ep = _largeEmitterGo.transform.position;
            _camera.transform.position = new Vector3(ep.x, ep.y, CAMERA_Z);
            // Apply user zoom: LargeOrthoZoom > 1 → smaller ortho → zoomed in.
            float autoFit = ComputeOrthoSize(_largeEmitterGo, ORTHO_SIZE_LARGE_MIN, ORTHO_SIZE_LARGE_MAX);
            _camera.orthographicSize   = autoFit / Mathf.Max(LargeOrthoZoom, 0.0001f);
            _camera.targetTexture      = _largeRT;
        }

        private static Vector3 SlotPosition(int index)
            => new Vector3(0f, OFFSCREEN_Y + index * SLOT_SPACING, 0f);

        private static void SafeApplyPreset(ParticleEmitter emitter, ParticlePresetDefinition def)
        {
            if (emitter == null || def == null) return;
            try { emitter.ApplyPreset(def, 1f); }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ParticlePreviewService] ApplyPreset failed for '{def.id}': {ex.Message}");
            }
        }

        private static int ResolvePreviewLayer()
        {
            int idx = LayerMask.NameToLayer("ParticlePreview");
            if (idx < 0)
            {
                Debug.LogWarning("[ParticlePreviewService] Layer 'ParticlePreview' not found in TagManager. " +
                                 "Falling back to Default (layer 0). Particles may appear in the game world.");
                return 0;
            }
            return idx;
        }

        private static void SetLayerRecursive(GameObject root, int layer)
        {
            if (root == null) return;
            root.layer = layer;
            for (int i = 0; i < root.transform.childCount; i++)
                SetLayerRecursive(root.transform.GetChild(i).gameObject, layer);
        }
    }
}
