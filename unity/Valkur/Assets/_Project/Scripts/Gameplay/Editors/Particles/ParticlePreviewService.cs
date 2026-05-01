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
    /// URP note: the camera is always enabled with a targetTexture assigned so URP renders it
    /// normally in its pipeline. We just relocate the camera each frame.
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
        private const float ORTHO_SIZE_THUMB  = 1.5f;
        private const float ORTHO_SIZE_LARGE  = 2.25f;
        private const int   LARGE_REFRESH_FRAMES = 3;  // render large preview every N thumb frames

        // ── State ────────────────────────────────────────────────────────────────

        private Camera           _camera;
        private RenderTexture    _largeRT;
        private GameObject       _largeEmitterGo;
        private ParticleEmitter  _largeEmitter;
        private string           _selectedPresetId;
        private int              _thumbFrameCounter;
        private int              _largeFrameCounter;
        private int              _activeSlotCount;

        // Pool of thumb slots.
        private readonly ThumbSlot[] _pool = new ThumbSlot[POOL_SIZE];

        // Mapping from preset id → pool slot index.
        private readonly Dictionary<string, int> _presetToSlot = new Dictionary<string, int>();

        // Visible presets list.
        private readonly List<ParticlePresetDefinition> _visible = new List<ParticlePresetDefinition>();

        private bool _initialized;

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
            _camera.orthographicSize = ORTHO_SIZE_THUMB;
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
                    slot.PresetId = pid;
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

            _largeFrameCounter++;
            bool renderLarge = _largeFrameCounter >= LARGE_REFRESH_FRAMES
                            && !string.IsNullOrEmpty(_selectedPresetId);

            if (renderLarge)
            {
                _largeFrameCounter = 0;
                PointCameraAtLarge();
            }
            else
            {
                // Advance to next thumb slot in round-robin.
                _thumbFrameCounter = (_thumbFrameCounter + 1) % _activeSlotCount;
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
            }

            if (_largeEmitterGo != null) Object.Destroy(_largeEmitterGo);
            if (_largeRT        != null) { _largeRT.Release(); Object.Destroy(_largeRT); }
            if (_camera         != null) Object.Destroy(_camera.gameObject);

            _presetToSlot.Clear();
            _visible.Clear();
            _selectedPresetId = null;
            _activeSlotCount  = 0;
            _initialized      = false;
        }

        // ── Private ──────────────────────────────────────────────────────────────

        private void PointCameraAtThumb(int slotIndex)
        {
            var slot = _pool[slotIndex];
            if (slot == null || slot.RT == null) return;

            Vector3 ep = slot.EmitterGo.transform.position;
            _camera.transform.position  = new Vector3(ep.x, ep.y, CAMERA_Z);
            _camera.orthographicSize    = ORTHO_SIZE_THUMB;
            _camera.targetTexture       = slot.RT;
        }

        private void PointCameraAtLarge()
        {
            if (_largeEmitterGo == null || _largeRT == null) return;

            Vector3 ep = _largeEmitterGo.transform.position;
            _camera.transform.position = new Vector3(ep.x, ep.y, CAMERA_Z);
            _camera.orthographicSize   = ORTHO_SIZE_LARGE;
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
