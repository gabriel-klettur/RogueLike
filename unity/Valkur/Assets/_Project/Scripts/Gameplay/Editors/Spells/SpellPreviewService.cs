using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Valkur.Core;
using Valkur.Data;

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
    ///     cooldown FSM, and SFX dispatch are all skipped. Audio mute on the caller
    ///     side handles any executor-internal SFX.
    ///   • A simple time-based loop fires one cast, waits a cycle, clears the stage,
    ///     waits a small idle gap, and repeats. The cycle length is derived from the
    ///     spell's own duration fields so persistent spells (Aura, Wall, Puddle, …)
    ///     get their full lifetime visible while one-shots (Projectile, Slash, …)
    ///     don't sit idle for long.
    ///
    /// All Unity-layer assignments use the user layer "SpellPreview" (separate from
    /// "ParticlePreview" so Spells and Particles previews never bleed into each
    /// other's cameras).
    /// </summary>
    public sealed class SpellPreviewService
    {
        // ── Constants ────────────────────────────────────────────────────────────

        private const int   RT_SIZE          = 384;
        private const float OFFSCREEN_Y      = -20000f;   // far from ParticlePreview (-10000)
        private const float CAMERA_Z         = -50f;
        private const float ORTHO_SIZE_MIN   = 2f;
        private const float ORTHO_SIZE_MAX   = 12f;
        private const float ORTHO_SIZE_DEFAULT = 6f;
        private const float BOUNDS_PADDING   = 0.5f;
        private const float IDLE_GAP_SECONDS = 0.25f;
        private const float MIN_CYCLE_SECONDS = 1.5f;
        private const float MIN_PERSISTENT_SECONDS = 2.0f;
        private const float CYCLE_TAIL_SECONDS = 0.2f;
        // User zoom: applied AFTER auto-fit so zooming doesn't fight bounds-tracking.
        // Values >1 zoom in (smaller ortho size), <1 zoom out.
        private const float USER_ZOOM_MIN  = 0.25f;
        private const float USER_ZOOM_MAX  = 6f;
        private const float USER_ZOOM_STEP = 1.2f;

        // ── State ────────────────────────────────────────────────────────────────

        private bool             _initialized;
        private bool             _open;
        private Camera           _camera;
        private RenderTexture    _rt;
        private GameObject       _stageRoot;
        private GameObject       _casterGo;
        private Transform        _casterTransform;
        private Vector2          _direction = Vector2.right;
        private SpellDefinition  _spell;
        private GameObject       _projectilePrefab;
        private bool             _projectilePrefabResolved;
        private float            _userZoom = 1f;

        // Loop timing.
        private enum LoopState { Idle, Active, Cooldown }
        private LoopState _loopState = LoopState.Idle;
        private float     _loopTimer;

        // Tracking world-rooted spawns (e.g. Projectile) so they get cleaned per cycle.
        private readonly List<GameObject> _trackedWorldSpawns = new List<GameObject>();

        public bool HasProjectilePrefab => ResolveProjectilePrefab() != null;

        // ── Public API ───────────────────────────────────────────────────────────

        public void Initialize(Transform parent)
        {
            if (_initialized) return;

            int layer = ResolvePreviewLayer();

            // Stage root + synthetic caster.
            _stageRoot = new GameObject("SpellPreviewStage_Internal");
            _stageRoot.transform.SetParent(parent, false);
            _stageRoot.transform.position = new Vector3(0f, OFFSCREEN_Y, 0f);
            SetLayerRecursive(_stageRoot, layer);

            _casterGo = new GameObject("SpellPreviewCaster");
            _casterGo.transform.SetParent(_stageRoot.transform, false);
            _casterGo.layer = layer;
            _casterTransform = _casterGo.transform;

            // Camera.
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
            _camera.enabled          = false; // enabled while panel is open

            var urpData = camGo.GetComponent<UniversalAdditionalCameraData>()
                       ?? camGo.AddComponent<UniversalAdditionalCameraData>();
            urpData.renderType    = CameraRenderType.Base;
            urpData.renderShadows = false;
            urpData.antialiasing  = AntialiasingMode.None;

            _rt = new RenderTexture(RT_SIZE, RT_SIZE, 16, RenderTextureFormat.ARGB32);
            _rt.name = "SpellPreview_RT";
            _rt.Create();
            _camera.targetTexture = _rt;

            _initialized = true;
        }

        public RenderTexture GetPreviewTexture() => _initialized ? _rt : null;

        public void SetSelectedSpell(SpellDefinition spell)
        {
            if (!_initialized) return;
            if (_spell == spell) return;
            _spell = spell;
            // Force a fresh cycle on the next Tick so the new spell appears immediately.
            ClearStage();
            _loopState = LoopState.Idle;
            _loopTimer = 0f;
        }

        public void SetDirection(Vector2 dir)
        {
            if (dir.sqrMagnitude < 0.0001f) return;
            _direction = dir.normalized;
        }

        /// <summary>Multiply the current user zoom (clamped). 1.0 = auto-fit baseline.</summary>
        public void ZoomIn()  => SetZoom(_userZoom * USER_ZOOM_STEP);
        public void ZoomOut() => SetZoom(_userZoom / USER_ZOOM_STEP);

        /// <summary>
        /// Apply a continuous zoom delta (e.g. mouse-wheel ticks). Each unit of
        /// <paramref name="delta"/> applies one full step; fractional deltas are
        /// applied via Pow so wheel sensitivity stays smooth.
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

        public void Open()
        {
            if (!_initialized) return;
            _open = true;
            if (_camera != null) _camera.enabled = true;
            // Start a cycle immediately when opened.
            _loopState = LoopState.Idle;
            _loopTimer = 0f;
        }

        public void Close()
        {
            if (!_initialized) return;
            _open = false;
            if (_camera != null) _camera.enabled = false;
            ClearStage();
            _loopState = LoopState.Idle;
            _loopTimer = 0f;
        }

        /// <summary>
        /// Drive the preview loop. Call from MonoBehaviour.Update while the panel is open.
        /// </summary>
        public void Tick()
        {
            if (!_initialized || !_open) return;

            float dt = Time.deltaTime;

            switch (_loopState)
            {
                case LoopState.Idle:
                    // Fire on the next frame after entering Idle. This gives any prior
                    // spawns a frame to be destroyed before re-firing.
                    if (_spell != null)
                    {
                        FireOnce();
                        _loopTimer = ComputeCycleTime(_spell);
                        _loopState = LoopState.Active;
                    }
                    break;

                case LoopState.Active:
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
        }

        public void Shutdown()
        {
            if (!_initialized) return;
            ClearStage();
            if (_rt != null) { _rt.Release(); SafeDestroy.Of(_rt); _rt = null; }
            if (_camera != null) { SafeDestroy.Of(_camera.gameObject); _camera = null; }
            if (_stageRoot != null) { SafeDestroy.Of(_stageRoot); _stageRoot = null; }
            _casterGo        = null;
            _casterTransform = null;
            _spell           = null;
            _open            = false;
            _initialized     = false;
        }

        // ── Internal — firing & cleanup ──────────────────────────────────────────

        private void FireOnce()
        {
            if (_spell == null || _casterTransform == null) return;
            int layer = ResolvePreviewLayer();

            var executor = SpellCaster.GetExecutor(_spell.type);
            if (executor == null)
            {
                Debug.LogWarning($"[SpellPreview] No executor registered for SpellType {_spell.type}");
                return;
            }

            var ctx = new SpellContext
            {
                Spell            = _spell,
                Caster           = _casterTransform,
                Direction        = _direction,
                TargetLayers     = 0,                          // hit nothing — empty stage
                ProjectilePrefab = ResolveProjectilePrefab(),
            };

            // Snapshot world projectiles before/after firing so projectile-style
            // executors that spawn at world root (not under ctx.Caster) are tracked
            // for cleanup.
            var beforeProjectiles = SnapshotWorldProjectiles();

            try { executor.Execute(ctx); }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[SpellPreview] Executor '{executor.GetType().Name}' threw for spell '{_spell.spellKey}': {ex.Message}");
            }

            // Force the SpellPreview Unity layer onto every newly spawned descendant
            // of the caster so the dedicated camera can see them.
            SetLayerRecursive(_stageRoot, layer);

            // Track new world-rooted projectiles for cycle cleanup.
            var afterProjectiles = SnapshotWorldProjectiles();
            foreach (var p in afterProjectiles)
            {
                if (p == null) continue;
                if (!beforeProjectiles.Contains(p))
                {
                    SetLayerRecursive(p.gameObject, layer);
                    _trackedWorldSpawns.Add(p.gameObject);
                }
            }
        }

        private void ClearStage()
        {
            // Children of the synthetic caster (Aura/Puddle/Wall/Totem/Shield parent here).
            if (_casterTransform != null)
            {
                for (int i = _casterTransform.childCount - 1; i >= 0; i--)
                {
                    var c = _casterTransform.GetChild(i);
                    if (c != null) SafeDestroy.Of(c.gameObject);
                }
            }

            // World-rooted spawns (projectiles, etc.) tracked from FireOnce.
            for (int i = 0; i < _trackedWorldSpawns.Count; i++)
            {
                var go = _trackedWorldSpawns[i];
                if (go != null) SafeDestroy.Of(go);
            }
            _trackedWorldSpawns.Clear();
        }

        private static HashSet<Projectile> SnapshotWorldProjectiles()
        {
            var set = new HashSet<Projectile>();
            foreach (var p in Object.FindObjectsOfType<Projectile>())
                if (p != null) set.Add(p);
            return set;
        }

        // ── Internal — projectile prefab resolution ──────────────────────────────

        private GameObject ResolveProjectilePrefab()
        {
            if (_projectilePrefabResolved) return _projectilePrefab;
            _projectilePrefabResolved = true;

            // Pick the first SpellCaster instance found in the loaded scene; its
            // serialized ProjectilePrefab is the canonical fireball/iceball/... shell.
            var caster = Object.FindObjectOfType<SpellCaster>();
            _projectilePrefab = caster != null ? caster.ProjectilePrefab : null;
            return _projectilePrefab;
        }

        // ── Internal — camera framing ────────────────────────────────────────────

        private void UpdateCameraFraming()
        {
            if (_camera == null || _casterTransform == null) return;

            // Auto-fit ortho size to the bounds of every renderer under the stage.
            bool hasBounds = false;
            var combined = new Bounds(_casterTransform.position, Vector3.zero);

            foreach (var sr in _stageRoot.GetComponentsInChildren<Renderer>())
            {
                if (sr == null) continue;
                var b = sr.bounds;
                if (b.size.sqrMagnitude < 0.0001f) continue;
                if (!hasBounds) { combined = b; hasBounds = true; }
                else combined.Encapsulate(b);
            }

            // Include tracked world spawns (their renderers are not children of _stageRoot).
            for (int i = 0; i < _trackedWorldSpawns.Count; i++)
            {
                var go = _trackedWorldSpawns[i];
                if (go == null) continue;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (r == null) continue;
                    var b = r.bounds;
                    if (b.size.sqrMagnitude < 0.0001f) continue;
                    if (!hasBounds) { combined = b; hasBounds = true; }
                    else combined.Encapsulate(b);
                }
            }

            float orthoSize;
            Vector3 focalPoint;
            if (hasBounds)
            {
                float halfX = combined.extents.x + BOUNDS_PADDING;
                float halfY = combined.extents.y + BOUNDS_PADDING;
                orthoSize  = Mathf.Clamp(Mathf.Max(halfX, halfY), ORTHO_SIZE_MIN, ORTHO_SIZE_MAX);
                focalPoint = combined.center;
            }
            else
            {
                orthoSize  = ORTHO_SIZE_DEFAULT;
                focalPoint = _casterTransform.position;
            }

            // Apply user zoom on top of auto-fit. Higher zoom = smaller ortho size.
            // Re-clamp to keep extreme user values from breaking the camera.
            float zoomed = orthoSize / Mathf.Max(_userZoom, 0.0001f);
            _camera.orthographicSize   = Mathf.Clamp(zoomed, ORTHO_SIZE_MIN * 0.25f, ORTHO_SIZE_MAX * 4f);
            _camera.transform.position = new Vector3(focalPoint.x, focalPoint.y, CAMERA_Z);
        }

        private static float ComputeCycleTime(SpellDefinition s)
        {
            bool persistent = s.type == SpellType.Aura
                           || s.type == SpellType.Puddle
                           || s.type == SpellType.VortexField
                           || s.type == SpellType.Wall
                           || s.type == SpellType.Totem
                           || s.type == SpellType.SphereMagicShield;

            float t = persistent
                ? Mathf.Max(s.duration, MIN_PERSISTENT_SECONDS)
                : Mathf.Max(s.prepareDuration + s.channelDuration + s.lifetime, MIN_CYCLE_SECONDS);
            return t + CYCLE_TAIL_SECONDS;
        }

        // ── Internal — utils ─────────────────────────────────────────────────────

        private static int ResolvePreviewLayer()
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

        private static void SetLayerRecursive(GameObject root, int layer)
        {
            if (root == null) return;
            root.layer = layer;
            for (int i = 0; i < root.transform.childCount; i++)
                SetLayerRecursive(root.transform.GetChild(i).gameObject, layer);
        }
    }
}
