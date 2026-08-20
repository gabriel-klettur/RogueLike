using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Valkur.Data;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Manages off-screen RenderTexture previews for particle presets in the Particles Editor (F1).
    ///
    /// Architecture:
    ///   • One emitter + one 96×96 RenderTexture per visible preset. The slot pool grows to
    ///     match the filtered preset list — it is never reused across presets, because a
    ///     shared slot means a thumbnail showing somebody else's effect.
    ///   • A small pool of orthographic thumb cameras (each culling only "ParticlePreview",
    ///     layer 16) walks the slots round-robin, one slot per camera per frame. More
    ///     presets ⇒ more cameras, so a full refresh cycle stays around
    ///     <see cref="TARGET_CYCLE_FRAMES"/> frames instead of growing with the catalog.
    ///   • A dedicated large camera renders the selected preset into a 256×256 RT every
    ///     frame, so the View panel animates smoothly regardless of the thumb workload.
    ///   • Emitter GameObjects sit at y = OFFSCREEN_Y + i*SLOT_SPACING, never visible in
    ///     gameplay. On Shutdown(): all RTs released, all GameObjects destroyed.
    ///
    /// Fidelity guarantee:
    ///   Preview calls the exact same ApplyPreset() path as SpawnEmitterAt() on the map — no
    ///   parameters are altered. Camera ortho-size auto-fits to the particle bounds every frame
    ///   so wide effects (water_flow, portal) are never cropped. Burst effects are looped:
    ///   when IsAlive() returns false the emitter is replayed so the thumbnail stays animated.
    ///
    /// URP note: cameras are always enabled with a targetTexture assigned so URP renders them
    /// normally in its pipeline (Camera.Render() is not supported under an SRP, which is why
    /// several cameras — not several manual renders — are what buys parallel slot updates).
    /// Particle materials use Particles/Unlit or Sprites/Default — no Light2D required.
    /// </summary>
    public sealed class ParticlePreviewService
    {
        // ── Constants ────────────────────────────────────────────────────────────

        private const int   THUMB_SIZE        = 96;
        private const int   LARGE_SIZE        = 256;
        private const float OFFSCREEN_Y       = -10000f;
        private const float SLOT_SPACING      = 12f;    // world-units between emitter slots
        private const float CAMERA_Z          = -50f;

        /// <summary>
        /// Hard ceiling on live preview slots. Each slot costs one RenderTexture and one
        /// simulating ParticleSystem, so an unbounded catalog must not translate into an
        /// unbounded scene. Presets past this point get no thumbnail (and say so in the log)
        /// rather than silently borrowing another preset's picture.
        /// </summary>
        private const int   MAX_POOL_SIZE     = 128;

        /// <summary>Upper bound on simultaneously rendering thumb cameras.</summary>
        private const int   MAX_THUMB_CAMERAS = 6;

        /// <summary>Frames a full round-robin over every slot should take, budget permitting.</summary>
        private const int   TARGET_CYCLE_FRAMES = 20;

        // Default ortho sizes used as a minimum — auto-fit may grow them.
        private const float ORTHO_SIZE_THUMB_MIN  = 1.5f;
        private const float ORTHO_SIZE_LARGE_MIN  = 2.25f;
        // Maximum ortho to cap very large effects in thumbnail view.
        private const float ORTHO_SIZE_THUMB_MAX  = 4f;
        private const float ORTHO_SIZE_LARGE_MAX  = 8f;
        // Padding factor around the computed bounds.
        private const float BOUNDS_PADDING    = 0.25f;

        /// <summary>
        /// Backdrop the preview cameras clear to.
        ///
        /// It used to be near-black (0.08). That flatters additive presets — explosions and
        /// auras add onto darkness and glow — while making every ALPHA preset invisible,
        /// because alpha composites toward the backdrop instead of adding to it. Water,
        /// smoke and foliage are alpha by design (they are mass, not light), so the whole
        /// decoration half of the catalog rendered as dark specks.
        ///
        /// A mid-dark neutral shows both: alpha effects now have something to sit against,
        /// and additive ones still read because they add on top. Kept slightly blue-grey so
        /// it does not tint warm effects.
        /// </summary>
        private static readonly Color PREVIEW_BACKDROP = new Color(0.24f, 0.25f, 0.28f, 1f);

        // ── State ────────────────────────────────────────────────────────────────

        private Transform        _parent;
        private int              _layer;
        private Camera           _largeCamera;
        private RenderTexture    _largeRT;
        private GameObject       _largeEmitterGo;
        private ParticleEmitter  _largeEmitter;
        private ParticlePresetDefinition _largePresetDef;
        private ParticleSystemRenderer[] _largeRenderers = System.Array.Empty<ParticleSystemRenderer>();
        private string           _selectedPresetId;
        private int              _cursor;

        private readonly List<Camera>    _thumbCameras = new List<Camera>();
        private readonly List<ThumbSlot> _pool         = new List<ThumbSlot>();

        /// <summary>Mapping from preset id → pool slot index. One slot per preset, never shared.</summary>
        private readonly Dictionary<string, int> _presetToSlot = new Dictionary<string, int>();

        private bool  _initialized;
        private float _speedAccumulator; // fractional frame accumulator for sub-1x speed

        /// <summary>Number of slots currently backing a visible preset.</summary>
        private int ActiveSlotCount => _presetToSlot.Count;

        // ── Playback controls ────────────────────────────────────────────────────

        /// <summary>
        /// When true the round-robin is frozen. The cameras still run so the last
        /// rendered frame is visible in the RT.
        /// NOTE: Only the preview round-robin is paused — map emitters are unaffected.
        /// </summary>
        public bool IsPaused { get; private set; }

        /// <summary>
        /// Multiplier applied to the preview round-robin each Tick().
        /// 0.25, 0.5, or 1.0 are the exposed values.
        /// </summary>
        public float SpeedMultiplier { get; private set; } = 1f;

        /// <summary>Pause the preview round-robin.</summary>
        public void Pause()  { IsPaused = true;  }
        /// <summary>Resume the preview round-robin.</summary>
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

        /// <summary>
        /// When true the View stops auto-fitting and shows the effect at the exact scale the
        /// game camera does: one world unit spans as many View pixels as it spans on screen.
        ///
        /// Auto-fit is the single biggest fidelity lie the preview tells — it inflates a
        /// two-pixel ember to fill the panel and shrinks a four-unit fountain to fit it, so
        /// no two presets are ever shown at comparable sizes, let alone the game's.
        /// Zoom still applies on top: 1x IS the game, 2x is a magnifier.
        /// </summary>
        public bool GameScaleMode { get; private set; }

        public void SetGameScaleMode(bool on) { GameScaleMode = on; }

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
            public ParticlePresetDefinition Def;

            /// <summary>
            /// Cached at ApplyPreset time. Both are re-read on every re-apply because
            /// ParticleEmitter rebuilds its child ParticleSystem from scratch. Null for
            /// the "lightning" kind, which draws with a LineRenderer instead.
            /// </summary>
            public ParticleSystem  Ps;
            public ParticleSystemRenderer[] Renderers = System.Array.Empty<ParticleSystemRenderer>();
        }

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Allocate the large preview camera and RT. Thumb slots and thumb cameras are
        /// allocated on demand by <see cref="SetVisiblePresets"/>.
        /// Call once on editor Activate().
        /// </summary>
        public void Initialize(Transform parent)
        {
            if (_initialized) return;

            _parent = parent;
            _layer  = ResolvePreviewLayer();

            // ── Large RT + emitter + dedicated camera ────────────────────────────
            _largeRT = new RenderTexture(LARGE_SIZE, LARGE_SIZE, 16, RenderTextureFormat.ARGB32);
            _largeRT.name = "PPrev_Large";
            _largeRT.Create();

            var largeGo = new GameObject("PPrev_Emitter_Large");
            largeGo.transform.SetParent(parent, false);
            largeGo.transform.position = SlotPosition(MAX_POOL_SIZE); // beyond every thumb slot
            largeGo.layer = _layer;

            _largeEmitter   = largeGo.AddComponent<ParticleEmitter>();
            _largeEmitterGo = largeGo;

            _largeCamera = CreateCamera("ParticlePreviewCamera_Large");
            _largeCamera.targetTexture = _largeRT;
            // Nothing selected yet — no reason to spend a render pass.
            _largeCamera.enabled = false;

            _initialized = true;
        }

        /// <summary>
        /// Update the visible preset list and assign one pool slot per preset.
        /// Call after filter/sort changes.
        /// </summary>
        public void SetVisiblePresets(IReadOnlyList<ParticlePresetDefinition> presets)
        {
            if (!_initialized) return;

            _presetToSlot.Clear();

            int wanted = 0;
            for (int i = 0; i < presets.Count; i++)
                if (presets[i] != null) wanted++;

            if (wanted > MAX_POOL_SIZE)
            {
                Debug.LogWarning(
                    $"[ParticlePreviewService] {wanted} presets visible but only {MAX_POOL_SIZE} preview " +
                    "slots exist. The remainder render no thumbnail — narrow the search filter to see them.");
                wanted = MAX_POOL_SIZE;
            }

            EnsureSlotCount(wanted);

            int slotIdx = 0;
            for (int i = 0; i < presets.Count && slotIdx < wanted; i++)
            {
                var def = presets[i];
                if (def == null) continue;

                string pid = def.id ?? "";
                _presetToSlot[pid] = slotIdx;

                var slot = _pool[slotIdx];
                if (slot.PresetId != pid)
                {
                    slot.PresetId = pid;
                    slot.Def      = def;
                    ApplyToSlot(slot, def);
                }
                slotIdx++;
            }

            // Park every slot the current filter does not use so a stale effect is not
            // left simulating off-screen.
            for (int i = slotIdx; i < _pool.Count; i++)
            {
                var slot = _pool[i];
                if (slot.PresetId == null) continue;
                slot.PresetId = null;
                slot.Def      = null;
                if (slot.Ps != null) slot.Ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            EnsureThumbCameras(DesiredThumbCameraCount());
            _cursor = 0;
        }

        /// <summary>Returns the 96×96 thumbnail RT for a preset id, or null if it has no slot.</summary>
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
                SetLayerRecursive(_largeEmitterGo, _layer);
                _largeRenderers = _largeEmitterGo.GetComponentsInChildren<ParticleSystemRenderer>();
            }
            if (_largeCamera != null)
            {
                _largeCamera.enabled = def != null && !string.IsNullOrEmpty(presetId);
                // Frame it immediately. Tick() is what normally positions this camera, but
                // it runs after the render that follows this call, so enabling here and
                // waiting would render one frame from wherever the camera last was — a dark
                // rectangle on the first selection, and a visible flicker on every switch.
                // It also un-gates the initial framing from IsPaused, which otherwise left
                // a preset picked while paused permanently unrendered.
                if (_largeCamera.enabled) PointCameraAtLarge();
            }
        }

        /// <summary>
        /// Drive the preview cameras. Call from MonoBehaviour.Update().
        /// </summary>
        public void Tick()
        {
            if (!_initialized) return;

            // The large preview has its own camera and needs no round-robin; it only
            // needs its burst effect restarted and its framing refreshed.
            if (!IsPaused && _largeCamera != null && _largeCamera.enabled)
            {
                RestartIfDead(_largeEmitter, _largePresetDef);
                PointCameraAtLarge();
            }

            int active = ActiveSlotCount;
            if (active == 0 || _thumbCameras.Count == 0) return;

            if (IsPaused) return;

            // SpeedMultiplier: at <1x we skip frames proportionally.
            // At 0.5x we only advance every 2nd tick, etc.
            _speedAccumulator += SpeedMultiplier;
            if (_speedAccumulator < 1f) return;  // not enough time accumulated
            _speedAccumulator -= 1f;

            // Keep every burst thumbnail alive, not just the ones about to be rendered:
            // a slot whose turn is 20 frames away would otherwise show a dead effect.
            for (int i = 0; i < active; i++)
            {
                var slot = _pool[i];
                if (slot.PresetId != null) RestartIfDead(slot);
            }

            // Each camera takes the next slot in the ring, so N cameras advance the
            // cycle N times faster.
            for (int c = 0; c < _thumbCameras.Count; c++)
            {
                _cursor = (_cursor + 1) % active;
                PointCameraAtThumb(_thumbCameras[c], _pool[_cursor]);
            }
        }

        /// <summary>
        /// Release all resources. Safe to call multiple times.
        /// </summary>
        public void Shutdown()
        {
            if (!_initialized) return;

            for (int i = 0; i < _pool.Count; i++)
            {
                var s = _pool[i];
                if (s == null) continue;
                DestroyObject(s.EmitterGo);
                if (s.RT != null) { s.RT.Release(); DestroyObject(s.RT); }
            }
            _pool.Clear();

            for (int i = 0; i < _thumbCameras.Count; i++)
                if (_thumbCameras[i] != null) DestroyObject(_thumbCameras[i].gameObject);
            _thumbCameras.Clear();

            DestroyObject(_largeEmitterGo);
            if (_largeRT != null) { _largeRT.Release(); DestroyObject(_largeRT); }
            if (_largeCamera != null) DestroyObject(_largeCamera.gameObject);

            _presetToSlot.Clear();
            _largeRenderers   = System.Array.Empty<ParticleSystemRenderer>();
            _selectedPresetId = null;
            _largePresetDef   = null;
            _cursor           = 0;
            _speedAccumulator = 0f;
            IsPaused          = false;
            SpeedMultiplier   = 1f;
            LargeOrthoZoom    = 1f;
            _parent           = null;
            _initialized      = false;
        }

        /// <summary>
        /// Destroy that also works outside play mode. EditMode tests drive Initialize/Shutdown
        /// directly, and Object.Destroy is deferred-and-illegal there — it logs
        /// "Destroy may not be called from edit mode" and leaves the object alive, so the
        /// cameras and RenderTextures would survive into the next test.
        /// </summary>
        private static void DestroyObject(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else                       Object.DestroyImmediate(obj);
        }

        // ── Private: allocation ──────────────────────────────────────────────────

        /// <summary>Grow the slot pool to at least <paramref name="count"/> slots. Never shrinks.</summary>
        private void EnsureSlotCount(int count)
        {
            while (_pool.Count < count)
            {
                int i = _pool.Count;

                var rt = new RenderTexture(THUMB_SIZE, THUMB_SIZE, 16, RenderTextureFormat.ARGB32);
                rt.name = $"PPrev_Thumb_{i}";
                rt.Create();

                var go = new GameObject($"PPrev_Emitter_{i}");
                go.transform.SetParent(_parent, false);
                go.transform.position = SlotPosition(i);
                go.layer = _layer;

                _pool.Add(new ThumbSlot
                {
                    RT        = rt,
                    EmitterGo = go,
                    Emitter   = go.AddComponent<ParticleEmitter>(),
                    PresetId  = null,
                });
            }
        }

        /// <summary>
        /// How many thumb cameras it takes to walk every active slot in roughly
        /// <see cref="TARGET_CYCLE_FRAMES"/> frames, capped so a huge catalog cannot
        /// flood the frame with render passes.
        /// </summary>
        private int DesiredThumbCameraCount()
        {
            int active = ActiveSlotCount;
            if (active <= 0) return 0;
            int needed = Mathf.CeilToInt(active / (float)TARGET_CYCLE_FRAMES);
            return Mathf.Clamp(needed, 1, MAX_THUMB_CAMERAS);
        }

        /// <summary>Grow the thumb camera pool to <paramref name="count"/>. Never shrinks.</summary>
        private void EnsureThumbCameras(int count)
        {
            while (_thumbCameras.Count < count)
                _thumbCameras.Add(CreateCamera($"ParticlePreviewCamera_{_thumbCameras.Count}"));
        }

        private Camera CreateCamera(string name)
        {
            var camGo = new GameObject(name);
            camGo.transform.SetParent(_parent, false);

            var cam = camGo.AddComponent<Camera>();
            cam.orthographic     = true;
            cam.orthographicSize = ORTHO_SIZE_THUMB_MIN;
            cam.cullingMask      = 1 << _layer;
            cam.clearFlags       = CameraClearFlags.SolidColor;
            cam.backgroundColor  = PREVIEW_BACKDROP;
            cam.nearClipPlane    = 0.1f;
            cam.farClipPlane     = 200f;
            // Born disabled: an enabled camera with no targetTexture renders straight
            // to the screen, which would flash the off-screen preview layer over the
            // game view. Callers enable it in the same breath as assigning its RT.
            cam.enabled          = false;

            // Configure URP camera data: standalone Base camera, no shadows, no AA.
            var urpData = camGo.GetComponent<UniversalAdditionalCameraData>()
                       ?? camGo.AddComponent<UniversalAdditionalCameraData>();
            urpData.renderType    = CameraRenderType.Base;
            urpData.renderShadows = false;
            urpData.antialiasing  = AntialiasingMode.None;

            return cam;
        }

        // ── Private: per-slot work ───────────────────────────────────────────────

        private void ApplyToSlot(ThumbSlot slot, ParticlePresetDefinition def)
        {
            SafeApplyPreset(slot.Emitter, def);
            SetLayerRecursive(slot.EmitterGo, _layer);
            // ParticleEmitter rebuilds its child ParticleSystem on every apply, so the
            // cached references have to be refreshed here rather than once at creation.
            // includeInactive: a finished burst deactivates the child via stopAction, and a
            // lookup that skipped inactive objects would lose the handle we need to revive it.
            slot.Ps        = slot.EmitterGo.GetComponentInChildren<ParticleSystem>(true);
            slot.Renderers = slot.EmitterGo.GetComponentsInChildren<ParticleSystemRenderer>();
        }

        /// <summary>
        /// If a non-looping (burst) emitter has finished playing, restart it so
        /// thumbnails remain animated. Identical to how a looping emitter behaves.
        /// </summary>
        private void RestartIfDead(ThumbSlot slot)
        {
            if (slot?.Ps == null || slot.Def == null) return;
            // IsAlive() returns false when the system finished and no particles remain.
            if (!slot.Ps.main.loop && !slot.Ps.IsAlive(true))
                ApplyToSlot(slot, slot.Def); // re-applies, which calls ps.Play()
        }

        private static void RestartIfDead(ParticleEmitter emitter, ParticlePresetDefinition def)
        {
            if (emitter == null || def == null) return;
            var ps = emitter.GetComponentInChildren<ParticleSystem>(true);
            if (ps == null) return;
            if (!ps.main.loop && !ps.IsAlive(true))
                SafeApplyPreset(emitter, def);
        }

        /// <summary>
        /// Compute the orthographic half-size needed to frame all active particles
        /// in this slot, accounting for the RT aspect ratio.
        /// Falls back to the provided minimum if bounds are degenerate.
        /// </summary>
        private static float ComputeOrthoSize(GameObject emitterGo, ParticleSystemRenderer[] renderers,
                                              float minSize, float maxSize)
        {
            if (emitterGo == null || renderers == null || renderers.Length == 0) return minSize;

            // Accumulate the world-space bounds of every ParticleSystemRenderer child.
            bool hasBounds = false;
            var combined = new Bounds(emitterGo.transform.position, Vector3.zero);

            for (int i = 0; i < renderers.Length; i++)
            {
                var psr = renderers[i];
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

        private static void PointCameraAtThumb(Camera cam, ThumbSlot slot)
        {
            if (cam == null || slot == null || slot.RT == null || slot.EmitterGo == null) return;

            Vector3 ep = slot.EmitterGo.transform.position;
            cam.transform.position = new Vector3(ep.x, ep.y, CAMERA_Z);
            cam.orthographicSize   = ComputeOrthoSize(slot.EmitterGo, slot.Renderers,
                                                      ORTHO_SIZE_THUMB_MIN, ORTHO_SIZE_THUMB_MAX);
            cam.targetTexture      = slot.RT;
            cam.enabled            = true;
        }

        private void PointCameraAtLarge()
        {
            if (_largeEmitterGo == null || _largeRT == null || _largeCamera == null) return;

            Vector3 ep = _largeEmitterGo.transform.position;
            _largeCamera.transform.position = new Vector3(ep.x, ep.y, CAMERA_Z);
            // Apply user zoom: LargeOrthoZoom > 1 → smaller ortho → zoomed in.
            float baseOrtho = GameScaleMode
                ? GameEquivalentOrtho()
                : ComputeOrthoSize(_largeEmitterGo, _largeRenderers,
                                   ORTHO_SIZE_LARGE_MIN, ORTHO_SIZE_LARGE_MAX);
            _largeCamera.orthographicSize = baseOrtho / Mathf.Max(LargeOrthoZoom, 0.0001f);
            _largeCamera.targetTexture    = _largeRT;
        }

        /// <summary>
        /// The ortho at which this RT's pixels-per-world-unit equals the live game camera's
        /// screen pixels-per-world-unit. The game shows 2*ortho world units across
        /// Screen.height pixels; matching that density on a LARGE_SIZE-pixel RT means
        /// ortho_rt = ortho_game * LARGE_SIZE / Screen.height. Resolved every frame so the
        /// View tracks window resizes and any game-camera ortho change.
        /// </summary>
        private static float GameEquivalentOrtho()
        {
            var gameCam = Camera.main;
            float gameOrtho = (gameCam != null && gameCam.orthographic)
                ? gameCam.orthographicSize
                : 5f;                              // the project's authored default
            float screenH = Mathf.Max(1, Screen.height);
            return Mathf.Max(0.05f, gameOrtho * LARGE_SIZE / screenH);
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
